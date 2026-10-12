param([Parameter(Mandatory=$true)][string]$InstallerPath, [string]$PreviousInstallerPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$existing = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D3A3F30D-A5F7-4DF9-A6D4-CC17D24B83DB}_is1' -ErrorAction SilentlyContinue
if ($existing.InstallLocation -and !$existing.InstallLocation.StartsWith((Join-Path $root 'artifacts\installer-test\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'A real per-user installation exists; isolated test will not overwrite its registration.' }
$testRoot = Join-Path $root ('artifacts\installer-test\' + [Guid]::NewGuid().ToString('N'))
$installPath = Join-Path $testRoot 'AutocadTools.bundle'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$sentinel = Join-Path $testRoot 'settings-outside-bundle.json'
'{"preserve":true}' | Set-Content -LiteralPath $sentinel -Encoding utf8
$sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$userData = Join-Path $env:LOCALAPPDATA 'AutocadTools'
function Get-SettingsSnapshot {
    if (!(Test-Path -LiteralPath $userData)) { return '' }
    return ((Get-ChildItem -LiteralPath $userData -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join "`n")
}
$settingsBefore = Get-SettingsSnapshot
function Run-TestInstall($logName, $executable = $InstallerPath) {
    $logPath = Join-Path $testRoot $logName
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER',('/DIR="'+$installPath+'"'),('/LOG="'+$logPath+'"'))
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode); $logPath" }
    foreach ($folder in @('2025-2026','2027')) {
        if (!(Test-Path -LiteralPath (Join-Path $installPath "Contents\Windows\$folder\AutocadTools.dll"))) { throw "Missing installed DLL: $folder" }
        if ($executable -eq $InstallerPath -and !(Test-Path -LiteralPath (Join-Path $installPath "Contents\Windows\$folder\Assets\WM_FORMAT.dwg"))) { throw "Missing installed format template: $folder" }
    }
    if (!(Test-Path -LiteralPath (Join-Path $installPath 'PackageContents.xml'))) { throw 'Manifest missing.' }
}
if ($PreviousInstallerPath) { Run-TestInstall 'previous-install.log' $PreviousInstallerPath }
else { Run-TestInstall 'install.log' }
# A controlled obsolete library verifies cleanup during upgrade.
$obsolete = Join-Path $installPath 'Contents\Windows\2027\obsolete-test.dll'
'test-only obsolete dependency' | Set-Content -LiteralPath $obsolete
Run-TestInstall 'upgrade.log'
if ($PreviousInstallerPath) {
    $log = Get-Content -LiteralPath (Join-Path $testRoot 'upgrade.log') -Raw
    if ($log -notmatch 'Autocad Tools: uninstall previous version') { throw 'Previous version uninstaller was not invoked.' }
}
if (Test-Path -LiteralPath $obsolete) { throw 'Obsolete DLL was not removed during update.' }
$uninstaller = Get-ChildItem -LiteralPath $installPath -Filter 'unins*.exe' | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
if (!$uninstaller) { throw 'New version uninstaller was not created.' }
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $testRoot 'uninstall.log')+'"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Uninstall failed: $($process.ExitCode)" }
if (Test-Path -LiteralPath (Join-Path $installPath 'Contents\Windows\2027\AutocadTools.dll')) { throw 'Plugin survived uninstall.' }
if ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ne $sentinelHash) { throw 'External settings sentinel was modified.' }
if ((Get-SettingsSnapshot) -ne $settingsBefore) { throw 'Existing user settings were modified.' }
Write-Output "PASS: install, upgrade, obsolete dependency cleanup, uninstall, settings preservation. Logs: $testRoot"
