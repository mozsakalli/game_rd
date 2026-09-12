// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Text.RegularExpressions;

namespace Unity.SmartStrings;

static class StringExtensionMethods
{
    static readonly Regex s_WhitespaceRegex = new Regex(@"\s+");

    public static string ReplaceWhiteSpaces(this string str, string replacement = "")
    {
        return s_WhitespaceRegex.Replace(str, replacement);
    }
}
