# Defines release planning helpers for CI and local checks.
function Get-ReleaseCandidate([string]$Root) {
    $version = [string](([xml][IO.File]::ReadAllText((Join-Path $Root 'AutocadTools.csproj'))).Project.PropertyGroup.Version | Select-Object -First 1)
    if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Project version must be X.Y.Z.' }
    $tag = "v$version"
    $notes = Join-Path $Root "docs\releases\$tag.md"
    $ready = (Test-Path -LiteralPath $notes) -and ![string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($notes))
    return [PSCustomObject]@{ Version = $version; Tag = $tag; Notes = $notes; Ready = $ready }
}

function Test-ReleaseRequired($Candidate, $ExistingRelease, [string]$Commit = '') {
    if (!$Candidate.Ready) { return $false }
    if ($null -eq $ExistingRelease) { return $true }
    if ($ExistingRelease.tag_name -ne $Candidate.Tag) { throw 'Unexpected release tag.' }
    if ($ExistingRelease.draft -eq $true -and $Commit -and $ExistingRelease.target_commitish -ne $Commit) {
        throw 'Release draft targets different sources. Retry its original run or prepare a new version.'
    }
    return $ExistingRelease.draft -eq $true
}
