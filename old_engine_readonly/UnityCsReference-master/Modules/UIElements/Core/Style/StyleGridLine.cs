// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Style value + keyword wrapper for a <see cref="GridLine"/> (grid-column-start / -end,
    /// grid-row-start / -end).
    /// </summary>
    public struct StyleGridLine : IStyleValue<GridLine>, IEquatable<StyleGridLine>
    {
        /// <summary>The <see cref="GridLine"/> value.</summary>
        public GridLine value
        {
            get => m_Keyword == StyleKeyword.Undefined ? m_Value : default;
            set
            {
                m_Value = value;
                m_Keyword = StyleKeyword.Undefined;
            }
        }

        /// <summary>The style keyword.</summary>
        public StyleKeyword keyword
        {
            get => m_Keyword;
            set
            {
                m_Keyword = value;
                m_Value = default;
            }
        }

        /// <summary>Creates a new StyleGridLine from a <see cref="GridLine"/>.</summary>
        public StyleGridLine(GridLine value)
            : this(value, StyleKeyword.Undefined)
        {}

        /// <summary>Creates a new StyleGridLine from a keyword.</summary>
        public StyleGridLine(StyleKeyword keyword)
            : this(default, keyword)
        {}

        internal StyleGridLine(GridLine value, StyleKeyword keyword)
        {
            m_Keyword = keyword;
            m_Value = value;
        }

        GridLine m_Value;
        StyleKeyword m_Keyword;

        /// <undoc/>
        public static implicit operator StyleGridLine(GridLine value) => new StyleGridLine(value);

        /// <summary>A positive integer is an explicit line; 0 and negatives throw (see <see cref="GridLine"/>).</summary>
        public static implicit operator StyleGridLine(int line) => new StyleGridLine(GridLine.AtLine(line));

        /// <undoc/>
        public static implicit operator GridLine(StyleGridLine value) => value.value;

        /// <undoc/>
        public static implicit operator StyleGridLine(StyleKeyword keyword) => new StyleGridLine(keyword);

        /// <undoc/>
        public static bool operator==(StyleGridLine lhs, StyleGridLine rhs)
            => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;

        /// <undoc/>
        public static bool operator!=(StyleGridLine lhs, StyleGridLine rhs) => !(lhs == rhs);

        /// <undoc/>
        public bool Equals(StyleGridLine other) => other == this;

        /// <undoc/>
        public override bool Equals(object obj) => obj is StyleGridLine other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
            }
        }

        public override string ToString() =>
            m_Keyword == StyleKeyword.Undefined ? m_Value.ToString() : m_Keyword.ToString();
    }
}
