param([int]$Iters=15)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DpiFix2 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[DpiFix2]::SetProcessDPIAware() | Out-Null
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W5 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
}
"@
function Drag([int]$x1,[int]$y1,[int]$x2,[int]$y2) {
  [W5]::SetCursorPos($x1,$y1) | Out-Null; Start-Sleep -Milliseconds 60
  [W5]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 40
  for ($i=1; $i -le 12; $i++) {
    $x = $x1 + ($x2-$x1)*$i/12; $y = $y1 + ($y2-$y1)*$i/12
    [W5]::SetCursorPos([int]$x,[int]$y) | Out-Null
    Start-Sleep -Milliseconds 15
  }
  [W5]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 60
}
function Alive { return $null -ne (Get-Process DigitoyEditor -ErrorAction SilentlyContinue) }
for ($n=0; $n -lt $Iters; $n++) {
  if (-not (Alive)) { Write-Output "CRASH iter=$n (pre)"; exit 1 }
  # sol dikey splitter ileri-geri
  Drag 487 700 900 700; Drag 900 700 300 700; Drag 300 700 487 700
  if (-not (Alive)) { Write-Output "CRASH iter=$n (left-v)"; exit 1 }
  # sag dikey splitter (Scene|Inspector)
  Drag 1610 470 1900 470; Drag 1900 470 1200 470; Drag 1200 470 1610 470
  if (-not (Alive)) { Write-Output "CRASH iter=$n (right-v)"; exit 1 }
  # yatay splitter (ust|alt)
  Drag 1453 830 1453 1200; Drag 1453 1200 1453 400; Drag 1453 400 1453 830
  if (-not (Alive)) { Write-Output "CRASH iter=$n (horiz)"; exit 1 }
  Write-Output "iter $n ok"
}
Write-Output "DONE no crash"
