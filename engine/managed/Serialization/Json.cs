using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DigitoyEngine;

// JSON: kucuk DOM (JsonNode) + ileri yonlu yazici (JsonWriter) + reflection binder (Json.FromJson/ToJson).
// Kullanim soguk yol (bootstrap settings, remote config, inbox, PlayerPrefs DTO'lari; wire protokolu Codec'tir) —
// hedef: bagimliliksiz, .NET ve AOT corelib'de AYNI kod, gereksiz alloc yok:
//   * kacissiz string = tek Substring; kacisli string UTF-8 scratch + Encoding.UTF8.GetString (corelib'de
//     new string(char[]) ve gercek StringBuilder yok)
//   * sayi hem long hem double tasinir (serverTime/version gibi 2^53 ustu long'lar kayipsiz)
//   * yazici dogrudan UTF-8 byte[] uretir (Http.PostAsync govdesi) — string istenirse tek decode
// Binder kurallari (Unity JsonUtility'nin duzeltilmisi): public alan + [SerializeField], [NonSerialized] haric,
// [JsonField("ad")] override; bilinmeyen alan atlanir, eksik alan dokunulmaz (ctor varsayilani kalir),
// JSON null -> referans null (JsonUtility bos nesne uretirdi), kok dizi/liste serbest, T? desteklenir.
// Desteklenen tipler: int/long/float/double/bool/string/byte/short/enum(ad|sayi)/T?/T[]/List<T>/
// Dictionary<string,T>/ic ice sinif+struct/Vec2-3-4+Color ([..] dizisi)/JsonNode (ham alt agac).
// Asset/GameObject/Component referansi JSON'a girmez (JsonException). Yorum/hex/NaN yok (JSON degil).
public sealed class JsonException : Exception
{
    public JsonException(string message) : base(message) { }
}

public sealed class JsonNode
{
    public enum Kind : byte { Null, Bool, Number, String, Array, Object }

    readonly Kind _kind;
    readonly bool _bool;
    readonly bool _isInt;
    readonly long _long;
    readonly double _double;
    readonly string _str;

    public List<JsonNode> Items;                           // Array
    public List<KeyValuePair<string, JsonNode>> Fields;    // Object (sira korunur)

    JsonNode(Kind k) { _kind = k; }
    JsonNode(bool b) { _kind = Kind.Bool; _bool = b; }
    JsonNode(long v) { _kind = Kind.Number; _isInt = true; _long = v; _double = v; }
    JsonNode(double v) { _kind = Kind.Number; _double = v; }
    JsonNode(string s) { _kind = Kind.String; _str = s; }

    // Paylasilan degismez dugumler (parser alloc etmez).
    public static readonly JsonNode Null = new(Kind.Null);
    public static readonly JsonNode True = new(true);
    public static readonly JsonNode False = new(false);

    public static JsonNode Bool(bool b) => b ? True : False;
    public static JsonNode Number(long v) => new(v);
    public static JsonNode Number(double v) => new(v);
    public static JsonNode String(string s) => s == null ? Null : new(s);
    public static JsonNode Array() => new(Kind.Array) { Items = new List<JsonNode>() };
    public static JsonNode Object() => new(Kind.Object) { Fields = new List<KeyValuePair<string, JsonNode>>() };

    public Kind Type => _kind;
    public bool IsNull => _kind == Kind.Null;
    public bool IsBool => _kind == Kind.Bool;
    public bool IsNumber => _kind == Kind.Number;
    public bool IsInteger => _kind == Kind.Number && _isInt;
    public bool IsString => _kind == Kind.String;
    public bool IsArray => _kind == Kind.Array;
    public bool IsObject => _kind == Kind.Object;

    public int Count => Items != null ? Items.Count : Fields != null ? Fields.Count : 0;

    public JsonNode this[int index] => Items != null && (uint)index < (uint)Items.Count ? Items[index] : null;

    public JsonNode this[string key]
    {
        get
        {
            if (Fields == null) return null;
            for (int i = 0; i < Fields.Count; i++)
                if (Fields[i].Key == key) return Fields[i].Value;
            return null;
        }
    }

    public JsonNode Add(JsonNode item) { Items.Add(item ?? Null); return this; }
    public JsonNode Add(string key, JsonNode value) { Fields.Add(new KeyValuePair<string, JsonNode>(key, value ?? Null)); return this; }

