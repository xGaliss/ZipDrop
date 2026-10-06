# Shared helpers for media scripts (Windows PowerShell 5.1). Dot-source: . "$PSScriptRoot\Native.ps1"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
if (-not ("ZdNative" -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Text; using System.Threading; using System.Runtime.InteropServices;
public static class ZdNative {
  public struct RECT { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  public struct POINT { public int X, Y; }

  public static IntPtr FindWindow(uint pid, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (p == pid && IsWindowVisible(h) && sb.ToString() == title) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static RECT RectOf(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
  public static void RaiseTopmost(IntPtr h) { SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
  public static void Place(IntPtr h, int x, int y, int w, int hgt) { SetWindowPos(h, IntPtr.Zero, x, y, w, hgt, 0x0004 | 0x0010); }

  public static void Down() { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); }
  public static void Up() { mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); }
  public static void Click() { Down(); Thread.Sleep(60); Up(); }
  public static void Move(int x, int y) {
    int vl = GetSystemMetrics(76), vt = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
    mouse_event(0x0001 | 0x8000 | 0x4000, (int)Math.Round((x - vl) * 65535.0 / (vw - 1)), (int)Math.Round((y - vt) * 65535.0 / (vh - 1)), 0, IntPtr.Zero);
  }
  /// Eased glide (looks natural in recordings).
  public static void Glide(int x1, int y1, int ms) {
    POINT p; GetCursorPos(out p); int steps = Math.Max(1, ms / 10);
    for (int i = 1; i <= steps; i++) { double t = (double)i / steps; t = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
      Move((int)(p.X + (x1 - p.X) * t), (int)(p.Y + (y1 - p.Y) * t)); Thread.Sleep(10); }
  }
  public static void Shake(int amplitude, int swings, int msPerSwing) {
    POINT p; GetCursorPos(out p);
    for (int i = 0; i < swings; i++) { int target = (i % 2 == 0) ? p.X + amplitude : p.X; int steps = Math.Max(1, msPerSwing / 8);
      POINT c; GetCursorPos(out c);
      for (int s = 1; s <= steps; s++) { Move(c.X + (target - c.X) * s / steps, p.Y); Thread.Sleep(8); } }
  }
}
"@
}
[ZdNative]::SetProcessDPIAware() | Out-Null

function Get-ZipDropWindow([string]$Title = "ZipDrop") {
    $p = Get-Process ZipDrop -ErrorAction Stop | Select-Object -First 1
    [ZdNative]::FindWindow([uint32]$p.Id, $Title)
}

# A borderless gradient "wallpaper" window behind the area we capture.
function Show-Backdrop([int]$X, [int]$Y, [int]$W, [int]$H) {
    $form = New-Object System.Windows.Forms.Form
    $form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.ShowInTaskbar = $false
    $form.Location = New-Object System.Drawing.Point $X, $Y; $form.Size = New-Object System.Drawing.Size $W, $H
    $form.Add_Paint({
        param($s, $e)
        $rect = $s.ClientRectangle
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 34, 38, 74)), ([System.Drawing.Color]::FromArgb(255, 112, 64, 140)), 35
        $e.Graphics.FillRectangle($brush, $rect)
        $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
        $glow.AddEllipse([int]($rect.Width * 0.55), [int](-$rect.Height * 0.3), [int]($rect.Width * 0.7), [int]($rect.Height * 1.0))
        $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
        $pgb.CenterColor = [System.Drawing.Color]::FromArgb(110, 120, 140, 255); $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 120, 140, 255))
        $e.Graphics.FillPath($pgb, $glow)
    })
    $form.Show(); [System.Windows.Forms.Application]::DoEvents()
    return $form
}

function Save-Region([int]$X, [int]$Y, [int]$W, [int]$H, [string]$Path) {
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($X, $Y, 0, 0, $bmp.Size); $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

# The native Save dialog (any UI language). Owned top-level window: UIA lists it under its owner,
# so look it up with EnumWindows instead. Returns IntPtr.Zero when it isn't open.
function Find-SaveDialog {
    foreach ($t in @("Save ZIP", "Guardar ZIP")) {
        $h = Get-ZipDropWindow $t
        if ($h -ne [IntPtr]::Zero) { return $h }
    }
    return [IntPtr]::Zero
}
