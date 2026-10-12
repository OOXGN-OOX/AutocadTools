param(
    [Parameter(Mandatory=$true)][string]$CompilerPath,
    [string]$BundlePath,
    [string]$AutoCAD2027Path = 'C:\Program Files\Autodesk\AutoCAD 2027'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'AutocadTools.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (!(Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup compiler not found.' }
if (!$BundlePath) {
    $buildOutput = & (Join-Path $PSScriptRoot 'Build-Bundle.ps1') -AutoCAD2027Path $AutoCAD2027Path
    $buildOutput | Write-Host
    $line = $buildOutput | Where-Object { $_ -like 'Bundle prepared: *' } | Select-Object -Last 1
    if (!$line) { throw 'Bundle build did not report a result.' }
    $BundlePath = $line.Substring('Bundle prepared: '.Length)
}
$BundlePath = (Resolve-Path -LiteralPath $BundlePath).Path
foreach ($folder in @('2025-2026','2027')) {
    $windows = Join-Path $BundlePath "Contents\Windows\$folder"
    foreach ($name in @('AutocadTools.dll','Newtonsoft.Json.dll','ClosedXML.dll','itext.kernel.dll','itext.bouncy-castle-adapter.dll','BouncyCastle.Cryptography.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $windows $name))) { throw "Incomplete bundle: $folder/$name" }
    }
    foreach ($name in @('accoremgd.dll','acdbmgd.dll','acmgd.dll','AdWindows.dll')) {
        if (Test-Path -LiteralPath (Join-Path $windows $name)) { throw "Autodesk assembly in bundle: $name" }
    }
    if (!(Test-Path -LiteralPath (Join-Path $windows 'Assets\WM_FORMAT.dwg'))) { throw "Format template missing in bundle: $folder" }
}
& (Join-Path $PSScriptRoot 'Collect-Notices.ps1') -BundlePath $BundlePath
$release = Join-Path $root 'artifacts\release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
& $CompilerPath "/DAppVersion=$version" "/DBundleDir=$BundlePath" "/DReleaseDir=$release" (Join-Path $root 'installer\AutocadTools.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$zip = Join-Path $release "AutocadTools-$version-Bundle.zip"
Compress-Archive -LiteralPath $BundlePath -DestinationPath $zip -Force
$files = @((Join-Path $release "AutocadTools-$version-Setup.exe"), $zip)
$hashes = foreach ($file in $files) { $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))" }
$hashes | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Release prepared: $release"
