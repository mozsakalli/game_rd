// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor.Utils;
using UnityEngine.Assemblies;

namespace UnityEditor.Build.Profile;

public class BuildDestinationSettings : ScriptableObject
{
    const char k_StartDelimiter = '[';
    const char k_EndDelimiter = ']';

    [SerializeField]
    string m_BuildPath;

    /// <summary>
    /// The build destination path.
    /// The getter returns the path with any public <c>[Namespace.Type.StaticMember]</c> variables resolved.
    /// The setter stores the value assigned, which may contain such variables.
    /// </summary>
    public string buildPath
    {
        get => EvaluateBuildPath();
        set => m_BuildPath = value;
    }

    string EvaluateBuildPath()
    {
        if (string.IsNullOrEmpty(m_BuildPath))
            return string.Empty;

        if (m_BuildPath.IndexOf(k_StartDelimiter) == -1)
            return m_BuildPath;

        var evaluationStack = new HashSet<string>(StringComparer.Ordinal);
        return EvaluateStringWithVariable(m_BuildPath, k_StartDelimiter, k_EndDelimiter, token => ResolveToken(token, evaluationStack));
    }

    /// <summary>
    /// Evaluates an input string by replacing tokens delimited by the specified characters with values provided by a resolver function.
    /// The resolver is called for each token found, and can return a replacement value for it. The replacement value is evaluated as well, so it can contain nested tokens.
    /// </summary>
    /// <param name="input">The input string containing tokens to be evaluated.</param>
    /// <param name="startDelimiter">The character that marks the start of a token.</param>
    /// <param name="endDelimiter">The character that marks the end of a token.</param>
    /// <param name="tokenResolver">A function that takes a token and returns its replacement value.</param>
    /// <returns>The evaluated string with all tokens replaced by their resolved values.</returns>
    static string EvaluateStringWithVariable(string input, char startDelimiter, char endDelimiter, Func<string, string> tokenResolver)
    {
        if (string.IsNullOrEmpty(input) || tokenResolver == null)
            return input;

        var result = input;
        var tokenStartStack = new Stack<int>();

        for (var i = 0; i < result.Length; ++i)
        {
            var c = result[i];
            if (c == startDelimiter)
            {
                tokenStartStack.Push(i);
                continue;
            }

            if (c != endDelimiter || tokenStartStack.Count == 0)
                continue;

            var tokenStart = tokenStartStack.Pop();
            var tokenLength = i - tokenStart - 1;
            if (tokenLength < 0)
                continue;

            var token = result.Substring(tokenStart + 1, tokenLength);
            var replacement = tokenResolver(token);
            if (replacement == null)
                replacement = result.Substring(tokenStart, tokenLength + 2);

            result = result.Substring(0, tokenStart) + replacement + result.Substring(i + 1);
            i = tokenStart + replacement.Length - 1;
            tokenStartStack.Clear();
        }

        return result;
    }

    /// <summary>
    /// Resolves a token to its static member value with cycle detection and path-safe sanitization.
    /// Recursively evaluates nested tokens in the resolved value.
    /// </summary>
    /// <param name="token">The token to be resolved.</param>
    /// <param name="evaluationStack">A set used to detect cycles in token resolution.</param>
    /// <returns>The resolved value of the token.</returns>
    static string ResolveToken(string token, HashSet<string> evaluationStack)
    {
        if (string.IsNullOrEmpty(token))
            return $"{k_StartDelimiter}{k_EndDelimiter}";

        if (!evaluationStack.Add(token))
        {
            Debug.LogWarning($"Cycle detected while evaluating build path token '{k_StartDelimiter}{token}{k_EndDelimiter}'.");
            return $"{k_StartDelimiter}{token}{k_EndDelimiter}";
        }

        try
        {
            var reflectedValue = ResolveStaticMember(token);
            if (string.Equals(reflectedValue, token, StringComparison.Ordinal))
                return $"{k_StartDelimiter}{token}{k_EndDelimiter}";

            reflectedValue = Paths.MakeValidFileName(reflectedValue);

            return EvaluateStringWithVariable(reflectedValue, k_StartDelimiter, k_EndDelimiter, nestedToken => ResolveToken(nestedToken, evaluationStack));
        }
        finally
        {
            evaluationStack.Remove(token);
        }
    }

    /// <summary>
    /// Resolves a fully-qualified public static property/field to its string value.
    /// Returns the input unchanged when it can't be resolved.
    /// </summary>
    /// <param name="fullyQualifiedMemberName">The fully-qualified name of the static member to resolve.</param>
    /// <returns>The resolved value of the static member, or the input string if it cannot be resolved.</returns>
    static string ResolveStaticMember(string fullyQualifiedMemberName)
    {
        if (string.IsNullOrEmpty(fullyQualifiedMemberName))
            return fullyQualifiedMemberName;

        var lastDotIndex = fullyQualifiedMemberName.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == fullyQualifiedMemberName.Length - 1)
            return fullyQualifiedMemberName;

        var typeName = fullyQualifiedMemberName.Substring(0, lastDotIndex);
        var memberName = fullyQualifiedMemberName.Substring(lastDotIndex + 1);

        var assemblies = CurrentAssemblies.GetLoadedAssemblies();
        for (var i = 0; i < assemblies.Count; ++i)
        {
            try
            {
                var type = assemblies[i].GetType(typeName, false);
                if (type == null)
                    continue;

                const BindingFlags flags = BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.Public;
                var property = type.GetProperty(memberName, flags);
                if (property != null && property.CanRead)
                    return property.GetValue(null, null)?.ToString() ?? string.Empty;

                var field = type.GetField(memberName, flags);
                if (field != null)
                    return field.GetValue(null)?.ToString() ?? string.Empty;
            }
            catch
            {
                // Fall through and try the next assembly.
            }
        }

        return fullyQualifiedMemberName;
    }
}
