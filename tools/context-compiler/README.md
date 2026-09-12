# Context Compiler

Builds a task-specific Markdown context package from structured project state, Git changes,
and C# syntax. It does not call an LLM.

## Usage

From the repository root:

```powershell
dotnet run --project tools/context-compiler -- compile `
  "Fix SceneDoc.ApplyOverrides for added components" `
  --file engine/managed/Serialization/SceneDoc.Prefab.cs `
  --diagnostics test_out.txt `
  --budget 8000 `
  --out .context/compiled.md
```

Options:

- `--root`: repository root; defaults to the current directory.
- `--file`: active or primary source file, used as a relevance signal.
- `--state`: structured state JSON; defaults to `.context/state.json`.
- `--diagnostics`: an existing build or test output file. Only result, error, and warning lines are retained.
- `--cache`: semantic-index cache path; defaults to `.context/cache/source-index-v1.json`.
- `--no-cache`: build the semantic index without reading or writing a cache.
- `--budget`: maximum estimated input tokens; defaults to `12000`.
- `--out`: output path; stdout is used when omitted.

The estimate deliberately uses the conservative and tokenizer-independent approximation of
four characters per token. Provider token counts may differ.

## State

`.context/state.json` separates durable facts and decisions from unverified hypotheses. Keep it
short and update it after meaningful decisions or validations. Evidence paths make stale facts
detectable without placing entire source files in the state.

## Selection

The current MVP parses C# with Roslyn and ranks declarations using:

- exact task identifiers and dotted identifier parts;
- active-file proximity;
- changed-file proximity;
- semantic caller, callee, field, property, and type relationships;
- lexical references to high-ranked declarations as a fallback;
- test-file proximity.

Target method bodies are emitted in full. Type declarations are reduced to headers and member
inventories. Each code block identifies its semantic symbol and relevant source-level relationships.

Task terms use simple plural normalization and compound-identifier matching, so natural-language
terms such as `rebuild` can match the `Build` part of `RequestBuild`. Methods that cover several
distinct task terms receive a bounded coordination bonus.

The semantic graph uses a synthetic compilation, so its compilation-level diagnostics are not
treated as authoritative across project boundaries. Syntax diagnostics are safe and included
automatically. Supply actual build or test output when diagnostic evidence is needed:

```powershell
dotnet build editor -v minimal --nologo *> .context/build.log
dotnet run --project tools/context-compiler -- compile `
  "Fix the current build errors" --diagnostics .context/build.log
```

## Cache

The disk cache stores the Roslyn source index and semantic graph as versioned JSON. Its key is a
SHA-256 fingerprint of ordered relative C# source paths and contents. Any source addition, removal,
rename, or content change invalidates the whole index; `.context`, build outputs, dependencies, and
vendored source trees are excluded. Writes use a temporary file followed by an atomic replacement.

The generated package reports `index_cache: hit` or `miss`. To keep independent indexes or place the
cache outside the repository:

```powershell
dotnet run --project tools/context-compiler -- compile `
  "Fix tween loop completion" --cache "$env:TEMP/game-rd-index.json"
```

On this repository, the initial implementation measured 6,657 ms cold and 2,024 ms warm while
preserving the selected prefab target. Timings vary by machine and repository state.

## Evaluation

The deterministic, LLM-free suite covers prefab overrides, script rebuilds, hierarchy ordering,
tweens, asset deserialization, and GUI event passes. Each case asserts that expected symbols occur
within its retrieval depth and that required semantic relationship text survives budget packing.
The report includes pass rate, mean reciprocal rank, token usage, cache state, latency, and the
selected symbols for every case.

```powershell
dotnet run --project tools/context-compiler -- evaluate `
  tools/context-compiler/eval-cases.json `
  --budget 5000 `
  --out .context/evaluation.md
```

The current baseline is 6/6 cases, 0.607 mean reciprocal rank, 4,965 average estimated tokens,
6,751 ms cold, and 2,441 ms warm. The task definitions and expectations live in
`eval-cases.json`; run the suite after changing parsing, scoring, graph expansion, or packing.

Run the repository smoke verification with:

```powershell
./tools/context-compiler/verify.ps1
```