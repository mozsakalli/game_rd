// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Text.RegularExpressions;
using Unity.Scripting.LifecycleManagement;

namespace UnityEditor.Search
{
    static partial class Parsers
    {
        static readonly Regex s_SelectorPattern = new Regex(@"^[@$][^><=!:\s]+");
        [NoAutoStaticsCleanup] // Immutable evaluator handle resolved from a fixed builtin name; safe to persist across reload.
        static readonly SearchExpressionEvaluator s_SelectorEvaluator = EvaluatorManager.GetConstantEvaluatorByName("selector");

        [SearchExpressionParser("selector", BuiltinParserPriority.String)]
        internal static SearchExpression SelectorParser(StringView outerText)
        {
            var text = ParserUtils.SimplifyExpression(outerText);
            if (!s_SelectorPattern.IsMatch(text.ToString()))
                return null;
            return new SearchExpression(SearchExpressionType.Selector, outerText, text.Substring(1), s_SelectorEvaluator);
        }
    }
}
