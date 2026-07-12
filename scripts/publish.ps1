[CmdletBinding()]
param(
    [ValidateSet("Folder", "Msix", "All")][string]$Mode = "All",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [ValidateSet("win-x64")][string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "1.0.0",
    [string]$OutputRoot = "",
    [switch]$Clean,
    [switch]$NoZip,
    [switch]$AllowUnsigned,
    [string]$Publisher = "",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    $displayArguments = [Collections.Generic.List[string]]::new()
    $redactNext = $false
    foreach ($argument in $Arguments) {
        if ($redactNext) {
            $displayArguments.Add("<redacted>")
            $redactNext = $false
            continue
        }
        $displayArguments.Add($argument)
        if ($argument -in "/p", "-p" -or $argument -match "(?i)password") {
            $redactNext = $true
        }
    }
    Write-Host ">> $FilePath $($displayArguments -join ' ')" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE." }
}

function Convert-ToPackageVersion([string]$Value) {
    $parts = @($Value.Split("."))
    if ($parts.Count -gt 4 -or ($parts | Where-Object { $_ -notmatch "^\d+$" })) {
        throw "Version must contain one to four numeric parts."
    }
    while ($parts.Count -lt 4) { $parts += "0" }
    return (($parts | ForEach-Object { [int]$_ }) -join ".")
}

function Assert-OutputInsideRepository([string]$Repository, [string]$Output) {
    $repoPath = [IO.Path]::GetFullPath($Repository).TrimEnd('\') + '\'
    $outputPath = [IO.Path]::GetFullPath($Output)
    if (-not $outputPath.StartsWith($repoPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputRoot must stay inside the repository."
    }
    return $outputPath
}

function Find-SignTool {
    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
        (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools")
    ) | Where-Object { Test-Path $_ }
    $tool = $roots |
        ForEach-Object { Get-ChildItem -Path $_ -Recurse -Filter "signtool.exe" -File } |
        Where-Object { $_.Directory.Name -eq "x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "SignTool.exe was not found in the Windows SDK." }
    return $tool.FullName
}

function Invoke-SignFile([string]$Path) {
    $arguments = @("sign", "/v", "/fd", "SHA256", "/td", "SHA256", "/tr", $TimestampUrl)
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        $arguments += @("/f", $CertificatePath)
        if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
            $arguments += @("/p", $CertificatePassword)
        }
    } else {
        $arguments += @("/sha1", $CertificateThumbprint)
    }
    $arguments += $Path
    Invoke-Checked $script:signToolPath $arguments
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\DeskSnapshot\DeskSnapshot.csproj"
$manifestPath = Join-Path $repoRoot "src\DeskSnapshot\Package.appxmanifest"
$packageVersion = Convert-ToPackageVersion $Version
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release\DeskSnapshot-$Version-$RuntimeIdentifier"
}
$OutputRoot = Assert-OutputInsideRepository $repoRoot $OutputRoot

$signingConfigPath = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "DeskSnapshot\Signing\certificate.json"
if ([string]::IsNullOrWhiteSpace($CertificatePath) -and
    [string]::IsNullOrWhiteSpace($CertificateThumbprint) -and
    (Test-Path $signingConfigPath)) {
    $signingConfig = Get-Content -LiteralPath $signingConfigPath -Raw | ConvertFrom-Json
    $CertificateThumbprint = $signingConfig.thumbprint
}

$signingCertificate = $null
$disposeSigningCertificate = $false
if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
    if (-not (Test-Path $CertificatePath)) { throw "Signing certificate was not found: $CertificatePath" }
    $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $signingCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $CertificatePath,
        $CertificatePassword,
        $flags)
    $disposeSigningCertificate = $true
} elseif (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $CertificateThumbprint = $CertificateThumbprint.Replace(" ", "").ToUpperInvariant()
    $signingCertificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
}

