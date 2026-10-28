param([ValidateSet('2026','2027')][string]$AutoCADVersion = '2026', [string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (!$DotnetPath) { $DotnetPath = (& (Join-Path $taskRoot 'scripts\Get-BuildTools.ps1') -SkipCompiler).DotnetPath }
& $DotnetPath build (Join-Path $PSScriptRoot 'NumberedCopyChecks.csproj') -c Debug "-p:AutoCADVersion=$AutoCADVersion" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Numbered copy checks compilation failed.' }
$taskFramework = if ($AutoCADVersion -eq '2027') { 'net10.0-windows' } else { 'net8.0-windows' }
$taskScratch = Join-Path $env:TEMP ('AutocadTools-NumberedCopyChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
$taskDll = Join-Path $taskScratch 'NumberedCopyChecks.dll'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin\Debug\$taskFramework\NumberedCopyChecks.dll") -Destination $taskDll
Copy-Item -LiteralPath (Join-Path $taskRoot 'Assets\WM_FORMAT.dwg') -Destination (Join-Path $taskScratch 'WM_FORMAT.dwg')
$taskScript = Join-Path $taskScratch 'checks.scr'
$taskLog = Join-Path $taskScratch 'results.log'
$taskDllForLisp = $taskDll.Replace('\','/')
[IO.File]::WriteAllText($taskScript, "(setvar `"SECURELOAD`" 0)`n(command `"_NETLOAD`" `"$taskDllForLisp`")`nNUMBERED_COPY_CHECKS`nNUMBERED_COPY_CHECK_PAPER`nNUMBERED_COPY_SEED_MANUAL`nNUMBERED_COPY_MANUAL`n10,20,3`n40,20,3`n70,20,3`n_Undo`n100,20,3`n`nNUMBERED_COPY_VERIFY_MANUAL`nNUMBERED_COPY_SEED_CURVE`nNUMBERED_COPY_CURVE`n_Last`n_No`nNUMBERED_COPY_VERIFY_CANCEL`nNUMBERED_COPY_CURVE`n_Last`n_Yes`nNUMBERED_COPY_VERIFY_CURVE`n_U`nNUMBERED_COPY_VERIFY_UNDO`n_QUIT`n_Y`n", [Text.Encoding]::ASCII)
$taskPreviousScratch = $env:AUTOCADTOOLS_NUMCOPY_SCRATCH
try {
    $env:AUTOCADTOOLS_NUMCOPY_SCRATCH = $taskScratch
    $taskProcess = Start-Process -FilePath "C:\Program Files\Autodesk\AutoCAD $AutoCADVersion\accoreconsole.exe" -ArgumentList @('/s', ('"' + $taskScript + '"'), '/l', 'en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskScratch 'stdout.log') -RedirectStandardError (Join-Path $taskScratch 'stderr.log')
} finally { $env:AUTOCADTOOLS_NUMCOPY_SCRATCH = $taskPreviousScratch }
if (!$taskProcess.WaitForExit(60000)) { Stop-Process -Id $taskProcess.Id; throw "Numbered copy checks timed out: $taskScratch" }
if (!(Test-Path -LiteralPath $taskLog)) { throw "Numbered copy checks did not run: $taskScratch" }
$taskResult = [IO.File]::ReadAllText($taskLog)
$taskValidation = Join-Path $taskRoot 'artifacts\validation'
New-Item -ItemType Directory -Path $taskValidation -Force | Out-Null
Copy-Item -LiteralPath $taskLog -Destination (Join-Path $taskValidation "numbered-copy-$AutoCADVersion.log") -Force
if ($taskResult -notmatch 'NUMBERED_COPY_PASS:' -or $taskResult -notmatch 'PAPER_PASS:' -or $taskResult -notmatch 'MANUAL_PASS:' -or $taskResult -notmatch 'CURVE_PASS:' -or $taskResult -notmatch 'CANCEL_PASS:' -or $taskResult -notmatch 'UNDO_PASS:' -or $taskResult -match 'NUMBERED_COPY_FAIL:') { throw $taskResult }
Write-Output $taskResult.Trim()