param(
    [ValidateSet('2026', '2027')][string]$AutoCADVersion = '2026',
    [string]$DotnetPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskConsole = "C:\Program Files\Autodesk\AutoCAD $AutoCADVersion\accoreconsole.exe"
if (!(Test-Path -LiteralPath $taskConsole)) { throw "AutoCAD $AutoCADVersion Core Console is not installed." }
if (!$DotnetPath) {
    $DotnetPath = Join-Path $taskRoot 'artifacts\tools\dotnet\dotnet.exe'
    if (!(Test-Path -LiteralPath $DotnetPath)) { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
& $DotnetPath build (Join-Path $PSScriptRoot 'MultiCopyChecks.csproj') -c Release "-p:AutoCADVersion=$AutoCADVersion" --nologo
if ($LASTEXITCODE -ne 0) { throw 'MultiCopyChecks compilation failed.' }
$taskFramework = if ($AutoCADVersion -eq '2027') { 'net10.0-windows' } else { 'net8.0-windows' }
$taskScratch = Join-Path $env:TEMP 'AutocadTools-MultiCopyChecks'
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
$taskDll = Join-Path $taskScratch 'MultiCopyChecks.dll'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin\Release\$taskFramework\MultiCopyChecks.dll") -Destination $taskDll -Force
$taskScript = Join-Path $taskScratch 'checks.scr'
$taskLog = Join-Path $taskScratch 'results.log'
if (Test-Path -LiteralPath $taskLog) { Remove-Item -LiteralPath $taskLog }
$taskDllForLisp = $taskDll.Replace('\', '/')
[IO.File]::WriteAllText($taskScript, "(setvar `"SECURELOAD`" 0)`n(command `"_netload`" `"$taskDllForLisp`")`nMULTICOPY_CHECKS`nMULTICOPY_SEED`nATMCOPY`n_Count`n3`n_Curve`n_Last`nMULTICOPY_VERIFY_COMMAND`nMULTICOPY_PRESELECT`nАТМКОП`n_Step`n30`n0,0`n100,0`nMULTICOPY_VERIFY_POINTS`nMULTICOPY_SEED_UCS`nАТМКОП`n30`n10,20,3`n100,20,3`nMULTICOPY_VERIFY_UCS`n_QUIT`n_Y`n", [Text.UTF8Encoding]::new($true))
$taskProcess = Start-Process -FilePath $taskConsole -ArgumentList @('/s', ('"' + $taskScript + '"'), '/l', 'en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskScratch 'stdout.log') -RedirectStandardError (Join-Path $taskScratch 'stderr.log')
if (!$taskProcess.WaitForExit(60000)) {
    Stop-Process -Id $taskProcess.Id -ErrorAction SilentlyContinue
    throw "Core Console timed out. Logs: $taskScratch"
}
if (!(Test-Path -LiteralPath $taskLog)) { throw "Checks did not run. Logs: $taskScratch" }
$taskResult = [IO.File]::ReadAllText($taskLog)
$taskValidation = Join-Path $taskRoot 'artifacts\validation'
New-Item -ItemType Directory -Path $taskValidation -Force | Out-Null
Copy-Item -LiteralPath $taskLog -Destination (Join-Path $taskValidation "multicopy-$AutoCADVersion.log") -Force
if ($taskResult -notmatch 'MULTICOPY_PASS:' -or $taskResult -notmatch 'Shifted/rotated UCS command and elevation passed' -or $taskResult -match 'MULTICOPY_FAIL:') { throw $taskResult }
Write-Output ($taskResult.Trim().Split("`n")[-1])
