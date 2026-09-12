// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Scripting.LifecycleManagement;

namespace UnityEditor
{
    static class GlobSearchUtilities
    {
        static readonly Regex k_BasicSymbolsRegex = new Regex(@"(?<range>\\\[..+?\])|(?<dstarfold>\\\*\\\*/)|(?<dstar>\\\*\\\*)|(?<star>\\\*)|(?<single>\\\?)");
        static readonly Regex k_ComplexSymbolsRegex = new Regex(@"(?<or>\\\(.+?(?:\\\|.+?)+\\\))");
        [NoAutoStaticsCleanup] // immutable lookup table of internal glob→regex lambdas built once; no user refs, safe to persist
        static readonly ImmutableDictionary<string, Func<string, string>> s_GlobToRegexMatch;

        static GlobSearchUtilities()
        {
            var globToRegexMatch = new Dictionary<string, Func<string, string>>();

            //Match any number of characters, where characters exist - end in a fold.
            globToRegexMatch.Add("dstarfold", match => "(.+/)?");

            //Match any number of characters
            globToRegexMatch.Add("dstar", match => ".*");

            //Match any number of non-"/" characters
            globToRegexMatch.Add("star", match => @"[^/]*");

            //Match a single non-"/" character
            globToRegexMatch.Add("single", match => @"[^/]");
            globToRegexMatch.Add("range", match => match.Replace(@"\[", "["));
            globToRegexMatch.Add("or", match => match.Replace(@"\(", "(").Replace(@"\|", "|").Replace(@"\)", ")"));
            s_GlobToRegexMatch = globToRegexMatch.ToImmutableDictionary();
        }

        static string GlobToRegex(string glob)
        {
            // Escape any glob character that could be interpreted in the regex
            var regex = Regex.Escape(glob);

            // Handle basic symbols replacement first
            regex = k_BasicSymbolsRegex.Replace(regex, ReplaceGlobGroups);

            // Complex patterns are replaced in a second pass because they may contain basic symbols that we want to replace first.
            regex = k_ComplexSymbolsRegex.Replace(regex, ReplaceGlobGroups);

            // Add ^ and $ to make sure the search is always done on the full path.
            // Searches like "Editor" should match the same as "**Editor" and looks only for Editor folders or file in any subfolder
            // This is why we are always adding optional folder path at the beginning and option folder ending character in the end.
            return $"^(.+/)?{regex}/?$";
        }

        static string ReplaceGlobGroups(Match match)
        {
            foreach (var replace in s_GlobToRegexMatch)
            {
                if (match.Groups[replace.Key].Success)
                {
                    return replace.Value(match.Value);
                }
            }

            return match.Value;
        }

        static bool IsRegexValid(string regex)
        {
            try
            {
                // Regex.Match throws and ArgumentException when the regex is not valid,
                // we use this to make sure the generated regex can be used during a search.
                Regex.Match("", regex);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

        internal static string[] GlobToRegex(this SearchFilter filter)
        {
            var regexes = new List<string>();
            foreach (var glob in filter.globs)
            {
                var regex = GlobToRegex(glob);
                if (IsRegexValid(regex))
                {
                    regexes.Add(regex);
                }
            }
            return regexes.ToArray();
        }
    }
}
