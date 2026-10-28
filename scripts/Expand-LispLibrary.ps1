param([Parameter(Mandatory=$true)][string]$ArchivePath, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'File-Hash.ps1')
$archiveHash = Get-FileDigest -Path $ArchivePath
$marker = Join-Path $Destination '.archive-sha256'
if ((Test-Path -LiteralPath $marker) -and ([IO.File]::ReadAllText($marker) -eq $archiveHash)) { return }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
$null = [IO.Directory]::CreateDirectory($destinationRoot)
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
try {
    foreach ($entry in $archive.Entries) {
        $targetPath = [IO.Path]::GetFullPath([IO.Path]::Combine($destinationRoot, $entry.FullName))
        if (!$targetPath.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Library entry escapes its destination.' }
        if (!$entry.Name) { $null = [IO.Directory]::CreateDirectory($targetPath); continue }
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($targetPath))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetPath, $true)
    }
} finally { $archive.Dispose() }
if (!(Test-Path -LiteralPath (Join-Path $Destination 'catalog.json'))) { throw 'Bundled LISP catalogue missing.' }
[IO.File]::WriteAllText($marker, $archiveHash)
