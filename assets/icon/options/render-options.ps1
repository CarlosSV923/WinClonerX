# Windows PowerShell 5.1, System.Drawing only. Geometric paths below mirror the hand-written SVG sources.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$navy = [System.Drawing.ColorTranslator]::FromHtml('#14213D')
$blue = [System.Drawing.ColorTranslator]::FromHtml('#1F6FEB')
$white = [System.Drawing.Color]::White
$light = [System.Drawing.ColorTranslator]::FromHtml('#EEF2F7')

function New-Brush($color) { return New-Object System.Drawing.SolidBrush($color) }
function Fill-Poly($g, $brush, [float[]]$points) {
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    for ($i = 0; $i -lt $points.Length; $i += 2) { $pts.Add((New-Object System.Drawing.PointF($points[$i], $points[$i + 1]))) }
    $g.FillPolygon($brush, $pts.ToArray())
}
function Fill-Shape($g, $brush, $shape) { Fill-Poly $g $brush ([float[]]$shape) }
function New-TilePath($radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc(256 - $d, 0, $d, $d, 270, 90)
    $p.AddArc(256 - $d, 256 - $d, $d, $d, 0, 90); $p.AddArc(0, 256 - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}
function Draw-Icon($g, [string]$option, [bool]$small = $false) {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $bg = if ($option -eq 'D') { New-Brush $blue } else { New-Brush $navy }
    $g.FillPath($bg, (New-TilePath 44)); $bg.Dispose()
    $w = New-Brush $white; $b = New-Brush $blue; $n = New-Brush $navy
    if ($small) {
        if ($option -eq 'C') {
            Fill-Shape $g $b @(52,58,80,58,128,109,176,58,198,58,145,128,198,198,176,198,128,147,80,198,52,198,111,128)
        } else {
            Fill-Shape $g $w @(43,60,65,60,84,151,110,95,128,95,153,151,173,60,195,60,166,199,143,199,119,140,96,199,73,199)
        }
        $w.Dispose(); $b.Dispose(); $n.Dispose(); return
    }
    switch ($option) {
      'A' {
        Fill-Shape $g $w @(15,86,31,86,42,145,56,105,69,105,83,145,95,86,111,86,93,178,77,178,62,139,47,178,31,178)
        Fill-Shape $g $w @(119,96,131,84,162,84,173,97,173,111,157,111,157,102,132,102,132,160,157,160,157,150,173,150,173,166,162,178,131,178,119,165)
        Fill-Shape $g $b @(183,84,199,84,240,174,224,174); Fill-Shape $g $b @(224,84,240,84,198,174,182,174)
      }
      'B' {
        Fill-Shape $g $w @(18,83,38,83,49,160,68,111,82,111,101,160,114,83,134,83,113,186,94,186,76,139,57,186,39,186)
        Fill-Shape $g $b @(166,55,177,43,214,43,226,55,226,70,209,70,209,62,183,62,183,101,209,101,209,93,226,93,226,109,214,121,177,121,166,109)
        Fill-Shape $g $b @(168,149,186,149,199,169,212,149,231,149,209,181,232,215,213,215,199,193,185,215,166,215,189,181)
      }
      'C' {
        Fill-Shape $g $w @(15,86,31,86,42,145,56,105,69,105,83,145,95,86,111,86,93,178,77,178,62,139,47,178,31,178)
        Fill-Shape $g $w @(120,96,132,84,163,84,174,97,174,111,158,111,158,102,133,102,133,160,158,160,158,150,174,150,174,166,163,178,132,178,120,165)
        Fill-Shape $g $b @(183,86,198,86,233,120,214,120); Fill-Shape $g $b @(233,86,248,86,214,120,199,120)
        Fill-Shape $g $b @(183,168,198,168,233,134,214,134); Fill-Shape $g $b @(233,168,248,168,214,134,199,134)
      }
      'D' {
        Fill-Shape $g $w @(15,86,31,86,42,145,56,105,69,105,83,145,95,86,111,86,93,178,77,178,62,139,47,178,31,178)
        Fill-Shape $g $w @(119,96,131,84,162,84,173,97,173,111,157,111,157,102,132,102,132,160,157,160,157,150,173,150,173,166,162,178,131,178,119,165)
        Fill-Shape $g $n @(183,84,199,84,240,174,224,174); Fill-Shape $g $n @(224,84,240,84,198,174,182,174)
      }
    }
    $w.Dispose(); $b.Dispose(); $n.Dispose()
}
function Save-Icon([string]$option, [int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    Draw-Icon $g $option
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
function Save-SmallStrip([string]$option, [string]$path) {
    $sizes = @(16,24,32,48); $cell = 192; $gap = 12
    $out = New-Object System.Drawing.Bitmap(($cell * 4 + $gap * 5), ($cell + $gap * 2), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($out); $g.Clear($light)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    for ($i=0; $i -lt $sizes.Length; $i++) {
        $native = New-Object System.Drawing.Bitmap($sizes[$i], $sizes[$i], [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $ng = [System.Drawing.Graphics]::FromImage($native); $ng.ScaleTransform($sizes[$i] / 256.0, $sizes[$i] / 256.0); Draw-Icon $ng $option $true; $ng.Dispose()
        $scaled = New-Object System.Drawing.Bitmap(($sizes[$i] * 4), ($sizes[$i] * 4), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $sg = [System.Drawing.Graphics]::FromImage($scaled); $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor; $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $sg.DrawImage($native, 0, 0, $scaled.Width, $scaled.Height); $sg.Dispose(); $native.Dispose()
        $x = $gap + $i * ($cell + $gap) + [int](($cell - $scaled.Width) / 2); $y = $gap + [int](($cell - $scaled.Height) / 2)
        $g.DrawImage($scaled, $x, $y); $scaled.Dispose()
    }
    $g.Dispose(); $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $out.Dispose()
}

foreach ($option in @('A','B','C','D')) {
    Save-Icon $option 256 (Join-Path $root "option-$option-256.png")
    Save-SmallStrip $option (Join-Path $root "option-$option-small.png")
}
$all = New-Object System.Drawing.Bitmap(1072, 288, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$ag = [System.Drawing.Graphics]::FromImage($all); $ag.Clear($light)
$ag.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
foreach ($i in 0..3) { $src = [System.Drawing.Image]::FromFile((Join-Path $root ("option-{0}-256.png" -f [char](65 + $i)))); $ag.DrawImage($src, (16 + 272 * $i), 16, 256, 256); $src.Dispose() }
$ag.Dispose(); $all.Save((Join-Path $root 'all-options.png'), [System.Drawing.Imaging.ImageFormat]::Png); $all.Dispose()
