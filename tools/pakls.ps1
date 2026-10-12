# Pak index dokumu: giris basina stored/raw boyut, buyukten kucuge. Kullanim: tools\pakls.ps1 <pak> [-Top 40]
param([Parameter(Mandatory)][string]$Pak, [int]$Top = 40)
$b = [IO.File]::ReadAllBytes((Resolve-Path $Pak))
if ([BitConverter]::ToInt32($b, 0) -ne 0x334B5044) { throw "DPK3 degil" }
$count = [BitConverter]::ToInt32($b, 4); $indexLen = [BitConverter]::ToInt32($b, 8)
$p = 12
function Str { $len = [BitConverter]::ToUInt16($b, $script:p); $script:p += 2; $s = [Text.Encoding]::UTF8.GetString($b, $script:p, $len); $script:p += $len; $s }
$rows = for ($i = 0; $i -lt $count; $i++) {
    $key = Str; $guid = Str
    $off = [BitConverter]::ToInt64($b, $p); $p += 8
    $stored = [BitConverter]::ToInt64($b, $p); $p += 8
    $raw = [BitConverter]::ToInt64($b, $p); $p += 8
    $nDep = [BitConverter]::ToUInt16($b, $p); $p += 2
    for ($d = 0; $d -lt $nDep; $d++) { Str | Out-Null }
    [pscustomobject]@{ Key = $key; StoredKB = [math]::Round($stored / 1KB, 1); RawKB = [math]::Round($raw / 1KB, 1); Ratio = if ($raw -gt 0) { [math]::Round(100.0 * $stored / $raw) } else { 0 } }
}
"{0} giris, index {1} KB, dosya {2} KB" -f $count, [math]::Round($indexLen / 1KB, 1), [math]::Round($b.Length / 1KB, 1)
$rows | Sort-Object StoredKB -Descending | Select-Object -First $Top | Format-Table -AutoSize
