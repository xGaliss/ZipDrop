# Generates src/ZipDrop/Assets/ZipDrop.ico (multi-size, PNG-compressed entries).
# Run with Windows PowerShell 5.1 (uses System.Drawing from .NET Framework):
#   powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$out = Join-Path $PSScriptRoot '..\src\ZipDrop\Assets\ZipDrop.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Background tile
    $pad = [Math]::Max(0.5, $s * 0.03)
    $tile = New-RoundRect $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.24)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 124, 137, 255)), ([System.Drawing.Color]::FromArgb(255, 72, 84, 230)), 90
    $g.FillPath($brush, $tile)

    # Glyph: arrow dropping into a tray
    $stroke = [Math]::Max(1.6, $s * 0.085)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'

    $cx = $s / 2
    # arrow shaft + head
    $g.DrawLine($pen, $cx, $s * 0.22, $cx, $s * 0.56)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($cx - $s * 0.14), ($s * 0.43)),
        (New-Object System.Drawing.PointF $cx, ($s * 0.57)),
        (New-Object System.Drawing.PointF ($cx + $s * 0.14), ($s * 0.43))))
    # tray
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.24), ($s * 0.60)),
        (New-Object System.Drawing.PointF ($s * 0.24), ($s * 0.76)),
        (New-Object System.Drawing.PointF ($s * 0.76), ($s * 0.76)),
        (New-Object System.Drawing.PointF ($s * 0.76), ($s * 0.60))))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $out)).Path + '\ZipDrop.ico')
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "Wrote $out"
