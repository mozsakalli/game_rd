using System.Text;
using System.Text.RegularExpressions;

namespace ContextCompiler;

internal sealed record SelectedContextSymbol(string DisplayName, string File, int Line, int Score);
internal sealed record CompiledPackage(string Content, int EstimatedTokens,
    List<SelectedContextSymbol> SelectedSymbols);

internal static class ContextPackage
{
    private static readonly Regex Words = new("[A-Za-z_][A-Za-z0-9_.]{2,}", RegexOptions.Compiled);
    private static readonly Regex IdentifierParts = new("[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+",
        RegexOptions.Compiled);

    public static CompiledPackage Compile(CompileOptions options, ProjectState? state,
        BuildDiagnostics? buildDiagnostics, GitChanges changes, SourceIndex index)
    {
        var terms = BuildTerms(options.Task);
        string? activeFile = options.ActiveFile == null
            ? null
            : Path.GetRelativePath(options.Root, options.ActiveFile).Replace('\\', '/');

        var direct = index.Members
            .Select(member => new RankedMember(member, Score(member, terms, activeFile, changes.Files)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Member.Source.Length)
            .ToList();

        int bestScore = direct.Count == 0 ? 0 : direct[0].Score;
        var anchors = direct
            .Where(item => item.Score >= bestScore - 6)
            .Take(4)
            .Select(item => item.Member)
            .ToList();
        var scores = direct.ToDictionary(item => item.Member, item => item.Score);
        var anchorNames = anchors.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var member in index.Members)
        {
            int graphScore = 0;
            foreach (var anchor in anchors)
            {
                if (member.SymbolId != null && anchor.Calls.Contains(member.SymbolId)) graphScore = Math.Max(graphScore, 12);
                if (member.SymbolId != null && anchor.Dependencies.Contains(member.SymbolId)) graphScore = Math.Max(graphScore, 8);
                if (anchor.SymbolId != null && member.Calls.Contains(anchor.SymbolId)) graphScore = Math.Max(graphScore, 10);
                if (anchor.SymbolId != null && member.Dependencies.Contains(anchor.SymbolId)) graphScore = Math.Max(graphScore, 6);
            }
            if (member.References.Overlaps(anchorNames)) graphScore = Math.Max(graphScore, 3);
            if (graphScore > 0)
                scores[member] = scores.GetValueOrDefault(member) + graphScore;
        }
        direct = scores
            .Select(pair => new RankedMember(pair.Key, pair.Value))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Member.Source.Length)
            .ToList();

        var builder = new BudgetWriter(options.TokenBudget - 32);
        builder.Append("# COMPILED CONTEXT\n\n");
        builder.Append($"## TASK\n{options.Task}\n\n");
        int semanticSymbols = index.Members.Count(member => member.SymbolId != null);
        int callEdges = index.Members.Sum(member => member.Calls.Count);
        builder.Append($"## REPOSITORY\nroot: {options.Root.Replace('\\', '/')}\ncsharp_files_indexed: {index.FileCount}\nindex_cache: {(index.CacheHit ? "hit" : "miss")}\nsemantic_symbols: {semanticSymbols}\ncall_edges: {callEdges}\nsyntax_diagnostics: {index.Diagnostics.Count}\n");
        if (activeFile != null)
            builder.Append($"active_file: {activeFile}\n");
        builder.Append("\n");

        AppendState(builder, state);
        AppendSyntaxDiagnostics(builder, index.Diagnostics);
        AppendBuildDiagnostics(builder, options.Root, buildDiagnostics);
        builder.Append($"## GIT CHANGES\n```text\n{changes.Summary}\n```\n\n");
        builder.Append("## RELEVANT CODE\n");

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<SelectedContextSymbol>();
        foreach (var item in direct)
        {
            var member = item.Member;
            string key = $"{member.File}:{member.Line}:{member.Name}";
            if (!emitted.Add(key))
                continue;
            string relationships = Relationships(index, member);
            string block = $"\n### {member.Kind} {member.Name}\nsource: {member.File}:{member.Line}\nsymbol: {member.DisplayName}\nscore: {item.Score}\n{relationships}```csharp\n{member.Source}\n```\n";
            if (!builder.TryAppend(block))
                continue;
            selected.Add(new SelectedContextSymbol(member.DisplayName, member.File, member.Line, item.Score));
        }

