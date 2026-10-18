# Draws the Thunderstore icon (256 x 256 PNG) in the style of Void Crew's ship loadout terminal: a light terminal bezel,
# a dark screen with gold meander bands, a top-down METEM ship with red / green / "+" hardpoints, and the title.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-Out thunderstore\icon.png]
param(
    [string]$Out = (Join-Path $PSScriptRoot "..\thunderstore\icon.png")
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

$scale = 4
$size = 256
$big = New-Object System.Drawing.Bitmap ($size * $scale), ($size * $scale)
$g = [System.Drawing.Graphics]::FromImage($big)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.ScaleTransform($scale, $scale)
$shipLift = -6

function C([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Poly([float[]]$xy) {
    $pts = New-Object 'System.Drawing.PointF[]' ($xy.Length / 2)
    for ($i = 0; $i -lt $pts.Length; $i++) { $pts[$i] = New-Object System.Drawing.PointF $xy[2 * $i], $xy[2 * $i + 1] }
    return $pts
}

# ---------------------------------------------------------------- bezel and screen
$bezel = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, 256, 256), (C "#F1EEE6"), (C "#C9C3B4"), 90
$g.FillRectangle($bezel, 0, 0, 256, 256)
# Mounting brackets on the sides, like the terminal's frame.
$bracket = New-Object System.Drawing.SolidBrush (C "#8E8A82")
foreach ($by in 34, 186) {
    $g.FillPath($bracket, (RoundRect 2 $by 9 36 3))
    $g.FillPath($bracket, (RoundRect 245 $by 9 36 3))
}

$screenPath = RoundRect 14 14 228 228 9
$screen = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 14, 14, 228, 228), (C "#20252A"), (C "#0B0D0F"), 90
$g.FillPath($screen, $screenPath)
# Soft glow behind the ship.
$glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowPath.AddEllipse(24, 50, 208, 126)
$glow = New-Object System.Drawing.Drawing2D.PathGradientBrush $glowPath
$glow.CenterColor = C "#3A4148" 150
$glow.SurroundColors = @(C "#0B0D0F" 0)
$g.FillPath($glow, $glowPath)
$g.DrawPath((New-Object System.Drawing.Pen (C "#000000" 120), 2), $screenPath)

# ---------------------------------------------------------------- meander (Greek key) bands, like the terminal's deco bars
$gold = C "#E3AE3C"
$goldPen = New-Object System.Drawing.Pen (C "#C99A36"), 1.15
$goldPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
function Meander([float]$y) {
    $unit = 10.0
    $h = 7.0
    for ($x = 24.0; $x + $unit -le 233; $x += $unit) {
        $pts = Poly @($x, ($y + $h), $x, $y, ($x + 7), $y, ($x + 7), ($y + 5), ($x + 3), ($y + 5), ($x + 3), ($y + 2.5), ($x + 5), ($y + 2.5))
        $g.DrawLines($goldPen, $pts)
        $g.DrawLine($goldPen, $x, ($y + $h), ($x + $unit), ($y + $h))
    }
}
Meander 21
Meander 226

# ---------------------------------------------------------------- the ship (top-down, nose to the left, as on the terminal)
$g.TranslateTransform(0, $shipLift)
$hull = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 30, 80, 190, 80), (C "#F2E8CC"), (C "#C9BA92"), 90
$outline = New-Object System.Drawing.Pen (C "#111111"), 1.3
$outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$dark = New-Object System.Drawing.SolidBrush (C "#2A2F35")
$darker = New-Object System.Drawing.SolidBrush (C "#16191C")
$panel = New-Object System.Drawing.Pen (C "#8C7F5E"), 0.8

# Fins
$finTop = Poly @(166, 87, 180, 74, 192, 74, 190, 87)
$finBottom = Poly @(166, 149, 180, 162, 192, 162, 190, 149)
$g.FillPolygon($hull, $finTop); $g.DrawPolygon($outline, $finTop)
$g.FillPolygon($hull, $finBottom); $g.DrawPolygon($outline, $finBottom)

# Central body
$body = Poly @(84, 104, 204, 104, 214, 109, 214, 127, 204, 132, 84, 132, 76, 118)
$g.FillPolygon($dark, $body); $g.DrawPolygon($outline, $body)
$g.DrawLine((New-Object System.Drawing.Pen (C "#4A525C"), 0.8), 100, 118, 200, 118)
$bridge = Poly @(84, 111, 96, 109, 100, 118, 96, 127, 84, 125)
$g.FillPolygon((New-Object System.Drawing.SolidBrush (C "#E3AE3C" 200)), $bridge)
$g.DrawPolygon($outline, $bridge)
$g.FillRectangle((New-Object System.Drawing.SolidBrush (C "#3C434C")), 150, 108, 26, 20)
$g.DrawRectangle((New-Object System.Drawing.Pen (C "#E3AE3C" 170), 0.8), 150, 108, 26, 20)

# Pontoon hulls
$upper = Poly @(32, 96, 62, 90, 100, 86, 196, 86, 205, 89, 205, 103, 100, 103, 62, 101)
$lower = Poly @(32, 140, 62, 146, 100, 150, 196, 150, 205, 147, 205, 133, 100, 133, 62, 135)
$g.FillPolygon($hull, $upper); $g.DrawPolygon($outline, $upper)
$g.FillPolygon($hull, $lower); $g.DrawPolygon($outline, $lower)
foreach ($px in 100, 156, 182) {
    $g.DrawLine($panel, $px, 87, $px, 102)
    $g.DrawLine($panel, $px, 134, $px, 149)
}
$g.FillRectangle($darker, 160, 90, 18, 4)
$g.FillRectangle($darker, 160, 142, 18, 4)

