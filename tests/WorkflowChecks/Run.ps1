$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $taskRoot 'scripts\File-Hash.ps1')
. (Join-Path $taskRoot 'scripts\Release-State.ps1')
. (Join-Path $taskRoot 'scripts\Release-Plan.ps1')
$taskScratch = Join-Path $env:TEMP ('AutocadTools-WorkflowChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
function Assert-Workflow([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
try {
    [IO.File]::WriteAllText((Join-Path $taskScratch 'AutocadTools.csproj'), '<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>')
    $taskCandidate = Get-ReleaseCandidate $taskScratch
    Assert-Workflow (!(Test-ReleaseRequired $taskCandidate $null)) 'Unfinished version must not publish.'
    New-Item -ItemType Directory -Path (Join-Path $taskScratch 'docs\releases') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskScratch 'docs\releases\v1.0.0.md'), 'Product release notes')
    $taskCandidate = Get-ReleaseCandidate $taskScratch
    Assert-Workflow (Test-ReleaseRequired $taskCandidate $null) 'Prepared version should publish.'
    Assert-Workflow (!(Test-ReleaseRequired $taskCandidate ([PSCustomObject]@{ tag_name = 'v1.0.0'; draft = $false }))) 'Published version must not rebuild.'
    Assert-Workflow (Test-ReleaseRequired $taskCandidate ([PSCustomObject]@{ tag_name = 'v1.0.0'; draft = $true })) 'Interrupted draft should be retryable.'
    $taskWrongTarget = $false
    try { Test-ReleaseRequired $taskCandidate ([PSCustomObject]@{ tag_name = 'v1.0.0'; draft = $true; target_commitish = 'old-sources' }) 'new-sources' | Out-Null }
    catch { $taskWrongTarget = $true }
    Assert-Workflow $taskWrongTarget 'Draft must not mix files built from different sources.'
    $taskCode = Join-Path $taskScratch 'Sample.cs'
    [IO.File]::WriteAllText($taskCode, 'first')
    $taskFirst = Get-ReleaseFingerprint -Root $taskScratch
    foreach ($taskDirectory in @('docs','tests','.agents')) {
        New-Item -ItemType Directory -Path (Join-Path $taskScratch $taskDirectory) -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $taskScratch "$taskDirectory\ignored.cs"), 'not shipped')
    }
    Assert-Workflow ((Get-ReleaseFingerprint -Root $taskScratch) -eq $taskFirst) 'Documentation/tests/local working files should not invalidate shipped binaries.'
    [IO.File]::WriteAllText($taskCode, 'changed')
    $taskChanged = Get-ReleaseFingerprint -Root $taskScratch
    Assert-Workflow ($taskChanged -ne $taskFirst) 'Code change must invalidate cache.'
    $taskArtifacts = Join-Path $taskScratch 'artifacts'
    New-Item -ItemType Directory -Path $taskArtifacts | Out-Null
    $taskFiles = foreach ($taskName in @('AutocadTools-1.0.0-Setup.exe','AutocadTools-1.0.0-Bundle.zip','SHA256SUMS.txt')) {
        $taskPath = Join-Path $taskArtifacts $taskName
        [IO.File]::WriteAllText($taskPath, $taskName)
        @{ Name = $taskName; Hash = Get-FileDigest -Path $taskPath }
    }
    @{ Version = '1.0.0'; Fingerprint = $taskChanged; Files = @($taskFiles) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskArtifacts 'build-state.json') -Encoding UTF8
    Assert-Workflow (Test-ReleaseCache -Directory $taskArtifacts -Fingerprint $taskChanged -Version '1.0.0') 'Valid cache rejected.'
    Assert-Workflow (!(Test-ReleaseCache -Directory $taskArtifacts -Fingerprint $taskFirst -Version '1.0.0')) 'Stale source cache accepted.'
    Assert-Workflow (!(Test-ReleaseCache -Directory $taskArtifacts -Fingerprint $taskChanged -Version '1.0.1')) 'Wrong version cache accepted.'
    [IO.File]::WriteAllText((Join-Path $taskArtifacts 'AutocadTools-1.0.0-Bundle.zip'), 'corrupt')
    Assert-Workflow (!(Test-ReleaseCache -Directory $taskArtifacts -Fingerprint $taskChanged -Version '1.0.0')) 'Corrupted artifact accepted.'
    Write-Output 'PASS: release cache invalidation, versions and artifact integrity.'
}
finally {
    $taskResolved = [IO.Path]::GetFullPath($taskScratch)
    $taskTempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (!$taskResolved.StartsWith($taskTempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Scratch path escaped TEMP.' }
    Remove-Item -LiteralPath $taskResolved -Recurse -Force
}
