namespace System.Text
{
    // Tek kodlama: UTF-8 (motorun tum metin asset'leri). Instance yuzeyi .NET ile ayni,
    // govde statik extern'lere iner (C tarafi Encoding nesnesini bilmez: byte[] <-> string).
    public class Encoding
    {
        public static Encoding utf8 = new Encoding();
        public static Encoding UTF8 { get { return utf8; } }

        public string GetString(byte[] bytes) { if (bytes == null) throw new ArgumentNullException("bytes"); return Utf8Decode(bytes, 0, bytes.Length); }
        public string GetString(byte[] bytes, int index, int count)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (index < 0 || count < 0) throw new ArgumentOutOfRangeException(index < 0 ? "index" : "count", "Non-negative number required.");
            if (bytes.Length - index < count) throw new ArgumentOutOfRangeException("bytes", "Index and count must refer to a location within the buffer.");
            return Utf8Decode(bytes, index, count);
        }
        public byte[] GetBytes(string value) { if (value == null) throw new ArgumentNullException("s"); return Utf8Encode(value); }
        // UTF-8 bayt sayisi, alloc yok (Codec gibi on-boyutlama yapanlar icin). Eslesmemis surrogate -> U+FFFD (3 bayt), .NET gibi.
        public int GetByteCount(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            return Utf8ByteCount(s, 0, s.Length);
        }
        static int Utf8ByteCount(string s, int index, int count)
        {
            int n = 0, end = index + count;
            for (int i = index; i < end; i++)
            {
                int c = s[i];
                if (c < 0x80) n += 1;
                else if (c < 0x800) n += 2;
                else if (c >= 0xD800 && c <= 0xDBFF && i + 1 < end && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF) { n += 4; i++; }
                else n += 3;
            }
            return n;
        }
        // s[charIndex..+charCount] -> bytes[byteIndex..]; yazilan bayt sayisi. Alloc yok. .NET: sigmazsa ArgumentException.
        public int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (charIndex < 0 || charCount < 0) throw new ArgumentOutOfRangeException(charIndex < 0 ? "charIndex" : "charCount", "Non-negative number required.");
            if (s.Length - charIndex < charCount) throw new ArgumentOutOfRangeException("s", "Index and count must refer to a location within the string.");
            if (byteIndex < 0 || byteIndex > bytes.Length) throw new ArgumentOutOfRangeException("byteIndex", "Index was out of range. Must be non-negative and less than the size of the collection.");
            if (bytes.Length - byteIndex < Utf8ByteCount(s, charIndex, charCount)) throw new ArgumentException("The output byte buffer is too small to contain the encoded data.", "bytes");
            int p = byteIndex, end = charIndex + charCount;
            for (int i = charIndex; i < end; i++)
            {
                int c = s[i];
                if (c >= 0xD800 && c <= 0xDBFF && i + 1 < end && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                    c = 0x10000 + ((c - 0xD800) << 10) + (s[++i] - 0xDC00);
                else if (c >= 0xD800 && c <= 0xDFFF) c = 0xFFFD; // eslesmemis surrogate -> U+FFFD (.NET replacement fallback)
                if (c < 0x80) bytes[p++] = (byte)c;
                else if (c < 0x800) { bytes[p++] = (byte)(0xC0 | (c >> 6)); bytes[p++] = (byte)(0x80 | (c & 0x3F)); }
                else if (c < 0x10000) { bytes[p++] = (byte)(0xE0 | (c >> 12)); bytes[p++] = (byte)(0x80 | ((c >> 6) & 0x3F)); bytes[p++] = (byte)(0x80 | (c & 0x3F)); }
                else { bytes[p++] = (byte)(0xF0 | (c >> 18)); bytes[p++] = (byte)(0x80 | ((c >> 12) & 0x3F)); bytes[p++] = (byte)(0x80 | ((c >> 6) & 0x3F)); bytes[p++] = (byte)(0x80 | (c & 0x3F)); }
            }
            return p - byteIndex;
        }
        // Span kaynagi: ToArray (intrinsic) ile kopyalanir — kisa UTF-8 anahtarlar icin kabul (hot path degil).
        public string GetString(ReadOnlySpan<byte> bytes) { return Utf8Decode(bytes.ToArray(), 0, bytes.Length); }

        public static extern string Utf8Decode(byte[] bytes, int index, int count);
        public static extern byte[] Utf8Encode(string value);
    }
}