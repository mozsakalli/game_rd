// corelib: C# string interpolation ($"...") lowering hedefi. Roslyn her interpolasyonu
// DefaultInterpolatedStringHandler (ctor + AppendLiteral/AppendFormatted + ToStringAndClear) cagirilarina cevirir.
namespace System.Runtime.CompilerServices
{
    struct DefaultInterpolatedStringHandler
    {
        System.Text.StringBuilder sb;
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount) { sb = new System.Text.StringBuilder(); }
        public void AppendLiteral(string value) { sb.Append(value); }
        public void AppendFormatted<T>(T value) { sb.Append(value.ToString()); }
        public void AppendFormatted(string value) { sb.Append(value); }
        public string ToStringAndClear() { return sb.ToString(); }
    }
}
