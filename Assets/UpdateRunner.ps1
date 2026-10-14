# SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
# Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
param([string]$RequestPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop

function Get-UpdateFileHash([string]$Path) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $hasher.Dispose() }
}

function Read-UpdateRequest([string]$Path) {
    $cacheRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AutocadTools\Updates')).TrimEnd('\') + '\'
    $requestFile = [IO.Path]::GetFullPath($Path)
    if (!$requestFile.StartsWith($cacheRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($requestFile) -ne 'request.json') { throw 'Invalid update request location.' }
    $request = [IO.File]::ReadAllText($requestFile) | ConvertFrom-Json
    if ($request.Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $request.Tag -notmatch '^v(\d+\.\d+\.\d+)(?:[-+][A-Za-z0-9.+-]+)?$') { throw 'Invalid update metadata.' }
    $number = $Matches[1]
    $directory = Split-Path $requestFile -Parent
    $installer = [IO.Path]::GetFullPath([string]$request.InstallerPath)
    $expectedInstaller = Join-Path $directory "AutocadTools-$number-Setup.exe"
    if (![string]::Equals($installer, $expectedInstaller, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid installer path.' }
    $root = if ($request.AllUsers -eq $true) { [Environment]::GetFolderPath('ProgramFiles') } else { [Environment]::GetFolderPath('ApplicationData') }
    $expectedDirectory = Join-Path $root 'Autodesk\ApplicationPlugins\AutocadTools.bundle'
    if (![string]::Equals([IO.Path]::GetFullPath([string]$request.InstallDirectory).TrimEnd('\'), $expectedDirectory, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid installation directory.' }
    if ([int]$request.TimeoutSeconds -lt 1 -or [int]$request.TimeoutSeconds -gt 86400) { throw 'Invalid update timeout.' }
    if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Installer is missing.' }
    if ((Get-UpdateFileHash $installer) -ne $request.Sha256) { throw 'Installer SHA256 verification failed.' }
    $request | Add-Member -NotePropertyName RequestDirectory -NotePropertyValue $directory
    return $request
}

function Write-UpdateStatus([string]$Directory, [string]$State, [string]$Message) {
    $content = @{ State = $State; Message = $Message; UpdatedUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $Directory 'status.json'), $content, [Text.UTF8Encoding]::new($false))
}

function Wait-AutoCADClosed([int]$TimeoutSeconds, [string]$CancelPath) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (@(Get-Process -Name acad,accoreconsole -ErrorAction SilentlyContinue).Count) {
        if ($CancelPath -and (Test-Path -LiteralPath $CancelPath)) { throw [OperationCanceledException]::new('Update cancelled.') }
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'AutoCAD did not close before the update timeout.' }
        Start-Sleep -Seconds 2
    }
    if ($CancelPath -and (Test-Path -LiteralPath $CancelPath)) { throw [OperationCanceledException]::new('Update cancelled.') }
}

function Start-UpdateInstaller($Request) {
    $scope = if ($Request.AllUsers -eq $true) { '/ALLUSERS' } else { '/CURRENTUSER' }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Request.InstallerPath
    $start.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART $scope /LOG=`"$(Join-Path $Request.RequestDirectory 'installer.log')`""
    $start.UseShellExecute = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    if ($Request.AllUsers -eq $true) { $start.Verb = 'runas' }
    $process = [Diagnostics.Process]::Start($start)
    if (!$process) { throw 'Installer could not be started.' }
    try { $process.WaitForExit(); if ($process.ExitCode -ne 0) { throw "Installer failed with exit code $($process.ExitCode)." } }
    finally { $process.Dispose() }
}

function Get-InstalledUpdateVersion($Request) {
    $versions = foreach ($profile in @('2025-2026','2027')) {
        $assembly = Join-Path $Request.InstallDirectory "Contents\Windows\$profile\AutocadTools.dll"
        if (Test-Path -LiteralPath $assembly) {
            $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).FileVersion
            if ($version) { [version]$version }
        }
    }
    if (!$versions) { throw 'Installation was removed; automatic update cancelled.' }
    return $versions | Sort-Object -Descending | Select-Object -First 1
}

function Invoke-PendingUpdate([string]$Path) {
    $directory = Split-Path ([IO.Path]::GetFullPath($Path)) -Parent
    $mutex = $null; $locked = $false; $validated = $false
    try {
        $request = Read-UpdateRequest $Path
        $validated = $true
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $key = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($request.InstallDirectory.ToUpperInvariant()))).Replace('-', '') }
        finally { $hasher.Dispose() }
        $mutex = New-Object Threading.Mutex($false, "Global\AutocadTools.Update.$key")
        try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
        if (!$locked) { Write-UpdateStatus $directory 'Deferred' 'Another update is already waiting.'; return }
        Write-UpdateStatus $directory 'Waiting' 'Waiting for AutoCAD and Core Console to close.'
        Wait-AutoCADClosed $request.TimeoutSeconds (Join-Path $directory 'cancel')
        $request = Read-UpdateRequest $Path
        $versionText = [regex]::Match($request.Tag, '^v(\d+\.\d+\.\d+)').Groups[1].Value
        $targetVersion = [version]($versionText + '.0')
        if ((Get-InstalledUpdateVersion $request) -lt $targetVersion) {
            Write-UpdateStatus $directory 'Installing' 'Installing update.'
            Start-UpdateInstaller $request
        }
        Write-UpdateStatus $directory 'Installed' 'Update installed.'
        Remove-Item -LiteralPath $request.InstallerPath -Force
        Remove-Item -LiteralPath $Path -Force
    }
    catch {
        if ($validated) {
            $state = if ($_.Exception -is [OperationCanceledException]) { 'Cancelled' } else { 'Failed' }
            Write-UpdateStatus $directory $state $_.Exception.Message
        }
        throw
    }
    finally { if ($locked) { $mutex.ReleaseMutex() }; if ($mutex) { $mutex.Dispose() } }
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-PendingUpdate $RequestPath }