try {
    if (-not $AllowUnsigned) {
        if (-not $signingCertificate -or -not $signingCertificate.HasPrivateKey) {
            throw "A reusable signing certificate with a private key is required. Run scripts/initialize-signing-certificate.ps1 once, or pass -CertificatePath. Use -AllowUnsigned only for explicit development builds."
        }
        if ($signingCertificate.NotAfter -le (Get-Date)) { throw "The signing certificate has expired." }
        if (-not [string]::IsNullOrWhiteSpace($Publisher) -and
            -not [string]::Equals($Publisher.Trim(), $signingCertificate.Subject, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Publisher must exactly match the signing certificate subject: $($signingCertificate.Subject)"
        }
        $Publisher = $signingCertificate.Subject
        $script:signToolPath = Find-SignTool
    }

    if ($Clean -and (Test-Path $OutputRoot)) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
    $folderOutput = Join-Path $OutputRoot "folder"
    $msixOutput = Join-Path $OutputRoot "msix"
    $publicCertificatePath = Join-Path $OutputRoot "DeskSnapshot-Signing.cer"

    if ($signingCertificate) {
        $certificateBytes = $signingCertificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
        [IO.File]::WriteAllBytes($publicCertificatePath, $certificateBytes)
    }

    if ($Mode -in "Folder", "All") {
        New-Item -ItemType Directory -Force -Path $folderOutput | Out-Null
        Invoke-Checked "dotnet" @(
            "publish", $projectPath,
            "--configuration", $Configuration,
            "--runtime", $RuntimeIdentifier,
            "--self-contained", "true",
            "-p:Version=$Version",
            "-p:PublishSingleFile=false",
            "-p:PublishDir=$folderOutput\"
        )
        if (-not $AllowUnsigned) {
            Invoke-SignFile (Join-Path $folderOutput "DeskSnapshot.exe")
            Invoke-SignFile (Join-Path $folderOutput "DeskSnapshot.dll")
            Copy-Item -LiteralPath $publicCertificatePath -Destination (Join-Path $folderOutput "DeskSnapshot-Signing.cer") -Force
        }
        if (-not $NoZip) {
            $zipPath = Join-Path $OutputRoot "DeskSnapshot-$Version-$RuntimeIdentifier-portable.zip"
            if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
            Compress-Archive -Path (Join-Path $folderOutput "*") -DestinationPath $zipPath -Force
        }
    }

    if ($Mode -in "Msix", "All") {
        New-Item -ItemType Directory -Force -Path $msixOutput | Out-Null
        $arguments = @(
            "msbuild", $projectPath,
            "/restore", "/t:Publish",
            "/p:Configuration=$Configuration",
            "/p:Platform=x64",
            "/p:RuntimeIdentifier=$RuntimeIdentifier",
            "/p:Version=$Version",
            "/p:AppxPackageVersion=$packageVersion",
            "/p:GenerateAppxPackageOnBuild=true",
            "/p:UapAppxPackageBuildMode=SideloadOnly",
            "/p:AppxBundle=Never",
            "/p:AppxSymbolPackageEnabled=false",
            "/p:AppxPackageDir=$msixOutput\",
            "/p:AppxPackageSigningEnabled=false"
        )

        $originalManifest = [IO.File]::ReadAllText($manifestPath)
        try {
            [xml]$manifest = $originalManifest
            $manifest.Package.Identity.Version = $packageVersion
            if (-not [string]::IsNullOrWhiteSpace($Publisher)) {
                $manifest.Package.Identity.Publisher = $Publisher.Trim()
            }
            $manifest.Save($manifestPath)
            Invoke-Checked "dotnet" $arguments
        } finally {
            [IO.File]::WriteAllText($manifestPath, $originalManifest, [Text.UTF8Encoding]::new($false))
        }

        $msixFiles = @(Get-ChildItem $msixOutput -Recurse -Filter "*.msix" -File)
        if ($msixFiles.Count -ne 1) {
            throw "Expected exactly one MSIX package, found $($msixFiles.Count)."
        }
        if (-not $AllowUnsigned) {
            Invoke-SignFile $msixFiles[0].FullName
            Copy-Item -LiteralPath $publicCertificatePath -Destination (Join-Path $msixOutput "DeskSnapshot-Signing.cer") -Force
        }
    }

    Write-Host "Release artifacts: $OutputRoot" -ForegroundColor Green
    if ($signingCertificate) {
        Write-Host "Signing certificate: $($signingCertificate.Thumbprint) ($($signingCertificate.Subject))" -ForegroundColor Green
    }
} finally {
    if ($disposeSigningCertificate -and $signingCertificate) { $signingCertificate.Dispose() }
}
