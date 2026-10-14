# Use streaming .NET hashing independently of PowerShell module autoloading.
function Get-FileDigest {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [ValidateSet('SHA256','SHA512')][string]$Algorithm = 'SHA256'
    )
    $hasher = if ($Algorithm -eq 'SHA512') { [Security.Cryptography.SHA512]::Create() }
              else { [Security.Cryptography.SHA256]::Create() }
    $stream = $null
    try {
        $stream = [IO.File]::OpenRead($Path)
        return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
    } finally {
        if ($stream) { $stream.Dispose() }
        $hasher.Dispose()
    }
}
