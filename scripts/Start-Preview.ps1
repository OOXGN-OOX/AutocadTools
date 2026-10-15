param(
    [ValidateSet('2025','2026','2027')][string]$AutoCADVersion = '2026',
    [string]$DotnetPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'File-Hash.ps1')
. (Join-Path $PSScriptRoot 'Release-State.ps1')
$taskFingerprint = Get-ReleaseFingerprint $taskRoot
$taskPreviewRoot = Join-Path $env:LOCALAPPDATA "AutocadTools\Preview\$AutoCADVersion"
$taskManifest = Join-Path $taskPreviewRoot 'current.json'
if (Test-Path -LiteralPath $taskManifest) {
    $taskCurrent = Get-Content -LiteralPath $taskManifest -Raw | ConvertFrom-Json
    if ($taskCurrent.Fingerprint -eq $taskFingerprint -and (Test-Path -LiteralPath $taskCurrent.AssemblyPath) -and
        (Get-FileDigest $taskCurrent.AssemblyPath) -eq $taskCurrent.Hash) {
        Write-Host "Preview already ready: $($taskCurrent.Build). No compilation needed."
        return
    }
}
if (!$DotnetPath) { $DotnetPath = (& (Join-Path $PSScriptRoot 'Get-BuildTools.ps1') -SkipCompiler).DotnetPath }
$taskBuild = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$taskAssembly = 'AutocadTools.Preview.' + $taskBuild.Replace('-', '')
$taskOutput = Join-Path $taskPreviewRoot $taskBuild
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& $DotnetPath build (Join-Path $taskRoot 'AutocadTools.csproj') -c Debug "-p:AutoCADVersion=$AutoCADVersion" "-p:PreviewAssemblyName=$taskAssembly" "-p:BaseIntermediateOutputPath=obj\Preview\$AutoCADVersion\" -o $taskOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Preview build failed; the previous preview remains active.' }
$taskLoaderProject = Join-Path $taskRoot 'tools\LivePreview\LivePreview.csproj'
& $DotnetPath build $taskLoaderProject -c Debug "-p:AutoCADVersion=$AutoCADVersion" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Preview loader compilation failed; the previous preview remains active.' }
$taskFramework = if ($AutoCADVersion -eq '2027') { 'net10.0-windows' } else { 'net8.0-windows' }
$taskLoaderSource = Join-Path $taskRoot "tools\LivePreview\bin\Debug\$taskFramework\AutocadTools.LivePreview.dll"
$taskLoaderRoot = Join-Path $taskPreviewRoot 'Loader'
New-Item -ItemType Directory -Path $taskLoaderRoot -Force | Out-Null
$taskLoader = Join-Path $taskLoaderRoot 'AutocadTools.LivePreview.dll'
# A loaded bootstrap must not be overwritten. Changes to the bootstrap itself require a new AutoCAD process.
if (!(Test-Path -LiteralPath $taskLoader)) { Copy-Item -LiteralPath $taskLoaderSource -Destination $taskLoader }
elseif ((Get-FileDigest $taskLoader) -ne (Get-FileDigest $taskLoaderSource)) {
    Write-Warning 'The bootstrap changed. Start a new AutoCAD session before loading the new bootstrap.'
}
if ((Get-ReleaseFingerprint $taskRoot) -ne $taskFingerprint) { throw 'Sources changed during compilation. The previous preview remains active; run Preview again.' }
$taskTempManifest = Join-Path $taskPreviewRoot ('current-' + [Guid]::NewGuid().ToString('N') + '.tmp')
[xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskRoot 'AutocadTools.csproj') -Raw
$taskVersion = @($taskProject.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$taskAssemblyPath = Join-Path $taskOutput "$taskAssembly.dll"
@{ AssemblyPath = $taskAssemblyPath; Build = "$taskVersion / $taskBuild"; Fingerprint = $taskFingerprint; Hash = (Get-FileDigest $taskAssemblyPath) } | ConvertTo-Json | Set-Content -LiteralPath $taskTempManifest -Encoding UTF8
if (Test-Path -LiteralPath $taskManifest) {
    $taskBackup = $taskManifest + '.previous'
    [IO.File]::Replace($taskTempManifest, $taskManifest, $taskBackup)
    Remove-Item -LiteralPath $taskBackup -Force
}
else { [IO.File]::Move($taskTempManifest, $taskManifest) }
Write-Host "Preview ready: $taskVersion / $taskBuild"
Write-Host "First load in AutoCAD: NETLOAD $taskLoader"
Write-Host 'Use the Preview ribbon tab. Later builds are selected automatically on the next test command.'
