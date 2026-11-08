# TekelOkey ESKI asset'lerini (digiplay-net) yeni projeye tasir. TEKRAR CALISTIRILABILIR (kopya = uzerine yaz).
#  - atlas-work/<grup>/*.png  -> Assets/Textures/<grup>/<ad>.png   (uretilmis atlas/ ALINMAZ; kendi atlasimizi sonra kurariz)
#      dosya adindaki "#x,y,w,h" (eski Scale9Grid: esneyen orta dikdortgen, doku pikseli) sokulur -> slices.json
#  - img/*                    -> Assets/Textures/img/
#  - font/*.ttf               -> Assets/Fonts/  + fonts.register (eski MSDFont face adlari: regular/bold/primary)
#  - lang/{tr,en,de}.json     -> loc.addLanguage + loc.set (editor acik olmali, RPC 5157)
param(
    [string]$Src = 'C:\Work\digitoygames\digiplay-net\TekelOkey\Assets',
    [string]$Dst = (Join-Path $PSScriptRoot '..\..\Projects\TekelOkey\Assets'),
    [switch]$SkipRpc,
    [switch]$OnlyLoc
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$Dst = (Resolve-Path $Dst).Path
$rpc = Join-Path $PSScriptRoot '..\rpc.ps1'

function Rpc([string[]]$lines) { $lines | & $rpc }

if (-not $OnlyLoc) {
# --- 1. parca png'ler ---
$slices = [ordered]@{}
$count = 0
foreach ($grp in Get-ChildItem (Join-Path $Src 'atlas-work') -Directory) {
    $out = Join-Path $Dst "Textures\$($grp.Name)"
    New-Item -ItemType Directory -Force $out | Out-Null
    foreach ($f in Get-ChildItem $grp.FullName -File -Recurse | Where-Object { $_.Extension -in '.png', '.jpg' }) {
        $base = $f.BaseName; $grid = $null
        $hash = $base.IndexOf('#')
        if ($hash -ge 0) {
            # "bg-timer#32,22,4,0.png.png" gibi cift uzantili bozuk adlar: yalniz sayisal kismi al
            $m = [regex]::Match($base.Substring($hash + 1), '\d+,\d+,\d+,\d+')
            if (-not $m.Success) { throw "grid okunamadi: $($f.Name)" }
            $grid = $m.Value; $base = $base.Substring(0, $hash)
        }
        $target = Join-Path $out ($base + $f.Extension)
        Copy-Item $f.FullName $target -Force
        $count++
        if ($grid) {
            $bmp = [System.Drawing.Bitmap]::FromFile($f.FullName)
            $n = $grid.Split(',') | ForEach-Object { [int]$_ }
            # Eski grid x,y,w,h -> yeni LayoutBox slice9 left,top,right,bottom (doku pikseli)
            $slices["$($grp.Name)/$base"] = [ordered]@{
                grid = $grid; width = $bmp.Width; height = $bmp.Height
                left = $n[0]; top = $n[1]
                right = [Math]::Max(0, $bmp.Width - $n[0] - $n[2])
                bottom = [Math]::Max(0, $bmp.Height - $n[1] - $n[3])
            }
            $bmp.Dispose()
        }
    }
}
$imgOut = Join-Path $Dst 'Textures\img'
New-Item -ItemType Directory -Force $imgOut | Out-Null
foreach ($f in Get-ChildItem (Join-Path $Src 'img') -File) { Copy-Item $f.FullName (Join-Path $imgOut $f.Name) -Force; $count++ }
$slices | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $Dst 'Textures\slices.json') -Encoding UTF8
Write-Host "textures: $count dosya, $($slices.Count) slice9 kaydi"

# --- 2. fontlar ---
$fontOut = Join-Path $Dst 'Fonts'
New-Item -ItemType Directory -Force $fontOut | Out-Null
foreach ($f in Get-ChildItem (Join-Path $Src 'font') -Filter *.ttf) { Copy-Item $f.FullName (Join-Path $fontOut $f.Name) -Force }
# Eski: MSDFont.Register(msdf.json, {"regular:0.83","bold:0.83","primary"}) — tek sheet'te 3 face, ttf eslemesi
# dosyada yok; VARSAYIM (gorsel dilimde dogrulanacak): regular=NotoSans-Regular, bold=NotoSans-Black, primary=BebasNeue.
$faces = @(
    @{ name = 'regular'; file = 'NotoSans-Regular.ttf'; default = $false },
    @{ name = 'bold';    file = 'NotoSans-Black.ttf';   default = $false },
    @{ name = 'primary'; file = 'BebasNeue-Regular.ttf'; default = $true }
)
Write-Host "fonts: $((Get-ChildItem $fontOut).Count) ttf"

if ($SkipRpc) { return }

# Editor watcher'i yeni dosyalari gorsun diye kisa bekle
Start-Sleep -Seconds 3
foreach ($fc in $faces) {
    $mk = if ($fc.default) { 'true' } else { 'false' }
    Rpc @("{""cmd"":""fonts.register"",""args"":{""path"":""Fonts/$($fc.file)"",""name"":""$($fc.name)"",""makeDefault"":$mk}}")
}
} # -not $OnlyLoc

# --- 3. yerellestirme ---
# tr kaynak dil ([0] = fallback; eski oyunun varsayilani tr, en/de eksik anahtarlar tr'ye duser).
# Tablo editor tarafindan varsayilan 'en' ile yaratilmis olabilir -> loc.setSource tr sutununu one tasir.
$langs = @('tr', 'en', 'de')
foreach ($l in $langs) { Rpc @("{""cmd"":""loc.addLanguage"",""args"":{""code"":""$l""}}") }
Rpc @('{"cmd":"loc.setSource","args":{"code":"tr"}}')
Rpc @('{"cmd":"loc.setLanguage","args":{"lang":"tr"}}')
$tables = @{}
foreach ($l in $langs) {
    $tables[$l] = Get-Content (Join-Path $Src "lang\$l.json") -Raw -Encoding UTF8 | ConvertFrom-Json
}
$entries = New-Object System.Collections.ArrayList
foreach ($p in $tables['tr'].PSObject.Properties) {
    $vals = [ordered]@{}
    foreach ($l in $langs) {
        $v = $tables[$l].PSObject.Properties[$p.Name]
        if ($v) { $vals[$l] = [string]$v.Value }
    }
    [void]$entries.Add([ordered]@{ key = $p.Name; values = $vals })
}
# Buyuk tablo: 100'luk paketler (tek satir JSON)
for ($i = 0; $i -lt $entries.Count; $i += 100) {
    $chunk = @($entries[$i..([Math]::Min($i + 99, $entries.Count - 1))])
    $json = @{ cmd = 'loc.set'; args = @{ entries = $chunk } } | ConvertTo-Json -Depth 6 -Compress
    Rpc @($json)
}
Write-Host "loc: $($entries.Count) anahtar x $($langs.Count) dil"
