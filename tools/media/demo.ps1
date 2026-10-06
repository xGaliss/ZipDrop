<#
  Records the README/promo demo with REAL File Explorer, REAL shake and REAL drops.
  MOVES YOUR MOUSE for ~40 s — don't touch anything while it runs.

    powershell -STA -File tools/media/demo.ps1 -Exe src/ZipDrop/bin/Release/net9.0-windows/ZipDrop.exe

  Output: docs/media/demo.gif and artifacts/media/demo.mp4 (needs ffmpeg in PATH or -FFmpeg).
#>
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$FFmpeg = "ffmpeg",
    [string]$Lang = "en",   # UI language recorded (README is English)
    [int]$RegionX = 400, [int]$RegionY = 110, [int]$RegionW = 1120, [int]$RegionH = 640
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Native.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$root = Resolve-Path "$PSScriptRoot\..\.."
$Exe = (Resolve-Path $Exe).Path

# ---------- demo files (realistic names and sizes; sparse content zips fast) ----------
$demo = Join-Path $env:TEMP "ZipDropDemo"
if (Test-Path $demo) { Remove-Item -Recurse -Force $demo }
function New-DemoFile([string]$Path, [long]$Size) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    $fs = [IO.File]::Create($Path); $fs.SetLength($Size); $fs.Dispose()
}
New-DemoFile "$demo\Pictures\Team photo.jpg" 4.2MB
New-DemoFile "$demo\Pictures\Office.png" 2.7MB
New-DemoFile "$demo\Pictures\Logo.svg" 24KB
New-DemoFile "$demo\Documents\Contract.pdf" 1.1MB
New-DemoFile "$demo\Documents\Invoice 0412.pdf" 380KB
New-DemoFile "$demo\Documents\Meeting notes.txt" 6KB
New-DemoFile "$demo\Projects\Website\index.html" 18KB
New-DemoFile "$demo\Projects\Website\assets\hero.webp" 6.4MB
New-DemoFile "$demo\Projects\Website\assets\styles.css" 42KB
New-DemoFile "$demo\Projects\Mobile app\README.md" 8KB
New-Item -ItemType Directory -Force "$demo\Out" | Out-Null

# ---------- stage ----------
Get-Process ZipDrop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$env:ZIPDROP_LANG = $Lang
Start-Process $Exe -ArgumentList "--background"
Start-Sleep 2

$backdrop = Show-Backdrop $RegionX $RegionY $RegionW $RegionH
$shell = New-Object -ComObject Shell.Application
$shell.Open("$demo\Pictures")
$explorer = $null
for ($i = 0; $i -lt 40 -and -not $explorer; $i++) {
    Start-Sleep -Milliseconds 250
    $explorer = @($shell.Windows()) | Where-Object { $_.LocationURL -like "*ZipDropDemo/Pictures" } | Select-Object -First 1
}
if (-not $explorer) { throw "Explorer window not found" }
$exHwnd = [IntPtr]$explorer.HWND
# The navigation pane (~168 px) is deliberately left OUTSIDE the recorded region:
# it shows the user's own pinned folders, which must not end up in a public GIF.
[ZdNative]::Place($exHwnd, $RegionX - 168, $RegionY + 50, 760, 500)
Start-Sleep 1.5

function Find-Item([string]$Name) {
    $el = $AE::FromHandle($exHwnd)
    for ($i = 0; $i -lt 20; $i++) {
        $item = $el.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Name)),
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))))
        if ($item) { return $item.Current.BoundingRectangle }
        Start-Sleep -Milliseconds 250
    }
    throw "Item '$Name' not found in Explorer"
}

function Get-OverlayCenter {
    $h = Get-ZipDropWindow
    if ($h -eq [IntPtr]::Zero) { return $null }
    $r = [ZdNative]::RectOf($h); return @{ X = [int](($r.L + $r.R) / 2); Y = [int](($r.T + $r.B) / 2) }
}

function Drag-Item([string]$Name, [switch]$Shake) {
    $r = Find-Item $Name
    $x = [int]($r.X + [Math]::Min(60, $r.Width / 2)); $y = [int]($r.Y + $r.Height / 2)
    [ZdNative]::Glide($x, $y, 700); Start-Sleep -Milliseconds 350
    [ZdNative]::Down(); Start-Sleep -Milliseconds 150
    [ZdNative]::Glide($x + 60, $y + 10, 250)
    $shakeX = $RegionX + 640
    [ZdNative]::Glide($shakeX, $y + 20, 650)
    if ($Shake) { [ZdNative]::Shake(80, 6, 80); Start-Sleep -Milliseconds 450 }
    $c = Get-OverlayCenter
    if (-not $c) { [ZdNative]::Up(); throw "Overlay did not appear" }
    [ZdNative]::Glide($c.X, $c.Y, 650); Start-Sleep -Milliseconds 700
    [ZdNative]::Up()
}

function Navigate([string]$Sub) {
    $explorer.Navigate("$demo\$Sub"); Start-Sleep 1.4
}

