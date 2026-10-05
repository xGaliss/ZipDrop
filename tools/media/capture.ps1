<#
  Captures a ZipDrop window over a clean gradient backdrop (for README / promo images).
    powershell -STA -File tools/media/capture.ps1 -Out docs/media/basket.png [-Title "ZipDrop Settings"] [-Pad 56]
#>
param([Parameter(Mandatory)][string]$Out, [string]$Title = "ZipDrop", [int]$Pad = 56, [int]$HoldMs = 0,
      [int]$X = 900, [int]$Y = 300)   # where to put the window first (keeps it clear of other topmost windows)
. "$PSScriptRoot\Native.ps1"

$h = Get-ZipDropWindow $Title
if ($h -eq [IntPtr]::Zero) { throw "Window '$Title' not visible" }
[ZdNative]::SetWindowPos($h, [IntPtr]::Zero, $X, $Y, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010) | Out-Null
Start-Sleep -Milliseconds 200
$r = [ZdNative]::RectOf($h)
$x = $r.L - $Pad; $y = $r.T - $Pad; $w = $r.W + 2 * $Pad; $hh = $r.H + 2 * $Pad

$backdrop = Show-Backdrop $x $y $w $hh
[ZdNative]::RaiseTopmost($h)
Start-Sleep -Milliseconds (350 + $HoldMs)
[System.Windows.Forms.Application]::DoEvents()
New-Item -ItemType Directory -Force (Split-Path -Parent ([IO.Path]::GetFullPath($Out))) | Out-Null
Save-Region $x $y $w $hh ([IO.Path]::GetFullPath($Out))
$backdrop.Close()
Write-Host "Saved $Out ($w x $hh)"
