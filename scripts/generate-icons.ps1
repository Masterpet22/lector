Add-Type -AssemblyName System.Drawing

$outputDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'public\icons'
foreach ($size in 192, 512) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#0b1020'))
    $margin = [int]($size * 0.18)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#70e1c1'))
    $graphics.FillEllipse($brush, $margin, $margin, $size - 2 * $margin, $size - 2 * $margin)
    $font = New-Object System.Drawing.Font('Segoe UI', ($size * 0.42), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#0b1020'))
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString('N', $font, $textBrush, (New-Object System.Drawing.RectangleF(0, 0, $size, $size)), $format)
    $bitmap.Save((Join-Path $outputDirectory "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $format.Dispose(); $textBrush.Dispose(); $font.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
