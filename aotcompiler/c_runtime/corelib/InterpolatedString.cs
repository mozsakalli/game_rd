// corelib: C# string interpolation ($"...") lowering hedefi. Roslyn her interpolasyonu
// DefaultInterpolatedStringHandler (ctor + AppendLiteral/AppendFormatted + ToStringAndClear) cagirilarina cevirir.
namespace System.Runtime.CompilerServices
{
    public struct DefaultInterpolatedStringHandler
    {
        public System.Text.StringBuilder sb;
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount) { sb = new System.Text.StringBuilder(); }
        public void AppendLiteral(string value) { sb.Append(value); }
        public void AppendFormatted<T>(T value) { sb.Append(value.ToString()); }
        // {x:fmt} / {x,align}: format YOK SAYILIR (kultur/IFormattable yok, string.Format ile ayni politika), hizalama uygulanir.
        public void AppendFormatted<T>(T value, string format) { AppendFormatted(value); }
        public void AppendFormatted<T>(T value, int alignment) { AppendFormatted(value, alignment, null); }
        public void AppendFormatted<T>(T value, int alignment, string format)
        {
            string s = value.ToString();
            int pad = (alignment < 0 ? -alignment : alignment) - s.Length;
            if (alignment > 0 && pad > 0) sb.Append(' ', pad);
            sb.Append(s);
            if (alignment < 0 && pad > 0) sb.Append(' ', pad);
        }
        public void AppendFormatted(string value) { sb.Append(value); }
        public void AppendFormatted(string value, int alignment, string format) { AppendFormatted<string>(value, alignment, format); }
        public string ToStringAndClear() { return sb.ToString(); }
    }
}
