[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern("^\d+(\.\d+){2,3}$")][string]$Version,
    [ValidateSet("win-x64")][string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "",
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
function Resolve-PathInsideRepository([string]$Path) {
    $repository = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($repository, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release paths must stay inside the repository: $resolved"
    }
    return $resolved
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release\DeskSnapshot-$Version-$RuntimeIdentifier"
}
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repoRoot "artifacts\release-assets"
}
$OutputRoot = Resolve-PathInsideRepository $OutputRoot
$Destination = Resolve-PathInsideRepository $Destination

$folderOutput = Join-Path $OutputRoot "folder"
$msixOutput = Join-Path $OutputRoot "msix"
if (-not (Test-Path $folderOutput)) { throw "Portable output was not found: $folderOutput" }

$msixFiles = @(Get-ChildItem -Path $msixOutput -Recurse -Filter "*.msix" -File)
if ($msixFiles.Count -ne 1) {
    throw "Expected exactly one MSIX package, found $($msixFiles.Count)."
}
$publicCertificatePath = Join-Path $OutputRoot "DeskSnapshot-Signing.cer"
if (-not (Test-Path $publicCertificatePath)) {
    throw "The public signing certificate was not found: $publicCertificatePath"
}

if (Test-Path $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $folderOutput -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $folderOutput -Force

$portableName = "DeskSnapshot-$Version-$RuntimeIdentifier-portable.zip"
$msixName = "DeskSnapshot-$Version-$RuntimeIdentifier.msix"
$portablePath = Join-Path $Destination $portableName
$releaseMsixPath = Join-Path $Destination $msixName
Compress-Archive -Path (Join-Path $folderOutput "*") -DestinationPath $portablePath -CompressionLevel Optimal
Copy-Item -LiteralPath $msixFiles[0].FullName -Destination $releaseMsixPath -Force
$releaseCertificatePath = Join-Path $Destination "DeskSnapshot-Signing.cer"
Copy-Item -LiteralPath $publicCertificatePath -Destination $releaseCertificatePath -Force

$checksumPath = Join-Path $Destination "SHA256SUMS.txt"
$checksumLines = @($portablePath, $releaseMsixPath, $releaseCertificatePath) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"
}
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

Write-Host "Prepared release assets in $Destination" -ForegroundColor Green
