param(
    [Parameter(Mandatory=$true)][string]$Directory,
    [Parameter(Mandatory=$true)][string]$CertificatePath,
    [Parameter(Mandatory=$true)][string]$TimestampServer
)
$ErrorActionPreference = 'Stop'
$address = [Uri]$TimestampServer
if ($address.Scheme -notin @('http','https') -or !$address.IsAbsoluteUri) { throw 'Invalid timestamp server.' }
$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2(
    (Resolve-Path -LiteralPath $CertificatePath).Path,
    $env:CODEXMETER_PFX_PASSWORD,
    [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
try {
    if (!$certificate.HasPrivateKey) { throw 'The code signing certificate has no private key.' }
    foreach ($name in @('CodexMeter.exe', 'CodexMeter.Core.dll')) {
        $path = Join-Path $Directory $name
        $result = Set-AuthenticodeSignature -LiteralPath $path -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
        if ($result.Status -ne 'Valid') { throw "Authenticode validation failed for $name." }
    }
} finally { $certificate.Dispose() }
