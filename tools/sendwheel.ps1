param([int]$X = 600, [int]$Y = 400, [int]$Clicks = 3)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DpiFix { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[DpiFix]::SetProcessDPIAware() | Out-Null
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W2 {
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
}
"@
$p = Get-Process DigitoyEditor | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "pencere yok"; exit 1 }
[W2]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 200
$wp = [IntPtr]([Int64]($Clicks * 120) -shl 16)
$lp = [IntPtr]([Int64]($Y -shl 16) -bor ($X -band 0xFFFF))
[W2]::PostMessageW($p.MainWindowHandle, 0x020A, $wp, $lp) | Out-Null
Write-Output "wheel gonderildi ($X,$Y clicks=$Clicks)"

