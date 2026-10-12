param([string]$AutoCADPath = 'C:\Program Files\Autodesk\AutoCAD 2026')
$ErrorActionPreference = 'Stop'
Write-Output 'AutocadTools load diagnostics (read-only)'
foreach ($base in @("$env:ProgramFiles\Autodesk\ApplicationPlugins", "$env:APPDATA\Autodesk\ApplicationPlugins", "$env:ProgramData\Autodesk\ApplicationPlugins")) {
    foreach ($name in @('AutocadTools.bundle')) {
        $bundle = Join-Path $base $name
        if (!(Test-Path -LiteralPath $bundle)) { continue }
        Write-Output "Found: $bundle"
        $manifest = Join-Path $bundle 'PackageContents.xml'
        if (!(Test-Path -LiteralPath $manifest)) { Write-Output 'ERROR: PackageContents.xml missing'; continue }
        $xml = [xml](Get-Content -LiteralPath $manifest -Raw)
        Write-Output "Package version: $($xml.ApplicationPackage.AppVersion)"
        foreach ($component in $xml.ApplicationPackage.Components) {
            foreach ($entry in $component.ComponentEntry) {
                $dll = Join-Path $bundle $entry.ModuleName
                if (!(Test-Path -LiteralPath $dll)) { Write-Output "ERROR: Missing module $dll"; continue }
                Write-Output "Module: $dll"
                Write-Output "Assembly: $([Reflection.AssemblyName]::GetAssemblyName($dll).FullName)"
                foreach ($dependency in @('ClosedXML.dll','Newtonsoft.Json.dll','Microsoft.Extensions.Logging.Abstractions.dll')) {
                    Write-Output "$dependency exists: $(Test-Path -LiteralPath (Join-Path (Split-Path $dll -Parent) $dependency))"
                }
            }
        }
    }
}
foreach ($name in @('acad.exe','acdbmgd.dll','accoremgd.dll')) {
    $path = Join-Path $AutoCADPath $name
    if (Test-Path -LiteralPath $path) {
        Write-Output "$name version: $((Get-Item -LiteralPath $path).VersionInfo.FileVersion)"
        if ($name.EndsWith('.dll')) {
            $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($path))
            $framework = [regex]::Match($text, '\.NETCoreApp,Version=v[0-9.]+').Value
            Write-Output "Host framework metadata: $framework"
        }
    } else { Write-Output "Not found: $path" }
}
Write-Output 'If NETLOAD fails, copy its exact error text. Check APPAUTOLOAD and SECURELOAD in AutoCAD; this script does not change them.'