        builder.AppendForced($"\n## BUDGET\ntoken_budget: {options.TokenBudget}\n");
        return new CompiledPackage(builder.ToString(), builder.EstimatedTokens, selected);
    }

    private static HashSet<string> BuildTerms(string task)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Words.Matches(task))
        {
            string value = match.Value;
            if (!StopWords.Contains(value))
                terms.Add(value);
            foreach (string part in value.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length > 2 && !StopWords.Contains(part))
                    terms.Add(part);
                if (part.Length > 4 && part.EndsWith('s') && !part.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
                    terms.Add(part[..^1]);
            }
        }
        return terms;
    }

    private static int Score(SourceMember member, HashSet<string> terms, string? activeFile,
        HashSet<string> changedFiles)
    {
        int score = terms.Count(term => term.Contains('.')
            && member.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)) * 30;
        int sourceMatches = 0;
        foreach (string term in terms)
        {
            if (FuzzyContains(member.Name, term)) score += 12;
            if (FuzzyContains(member.DisplayName, term)) score += 10;
            if (member.File.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 8;
            if (member.References.Any(reference => FuzzyContains(reference, term))) score += 4;
            if (member.Kind is "method" or "constructor" or "property"
                && member.Source.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
                sourceMatches++;
            }
        }
        score += Math.Min(48, sourceMatches * sourceMatches * 2);
        if (activeFile != null && member.File.Equals(activeFile, StringComparison.OrdinalIgnoreCase)) score += 30;
        if (changedFiles.Contains(member.File)) score += 6;
        if (member.File.Contains("Test", StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private static bool FuzzyContains(string identifier, string term)
    {
        if (identifier.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (Match match in IdentifierParts.Matches(identifier))
        {
            string part = match.Value;
            if (part.Length >= 4 && term.Length >= 4
                && (part.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || term.Contains(part, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static string Relationships(SourceIndex index, SourceMember member)
    {
        var calls = index.DisplayKnownSymbols(member.Calls, member.SymbolId);
        var callers = index.DisplayKnownSymbols(member.CalledBy, member.SymbolId);
        var dependencies = index.DisplayKnownSymbols(member.Dependencies
            .Where(id => !member.Calls.Contains(id)), member.SymbolId);
        var text = new StringBuilder();
        if (calls.Count > 0) text.Append("calls: ").AppendJoin(", ", calls).Append('\n');
        if (callers.Count > 0) text.Append("called_by: ").AppendJoin(", ", callers).Append('\n');
        if (dependencies.Count > 0) text.Append("depends_on: ").AppendJoin(", ", dependencies).Append('\n');
        return text.ToString();
    }

    private static void AppendSyntaxDiagnostics(BudgetWriter builder, List<SourceDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
            return;
        if (!builder.TryAppend("## SYNTAX DIAGNOSTICS\n"))
            return;
        foreach (var diagnostic in diagnostics)
        {
            string line = $"- {diagnostic.Severity}: {diagnostic.File}:{diagnostic.Line}: {diagnostic.Message}\n";
            if (!builder.TryAppend(line))
                break;
        }
        builder.TryAppend("\n");
    }

    private static void AppendBuildDiagnostics(BudgetWriter builder, string root, BuildDiagnostics? diagnostics)
    {
        if (diagnostics == null || diagnostics.Lines.Count == 0)
            return;
        string relative = Path.GetRelativePath(root, diagnostics.Path).Replace('\\', '/');
        if (!builder.TryAppend($"## BUILD/TEST DIAGNOSTICS\nsource: {relative}\n```text\n"))
            return;
        foreach (string line in diagnostics.Lines)
            if (!builder.TryAppend(line + "\n"))
                break;
        builder.TryAppend("```\n\n");
    }

    private static void AppendState(BudgetWriter builder, ProjectState? state)
    {
        if (state == null)
            return;
        builder.Append("## PROJECT STATE\n");
        if (!string.IsNullOrWhiteSpace(state.Goal)) builder.Append($"goal: {state.Goal}\n");
        AppendList(builder, "constraints", state.Constraints);
        AppendList(builder, "decisions", state.Decisions);
        if (state.Facts.Count > 0)
        {
            builder.Append("facts:\n");
            foreach (var fact in state.Facts)
                builder.Append($"- {fact.Claim}" + (fact.Evidence == null ? "\n" : $" [evidence: {fact.Evidence}]\n"));
        }
        AppendList(builder, "hypotheses", state.Hypotheses);
        AppendList(builder, "open_items", state.OpenItems);
        if (state.Validations.Count > 0)
        {
            builder.Append("validations:\n");
            foreach (var validation in state.Validations)
                builder.Append($"- {validation.Command}: {validation.Result}\n");
        }
        builder.Append("\n");
    }

    private static void AppendList(BudgetWriter builder, string name, List<string> values)
    {
        if (values.Count == 0)
            return;
        builder.Append(name + ":\n");
        foreach (string value in values)
            builder.Append("- " + value + "\n");
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "the", "for", "with", "from", "this", "that", "bir", "icin", "ile", "ve", "veya",
        "fix", "implement", "add", "change", "bug", "handle"
    };

    private sealed record RankedMember(SourceMember Member, int Score);
}

internal sealed class BudgetWriter(int tokenBudget)
{
    private readonly StringBuilder _builder = new();
    private int _characters;

    public int EstimatedTokens => (_characters + 3) / 4;

    public bool TryAppend(string text)
    {
        if (EstimatedTokens + EstimateTokens(text) > tokenBudget)
            return false;
        AppendForced(text);
        return true;
    }

    public void Append(string text)
    {
        if (!TryAppend(text))
            throw new InvalidOperationException("Metadata exceeds the configured token budget.");
    }

    public void AppendForced(string text)
    {
        _builder.Append(text);
        _characters += text.Length;
    }

    public override string ToString() => _builder.ToString();

    private static int EstimateTokens(string text) => (text.Length + 3) / 4;
}