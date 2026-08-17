Add-Type -AssemblyName System.Drawing

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourcePath = Join-Path $projectRoot 'logo.png'
$outputDirectory = Join-Path $projectRoot 'public\icons'
$brandingDirectory = Join-Path $projectRoot 'public\branding'
$source = [System.Drawing.Image]::FromFile($sourcePath)

function Write-NebulaIcon([int]$size, [double]$scale, [string]$fileName) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#070912'))
    $targetSize = [int]($size * $scale)
    $offset = [int](($size - $targetSize) / 2)
    $graphics.DrawImage($source, $offset, $offset, $targetSize, $targetSize)
    $bitmap.Save((Join-Path $outputDirectory $fileName), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

New-Item -ItemType Directory -Force -Path $outputDirectory,$brandingDirectory | Out-Null
Write-NebulaIcon 192 0.96 'icon-192.png'
Write-NebulaIcon 512 0.96 'icon-512.png'
Write-NebulaIcon 512 0.78 'icon-maskable-512.png'
Write-NebulaIcon 180 0.96 'apple-touch-icon.png'
Write-NebulaIcon 256 0.96 '..\branding\logo.png'
$source.Dispose()
