# mingw spike (docs/editor-distribution.md Faz 0a): llvm-mingw ile (1) runtime + selftest generated.c derlenip baseline'la
# eslesiyor mu, (2) player native kaynaklari (sokol/glfw/ses/de_fs) + player generated.c derlenip linkleniyor mu.
# Kullanim (aotcompiler/ dizininden, once AOT_MODULES=1 dotnet run ve dotnet run -- player kosmus olmali):
#   powershell -File tests/mingw_spike.ps1 [-Toolchain C:\DigitoyPublish\toolchains\llvm-mingw-20261006-ucrt-x86_64]
param([string]$Toolchain = "C:\DigitoyPublish\cache\llvm-mingw")
$ErrorActionPreference = "Continue"
$clang = Join-Path $Toolchain "bin\clang.exe"
$ar = Join-Path $Toolchain "bin\llvm-ar.exe"
if (-not (Test-Path $clang)) { Write-Output "clang yok: $clang"; exit 1 }
$out = "obj\mingw-spike"; New-Item -ItemType Directory -Force -Path $out | Out-Null
& $clang --version | Select-Object -First 1

# --- 1) runtime lib (release define seti) ---
Write-Output "== runtime lib"
foreach ($f in @("vmrt", "corelib", "vmint")) {
    & $clang -c -O1 -w -Ic_runtime "c_runtime\$f.c" -o "$out\$f.o"
    if ($LASTEXITCODE -ne 0) { Write-Output "FAIL: $f.c derlenemedi"; exit 1 }
}
& $ar rcs "$out\digitoyengine_runtime.a" "$out\vmrt.o" "$out\corelib.o" "$out\vmint.o"
Write-Output "ok   runtime .a"

# --- 2) selftest: generated.c + runtime lib -> kos -> baseline ---
Write-Output "== selftest"
& $clang -O1 -w -Ic_runtime obj\selftest-c\generated.c "$out\digitoyengine_runtime.a" -o "$out\selftest.exe"
if ($LASTEXITCODE -ne 0) { Write-Output "FAIL: selftest link"; exit 1 }
$baseline = (cmd /c "dotnet tests\dotnet-selftest\bin\Debug\net9.0\DotnetSelfTest.dll 2>&1") -join "`n"
$p = Start-Process -FilePath "$out\selftest.exe" -RedirectStandardOutput "$out\st_out.txt" -RedirectStandardError "$out\st_err.txt" -PassThru -NoNewWindow
if (-not $p.WaitForExit(60000)) { $p.Kill(); Write-Output "FAIL: selftest TIMEOUT" }
$actual = (Get-Content "$out\st_out.txt") -join "`n"
function ParseMap($text) { $m = @{}; foreach ($l in ($text -split "`n")) { $l = $l.TrimEnd("`r"); $t = $l.IndexOf("`t"); if ($t -gt 0) { $n = 0; if ([int]::TryParse($l.Substring(0, $t), [ref]$n)) { $m[$n] = $l.Substring($t + 1) } } }; return $m }
$b = ParseMap $baseline; $a = ParseMap $actual; $pass = 0; $fail = 0
foreach ($k in ($b.Keys | Sort-Object)) { if ($k -eq 15) { continue }; if ($a.ContainsKey($k) -and $a[$k] -eq $b[$k]) { $pass++ } else { $fail++; Write-Output "FAIL $k`: beklenen [$($b[$k])] geldi [$(if ($a.ContainsKey($k)) { $a[$k] } else { '<yok>' })]" } }
Write-Output "--- selftest (mingw): $pass PASS, $fail FAIL (AOT/MSVC referans: 50/53) ---"
Get-Content "$out\st_err.txt" | Select-Object -First 5

