[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string]$Version,
    [Parameter(Mandatory=$true)][string]$NotesPath,
    [switch]$Publish,
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
$taskAction = if ($Publish) { "Test, commit all project changes and push $taskTag; GitHub builds and publishes the release" } else { 'Test and build a local release; leave Git unchanged' }
if (!$PSCmdlet.ShouldProcess($taskRoot, "Finish $Version. $taskAction")) { return }

# Test code first; an interrupted test run must not advance the release version.
$taskHasAutoCAD = Test-Path -LiteralPath "C:\Program Files\Autodesk\AutoCAD $AutoCADVersion\accoreconsole.exe"
& (Join-Path $PSScriptRoot 'Dev.ps1') -Mode Test -Suite All -AutoCAD:$taskHasAutoCAD -AutoCADVersion $AutoCADVersion | Out-Host
$taskUpdated = [regex]::Replace($taskSource, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($taskProject, $taskUpdated, [Text.UTF8Encoding]::new($false))
$taskReleaseNotes = Join-Path $taskRoot 'docs\releases'
New-Item -ItemType Directory -Path $taskReleaseNotes -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $taskReleaseNotes "$taskTag.md"), $taskNotes, [Text.UTF8Encoding]::new($false))
if (!$Publish) {
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') | Out-Host
    Write-Output "Local version $Version prepared. Commit/push/release were not performed."
    return
}
Invoke-CycleGit @('diff', '--check') | Out-Host
Invoke-CycleGit @('add', '--all') | Out-Host
Invoke-CycleGit @('commit', '-m', "Release $taskTag") | Out-Host
Invoke-CycleGit @('tag', '-a', $taskTag, '-m', "Release $taskTag") | Out-Host
# Commit and tag arrive together, or neither does. A failed push can be retried
# with the same command printed below; no second commit or tag is needed.
Write-Host "Publishing with: git push --atomic origin HEAD refs/tags/$taskTag"
Invoke-CycleGit @('push', '--atomic', 'origin', 'HEAD', "refs/tags/$taskTag") | Out-Host
Write-Output "Cycle $Version sent. Track the Release workflow on GitHub."
