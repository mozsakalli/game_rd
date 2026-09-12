// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

//
// Copyright SmartFormat Project maintainers and contributors.
// Licensed under the MIT license.

using UnityEngine;
using System;
using System.Collections.Generic;
using Unity.SmartStrings.Core.Parsing;

namespace Unity.SmartStrings.Core.Settings;

/// <summary>
/// Class for <see cref="Parser"/> settings.
/// Properties should be considered as 'init-only' like implemented in C# 9.
/// Any changes after passing settings as argument to CTORs may not have effect.
/// </summary>
[Serializable]
public class ParserSettings
{
    readonly List<char> m_AlphanumericSelectorChars = new List<char>("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_-");

    [SerializeField] ParseErrorAction m_ErrorAction = ParseErrorAction.ThrowError;

    [Tooltip(@"When enabled, character string literals will be converted, for example `\t` will become a TAB character.")]
    [SerializeField] bool m_ConvertCharacterStringLiterals = true;

    [SerializeField] List<char> m_CustomSelectorChars = new List<char>();
    [SerializeField] List<char> m_CustomOperatorChars = new List<char>();

    /// <summary>
    /// Behavior that the <see cref="Parser" /> applies when a parsing error occurs.
    /// The default is <see cref="ParseErrorAction.ThrowError"/>.
    /// </summary>
    public ParseErrorAction ErrorAction { get => m_ErrorAction; set => m_ErrorAction = value; }

    /// <summary>
    /// The list of standard selector characters.
    /// </summary>
    internal List<char> SelectorChars() => m_AlphanumericSelectorChars;

    /// <summary>
    /// Gets a read-only list of the custom selector characters, which were set with <see cref="AddCustomSelectorChars"/>.
    /// </summary>
    internal List<char> CustomSelectorChars() => m_CustomSelectorChars;

    /// <summary>
    /// Gets a list of characters which are allowed in a selector.
    /// </summary>
    internal List<char> DisallowedSelectorChars()
    {
        var chars = new List<char>
        {
            CharLiteralEscapeChar, FormatterNameSeparator, AlignmentOperator, SelectorOperator,
            PlaceholderBeginChar, PlaceholderEndChar, FormatterOptionsBeginChar, FormatterOptionsEndChar
        };
        chars.AddRange(OperatorChars());
        return chars;
    }

    /// <summary>
    /// Gets a read-only list of the custom operator characters, which were set with <see cref="AddCustomSelectorChars"/>.
    /// Contiguous operator characters are parsed as one operator (e.g. '?.').
    /// </summary>
    internal List<char> CustomOperatorChars() => m_CustomOperatorChars;

    /// <summary>
    /// Adds a list of allowable selector characters on top of the <see cref="SelectorChars"/> setting.
    /// This can be useful to support additional selector syntax such as math.
    /// Characters in <see cref="DisallowedSelectorChars"/> cannot be added.
    /// Operator chars and selector chars must be different.
    /// </summary>
    /// <param name="characters">Selector characters to allow in addition to the standard set.</param>
    public void AddCustomSelectorChars(IList<char> characters)
    {
        foreach (var c in characters)
        {
            if (DisallowedSelectorChars().Contains(c) || m_CustomOperatorChars.Contains(c))
                throw new ArgumentException($"Cannot add '{c}' as a custom selector character. It is disallowed or in use as an operator.");

            if (!m_CustomSelectorChars.Contains(c) && !m_AlphanumericSelectorChars.Contains(c))
                m_CustomSelectorChars.Add(c);
        }
    }

    /// <summary>
    /// Adds a list of allowable operator characters on top of the standard <see cref="OperatorChars"/> setting.
    /// Operator chars and selector chars must be different.
    /// </summary>
    /// <param name="characters">Operator characters to allow in addition to the standard set.</param>
    public void AddCustomOperatorChars(IList<char> characters)
    {
        foreach (var c in characters)
        {
            if ((!OperatorChars().Contains(c) && DisallowedSelectorChars().Contains(c)) ||
                SelectorChars().Contains(c) || CustomSelectorChars().Contains(c))
                throw new ArgumentException($"Cannot add '{c}' as a custom operator character. It is disallowed or in use as a selector.");

            if (!OperatorChars().Contains(c) && !CustomOperatorChars().Contains(c))
                m_CustomOperatorChars.Add(c);
        }
    }

