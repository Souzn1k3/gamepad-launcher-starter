param(
    [Parameter(Mandatory = $true)]
    [string]$OutPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }
    if ($d -lt 1) { $d = 1 }
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-IconImage([int]$size, $images) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 22, 24, 30))
    $foreground = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 124, 255, 107))
    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 22, 24, 30))

    $sizeSingle = [single]$size
    $backgroundPath = New-RoundedPath 0 0 $sizeSingle $sizeSingle ($sizeSingle * 0.22)
    $graphics.FillPath($background, $backgroundPath)
    $backgroundPath.Dispose()

    $bodyPath = New-RoundedPath ($sizeSingle * 0.12) ($sizeSingle * 0.33) ($sizeSingle * 0.76) ($sizeSingle * 0.34) ($sizeSingle * 0.13)
    $graphics.FillPath($foreground, $bodyPath)
    $bodyPath.Dispose()

    $graphics.FillEllipse($foreground, $sizeSingle * 0.07, $sizeSingle * 0.39, $sizeSingle * 0.30, $sizeSingle * 0.30)
    $graphics.FillEllipse($foreground, $sizeSingle * 0.63, $sizeSingle * 0.39, $sizeSingle * 0.30, $sizeSingle * 0.30)

    $graphics.FillRectangle($dark, $sizeSingle * 0.215, $sizeSingle * 0.487, $sizeSingle * 0.13, $sizeSingle * 0.055)
    $graphics.FillRectangle($dark, $sizeSingle * 0.2575, $sizeSingle * 0.4445, $sizeSingle * 0.055, $sizeSingle * 0.13)

    $graphics.FillEllipse($dark, $sizeSingle * 0.63, $sizeSingle * 0.44, $sizeSingle * 0.055, $sizeSingle * 0.055)
    $graphics.FillEllipse($dark, $sizeSingle * 0.705, $sizeSingle * 0.475, $sizeSingle * 0.055, $sizeSingle * 0.055)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()

    $stream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    $background.Dispose()
    $foreground.Dispose()
    $dark.Dispose()

    [void]$images.Add($bytes)
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.ArrayList
foreach ($size in $sizes) {
    Add-IconImage -size $size -images $images
}

$directory = Split-Path -Parent $OutPath
if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $bytes = $images[$i]
    $dimension = 0
    if ($size -lt 256) { $dimension = $size }
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($image in $images) {
    $writer.Write([Byte[]]$image)
}
$writer.Flush()
[System.IO.File]::WriteAllBytes($OutPath, $stream.ToArray())
$writer.Close()
$stream.Close()

$icon = New-Object System.Drawing.Icon -ArgumentList $OutPath
$icon.Dispose()
Write-Host "Icon written: $OutPath"
