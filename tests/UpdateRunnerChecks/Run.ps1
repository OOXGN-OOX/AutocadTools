$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $taskRoot 'Assets\UpdateRunner.ps1')
$taskCache = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AutocadTools\Updates'
$taskScratch = Join-Path $taskCache ('runner-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
function Assert-Runner([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function New-TestUpdate {
    $directory = Join-Path $taskScratch ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory | Out-Null
    $installer = Join-Path $directory 'AutocadTools-1.0.0-Setup.exe'
    [IO.File]::WriteAllText($installer, 'test payload; never executed')
    $request = @{ InstallerPath = $installer; Sha256 = Get-UpdateFileHash $installer;
        Tag = 'v1.0.0'; AllUsers = $false; TimeoutSeconds = 5;
        InstallDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\ApplicationPlugins\AutocadTools.bundle' }
    $path = Join-Path $directory 'request.json'
    $request | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}
$script:polls = 0; $script:waits = 0; $script:installs = 0; $script:alwaysRunning = $false
function Get-Process {
    param([string[]]$Name)
    $script:polls++
    if ($script:alwaysRunning -or $script:polls -le 2) { return [PSCustomObject]@{ Name = 'acad' } }
}
function Start-Sleep { param([int]$Seconds) $script:waits++; [Threading.Thread]::Sleep(20) }
function Start-UpdateInstaller($Request) { Assert-Runner ($script:polls -gt 2) 'Installer started while AutoCAD was running.'; $script:installs++ }
function Get-InstalledUpdateVersion($Request) { return [version]'0.1.0.0' }
try {
    $path = New-TestUpdate
    Invoke-PendingUpdate $path
    $state = Get-Content -LiteralPath (Join-Path (Split-Path $path -Parent) 'status.json') -Raw | ConvertFrom-Json
    Assert-Runner ($script:installs -eq 1 -and $script:waits -eq 2 -and $state.State -eq 'Installed') 'Wait/install workflow failed.'
    Assert-Runner (!(Test-Path -LiteralPath $path)) 'Completed request was not cleaned up.'

    $path = New-TestUpdate
    $request = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    [IO.File]::WriteAllText($request.InstallerPath, 'corrupt')
    $rejected = $false
    try { Read-UpdateRequest $path | Out-Null } catch { $rejected = $true }
    Assert-Runner $rejected 'Corrupted installer accepted.'

    $path = New-TestUpdate
    $request = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $request.InstallDirectory = Join-Path $taskScratch 'wrong-installation'
    $request | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
    $rejected = $false
    try { Read-UpdateRequest $path | Out-Null } catch { $rejected = $true }
    Assert-Runner $rejected 'Different installation target accepted.'

    $path = New-TestUpdate
    $script:polls = 0
    [IO.File]::WriteAllText((Join-Path (Split-Path $path -Parent) 'cancel'), '')
    $cancelled = $false
    try { Invoke-PendingUpdate $path } catch [OperationCanceledException] { $cancelled = $true }
    Assert-Runner ($cancelled -and $script:installs -eq 1) 'Cancelled update was installed.'

    $script:alwaysRunning = $true
    $timedOut = $false
    try { Wait-AutoCADClosed 1 '' } catch { $timedOut = $true }
    Assert-Runner $timedOut 'Running AutoCAD did not prevent installation.'
    $script:alwaysRunning = $false

    $path = New-TestUpdate
    $script:polls = 3
    function Get-InstalledUpdateVersion($Request) { return [version]'2.0.0.0' }
    Invoke-PendingUpdate $path
    Assert-Runner ($script:installs -eq 1) 'Older queued update downgraded a newer installation.'
    Write-Output 'PASS: updater wait, checksum, target, cancellation, timeout and downgrade prevention.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($taskScratch)
    $prefix = [IO.Path]::GetFullPath($taskCache).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test path escaped the update cache.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
