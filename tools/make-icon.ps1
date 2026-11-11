# Draws the application icon.
#
#   pwsh tools/make-icon.ps1                    writes src/BtManage/Assets/app.ico
#   pwsh tools/make-icon.ps1 -Variant tile      the same, with another variant
#   pwsh tools/make-icon.ps1 -Sheet             writes design/icon-variants.png with every variant
#
# The mark is an angular letter B, the rune the Bluetooth symbol is built on, without the
# two tails that make the registered Bluetooth figure mark. Variants:
#
#   rune        the B alone
#   waves       the B between two arcs, like a signal going out both ways
#   headphones  the B inside a pair of headphones
#   tile        the B and the arcs in white on a blue tile
#
# Geometry lives on a 16-unit grid. The tray icon (src/BtManage/Tray/TrayGlyph.cs) draws the
# rune from the same numbers. The generated .ico is committed; run this again only when the
# mark changes.

param(
    [ValidateSet('rune', 'waves', 'headphones', 'tile')]
    [string]$Variant = 'waves',
    [string]$Color = '#0082FC',
    [switch]$Sheet,
    [string]$Output = (Join-Path $PSScriptRoot '..\src\BtManage\Assets\app.ico'),
    [string]$SheetOutput = (Join-Path $PSScriptRoot '..\design\icon-variants.png')
)

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

# Draws one variant into a square of $size pixels whose top left corner is at ($left, $top).
function Add-Mark($graphics, [single]$left, [single]$top, [single]$size, [string]$variant, $ink) {
    [single]$u = $size / 16
    $state = $graphics.Save()
    $graphics.TranslateTransform($left, $top)

    if ($variant -eq 'tile') {
        $tile = New-RoundedRect 0 0 $size $size ([single](3.6 * $u))
        $brush = [System.Drawing.SolidBrush]::new($ink)
        $graphics.FillPath($brush, $tile)
        $brush.Dispose(); $tile.Dispose()
        $ink = [System.Drawing.Color]::White
        # Inside the tile the mark is drawn smaller, around the same centre.
        $graphics.TranslateTransform([single](8 * $u), [single](8 * $u))
        $graphics.ScaleTransform(0.74, 0.74)
        $graphics.TranslateTransform([single](-8 * $u), [single](-8 * $u))
    }

    # Small sizes need a heavier stroke to stay readable.
    [single]$weight = if ($size -le 24) { 1.9 } else { 1.6 }
    [single]$shift = if ($variant -eq 'headphones') { 1.0 } else { 0 }
    [single]$scale = if ($variant -eq 'headphones') { 0.78 } else { 1 }

    $pen = [System.Drawing.Pen]::new($ink, [single]($weight * $u * $scale))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
    $pen.MiterLimit = 6
    # One stroke that starts and ends at the middle of the stem, so the sharp corners are
    # the four on the outside and nothing sticks out to the left where the two bowls meet.
    $points = @(
        @(6.2, 8), @(10.6, 5.3), @(6.2, 2.6), @(6.2, 13.4), @(10.6, 10.7), @(6.2, 8)
    ) | ForEach-Object {
        [System.Drawing.PointF]::new(
            [single]((8 + ($_[0] - 8.4) * $scale) * $u),
            [single]((8 + $shift + ($_[1] - 8) * $scale) * $u))
    }
    $rune = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $rune.AddLines([System.Drawing.PointF[]]$points)
    $graphics.DrawPath($pen, $rune)
    $rune.Dispose(); $pen.Dispose()

    if ($variant -in 'waves', 'tile') {
        $arc = [System.Drawing.Pen]::new($ink, [single](1.25 * $u))
        $arc.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $arc.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        [single]$r = 6.9
        $box = [System.Drawing.RectangleF]::new([single]((8 - $r) * $u), [single]((8 - $r) * $u), [single](2 * $r * $u), [single](2 * $r * $u))
        $graphics.DrawArc($arc, $box, 138, 84)
        $graphics.DrawArc($arc, $box, -42, 84)
        $arc.Dispose()
    }

    if ($variant -eq 'headphones') {
        $band = [System.Drawing.Pen]::new($ink, [single](1.4 * $u))
        $band.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $band.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($band, [single](1.7 * $u), [single](1.2 * $u), [single](12.6 * $u), [single](12.6 * $u), 180, 180)
        $band.Dispose()
        $brush = [System.Drawing.SolidBrush]::new($ink)
        foreach ($x in 0.6, 12.8) {
            $cup = New-RoundedRect ([single]($x * $u)) ([single](7.4 * $u)) ([single](2.6 * $u)) ([single](5.6 * $u)) ([single](1.1 * $u))
            $graphics.FillPath($brush, $cup)
            $cup.Dispose()
        }
        $brush.Dispose()
    }

    $graphics.Restore($state)
}

function New-Canvas([int]$width, [int]$height) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $bitmap, $graphics
}

$ink = [System.Drawing.ColorTranslator]::FromHtml($Color)

if ($Sheet) {
    # Every variant at four sizes, on a light and on a dark strip.
    $variants = 'rune', 'waves', 'headphones', 'tile'
    $sizes = 128, 48, 32, 16
    $column = 300; $row = 190
    $bitmap, $graphics = New-Canvas ($column * $variants.Count) ($row * 2)
    $font = [System.Drawing.Font]::new('Segoe UI', 11)
    foreach ($theme in 0, 1) {
        $background = if ($theme -eq 0) { '#F3F3F3' } else { '#202020' }
        $text = if ($theme -eq 0) { '#5C5C5C' } else { '#C8C8C8' }
        $strip = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($background))
        $graphics.FillRectangle($strip, 0, $theme * $row, $bitmap.Width, $row)
        $strip.Dispose()
        $label = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($text))
        for ($i = 0; $i -lt $variants.Count; $i++) {
            $x = $i * $column + 20
            $y = $theme * $row + 20
            foreach ($size in $sizes) {
                Add-Mark $graphics $x ($y + 128 - $size) $size $variants[$i] $ink
                $x += $size + 14
            }
            $graphics.DrawString($variants[$i], $font, $label, [single]($i * $column + 20), [single]($theme * $row + 156))
        }
        $label.Dispose()
    }
    $font.Dispose(); $graphics.Dispose()
    New-Item -ItemType Directory -Force (Split-Path -Parent $SheetOutput) | Out-Null
    $bitmap.Save($SheetOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    "Wrote $SheetOutput"
    return
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) {
    $bitmap, $graphics = New-Canvas $size $size
    Add-Mark $graphics 0 0 $size $Variant $ink
    $graphics.Dispose()
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

$file = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Flush()

New-Item -ItemType Directory -Force (Split-Path -Parent $Output) | Out-Null
[System.IO.File]::WriteAllBytes($Output, $file.ToArray())
"Wrote $Output ($Variant, $($file.Length) bytes, sizes: $($sizes -join ', '))"
