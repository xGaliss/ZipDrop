<#
  Real test of the tray icon context menu (moves the mouse):
  opens the taskbar "hidden icons" flyout if needed, right-clicks the ZipDrop icon and checks that the
  menu stays open. Optionally picks an item by its position (1-based, separators count).
    powershell -STA -File tools/e2e/tray-menu.ps1 [-Pick 3] [-Screenshot out.png]
#>
param([int]$Pick = 0, [string]$Screenshot)
. "$PSScriptRoot\..\media\Native.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TrayTest {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  public static void RightClick() { mouse_event(0x0008, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x0010, 0, 0, 0, IntPtr.Zero); }
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  public static void Esc() { keybd_event(0x1B, 0, 0, IntPtr.Zero); keybd_event(0x1B, 0, 2, IntPtr.Zero); }
}
"@

function Find-ZipDropIcon {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $AE::RootElement.FindAll($TS::Descendants, $cond)) {
        $n = $b.Current.Name
        if ($n -like "ZipDrop*" -and -not $b.Current.IsOffscreen -and $b.Current.BoundingRectangle.Width -gt 0) { return $b }
    }
    return $null
}

$icon = Find-ZipDropIcon
if (-not $icon) {
    # Icon lives in the overflow flyout: open it with the taskbar chevron.
    $tray = $AE::FromHandle([TrayTest]::FindWindow("Shell_TrayWnd", $null))
    $chevron = $tray.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "SystemTrayIcon")))
    if (-not $chevron) { throw "Hidden-icons chevron not found" }
    $r = $chevron.Current.BoundingRectangle
    [ZdNative]::Glide([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2), 300); [ZdNative]::Click()
    Start-Sleep 1
    $icon = Find-ZipDropIcon
    if (-not $icon) { throw "ZipDrop tray icon not found" }
    Write-Host "Icon is in the hidden-icons flyout"
}
Write-Host "Icon: '$($icon.Current.Name)'"
$r = $icon.Current.BoundingRectangle
[ZdNative]::Glide([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2), 300)
Start-Sleep -Milliseconds 200
[TrayTest]::RightClick()

$open = $false
foreach ($t in 1..4) {
    Start-Sleep -Milliseconds 400
    $menu = [TrayTest]::FindWindow("#32768", $null)
    $visible = $menu -ne [IntPtr]::Zero -and [TrayTest]::IsWindowVisible($menu)
    Write-Host ("after {0} ms: menu visible = {1}" -f ($t * 400), $visible)
    $open = $visible
}
if ($Screenshot -and $open) {
    $m = [ZdNative]::RectOf($menu)
    Save-Region ($m.L - 20) ($m.T - 20) ($m.W + 40) ($m.H + 40) ([IO.Path]::GetFullPath($Screenshot))
}
if ($open -and $Pick -gt 0) {
    $m = [ZdNative]::RectOf($menu)
    # Items: ~same height; estimate by count of 5 entries (4 items + separator counted as a thin row).
    $itemH = ($m.H - 16) / 4.4
    $y = $m.T + 8 + ($Pick - 0.5) * $itemH
    [ZdNative]::Glide([int]($m.L + $m.W / 2), [int]$y, 250); Start-Sleep -Milliseconds 200; [ZdNative]::Click()
    Write-Host "Picked item $Pick"
} elseif ($open) {
    [TrayTest]::Esc()
}
if (-not $open) { exit 1 }