    // Skaler okuma; tip uymazsa varsayilan (sessiz).
    public string AsString(string def = null) => _kind == Kind.String ? _str : def;
    public bool AsBool(bool def = false) => _kind == Kind.Bool ? _bool : def;
    public long AsLong(long def = 0) => _kind == Kind.Number ? (_isInt ? _long : (long)_double) : def;
    public int AsInt(int def = 0) => _kind == Kind.Number ? (int)(_isInt ? _long : (long)_double) : def;
    public double AsDouble(double def = 0) => _kind == Kind.Number ? _double : def;

    public string GetString(string key, string def = null) => this[key] is { } n ? n.AsString(def) : def;
    public bool GetBool(string key, bool def = false) => this[key] is { } n ? n.AsBool(def) : def;
    public long GetLong(string key, long def = 0) => this[key] is { } n ? n.AsLong(def) : def;
    public int GetInt(string key, int def = 0) => this[key] is { } n ? n.AsInt(def) : def;
    public double GetDouble(string key, double def = 0) => this[key] is { } n ? n.AsDouble(def) : def;

    public string ToJson() => Json.Write(this);
    public override string ToString() => ToJson();
}

// UTF-8 uretir; virgul/ad durumunu kendisi izler. Reset ile yeniden kullanilir (tampon kalir).
public sealed class JsonWriter
{
    byte[] _buf;
    int _len;
    bool[] _first = new bool[16];
    int _depth;
    bool _afterName;

    public JsonWriter(int capacity = 256) { _buf = new byte[capacity < 16 ? 16 : capacity]; }

    public int Length => _len;

    public void Reset() { _len = 0; _depth = 0; _afterName = false; }

    public byte[] ToArray()
    {
        var a = new byte[_len];
        System.Buffer.BlockCopy(_buf, 0, a, 0, _len);
        return a;
    }

    public override string ToString() => Encoding.UTF8.GetString(_buf, 0, _len);

    void Ensure(int extra)
    {
        if (_len + extra <= _buf.Length) return;
        int n = _buf.Length * 2;
        while (n < _len + extra) n *= 2;
        var nb = new byte[n];
        System.Buffer.BlockCopy(_buf, 0, nb, 0, _len);
        _buf = nb;
    }

    void Put(byte b) { Ensure(1); _buf[_len++] = b; }

    void PutAscii(string s)
    {
        Ensure(s.Length);
        for (int i = 0; i < s.Length; i++) _buf[_len++] = (byte)s[i];
    }

    // Deger baslangici: ad'dan sonra virgul yok; aksi halde ayni duzeyde ilk degilse virgul.
    void BeginValue()
    {
        if (_afterName) { _afterName = false; return; }
        if (_depth > 0)
        {
            if (_first[_depth - 1]) _first[_depth - 1] = false;
            else Put((byte)',');
        }
    }

    void Push()
    {
        if (_depth == _first.Length) System.Array.Resize(ref _first, _depth * 2);
        _first[_depth++] = true;
    }

    public void StartObject() { BeginValue(); Put((byte)'{'); Push(); }
    public void EndObject() { _depth--; Put((byte)'}'); }
    public void StartArray() { BeginValue(); Put((byte)'['); Push(); }
    public void EndArray() { _depth--; Put((byte)']'); }

    public void Name(string name)
    {
        BeginValue();
        WriteString(name ?? "");
        Put((byte)':');
        _afterName = true;
    }

    public void Null() { BeginValue(); PutAscii("null"); }
    public void Value(bool v) { BeginValue(); PutAscii(v ? "true" : "false"); }
    public void Value(int v) { Value((long)v); }

    public void Value(long v)
    {
        BeginValue();
        Ensure(20);
        if (v == 0) { _buf[_len++] = (byte)'0'; return; }
        if (v < 0)
        {
            _buf[_len++] = (byte)'-';
            if (v == long.MinValue) { PutAscii("9223372036854775808"); return; }
            v = -v;
        }
        int start = _len;
        while (v > 0) { _buf[_len++] = (byte)('0' + (int)(v % 10)); v /= 10; }
        for (int i = start, j = _len - 1; i < j; i++, j--) { var t = _buf[i]; _buf[i] = _buf[j]; _buf[j] = t; }
    }

