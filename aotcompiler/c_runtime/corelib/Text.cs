namespace System.Text
{
    class Encoding
    {
        public static extern Encoding UTF8 { get; }
        public extern string GetString(byte[] bytes);
        public extern string GetString(byte[] bytes, int index, int count);
        public extern byte[] GetBytes(string value);
    }
}