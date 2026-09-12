param([int]$Id = 0)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@
$p = Get-Process DigitoyEditor | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "pencere yok"; exit 1 }
[W]::PostMessageW($p.MainWindowHandle, 0x0111, [IntPtr](1000 + $Id), [IntPtr]::Zero) | Out-Null
Write-Output "WM_COMMAND $(1000+$Id) gonderildi"
