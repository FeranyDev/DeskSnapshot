[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PortableFolder,
    [Parameter(Mandatory = $true)][string]$MsixFolder,
    [string]$TrustCertificatePath = ""
)

$ErrorActionPreference = "Stop"
$roots = @(
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
    (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools")
) | Where-Object { Test-Path $_ }
$signTool = $roots |
    ForEach-Object { Get-ChildItem -Path $_ -Recurse -Filter "signtool.exe" -File } |
    Where-Object { $_.Directory.Name -eq "x64" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signTool) { throw "SignTool.exe was not found in the Windows SDK." }

$importedThumbprint = ""
try {
    if (-not [string]::IsNullOrWhiteSpace($TrustCertificatePath)) {
        $TrustCertificatePath = [IO.Path]::GetFullPath($TrustCertificatePath)
        if (-not (Test-Path $TrustCertificatePath)) { throw "Public signing certificate was not found." }
        $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($TrustCertificatePath)
        try {
            $existing = Get-Item -LiteralPath "Cert:\CurrentUser\Root\$($publicCertificate.Thumbprint)" -ErrorAction SilentlyContinue
            if (-not $existing) {
                Import-Certificate -FilePath $TrustCertificatePath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
                $importedThumbprint = $publicCertificate.Thumbprint
            }
        } finally {
            $publicCertificate.Dispose()
        }
    }

    $files = @(
        Join-Path $PortableFolder "DeskSnapshot.exe"
        Join-Path $PortableFolder "DeskSnapshot.dll"
    )
    $files += @(Get-ChildItem -Path $MsixFolder -Recurse -Filter "*.msix" -File | Select-Object -ExpandProperty FullName)
    foreach ($file in $files) {
        if (-not (Test-Path $file)) { throw "Signed output was not found: $file" }
        & $signTool.FullName verify /pa /all /v $file
        if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $file" }
    }

    Write-Host "Verified Authenticode signatures for $($files.Count) release files." -ForegroundColor Green
} finally {
    if (-not [string]::IsNullOrWhiteSpace($importedThumbprint)) {
        $store = [Security.Cryptography.X509Certificates.X509Store]::new(
            [Security.Cryptography.X509Certificates.StoreName]::Root,
            [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        try {
            $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $matches = $store.Certificates.Find(
                [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $importedThumbprint,
                $false)
            foreach ($certificate in $matches) { $store.Remove($certificate) }
        } finally {
            $store.Close()
        }
    }
}
