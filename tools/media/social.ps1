<#
  Builds the GitHub social preview / promo card (1280x640) from docs/media/basket.png.
    powershell -File tools/media/social.ps1
#>
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path "$PSScriptRoot\..\.."
$shot = [System.Drawing.Image]::FromFile("$root\docs\media\basket.png")
# The .ico stores PNG frames (GDI+ can't decode those via Icon): pull the last (256 px) PNG out directly.
$ico = [IO.File]::ReadAllBytes("$root\src\ZipDrop\Assets\ZipDrop.ico")
$count = [BitConverter]::ToUInt16($ico, 4); $entry = 6 + 16 * ($count - 1)
$len = [BitConverter]::ToInt32($ico, $entry + 8); $off = [BitConverter]::ToInt32($ico, $entry + 12)
$logo = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream (, $ico[$off..($off + $len - 1)])))

$W = 1280; $H = 640
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'

$rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 30, 33, 66)), ([System.Drawing.Color]::FromArgb(255, 108, 60, 138)), 30
$g.FillRectangle($bg, $rect)

# Crop just the card out of the capture (capture pad 56 + window margin 18 = 74 px) and redraw it
# with its own rounded clip and soft shadow, so no rectangle edge shows on the new background.
$m = 74
$src = New-Object System.Drawing.Rectangle $m, $m, ($shot.Width - 2 * $m), ($shot.Height - 2 * $m)
$scale = 1.6
$cw = [int]($src.Width * $scale); $ch = [int]($src.Height * $scale)
$cx = $W - $cw - 110; $cy = [int](($H - $ch) / 2)
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p
}
for ($i = 14; $i -ge 1; $i--) {
    $a = [int](10 - $i * 0.6); if ($a -lt 1) { $a = 1 }
    $sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($a, 0, 0, 0))
    $g.FillPath($sb, (RoundRect ($cx - $i) ($cy - $i + 14) ($cw + 2 * $i) ($ch + 2 * $i) (26 + $i)))
}
$card = RoundRect $cx $cy $cw $ch 25
$g.SetClip($card)
$g.DrawImage($shot, (New-Object System.Drawing.Rectangle $cx, $cy, $cw, $ch), $src, [System.Drawing.GraphicsUnit]::Pixel)
$g.ResetClip()

# Text block on the left.
$white = [System.Drawing.Brushes]::White
$soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(210, 225, 228, 255))
$g.DrawImage($logo, (New-Object System.Drawing.Rectangle 80, 150, 72, 72))
$title = New-Object System.Drawing.Font "Segoe UI Semibold", 54, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$tag = New-Object System.Drawing.Font "Segoe UI Semibold", 40, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$body = New-Object System.Drawing.Font "Segoe UI", 25, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString("ZipDrop", $title, $white, 168, 152)
$g.DrawString("Build ZIPs while you work.", $tag, $white, 76, 262)
$g.DrawString("Shake while dragging files to open a basket.`nDrop from any folder. One click: one ZIP.", $body, $soft, 80, 330)
$g.DrawString("Free & open source  ·  Windows 10/11", $body, $soft, 80, 450)

$out = "$root\docs\media\social-preview.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $shot.Dispose()
Write-Host "Saved $out"