# Engines
foreach ($ey in 88, 134) {
    $g.FillRectangle($darker, 205, $ey, 17, 14)
    $g.DrawRectangle($outline, 205, $ey, 17, 14)
    $g.DrawLine((New-Object System.Drawing.Pen $gold, 1.2), 222, ($ey + 2), 222, ($ey + 12))
}
$g.FillRectangle($darker, 214, 112, 12, 12)
$g.DrawRectangle($outline, 214, 112, 12, 12)
$g.DrawLine((New-Object System.Drawing.Pen $gold, 1.2), 226, 114, 226, 122)

# ---------------------------------------------------------------- hardpoints (the terminal's red weapon / green system / "+" slots)
function Slot([float]$x, [float]$y, [float]$w, [string]$fill, [string]$edge, [bool]$plus) {
    $path = RoundRect $x $y $w 12 2.2
    $g.FillPath((New-Object System.Drawing.SolidBrush (C $fill)), $path)
    $g.DrawPath((New-Object System.Drawing.Pen (C $edge), 1.4), $path)
    $glyph = New-Object System.Drawing.Pen (C $edge), 1.3
    if ($plus) {
        $cx = $x + $w / 2; $cy = $y + 6
        $g.DrawLine($glyph, ($cx - 3), $cy, ($cx + 3), $cy)
        $g.DrawLine($glyph, $cx, ($cy - 3), $cx, ($cy + 3))
    } else {
        $g.DrawLine($glyph, ($x + 3.5), ($y + 8.5), ($x + 8.5), ($y + 3.5))
        $g.DrawLine($glyph, ($x + 6), ($y + 8.5), ($x + 8.5), ($y + 6))
    }
}
Slot 108 88.5 12 "#5A1412" "#FF5A4E" $false
Slot 122 88.5 12 "#0F4A22" "#58E07C" $false
Slot 136 88.5 24 "#14181C" "#F2F2F2" $true
Slot 108 135.5 12 "#5A1412" "#FF5A4E" $false
Slot 122 135.5 12 "#14181C" "#F2F2F2" $true
Slot 62 106 12 "#5A1412" "#FF5A4E" $false
Slot 62 119 12 "#0F4A22" "#58E07C" $false
$g.TranslateTransform(0, -$shipLift)

# ---------------------------------------------------------------- text
function CharWidth([string]$ch, $font) {
    if ($ch -eq " ") { return $font.Size * 0.32 }
    return $g.MeasureString($ch, $font, 1000, [System.Drawing.StringFormat]::GenericTypographic).Width
}
function TextWidth([string]$text, $font, [float]$spacing) {
    $w = 0.0
    foreach ($ch in $text.ToCharArray()) { $w += (CharWidth ([string]$ch) $font) + $spacing }
    return $w - $spacing
}
function Spaced([string]$text, $font, $brush, [float]$centerX, [float]$y, [float]$spacing, [bool]$shadow) {
    $x = $centerX - (TextWidth $text $font $spacing) / 2
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $shadowBrush = New-Object System.Drawing.SolidBrush (C "#000000" 170)
    foreach ($ch in $text.ToCharArray()) {
        $s = [string]$ch
        if ($shadow) { $g.DrawString($s, $font, $shadowBrush, ($x + 1.2), ($y + 1.4), $format) }
        $g.DrawString($s, $font, $brush, $x, $y, $format)
        $x += (CharWidth $s $font) + $spacing
    }
}

$px = [System.Drawing.GraphicsUnit]::Pixel
$goldBrush = New-Object System.Drawing.SolidBrush $gold
$white = New-Object System.Drawing.SolidBrush (C "#F4F2EC")

Spaced "VOID CREW" (New-Object System.Drawing.Font "Bahnschrift SemiBold", 17, ([System.Drawing.FontStyle]::Regular), $px) $goldBrush 128 35 3.2 $true
$g.DrawLine((New-Object System.Drawing.Pen (C "#FFFFFF" 70), 0.8), 76, 58, 180, 58)

Spaced "SHIP CUSTOMIZER" (New-Object System.Drawing.Font "Bahnschrift SemiBold Condensed", 26, ([System.Drawing.FontStyle]::Regular), $px) $white 128 162 1.0 $true
$g.DrawLine((New-Object System.Drawing.Pen (C "#FFFFFF" 70), 0.8), 40, 193, 216, 193)

# "VR READY" tag, styled like the terminal's gold-edged buttons (angled ends).
$tagFont = New-Object System.Drawing.Font "Bahnschrift SemiBold", 11, ([System.Drawing.FontStyle]::Regular), $px
$tagW = (TextWidth "VR READY" $tagFont 1.6) + 22
$tx = 128 - $tagW / 2
$tag = Poly @(($tx + 5), 199, ($tx + $tagW - 5), 199, ($tx + $tagW), 207, ($tx + $tagW - 5), 215, ($tx + 5), 215, $tx, 207)
$g.FillPolygon((New-Object System.Drawing.SolidBrush (C "#000000" 120)), $tag)
$g.DrawPolygon((New-Object System.Drawing.Pen $gold, 1.3), $tag)
Spaced "VR READY" $tagFont $goldBrush 128 200.5 1.6 $false

# ---------------------------------------------------------------- downscale to 256 x 256 and save
$g.Dispose()
$icon = New-Object System.Drawing.Bitmap $size, $size
$g2 = [System.Drawing.Graphics]::FromImage($icon)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g2.DrawImage($big, 0, 0, $size, $size)
$g2.Dispose()
$big.Dispose()

$outPath = [System.IO.Path]::GetFullPath($Out)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outPath)) | Out-Null
$icon.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$icon.Dispose()
Write-Output "Saved $outPath"