    public void Value(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) { Null(); return; }
        if (v == (long)v && v > -1e15 && v < 1e15) { Value((long)v); return; }
        BeginValue();
        PutNumberText(v.ToString());
    }

    public void Value(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) { Null(); return; }
        if (v == (long)v && v > -1e9f && v < 1e9f) { Value((long)v); return; }
        BeginValue();
        PutNumberText(v.ToString());
    }

    // ToString kultur bagimli olabilir (tr: "1,5"); corelib'de InvariantCulture overload'u yok -> virgulu duzelt.
    void PutNumberText(string s)
    {
        Ensure(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            _buf[_len++] = (byte)(c == ',' ? '.' : c);
        }
    }

    public void Value(string s)
    {
        if (s == null) { Null(); return; }
        BeginValue();
        WriteString(s);
    }

    void WriteString(string s)
    {
        Ensure(s.Length + 2);
        _buf[_len++] = (byte)'"';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80)
            {
                if (c >= 0x20 && c != '"' && c != '\\') { Ensure(1); _buf[_len++] = (byte)c; continue; }
                Ensure(6);
                _buf[_len++] = (byte)'\\';
                switch (c)
                {
                    case '"': _buf[_len++] = (byte)'"'; break;
                    case '\\': _buf[_len++] = (byte)'\\'; break;
                    case '\n': _buf[_len++] = (byte)'n'; break;
                    case '\r': _buf[_len++] = (byte)'r'; break;
                    case '\t': _buf[_len++] = (byte)'t'; break;
                    case '\b': _buf[_len++] = (byte)'b'; break;
                    case '\f': _buf[_len++] = (byte)'f'; break;
                    default:
                        _buf[_len++] = (byte)'u'; _buf[_len++] = (byte)'0'; _buf[_len++] = (byte)'0';
                        _buf[_len++] = Hex(c >> 4); _buf[_len++] = Hex(c & 15);
                        break;
                }
                continue;
            }
            int cp = c;
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
            {
                cp = 0x10000 + ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00);
                i++;
            }
            Ensure(4);
            _len = Json.PutUtf8(_buf, _len, cp);
        }
        Ensure(1);
        _buf[_len++] = (byte)'"';
    }

    static byte Hex(int v) => (byte)(v < 10 ? '0' + v : 'a' + v - 10);
}

public static class Json
{
    // ---------------- parse ----------------

