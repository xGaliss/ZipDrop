param([string]$Name = "shot", [string]$Title = "ZipDrop")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class Snap { public delegate bool P(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
public struct RECT { public int L,T,R,B; }
public static RECT Find(uint pid, string title) { RECT found = new RECT(); EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); var sb=new StringBuilder(256); GetWindowText(h,sb,256); if(p==pid && IsWindowVisible(h) && sb.ToString()==title){ GetWindowRect(h,out found); return false;} return true;}, IntPtr.Zero); return found; } }
"@
[Snap]::SetProcessDPIAware() | Out-Null
$p = Get-Process ZipDrop
$r = [Snap]::Find($p.Id, $Title)
if ($r.R -eq 0) { Write-Host "overlay not visible"; exit 1 }
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$out = Join-Path $PSScriptRoot "$Name.png"; $bmp.Save($out); Write-Host "$out  rect=$($r.L),$($r.T),$($r.R),$($r.B)"
