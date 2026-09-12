using System.Text.Json;

namespace ContextCompiler;

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        if (args[0] == "evaluate")
            return await Evaluation.RunAsync(args);

        if (args[0] != "compile" || args.Length < 2)
        {
            Console.Error.WriteLine("Expected: context-compiler compile <task> [options]");
            return 2;
        }

        try
        {
            var options = ParseOptions(args);
            var state = await ProjectState.LoadAsync(options.StatePath);
            var buildDiagnostics = await BuildDiagnostics.LoadAsync(options.DiagnosticsPath);
            var changes = await GitChanges.ReadAsync(options.Root);
            var index = await SourceIndex.BuildAsync(options.Root, options.CachePath);
            var package = ContextPackage.Compile(options, state, buildDiagnostics, changes, index);

            if (options.OutputPath == null)
            {
                Console.Write(package.Content);
            }
            else
            {
                string fullOutput = Path.GetFullPath(options.OutputPath, options.Root);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
                await File.WriteAllTextAsync(fullOutput, package.Content);
                Console.Error.WriteLine($"Wrote {package.EstimatedTokens:N0} estimated tokens to {fullOutput}");
            }

            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"context-compiler failed: {exception.Message}");
            return 1;
        }
    }

    private static CompileOptions ParseOptions(string[] args)
    {
        string root = Directory.GetCurrentDirectory();
        string? activeFile = null;
        string? statePath = null;
        string? diagnosticsPath = null;
        string? cachePath = null;
        bool noCache = false;
        string? outputPath = null;
        int budget = 12_000;

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
                case "--file": activeFile = Value(); break;
                case "--state": statePath = Value(); break;
                case "--diagnostics": diagnosticsPath = Value(); break;
                case "--cache": cachePath = Value(); break;
                case "--no-cache": noCache = true; break;
                case "--out": outputPath = Value(); break;
                case "--budget":
                    if (!int.TryParse(Value(), out budget) || budget < 1_000)
                        throw new ArgumentException("--budget must be an integer of at least 1000 tokens.");
                    break;
                default: throw new ArgumentException($"Unknown option: {option}");
            }
        }

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new ArgumentException($"Repository root does not exist: {root}");

        activeFile = ResolveOptionalPath(root, activeFile);
        statePath = ResolveOptionalPath(root, statePath ?? ".context/state.json");
        diagnosticsPath = ResolveOptionalPath(root, diagnosticsPath);
        cachePath = noCache ? null : ResolveOptionalPath(root, cachePath ?? ".context/cache/source-index-v1.json");
        return new CompileOptions(args[1], root, activeFile, statePath, diagnosticsPath, cachePath, outputPath, budget);
    }

    private static string? ResolveOptionalPath(string root, string? path) =>
        path == null ? null : Path.GetFullPath(path, root);

    private static void PrintHelp()
    {
        Console.WriteLine("""
            context-compiler compile <task> [options]
            context-compiler evaluate <suite.json> [options]

              --root <path>     Repository root (default: current directory)
              --file <path>     Active or primary source file
              --state <path>    Structured project state JSON (default: .context/state.json)
              --diagnostics <path>  Real build/test output to include
              --cache <path>    Semantic index cache (default: .context/cache/source-index-v1.json)
              --no-cache        Disable reading and writing the semantic index cache
              --budget <tokens> Maximum estimated input tokens (default: 12000)
              --out <path>      Write Markdown output instead of stdout

                        Evaluate options: --root, --cache, --no-cache, --budget, --out
            """);
    }
}

internal sealed record CompileOptions(
    string Task,
    string Root,
    string? ActiveFile,
    string? StatePath,
    string? DiagnosticsPath,
    string? CachePath,
    string? OutputPath,
    int TokenBudget);

internal sealed record BuildDiagnostics(string Path, List<string> Lines)
{
    public static async Task<BuildDiagnostics?> LoadAsync(string? path)
    {
        if (path == null || !File.Exists(path))
            return null;
        string[] allLines = await File.ReadAllLinesAsync(path);
        var relevant = allLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("warning", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || line.Contains("succeeded", StringComparison.OrdinalIgnoreCase)
                || line.Contains("passed", StringComparison.OrdinalIgnoreCase))
            .TakeLast(40)
            .ToList();
        if (relevant.Count == 0)
            relevant = allLines.Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(20).ToList();
        return new BuildDiagnostics(path, relevant);
    }
}

internal sealed class ProjectState
{
    public string? Goal { get; init; }
    public List<string> Decisions { get; init; } = [];
    public List<string> Constraints { get; init; } = [];
    public List<StateFact> Facts { get; init; } = [];
    public List<string> Hypotheses { get; init; } = [];
    public List<string> OpenItems { get; init; } = [];
    public List<StateValidation> Validations { get; init; } = [];

    public static async Task<ProjectState?> LoadAsync(string? path)
    {
        if (path == null || !File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProjectState>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}

internal sealed class StateFact
{
    public string Claim { get; init; } = "";
    public string? Evidence { get; init; }
}

internal sealed class StateValidation
{
    public string Command { get; init; } = "";
    public string Result { get; init; } = "";
}