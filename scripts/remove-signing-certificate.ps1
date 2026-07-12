[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern("^[A-Fa-f0-9]{40}$")][string]$Thumbprint
)

$ErrorActionPreference = "Stop"
$store = [Security.Cryptography.X509Certificates.X509Store]::new(
    [Security.Cryptography.X509Certificates.StoreName]::My,
    [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
try {
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $matches = $store.Certificates.Find(
        [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
        $Thumbprint,
        $false)
    foreach ($certificate in $matches) { $store.Remove($certificate) }
} finally {
    $store.Close()
}
