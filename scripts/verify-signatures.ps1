[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PortableFolder,
    [Parameter(Mandatory = $true)][string]$MsixFolder,
    [string]$TrustCertificatePath = "",
    [ValidateRange(10, 300)][int]$VerificationTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

function Invoke-SignatureVerification {
    param(
        [Parameter(Mandatory = $true)][string]$SignToolPath,
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $stdoutPath = [IO.Path]::GetTempFileName()
    $stderrPath = [IO.Path]::GetTempFileName()
    $process = $null
    try {
        Write-Host "Verifying signature: $FilePath"
        $process = Start-Process `
            -FilePath $SignToolPath `
            -ArgumentList @("verify", "/pa", "/all", "/v", "`"$FilePath`"") `
            -NoNewWindow `
            -PassThru `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath

        if (-not $process.WaitForExit($VerificationTimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Signature verification timed out after $VerificationTimeoutSeconds seconds: $FilePath"
        }
        $process.WaitForExit()

        $stdout = Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
        $stderr = Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host $stdout.TrimEnd() }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Host $stderr.TrimEnd() }
        if ($process.ExitCode -ne 0) { throw "Signature verification failed: $FilePath" }
    } finally {
        if ($process) { $process.Dispose() }
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

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
Write-Host "Using SignTool: $($signTool.FullName)"

$expectedThumbprint = ""
$importedThumbprint = ""
try {
    if (-not [string]::IsNullOrWhiteSpace($TrustCertificatePath)) {
        $TrustCertificatePath = [IO.Path]::GetFullPath($TrustCertificatePath)
        if (-not (Test-Path $TrustCertificatePath)) { throw "Public signing certificate was not found." }
        $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($TrustCertificatePath)
        $expectedThumbprint = $publicCertificate.Thumbprint
        $store = [Security.Cryptography.X509Certificates.X509Store]::new(
            [Security.Cryptography.X509Certificates.StoreName]::TrustedPeople,
            [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        try {
            $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $existing = $store.Certificates.Find(
                [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $publicCertificate.Thumbprint,
                $false)
            if ($existing.Count -eq 0) {
                Write-Host "Temporarily trusting signing certificate in CurrentUser\TrustedPeople: $($publicCertificate.Thumbprint)"
                $store.Add($publicCertificate)
                $importedThumbprint = $publicCertificate.Thumbprint
            } else {
                Write-Host "Signing certificate is already trusted in CurrentUser\TrustedPeople: $($publicCertificate.Thumbprint)"
            }
        } finally {
            $store.Close()
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
        $fullPath = [IO.Path]::GetFullPath($file)
        Invoke-SignatureVerification -SignToolPath $signTool.FullName -FilePath $fullPath
        if (-not [string]::IsNullOrWhiteSpace($expectedThumbprint)) {
            $signature = Get-AuthenticodeSignature -LiteralPath $fullPath
            $actualThumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { "" }
            if (-not [string]::Equals($actualThumbprint, $expectedThumbprint, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unexpected signing certificate for $fullPath. Expected $expectedThumbprint, found $actualThumbprint."
            }
        }
    }

    Write-Host "Verified Authenticode signatures for $($files.Count) release files." -ForegroundColor Green
} finally {
    if (-not [string]::IsNullOrWhiteSpace($importedThumbprint)) {
        $store = [Security.Cryptography.X509Certificates.X509Store]::new(
            [Security.Cryptography.X509Certificates.StoreName]::TrustedPeople,
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
