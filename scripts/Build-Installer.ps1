param(
    [string]$CompilerPath,
    [string]$BundlePath,
    [string]$AutoCAD2025Path,
    [string]$AutoCAD2027Path
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_NOLOGO = 'true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Import-Module Microsoft.PowerShell.Archive -ErrorAction Stop
. (Join-Path $PSScriptRoot 'File-Hash.ps1')
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'AutocadTools.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$usePrebuiltBundle = ![string]::IsNullOrWhiteSpace($BundlePath)
$tools = & (Join-Path $PSScriptRoot 'Get-BuildTools.ps1') -CompilerPath $CompilerPath -SkipDotnet:$usePrebuiltBundle
$CompilerPath = $tools.CompilerPath
if (!$usePrebuiltBundle) {
    & (Join-Path $PSScriptRoot 'Verify-Structure.ps1') | Out-Host
    $BundlePath = & (Join-Path $PSScriptRoot 'Build-Bundle.ps1') -AutoCAD2025Path $AutoCAD2025Path -AutoCAD2027Path $AutoCAD2027Path -DotnetPath $tools.DotnetPath -PassThru
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
if (!(Test-Path -LiteralPath (Join-Path $BundlePath 'PackageContents.xml') -PathType Leaf)) { throw 'Bundle manifest missing.' }
# Keep the manifest version in sync with the assemblies and installer.
$manifestPath = Join-Path $BundlePath 'PackageContents.xml'
$manifest = [xml](Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8)
$manifest.ApplicationPackage.AppVersion = [string]$version
$manifest.Save($manifestPath)
if (!$usePrebuiltBundle) {
    & (Join-Path $PSScriptRoot 'Collect-Notices.ps1') -BundlePath $BundlePath | Out-Host
} elseif (!(Test-Path -LiteralPath (Join-Path $BundlePath 'ThirdPartyNotices') -PathType Container)) {
    throw 'Prebuilt bundle must include ThirdPartyNotices. Use Build-Installer.ps1 without -BundlePath to build a complete release.'
}
$release = Join-Path $root 'artifacts\release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
Write-Host 'Compiling EXE installer...'
& $CompilerPath /Q "/DAppVersion=$version" "/DBundleDir=$BundlePath" "/DReleaseDir=$release" (Join-Path $root 'installer\AutocadTools.iss') | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$zip = Join-Path $release "AutocadTools-$version-Bundle.zip"
Compress-Archive -LiteralPath $BundlePath -DestinationPath $zip -Force
$files = @((Join-Path $release "AutocadTools-$version-Setup.exe"), $zip)
$hashes = foreach ($file in $files) { "$(Get-FileDigest -Path $file)  $([IO.Path]::GetFileName($file))" }
$hashes | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Release prepared: $release"
Write-Output "Installer: $($files[0])"
