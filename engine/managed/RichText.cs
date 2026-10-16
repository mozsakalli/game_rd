using System;

namespace DigitoyEngine;

// Satir ici zengin metin (TMPro alt kumesi) — tek tarayici, LayoutBox.Text ve editor denetimleri (LocAudit) paylasir.
// Tag'ler: <b> <i> <u> <color=#RRGGBB|#RRGGBBAA|ad> <size=N|+N|-N|N%> <font=ad> <link=id> <noparse> + kapanislari.
//   * <b> = SDF weight (+BoldWeight), <i> = baseline etrafinda shear (ItalicSkew): font listesi gerekmez; gercek
//     bold/italik yuz isteyen <font=Bold> (Fonts tablosu adi, docs/engine-notes.md FONT KAYIT DEFTERI).
//   * size: em px (textSize birimi); +N/-N taban boyuta gore; N% yuzde. link gorunmez, LayoutBox rect tutar (LinkAt).
//   * Gecersiz/eslesmeyen tag OLDUGU GIBI cizilir ('<' literal); fazla kapanis yutulur; '>' 64 karakter icinde
//     yoksa / icinde '\n' varsa tag degildir. <noparse>..</noparse> icinde tag yorumlanmaz (Escape bunu kullanir).
//   * Her zaman acik; component'e alan eklenmez. Parse yalniz yerlesim kurulurken (rebuild) kosar, sicak yolda yok.
// Durum (State) kalem ilerledikce guncellenir; ic ice acilislar Frame yigininda (tam durum anlik goruntusu),
// kapanis = en son acilan ayni tur tag'e kadar pop. Yigin sahibinin dizisi (alloc yok).
public static class RichText
{
    public const float BoldWeight = 0.75f;       // SDF px (LayoutBox.textWeight birimi); kalin yuzlerde 1.5 harfleri yapistiriyordu
    public const float ItalicSkew = 0.2f;        // x = x - skew * (y - baseline)
    public const float UnderlineOffset = 0.08f;  // em: baseline'in altina
    public const float UnderlineThickness = 0.06f; // em (en az 1 px)
    public const int MaxDepth = 16;
    const int MaxTagLen = 64;

    public enum Tag : byte { None, Bold, Italic, Underline, Color, Size, Font, Link, NoParse }

    public struct State
    {
        public float Size;
        public Font Font;
        public Color Color;
        public bool HasColor, Bold, Italic, Underline, NoParse;
        public int LinkStart, LinkLen; // text icindeki id araligi; LinkLen 0 = link yok

        public bool InLink => LinkLen > 0;
    }

    public struct Frame
    {
        public Tag Tag;
        public State Prev;
    }

    // Metin konumu + durum + yigin derinligi: NextLine/EmitRun bunu ilerletir (yigin dizisi sahipte).
    public struct Cursor
    {
        public int Pos;
        public State St;
        public int Depth;

        public static Cursor Start(float size, Font font)
            => new() { St = new State { Size = size, Font = font } };
    }

