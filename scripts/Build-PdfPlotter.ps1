param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets/Plotters'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path (Join-Path $Destination 'PMP Files') -Force | Out-Null
function Write-PlotterFile([string]$Path, [string]$Text) {
    $payload = [Text.Encoding]::ASCII.GetBytes($Text.Replace("`r`n", "`n").Replace("`r", "`n") + [char]0)
    $compressed = [IO.MemoryStream]::new()
    $codec = [IO.Compression.ZLibStream]::new($compressed, [IO.Compression.CompressionLevel]::SmallestSize, $true)
    $codec.Write($payload); $codec.Dispose()
    $output = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($output)
    $writer.Write([Text.Encoding]::ASCII.GetBytes("PIAFILEVERSION_2.0,PC3VER1,compress`r`npmzlibcodec"))
    [uint32]$adlerA=1; [uint32]$adlerB=0
    foreach ($value in $compressed.ToArray()) { $adlerA=($adlerA+$value)%65521; $adlerB=($adlerB+$adlerA)%65521 }
    $writer.Write([uint32](($adlerB -shl 16) -bor $adlerA))
    $writer.Write([int]$payload.Length); $writer.Write([int]$compressed.Length)
    $writer.Write($compressed.ToArray()); $writer.Flush()
    [IO.File]::WriteAllBytes($Path, $output.ToArray())
    $writer.Dispose(); $compressed.Dispose()
}
$meta = @'
meta{
 user_defined_model_pathname="PMP Files\AutocadTools PDF.pmp
 user_defined_model_basename="AutocadTools PDF
 driver_pathname="pdfplot.hdi
 driver_version="1.1
 driver_tag_line="PDF ePlot - by Autodesk
 toolkit_version=1
 driver_type=3
 canonical_family_name="Autodesk ePlot
 canonical_model_name="pdf
 localized_family_name="Autodesk ePlot (PDF)
 localized_model_name="DWG To PDF
 file_only=TRUE
 model_abilities="000550055000
 show_custom_first=TRUE
 truetype_as_text=TRUE
 default_system_cfg=FALSE
 platform="2,10,0
 locale="4B00409
}
'@
$pmp = [Text.StringBuilder]::new()
[void]$pmp.AppendLine($meta)
[void]$pmp.AppendLine("udm{`n calibration{`n  _x=1.0`n  _y=1.0`n }`n media{`n  description{")
$source = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'Services/BatchPlotService.cs') -Raw
$sizes = @()
foreach ($match in [regex]::Matches($source, 'new PaperFormatDefinition\("([^"]+)", (\d+), (\d+)\)')) {
    $sizes += @{Name=$match.Groups[1].Value; W=[int]$match.Groups[2].Value; H=[int]$match.Groups[3].Value}
    $sizes += @{Name=$match.Groups[1].Value+' v'; W=[int]$match.Groups[3].Value; H=[int]$match.Groups[2].Value}
}
# A zero-margin carrier for arbitrary frames up to the PDF driver's 200-inch limit.
# Printing stays at exact scale; postprocessing cuts both physical and visible page bounds.
$sizes += @{Name='Custom maximum'; W=5080; H=5080}
for ($i=0; $i -lt $sizes.Count; $i++) {
    $w=$sizes[$i].W; $h=$sizes[$i].H
    [void]$pmp.AppendLine(@"
   $i{
    name="AutocadTools_${w}x${h}_bounds
    caps_type=2
    dimensional=TRUE
    media_bounds_urx=$w.0
    media_bounds_ury=$h.0
    printable_bounds_llx=0.0
    printable_bounds_lly=0.0
    printable_bounds_urx=$w.0
    printable_bounds_ury=$h.0
    printable_area=$($w*$h).0
   }
"@)
}
[void]$pmp.AppendLine("  }`n  size{")
for ($i=0; $i -lt $sizes.Count; $i++) {
    $w=$sizes[$i].W; $h=$sizes[$i].H; $landscape=if($w -gt $h){'TRUE'}else{'FALSE'}
    [void]$pmp.AppendLine(@"
   $i{
    name="AutocadTools_${w}x${h}_MM
    localized_name="AutocadTools $($sizes[$i].Name) ($w.00 x $h.00 MM)
    media_description_name="AutocadTools_${w}x${h}_bounds
    media_group=15
    caps_type=2
    landscape_mode=$landscape
   }
"@)
}
[void]$pmp.AppendLine("  }`n }`n}")
$pc3 = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets/Plotters/PdfConfiguration.txt') -Raw
Write-PlotterFile (Join-Path $Destination 'AutocadTools PDF.pc3') $pc3
Write-PlotterFile (Join-Path $Destination 'PMP Files/AutocadTools PDF.pmp') $pmp.ToString()
Write-Output "PDF plotter: $($sizes.Count) zero-margin paper sizes in $Destination"
