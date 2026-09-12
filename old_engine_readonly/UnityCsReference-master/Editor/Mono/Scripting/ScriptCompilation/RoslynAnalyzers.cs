// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityEditor.Scripting.ScriptCompilation
{
    internal static class RoslynAnalyzers
    {
        private static readonly string[] Unset = null;
#pragma warning disable UAC2003 // The way this is used means it must be an explicit empty array, not Array.Empty<string>()
        private static readonly string[] CyclicDependencies = {};
#pragma warning restore UAC2003

        private static string[] SetAnalyzers(ScriptAssembly scriptAssembly, IEnumerable<(string scriptAssemblyFileName, string analyzerDll)> allAnalyzers, bool scanPrecompiledReferences)
        {
            if (scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths == Unset)
            {
                // If this is a cyclic chain we want to detect that and do two iterations
                // Doing two iterations ensures that all participants in the chain will see all the analyzers of all members involved in the chain.
                scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths = CyclicDependencies;
            }
            else if (scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths == CyclicDependencies)
            {
                // On second iteration return an empty array (this will be replaced be actual content of the cyclic chain)
                scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths = Array.Empty<string>();
            }
            else
            {
                // Analyzers for this ScriptAssembly has already been setup
                return scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths;
            }

            // Build HashSet of reference filenames once to avoid unnecessary Path.GetFileName calls
            HashSet<string> referenceFileNames = null;
            if (scanPrecompiledReferences && scriptAssembly.References != null && scriptAssembly.References.Length > 0)
            {
                referenceFileNames = new HashSet<string>(scriptAssembly.References.Length);
                foreach (var reference in scriptAssembly.References)
                {
                    referenceFileNames.Add(Path.GetFileName(reference));
                }
            }

            scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths =
#pragma warning disable UAC2001 // Avoid Linq
                scriptAssembly.ScriptAssemblyReferences
#pragma warning restore UAC2001
                    .SelectMany(sa => SetAnalyzers(sa, allAnalyzers, scanPrecompiledReferences))
#pragma warning disable UAC2001 // Avoid Linq
                    .Concat(allAnalyzers
#pragma warning restore UAC2001
                        .Where(a => a.scriptAssemblyFileName == null ||
                                    a.scriptAssemblyFileName == scriptAssembly.Filename ||
                                    (referenceFileNames != null && referenceFileNames.Contains(a.scriptAssemblyFileName)))
                        .Select(a => a.analyzerDll))
                    .Distinct()
                    .ToArray();

            if (scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths.Length > 0)
            {
                // Only set ruleset/config paths if not already set (e.g., by immutable package logic)
                if (string.IsNullOrEmpty(scriptAssembly.CompilerOptions.RoslynAnalyzerRulesetPath) &&
                    string.IsNullOrEmpty(scriptAssembly.CompilerOptions.AnalyzerConfigPath))
                {
                    if(scriptAssembly.TargetAssemblyType == TargetAssemblyType.Predefined)
                    {
                        var originPath = Path.ChangeExtension(scriptAssembly.Filename, null);
                        scriptAssembly.CompilerOptions.RoslynAnalyzerRulesetPath = RuleSetFileCache.GetRuleSetFilePathInRootFolder(originPath);
                        scriptAssembly.CompilerOptions.AnalyzerConfigPath = RoslynAnalyzerConfigFiles.GetAnalyzerConfigRootFolder(originPath);
                    }
                    else
                    {
                        scriptAssembly.CompilerOptions.RoslynAnalyzerRulesetPath = RuleSetFileCache.GetPathForAssembly(scriptAssembly.OriginPath);
                        scriptAssembly.CompilerOptions.AnalyzerConfigPath = RoslynAnalyzerConfigFiles.GetAnalyzerConfigForAssembly(scriptAssembly.OriginPath);
                    }
                }
#pragma warning disable UAC2001 // Avoid Linq
                scriptAssembly.CompilerOptions.RoslynAdditionalFilePaths = scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths
#pragma warning restore UAC2001
                    .SelectMany(a=>RoslynAdditionalFiles.GetAnalyzerAdditionalFilesForTargetAssembly(a, scriptAssembly.OriginPath))
                    .Distinct()
                    .ToArray();
            }

            return scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths;
        }

        internal static void SetAnalyzers(ScriptAssembly[] scriptAssemblies, TargetAssembly[] potentialAnalyzerOwners, string[] analyzerDlls, bool scanPrecompiledReferences)
        {
            // Figure out what assemblies own each analyzer
#pragma warning disable UAC2001 // Avoid Linq
            var analyzerAssemblies = analyzerDlls.Select(analyzerDll =>
#pragma warning restore UAC2001
            {
#pragma warning disable UAC2001, UAC2011 // Avoid Linq
                var potentialAnalyzerOwner = potentialAnalyzerOwners
#pragma warning restore UAC2001, UAC2011
                    .Where(targetAssembly => targetAssembly.PathFilter(analyzerDll) > 0)
                    .OrderByDescending(targetAssembly => targetAssembly.PathFilter(analyzerDll))
                    .FirstOrDefault();

                return (potentialOwnerOfAnalyzer: potentialAnalyzerOwner?.Filename, dll: analyzerDll);

            }).ToArray();

            // Null out all RoslynAnalyzerDllPaths to indicate they need to be set
            foreach (var scriptAssembly in scriptAssemblies)
                scriptAssembly.CompilerOptions.RoslynAnalyzerDllPaths = Unset;

            foreach (var scriptAssembly in scriptAssemblies)
                SetAnalyzers(scriptAssembly, analyzerAssemblies, scanPrecompiledReferences);
        }
    }
}
