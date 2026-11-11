# Draws the application icon and writes src/BtManage/Assets/app.ico.
#
# The mark is a pair of headphones on a 16-unit grid, the same geometry the
# tray icon uses (src/BtManage/Tray/TrayGlyph.cs). Run this again only when
# the mark changes; the generated .ico is committed.
#
#   pwsh tools/make-icon.ps1

param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\BtManage\Assets\app.ico'),
    [string]$Color = '#3D7DCA'
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

function New-MarkPng([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    [single]$u = $size / 16
    $ink = [System.Drawing.ColorTranslator]::FromHtml($Color)
    $pen = [System.Drawing.Pen]::new($ink, [single](1.5 * $u))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $brush = [System.Drawing.SolidBrush]::new($ink)

    # Headband, then the two ear cups.
    $graphics.DrawArc($pen, [single](3.5 * $u), [single](2.5 * $u), [single](9 * $u), [single](9 * $u), 180, 180)
    $graphics.DrawLine($pen, [single](3.5 * $u), [single](7 * $u), [single](3.5 * $u), [single](8.5 * $u))
    $graphics.DrawLine($pen, [single](12.5 * $u), [single](7 * $u), [single](12.5 * $u), [single](8.5 * $u))
    foreach ($x in 2, 11) {
        $cup = New-RoundedRect ([single]($x * $u)) ([single](8.5 * $u)) ([single](3 * $u)) ([single](5.5 * $u)) ([single](1.2 * $u))
        $graphics.FillPath($brush, $cup)
        $cup.Dispose()
    }

    $brush.Dispose(); $pen.Dispose(); $graphics.Dispose()
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) { , (New-MarkPng $size) }

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

$directory = Split-Path -Parent $Output
New-Item -ItemType Directory -Force $directory | Out-Null
[System.IO.File]::WriteAllBytes($Output, $file.ToArray())
"Wrote $Output ($($file.Length) bytes, sizes: $($sizes -join ', '))"
