param(
    [string]$AutoCAD2025Path,
    [string]$AutoCAD2027Path = 'C:\Program Files\Autodesk\AutoCAD 2027',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $projectRoot 'AutocadTools.csproj'
$specs = @(
    @{ Year = 2025; Framework = 'net8.0-windows'; Path = $AutoCAD2025Path; Folder = '2025-2026' },
    @{ Year = 2027; Framework = 'net10.0-windows'; Path = $AutoCAD2027Path; Folder = '2027' }
)
foreach ($spec in $specs) {
    if (!$spec.Path -and $spec.Year -eq 2025) { continue }
    if ($spec.Year -eq 2027 -and $spec.Path -eq 'C:\Program Files\Autodesk\AutoCAD 2027' -and !(Test-Path -LiteralPath (Join-Path $spec.Path 'acdbmgd.dll'))) { continue }
    foreach ($name in @('accoremgd.dll','acdbmgd.dll','acmgd.dll','AdWindows.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $spec.Path $name))) { throw "Missing AutoCAD $($spec.Year) reference: $name in $($spec.Path)" }
    }
}
$stageRoot = Join-Path $projectRoot ('artifacts\staging\' + [Guid]::NewGuid().ToString('N'))
$stageBundle = Join-Path $stageRoot 'AutocadTools.bundle'
New-Item -ItemType Directory -Path $stageBundle -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Bundle\AutocadTools.bundle\PackageContents.xml') -Destination $stageBundle
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stageBundle
foreach ($spec in $specs) {
    $destination = Join-Path $stageBundle ('Contents\Windows\' + $spec.Folder)
    $arguments = @('build', $projectFile, '--configuration', $Configuration, "-p:AutoCADVersion=$($spec.Year)", "-p:OutputPath=$destination\", '-p:AppendTargetFrameworkToOutputPath=false')
    if ($spec.Path) { $arguments += "-p:AutoCADPath=$($spec.Path)" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed for AutoCAD $($spec.Year). No installation performed." }
    if (!(Test-Path -LiteralPath (Join-Path $destination 'AutocadTools.dll'))) { throw 'Plugin DLL missing after build.' }
    foreach ($name in @('accoremgd.dll','acdbmgd.dll','acmgd.dll','AdWindows.dll')) {
        if (Test-Path -LiteralPath (Join-Path $destination $name)) { throw "Autodesk host assembly must not be distributed: $name" }
    }
}
Write-Output "Bundle prepared: $stageBundle"
