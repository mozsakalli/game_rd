using System;
using System.Collections.Generic;
using System.Text;

namespace DigitoyEngine.Frontend
{
    // MiniCs lexer: kaynak -> Tok listesi. Longest-first operator eslesmesi, // ve /* */ yorum.
    public class Tok
    {
        public string Kind; // "id" | "num" | "str" | "chr" | "op" | "eof"
        public string Value;
        public bool IsEscapedIdentifier;
        public bool IsFloat; // num: f suffix'li ya da ondalikli
        public string NumSuffix = ""; // "u" | "l" | "ul" (kucuk harfe indirgenmis)
        public int Pos;
        public int Line;
        public int Col; // 1-tabanli kolon (trace paketi: (satir<<10)|kolon)
        public Tok(string kind, string value, int pos, int line, int col = 0) { Kind = kind; Value = value; Pos = pos; Line = line; Col = col; }
        public override string ToString() => $"{Kind}:{Value}";
    }

    public class CsError : System.Exception
    {
        // line PAKETLI gelir: (satir<<10)|kolon (eski derleyici/vmrt sozlesmesi)
        public CsError(string file, int line, string msg) : base($"{(string.IsNullOrEmpty(file) ? "<kaynak>" : file)}({line >> 10},{line & 1023}): {msg}") { }
    }

    public static class CsLexer
    {
        // ">>" / ">>=" BILEREK YOK: "Box<Box<int>>" kapanisi iki ayri ">" olmali (C# lexer'i da
        // tip baglaminda boler). Shift operatoru eklendiginde parser ardisik ">" ">"i birlestirir.
        static readonly string[] Ops = {
            "<<=", "<<", "<=", ">=", "=>", "?.", "??", "==", "!=", "&&", "||", "++", "--",
            "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=",
            "+", "-", "*", "/", "%", "&", "|", "^", "~", "<", ">", "=",
            "(", ")", "[", "]", "{", "}", ",", ";", ".", "!", "?", ":"
        };

        // u/U, l/L, ul/lu kombinasyonlari (C# tam sayi suffix'leri)
        static string ReadNumSuffix(string src, ref int i, int n)
        {
            bool u = false, l = false;
            for (int k = 0; k < 2 && i < n; k++)
            {
                if (!u && (src[i] == 'u' || src[i] == 'U')) { u = true; i++; }
                else if (!l && (src[i] == 'l' || src[i] == 'L')) { l = true; i++; }
                else break;
            }
            return (u ? "u" : "") + (l ? "l" : "");
        }

