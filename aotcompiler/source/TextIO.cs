using System.Text;

namespace DigitoyEngine.Language
{
    // Basit metin serilestirme: bool/int/enum gibi tekil degerler satir (\n ile ayrilir), ama serbest
    // metin alanlari (isim/govde) UZUNLUK-ONEKLI yazilir - icinde \n olsa bile Split('\n') YERINE
    // Substring ile atlanir, hicbir zaman bozulmaz (NativeBody gibi coklu satirli govdeler icin sart).
    public class TextWriter
    {
        readonly StringBuilder sb = new StringBuilder();

        public void Line(object value) => sb.Append(value).Append('\n');

        // null -> "-1", degilse "<uzunluk>\n<icerik>\n" (icerik \n icerse bile guvenli, uzunlukla atlanir)
        public void Str(string value)
        {
            if (value == null) { sb.Append("-1\n"); return; }
            sb.Append(value.Length).Append('\n').Append(value).Append('\n');
        }

        public override string ToString() => sb.ToString();
    }

    public class TextReader
    {
        readonly string s;
        int pos;

        public TextReader(string s) { this.s = s; pos = 0; }

        public string Line()
        {
            int nl = s.IndexOf('\n', pos);
            string line;
            if (nl < 0) { line = s.Substring(pos); pos = s.Length; }
            else { line = s.Substring(pos, nl - pos); pos = nl + 1; }
            return line;
        }

        public int Int() => int.Parse(Line());
        public bool Bool() => bool.Parse(Line());

        public string Str()
        {
            int len = int.Parse(Line());
            if (len < 0) return null;
            string value = s.Substring(pos, len);
            pos += len;
            if (pos < s.Length && s[pos] == '\n') pos++; // yazarken eklenen ayirici
            return value;
        }
    }
}
