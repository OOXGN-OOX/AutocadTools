param(
    [string]$CompilerPath,
    [switch]$SkipDotnet,
    [switch]$SkipCompiler
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'File-Hash.ps1')
$root = Split-Path $PSScriptRoot -Parent
$toolsRoot = Join-Path $root 'artifacts\tools'
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
# Windows PowerShell 5.1 needs TLS 1.2 for the official download endpoints.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Get-DotnetWithSdk($candidate) {
    if (!$candidate -or !(Test-Path -LiteralPath $candidate -PathType Leaf)) { return }
    try {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and @($sdks | Where-Object { $_ -match '^10\.0\.\d+ ' }).Count) {
            $activeVersion = & $candidate --version 2>$null
            if ($LASTEXITCODE -eq 0 -and $activeVersion -match '^10\.0\.\d+$') { return $candidate }
        }
    } catch { return } # An interrupted SDK extraction will be repaired from the verified archive.
}

$dotnetPath = $null
if (!$SkipDotnet) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($dotnetCommand) { $dotnetPath = Get-DotnetWithSdk $dotnetCommand.Source }
    $localDotnet = Join-Path $toolsRoot 'dotnet\dotnet.exe'
    if (!$dotnetPath) { $dotnetPath = Get-DotnetWithSdk $localDotnet }
    if (!$dotnetPath) {
        Write-Host 'Preparing local .NET 10 SDK (first build only)...'
        $sdkArchive = Join-Path $toolsRoot 'dotnet-sdk-10.0.402-win-x64.zip'
        # SHA512 from Microsoft's official .NET 10 release metadata.
        $sdkHash = 'ae4fd528cb7e9f6af6aaf7de36715a69f761f183744f1bb3025d19f5a92447090ec561410e46266c1d10a63781ed6f3de771760bc76c876d87daabc367b898cd'
        if (!(Test-Path -LiteralPath $sdkArchive -PathType Leaf) -or (Get-FileDigest -Path $sdkArchive -Algorithm SHA512) -ne $sdkHash) {
            Invoke-WebRequest -UseBasicParsing -Uri 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-win-x64.zip' -OutFile $sdkArchive
        }
        if ((Get-FileDigest -Path $sdkArchive -Algorithm SHA512) -ne $sdkHash) { throw '.NET SDK download failed SHA512 verification.' }
        Write-Host 'Extracting .NET SDK...'
        $sdkDirectory = Split-Path $localDotnet -Parent
        New-Item -ItemType Directory -Path $sdkDirectory -Force | Out-Null
        $tarCommand = Get-Command tar.exe -ErrorAction SilentlyContinue
        if ($tarCommand) {
            # Ignore ZIP timestamps that Windows tar cannot restore on some drives.
            & $tarCommand.Source -xmf $sdkArchive -C $sdkDirectory
            if ($LASTEXITCODE -ne 0) { throw '.NET SDK extraction failed.' }
        } else {
            Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkDirectory -Force
        }
        $dotnetPath = Get-DotnetWithSdk $localDotnet
        if (!$dotnetPath) { throw 'Failed to prepare .NET 10 SDK. Check the download output above.' }
    }
    Write-Host "Using .NET: $dotnetPath"
}

if ($SkipCompiler) {
    return [PSCustomObject]@{ DotnetPath = $dotnetPath; CompilerPath = $null }
}
if ($CompilerPath) {
    $CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
    if (!(Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Inno Setup compiler not found.' }
} else {
    $localCompiler = Join-Path $toolsRoot 'InnoSetup-6.7.3\ISCC.exe'
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @($localCompiler)
    if ($compilerCommand) { $candidates += $compilerCommand.Source }
    foreach ($programFiles in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($programFiles) {
            $candidates += Join-Path $programFiles 'Inno Setup 6\ISCC.exe'
            $candidates += Join-Path $programFiles 'Inno Setup 7\ISCC.exe'
        }
    }
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$CompilerPath) {
        Write-Host 'Preparing portable Inno Setup 6.7.3 (first build only)...'
        $download = Join-Path $toolsRoot 'innosetup-6.7.3.exe'
        $expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
        if (!(Test-Path -LiteralPath $download -PathType Leaf) -or (Get-FileDigest -Path $download) -ne $expectedHash) {
            Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download
        }
        if ((Get-FileDigest -Path $download) -ne $expectedHash) { throw 'Inno Setup download failed SHA256 verification.' }
        $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/PORTABLE=1', '/NOICONS', ('/DIR="' + (Split-Path $localCompiler -Parent) + '"'))
        $process = Start-Process -FilePath $download -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $localCompiler -PathType Leaf)) { throw "Failed to prepare Inno Setup. Exit code: $($process.ExitCode)" }
        $CompilerPath = $localCompiler
    }
}
Write-Host "Using Inno Setup: $CompilerPath"
[PSCustomObject]@{ DotnetPath = $dotnetPath; CompilerPath = $CompilerPath }
