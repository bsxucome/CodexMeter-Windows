param(
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][string]$Version,
    [ValidateSet('stable','beta')][string]$Channel = 'stable',
    [ValidateSet('framework-x64','dotnet10-x64')][string]$Runtime = 'framework-x64',
    [Parameter(Mandatory=$true)][string]$PackageUrl,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$PrivateKeyFile,
    [string]$Notes = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$' -or $Version.Length -gt 100) { throw 'Invalid SemVer.' }
foreach ($number in ($Version.Split('-')[0].Split('+')[0].Split('.'))) {
    [int]$parsedNumber = 0
    if (![int]::TryParse($number, [ref]$parsedNumber)) { throw 'Version component exceeds the supported range.' }
}
$coreVersion = $Version.Split('+')[0]
if ($coreVersion.Contains('-')) {
    foreach ($identifier in $coreVersion.Substring($coreVersion.IndexOf('-') + 1).Split('.')) {
        if (!$identifier -or ($identifier -match '^\d+$' -and $identifier.Length -gt 1 -and $identifier.StartsWith('0'))) { throw 'Invalid prerelease identifier.' }
    }
}
if ($Version.Contains('+') -and @($Version.Split('+')[1].Split('.') | Where-Object { !$_.Length }).Count) { throw 'Invalid build metadata.' }
if ($Channel -eq 'stable' -and $Version.Contains('-')) { throw 'Stable does not accept prerelease versions.' }
$url = [Uri]$PackageUrl
if (!$url.IsAbsoluteUri -or $url.Scheme -ne 'https' -or $url.UserInfo -or $url.Fragment) { throw 'An HTTPS Windows package URL is required.' }
if ($Notes.Length -gt 8000) { throw 'Release notes exceed 8000 characters.' }
$privateXml = if ($PrivateKeyFile) { [IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyFile).Path) } else { $env:CODEXMETER_RELEASE_KEY_XML }
if (!$privateXml) { throw 'Provide an RSA private XML key file or CODEXMETER_RELEASE_KEY_XML. No production key is generated automatically.' }
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
$zipPath = (Resolve-Path -LiteralPath $Archive).Path
if ((Get-Item -LiteralPath $zipPath).Length -gt 536870912) { throw 'Archive exceeds 512 MB.' }
$files = @()
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $names = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $name = $entry.FullName
        if ($name.Length -gt 240 -or $name -match '[\\:]|^/|(^|/)\.\.?(/|$)|(^|/)[^/]*[. ](/|$)|[<>"|?*]' -or !$names.Add($name)) { throw 'Invalid or duplicate archive path.' }
        foreach ($segment in $name.Split('/')) {
            if (!$segment.Length -or $segment -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)|[\x00-\x1f]') { throw 'Invalid Windows file name.' }
        }
        if (($entry.ExternalAttributes -band 0x400) -or (($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000) { throw 'Links cannot be published.' }
        if ($entry.Length -gt 536870912 -or $files.Count -ge 4096) { throw 'Update package exceeds limits.' }
        $total += $entry.Length
        if ($total -gt 1073741824) { throw 'Extracted package exceeds limits.' }
        $sha = [Security.Cryptography.SHA256]::Create()
        $stream = $entry.Open()
        try { $hash = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        $files += [ordered]@{ path=$name; size=$entry.Length; sha256=$hash }
    }
    if (!$names.Contains('CodexMeter.exe') -or !$names.Contains('CodexMeter.Core.dll')) { throw 'Missing Windows application files.' }
    $rsa.FromXmlString($privateXml)
    if ($rsa.KeySize -lt 2048 -or $rsa.KeySize -gt 8192) { throw 'RSA key must be 2048 to 8192 bits.' }
    $payload = [ordered]@{ product='CodexMeter.Windows'; channel=$Channel; runtime=$Runtime; version=$Version; url=$url.AbsoluteUri;
        sha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant(); size=(Get-Item -LiteralPath $zipPath).Length; notes=$Notes; files=$files }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 8 -Compress))
    if ($bytes.Length -gt 262144) { throw 'Release payload exceeds 256 KB.' }
    $signature = $rsa.SignData($bytes, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    $envelope = [ordered]@{ payload=[Convert]::ToBase64String($bytes); signature=[Convert]::ToBase64String($signature) } | ConvertTo-Json -Compress
    $public = $rsa.ExportParameters($false)
    $publicJson = [ordered]@{ modulus=[Convert]::ToBase64String($public.Modulus); exponent=[Convert]::ToBase64String($public.Exponent) } | ConvertTo-Json -Compress
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory "$Channel-$Runtime.json"), $envelope, $encoding)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'public-key.json'), $publicJson, $encoding)
    Write-Output "Signed Windows feed: $Channel-$Runtime.json; public key: public-key.json"
} finally { $zip.Dispose(); $rsa.Dispose(); $privateXml = $null }
