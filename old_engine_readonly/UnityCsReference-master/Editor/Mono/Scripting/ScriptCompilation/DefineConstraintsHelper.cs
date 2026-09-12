// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.Scripting;

namespace UnityEditor.Scripting.ScriptCompilation
{
    static class DefineConstraintsHelper
    {
        public const string Not = "!";
        public const string Or = "||";

        // Characters that we consider valid whitespaces.
        public static readonly char[] k_ValidWhitespaces = { ' ', '\t' };

        static readonly Regex s_SplitAndKeep = new Regex("(\\|\\|)", RegexOptions.Compiled);

        public enum DefineConstraintStatus
        {
            Compatible,
            Incompatible,
            Invalid,
        }

        [RequiredByNativeCode]
        public static bool IsDefineConstraintsCompatible(string[] defines, string[] defineConstraints)
        {
            return IsDefineConstraintsCompatibleContext(new EditorBuildRules.SymbolDefinitionContext(defines), defineConstraints);
        }

        internal static bool IsDefineConstraintsCompatibleContext(EditorBuildRules.SymbolDefinitionContext symbolDefinitionContext, string[] defineConstraints)
        {
            if ((defineConstraints == null) || (defineConstraints.Length == 0))
            {
                return true;
            }

            foreach (string constraint in defineConstraints)
            {
                if (GetDefineConstraintCompatibility(symbolDefinitionContext, constraint) != DefineConstraintStatus.Compatible)
                {
                    return false;
                }
            }

            return true;
        }

        internal static DefineConstraintStatus GetDefineConstraintCompatibility(EditorBuildRules.SymbolDefinitionContext symbolDefinitionContext, string defineConstraints)
        {
            if (string.IsNullOrEmpty(defineConstraints))
            {
                return DefineConstraintStatus.Invalid;
            }

            // Split by "||" (OR) and keep it in the resulting array
            var splitDefineConstraints = s_SplitAndKeep.Split(defineConstraints);

            // Trim what we consider valid space characters
            for (var i = 0; i < splitDefineConstraints.Length; ++i)
            {
                splitDefineConstraints[i] = splitDefineConstraints[i].Trim(k_ValidWhitespaces);
            }

            // Check for consecutive OR
            for (var i = 0; i < splitDefineConstraints.Length; ++i)
            {
                if (splitDefineConstraints[i] == Or && (i < splitDefineConstraints.Length - 1 && splitDefineConstraints[i + 1] == Or))
                {
                    return DefineConstraintStatus.Invalid;
                }
            }

#pragma warning disable UAC2001 // Avoid Linq
            var notExpectedDefines = new HashSet<string>(splitDefineConstraints.Where(x => x.StartsWith(Not, StringComparison.Ordinal) && x != Or).Select(x => x.Substring(1)));
#pragma warning restore UAC2001
#pragma warning disable UAC2001 // Avoid Linq
            var expectedDefines = new HashSet<string>(splitDefineConstraints.Where(x => !x.StartsWith(Not, StringComparison.Ordinal) && x != Or));
#pragma warning restore UAC2001

            if (!symbolDefinitionContext.HasAny())
            {
                if (expectedDefines.Count > 0)
                {
                    return DefineConstraintStatus.Incompatible;
                }

                return DefineConstraintStatus.Compatible;
            }

            foreach (var define in expectedDefines)
            {
                if (!SymbolNameRestrictions.IsValid(define))
                {
                    return DefineConstraintStatus.Invalid;
                }
            }

            foreach (var define in notExpectedDefines)
            {
                if (!SymbolNameRestrictions.IsValid(define))
                {
                    return DefineConstraintStatus.Invalid;
                }
            }

            if (expectedDefines.Overlaps(notExpectedDefines))
            {
                var complement = new HashSet<string>(expectedDefines);
                expectedDefines.ExceptWith(notExpectedDefines);
                notExpectedDefines.ExceptWith(complement);
            }

            if (expectedDefines.Count == 0 && notExpectedDefines.Count == 0)
            {
                return DefineConstraintStatus.Invalid;
            }

            var expectedDefinesResult = DefineConstraintStatus.Incompatible;
            foreach (var define in expectedDefines)
            {
                if (symbolDefinitionContext.Contains(define))
                {
                    expectedDefinesResult = DefineConstraintStatus.Compatible;
                    break;
                }
            }

            if (expectedDefines.Count > 0 && notExpectedDefines.Count == 0)
            {
                return expectedDefinesResult;
            }

            var notExpectedDefinesResult = DefineConstraintStatus.Compatible;
            foreach (var define in notExpectedDefines)
            {
                if (symbolDefinitionContext.Contains(define))
                {
                    notExpectedDefinesResult = DefineConstraintStatus.Incompatible;
                    break;
                }
            }

            if (notExpectedDefines.Count > 0 && expectedDefines.Count == 0)
            {
                return notExpectedDefinesResult;
            }

            if (expectedDefinesResult == DefineConstraintStatus.Compatible || notExpectedDefinesResult == DefineConstraintStatus.Compatible)
            {
                return DefineConstraintStatus.Compatible;
            }

            return DefineConstraintStatus.Incompatible;
        }

        internal static bool IsDefineConstraintValid(string define)
        {
            if (define == null)
            {
                return false;
            }

            // Split define by OR symbol
            var splitDefines = define.Split(new[] { Or }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var d in splitDefines)
            {
                var finalDefine = d.Trim(k_ValidWhitespaces);
                finalDefine = finalDefine.StartsWith(Not, StringComparison.Ordinal) ? finalDefine.Substring(1) : finalDefine;
                if (!SymbolNameRestrictions.IsValid(finalDefine))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