    public static JsonNode Parse(string json)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));
        var p = new Parser(json);
        return p.ParseRoot();
    }

    sealed class Parser
    {
        readonly string s;
        int pos;
        byte[] scratch;

        public Parser(string s) { this.s = s; }

        JsonException Err(string msg) => new JsonException("pos " + pos + ": " + msg);

        public JsonNode ParseRoot()
        {
            var n = ParseValue();
            SkipWs();
            if (pos < s.Length) throw Err("trailing characters");
            return n;
        }

        void SkipWs()
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') pos++;
                else break;
            }
        }

        JsonNode ParseValue()
        {
            SkipWs();
            if (pos >= s.Length) throw Err("unexpected end");
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': pos++; return JsonNode.String(ParseString());
                case 't': Expect("true"); return JsonNode.True;
                case 'f': Expect("false"); return JsonNode.False;
                case 'n': Expect("null"); return JsonNode.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                    throw Err("unexpected '" + c + "'");
            }
        }

        void Expect(string lit)
        {
            if (pos + lit.Length > s.Length) throw Err("unexpected end");
            for (int i = 0; i < lit.Length; i++)
                if (s[pos + i] != lit[i]) throw Err("expected '" + lit + "'");
            pos += lit.Length;
        }

        JsonNode ParseObject()
        {
            pos++; // {
            var node = JsonNode.Object();
            SkipWs();
            if (pos < s.Length && s[pos] == '}') { pos++; return node; }
            while (true)
            {
                SkipWs();
                if (pos >= s.Length) throw Err("unexpected end in object");
                if (s[pos] != '"') throw Err("expected '\"' (object key)");
                pos++;
                string key = ParseString();
                SkipWs();
                if (pos >= s.Length || s[pos] != ':') throw Err("expected ':'");
                pos++;
                node.Fields.Add(new KeyValuePair<string, JsonNode>(key, ParseValue()));
                SkipWs();
                if (pos >= s.Length) throw Err("unexpected end in object");
                char c = s[pos++];
                if (c == ',') continue;
                if (c == '}') return node;
                throw Err("expected ',' or '}'");
            }
        }

        JsonNode ParseArray()
        {
            pos++; // [
            var node = JsonNode.Array();
            SkipWs();
            if (pos < s.Length && s[pos] == ']') { pos++; return node; }
            while (true)
            {
                node.Items.Add(ParseValue());
                SkipWs();
                if (pos >= s.Length) throw Err("unexpected end in array");
                char c = s[pos++];
                if (c == ',') continue;
                if (c == ']') return node;
                throw Err("expected ',' or ']'");
            }
        }

        // pos acilis tirnagindan sonra. Kacis yoksa tek Substring; varsa UTF-8 scratch'e acilir.
        string ParseString()
        {
            int start = pos;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '"') { string r = s.Substring(start, pos - start); pos++; return r; }
                if (c == '\\') break;
                if (c < 0x20) throw Err("control character in string");
                pos++;
            }
            if (pos >= s.Length) throw Err("unterminated string");

            // kacisli yol
            if (scratch == null) scratch = new byte[256];
            int n = 0;
            for (int i = start; i < pos; i++) n = Append(ref scratch, n, s[i]);
            while (true)
            {
                if (pos >= s.Length) throw Err("unterminated string");
                char c = s[pos++];
                if (c == '"') break;
                if (c < 0x20) throw Err("control character in string");
                if (c != '\\') { n = Append(ref scratch, n, c); continue; }
                if (pos >= s.Length) throw Err("unterminated escape");
                c = s[pos++];
                switch (c)
                {
                    case '"': n = Append(ref scratch, n, '"'); break;
                    case '\\': n = Append(ref scratch, n, '\\'); break;
                    case '/': n = Append(ref scratch, n, '/'); break;
                    case 'b': n = Append(ref scratch, n, '\b'); break;
                    case 'f': n = Append(ref scratch, n, '\f'); break;
                    case 'n': n = Append(ref scratch, n, '\n'); break;
                    case 'r': n = Append(ref scratch, n, '\r'); break;
                    case 't': n = Append(ref scratch, n, '\t'); break;
                    case 'u':
                    {
                        int cp = Hex4();
                        if (cp >= 0xD800 && cp <= 0xDBFF && pos + 1 < s.Length && s[pos] == '\\' && s[pos + 1] == 'u')
                        {
                            int save = pos;
                            pos += 2;
                            int lo = Hex4();
                            if (lo >= 0xDC00 && lo <= 0xDFFF) cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00);
                            else pos = save; // eslesmeyen cift: yuksek surrogate tek basina yazilir
                        }
                        n = AppendCp(ref scratch, n, cp);
                        break;
                    }
                    default: throw Err("invalid escape '\\" + c + "'");
                }
            }
            return Encoding.UTF8.GetString(scratch, 0, n);
        }

        int Hex4()
        {
            if (pos + 4 > s.Length) throw Err("short \\u escape");
            int v = 0;
            for (int i = 0; i < 4; i++)
            {
                char c = s[pos++];
                int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (d < 0) throw Err("invalid hex digit in \\u escape");
                v = (v << 4) | d;
            }
            return v;
        }

        // UTF-16 char'i scratch'e UTF-8 olarak ekler (surrogate ciftini kaynaktan birlestirir).
        int Append(ref byte[] buf, int n, char c)
        {
            int cp = c;
            if (c >= 0xD800 && c <= 0xDBFF && pos < s.Length && s[pos] >= 0xDC00 && s[pos] <= 0xDFFF)
            {
                cp = 0x10000 + ((c - 0xD800) << 10) + (s[pos] - 0xDC00);
                pos++;
            }
            return AppendCp(ref buf, n, cp);
        }

        static int AppendCp(ref byte[] buf, int n, int cp)
        {
            if (n + 4 > buf.Length) System.Array.Resize(ref buf, buf.Length * 2);
            return PutUtf8(buf, n, cp);
        }

        JsonNode ParseNumber()
        {
            int start = pos;
            bool neg = false;
            if (s[pos] == '-') { neg = true; pos++; }
            int digitsStart = pos;
            long acc = 0;
            bool overflow = false;
            while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
            {
                int d = s[pos] - '0';
                // negatif biriktir: long.MinValue tasmadan okunur
                if (!overflow)
                {
                    if (acc < (long.MinValue + d) / 10) overflow = true;
                    else acc = acc * 10 - d;
                }
                pos++;
            }
            if (pos == digitsStart) throw Err("invalid number");
            if (s[digitsStart] == '0' && pos - digitsStart > 1) throw Err("invalid number (leading zero)");
            bool isFloat = false;
            if (pos < s.Length && s[pos] == '.')
            {
                isFloat = true; pos++;
                int fs = pos;
                while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
                if (pos == fs) throw Err("invalid number (fraction)");
            }
            if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
            {
                isFloat = true; pos++;
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                int es = pos;
                while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
                if (pos == es) throw Err("invalid number (exponent)");
            }
            if (!isFloat && !overflow)
            {
                if (neg) return JsonNode.Number(acc);
                if (acc == long.MinValue) overflow = true; // +9223372036854775808
                else return JsonNode.Number(-acc);
            }
            if (!double.TryParse(s.Substring(start, pos - start), out var dv)) throw Err("invalid number");
            return JsonNode.Number(dv);
        }
    }

    // Kod noktasini UTF-8 olarak yazar; yeni uzunlugu doner. Cagiran 4 bayt yer garanti eder.
    internal static int PutUtf8(byte[] buf, int n, int cp)
    {
        if (cp < 0x80) { buf[n++] = (byte)cp; }
        else if (cp < 0x800) { buf[n++] = (byte)(0xC0 | (cp >> 6)); buf[n++] = (byte)(0x80 | (cp & 0x3F)); }
        else if (cp < 0x10000) { buf[n++] = (byte)(0xE0 | (cp >> 12)); buf[n++] = (byte)(0x80 | ((cp >> 6) & 0x3F)); buf[n++] = (byte)(0x80 | (cp & 0x3F)); }
        else { buf[n++] = (byte)(0xF0 | (cp >> 18)); buf[n++] = (byte)(0x80 | ((cp >> 12) & 0x3F)); buf[n++] = (byte)(0x80 | ((cp >> 6) & 0x3F)); buf[n++] = (byte)(0x80 | (cp & 0x3F)); }
        return n;
    }

    // ---------------- node yazimi ----------------

    public static string Write(JsonNode node)
    {
        var w = new JsonWriter();
        Write(node, w);
        return w.ToString();
    }

    public static void Write(JsonNode node, JsonWriter w)
    {
        if (node == null) { w.Null(); return; }
        switch (node.Type)
        {
            case JsonNode.Kind.Null: w.Null(); break;
            case JsonNode.Kind.Bool: w.Value(node.AsBool()); break;
            case JsonNode.Kind.Number:
                if (node.IsInteger) w.Value(node.AsLong()); else w.Value(node.AsDouble());
                break;
            case JsonNode.Kind.String: w.Value(node.AsString()); break;
            case JsonNode.Kind.Array:
                w.StartArray();
                for (int i = 0; i < node.Items.Count; i++) Write(node.Items[i], w);
                w.EndArray();
                break;
            case JsonNode.Kind.Object:
                w.StartObject();
                for (int i = 0; i < node.Fields.Count; i++) { w.Name(node.Fields[i].Key); Write(node.Fields[i].Value, w); }
                w.EndObject();
                break;
        }
    }

    // ---------------- binder: tip haritasi ----------------

    internal enum SlotKind : byte { Int, Long, Float, Double, Bool, String, Byte, Short, Enum, Nullable, Array, List, Dict, Vec2, Vec3, Vec4, Color, Object, Node }

    internal sealed class Slot
    {
        public SlotKind Kind;
        public Type Type;
        public Slot Elem;      // Nullable ic tipi / dizi-liste elemani / sozluk degeri
        public TypeMap Map;    // Object
    }

    internal sealed class TypeMap
    {
        public Type Type;
        public FieldEntry[] Fields;

        public sealed class FieldEntry
        {
            public FieldInfo Info;
            public string Name;   // JSON adi ([JsonField] ya da alan adi)
            public Slot Slot;
        }

        public FieldEntry Find(string name)
        {
            for (int i = 0; i < Fields.Length; i++)
                if (Fields[i].Name == name) return Fields[i];
            return null;
        }
    }

    // Harita cache'i katalogda yasar (statik tip cache yasagi: ALC reload). Katalog yoksa gecici sozluk.
    static Dictionary<Type, TypeMap> Maps(TypeCatalog catalog)
        => (catalog ?? Scene.Active.Catalog)?.JsonMaps ?? new Dictionary<Type, TypeMap>();

    static TypeMap GetMap(Type t, Dictionary<Type, TypeMap> maps)
    {
        if (maps.TryGetValue(t, out var m)) return m;
        m = new TypeMap { Type = t };
        maps[t] = m; // once kaydet: ozyineli tip (A icinde A) donguye girmesin
        var list = new List<TypeMap.FieldEntry>();
        var chain = new List<Type>();
        for (var tt = t; tt != null && tt != typeof(object) && tt != typeof(ValueType) && tt != typeof(Component); tt = tt.BaseType)
            chain.Add(tt);
        chain.Reverse();
        foreach (var tt in chain)
            foreach (var fi in Reflect.DeclaredInstanceFields(tt))
            {
                if (!Reflect.IsSerialized(fi)) continue;
                var attr = fi.GetCustomAttribute<JsonFieldAttribute>(false);
                list.Add(new TypeMap.FieldEntry { Info = fi, Name = attr?.Name ?? fi.Name, Slot = SlotFor(fi.FieldType, maps) });
            }
        m.Fields = list.ToArray();
        return m;
    }

    static Slot SlotFor(Type t, Dictionary<Type, TypeMap> maps)
    {
        var s = new Slot { Type = t };
        if (t == typeof(int)) s.Kind = SlotKind.Int;
        else if (t == typeof(long)) s.Kind = SlotKind.Long;
        else if (t == typeof(float)) s.Kind = SlotKind.Float;
        else if (t == typeof(double)) s.Kind = SlotKind.Double;
        else if (t == typeof(bool)) s.Kind = SlotKind.Bool;
        else if (t == typeof(string)) s.Kind = SlotKind.String;
        else if (t == typeof(byte)) s.Kind = SlotKind.Byte;
        else if (t == typeof(short)) s.Kind = SlotKind.Short;
        else if (t == typeof(JsonNode)) s.Kind = SlotKind.Node;
        else if (t.IsEnum) s.Kind = SlotKind.Enum;
        else if (t == typeof(Vec2)) s.Kind = SlotKind.Vec2;
        else if (t == typeof(Vec3)) s.Kind = SlotKind.Vec3;
        else if (t == typeof(Vec4)) s.Kind = SlotKind.Vec4;
        else if (t == typeof(Color)) s.Kind = SlotKind.Color;
        else if (Nullable.GetUnderlyingType(t) is { } inner) { s.Kind = SlotKind.Nullable; s.Elem = SlotFor(inner, maps); }
        else if (t.IsArray)
        {
            if (t.GetArrayRank() != 1) throw new JsonException("multi-dimensional array not supported: " + t.Name);
            s.Kind = SlotKind.Array; s.Elem = SlotFor(t.GetElementType(), maps);
        }
        else if (Reflect.IsList(t, out var le)) { s.Kind = SlotKind.List; s.Elem = SlotFor(le, maps); }
        else if (t.IsGenericType && !t.IsGenericTypeDefinition && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var args = t.GetGenericArguments();
            if (args[0] != typeof(string)) throw new JsonException("dictionary key must be string: " + t.Name);
            s.Kind = SlotKind.Dict; s.Elem = SlotFor(args[1], maps);
        }
        else if (t == typeof(object) || t.IsAbstract || t.IsInterface || t.IsPrimitive
                 || typeof(Component).IsAssignableFrom(t) || t == typeof(GameObject)
                 || typeof(IAsset).IsAssignableFrom(t) || Reflect.IsAssetClass(t))
            throw new JsonException("type not supported in JSON: " + t.Name);
        else { s.Kind = SlotKind.Object; s.Map = GetMap(t, maps); }
        return s;
    }

    // ---------------- binder: okuma ----------------

    public static T FromJson<T>(string json, TypeCatalog catalog = null) => (T)FromJson(json, typeof(T), catalog);

    public static object FromJson(string json, Type type, TypeCatalog catalog = null)
    {
        if (string.IsNullOrEmpty(json)) return null;
        return FromNode(Parse(json), type, catalog);
    }

    public static T FromNode<T>(JsonNode node, TypeCatalog catalog = null) => (T)FromNode(node, typeof(T), catalog);

    public static object FromNode(JsonNode node, Type type, TypeCatalog catalog = null)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        var maps = Maps(catalog);
        var v = ReadValue(node, SlotFor(type, maps), null, "$", null, -1);
        if (v == null && type.IsValueType) return Activator.CreateInstance(type);
        return v;
    }

    // Mevcut nesnenin alanlarina yazar (JsonUtility.FromJsonOverwrite): JSON'da olmayan alanlar dokunulmaz.
    public static void FromJsonOverwrite(string json, object target, TypeCatalog catalog = null)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrEmpty(json)) return;
        var node = Parse(json);
        if (!node.IsObject) throw new JsonException("root must be an object for overwrite");
        var maps = Maps(catalog);
        var t = target.GetType();
        if (t.IsValueType) throw new JsonException("overwrite target must be a class");
        ReadInto(target, GetMap(t, maps), node, "$");
    }

    // Hata yolu yalniz hata aninda kurulur (alan basina string alloc yok): owner.key | owner[index].
    static string PathOf(string owner, string key, int index)
        => index >= 0 ? owner + "[" + index + "]" : key == null ? owner : owner + "." + key;

    static JsonException Mismatch(string owner, string key, int index, string expected, JsonNode n)
        => new JsonException(PathOf(owner, key, index) + ": expected " + expected + ", got " + n.Type);

    // null donus: deger-tipi alanlarda "dokunma", referanslarda null.
    static object ReadValue(JsonNode n, Slot s, object existing, string owner, string key, int index)
    {
        if (n == null) return existing;
        if (n.IsNull) return s.Kind == SlotKind.Nullable || !s.Type.IsValueType ? null : existing;
        switch (s.Kind)
        {
            case SlotKind.Int: if (!n.IsNumber) throw Mismatch(owner, key, index, "number", n); return (int)n.AsLong();
            case SlotKind.Long: if (!n.IsNumber) throw Mismatch(owner, key, index, "number", n); return n.AsLong();
            case SlotKind.Float: if (!n.IsNumber) throw Mismatch(owner, key, index, "number", n); return (float)n.AsDouble();
            case SlotKind.Double: if (!n.IsNumber) throw Mismatch(owner, key, index, "number", n); return n.AsDouble();
            case SlotKind.Byte: if (!n.IsNumber) throw Mismatch(owner, key, index, "number", n); return (byte)n.AsLong();
            case SlotKind.Short: if (!n.IsNumber) throw Mismatch(owner, key, index, "number", n); return (short)n.AsLong();
            case SlotKind.Bool: if (!n.IsBool) throw Mismatch(owner, key, index, "bool", n); return n.AsBool();
            case SlotKind.String: if (!n.IsString) throw Mismatch(owner, key, index, "string", n); return n.AsString();
            case SlotKind.Node: return n;
            case SlotKind.Enum:
                if (n.IsNumber) return Enum.ToObject(s.Type, n.AsLong());
                if (n.IsString)
                {
                    if (Enum.TryParse(s.Type, n.AsString(), out var ev)) return ev;
                    throw new JsonException(PathOf(owner, key, index) + ": '" + n.AsString() + "' is not a member of " + s.Type.Name);
                }
                throw Mismatch(owner, key, index, "enum name or number", n);
            case SlotKind.Nullable:
                return ReadValue(n, s.Elem, null, owner, key, index); // T kutusu == T? kutusu
            case SlotKind.Array:
            {
                if (!n.IsArray) throw Mismatch(owner, key, index, "array", n);
                var et = s.Elem.Type;
                var arr = Reflect.NewArray(et, n.Items.Count);
                string path = PathOf(owner, key, index);
                for (int i = 0; i < n.Items.Count; i++)
                {
                    var v = ReadValue(n.Items[i], s.Elem, null, path, null, i);
                    if (v != null || !et.IsValueType) arr.SetValue(v, i);
                }
                return arr;
            }
            case SlotKind.List:
            {
                if (!n.IsArray) throw Mismatch(owner, key, index, "array", n);
                var et = s.Elem.Type;
                var list = (IList)Activator.CreateInstance(s.Type, true);
                string path = PathOf(owner, key, index);
                for (int i = 0; i < n.Items.Count; i++)
                {
                    var v = ReadValue(n.Items[i], s.Elem, null, path, null, i);
                    if (v == null && et.IsValueType) v = Activator.CreateInstance(et);
                    list.Add(v);
                }
                return list;
            }
            case SlotKind.Dict:
            {
                if (!n.IsObject) throw Mismatch(owner, key, index, "object", n);
                var vt = s.Elem.Type;
                var dict = (IDictionary)Activator.CreateInstance(s.Type, true);
                string path = PathOf(owner, key, index);
                for (int i = 0; i < n.Fields.Count; i++)
                {
                    var kv = n.Fields[i];
                    var v = ReadValue(kv.Value, s.Elem, null, path, kv.Key, -1);
                    if (v == null && vt.IsValueType) v = Activator.CreateInstance(vt);
                    dict[kv.Key] = v;
                }
                return dict;
            }
            case SlotKind.Vec2: { var a = Floats(n, 2, owner, key, index); return new Vec2(a[0], a[1]); }
            case SlotKind.Vec3: { var a = Floats(n, 3, owner, key, index); return new Vec3(a[0], a[1], a[2]); }
            case SlotKind.Vec4: { var a = Floats(n, 4, owner, key, index); return new Vec4(a[0], a[1], a[2], a[3]); }
            case SlotKind.Color:
            {
                if (!n.IsArray || n.Items.Count < 3) throw Mismatch(owner, key, index, "[r,g,b(,a)] array", n);
                return new Color((byte)n.Items[0].AsLong(), (byte)n.Items[1].AsLong(), (byte)n.Items[2].AsLong(),
                    n.Items.Count > 3 ? (byte)n.Items[3].AsLong() : (byte)255);
            }
            case SlotKind.Object:
            {
                if (!n.IsObject) throw Mismatch(owner, key, index, "object", n);
                var inst = existing ?? Activator.CreateInstance(s.Type, true);
                ReadInto(inst, s.Map, n, PathOf(owner, key, index));
                return inst;
            }
        }
        return existing;
    }

    static readonly float[] _floats = new float[4];

    static float[] Floats(JsonNode n, int count, string owner, string key, int index)
    {
        if (!n.IsArray || n.Items.Count < count) throw Mismatch(owner, key, index, "array of " + count + " numbers", n);
        for (int i = 0; i < count; i++) _floats[i] = (float)n.Items[i].AsDouble();
        return _floats;
    }

    static void ReadInto(object inst, TypeMap map, JsonNode obj, string path)
    {
        for (int i = 0; i < obj.Fields.Count; i++)
        {
            var kv = obj.Fields[i];
            var f = map.Find(kv.Key);
            if (f == null) continue; // bilinmeyen alan
            // Ic ice nesne: mevcut ornek varsa icine yazilir (varsayilanlar korunur); struct kutusu geri yazilir.
            object existing = f.Slot.Kind == SlotKind.Object ? f.Info.GetValue(inst) : null;
            var v = ReadValue(kv.Value, f.Slot, existing, path, kv.Key, -1);
            if (v == null && f.Slot.Type.IsValueType && f.Slot.Kind != SlotKind.Nullable) continue;
            f.Info.SetValue(inst, v);
        }
    }

    // ---------------- binder: yazma ----------------

    public static string ToJson(object obj, TypeCatalog catalog = null)
    {
        var w = new JsonWriter();
        Write(obj, w, catalog);
        return w.ToString();
    }

    public static byte[] ToJsonBytes(object obj, TypeCatalog catalog = null)
    {
        var w = new JsonWriter();
        Write(obj, w, catalog);
        return w.ToArray();
    }

    public static void Write(object obj, JsonWriter w, TypeCatalog catalog = null)
    {
        if (obj == null) { w.Null(); return; }
        WriteValue(obj, SlotFor(obj.GetType(), Maps(catalog)), w);
    }

    public static JsonNode ToNode(object obj, TypeCatalog catalog = null) => Parse(ToJson(obj, catalog));

    static void WriteValue(object v, Slot s, JsonWriter w)
    {
        if (v == null) { w.Null(); return; }
        switch (s.Kind)
        {
            case SlotKind.Int: w.Value((int)v); break;
            case SlotKind.Long: w.Value((long)v); break;
            case SlotKind.Float: w.Value((float)v); break;
            case SlotKind.Double: w.Value((double)v); break;
            case SlotKind.Byte: w.Value((long)(byte)v); break;
            case SlotKind.Short: w.Value((long)(short)v); break;
            case SlotKind.Bool: w.Value((bool)v); break;
            case SlotKind.String: w.Value((string)v); break;
            case SlotKind.Enum: w.Value(v.ToString()); break;
            case SlotKind.Node: Write((JsonNode)v, w); break;
            case SlotKind.Nullable: WriteValue(v, s.Elem, w); break; // kutu T kutusudur
            case SlotKind.Array:
            case SlotKind.List:
            {
                var list = (IList)v;
                w.StartArray();
                for (int i = 0; i < list.Count; i++) WriteValue(list[i], s.Elem, w);
                w.EndArray();
                break;
            }
            case SlotKind.Dict:
            {
                var dict = (IDictionary)v;
                w.StartObject();
                var e = dict.GetEnumerator();
                while (e.MoveNext())
                {
                    w.Name((string)e.Key);
                    WriteValue(e.Value, s.Elem, w);
                }
                w.EndObject();
                break;
            }
            case SlotKind.Vec2: { var a = (Vec2)v; w.StartArray(); w.Value(a.x); w.Value(a.y); w.EndArray(); break; }
            case SlotKind.Vec3: { var a = (Vec3)v; w.StartArray(); w.Value(a.x); w.Value(a.y); w.Value(a.z); w.EndArray(); break; }
            case SlotKind.Vec4: { var a = (Vec4)v; w.StartArray(); w.Value(a.x); w.Value(a.y); w.Value(a.z); w.Value(a.w); w.EndArray(); break; }
            case SlotKind.Color: { var c = (Color)v; w.StartArray(); w.Value((long)c.r); w.Value((long)c.g); w.Value((long)c.b); w.Value((long)c.a); w.EndArray(); break; }
            case SlotKind.Object:
            {
                w.StartObject();
                var fields = s.Map.Fields;
                for (int i = 0; i < fields.Length; i++)
                {
                    w.Name(fields[i].Name);
                    WriteValue(fields[i].Info.GetValue(v), fields[i].Slot, w);
                }
                w.EndObject();
                break;
            }
        }
    }
}
