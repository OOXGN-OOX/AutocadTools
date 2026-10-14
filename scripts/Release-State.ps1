# Defines helpers only. Import with dot-sourcing.
function Get-ReleaseFingerprint([string]$Root) {
    $taskInputs = Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\(\.git|\.vs|\.agents|bin|obj|artifacts|tests|docs)\\' -and
        ($_.Extension -in '.cs','.xaml','.csproj','.props','.ps1','.cmd','.iss','.xml','.png','.dwg' -or $_.Name -in 'LICENSE','THIRD-PARTY-NOTICES.md')
    } | Sort-Object FullName
    $taskManifest = foreach ($taskFile in $taskInputs) {
        $taskRelative = $taskFile.FullName.Substring($Root.TrimEnd('\').Length).Replace('\', '/')
        "$taskRelative $(Get-FileDigest -Path $taskFile.FullName)"
    }
    $taskHasher = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($taskHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(($taskManifest -join "`n"))))).Replace('-', '').ToLowerInvariant() }
    finally { $taskHasher.Dispose() }
}

function Test-ReleaseCache([string]$Directory, [string]$Fingerprint, [string]$Version) {
    try {
        $taskReceiptPath = Join-Path $Directory 'build-state.json'
        if (!(Test-Path -LiteralPath $taskReceiptPath)) { return $false }
        $taskReceipt = Get-Content -LiteralPath $taskReceiptPath -Raw | ConvertFrom-Json
        if ($taskReceipt.Fingerprint -ne $Fingerprint -or $taskReceipt.Version -ne $Version) { return $false }
        $taskExpected = @("AutocadTools-$Version-Setup.exe", "AutocadTools-$Version-Bundle.zip", 'SHA256SUMS.txt')
        foreach ($taskName in $taskExpected) {
            $taskArtifact = Join-Path $Directory $taskName
            $taskEntry = @($taskReceipt.Files | Where-Object Name -eq $taskName)
            if ($taskEntry.Count -ne 1 -or !(Test-Path -LiteralPath $taskArtifact) -or (Get-FileDigest -Path $taskArtifact) -ne $taskEntry[0].Hash) { return $false }
        }
        return $true
    }
    catch { return $false }
}
