[CmdletBinding()]
param(
    [string]$Subject = "CN=DeskSnapshot",
    [ValidateRange(1, 20)][int]$ValidYears = 10,
    [switch]$Rotate,
    [switch]$ConfigureGitHub,
    [string]$Repository = "FeranyDev/DeskSnapshot"
)

$ErrorActionPreference = "Stop"
$signingFolder = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "DeskSnapshot\Signing"
$configPath = Join-Path $signingFolder "certificate.json"
$publicCertificatePath = Join-Path $signingFolder "DeskSnapshot-Signing.cer"
New-Item -ItemType Directory -Force -Path $signingFolder | Out-Null

$certificate = $null
$previousCertificate = $null
$createdCertificate = $false
if (Test-Path $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ($config.thumbprint) {
        $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$($config.thumbprint)" -ErrorAction SilentlyContinue
    }
}

if ($Rotate) {
    if (-not $ConfigureGitHub) {
        throw "Certificate rotation must include -ConfigureGitHub so local and remote signing identities stay synchronized."
    }
    $previousCertificate = $certificate
    $certificate = $null
} elseif ($certificate -and -not [string]::Equals(
    $certificate.Subject,
    $Subject,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "The configured certificate subject is $($certificate.Subject). Run again with -Rotate -ConfigureGitHub to replace it with $Subject."
}

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -FriendlyName "DeskSnapshot reusable signing certificate" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
        -NotAfter (Get-Date).AddYears($ValidYears)
    $createdCertificate = $true
}

if (-not $certificate.HasPrivateKey) { throw "The configured certificate does not contain a private key." }
if ($certificate.NotAfter -le (Get-Date)) { throw "The configured certificate has expired." }

try {
if ($ConfigureGitHub) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is required for -ConfigureGitHub. The certificate remains configured locally."
    }
    & gh auth status | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not authenticated." }
    & gh api --method PUT "repos/$Repository/environments/signing" --silent
    if ($LASTEXITCODE -ne 0) { throw "Unable to create or access the GitHub signing environment." }

    $password = Read-Host "Create a strong PFX password for GitHub Actions" -AsSecureString
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
    $plainPassword = $null
    $pfxBytes = $null
    $base64 = $null
    $temporaryPfx = Join-Path ([IO.Path]::GetTempPath()) "DeskSnapshot-Signing-$([guid]::NewGuid().ToString('N')).pfx"
    try {
        $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
        if ([string]::IsNullOrWhiteSpace($plainPassword)) { throw "The PFX password cannot be empty." }
        Export-PfxCertificate -Cert $certificate -FilePath $temporaryPfx -Password $password -ChainOption EndEntityCertOnly | Out-Null
        $pfxBytes = [IO.File]::ReadAllBytes($temporaryPfx)
        $base64 = [Convert]::ToBase64String($pfxBytes)
        $base64 | & gh secret set DESKSNAPSHOT_SIGNING_CERTIFICATE_BASE64 --env signing --repo $Repository
        if ($LASTEXITCODE -ne 0) { throw "Unable to save the certificate GitHub secret." }
        $plainPassword | & gh secret set DESKSNAPSHOT_SIGNING_CERTIFICATE_PASSWORD --env signing --repo $Repository
        if ($LASTEXITCODE -ne 0) { throw "Unable to save the certificate password GitHub secret." }
        & gh variable set DESKSNAPSHOT_SIGNING_CERTIFICATE_THUMBPRINT --env signing --repo $Repository --body $certificate.Thumbprint
        if ($LASTEXITCODE -ne 0) { throw "Unable to save the expected certificate thumbprint." }
        Write-Host "The same certificate was stored in GitHub Secrets without printing its private key." -ForegroundColor Green
    } finally {
        if ($pfxBytes) { [Array]::Clear($pfxBytes, 0, $pfxBytes.Length) }
        $base64 = $null
        if ($passwordPointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        }
        $plainPassword = $null
        if (Test-Path $temporaryPfx) { Remove-Item -LiteralPath $temporaryPfx -Force }
    }
}

$config = [ordered]@{
    thumbprint = $certificate.Thumbprint
    subject = $certificate.Subject
    expiresAt = $certificate.NotAfter.ToUniversalTime().ToString("O")
}
$config | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding utf8
Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null
} catch {
    if ($createdCertificate -and $certificate) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }
    throw
}

Write-Host "Reusable signing certificate ready." -ForegroundColor Green
Write-Host "Thumbprint: $($certificate.Thumbprint)" -ForegroundColor Green
Write-Host "Subject: $($certificate.Subject)" -ForegroundColor Green
Write-Host "Public certificate: $publicCertificatePath" -ForegroundColor Green
Write-Host "The private key remains in Cert:\CurrentUser\My and is not stored in the repository." -ForegroundColor Green
if ($previousCertificate) {
    Write-Host "Previous certificate retained for rollback: $($previousCertificate.Thumbprint) ($($previousCertificate.Subject))" -ForegroundColor Yellow
}