    /// <summary>
    /// This setting is relevant for the <see cref="LiteralText" />.
    /// If <see langword="true"/> (the default), character string literals are treated like in "normal" string.Format:
    /// string.Format("\t")   will return a "TAB" character
    /// If <see langword="false"/>, character string literals are not converted, just like with this string.Format:
    /// string.Format(@"\t")  will return the 2 characters "\" and "t"
    /// </summary>
    public bool ConvertCharacterStringLiterals { get => m_ConvertCharacterStringLiterals; set => m_ConvertCharacterStringLiterals = value; }

    /// <summary>
    /// The character literal escape character for <see cref="PlaceholderBeginChar"/> and <see cref="PlaceholderEndChar"/>,
    /// but also others like for \t (TAB), \n (NEW LINE), \\ (BACKSLASH) and others defined in <see cref="EscapedLiteral"/>.
    /// </summary>
    internal char CharLiteralEscapeChar { get; } = '\\';

    /// <summary>
    /// The character which separates the formatter name (if any exists) from other parts of the placeholder.
    /// E.g.: {Variable:FormatterName:argument} or {Variable:FormatterName}
    /// </summary>
    internal char FormatterNameSeparator { get; } = ':';

    /// <summary>
    /// The standard operator characters.
    /// Contiguous operator characters are parsed as one operator (e.g. '?.').
    /// </summary>
    internal List<char> OperatorChars() => new()
    { SelectorOperator, NullableOperator, AlignmentOperator, ListIndexBeginChar, ListIndexEndChar };

    /// <summary>
    /// The character which separates the selector for alignment. <c>E.g.: Smart.Format("Name: {name,10}")</c>
    /// </summary>
    internal char AlignmentOperator { get; } = ',';

    /// <summary>
    /// The character which separates two or more selectors <c>E.g.: "First.Second.Third"</c>
    /// </summary>
    internal char SelectorOperator { get; } = '.';

    /// <summary>
    /// The character which flags the selector as <see langword="nullable"/>.
    /// The character after <see cref="NullableOperator"/> must be the <see cref="SelectorOperator"/>.
    /// <c>E.g.: "First?.Second"</c>
    /// </summary>
    internal char NullableOperator { get; } = '?';

    /// <summary>
    /// Gets the character indicating the start of a <see cref="Placeholder"/>.
    /// </summary>
    internal char PlaceholderBeginChar { get; } = '{';

    /// <summary>
    /// Gets the character indicating the end of a <see cref="Placeholder"/>.
    /// </summary>
    internal char PlaceholderEndChar { get; } = '}';

    /// <summary>
    /// Gets the character indicating the begin of formatter options.
    /// </summary>
    internal char FormatterOptionsBeginChar { get; } = '(';

    /// <summary>
    /// Gets the character indicating the end of formatter options.
    /// </summary>
    internal char FormatterOptionsEndChar { get; } = ')';

    /// <summary>
    /// Gets the character indicating the begin of a list index, like in "{Numbers[0]}"
    /// </summary>
    internal char ListIndexBeginChar { get; } = '[';

    /// <summary>
    /// Gets the character indicating the end of a list index, like in "{Numbers[0]}"
    /// </summary>
    internal char ListIndexEndChar { get; } = ']';

    /// <summary>
    /// Characters which terminate parsing of format options.
    /// To use them as options, they must be escaped (preceded) by the <see cref="CharLiteralEscapeChar"/>.
    /// </summary>
    internal List<char> FormatOptionsTerminatorChars() => new()
    {
        FormatterNameSeparator,
        FormatterOptionsBeginChar,
        FormatterOptionsEndChar,
        PlaceholderBeginChar,
        PlaceholderEndChar
    };
}
