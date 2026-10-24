[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = $PSScriptRoot
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$pngs = @{}

function New-Color([int]$r, [int]$g, [int]$b) {
    return [System.Drawing.Color]::FromArgb(255, $r, $g, $b)
}

function Draw-RoundedRectangle($graphics, $brush, [single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($brush, $path)
    $path.Dispose()
}

function Draw-Icon([System.Drawing.Graphics]$graphics, [int]$size) {
    $navy = New-Object System.Drawing.SolidBrush (New-Color 20 33 61)
    $blue = New-Object System.Drawing.SolidBrush (New-Color 31 111 235)
    $white = New-Object System.Drawing.SolidBrush (New-Color 255 255 255)
    $pen = New-Object System.Drawing.Pen ((New-Color 255 255 255), 8.0)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    if ($size -le 24) {
        # Compact silhouette and a thick down-right copy arrow stay legible at small sizes.
        $smallScale = $size / 16.0
        $graphics.ScaleTransform($smallScale, $smallScale)
        Draw-RoundedRectangle $graphics $navy 1 1 14 14 2
        $graphics.FillRectangle($white, 4, 4, 8, 5)
        $graphics.FillRectangle($navy, 5, 5, 6, 3)
        $graphics.FillRectangle($blue, 5, 7, 8, 6)
        $arrowPen = New-Object System.Drawing.Pen ((New-Color 255 255 255), 1.8)
        $arrowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($arrowPen, 6, 5, 10, 9)
        $arrowHead = New-Object System.Drawing.Drawing2D.GraphicsPath
        $arrowHead.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(8.1, 8.8)),
            (New-Object System.Drawing.PointF(10.4, 10.2)),
            (New-Object System.Drawing.PointF(10.1, 7.5))
        ))
        $graphics.FillPath($white, $arrowHead)
        $arrowHead.Dispose(); $arrowPen.Dispose()
        $graphics.ResetTransform()
    }
    else {
        $scale = $size / 256.0
        $graphics.ScaleTransform($scale, $scale)
        Draw-RoundedRectangle $graphics $navy 12 12 232 232 52

        $backPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $backPath.AddArc(48, 57, 40, 40, 180, 90)
        $backPath.AddArc(153, 57, 40, 40, 270, 90)
        $backPath.AddArc(153, 108, 40, 40, 0, 90)
        $backPath.AddArc(48, 108, 40, 40, 90, 90)
        $backPath.CloseFigure()
        $graphics.FillPath($navy, $backPath)
        $graphics.DrawPath($pen, $backPath)
        $backPath.Dispose()
        Draw-RoundedRectangle $graphics $blue 64 91 145 99 20
        # Outline the front drive with a rounded path.
        $frontPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $frontPath.AddArc(64, 91, 40, 40, 180, 90)
        $frontPath.AddArc(169, 91, 40, 40, 270, 90)
        $frontPath.AddArc(169, 150, 40, 40, 0, 90)
        $frontPath.AddArc(64, 150, 40, 40, 90, 90)
        $frontPath.CloseFigure()
        $graphics.DrawPath($pen, $frontPath)
        $frontPath.Dispose()
        $graphics.DrawLine($pen, 83, 158, 189, 158)
        $graphics.FillEllipse($white, 183, 169, 10, 10)

        $graphics.DrawLine($pen, 91, 81, 151, 141)
        $arrowHead = New-Object System.Drawing.Drawing2D.GraphicsPath
        $arrowHead.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(128, 137)),
            (New-Object System.Drawing.PointF(153, 147)),
            (New-Object System.Drawing.PointF(147, 122))
        ))
        $graphics.FillPath($white, $arrowHead)
        $arrowHead.Dispose()
        $graphics.ResetTransform()
    }

    $pen.Dispose(); $navy.Dispose(); $blue.Dispose(); $white.Dispose()
}

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    Draw-Icon $graphics $size
    $graphics.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$size] = $stream.ToArray()
    if ($size -eq 256) {
        [System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'winclonerx-256.png'), $pngs[$size])
    }
    $stream.Dispose(); $bitmap.Dispose()
}

# ICO directory header + PNG payloads (supported by Windows Vista and later).
$entryCount = $sizes.Count
$directoryLength = 6 + (16 * $entryCount)
$iconStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($iconStream)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$entryCount)
$offset = $directoryLength
foreach ($size in $sizes) {
    $dimension = if ($size -eq 256) { [byte]0 } else { [byte]$size }
    $writer.Write($dimension); $writer.Write($dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngs[$size].Length); $writer.Write([UInt32]$offset)
    $offset += $pngs[$size].Length
}
foreach ($size in $sizes) { $writer.Write($pngs[$size]) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'winclonerx.ico'), $iconStream.ToArray())
$writer.Dispose(); $iconStream.Dispose()

# Side-by-side review sheet: 16, 32 and 48 px icons enlarged 4x with nearest-neighbor sampling.
$preview = New-Object System.Drawing.Bitmap(384, 192, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$previewGraphics = [System.Drawing.Graphics]::FromImage($preview)
$previewGraphics.Clear([System.Drawing.Color]::Transparent)
$previewGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$previewGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$previewX = 0
foreach ($previewSize in @(16, 32, 48)) {
    $previewStream = New-Object System.IO.MemoryStream(, $pngs[$previewSize])
    $previewBitmap = [System.Drawing.Bitmap]::FromStream($previewStream)
    $scaledSize = $previewSize * 4
    $previewY = (192 - $scaledSize) / 2
    $previewGraphics.DrawImage($previewBitmap, $previewX, $previewY, $scaledSize, $scaledSize)
    $previewX += $scaledSize
    $previewBitmap.Dispose(); $previewStream.Dispose()
}
$preview.Save((Join-Path $assetDirectory 'preview-16-32-48.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$previewGraphics.Dispose(); $preview.Dispose()

Write-Output "Generated winclonerx.ico (sizes: $($sizes -join ', ')), winclonerx-256.png, and preview-16-32-48.png using System.Drawing/GDI+."
