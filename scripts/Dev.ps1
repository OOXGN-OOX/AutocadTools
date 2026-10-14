param(
    [ValidateSet('Check','Test','Build')][string]$Mode = 'Check',
    [ValidateSet('All','MultiCopy','Updates','Pdf')][string]$Suite = 'All',
    [ValidateSet('2025','2026','2027')][string]$AutoCADVersion = '2026',
    [switch]$AutoCAD,
    [string]$DotnetPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-Structure.ps1') | Out-Host
foreach ($taskScript in @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') + @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tests') -Filter '*.ps1' -Recurse)) {
    $taskTokens = $null; $taskErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($taskScript.FullName, [ref]$taskTokens, [ref]$taskErrors)
    if ($taskErrors.Count) { throw ($taskErrors | Out-String) }
}
Write-Host 'PASS: PowerShell syntax. No plugin or installer was built.'
if ($Mode -eq 'Check') { return }
if ($Mode -eq 'Test' -and $Suite -eq 'All') { & (Join-Path $taskRoot 'tests\WorkflowChecks\Run.ps1') | Out-Host }
if (!$DotnetPath) { $DotnetPath = (& (Join-Path $PSScriptRoot 'Get-BuildTools.ps1') -SkipCompiler).DotnetPath }
function Invoke-TaskDotnet([string[]]$Arguments) {
    & $DotnetPath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}
if ($Mode -eq 'Build') {
    Invoke-TaskDotnet @('build', (Join-Path $taskRoot 'AutocadTools.csproj'), '-c', 'Debug', "-p:AutoCADVersion=$AutoCADVersion")
    Write-Host 'Debug DLL ready. No distribution or Git operation was performed.'
    return
}
if ($Suite -in @('All','MultiCopy')) {
    Invoke-TaskDotnet @('run', '--project', (Join-Path $taskRoot 'tests\MultiCopyLayoutChecks\MultiCopyLayoutChecks.csproj'), '-c', 'Debug')
}
if ($Suite -in @('All','Pdf')) {
    Invoke-TaskDotnet @('run', '--project', (Join-Path $taskRoot 'tests\PdfChecks\PdfChecks.csproj'), '-c', 'Debug')
}
if ($Suite -in @('All','Updates')) {
    $taskSystemDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    foreach ($taskFramework in @('net8.0','net10.0')) {
        $taskMajor = if ($taskFramework -eq 'net8.0') { '8' } else { '10' }
        $taskRuntime = $null
        foreach ($taskCandidate in @($DotnetPath, $taskSystemDotnet.Source) | Where-Object { $_ } | Select-Object -Unique) {
            $taskRuntimes = & $taskCandidate --list-runtimes
            if (@($taskRuntimes | Where-Object { $_ -match "^Microsoft.NETCore.App $taskMajor\." }).Count) { $taskRuntime = $taskCandidate; break }
        }
        if (!$taskRuntime) { throw "Install the .NET $taskMajor x64 runtime to run $taskFramework checks. No roll-forward is used." }
        Invoke-TaskDotnet @('build', (Join-Path $taskRoot 'tests\UpdateChecks\UpdateChecks.csproj'), '-c', 'Debug', '-f', $taskFramework)
        & $taskRuntime exec (Join-Path $taskRoot "tests\UpdateChecks\bin\Debug\$taskFramework\UpdateChecks.dll") | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Update checks failed on $taskFramework." }
    }
}
if ($AutoCAD) {
    if ($AutoCADVersion -eq '2025') { throw 'Core Console checks currently support 2026 and 2027.' }
    if ($Suite -in @('All','MultiCopy')) { & (Join-Path $taskRoot 'tests\MultiCopyChecks\Run.ps1') -AutoCADVersion $AutoCADVersion -DotnetPath $DotnetPath | Out-Host }
    if ($Suite -eq 'All') { & (Join-Path $taskRoot 'tests\AlignSkewChecks\Run.ps1') -AutoCADVersion $AutoCADVersion -DotnetPath $DotnetPath | Out-Host }
}
