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
        // Span kaynagi: ToArray (intrinsic) ile kopyalanir — kisa UTF-8 anahtarlar icin kabul (hot path degil).
        public string GetString(ReadOnlySpan<byte> bytes) { return Utf8Decode(bytes.ToArray(), 0, bytes.Length); }

        public static extern string Utf8Decode(byte[] bytes, int index, int count);
        public static extern byte[] Utf8Encode(string value);
    }
}