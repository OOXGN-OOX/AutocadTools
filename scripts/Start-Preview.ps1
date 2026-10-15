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
    Write-Host 'Existing bootstrap preserved. The command preview DLL dispatches the current command catalog.'
}
$taskRunnerProject = Join-Path $taskRoot 'tools\CommandPreview\CommandPreview.csproj'
$taskGenerated = [Text.StringBuilder]::new("using Autodesk.AutoCAD.Runtime;`npublic sealed partial class CommandPreview {`n")
$taskIndex = 0
foreach ($taskCommandFile in (Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Commands') -Filter '*.cs' | Sort-Object Name)) {
    $taskMethods = [regex]::Matches([IO.File]::ReadAllText($taskCommandFile.FullName), '\[CommandMethod\("([^"\r\n]+)"[^\r\n]*\]')
    if (!$taskMethods.Count) { continue }
    foreach ($taskMethod in $taskMethods) { $null = $taskGenerated.AppendLine($taskMethod.Value) }
    $null = $taskGenerated.AppendLine(('public void Run{0}() => Invoke("{1}");' -f $taskIndex, $taskMethods[0].Groups[1].Value))
    $taskIndex++
}
$null = $taskGenerated.AppendLine('}')
$taskGeneratedPath = Join-Path $taskRoot 'tools\CommandPreview\obj\CommandMethods.g.cs'
New-Item -ItemType Directory -Path (Split-Path $taskGeneratedPath -Parent) -Force | Out-Null
[IO.File]::WriteAllText($taskGeneratedPath, $taskGenerated.ToString(), [Text.UTF8Encoding]::new($false))
$taskRunnerHasher = [Security.Cryptography.SHA256]::Create()
try {
    $taskRunnerInputs = (Get-FileDigest (Join-Path $taskRoot 'tools\CommandPreview\CommandPreview.cs')) + (Get-FileDigest $taskRunnerProject) + (Get-FileDigest $taskGeneratedPath)
    $taskRunnerRevision = ([BitConverter]::ToString($taskRunnerHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskRunnerInputs)))).Replace('-', '').Substring(0, 12).ToLowerInvariant()
} finally { $taskRunnerHasher.Dispose() }
$taskRunnerOutput = Join-Path $taskPreviewRoot "CommandRunner\$taskRunnerRevision"
$taskRunner = Join-Path $taskRunnerOutput "AutocadTools.CommandPreview.$taskRunnerRevision.dll"
if (!(Test-Path -LiteralPath $taskRunner)) {
    & $DotnetPath build $taskRunnerProject -c Debug "-p:AutoCADVersion=$AutoCADVersion" "-p:RunnerRevision=$taskRunnerRevision" -o $taskRunnerOutput --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Command preview runner compilation failed; the previous preview remains active.' }
}
if ((Get-ReleaseFingerprint $taskRoot) -ne $taskFingerprint) { throw 'Sources changed during compilation. The previous preview remains active; run Preview again.' }
$taskTempManifest = Join-Path $taskPreviewRoot ('current-' + [Guid]::NewGuid().ToString('N') + '.tmp')
[xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskRoot 'AutocadTools.csproj') -Raw
$taskVersion = @($taskProject.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$taskAssemblyPath = Join-Path $taskOutput "$taskAssembly.dll"
@{ AssemblyPath = $taskAssemblyPath; Build = "$taskVersion / $taskBuild"; CommandRunner = $taskRunner; Fingerprint = $taskFingerprint; Hash = (Get-FileDigest $taskAssemblyPath) } | ConvertTo-Json | Set-Content -LiteralPath $taskTempManifest -Encoding UTF8
if (Test-Path -LiteralPath $taskManifest) {
    $taskBackup = $taskManifest + '.previous'
    [IO.File]::Replace($taskTempManifest, $taskManifest, $taskBackup)
    Remove-Item -LiteralPath $taskBackup -Force
}
else { [IO.File]::Move($taskTempManifest, $taskManifest) }
Write-Host "Preview ready: $taskVersion / $taskBuild"
Write-Host "First load in AutoCAD: NETLOAD $taskLoader"
Write-Host "Command catalog preview: NETLOAD $taskRunner"
Write-Host 'After NETLOAD use the new English/Russian commands directly. Later builds are selected automatically.'