    // s[i] == '<' iken: tag ise tuketir (i tag sonrasina, durum guncel) ve true; degilse false (literal '<').
    // baseSize: <size=+N/-N/N%> tabani. Yigin tasarsa tag tuketilir ama durum degismez (kapanisi da yutulur).
    public static bool TryTag(string s, ref int i, ref State st, Frame[] stack, ref int depth, float baseSize)
    {
        int len = s.Length;
        int p = i + 1;
        if (p >= len)
            return false;
        bool close = s[p] == '/';
        if (close)
            p++;
        int nameStart = p;
        while (p < len && p - i < MaxTagLen && IsNameChar(s[p]))
            p++;
        int nameLen = p - nameStart;
        if (nameLen == 0)
            return false;
        int argStart = -1, argLen = 0;
        if (p < len && s[p] == '=' && !close)
        {
            argStart = ++p;
            while (p < len && p - i < MaxTagLen && s[p] != '>' && s[p] != '<' && s[p] != '\n')
                p++;
            argLen = p - argStart;
            // Tirnakli arguman serbest: <link="id">
            if (argLen >= 2 && s[argStart] == '"' && s[argStart + argLen - 1] == '"')
            {
                argStart++;
                argLen -= 2;
            }
        }
        if (p >= len || s[p] != '>')
            return false;
        var tag = TagOf(s.AsSpan(nameStart, nameLen));
        if (tag == Tag.None)
            return false;
        if (st.NoParse && !(close && tag == Tag.NoParse))
            return false; // noparse icinde her sey literal
        if (!close && tag != Tag.NoParse && argStart < 0 && tag is Tag.Color or Tag.Size or Tag.Font or Tag.Link)
            return false; // arguman zorunlu

        int end = p + 1;
        if (close)
        {
            // En son acilan ayni tur frame'e kadar pop (yanlis sirali kapanis: aradakiler de kapanir, TMPro gibi).
            int k = depth - 1;
            while (k >= 0 && stack[k].Tag != tag)
                k--;
            if (k >= 0)
            {
                st = stack[k].Prev;
                depth = k;
            }
            i = end;
            return true; // eslesmeyen kapanis yutulur
        }
        if (depth >= MaxDepth)
        {
            i = end;
            return true;
        }
        var prev = st;
        bool applied = true;
        switch (tag)
        {
            case Tag.Bold: st.Bold = true; break;
            case Tag.Italic: st.Italic = true; break;
            case Tag.Underline: st.Underline = true; break;
            case Tag.NoParse: st.NoParse = true; break;
            case Tag.Color:
                if (TryParseColor(s.AsSpan(argStart, argLen), out var c)) { st.Color = c; st.HasColor = true; }
                else applied = false;
                break;
            case Tag.Size:
                if (TryParseSize(s.AsSpan(argStart, argLen), baseSize, out float sz)) st.Size = sz;
                else applied = false;
                break;
            case Tag.Font:
                {
                    var f = Fonts.Resolve(s.AsSpan(argStart, argLen));
                    if (f != null) st.Font = f;
                    else applied = false;
                    break;
                }
            case Tag.Link:
                st.LinkStart = argStart;
                st.LinkLen = argLen;
                break;
        }
        if (!applied)
            return false; // bozuk arguman: literal kalsin (gorunur hata)
        stack[depth++] = new Frame { Tag = tag, Prev = prev };
        i = end;
        return true;
    }

    static bool IsNameChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    static Tag TagOf(ReadOnlySpan<char> n)
    {
        if (n.Length == 1)
        {
            switch (n[0] | 0x20)
            {
                case 'b': return Tag.Bold;
                case 'i': return Tag.Italic;
                case 'u': return Tag.Underline;
            }
            return Tag.None;
        }
        if (n.Equals("color", StringComparison.OrdinalIgnoreCase)) return Tag.Color;
        if (n.Equals("size", StringComparison.OrdinalIgnoreCase)) return Tag.Size;
        if (n.Equals("font", StringComparison.OrdinalIgnoreCase)) return Tag.Font;
        if (n.Equals("link", StringComparison.OrdinalIgnoreCase)) return Tag.Link;
        if (n.Equals("noparse", StringComparison.OrdinalIgnoreCase)) return Tag.NoParse;
        return Tag.None;
    }

