[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string]$Version,
    [Parameter(Mandatory=$true)][string]$NotesPath,
    [switch]$Publish,
    [switch]$BuildLocal,
    [ValidateSet('2026','2027')][string]$AutoCADVersion = '2026'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskProject = Join-Path $taskRoot 'AutocadTools.csproj'
$taskSource = [IO.File]::ReadAllText($taskProject)
$taskOldVersion = ([xml]$taskSource).Project.PropertyGroup.Version | Select-Object -First 1
if ([version]$Version -lt [version]$taskOldVersion) { throw "Version must not be older than $taskOldVersion." }
$taskNotes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesPath).Path)
if ([string]::IsNullOrWhiteSpace($taskNotes)) { throw 'Release notes are empty.' }
$taskTag = "v$Version"
function Invoke-CycleGit([string[]]$Arguments) {
    $taskOutput = & git -C $taskRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" }
    return $taskOutput
}
$taskExisting = Invoke-CycleGit @('tag', '--list', $taskTag)
if ($taskExisting) { throw "Tag already exists: $taskTag" }
if ($Publish) {
    $taskBranch = Invoke-CycleGit @('branch', '--show-current')
    if (!$taskBranch) { throw 'Cannot publish from a detached HEAD.' }
    $taskRemoteTag = Invoke-CycleGit @('ls-remote', '--tags', 'origin', "refs/tags/$taskTag")
    if ($taskRemoteTag) { throw "Remote tag already exists: $taskTag" }
}
$taskAction = if ($Publish) { 'Test, prepare and push the version; release starts after merging into main' } else { 'Test and prepare version metadata locally' }
if (!$PSCmdlet.ShouldProcess($taskRoot, "Finish $Version. $taskAction")) { return }

# Test code first; an interrupted test run must not advance the release version.
$taskHasAutoCAD = Test-Path -LiteralPath "C:\Program Files\Autodesk\AutoCAD $AutoCADVersion\accoreconsole.exe"
& (Join-Path $PSScriptRoot 'Dev.ps1') -Mode Test -Suite All -AutoCAD:$taskHasAutoCAD -AutoCADVersion $AutoCADVersion | Out-Host
$taskUpdated = [regex]::Replace($taskSource, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($taskProject, $taskUpdated, [Text.UTF8Encoding]::new($false))
$taskReleaseNotes = Join-Path $taskRoot 'docs\releases'
New-Item -ItemType Directory -Path $taskReleaseNotes -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $taskReleaseNotes "$taskTag.md"), $taskNotes, [Text.UTF8Encoding]::new($false))
if ($BuildLocal) {
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') | Out-Host
}
if (!$Publish) {
    Write-Output "Version $Version prepared. Merge the version and its release notes into main to start the release workflow."
    return
}
Invoke-CycleGit @('diff', '--check') | Out-Host
Invoke-CycleGit @('add', '--all') | Out-Host
& git -C $taskRoot diff --cached --quiet
if ($LASTEXITCODE -eq 1) { Invoke-CycleGit @('commit', '-m', "Prepare $taskTag") | Out-Host }
elseif ($LASTEXITCODE -ne 0) { throw 'Cannot inspect prepared changes.' }
Invoke-CycleGit @('push', 'origin', "HEAD:refs/heads/$taskBranch") | Out-Host
Write-Output "Version $Version sent to $taskBranch. Merge into main to build and publish the release."
