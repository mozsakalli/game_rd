namespace System.Text
{
    // Tek kodlama: UTF-8 (motorun tum metin asset'leri). Instance yuzeyi .NET ile ayni,
    // govde statik extern'lere iner (C tarafi Encoding nesnesini bilmez: byte[] <-> string).
    public class Encoding
    {
        public static Encoding utf8 = new Encoding();
        public static Encoding UTF8 { get { return utf8; } }

        public string GetString(byte[] bytes) { return Utf8Decode(bytes, 0, bytes.Length); }
        public string GetString(byte[] bytes, int index, int count) { return Utf8Decode(bytes, index, count); }
        public byte[] GetBytes(string value) { return Utf8Encode(value); }

        public static extern string Utf8Decode(byte[] bytes, int index, int count);
        public static extern byte[] Utf8Encode(string value);
    }
}