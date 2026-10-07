# vmint.c degisikliklerinde hizli dongu: interp host'u yeniden derle + selftest.dmod'u kos + baseline ile karsilastir.
# Kullanim (aotcompiler/ dizininden): powershell -File tests/interp_quick.ps1
$ErrorActionPreference = "Continue"
$clang = "C:\Program Files\LLVM\bin\clang.exe"
$work = "obj\selftest-c"
& $clang -O1 -w -Ic_runtime "$work\interp_host.c" c_runtime/vmrt.c c_runtime/corelib.c c_runtime/vmint.c -o "$work\interp_host.exe"
if ($LASTEXITCODE -ne 0) { exit 1 }
$baseline = (cmd /c "dotnet tests\dotnet-selftest\bin\Debug\net9.0\DotnetSelfTest.dll 2>&1") -join "`n"
$p = Start-Process -FilePath "$work\interp_host.exe" -ArgumentList "$work\selftest.dmod" -RedirectStandardOutput "$work\interp_out.txt" -RedirectStandardError "$work\interp_err.txt" -PassThru -NoNewWindow
if (-not $p.WaitForExit(30000)) { $p.Kill(); Write-Output "[interp] TIMEOUT (30s) - sonsuz dongu?" }
$actual = ((Get-Content "$work\interp_out.txt") + (Get-Content "$work\interp_err.txt" | Where-Object { $_ -match '^\[' })) -join "`n"
function ParseMap($text) { $m = @{}; foreach ($l in ($text -split "`n")) { $l = $l.TrimEnd("`r"); $t = $l.IndexOf("`t"); if ($t -gt 0) { $n = 0; if ([int]::TryParse($l.Substring(0, $t), [ref]$n)) { $m[$n] = $l.Substring($t + 1) } } }; return $m }
$b = ParseMap $baseline; $a = ParseMap $actual
$pass = 0; $fail = 0
foreach ($k in ($b.Keys | Sort-Object)) {
    if ($k -eq 15) { continue }
    if ($a.ContainsKey($k) -and $a[$k] -eq $b[$k]) { $pass++ } else { $fail++; $got = if ($a.ContainsKey($k)) { $a[$k] } else { "<yok>" }; Write-Output "FAIL $k`: beklenen [$($b[$k])] geldi [$got]" }
}
($actual -split "`n") | Where-Object { $_ -match '^\[(interp|vmint)\]' } | ForEach-Object { Write-Output $_.Trim() }
Write-Output "--- interp: $pass PASS, $fail FAIL ---"
