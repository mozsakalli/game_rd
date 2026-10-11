# Ic golge gorsel testi: 4 kutu (b<r, r=0, b>r kesme, border+dis golge). Editor acikken calistir.
$ErrorActionPreference = 'Stop'
function Rpc($cmd, $a) {
    $o = @{ id = 1; cmd = $cmd; args = $a } | ConvertTo-Json -Compress -Depth 5
    $r = @($o) | & "$PSScriptRoot\rpc.ps1"
    $j = $r | ConvertFrom-Json
    if (-not $j.ok) { throw "$cmd : $($j.error)" }
    return $j.result
}
function SetProps($go, $comp, $props) {
    foreach ($k in $props.Keys) { Rpc 'prop.set' @{ go = $go; comp = $comp; prop = $k; value = $props[$k] } | Out-Null }
}

Rpc 'scene.open' @{ path = 'Scenes/InnerShadowTest.scene' } | Out-Null
$root = (Rpc 'scene.addGo' @{ name = 'Root' }).id
$rc = (Rpc 'comp.add' @{ go = $root; type = 'LayoutBox' }).index
SetProps $root $rc @{ fitScreen = 'true'; layout = 'Horizontal'; spacing = 40; alignChildren = 'Center'; justifyChildren = 'Center'; 'fill.color' = '#EBEBF0FF' }

$cases = @(
    @{ name = 'b<r'; radius = '24 24 24 24'; blur = 8 },
    @{ name = 'r=0'; radius = '0 0 0 0'; blur = 12 },
    @{ name = 'b>r'; radius = '6 6 6 6'; blur = 16 },
    @{ name = 'border'; radius = '16 16 16 16'; blur = 16; border = '3 3 3 3' }
)
foreach ($c in $cases) {
    $go = (Rpc 'scene.addGo' @{ parent = $root; name = $c.name }).id
    $ci = (Rpc 'comp.add' @{ go = $go; type = 'LayoutBox' }).index
    $p = @{ width = 160; height = 160; radius = $c.radius; 'fill.color' = '#FFFFFFFF';
            innerShadowColor = '#0000006E'; innerShadowBlur = $c.blur }
    if ($c.border) { $p.border = $c.border; $p.'borderFill.color' = '#3C3C46FF'; $p.shadowColor = '#00000050'; $p.shadowBlur = 10 }
    SetProps $go $ci $p
}
Rpc 'view.device' @{ name = 'ipad'; orientation = 'landscape' } | Out-Null
$r = Rpc 'sim.run' @{ frames = 5; screenshot = $true; stop = $true }
$r | ConvertTo-Json -Compress -Depth 3





