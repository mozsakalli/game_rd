param([int]$X1=600,[int]$Y1=400,[int]$X2=780,[int]$Y2=520)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DpiFix { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[DpiFix]::SetProcessDPIAware() | Out-Null
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W4 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
}
"@
[W4]::SetCursorPos($X1,$Y1) | Out-Null; Start-Sleep -Milliseconds 150
[W4]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
Start-Sleep -Milliseconds 80
for ($i=1; $i -le 8; $i++) {
  $x = $X1 + ($X2-$X1)*$i/8; $y = $Y1 + ($Y2-$Y1)*$i/8
  [W4]::SetCursorPos([int]$x,[int]$y) | Out-Null
  Start-Sleep -Milliseconds 30
}
[W4]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
Write-Output "sol drag ($X1,$Y1)->($X2,$Y2)"

