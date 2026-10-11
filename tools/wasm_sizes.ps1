# Lean wasm obj'lerinin CODE/DATA boyut dagilimi (link oncesi, -O2). Kullanim: tools\wasm_sizes.ps1 [objDir]
param([string]$ObjDir = "$PSScriptRoot\..\Projects\Sandbox\Library\aot\wasm\obj")
$size = "C:\emsdk\upstream\bin\llvm-size.exe"
$rows = @()
Get-ChildItem $ObjDir -Recurse -Filter *.o | ForEach-Object {
    $f = $_.Name
    foreach ($l in (& $size -A $_.FullName 2>$null)) {
        if ($l -match '^(CODE|DATA)\s+(\d+)') { $rows += [pscustomobject]@{ File = $f; Sec = $Matches[1]; Size = [int]$Matches[2] } }
    }
}
function Grp($f) {
    if ($f -like 'T_System_Collections*') { 'gen System.Collections' }
    elseif ($f -like 'T_System_*') { 'gen System.other' }
    elseif ($f -like 'T_DigitoyEngine_*') { 'gen DigitoyEngine' }
    elseif ($f -like 'T_*') { 'gen game' }
    elseif ($f -like 'gen_*') { 'gen meta/strings' }
    elseif ($f -match 'sokol|audio|de_fs|de_async|de_net') { 'native shim' }
    else { 'c_runtime' }
}
"toplam obj: $((Get-ChildItem $ObjDir -Recurse -Filter *.o).Count)"
$rows | ForEach-Object { $_ | Add-Member -NotePropertyName Grp -NotePropertyValue (Grp $_.File) -PassThru } |
    Group-Object Grp, Sec | ForEach-Object { [pscustomobject]@{ Grp = $_.Name; KB = [math]::Round(($_.Group | Measure-Object Size -Sum).Sum / 1024) } } |
    Sort-Object KB -Descending | Format-Table -AutoSize
"en buyuk 20 CODE:"
$rows | Where-Object Sec -eq 'CODE' | Sort-Object Size -Descending | Select-Object -First 20 File, @{n = 'KB'; e = { [math]::Round($_.Size / 1024) } } | Format-Table -AutoSize
"en buyuk 10 DATA:"
$rows | Where-Object Sec -eq 'DATA' | Sort-Object Size -Descending | Select-Object -First 10 File, @{n = 'KB'; e = { [math]::Round($_.Size / 1024) } } | Format-Table -AutoSize
