[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PortableFolder,
    [Parameter(Mandatory = $true)][string]$MsixFolder,
    [string]$TrustCertificatePath = "",
    [ValidateRange(10, 300)][int]$VerificationTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

function Invoke-SignToolVerification {
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

        [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
            StandardError = Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
        }
    } finally {
        if ($process) { $process.Dispose() }
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Write-SignToolOutput {
    param([Parameter(Mandatory = $true)]$Result)

    if (-not [string]::IsNullOrWhiteSpace($Result.StandardOutput)) {
        Write-Host $Result.StandardOutput.TrimEnd()
    }
    if (-not [string]::IsNullOrWhiteSpace($Result.StandardError)) {
        Write-Host $Result.StandardError.TrimEnd()
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
$expectedCertificateIsSelfSigned = $false
if (-not [string]::IsNullOrWhiteSpace($TrustCertificatePath)) {
    $TrustCertificatePath = [IO.Path]::GetFullPath($TrustCertificatePath)
    if (-not (Test-Path $TrustCertificatePath)) { throw "Public signing certificate was not found." }
    $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($TrustCertificatePath)
    try {
        $expectedThumbprint = $publicCertificate.Thumbprint
        $expectedCertificateIsSelfSigned = [string]::Equals(
            $publicCertificate.Subject,
            $publicCertificate.Issuer,
            [StringComparison]::OrdinalIgnoreCase)
        Write-Host "Expected signing certificate: $expectedThumbprint ($($publicCertificate.Subject))"
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
    $fullPath = [IO.Path]::GetFullPath($file)
    $signature = Get-AuthenticodeSignature -LiteralPath $fullPath
    $actualThumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { "" }

    if (-not [string]::IsNullOrWhiteSpace($expectedThumbprint) -and
        -not [string]::Equals($actualThumbprint, $expectedThumbprint, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected signing certificate for $fullPath. Expected $expectedThumbprint, found $actualThumbprint."
    }

    $result = Invoke-SignToolVerification -SignToolPath $signTool.FullName -FilePath $fullPath
    if ($result.ExitCode -eq 0) {
        Write-SignToolOutput -Result $result
        continue
    }

    $signToolDetails = "$($result.StandardOutput)`n$($result.StandardError)"
    $untrustedRootPattern = "(?i)certificate chain processed.+terminated in a root\s+certificate which is not trusted"
    # Windows PowerShell maps CERT_E_UNTRUSTEDROOT to NotTrusted on some systems
    # and UnknownError on others. SignTool's exact error is checked separately.
    $authenticodeHasOnlyTrustFailure =
        $signature.Status -eq [Management.Automation.SignatureStatus]::NotTrusted -or
        $signature.Status -eq [Management.Automation.SignatureStatus]::UnknownError
    $signToolHasOnlyTrustFailure = $signToolDetails -match $untrustedRootPattern
    $allowExpectedSelfSignedRoot =
        -not [string]::IsNullOrWhiteSpace($expectedThumbprint) -and
        $expectedCertificateIsSelfSigned -and
        $authenticodeHasOnlyTrustFailure -and
        $signToolHasOnlyTrustFailure

    if (-not $allowExpectedSelfSignedRoot) {
        Write-SignToolOutput -Result $result
        throw "Signature verification failed: $fullPath"
    }

    Write-Warning "The signature and file hash match the expected self-signed certificate; the ephemeral runner does not trust that certificate as a system root."
}

Write-Host "Verified Authenticode signatures for $($files.Count) release files." -ForegroundColor Green
