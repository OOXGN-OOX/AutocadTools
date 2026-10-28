param(
    [ValidateSet(2026,2027)][int]$Year = 2026,
    [ValidateSet('Integration','Plot','Library','Gost')][string]$Kind = 'Integration',
    [string]$PluginPath,
    [string]$DotnetPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$DotnetPath) { $DotnetPath = (& (Join-Path $PSScriptRoot 'Get-BuildTools.ps1') -SkipCompiler).DotnetPath }
$hostPath = "C:\Program Files\Autodesk\AutoCAD $Year\accoreconsole.exe"
if (!(Test-Path -LiteralPath $hostPath)) { throw "AutoCAD $Year Core Console is not installed." }
$framework = if ($Year -eq 2026) { 'net8.0-windows' } else { 'net10.0-windows' }
if (!$PluginPath) {
    & $DotnetPath build (Join-Path $root 'AutocadTools.csproj') -c Release "-p:AutoCADVersion=$Year" --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    $PluginPath = Join-Path $root "bin\Release\AutoCAD$Year\$framework\AutocadTools.dll"
}
& $DotnetPath build (Join-Path $root 'tests\IntegrationChecks\IntegrationChecks.csproj') -c Release "-p:AutoCADVersion=$Year" --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Integration module build failed.' }
$scratch = Join-Path $env:TEMP ('AutocadTools-Integration-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$null = New-Item -ItemType Directory -Path $scratch
Get-ChildItem -LiteralPath (Split-Path $PluginPath -Parent) | Copy-Item -Destination $scratch -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "tests\IntegrationChecks\bin\Release\$framework\AutoCADSmoke.dll") -Destination $scratch
$inputDrawing = Join-Path $scratch 'input.dwg'
Copy-Item -LiteralPath (Join-Path $root 'Assets\WM_FORMAT.dwg') -Destination $inputDrawing
$testDll = (Join-Path $scratch 'AutoCADSmoke.dll').Replace('\','/')
$pluginDll = (Join-Path $scratch 'AutocadTools.dll').Replace('\','/')
$script = Join-Path $scratch 'checks.scr'
$log = Join-Path $scratch 'stdout.log'
$command = if ($Kind -eq 'Gost') { 'AUTOCADTOOLS_WIDEPLOTTEST' } elseif ($Kind -eq 'Library') { 'AUTOCADTOOLS_LIBRARYSETUP' } elseif ($Kind -eq 'Plot') { 'AUTOCADTOOLS_PLOTTEST' } else { 'AUTOCADTOOLS_INTEGRATION' }
$lines = @('(setvar "SECURELOAD" 0)', '(command "_NETLOAD" "' + $pluginDll + '")', '(command "_NETLOAD" "' + $testDll + '")', $command)
if ($Kind -eq 'Library') {
    $dispatch = (Join-Path $scratch 'library-dispatch.lsp').Replace('\','/')
    $lines += @('(load "'+$dispatch+'")', '(c:bp-library-insert)', '0,0', 'AUTOCADTOOLS_LIBRARYVERIFY')
}
$lines += @('_QUIT', '_Y', '')
[IO.File]::WriteAllLines($script, $lines, [Text.Encoding]::ASCII)
$data = Join-Path $scratch 'data'
$null = New-Item -ItemType Directory -Path $data
[IO.File]::WriteAllText((Join-Path $data 'settings.json'), '{"AutoCheckUpdates":false,"AutoInstallUpdates":false}')
$priorData = $env:AUTOCADTOOLS_DATA_DIRECTORY
try {
    $env:AUTOCADTOOLS_DATA_DIRECTORY = $data
    $process = Start-Process -FilePath $hostPath -ArgumentList @('/i', ('"'+$inputDrawing+'"'), '/s', ('"'+$script+'"'), '/l', 'en-US', '/isolate', 'AutocadToolsTest', ('"'+(Join-Path $scratch 'profile')+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError (Join-Path $scratch 'stderr.log')
} finally { $env:AUTOCADTOOLS_DATA_DIRECTORY = $priorData }
if (!$process.WaitForExit(180000)) { Stop-Process -Id $process.Id; throw "Integration checks timed out: $scratch" }
$text = [IO.File]::ReadAllText($log, [Text.Encoding]::Unicode)
$text = $text.Replace([string][char]0x0A0D, '').Replace([string][char]0x0D00, '')
[IO.File]::WriteAllText((Join-Path $scratch 'results.log'), $text, [Text.UTF8Encoding]::new($false))
$marker = if ($Kind -eq 'Gost') { 'AUTOCADTOOLS_WIDEPLOT_PASS:' } elseif ($Kind -eq 'Library') { 'LIBRARY_PASS:' } elseif ($Kind -eq 'Plot') { 'AUTOCADTOOLS_PLOT_PASS:' } else { 'INTEGRATION_PASS' }
if ($text -notmatch $marker -or $text -match '(INTEGRATION_FAIL|[A-Z_]+_FAIL:)') { throw "Native checks failed: $scratch\results.log" }
$text.Split("`n") | Where-Object { $_ -match 'PASS' } | ForEach-Object { $_.Trim() }
Write-Output "Native report: $scratch\results.log"
