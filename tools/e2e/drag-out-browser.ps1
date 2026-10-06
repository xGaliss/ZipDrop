<#
  Real test: drag the finished ZIP out of the basket into a browser page (moves the mouse).
  Needs the basket showing "ZIP created". Uses Microsoft Edge and tools/e2e/browser-drop.html,
  which reports the received file in its title.
    powershell -STA -File tools/e2e/drag-out-browser.ps1
#>
. "$PSScriptRoot\..\media\Native.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]

$overlay = $AE::FromHandle((Get-ZipDropWindow))
$texts = $overlay.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
$chip = @($texts) | Where-Object { $_.Current.Name -like "*.zip" -and -not $_.Current.IsOffscreen } | Select-Object -First 1
if (-not $chip) { throw "ZIP chip not found: is the basket showing 'ZIP created'?" }
$zipName = $chip.Current.Name
$c = $chip.Current.BoundingRectangle
$o = [ZdNative]::RectOf((Get-ZipDropWindow))

$page = (Resolve-Path "$PSScriptRoot\browser-drop.html").Path
$x = if ($o.L -gt 800) { $o.L - 760 } else { $o.R + 40 }
Start-Process msedge -ArgumentList "--new-window", "--window-position=$x,$($o.T)", "--window-size=700,500", "file:///$($page.Replace('\','/'))"
$edge = $null
for ($i = 0; $i -lt 40 -and -not $edge; $i++) {
    Start-Sleep -Milliseconds 300
    $edge = @($AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) |
        Where-Object { $_.Current.Name -like "ZipDrop drop test*" } | Select-Object -First 1
}
if (-not $edge) { throw "Edge window not found" }
Start-Sleep 1
[ZdNative]::RaiseTopmost((Get-ZipDropWindow))
$e = $edge.Current.BoundingRectangle

$sx = [int]($c.X + $c.Width / 2); $sy = [int]($c.Y + $c.Height / 2)
[ZdNative]::Glide($sx, $sy, 400); Start-Sleep -Milliseconds 300
[ZdNative]::Down(); Start-Sleep -Milliseconds 150
[ZdNative]::Glide($sx - 30, $sy + 10, 200)
[ZdNative]::Glide([int]($e.X + $e.Width / 2), [int]($e.Y + $e.Height / 2 + 40), 700); Start-Sleep -Milliseconds 800
[ZdNative]::Up()
Start-Sleep 1.5

$title = $edge.Current.Name
"Browser title: $title"
$ok = $title -like "RECEIVED $zipName *"
"Browser received ${zipName}: $ok"
# Close exactly the test window (never send keys: Edge shares one process across all the user's windows).
$edge.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
if (-not $ok) { exit 1 }
