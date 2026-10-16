// corelib: System.Index / System.Range — Roslyn `a[^1]`, `s[i..]`, `list[^1]` lowering hedefleri (CS0518 yoksa derlenmez).
// Roslyn deseni: Index.ctor(int, bool) / GetOffset(int) / IsFromEnd / Value; Range.Start/End/GetOffsetAndLength(int);
// string/dizi icin `Substring`/`RuntimeHelpers.GetSubArray` uretir; List<T> icin Count + indexer.
namespace System
{
    public readonly struct Index
    {
        readonly int _value; // >= 0: bastan; < 0: ~value sondan (.NET ile ayni kodlama)

        public Index(int value, bool fromEnd)
        {
            if (value < 0) throw new ArgumentOutOfRangeException("value", "Non-negative number required.");
            int raw = value;
            if (fromEnd) raw = ~value; // ternary YAZMA: `this` dal boyunca yiginda kalir, frontend struct this'i deger sanir
            _value = raw;
        }
        Index(int rawValue) { _value = rawValue; }

        public static Index Start { get { return new Index(0); } }
        public static Index End { get { return new Index(~0); } }
        public static Index FromStart(int value) { return new Index(value, false); }
        public static Index FromEnd(int value) { return new Index(value, true); }

        public int Value { get { return _value < 0 ? ~_value : _value; } }
        public bool IsFromEnd { get { return _value < 0; } }
        public int GetOffset(int length) { return _value < 0 ? length + _value + 1 : _value; }

        public static implicit operator Index(int value) { return FromStart(value); }
        public override bool Equals(object other) { return other is Index i && i._value == _value; }
        public bool Equals(Index other) { return other._value == _value; }
        public override int GetHashCode() { return _value; }
        public override string ToString() { return IsFromEnd ? "^" + Value : Value.ToString(); }
    }

    public readonly struct Range
    {
        public Index Start { get; }
        public Index End { get; }
        public Range(Index start, Index end) { Start = start; End = end; }

        public static Range StartAt(Index start) { return new Range(start, Index.End); }
        public static Range EndAt(Index end) { return new Range(Index.Start, end); }
        public static Range All { get { return new Range(Index.Start, Index.End); } }

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end) throw new ArgumentOutOfRangeException("length");
            return (start, end - start);
        }
        public override bool Equals(object other) { return other is Range r && r.Start.Equals(Start) && r.End.Equals(End); }
        public override int GetHashCode() { return Start.GetHashCode() * 31 + End.GetHashCode(); }
        public override string ToString() { return Start.ToString() + ".." + End.ToString(); }
    }

    // 128 bit kimlik. NewGuid: Random tabanli v4 (kriptografik DEGIL; cihaz/istemci kimligi icin yeterli).
    // ToString: "D" (varsayilan, tireli), "N" (tiresiz), "B" ({...}), "P" ((...)). Parse yok.
    public struct Guid
    {
        readonly uint _a; readonly ushort _b, _c; readonly byte _d, _e, _f, _g, _h, _i, _j, _k;

        public static readonly Guid Empty = new Guid();

        public Guid(byte[] b)
        {
            if (b == null) throw new ArgumentNullException("b");
            if (b.Length != 16) throw new ArgumentException("Byte array for GUID must be exactly 16 bytes long.");
            _a = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
            _b = (ushort)(b[4] | (b[5] << 8));
            _c = (ushort)(b[6] | (b[7] << 8));
            _d = b[8]; _e = b[9]; _f = b[10]; _g = b[11]; _h = b[12]; _i = b[13]; _j = b[14]; _k = b[15];
        }
        public Guid(uint a, ushort b, ushort c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
        { _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _g = g; _h = h; _i = i; _j = j; _k = k; }

        public static Guid NewGuid()
        {
            var r = Random.Shared;
            var b = new byte[16];
            for (int i = 0; i < 16; i++) b[i] = (byte)r.Next(256);
            b[7] = (byte)((b[7] & 0x0F) | 0x40); // surum 4
            b[8] = (byte)((b[8] & 0x3F) | 0x80); // varyant RFC 4122
            return new Guid(b);
        }

        public byte[] ToByteArray()
        {
            return new byte[]
            {
                (byte)_a, (byte)(_a >> 8), (byte)(_a >> 16), (byte)(_a >> 24),
                (byte)_b, (byte)(_b >> 8), (byte)_c, (byte)(_c >> 8),
                _d, _e, _f, _g, _h, _i, _j, _k
            };
        }

        public override string ToString() { return ToString("D"); }
        public string ToString(string format)
        {
            if (format == null || format.Length == 0) format = "D";
            if (format.Length != 1) throw new FormatException("Format string can be only \"D\", \"d\", \"N\", \"n\", \"P\", \"p\", \"B\", \"b\".");
            char f = format[0];
            bool dashes = f != 'N' && f != 'n';
            var sb = new Text.StringBuilder(38);
            if (f == 'B' || f == 'b') sb.Append('{'); else if (f == 'P' || f == 'p') sb.Append('(');
            Hex(sb, _a >> 24, 2); Hex(sb, _a >> 16, 2); Hex(sb, _a >> 8, 2); Hex(sb, _a, 2);
            if (dashes) sb.Append('-');
            Hex(sb, (uint)(_b >> 8), 2); Hex(sb, _b, 2);
            if (dashes) sb.Append('-');
            Hex(sb, (uint)(_c >> 8), 2); Hex(sb, _c, 2);
            if (dashes) sb.Append('-');
            Hex(sb, _d, 2); Hex(sb, _e, 2);
            if (dashes) sb.Append('-');
            Hex(sb, _f, 2); Hex(sb, _g, 2); Hex(sb, _h, 2); Hex(sb, _i, 2); Hex(sb, _j, 2); Hex(sb, _k, 2);
            if (f == 'B' || f == 'b') sb.Append('}'); else if (f == 'P' || f == 'p') sb.Append(')');
            return sb.ToString();
        }
        static void Hex(Text.StringBuilder sb, uint v, int digits)
        {
            const string digitsStr = "0123456789abcdef";
            for (int i = digits - 1; i >= 0; i--) sb.Append(digitsStr[(int)((v >> (i * 4)) & 0xF)]);
        }

        public static bool operator ==(Guid a, Guid b)
        { return a._a == b._a && a._b == b._b && a._c == b._c && a._d == b._d && a._e == b._e && a._f == b._f && a._g == b._g && a._h == b._h && a._i == b._i && a._j == b._j && a._k == b._k; }
        public static bool operator !=(Guid a, Guid b) { return !(a == b); }
        public bool Equals(Guid other) { return this == other; }
        public override bool Equals(object other) { return other is Guid g && g == this; }
        public override int GetHashCode() { return (int)_a ^ ((_b << 16) | _c) ^ ((_f << 24) | (_g << 16) | (_h << 8) | _k); }
    }
}