# --- 3) interp host (vmint) ---
if (Test-Path obj\selftest-c\interp_host.c) {
    Write-Output "== interp host"
    & $clang -O1 -w -Ic_runtime obj\selftest-c\interp_host.c "$out\digitoyengine_runtime.a" -o "$out\interp_host.exe"
    if ($LASTEXITCODE -eq 0) {
        $p = Start-Process -FilePath "$out\interp_host.exe" -ArgumentList "obj\selftest-c\selftest.dmod" -RedirectStandardOutput "$out\ih_out.txt" -RedirectStandardError "$out\ih_err.txt" -PassThru -NoNewWindow
        if (-not $p.WaitForExit(60000)) { $p.Kill(); Write-Output "FAIL: interp TIMEOUT" }
        $a2 = ParseMap ((Get-Content "$out\ih_out.txt") -join "`n"); $pass = 0; $fail = 0
        foreach ($k in ($b.Keys | Sort-Object)) { if ($k -eq 15) { continue }; if ($a2.ContainsKey($k) -and $a2[$k] -eq $b[$k]) { $pass++ } else { $fail++ } }
        Write-Output "--- interp (mingw): $pass PASS, $fail FAIL ---"
        Get-Content "$out\ih_err.txt" | Where-Object { $_ -match '^\[(interp|vmint)\]' } | Select-Object -First 6
    } else { Write-Output "FAIL: interp host link" }
}

# --- 4) player native lib (sokol+glfw+ses+de_fs) + player generated.c ---
Write-Output "== player native lib"
$native = "..\engine\native"
$glfw = @("context","init","input","monitor","platform","vulkan","window","win32_init","win32_joystick","win32_module","win32_monitor","win32_time","win32_thread","win32_window","wgl_context","egl_context","osmesa_context","null_init","null_joystick","null_monitor","null_window")
$objs = @()
foreach ($src in @("sokol_shim","audio_shim","de_fs")) {
    & $clang -c -O1 -w -DSOKOL_GLCORE -DSOKOL_IMPL -D_GLFW_WIN32 "-I$native" "$native\$src.c" -o "$out\n_$src.o"
    if ($LASTEXITCODE -ne 0) { Write-Output "FAIL: $src.c (mingw)"; exit 1 }
    $objs += "$out\n_$src.o"
}
# platform host (docs/platform-hosts.md): de_app.c (olay kuyrugu/safepoint) + host_desktop.c (GLFW dongusu, main)
foreach ($src in @("de_app","host_desktop")) {
    & $clang -c -O1 -w -DSOKOL_GLCORE -Ic_runtime "-I$native" "-I$native\glfw-master\include" "c_runtime\$src.c" -o "$out\h_$src.o"
    if ($LASTEXITCODE -ne 0) { Write-Output "FAIL: $src.c (mingw)"; exit 1 }
    $objs += "$out\h_$src.o"
}
foreach ($g in $glfw) {
    & $clang -c -O1 -w -D_GLFW_WIN32 "-I$native" "$native\glfw-master\src\$g.c" -o "$out\g_$g.o"
    if ($LASTEXITCODE -ne 0) { Write-Output "FAIL: glfw $g.c (mingw)"; exit 1 }
    $objs += "$out\g_$g.o"
}
& $ar rcs "$out\digitoyengine_static.a" ($objs + @("$out\vmrt.o", "$out\corelib.o", "$out\vmint.o"))
Write-Output "ok   native+runtime .a ($($objs.Count + 3) obj)"
if (Test-Path obj\player-c\generated.c) {
    Write-Output "== player exe"
    & $clang -O1 -w -Ic_runtime obj\player-c\generated.c "$out\digitoyengine_static.a" -lopengl32 -lgdi32 -luser32 -lkernel32 -lshell32 -lole32 -loleaut32 -lmfplat -lmfuuid -luuid -o "$out\player.exe"
    if ($LASTEXITCODE -eq 0) { Write-Output "ok   player.exe ($([math]::Round((Get-Item "$out\player.exe").Length/1KB)) KB)" } else { Write-Output "FAIL: player link" }
}
