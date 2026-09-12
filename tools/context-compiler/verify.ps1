param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\.."))
)

$ErrorActionPreference = "Stop"
$output = Join-Path ([System.IO.Path]::GetTempPath()) "game-rd-context-compiler-verify.md"

Push-Location $Root
try {
    dotnet run --project tools/context-compiler -- compile `
        "Fix SceneDoc.ApplyOverrides for added components" `
        --diagnostics test_out.txt `
        --budget 5000 `
        --out $output
    if ($LASTEXITCODE -ne 0) {
        throw "context-compiler exited with code $LASTEXITCODE"
    }

    $text = Get-Content $output -Raw
    $firstRelevant = ([regex]::Match($text, "(?m)^### .+$")).Value
    $checks = [ordered]@{
        TargetFirst = $firstRelevant -eq "### method ApplyOverrides"
        CallerEdge = $text.Contains("called_by: DigitoyEngine.SceneDoc.ExpandInstance")
        SyntaxClean = $text.Contains("syntax_diagnostics: 0")
        RealDiagnostics = $text.Contains("## BUILD/TEST DIAGNOSTICS")
        WithinBudget = $text.Length -le 20000
    }

    $checks.GetEnumerator() | ForEach-Object {
        "{0}={1}" -f $_.Key, $_.Value
    }
    if ($checks.Values -contains $false) {
        throw "context-compiler verification failed"
    }
}
finally {
    Pop-Location
    Remove-Item $output -ErrorAction SilentlyContinue
}