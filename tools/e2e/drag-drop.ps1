param(
    [string]$Files,
    [int]$TargetX, [int]$TargetY,      # where to release (physical px)
    [int]$SourceX = 300, [int]$SourceY = 300,
    [switch]$Shake                      # shake halfway through the drag
)
# Simulates a real OLE drag (like Explorer) from a throwaway form, moving the cursor with mouse_event.
# The worker thread ALWAYS releases the button at the end, so the mouse can never stay stuck.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Threading;
public static class Mouse {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  public static void Down() { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); }
  public static void Up() { mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); }
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  public static void Move(int x, int y) {
    int vl = GetSystemMetrics(76), vt = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
    int nx = (int)Math.Round((x - vl) * 65535.0 / (vw - 1)), ny = (int)Math.Round((y - vt) * 65535.0 / (vh - 1));
    mouse_event(0x0001 | 0x8000 | 0x4000, nx, ny, 0, IntPtr.Zero);
  }
  public static void Glide(int x0, int y0, int x1, int y1, int ms) {
    int steps = Math.Max(1, ms / 8);
    for (int i = 1; i <= steps; i++) { Move(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps); Thread.Sleep(8); }
  }
  public static void Script(int sx, int sy, int tx, int ty, bool shake) {
    new Thread(() => { Thread.Sleep(15000); Up(); Environment.Exit(2); }) { IsBackground = true }.Start();
    new Thread(() => {
      try {
        Thread.Sleep(300);
        int mx = (sx + tx) / 2, my = (sy + ty) / 2;
        Glide(sx, sy, mx, my, 300);
        if (shake) {
          for (int i = 0; i < 6; i++) { Glide(mx, my, mx + 90, my, 70); Glide(mx + 90, my, mx, my, 70); }
          Thread.Sleep(500);
        }
        Glide(mx, my, tx, ty, 300);
        Thread.Sleep(400);
      } finally { Up(); }
    }) { IsBackground = true }.Start();
  }
}
"@
[Mouse]::SetProcessDPIAware() | Out-Null

$form = New-Object System.Windows.Forms.Form
$form.StartPosition = 'Manual'; $form.FormBorderStyle = 'None'; $form.TopMost = $true
$form.Size = New-Object System.Drawing.Size 120, 60
$form.Location = New-Object System.Drawing.Point ($SourceX - 60), ($SourceY - 30)
$form.BackColor = [System.Drawing.Color]::Orange
$global:effect = 'none'
$form.Add_Shown({
    $form.Refresh()
    [Mouse]::Move($SourceX, $SourceY); Start-Sleep -Milliseconds 200; [Mouse]::Down(); Start-Sleep -Milliseconds 100; [System.Windows.Forms.Application]::DoEvents()
    $data = New-Object System.Windows.Forms.DataObject
    $list = New-Object System.Collections.Specialized.StringCollection
    foreach ($f in $Files.Split("|")) { [void]$list.Add($f) }
    $data.SetFileDropList($list)
    [Mouse]::Script($SourceX, $SourceY, $TargetX, $TargetY, $Shake.IsPresent)
    $global:effect = $form.DoDragDrop($data, [System.Windows.Forms.DragDropEffects]'Copy, Move, Link')
    $form.Close()
})
[System.Windows.Forms.Application]::Run($form)
Write-Host "Drop effect returned to source: $global:effect"
