[CmdletBinding()]
param(
    [ValidateSet("Folder", "Msix", "All")][string]$Mode = "All",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [ValidateSet("win-x64")][string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "1.0.0",
    [string]$OutputRoot = "",
    [switch]$Clean,
    [switch]$NoZip,
    [switch]$CreateTestCertificate,
    [string]$CertificateSubject = "CN=FeranyDev",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    Write-Host ">> $FilePath $($Arguments -join ' ')" -ForegroundColor Cyan
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
    $repoPath = [IO.Path]::GetFullPath($Repository)
    $outputPath = [IO.Path]::GetFullPath($Output)
    if (-not $outputPath.StartsWith($repoPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputRoot must stay inside the repository."
    }
    return $outputPath
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\DeskSnapshot\DeskSnapshot.csproj"
$manifestPath = Join-Path $repoRoot "src\DeskSnapshot\Package.appxmanifest"
$packageVersion = Convert-ToPackageVersion $Version
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release\DeskSnapshot-$Version-$RuntimeIdentifier"
}
$OutputRoot = Assert-OutputInsideRepository $repoRoot $OutputRoot

if ($Clean -and (Test-Path $OutputRoot)) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$folderOutput = Join-Path $OutputRoot "folder"
$msixOutput = Join-Path $OutputRoot "msix"

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
    if (-not $NoZip) {
        $zipPath = Join-Path $OutputRoot "DeskSnapshot-$Version-$RuntimeIdentifier-portable.zip"
        if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        Compress-Archive -Path (Join-Path $folderOutput "*") -DestinationPath $zipPath -Force
    }
}

if ($Mode -in "Msix", "All") {
    New-Item -ItemType Directory -Force -Path $msixOutput | Out-Null
    $temporaryCertificate = $null
    $certificateThumbprint = ""
    $testCertificatePath = ""
    if ($CreateTestCertificate) {
        $temporaryCertificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $CertificateSubject `
            -KeyUsage DigitalSignature `
            -FriendlyName "DeskSnapshot test signing certificate" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
            -NotAfter (Get-Date).AddYears(3)
        $certificateThumbprint = $temporaryCertificate.Thumbprint
        $certificateDirectory = Join-Path $OutputRoot "certificates"
        New-Item -ItemType Directory -Force -Path $certificateDirectory | Out-Null
        $testCertificatePath = Join-Path $certificateDirectory "DeskSnapshot-TestSigning.cer"
        Export-Certificate -Cert $temporaryCertificate -FilePath $testCertificatePath | Out-Null
    }

    $signingEnabled = $CreateTestCertificate -or -not [string]::IsNullOrWhiteSpace($CertificatePath)
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
        "/p:AppxPackageSigningEnabled=$signingEnabled"
    )
    if ($CreateTestCertificate) { $arguments += "/p:PackageCertificateThumbprint=$certificateThumbprint" }
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) { $arguments += "/p:PackageCertificateKeyFile=$CertificatePath" }
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) { $arguments += "/p:PackageCertificatePassword=$CertificatePassword" }

    $originalManifest = [IO.File]::ReadAllText($manifestPath)
    try {
        [xml]$manifest = $originalManifest
        $manifest.Package.Identity.Version = $packageVersion
        $manifest.Save($manifestPath)
        Invoke-Checked "dotnet" $arguments
        if (-not [string]::IsNullOrWhiteSpace($testCertificatePath) -and (Test-Path $testCertificatePath)) {
            Copy-Item -LiteralPath $testCertificatePath -Destination (Join-Path $msixOutput "DeskSnapshot-TestSigning.cer") -Force
            Get-ChildItem -Path $msixOutput -Recurse -Filter "Add-AppDevPackage.ps1" -File |
                ForEach-Object {
                    Copy-Item -LiteralPath $testCertificatePath -Destination (Join-Path $_.DirectoryName "DeskSnapshot-TestSigning.cer") -Force
                }
        }
    }
    finally {
        [IO.File]::WriteAllText($manifestPath, $originalManifest, [Text.UTF8Encoding]::new($false))
        if ($temporaryCertificate) {
            Remove-Item -LiteralPath "Cert:\CurrentUser\My\$certificateThumbprint" -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Get-ChildItem $msixOutput -Recurse -Filter "*.msix" -File)) {
        throw "MSIX build completed without producing a .msix file."
    }
}

Write-Host "Release artifacts: $OutputRoot" -ForegroundColor Green
