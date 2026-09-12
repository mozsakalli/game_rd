param([int]$X = 100, [int]$Y = 100)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W5 {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
[W5]::SetProcessDPIAware() | Out-Null
[W5]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)   # CTRL down
Start-Sleep -Milliseconds 60
[W5]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 120
[W5]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[W5]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[W5]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)   # CTRL up
Write-Output "ctrl+click ($X,$Y)"