        public static List<Tok> Tokenize(string src, string fileName = "")
        {
            var toks = new List<Tok>();
            int i = 0, n = src.Length, line = 1, lineStart = 0;
            int Col(int at) => at - lineStart + 1;
            int Packed(int at) => (line << 10) | (Col(at) & 1023);
            while (i < n)
            {
                char c = src[i];
                if (char.IsWhiteSpace(c) && c != '\n') { i++; continue; }
                if (c == '\n') { line++; i++; lineStart = i; continue; }
                if (c == '/' && i + 1 < n && src[i + 1] == '/') { while (i < n && src[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/')) { if (src[i] == '\n') { line++; lineStart = i + 1; } i++; }
                    i += 2;
                    continue;
                }
                int start = i;
                if (c == '@' && i + 1 < n && src[i + 1] == '"')
                {
                    i += 2;
                    var buf = new StringBuilder();
                    while (i < n)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < n && src[i + 1] == '"') { buf.Append('"'); i += 2; continue; }
                            break;
                        }
                        if (src[i] == '\n') { line++; lineStart = i + 1; }
                        buf.Append(src[i++]);
                    }
                    if (i >= n) throw new CsError(fileName, Packed(start), "kapanmayan verbatim string");
                    i++;
                    toks.Add(new Tok("str", buf.ToString(), start, line, Col(start)));
                    continue;
                }
                if (c == '@' && i + 1 < n && (char.IsLetter(src[i + 1]) || src[i + 1] == '_'))
                {
                    i++;
                    int nameStart = i;
                    while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    toks.Add(new Tok("id", src.Substring(nameStart, i - nameStart), start, line, Col(start)) { IsEscapedIdentifier = true });
                    continue;
                }
                if (c == '$' && i + 1 < n && src[i + 1] == '"')
                {
                    i += 2;
                    var buf = new StringBuilder();
                    while (i < n && src[i] != '"')
                    {
                        if (src[i] == '\\' && i + 1 < n)
                        {
                            i++;
                            buf.Append(src[i] == 'n' ? '\n' : src[i] == 't' ? '\t' : src[i] == 'r' ? '\r' : src[i]);
                        }
                        else buf.Append(src[i]);
                        i++;
                    }
                    if (i >= n) throw new CsError(fileName, Packed(start), "kapanmayan interpolated string");
                    i++;
                    toks.Add(new Tok("istr", buf.ToString(), start, line, Col(start)));
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    var buf = new StringBuilder();
                    while (i < n && src[i] != '"')
                    {
                        if (src[i] == '\\' && i + 1 < n)
                        {
                            i++;
                            buf.Append(src[i] == 'n' ? '\n' : src[i] == 't' ? '\t' : src[i] == 'r' ? '\r' : src[i]);
                        }
                        else buf.Append(src[i]);
                        i++;
                    }
                    if (i >= n) throw new CsError(fileName, Packed(start), "kapanmayan string");
                    i++;
                    toks.Add(new Tok("str", buf.ToString(), start, line, Col(start)));
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    if (i >= n) throw new CsError(fileName, Packed(start), "kapanmayan char");
                    char cv = src[i];
                    if (cv == '\\' && i + 1 < n)
                    {
                        i++;
                        cv = src[i] == 'n' ? '\n' : src[i] == 't' ? '\t' : src[i] == 'r' ? '\r' : src[i] == '0' ? '\0' : src[i];
                    }
                    i++;
                    if (i >= n || src[i] != '\'') throw new CsError(fileName, Packed(start), "kapanmayan char");
                    i++;
                    toks.Add(new Tok("chr", cv.ToString(), start, line, Col(start)));
                    continue;
                }
                if (c == '.' && i + 1 < n && char.IsDigit(src[i + 1]))
                {
                    i++;
                    while (i < n && char.IsDigit(src[i])) i++;
                    string suffix = "";
                    if (i < n && (src[i] == 'f' || src[i] == 'F')) { suffix = "f"; i++; }
                    else if (i < n && (src[i] == 'd' || src[i] == 'D')) { suffix = "d"; i++; }
                    toks.Add(new Tok("num", "0" + src.Substring(start, i - start - suffix.Length), start, line, Col(start)) { IsFloat = true, NumSuffix = suffix });
                    continue;
                }
                if (char.IsDigit(c))
                {
                    if (c == '0' && i + 1 < n && (src[i + 1] == 'x' || src[i + 1] == 'X')) // hex: 0xFF
                    {
                        i += 2;
                        ulong hv = 0;
                        int hd = 0;
                        while (i < n && Uri.IsHexDigit(src[i])) { hv = hv * 16 + (ulong)Convert.ToInt32(src[i].ToString(), 16); i++; hd++; }
                        if (hd == 0) throw new CsError(fileName, Packed(start), "hex rakami bekleniyordu");
                        toks.Add(new Tok("num", hv.ToString(), start, line, Col(start)) { NumSuffix = ReadNumSuffix(src, ref i, n) });
                        continue;
                    }
                    bool isFloat = false;
                    while (i < n && char.IsDigit(src[i])) i++;
                    if (i + 1 < n && src[i] == '.' && char.IsDigit(src[i + 1]))
                    {
                        isFloat = true;
                        i++;
                        while (i < n && char.IsDigit(src[i])) i++;
                    }
                    if (i < n && (src[i] == 'e' || src[i] == 'E')) // us: 1e15, 5E-324, 3.4e38f
                    {
                        int j = i + 1;
                        if (j < n && (src[j] == '+' || src[j] == '-')) j++;
                        if (j < n && char.IsDigit(src[j]))
                        {
                            i = j;
                            while (i < n && char.IsDigit(src[i])) i++;
                            isFloat = true;
                        }
                    }
                    string num = src.Substring(start, i - start);
                    string fsfx = "";
                    if (i < n && (src[i] == 'f' || src[i] == 'F')) { isFloat = true; fsfx = "f"; i++; }
                    else if (i < n && (src[i] == 'd' || src[i] == 'D')) { isFloat = true; fsfx = "d"; i++; }
                    var sfx = isFloat ? fsfx : ReadNumSuffix(src, ref i, n);
                    toks.Add(new Tok("num", num, start, line, Col(start)) { IsFloat = isFloat, NumSuffix = sfx });
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    toks.Add(new Tok("id", src.Substring(start, i - start), start, line, Col(start)));
                    continue;
                }
                bool matched = false;
                foreach (var op in Ops)
                {
                    if (i + op.Length <= n && src.Substring(i, op.Length) == op)
                    {
                        toks.Add(new Tok("op", op, start, line, Col(start)));
                        i += op.Length;
                        matched = true;
                        break;
                    }
                }
                if (!matched) throw new CsError(fileName, Packed(i), $"beklenmeyen karakter: '{c}'");
            }
            toks.Add(new Tok("eof", "", n, line));
            return toks;
        }
    }
}