    // "#RRGGBB" | "#RRGGBBAA" | ad (white black red green blue yellow orange purple cyan magenta gray grey).
    public static bool TryParseColor(ReadOnlySpan<char> a, out Color c)
    {
        c = Color.White;
        if (a.Length == 0)
            return false;
        if (a[0] == '#')
        {
            if (a.Length != 7 && a.Length != 9)
                return false;
            int v = 0;
            for (int k = 1; k < a.Length; k++)
            {
                int h = Hex(a[k]);
                if (h < 0) return false;
                v = (v << 4) | h;
            }
            if (a.Length == 7)
                c = new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
            else
                c = new Color((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }
        if (Eq(a, "white")) { c = Color.White; return true; }
        if (Eq(a, "black")) { c = Color.Black; return true; }
        if (Eq(a, "red")) { c = new Color(255, 0, 0, 255); return true; }
        if (Eq(a, "green")) { c = new Color(0, 255, 0, 255); return true; }
        if (Eq(a, "blue")) { c = new Color(0, 0, 255, 255); return true; }
        if (Eq(a, "yellow")) { c = new Color(255, 255, 0, 255); return true; }
        if (Eq(a, "orange")) { c = new Color(255, 165, 0, 255); return true; }
        if (Eq(a, "purple")) { c = new Color(160, 32, 240, 255); return true; }
        if (Eq(a, "cyan")) { c = new Color(0, 255, 255, 255); return true; }
        if (Eq(a, "magenta")) { c = new Color(255, 0, 255, 255); return true; }
        if (Eq(a, "gray") || Eq(a, "grey")) { c = new Color(128, 128, 128, 255); return true; }
        return false;
    }

    static bool Eq(ReadOnlySpan<char> a, string b) => a.Equals(b.AsSpan(), StringComparison.OrdinalIgnoreCase);

    static int Hex(char ch)
        => ch is >= '0' and <= '9' ? ch - '0'
         : ch is >= 'a' and <= 'f' ? ch - 'a' + 10
         : ch is >= 'A' and <= 'F' ? ch - 'A' + 10 : -1;

    // "N" | "+N" | "-N" | "N%" (ondalik serbest; corelib yuzeyi dar: elle parse).
    public static bool TryParseSize(ReadOnlySpan<char> a, float baseSize, out float size)
    {
        size = baseSize;
        if (a.Length == 0)
            return false;
        int k = 0;
        int sign = 0;
        if (a[0] == '+') { sign = 1; k = 1; }
        else if (a[0] == '-') { sign = -1; k = 1; }
        bool pct = a[^1] == '%';
        int endN = pct ? a.Length - 1 : a.Length;
        if (!TryParseNumber(a.Slice(k, Math.Max(0, endN - k)), out float n))
            return false;
        if (pct)
            size = baseSize * (sign == 0 ? n : 100f + sign * n) * 0.01f;
        else
            size = sign == 0 ? n : baseSize + sign * n;
        return size > 0;
    }

    static bool TryParseNumber(ReadOnlySpan<char> a, out float v)
    {
        v = 0;
        if (a.Length == 0)
            return false;
        float frac = 0, scale = 1;
        bool dot = false, any = false;
        foreach (char ch in a)
        {
            if (ch == '.') { if (dot) return false; dot = true; continue; }
            if (ch is < '0' or > '9') return false;
            any = true;
            if (!dot) v = v * 10 + (ch - '0');
            else { scale *= 0.1f; frac += (ch - '0') * scale; }
        }
        v += frac;
        return any;
    }

    // Dinamik/kullanici metni (nick) tag olarak yorumlanmasin: <noparse> ile sarar.
    public static string Escape(string s)
        => string.IsNullOrEmpty(s) || s.IndexOf('<') < 0 ? s ?? "" : "<noparse>" + s + "</noparse>";

    // Editor/denetim: metni tag'siz gezer; her glyph icin (karakter, durum) callback'i (LocAudit glyph kapsami).
    public static void ForEachGlyph(string s, float baseSize, Font baseFont, Action<int, State> visit)
    {
        var stack = new Frame[MaxDepth];
        var cur = Cursor.Start(baseSize, baseFont);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '<' && TryTag(s, ref i, ref cur.St, stack, ref cur.Depth, baseSize))
                continue;
            int cp = char.IsHighSurrogate(c) && i + 1 < s.Length ? char.ConvertToUtf32(c, s[i + 1]) : c;
            if (cp > 0xFFFF) i++;
            visit(cp, cur.St);
            i++;
        }
    }
}