# ---------- record ----------
New-Item -ItemType Directory -Force "$root\artifacts\media", "$root\docs\media" | Out-Null
function Start-Recording([string]$File) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo $FFmpeg, ("-y -loglevel error -f gdigrab -framerate 30 -draw_mouse 1 " +
        "-offset_x $RegionX -offset_y $RegionY -video_size ${RegionW}x$RegionH -i desktop -c:v libx264 -preset ultrafast -crf 18 `"$File`"")
    $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi); Start-Sleep 1.2; return $p
}
function Stop-Recording($p) {
    if ($p.HasExited) { return }
    $p.StandardInput.Write("q"); $p.StandardInput.Close(); $p.WaitForExit(15000) | Out-Null
}
# Two segments: the native Save dialog is NOT recorded (it shows the user's name and folders).
$seg1 = "$root\artifacts\media\demo-1.mkv"; $seg2 = "$root\artifacts\media\demo-2.mkv"
$rec = Start-Recording $seg1

try {
    Drag-Item "Team photo.jpg" -Shake          # shake → basket appears → drop
    Start-Sleep 4.6                             # basket tucks itself away; keep working
    Navigate "Documents"
    Drag-Item "Contract.pdf" -Shake
    Start-Sleep -Milliseconds 600
    Drag-Item "Meeting notes.txt"                # basket still open: drop straight in
    Start-Sleep 4.6
    Navigate "Projects"
    Drag-Item "Website" -Shake
    Start-Sleep 1.2

    # Create ZIP
    $overlay = $AE::FromHandle((Get-ZipDropWindow))
    $btn = $overlay.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "CreateZip")))
    $b = $btn.Current.BoundingRectangle
    [ZdNative]::Glide([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2), 600); Start-Sleep -Milliseconds 300
    # Stop recording BEFORE clicking: ffmpeg keeps capturing for a moment after "q", and the native
    # Save dialog (user name, folders) must never end up in the video.
    Start-Sleep -Milliseconds 500
    Stop-Recording $rec
    Start-Sleep -Milliseconds 300
    # Not recorded any more, so press it through UI Automation (reliable; Invoke blocks while the dialog
    # is open, hence the background runspace).
    $invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $ps = [PowerShell]::Create().AddScript({ param($p) $p.Invoke() }).AddArgument($invoke); $null = $ps.BeginInvoke()
    $dlg = $null
    for ($i = 0; $i -lt 40 -and ($null -eq $dlg -or $dlg -eq [IntPtr]::Zero); $i++) {
        Start-Sleep -Milliseconds 150
        $dlg = (Find-SaveDialog)
    }
    if ($dlg -eq [IntPtr]::Zero) { throw "Save dialog did not open" }
    [ZdNative]::SetForegroundWindow($dlg) | Out-Null
    Start-Sleep -Milliseconds 600
    [System.Windows.Forms.SendKeys]::SendWait("^a")
    [System.Windows.Forms.SendKeys]::SendWait("$demo\Out\Delivery.zip")
    # Close the dialog BEFORE recording again: ffmpeg needs ~1 s to start capturing, and the dialog
    # shows the user's name and folders. Wait until it is really gone.
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 150
        $dlg = (Find-SaveDialog)
        if ($dlg -eq [IntPtr]::Zero) { break }
    }
    if ($dlg -ne [IntPtr]::Zero) { throw "Save dialog still open: refusing to record it" }
    Start-Sleep -Milliseconds 300
    $rec = Start-Recording $seg2
    Start-Sleep 1.0

    # Finale: drag the finished ZIP straight out of the basket into the Explorer window.
    $ov = $AE::FromHandle((Get-ZipDropWindow))
    $texts = $ov.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    $chip = @($texts) | Where-Object { $_.Current.Name -like "*.zip" -and -not $_.Current.IsOffscreen } | Select-Object -First 1
    if ($chip) {
        $cr = $chip.Current.BoundingRectangle
        [ZdNative]::Glide([int]($cr.X + $cr.Width / 2), [int]($cr.Y + $cr.Height / 2), 600); Start-Sleep -Milliseconds 400
        [ZdNative]::Down(); Start-Sleep -Milliseconds 150
        [ZdNative]::Glide([int]($cr.X - 20), [int]($cr.Y + 20), 200)
        [ZdNative]::Glide($RegionX + 160, $RegionY + 330, 900); Start-Sleep -Milliseconds 700
        [ZdNative]::Up()
        Start-Sleep 2.5
    }
}
finally {
    [ZdNative]::Up()
    Stop-Recording $rec
    $backdrop.Close()
    $explorer.Quit()
}

# ---------- encode ----------
$raw = "$root\artifacts\media\demo-raw.mkv"
& $FFmpeg -y -loglevel error -i $seg1 -i $seg2 -filter_complex "[0:v][1:v]concat=n=2:v=1[v]" -map "[v]" -c:v libx264 -preset ultrafast -crf 16 $raw
& $FFmpeg -y -loglevel error -i $raw -vf "fps=30,scale=1120:-2:flags=lanczos" -c:v libx264 -pix_fmt yuv420p -crf 20 -movflags +faststart "$root\artifacts\media\demo.mp4"
& $FFmpeg -y -loglevel error -i $raw -vf "fps=14,scale=820:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=160:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" "$root\docs\media\demo.gif"
Write-Host "Done: docs/media/demo.gif, artifacts/media/demo.mp4"
Get-Item "$root\docs\media\demo.gif", "$root\artifacts\media\demo.mp4" | Select-Object Name, Length
