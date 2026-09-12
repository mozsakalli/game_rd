param([int]$VK = 0x58)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W6 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@
[W6]::keybd_event([byte]$VK, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 50
[W6]::keybd_event([byte]$VK, 0, 2, [IntPtr]::Zero)
Write-Output "key $VK"
