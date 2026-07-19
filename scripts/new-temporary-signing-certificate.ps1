[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Subject = "CN=DeskSnapshot",
    [ValidateRange(1, 30)][int]$ValidDays = 7
)

$ErrorActionPreference = "Stop"
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$pfxPath = Join-Path $OutputDirectory "DeskSnapshot-Build-Signing.pfx"
$passwordPath = Join-Path $OutputDirectory "DeskSnapshot-Build-Signing.password"
$passwordBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$password = [Convert]::ToBase64String($passwordBytes)
$rsa = [Security.Cryptography.RSA]::Create(3072)
$certificate = $null
$pfxBytes = $null

try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $Subject,
        $rsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
            $true))
    $codeSigningOids = [Security.Cryptography.OidCollection]::new()
    [void]$codeSigningOids.Add([Security.Cryptography.Oid]::new("1.3.6.1.5.5.7.3.3"))
    $request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($codeSigningOids, $true))
    $request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))

    $certificate = $request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddMinutes(-5),
        [DateTimeOffset]::UtcNow.AddDays($ValidDays))
    $pfxBytes = $certificate.Export(
        [Security.Cryptography.X509Certificates.X509ContentType]::Pfx,
        $password)
    [IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
    [IO.File]::WriteAllText($passwordPath, $password, [Text.UTF8Encoding]::new($false))

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        "pfx_path=$pfxPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "password_path=$passwordPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "thumbprint=$($certificate.Thumbprint)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }

    Write-Host "Created temporary build signing certificate $($certificate.Thumbprint) ($Subject)."
} finally {
    if ($pfxBytes) { [Array]::Clear($pfxBytes, 0, $pfxBytes.Length) }
    [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
    $password = $null
    if ($certificate) { $certificate.Dispose() }
    $rsa.Dispose()
}
