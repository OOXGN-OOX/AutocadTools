param([ValidateSet('2026','2027')][string]$AutoCADVersion = '2026', [string]$DotnetPath, [switch]$RenderPreview)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (!$DotnetPath) { $DotnetPath = (& (Join-Path $taskRoot 'scripts\Get-BuildTools.ps1') -SkipCompiler).DotnetPath }
& $DotnetPath build (Join-Path $PSScriptRoot 'DrawingSymbolChecks.csproj') -c Debug "-p:AutoCADVersion=$AutoCADVersion" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Drawing symbol checks compilation failed.' }
$taskFramework = if ($AutoCADVersion -eq '2027') { 'net10.0-windows' } else { 'net8.0-windows' }
$taskScratch = Join-Path $env:TEMP ('AutocadTools-DrawingSymbolChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
$taskDll = Join-Path $taskScratch 'DrawingSymbolChecks.dll'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin\Debug\$taskFramework\DrawingSymbolChecks.dll") -Destination $taskDll
$taskScript = Join-Path $taskScratch 'checks.scr'
$taskLog = Join-Path $taskScratch 'results.log'
$taskDllForLisp = $taskDll.Replace('\','/')
$taskPreviewCommand = if ($RenderPreview) { "DRAWING_SYMBOL_PREVIEW`n" } else { '' }
[IO.File]::WriteAllText($taskScript, "(setvar `"SECURELOAD`" 0)`n(command `"_NETLOAD`" `"$taskDllForLisp`")`nDRAWING_SYMBOL_CHECKS`n${taskPreviewCommand}_QUIT`n_Y`n", [Text.Encoding]::ASCII)
$taskPreviousScratch = $env:AUTOCADTOOLS_SYMBOL_SCRATCH
try {
    $env:AUTOCADTOOLS_SYMBOL_SCRATCH = $taskScratch
    $taskProcess = Start-Process -FilePath "C:\Program Files\Autodesk\AutoCAD $AutoCADVersion\accoreconsole.exe" -ArgumentList @('/s', ('"' + $taskScript + '"'), '/l', 'en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskScratch 'stdout.log') -RedirectStandardError (Join-Path $taskScratch 'stderr.log')
} finally { $env:AUTOCADTOOLS_SYMBOL_SCRATCH = $taskPreviousScratch }
if (!$taskProcess.WaitForExit(60000)) { Stop-Process -Id $taskProcess.Id; throw "Symbol checks timed out: $taskScratch" }
if (!(Test-Path -LiteralPath $taskLog)) { throw "Symbol checks did not run: $taskScratch" }
$taskResult = [IO.File]::ReadAllText($taskLog)
$taskValidation = Join-Path $taskRoot 'artifacts\validation'
New-Item -ItemType Directory -Path $taskValidation -Force | Out-Null
Copy-Item -LiteralPath $taskLog -Destination (Join-Path $taskValidation "drawing-symbols-$AutoCADVersion.log") -Force
if ($taskResult -notmatch 'SYMBOL_PASS:' -or $taskResult -match 'SYMBOL_FAIL:') { throw $taskResult }
Write-Output $taskResult.Trim()
if ($RenderPreview) {
    $taskPreviewResult = Get-Content -LiteralPath (Join-Path $taskScratch 'preview.log') -Raw
    if ($taskPreviewResult -notmatch '^PREVIEW_PASS') { throw $taskPreviewResult }
    foreach ($taskPreviewName in @('symbol-preview.dwg','symbol-preview.pdf')) {
        Copy-Item -LiteralPath (Join-Path $taskScratch $taskPreviewName) -Destination (Join-Path $taskValidation $taskPreviewName) -Force
    }
    Write-Output 'PASS: AutoCAD PDF preview exported.'
}
