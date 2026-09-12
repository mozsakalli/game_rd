param([int]$X = 100, [int]$Y = 100)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DpiFix { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[DpiFix]::SetProcessDPIAware() | Out-Null
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W3 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
}
"@
[W3]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 150
[W3]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[W3]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Write-Output "click ($X,$Y)"

