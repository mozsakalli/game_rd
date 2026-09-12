using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContextCompiler;

internal sealed record SourceMember(
    string Name,
    string Kind,
    string File,
    int Line,
    string Source,
    HashSet<string> References,
    string? SymbolId,
    string DisplayName,
    HashSet<string> Dependencies,
    HashSet<string> Calls,
    HashSet<string> CalledBy);

internal sealed record SourceDiagnostic(string Severity, string File, int Line, string Message);

internal sealed class SourceIndex
{
    private const int CacheVersion = 1;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".context", "bin", "obj", "node_modules", "Library",
        "old_engine_readonly", "UnityCsReference-master", "glfw-master", "fontstash-master"
    };

    public List<SourceMember> Members { get; } = [];
    public List<SourceDiagnostic> Diagnostics { get; } = [];
    public int FileCount { get; private set; }
    public bool CacheHit { get; private set; }

    public static async Task<SourceIndex> BuildAsync(string root, string? cachePath = null)
    {
        var sources = new List<SourceFile>();
        foreach (string file in EnumerateSourceFiles(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            sources.Add(new SourceFile(relative, file, await File.ReadAllTextAsync(file)));
        }

        string fingerprint = Fingerprint(sources);
        if (cachePath != null)
        {
            var cached = await TryLoadCacheAsync(cachePath, fingerprint);
            if (cached != null)
                return cached;
        }

        var index = new SourceIndex();
        var files = new List<ParsedFile>(sources.Count);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest,
            preprocessorSymbols: ["DE_EDITOR"]);
        foreach (var source in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(source.Text, parseOptions, source.FullPath);
            var rootNode = await tree.GetRootAsync();
            index.FileCount++;
            files.Add(new ParsedFile(source.Relative, tree, rootNode));
        }

        var compilation = CSharpCompilation.Create(
            "ContextCompiler.Analysis",
            files.Select(file => file.Tree).Append(CSharpSyntaxTree.ParseText("""
                global using System;
                global using System.Collections.Generic;
                global using System.IO;
                global using System.Linq;
                global using System.Net.Http;
                global using System.Threading;
                global using System.Threading.Tasks;
                """, parseOptions, "<implicit-usings>")),
            MetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
                {
                    ["CS8632"] = ReportDiagnostic.Suppress
                }));

        foreach (var file in files)
        {
            var model = compilation.GetSemanticModel(file.Tree, ignoreAccessibility: true);
            string relative = file.Relative;
            var tree = file.Tree;
            var rootNode = file.Root;
            foreach (var member in rootNode.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (!TryDescribe(member, out string name, out string kind, out string source))
                    continue;
                int line = tree.GetLineSpan(member.Span).StartLinePosition.Line + 1;
                var references = member.DescendantTokens()
                    .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
                    .Select(token => token.ValueText)
                    .Where(value => value.Length > 1)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var symbol = model.GetDeclaredSymbol(member);
                string? symbolId = CanonicalId(symbol);
                string displayName = symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? name;
                var dependencies = SemanticDependencies(member, model);
                var calls = SemanticCalls(member, model);
                index.Members.Add(new SourceMember(name, kind, relative, line, source, references,
                    symbolId, displayName, dependencies, calls, []));
            }
        }

        index.BuildReverseCallGraph();
        index.AddDiagnostics(root, files.SelectMany(file => file.Tree.GetDiagnostics()));
        if (cachePath != null)
            await index.TrySaveCacheAsync(cachePath, fingerprint);
        return index;
    }

    private static string Fingerprint(List<SourceFile> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var source in sources)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(source.Relative));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(source.Text));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<SourceIndex?> TryLoadCacheAsync(string path, string fingerprint)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<CacheDocument>(stream);
            if (document == null || document.Version != CacheVersion || document.Fingerprint != fingerprint)
                return null;

            var index = new SourceIndex { FileCount = document.FileCount, CacheHit = true };
            foreach (var member in document.Members)
                index.Members.Add(new SourceMember(member.Name, member.Kind, member.File, member.Line,
                    member.Source,
                    new HashSet<string>(member.References, StringComparer.OrdinalIgnoreCase),
                    member.SymbolId, member.DisplayName,
                    new HashSet<string>(member.Dependencies, StringComparer.Ordinal),
                    new HashSet<string>(member.Calls, StringComparer.Ordinal),
                    new HashSet<string>(member.CalledBy, StringComparer.Ordinal)));
            index.Diagnostics.AddRange(document.Diagnostics);
            return index;
        }
        catch
        {
            return null;
        }
    }

    private async Task TrySaveCacheAsync(string path, string fingerprint)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + ".tmp." + Environment.ProcessId;
            var document = new CacheDocument(CacheVersion, fingerprint, FileCount, Members, Diagnostics);
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, document);
            File.Move(temporary, path, true);
        }
        catch
        {
            if (temporary != null)
                File.Delete(temporary);
        }
    }

    public string DisplaySymbol(string symbolId)
    {
        var member = Members.FirstOrDefault(candidate => candidate.SymbolId == symbolId);
        return member?.DisplayName ?? symbolId;
    }

    public List<string> DisplayKnownSymbols(IEnumerable<string> symbolIds, string? exclude = null, int max = 8)
    {
        var requested = symbolIds
            .Where(id => id != exclude)
            .ToHashSet(StringComparer.Ordinal);
        return Members
            .Where(member => member.SymbolId != null && requested.Contains(member.SymbolId))
            .Select(member => member.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    private void BuildReverseCallGraph()
    {
        var bySymbol = Members
            .Where(member => member.SymbolId != null)
            .GroupBy(member => member.SymbolId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var caller in Members)
        {
            if (caller.SymbolId == null)
                continue;
            foreach (string call in caller.Calls)
                if (bySymbol.TryGetValue(call, out var targets))
                    foreach (var target in targets)
                        if (target.SymbolId != caller.SymbolId)
                            target.CalledBy.Add(caller.SymbolId);
        }
    }

    private void AddDiagnostics(string root, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics
            .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Take(30))
        {
            var span = diagnostic.Location.GetLineSpan();
            string file = span.Path.Length == 0
                ? "<compilation>"
                : Path.GetRelativePath(root, span.Path).Replace('\\', '/');
            Diagnostics.Add(new SourceDiagnostic(
                diagnostic.Severity.ToString().ToLowerInvariant(),
                file,
                span.StartLinePosition.Line + 1,
                diagnostic.GetMessage()));
        }
    }

    private static IEnumerable<MetadataReference> MetadataReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platformAssemblies)
            foreach (string path in platformAssemblies.Split(Path.PathSeparator))
                if (path.Length > 0)
                    paths.Add(path);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic && assembly.Location.Length > 0)
                paths.Add(assembly.Location);
        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }

    private static HashSet<string> SemanticDependencies(MemberDeclarationSyntax member, SemanticModel model)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        if (member is BaseTypeDeclarationSyntax)
            return dependencies;
        foreach (var name in member.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var info = model.GetSymbolInfo(name);
            string? id = CanonicalId(info.Symbol ?? info.CandidateSymbols.FirstOrDefault());
            if (id != null)
                dependencies.Add(id);
        }
        return dependencies;
    }

    private static HashSet<string> SemanticCalls(MemberDeclarationSyntax member, SemanticModel model)
    {
        var calls = new HashSet<string>(StringComparer.Ordinal);
        if (member is BaseTypeDeclarationSyntax)
            return calls;
        foreach (var invocation in member.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var info = model.GetSymbolInfo(invocation);
            string? id = CanonicalId(info.Symbol ?? info.CandidateSymbols.FirstOrDefault());
            if (id != null)
                calls.Add(id);
        }
        foreach (var creation in member.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var info = model.GetSymbolInfo(creation);
            string? id = CanonicalId(info.Symbol ?? info.CandidateSymbols.FirstOrDefault());
            if (id != null)
                calls.Add(id);
        }
        return calls;
    }

    private static string? CanonicalId(ISymbol? symbol)
    {
        symbol = symbol?.OriginalDefinition;
        return symbol?.GetDocumentationCommentId()
            ?? symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string child in Directory.EnumerateDirectories(directory))
                if (!ExcludedDirectories.Contains(Path.GetFileName(child)))
                    pending.Push(child);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs"))
                yield return file;
        }
    }

    private static bool TryDescribe(MemberDeclarationSyntax member, out string name, out string kind,
        out string source)
    {
        name = "";
        kind = "";
        source = "";
        switch (member)
        {
            case BaseTypeDeclarationSyntax type:
                name = type.Identifier.ValueText;
                kind = TypeKeyword(type);
                source = TypeSummary(type);
                return true;
            case MethodDeclarationSyntax method:
                name = method.Identifier.ValueText;
                kind = "method";
                source = method.ToFullString().Trim();
                return true;
            case ConstructorDeclarationSyntax constructor:
                name = constructor.Identifier.ValueText;
                kind = "constructor";
                source = constructor.ToFullString().Trim();
                return true;
            case PropertyDeclarationSyntax property:
                name = property.Identifier.ValueText;
                kind = "property";
                source = property.ToFullString().Trim();
                return true;
            case FieldDeclarationSyntax field:
                name = string.Join(", ", field.Declaration.Variables.Select(value => value.Identifier.ValueText));
                kind = "field";
                source = field.ToFullString().Trim();
                return true;
            default:
                return false;
        }
    }

    private static string TypeSummary(BaseTypeDeclarationSyntax type)
    {
        string header = type switch
        {
            TypeDeclarationSyntax declaration => declaration.WithMembers(default).ToFullString().Trim(),
            EnumDeclarationSyntax declaration => declaration.WithMembers(default).ToFullString().Trim(),
            _ => type.ToFullString().Trim()
        };
        IEnumerable<MemberDeclarationSyntax> declarations = type switch
        {
            TypeDeclarationSyntax declaration => declaration.Members,
            EnumDeclarationSyntax => [],
            _ => []
        };
        var members = declarations
            .Select(member => member switch
            {
                MethodDeclarationSyntax method => $"method {method.Identifier}{method.ParameterList}",
                ConstructorDeclarationSyntax constructor => $"constructor {constructor.Identifier}{constructor.ParameterList}",
                PropertyDeclarationSyntax property => $"property {property.Type} {property.Identifier}",
                FieldDeclarationSyntax field => $"field {field.Declaration}",
                BaseTypeDeclarationSyntax nested => $"{TypeKeyword(nested)} {nested.Identifier}",
                _ => null
            })
            .Where(value => value != null);
        return header + "\nMEMBERS\n" + string.Join("\n", members);
    }

    private static string TypeKeyword(BaseTypeDeclarationSyntax type) => type switch
    {
        TypeDeclarationSyntax declaration => declaration.Keyword.ValueText,
        EnumDeclarationSyntax declaration => declaration.EnumKeyword.ValueText,
        _ => "type"
    };

    private sealed record SourceFile(string Relative, string FullPath, string Text);
    private sealed record ParsedFile(string Relative, SyntaxTree Tree, SyntaxNode Root);
    private sealed record CacheDocument(int Version, string Fingerprint, int FileCount,
        List<SourceMember> Members, List<SourceDiagnostic> Diagnostics);
}

