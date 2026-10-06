<#
  Real test of dragging the finished ZIP out of the basket (moves the mouse).
  Needs the basket showing "ZIP created". Opens an empty folder in File Explorer, drags the ZIP chip
  onto it and checks that Explorer received a copy.
    powershell -STA -File tools/e2e/drag-out.ps1
#>
param([string]$Target = (Join-Path $env:TEMP "ZipDropDragOut"))
. "$PSScriptRoot\..\media\Native.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]

if (Test-Path $Target) { Get-ChildItem $Target | ForEach-Object { [IO.File]::Delete($_.FullName) } }
New-Item -ItemType Directory -Force $Target | Out-Null

$overlay = $AE::FromHandle((Get-ZipDropWindow))
# The chip itself (a Border) has no automation peer: use its file-name text, which sits inside it.
$texts = $overlay.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
$chip = @($texts) | Where-Object { $_.Current.Name -like "*.zip" -and -not $_.Current.IsOffscreen } | Select-Object -First 1
if (-not $chip) { throw "ZIP chip not found: is the basket showing 'ZIP created'?" }
$zipName = $chip.Current.Name
if (-not $zipName) { throw "Could not read the ZIP name" }
$c = $chip.Current.BoundingRectangle
$o = [ZdNative]::RectOf((Get-ZipDropWindow))

# Explorer window on the free side of the overlay.
$shell = New-Object -ComObject Shell.Application
$shell.Open($Target)
$win = $null
for ($i = 0; $i -lt 40 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 250
    $win = @($shell.Windows()) | Where-Object { $_.LocationURL -like "*$(Split-Path $Target -Leaf)" } | Select-Object -First 1
}
if (-not $win) { throw "Explorer window not found" }
$ex = [IntPtr]$win.HWND
$exX = if ($o.L -gt 800) { $o.L - 700 } else { $o.R + 40 }
[ZdNative]::Place($ex, $exX, $o.T, 620, 420)
Start-Sleep 1
[ZdNative]::RaiseTopmost((Get-ZipDropWindow))

$sx = [int]($c.X + $c.Width / 2); $sy = [int]($c.Y + $c.Height / 2)
[ZdNative]::Glide($sx, $sy, 400); Start-Sleep -Milliseconds 300
[ZdNative]::Down(); Start-Sleep -Milliseconds 150
[ZdNative]::Glide($sx - 30, $sy + 10, 200)
[ZdNative]::Glide($exX + 380, $o.T + 260, 700); Start-Sleep -Milliseconds 700
[ZdNative]::Up()
Start-Sleep 1.5

$copied = Join-Path $Target $zipName
"ZIP copied into Explorer folder: $(Test-Path $copied)  ($copied)"
$win.Quit()
if (-not (Test-Path $copied)) { exit 1 }
