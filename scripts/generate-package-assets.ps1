[CmdletBinding()]
param(
    [string]$SourcePath = "",
    [string]$ProjectPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "src\DeskSnapshot"
}
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $designSource = Join-Path $repoRoot "desksnapshot_pack\DeskSnapshot_source_1254x1254.png"
    $SourcePath = if (Test-Path $designSource) {
        $designSource
    } else {
        Join-Path $ProjectPath "Assets\DeskSnapshot_96x96.png"
    }
}

Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $ProjectPath "Assets"
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePath))

function New-PackageAsset {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height,
        [Parameter(Mandatory = $true)][int]$IconSize
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $x = [int](($Width - $IconSize) / 2)
        $y = [int](($Height - $IconSize) / 2)
        $graphics.DrawImage($source, $x, $y, $IconSize, $IconSize)
        $bitmap.Save((Join-Path $assetDirectory $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try {
    New-PackageAsset "Square44x44Logo.scale-200.png" 88 88 88
    New-PackageAsset "Square44x44Logo.targetsize-24_altform-unplated.png" 24 24 24
    New-PackageAsset "Square44x44Logo.targetsize-48_altform-unplated.png" 48 48 48
    New-PackageAsset "Square150x150Logo.scale-200.png" 300 300 300
    New-PackageAsset "StoreLogo.png" 50 50 50
    New-PackageAsset "Wide310x150Logo.scale-200.png" 620 300 220
    New-PackageAsset "SplashScreen.scale-200.png" 1240 600 240
}
finally {
    $source.Dispose()
}

Write-Host "Package assets generated in $assetDirectory" -ForegroundColor Green
