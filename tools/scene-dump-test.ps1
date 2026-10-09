# Sahne alan dokumu regresyon testi (docs/registry-removal.md Faz 0).
#   1) Editor RPC scene.dumpFields(edit) -> Library/Build/scene-dump.editor.txt
#   2) Build/<Proje>.exe (Windows AOT player) scene-dump.flag ile -> Library/Build/scene-dump.player.txt
#   3) Karsilastirma: tam metin + "gurultu disi" (T pos/rot/scale satirlari ve GO active bayragi haric:
#      editorde layout kosmus, player'da Awake oncesi ham deger).
#   -Baseline: player dokumunu Library/Build/scene-dump.baseline.txt olarak kaydeder.
#   Baseline varsa player dokumu onunla da TAM karsilastirilir (fazlar arasi regresyon).
# On kosul: editor acik (RPC 5157), player exe guncel (RPC player.build).
param(
    [string]$Project = "$PSScriptRoot\..\Projects\Sandbox",
    [int]$Port = 5157,
    [switch]$Baseline
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path $Project).Path
$name = Split-Path $Project -Leaf
$exe = Join-Path $Project "Build\$name.exe"
$out = Join-Path $Project "Library\Build"
New-Item -ItemType Directory -Force $out | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

# 1) editor
$resp = '{"id":1,"cmd":"scene.dumpFields","args":{"which":"edit"}}' | powershell -NoProfile -File "$PSScriptRoot\rpc.ps1" -Port $Port
$j = $resp | ConvertFrom-Json
if (-not $j.ok) { throw "rpc: $($j.error)" }
$editorText = $j.result.text
[IO.File]::WriteAllText("$out\scene-dump.editor.txt", $editorText, $utf8)

# 2) player
if (-not (Test-Path $exe)) { throw "player exe yok: $exe (RPC player.build)" }
$flag = Join-Path $Project "Build\scene-dump.flag"
New-Item -ItemType File -Force $flag | Out-Null
$prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue' # exe stderr'i hata sayilmasin
try { $run = (& $exe 2>&1 | ForEach-Object { "$_" }) -join "`n" } finally { $ErrorActionPreference = $prevEap; Remove-Item $flag -ErrorAction SilentlyContinue }
$lines = $run -split "`r?`n"
$b = [Array]::IndexOf($lines, '[scene-dump-begin]'); $e = [Array]::IndexOf($lines, '[scene-dump-end]')
if ($b -lt 0 -or $e -le $b) { Write-Output $run; throw "player dokumu yok" }
$playerText = ($lines[($b + 1)..($e - 1)] -join "`n") + "`n"
[IO.File]::WriteAllText("$out\scene-dump.player.txt", $playerText, $utf8)

# 3) karsilastirma
function Lines($t) { $t -split "`n" | Where-Object { $_ -ne '' } }
function Strip($ls) { $ls | ForEach-Object { ($_ -replace '^(\s*)T pos=.*$', '$1T <runtime>') -replace ' active=[01]', '' } }
$a = Lines $editorText; $p = Lines $playerText
$full = @(Compare-Object $a $p -SyncWindow 200)
$noise = @(Compare-Object (Strip $a) (Strip $p) -SyncWindow 200)
"editor=$($a.Count) player=$($p.Count) fark(tam)=$($full.Count) fark(gurultu disi)=$($noise.Count)"
$noise | Select-Object -First 40 | ForEach-Object { "  $($_.SideIndicator) $($_.InputObject)" }

$fail = $noise.Count -gt 0
$base = "$out\scene-dump.baseline.txt"
if ($Baseline) {
    [IO.File]::WriteAllText($base, $playerText, $utf8)
    "baseline yazildi: $base"
} elseif (Test-Path $base) {
    $bl = Lines ([IO.File]::ReadAllText($base, $utf8))
    $d = @(Compare-Object $bl $p -SyncWindow 200)
    "baseline fark=$($d.Count)"
    $d | Select-Object -First 40 | ForEach-Object { "  $($_.SideIndicator) $($_.InputObject)" }
    if ($d.Count -gt 0) { $fail = $true }
}
if ($fail) { "SONUC: FAIL"; exit 1 } else { "SONUC: PASS"; exit 0 }
