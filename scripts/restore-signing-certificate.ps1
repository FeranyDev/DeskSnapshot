[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"
$base64 = $env:DESKSNAPSHOT_SIGNING_CERTIFICATE_BASE64
$password = $env:DESKSNAPSHOT_SIGNING_CERTIFICATE_PASSWORD
$expectedThumbprint = $env:DESKSNAPSHOT_SIGNING_CERTIFICATE_THUMBPRINT
if ([string]::IsNullOrWhiteSpace($base64) -or
    [string]::IsNullOrWhiteSpace($password) -or
    [string]::IsNullOrWhiteSpace($expectedThumbprint)) {
    throw "The reusable signing certificate secrets are not configured."
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$allowedTempRoot = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [IO.Path]::GetFullPath($env:RUNNER_TEMP)
} else {
    [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
}
$allowedTempPrefix = $allowedTempRoot.TrimEnd('\') + '\'
if (-not $OutputPath.StartsWith($allowedTempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The temporary PFX must be restored under the runner's temporary directory."
}

$bytes = [Convert]::FromBase64String($base64)
$importedThumbprint = ""
try {
    [IO.File]::WriteAllBytes($OutputPath, $bytes)
    $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($OutputPath, $password, $flags)
    try {
        if (-not $certificate.HasPrivateKey) { throw "The restored certificate does not contain a private key." }
        if ($certificate.NotAfter -le (Get-Date)) { throw "The restored signing certificate has expired." }
        if (-not [string]::Equals(
            $certificate.Thumbprint,
            $expectedThumbprint.Replace(" ", ""),
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "The restored certificate thumbprint does not match the protected signing configuration."
        }
        $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
        $imported = Import-PfxCertificate `
            -FilePath $OutputPath `
            -Password $securePassword `
            -CertStoreLocation "Cert:\CurrentUser\My"
        $importedThumbprint = $imported.Thumbprint
        if (-not $imported.HasPrivateKey -or $imported.Thumbprint -ne $certificate.Thumbprint) {
            throw "The reusable signing certificate could not be imported safely."
        }
        if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
            "thumbprint=$($certificate.Thumbprint)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        }
        Write-Host "Reusable signing certificate imported: $($certificate.Thumbprint) ($($certificate.Subject))" -ForegroundColor Green
    } finally {
        $certificate.Dispose()
    }
} catch {
    if (-not [string]::IsNullOrWhiteSpace($importedThumbprint)) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$importedThumbprint" -Force -ErrorAction SilentlyContinue
    }
    throw
} finally {
    [Array]::Clear($bytes, 0, $bytes.Length)
    if (Test-Path $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
}
