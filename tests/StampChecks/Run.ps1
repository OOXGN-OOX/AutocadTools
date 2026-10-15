param([ValidateSet('2026','2027')][string]$AutoCADVersion = '2026', [string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (!$DotnetPath) { $DotnetPath = (& (Join-Path $taskRoot 'scripts\Get-BuildTools.ps1') -SkipCompiler).DotnetPath }
& $DotnetPath build (Join-Path $PSScriptRoot 'StampChecks.csproj') -c Release "-p:AutoCADVersion=$AutoCADVersion" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Stamp checks compilation failed.' }
$taskFramework = if ($AutoCADVersion -eq '2027') { 'net10.0-windows' } else { 'net8.0-windows' }
$taskScratch = Join-Path $env:TEMP ('AutocadTools-StampChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "bin\Release\$taskFramework") -Filter '*.dll' | Copy-Item -Destination $taskScratch -Force
$taskLog = Join-Path $taskScratch 'results.log'
if (Test-Path -LiteralPath $taskLog) { Remove-Item -LiteralPath $taskLog }
$taskDll = (Join-Path $taskScratch 'StampChecks.dll').Replace('\','/')
$taskScript = Join-Path $taskScratch 'checks.scr'
[IO.File]::WriteAllText($taskScript, "(setvar `"SECURELOAD`" 0)`n(command `"_NETLOAD`" `"$taskDll`")`nSTAMP_CHECKS`n_QUIT`n_Y`n", [Text.Encoding]::ASCII)
$taskOriginalTemplate = $env:AUTOCADTOOLS_STAMP_TEMPLATE
$taskOriginalScratch = $env:AUTOCADTOOLS_STAMP_SCRATCH
try {
    $env:AUTOCADTOOLS_STAMP_TEMPLATE = Join-Path $taskRoot 'Assets\WM_FORMAT.dwg'
    $env:AUTOCADTOOLS_STAMP_SCRATCH = $taskScratch
    $taskProcess = Start-Process -FilePath "C:\Program Files\Autodesk\AutoCAD $AutoCADVersion\accoreconsole.exe" -ArgumentList @('/s', ('"' + $taskScript + '"'), '/l', 'en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskScratch 'stdout.log') -RedirectStandardError (Join-Path $taskScratch 'stderr.log')
} finally { $env:AUTOCADTOOLS_STAMP_TEMPLATE = $taskOriginalTemplate; $env:AUTOCADTOOLS_STAMP_SCRATCH = $taskOriginalScratch }
if (!$taskProcess.WaitForExit(60000)) { Stop-Process -Id $taskProcess.Id; throw "Stamp checks timed out: $taskScratch" }
if (!(Test-Path -LiteralPath $taskLog)) { throw "Stamp checks did not run: $taskScratch" }
$taskResult = [IO.File]::ReadAllText($taskLog)
if ($taskResult -notmatch 'STAMP_PASS:' -or $taskResult -match 'STAMP_FAIL:') { throw $taskResult }
Write-Output $taskResult.Trim()
