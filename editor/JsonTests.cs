#if DE_EDITOR
using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: Json (parser/writer/binder). Gercek sunucu sozlesmeleri (settings cevabi, ControlTags) ornek alinir.
public static class JsonTests
{
    static int _pass, _fail;

    [Serializable]
    class GameSettings
    {
        public string ios_force_version;
        public string android_force_version;
        public bool maintenance;
        public int policy_version = 3;
        public string privacy_url;
    }

    [Serializable]
    class SettingsResponse
    {
        public GameSettings settings;
        public long version;
        public string[] gateways;
        public List<string> seeds;
        public long serverTime;
    }

    [Serializable]
    class ControlTags
    {
        [JsonField("store_coin")] public string StoreCoins = "store_coin_nonpayer";
        [JsonField("offer")] public string Offers = "offer_test";
        public string Untouched = "keep";
        [NonSerialized] public string Skipped = "ns";
        [SerializeField] int hidden = 7;
        public int Hidden => hidden;
    }

    enum Mode { Off, On, Auto }

    [Serializable]
    struct Pt { public int x, y; }

    [Serializable]
    class Mixed
    {
        public int? optInt;
        public bool? optBool;
        public Mode? optMode;
        public Mode mode = Mode.Auto;
        public double d;
        public float f;
        public byte b;
        public short sh;
        public Pt pt;
        public Dictionary<string, int> counts;
        public List<Pt> pts;
        public Vec2 v2;
        public Color color;
        public JsonNode raw;
        public Mixed child;
    }

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        try
        {
            // --- parser ---
            var n = Json.Parse(" {\"a\": 1, \"b\": [true, false, null, \"x\"], \"c\": {\"d\": -2.5e2}, \"e\": \"\" } ");
            Check(n.IsObject && n.Count == 4 && n.GetInt("a") == 1 && n["b"].IsArray && n["b"].Count == 4, "nesne/dizi yapisi");
            Check(n["b"][0].AsBool() && !n["b"][1].AsBool() && n["b"][2].IsNull && n["b"][3].AsString() == "x", "dizi elemanlari");
            Check(n["c"].GetDouble("d") == -250.0 && !n["c"]["d"].IsInteger, "ustel sayi double");
            Check(n["a"].IsInteger && n.GetString("e") == "" && n["yok"] == null && n.GetString("yok", "def") == "def", "tam sayi / bos string / eksik anahtar");

            var big = Json.Parse("[9223372036854775807, -9223372036854775808, 9223372036854775808, -0, 0.1, 1E3]");
            Check(big[0].AsLong() == long.MaxValue && big[1].AsLong() == long.MinValue, "long sinirlari kayipsiz");
            Check(!big[2].IsInteger && big[2].AsDouble() == 9223372036854775808.0, "long tasmasi double'a duser");
            Check(big[3].IsInteger && big[3].AsLong() == 0 && big[4].AsDouble() == 0.1 && big[5].AsDouble() == 1000, "-0 / 0.1 / 1E3");

            var esc = Json.Parse("\"a\\\"b\\\\c\\n\\t\\u00e7\\u011f \\ud83d\\ude00 düz\"");
            Check(esc.AsString() == "a\"b\\c\n\tçğ 😀 düz", "kacislar + \\u + surrogate cifti + ham unicode");
            Check(Json.Parse("\"plain\"").AsString() == "plain", "kacissiz string");

            Check(Fails("{\"a\":1,}"), "sondaki virgul hata");
            Check(Fails("[1 2]"), "eksik virgul hata");
            Check(Fails("{a:1}"), "tirnaksiz anahtar hata");
            Check(Fails("\"abc"), "bitmemis string hata");
            Check(Fails("tru"), "yarim literal hata");
            Check(Fails("01"), "oncu sifir sonrasi karakter hata");
            Check(Fails("1.") && Fails("-") && Fails("1e"), "bozuk sayi hata");
            Check(Fails("{} x"), "kuyruk karakteri hata");
            Check(Fails("/**/ 1"), "yorum JSON degil");
            string msg = null;
            try { Json.Parse("[1,\n 2, @]"); } catch (JsonException e) { msg = e.Message; }
            Check(msg != null && msg.StartsWith("pos 8:"), "hata konumu: " + msg);

            // --- writer / round-trip ---
            var w = new JsonWriter();
            w.StartObject();
            w.Name("s"); w.Value("tab\tq\"bs\\ \u0001 çğ😀");
            w.Name("i"); w.Value(-42); w.Name("l"); w.Value(long.MinValue);
            w.Name("d"); w.Value(1.5); w.Name("d2"); w.Value(3.0); w.Name("f"); w.Value(0.25f);
            w.Name("nan"); w.Value(double.NaN);
            w.Name("arr"); w.StartArray(); w.Value(true); w.Null(); w.StartObject(); w.EndObject(); w.EndArray();
            w.Name("empty"); w.StartArray(); w.EndArray();
            w.EndObject();
            var text = w.ToString();
            Check(text == "{\"s\":\"tab\\tq\\\"bs\\\\ \\u0001 çğ😀\",\"i\":-42,\"l\":-9223372036854775808,\"d\":1.5,\"d2\":3,\"f\":0.25,\"nan\":null,\"arr\":[true,null,{}],\"empty\":[]}", "writer ciktisi: " + text);
            var back = Json.Parse(text);
            Check(back.GetString("s") == "tab\tq\"bs\\ \u0001 çğ😀" && back.GetLong("l") == long.MinValue && back["arr"][2].IsObject, "writer -> parser round-trip");
            Check(System.Text.Encoding.UTF8.GetString(w.ToArray()) == text, "ToArray UTF-8 == ToString");
            w.Reset(); w.Value(7);
            Check(w.ToString() == "7", "Reset sonrasi yeniden kullanim");
            Check(Json.Write(back) == text, "DocNode yazimi birebir");

            // --- binder: settings cevabi ---
            var resp = Json.FromJson<SettingsResponse>("{\"version\":12,\"settings\":{\"ios_force_version\":\"2.1.0\",\"maintenance\":true,\"policy_version\":5,\"unknown_field\":[1,2]},\"gateways\":[\"a.digitoy.co\",\"b.digitoy.co\"],\"seeds\":[\"s1\"],\"serverTime\":1760630000123}", catalog);
            Check(resp.version == 12 && resp.serverTime == 1760630000123L, "long alanlar");
            Check(resp.settings != null && resp.settings.ios_force_version == "2.1.0" && resp.settings.maintenance && resp.settings.policy_version == 5 && resp.settings.android_force_version == null, "ic ice nesne + bilinmeyen alan atlanir");
            Check(resp.gateways.Length == 2 && resp.gateways[1] == "b.digitoy.co" && resp.seeds.Count == 1 && resp.seeds[0] == "s1", "string[] ve List<string>");
            var cached = Json.FromJson<SettingsResponse>("{\"settings\":null,\"version\":12}", catalog);
            Check(cached.settings == null && cached.gateways == null, "settings:null -> null (JsonUtility'nin aksine)");

            // --- binder: ControlTags ([JsonField], varsayilanlar, NonSerialized, SerializeField) ---
            var tags = Json.FromJson<ControlTags>("{\"store_coin\":\"store_coin_vip\",\"StoreCoins\":\"ignored\",\"Skipped\":\"x\",\"hidden\":9}", catalog);
            Check(tags.StoreCoins == "store_coin_vip" && tags.Offers == "offer_test" && tags.Untouched == "keep", "[JsonField] eslesme + eksik alan varsayilani");
            Check(tags.Skipped == "ns" && tags.Hidden == 9, "[NonSerialized] atlanir, [SerializeField] private okunur");
            var tagsJson = Json.ToJson(tags, catalog);
            Check(tagsJson == "{\"store_coin\":\"store_coin_vip\",\"offer\":\"offer_test\",\"Untouched\":\"keep\",\"hidden\":9}", "ToJson [JsonField] adlari: " + tagsJson);

            // --- binder: kok dizi/liste ---
            var roots = Json.FromJson<int[]>("[1,2,3]", catalog);
            var rootList = Json.FromJson<List<GameSettings>>("[{\"maintenance\":true},{}]", catalog);
            Check(roots.Length == 3 && roots[2] == 3 && rootList.Count == 2 && rootList[0].maintenance && rootList[1].policy_version == 3, "kok dizi + kok liste");
            Check(Json.FromJson<string>("\"s\"", catalog) == "s" && Json.FromJson<int>("5", catalog) == 5 && Json.FromJson<Mode>("\"On\"", catalog) == Mode.On, "kok skaler");

            // --- binder: karma tipler ---
            var m = Json.FromJson<Mixed>("{\"optInt\":5,\"optBool\":null,\"optMode\":\"On\",\"mode\":2,\"d\":2.5,\"f\":1.5,\"b\":200,\"sh\":-3,\"pt\":{\"x\":1,\"y\":2},\"counts\":{\"a\":1,\"b\":2},\"pts\":[{\"x\":3,\"y\":4}],\"v2\":[1.5,2],\"color\":[255,0,128],\"raw\":{\"any\":[1,{\"z\":null}]},\"child\":{\"d\":9}}", catalog);
            Check(m.optInt == 5 && m.optBool == null && m.optMode == Mode.On, "Nullable: deger / null / enum");
            Check(m.mode == Mode.Auto && m.d == 2.5 && m.f == 1.5f && m.b == 200 && m.sh == -3, "enum sayi, double, float, byte, short");
            Check(m.pt.x == 1 && m.pt.y == 2 && m.pts.Count == 1 && m.pts[0].y == 4, "struct ic ice + struct listesi");
            Check(m.counts != null && m.counts.Count == 2 && m.counts["b"] == 2, "Dictionary<string,int>");
            Check(m.v2.x == 1.5f && m.v2.y == 2 && m.color.r == 255 && m.color.b == 128 && m.color.a == 255, "Vec2 + Color (alpha varsayilan 255)");
            Check(m.raw != null && m.raw["any"][1]["z"].IsNull, "JsonNode alani ham alt agac");
            Check(m.child != null && m.child.d == 9 && m.child.child == null, "ozyineli tip");
            var mj = Json.ToJson(m, catalog);
            var m2 = Json.FromJson<Mixed>(mj, catalog);
            Check(m2.optInt == 5 && m2.optBool == null && m2.optMode == Mode.On && m2.mode == Mode.Auto && m2.counts["a"] == 1 && m2.pts[0].x == 3 && m2.color.b == 128 && m2.raw["any"][0].AsInt() == 1 && m2.child.d == 9, "Mixed ToJson -> FromJson round-trip");
            Check(mj.Contains("\"optBool\":null") && mj.Contains("\"mode\":\"Auto\"") && mj.Contains("\"v2\":[1.5,2]"), "yazim: null Nullable, enum adi, Vec2 dizisi: " + mj);

            // --- overwrite ---
            var ow = new GameSettings { privacy_url = "p", policy_version = 1 };
            Json.FromJsonOverwrite("{\"maintenance\":true}", ow, catalog);
            Check(ow.maintenance && ow.privacy_url == "p" && ow.policy_version == 1, "FromJsonOverwrite yalniz gelen alanlar");

            // --- hatalar ---
            Check(FailsBind<GameSettings>("{\"maintenance\":\"yes\"}"), "tip uyusmazligi hata");
            Check(FailsBind<GameSettings>("[1]"), "nesne beklenirken dizi hata");
            Check(FailsBind<Mixed>("{\"mode\":\"Nope\"}"), "tanimsiz enum adi hata");
            Check(FailsBind<Transform>("{}"), "Component JSON'a girmez");
            Check(Json.FromJson<GameSettings>("", catalog) == null && Json.FromJson<GameSettings>(null, catalog) == null, "bos/null giris null");

            // --- alloc: isinmis parse yalniz DOM kadar ---
            const string sample = "{\"version\":12,\"settings\":{\"ios_force_version\":\"2.1.0\",\"maintenance\":true},\"gateways\":[\"a\",\"b\"],\"serverTime\":1760630000123}";
            Json.FromJson<SettingsResponse>(sample, catalog);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Json.FromJson<SettingsResponse>(sample, catalog);
            long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
            Console.WriteLine("[json] parse+bind alloc/cagri: " + perCall + " B");
            Check(perCall < 2500, "parse+bind alloc/cagri " + perCall + " B (< 2500)");
        }
        catch (Exception e)
        {
            _fail++;
            Console.WriteLine("[json] EXCEPTION: " + e);
        }
        Console.WriteLine($"[json] {_pass} PASS, {_fail} FAIL");
    }

    static bool Fails(string json)
    {
        try { Json.Parse(json); return false; }
        catch (JsonException) { return true; }
    }

    static bool FailsBind<T>(string json)
    {
        try { Json.FromJson<T>(json); return false; }
        catch (JsonException) { return true; }
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[json] FAIL: {name}"); }
    }
}
#endif
