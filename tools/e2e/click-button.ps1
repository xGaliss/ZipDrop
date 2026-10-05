param([string]$Destination, [string]$ButtonName = "Create ZIP")
# Clicks a ZipDrop overlay button via UI Automation and fills the native Save dialog.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$pid_ = (Get-Process ZipDrop).Id

function Find-Overlay {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pid_)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "ZipDrop")))
    $AE::RootElement.FindFirst($TS::Children, $cond)
}

$overlay = Find-Overlay
if (-not $overlay) { throw "overlay not found" }
$btn = $overlay.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $ButtonName)))
if (-not $btn) { throw "button '$ButtonName' not found" }
$invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
# Invoke is synchronous for WPF buttons; the Save dialog is modal, so invoke on a background runspace.
$ps = [PowerShell]::Create().AddScript({ param($p) $p.Invoke() }).AddArgument($invoke)
$async = $ps.BeginInvoke()

if (-not $Destination) { Start-Sleep 1; return }

$dialog = $null
for ($i = 0; $i -lt 40 -and -not $dialog; $i++) {
    Start-Sleep -Milliseconds 250
    $dialog = $AE::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pid_)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Save ZIP")))))
}
if (-not $dialog) { throw "Save dialog not found" }
Write-Host "Save dialog open"

$edit = $dialog.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "1001")),
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))))
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Destination)
$save = $dialog.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "1")))
$save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Host "Saved as $Destination"