internal sealed class GitChanges
{
    private static readonly Regex StatusPath = new("^..\\s+(.+)$", RegexOptions.Compiled);

    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Summary { get; private set; } = "Git metadata unavailable.";

    public static async Task<GitChanges> ReadAsync(string root)
    {
        var changes = new GitChanges();
        var startInfo = new ProcessStartInfo("git", $"-C \"{root}\" status --short --untracked-files=all")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return changes;
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                return changes;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string line in lines)
            {
                var match = StatusPath.Match(line);
                if (match.Success)
                    changes.Files.Add(match.Groups[1].Value.Replace('\\', '/'));
            }
            int untracked = lines.Count(line => line.StartsWith("??", StringComparison.Ordinal));
            if (lines.Length > 100 && untracked == lines.Length)
                changes.Files.Clear();
            changes.Summary = Summarize(lines);
        }
        catch
        {
            // Context generation remains useful outside a Git repository.
        }
        return changes;
    }

    private static string Summarize(string[] lines)
    {
        if (lines.Length == 0)
            return "Working tree clean.";
        if (lines.Length <= 20)
            return string.Join("\n", lines);

        int untracked = lines.Count(line => line.StartsWith("??", StringComparison.Ordinal));
        int tracked = lines.Length - untracked;
        if (tracked == 0)
            return $"No tracked baseline; {untracked} untracked paths omitted.";
        var sourceLines = lines
            .Where(line => line.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || line.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("/Library/", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();

        var summary = new List<string>
        {
            $"status_entries: {lines.Length}",
            $"tracked: {tracked}",
            $"untracked: {untracked}"
        };
        if (sourceLines.Count > 0)
        {
            summary.Add("source_changes:");
            summary.AddRange(sourceLines.Select(line => "  " + line));
        }
        if (lines.Length > sourceLines.Count)
            summary.Add("remaining paths omitted");
        return string.Join("\n", summary);
    }
}