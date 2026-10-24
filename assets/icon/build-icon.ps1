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

function Fill-Polygon([System.Drawing.Graphics]$graphics, [System.Drawing.Brush]$brush, [float[]]$coordinates, [int]$size, [bool]$pixelSnap) {
    $points = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    for ($index = 0; $index -lt $coordinates.Length; $index += 2) {
        $x = $coordinates[$index] * $size / 256.0
        $y = $coordinates[$index + 1] * $size / 256.0
        if ($pixelSnap) {
            $x = [Math]::Round($x)
            $y = [Math]::Round($y)
        }
        $points.Add((New-Object System.Drawing.PointF([single]$x, [single]$y)))
    }
    $graphics.FillPolygon($brush, $points.ToArray())
}

function Draw-Tile([System.Drawing.Graphics]$graphics, [System.Drawing.Brush]$brush, [int]$size) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $radius = 44.0 * $size / 256.0
    if ($size -le 32) { $radius = [Math]::Max(2, [Math]::Round($radius)) }
    $diameter = [single]($radius * 2)
    $edge = [single]$size
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($edge - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($edge - $diameter, $edge - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $edge - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($brush, $path)
    $path.Dispose()
}

function Draw-Icon([System.Drawing.Graphics]$graphics, [int]$size) {
    $blue = New-Object System.Drawing.SolidBrush (New-Color 31 111 235)
    $white = New-Object System.Drawing.SolidBrush (New-Color 255 255 255)
    $navy = New-Object System.Drawing.SolidBrush (New-Color 20 33 61)
    $pixelSnap = $size -le 32

    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Draw-Tile $graphics $blue $size

    if ($size -le 32) {
        # Pixel-snapped white W silhouette, matching option D's small-size mark.
        Fill-Polygon $graphics $white ([float[]]@(43,60,65,60,84,151,110,95,128,95,153,151,173,60,195,60,166,199,143,199,119,140,96,199,73,199)) $size $true
    }
    else {
        # Geometric WCX paths from option D; no font rendering is used.
        Fill-Polygon $graphics $white ([float[]]@(15,86,31,86,42,145,56,105,69,105,83,145,95,86,111,86,93,178,77,178,62,139,47,178,31,178)) $size $false
        Fill-Polygon $graphics $white ([float[]]@(119,96,131,84,162,84,173,97,173,111,157,111,157,102,132,102,132,160,157,160,157,150,173,150,173,166,162,178,131,178,119,165)) $size $false
        Fill-Polygon $graphics $navy ([float[]]@(183,84,199,84,240,174,224,174)) $size $false
        Fill-Polygon $graphics $navy ([float[]]@(224,84,240,84,198,174,182,174)) $size $false
    }

    $blue.Dispose(); $white.Dispose(); $navy.Dispose()
}

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
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

# Side-by-side preview: native 16, 32 and 48 px icons enlarged 4x with nearest-neighbor sampling.
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
