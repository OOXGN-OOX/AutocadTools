param([Parameter(Mandatory=$true)][string]$BundlePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $BundlePath 'ThirdPartyNotices'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$seen = @{}
foreach ($year in @(2025,2027)) {
    $assetsPath = Join-Path $root "obj\AutoCAD$year\project.assets.json"
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    foreach ($entry in $assets.libraries.PSObject.Properties) {
        if ($entry.Value.type -ne 'package' -or $entry.Name -like 'AutoCAD.NET*' -or $seen.ContainsKey($entry.Name)) { continue }
        $seen[$entry.Name] = $true
        $packagePath = Join-Path ($assets.packageFolders.PSObject.Properties | Select-Object -First 1 -ExpandProperty Name) $entry.Value.path
        $nuspec = Get-ChildItem -LiteralPath $packagePath -Filter *.nuspec | Select-Object -First 1
        if (!$nuspec) { throw "Missing package metadata: $($entry.Name)" }
        $metadata = ([xml](Get-Content -LiteralPath $nuspec.FullName -Raw)).package.metadata
        # Short directory names keep installer source paths below the Windows MAX_PATH limit.
        $packageDestination = Join-Path $destination ('p' + $seen.Count.ToString('D3'))
        New-Item -ItemType Directory -Path $packageDestination -Force | Out-Null
        @("Package: $($entry.Name)","Authors: $($metadata.authors)","Copyright: $($metadata.copyright)","License: $($metadata.license.InnerText)","License URL: $($metadata.licenseUrl)","Project URL: $($metadata.projectUrl)") | Set-Content -LiteralPath (Join-Path $packageDestination 'NOTICE.txt') -Encoding utf8
        $noticeFiles = Get-ChildItem -LiteralPath $packagePath -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD.?PARTY)' }
        $fileIndex = 0
        foreach ($file in $noticeFiles) {
            $fileIndex++
            $target = Join-Path $packageDestination ($fileIndex.ToString('D2') + '_' + $file.Name)
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $BundlePath 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $destination -Force
Write-Output "Notices collected: $($seen.Count) packages"
