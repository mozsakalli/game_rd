using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ContextCompiler;

internal static class Evaluation
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Expected: context-compiler evaluate <suite.json> [options]");
            return 2;
        }

        try
        {
            var options = ParseOptions(args);
            var suite = await LoadSuiteAsync(options.SuitePath);
            var state = await ProjectState.LoadAsync(Path.Combine(options.Root, ".context", "state.json"));
            var changes = await GitChanges.ReadAsync(options.Root);
            var stopwatch = Stopwatch.StartNew();
            var index = await SourceIndex.BuildAsync(options.Root, options.CachePath);
            var results = new List<EvaluationResult>();

            foreach (var testCase in suite.Cases)
            {
                string? activeFile = testCase.ActiveFile == null
                    ? null
                    : Path.GetFullPath(testCase.ActiveFile, options.Root);
                var compileOptions = new CompileOptions(testCase.Task, options.Root, activeFile,
                    null, null, options.CachePath, null, options.TokenBudget);
                var package = ContextPackage.Compile(compileOptions, state, null, changes, index);
                var ranks = testCase.ExpectedSymbols.ToDictionary(
                    expected => expected,
                    expected => FindRank(package.SelectedSymbols, expected),
                    StringComparer.OrdinalIgnoreCase);
                var requiredText = testCase.RequiredText.ToDictionary(
                    expected => expected,
                    expected => package.Content.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
                bool passed = ranks.Values.All(rank => rank > 0 && rank <= testCase.Top)
                    && requiredText.Values.All(found => found);
                results.Add(new EvaluationResult(testCase, package, ranks, requiredText, passed));
            }

            stopwatch.Stop();
            string report = RenderReport(options, index, results, stopwatch.ElapsedMilliseconds);
            if (options.OutputPath == null)
                Console.Write(report);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
                await File.WriteAllTextAsync(options.OutputPath, report);
                Console.Error.WriteLine($"Wrote evaluation report to {options.OutputPath}");
            }
            return results.All(result => result.Passed) ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"context-compiler evaluation failed: {exception.Message}");
            return 2;
        }
    }

    private static EvaluationOptions ParseOptions(string[] args)
    {
        string root = Directory.GetCurrentDirectory();
        string? cachePath = null;
        string? outputPath = null;
        bool noCache = false;
        int budget = 5_000;
        for (int i = 2; i < args.Length; i++)
        {
            string option = args[i];
            string Value()
            {
                if (++i >= args.Length)
                    throw new ArgumentException($"Missing value for {option}");
                return args[i];
            }

            switch (option)
            {
                case "--root": root = Value(); break;
                case "--cache": cachePath = Value(); break;
                case "--no-cache": noCache = true; break;
                case "--out": outputPath = Value(); break;
                case "--budget":
                    if (!int.TryParse(Value(), out budget) || budget < 1_000)
                        throw new ArgumentException("--budget must be an integer of at least 1000 tokens.");
                    break;
                default: throw new ArgumentException($"Unknown evaluate option: {option}");
            }
        }

        root = Path.GetFullPath(root);
        string suitePath = Path.GetFullPath(args[1], root);
        if (!File.Exists(suitePath))
            throw new ArgumentException($"Evaluation suite does not exist: {suitePath}");
        cachePath = noCache ? null : Path.GetFullPath(cachePath ?? ".context/cache/source-index-v1.json", root);
        outputPath = outputPath == null ? null : Path.GetFullPath(outputPath, root);
        return new EvaluationOptions(root, suitePath, cachePath, outputPath, budget);
    }

    private static async Task<EvaluationSuite> LoadSuiteAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EvaluationSuite>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Evaluation suite is empty.");
    }

    private static int FindRank(List<SelectedContextSymbol> symbols, string expected)
    {
        int index = symbols.FindIndex(symbol =>
            symbol.DisplayName.Contains(expected, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index + 1;
    }

    private static string RenderReport(EvaluationOptions options, SourceIndex index,
        List<EvaluationResult> results, long elapsedMilliseconds)
    {
        int passed = results.Count(result => result.Passed);
        double averageTokens = results.Count == 0 ? 0 : results.Average(result => result.Package.EstimatedTokens);
        var ranks = results.SelectMany(result => result.ExpectedRanks.Values).ToList();
        double meanReciprocalRank = ranks.Count == 0 ? 0 : ranks.Average(rank => rank == 0 ? 0 : 1.0 / rank);
        var text = new StringBuilder();
        text.AppendLine("# Context Compiler Evaluation").AppendLine();
        text.AppendLine($"- cases: {results.Count}");
        text.AppendLine($"- passed: {passed}");
        text.AppendLine($"- pass_rate: {(results.Count == 0 ? 0 : passed * 100.0 / results.Count):0.0}%");
        text.AppendLine($"- mean_reciprocal_rank: {meanReciprocalRank:0.000}");
        text.AppendLine($"- average_tokens: {averageTokens:0}");
        text.AppendLine($"- budget_per_case: {options.TokenBudget}");
        text.AppendLine($"- index_cache: {(index.CacheHit ? "hit" : "miss")}");
        text.AppendLine($"- elapsed_ms: {elapsedMilliseconds}").AppendLine();
        foreach (var result in results)
        {
            text.AppendLine($"## {(result.Passed ? "PASS" : "FAIL")} {result.Case.Name}");
            text.AppendLine($"task: {result.Case.Task}");
            text.AppendLine($"tokens: {result.Package.EstimatedTokens}");
            foreach (var expected in result.ExpectedRanks)
                text.AppendLine($"expected: {expected.Key} | rank: {(expected.Value == 0 ? "missing" : expected.Value)} | top: {result.Case.Top}");
            foreach (var expected in result.RequiredText)
                text.AppendLine($"required_text: {expected.Key} | found: {expected.Value.ToString().ToLowerInvariant()}");
            text.AppendLine("selected:");
            foreach (var symbol in result.Package.SelectedSymbols.Take(result.Case.Top))
                text.AppendLine($"- {symbol.DisplayName} [{symbol.File}:{symbol.Line}]");
            text.AppendLine();
        }
        return text.ToString();
    }

    private sealed record EvaluationOptions(string Root, string SuitePath, string? CachePath,
        string? OutputPath, int TokenBudget);
    private sealed record EvaluationResult(EvaluationCase Case, CompiledPackage Package,
        Dictionary<string, int> ExpectedRanks, Dictionary<string, bool> RequiredText, bool Passed);
}

internal sealed class EvaluationSuite
{
    public List<EvaluationCase> Cases { get; init; } = [];
}

internal sealed class EvaluationCase
{
    public string Name { get; init; } = "";
    public string Task { get; init; } = "";
    public string? ActiveFile { get; init; }
    public List<string> ExpectedSymbols { get; init; } = [];
    public List<string> RequiredText { get; init; } = [];
    public int Top { get; init; } = 10;
}