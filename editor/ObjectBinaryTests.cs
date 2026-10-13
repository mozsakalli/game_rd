#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// [Asset] veri nesnesi smoke testleri: YAML (ObjectSerializer) <-> ObjectBinary (DOBJ) round-trip,
// ic ice nesne/liste/Blob, Asset->Asset referansi (bellek-ici kaynaktan AssetDatabase.LoadObject),
// tek instance paylasimi, yerinde reload, alan rename toleransi, PakRoots icin tip adi.
public static class ObjectBinaryTests
{
    static int _pass, _fail;

    [Serializable]
    public struct Goal
    {
        public int Kind;
        public float Amount;
    }

    // Polimorfik referans sozlesmesi (ISpriteSource modeli): arayuz alani = asset referansi, somut tip yoldan.
    public interface IObjTestTag { }

    [Serializable, Asset]
    public sealed class ObjTestEnemy : IObjTestTag
    {
        public string Name = "grunt";
        public int Hp = 10;
    }

    [Serializable, Asset]
    public sealed class ObjTestLevel
    {
        public int Width = 8;
        public float Gravity = -9.8f;
        public bool Hard;
        public Color Tint = Color.White;
        public List<Goal> Goals = new();
        public int[] Cells;
        public byte[] Packed;
        public ObjTestEnemy Boss;
        public List<IObjTestTag> Tagged = new();
        public SortMode Mode = SortMode.Ui;
    }

    [AddComponentMenu("")]
    public sealed class ObjTestHolder : Component
    {
        public ObjTestLevel Level;
        public ObjTestEnemy Inline; // [Asset] tip alanda REFERANS olmali, gomulu degil
    }

    // Bellek-ici kaynak: anahtar -> bayt (pak gibi; bagimliliksiz).
    sealed class MemorySource : AssetSource
    {
        public readonly Dictionary<string, byte[]> Data = new();
        public override int StartLoad(string key) => -1;
        public override Task<byte[]> ReadBytesAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var b) ? b : null);
        public override byte[] ReadBytes(string key) => Data.TryGetValue(key, out var b) ? b : null;
        public override bool Exists(string key) => Data.ContainsKey(key);
        public override bool TryGetRange(string key, out string path, out long offset, out long length)
        { path = null; offset = length = 0; return false; }
        public override IEnumerable<string> Keys => Data.Keys;
    }

    static void Check(bool ok, string name)
    {
        if (ok) _pass++;
        else { _fail++; Console.WriteLine("[objtest] FAIL: " + name); }
    }

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prevWarn = AssetDatabase.LogWarning;
        var warnings = new List<string>();
        AssetDatabase.LogWarning = warnings.Add;
        try { RunCore(catalog); }
        finally { AssetDatabase.LogWarning = prevWarn; }
        Console.WriteLine($"[objtest] {_pass} PASS, {_fail} FAIL");
    }

    static void RunCore(TypeCatalog catalog)
    {
        // --- Sema: [Asset] tipler katalogda, alanlarda Kind.Asset ---
        var levelEntry = catalog.Find(nameof(ObjTestLevel));
        Check(levelEntry is { IsAsset: true } && levelEntry.CreateObject != null, "katalog [Asset] kaydi");
        var holder = catalog.Find(nameof(ObjTestHolder));
        Check(holder != null && SerializedType.Find(holder.Schema, "Level")?.Kind == SerializedType.Kind.Asset, "component alani Kind.Asset");
        Check(SerializedType.Find(holder.Schema, "Inline")?.Kind == SerializedType.Kind.Asset, "[Asset] tip gomulmez");
        Check(SerializedType.Find(levelEntry.Schema, "Goals")?.ElementKind == SerializedType.Kind.Object, "Goal struct gomulu liste");
        Check(SerializedType.Find(levelEntry.Schema, "Packed")?.Kind == SerializedType.Kind.Blob, "byte[] Blob");
        Check(SerializedType.Find(levelEntry.Schema, "Cells")?.Kind == SerializedType.Kind.List, "int[] List kalir");
        Check(SerializedType.Find(levelEntry.Schema, "Tagged")?.ElementKind == SerializedType.Kind.Asset, "arayuz listesi Kind.Asset (polimorfik)");

        // --- Kaynak: enemy (YAML) + level (YAML, enemy'ye referans) ---
        var src = new MemorySource();
        var db = new AssetDatabase(src) { Catalog = catalog };
        var enemy = new ObjTestEnemy { Name = "boss", Hp = 250 };
        src.Data["Defs/boss.asset"] = Encoding.UTF8.GetBytes(SaveYaml(enemy, db));
        var level = new ObjTestLevel
        {
            Width = 12,
            Gravity = 1.5f,
            Hard = true,
            Tint = new Color(10, 20, 30, 40),
            Cells = new[] { 1, 2, 3 },
            Packed = new byte[] { 9, 8, 7, 6, 5 },
            Mode = SortMode.Transparent,
        };
        level.Goals.Add(new Goal { Kind = 2, Amount = 3.5f });
        level.Goals.Add(new Goal { Kind = 5, Amount = 0.25f });
        // Referans anahtara yazilsin diye boss onceden yuklenip anahtarla eslesir.
        level.Boss = (ObjTestEnemy)db.LoadObject("Defs/boss.asset", typeof(ObjTestEnemy));
        Check(level.Boss != null && level.Boss.Hp == 250, "YAML .asset LoadObject");
        level.Tagged.Add(level.Boss);
        string levelYaml = SaveYaml(level, db);
        Check(levelYaml.Contains("Boss: Defs/boss.asset"), "YAML'da referans anahtar");
        Check(levelYaml.Contains("Tagged:") && levelYaml.Contains("- Defs/boss.asset"), "YAML'da arayuz referansi anahtar");
        src.Data["Defs/L1.asset"] = Encoding.UTF8.GetBytes(levelYaml);

        // --- YAML -> nesne (editor yolu) ---
        var l1 = (ObjTestLevel)db.LoadObject("Defs/L1.asset", typeof(ObjTestLevel));
        Check(l1 != null && l1.Width == 12 && l1.Gravity == 1.5f && l1.Hard && l1.Tint.a == 40, "YAML skalerler");
        Check(l1 != null && l1.Goals.Count == 2 && l1.Goals[1].Kind == 5 && l1.Goals[1].Amount == 0.25f, "YAML gomulu struct listesi");
        Check(l1 != null && l1.Cells != null && l1.Cells.Length == 3 && l1.Cells[2] == 3, "YAML int[]");
        Check(l1 != null && l1.Packed != null && l1.Packed.Length == 5 && l1.Packed[0] == 9 && l1.Packed[4] == 5, "YAML Blob base64");
        Check(l1 != null && l1.Mode == SortMode.Transparent, "YAML enum");
        Check(l1 != null && ReferenceEquals(l1.Boss, level.Boss), "Asset->Asset referansi TEK instance");
        Check(l1 != null && l1.Tagged.Count == 1 && ReferenceEquals(l1.Tagged[0], level.Boss), "YAML arayuz referansi somut tipe cozuldu");
        Check(db.LoadAsset("Defs/L1.asset", typeof(IObjTestTag)) == null, "arayuzu tasimayan asset -> null");
        Check(ReferenceEquals(db.LoadObject("Defs/L1.asset"), l1), "ikinci LoadObject ayni instance");
        Check(AssetDatabase.KeyOf(l1, db) == "Defs/L1.asset", "KeyOf nesne -> anahtar");

        // --- YAML -> DOBJ (pak bake) -> nesne (release yolu) ---
        var refs = new List<string>();
        var root = Yaml.Parse(levelYaml);
        byte[] baked = ObjectBinary.Bake(nameof(ObjTestLevel), root.Get("fields"), catalog, null, refs);
        Check(ObjectBinary.IsBaked(baked) && ObjectBinary.TypeNameOf(baked) == nameof(ObjTestLevel), "DOBJ magic + tip adi");
        Check(refs.Count == 1 && refs[0] == "Defs/boss.asset", "bake asset referanslari (ayni anahtar tek kenar)");
        var src2 = new MemorySource();
        var db2 = new AssetDatabase(src2) { Catalog = catalog };
        src2.Data["Defs/boss.asset"] = ObjectBinary.Bake(enemy, catalog, db);
        src2.Data["Defs/L1.asset"] = baked;
        var l2 = (ObjTestLevel)db2.LoadObject("Defs/L1.asset", typeof(ObjTestLevel));
        Check(l2 != null && l2.Width == 12 && l2.Gravity == 1.5f && l2.Hard && l2.Tint.r == 10 && l2.Tint.a == 40, "DOBJ skalerler");
        Check(l2 != null && l2.Goals.Count == 2 && l2.Goals[0].Kind == 2 && l2.Goals[1].Amount == 0.25f, "DOBJ gomulu struct listesi");
        Check(l2 != null && l2.Cells is { Length: 3 } && l2.Cells[1] == 2, "DOBJ int[]");
        Check(l2 != null && l2.Packed is { Length: 5 } && l2.Packed[2] == 7, "DOBJ Blob");
        Check(l2 != null && l2.Mode == SortMode.Transparent, "DOBJ enum");
        Check(l2 != null && l2.Boss != null && l2.Boss.Name == "boss" && l2.Boss.Hp == 250, "DOBJ Asset->Asset cozumu");
        Check(l2 != null && ReferenceEquals(l2.Boss, db2.LoadObject("Defs/boss.asset")), "DOBJ referans tek instance");
        Check(l2 != null && l2.Tagged.Count == 1 && ReferenceEquals(l2.Tagged[0], l2.Boss), "DOBJ arayuz referansi somut tipe cozuldu");

        // --- Tip uyusmazligi: beklenen tip farkli -> null ---
        Check(db2.LoadObject("Defs/L1.asset", typeof(ObjTestEnemy)) == null, "tip uyusmazligi null");

        // --- Yerinde reload: ayni instance, yeni degerler ---
        var changed = new ObjTestLevel { Width = 99 };
        src.Data["Defs/L1.asset"] = Encoding.UTF8.GetBytes(SaveYaml(changed, db));
        Check(db.ReloadObject("Defs/L1.asset") && l1.Width == 99 && ReferenceEquals(db.LoadObject("Defs/L1.asset"), l1), "ReloadObject yerinde");

        // --- Alan toleransi: pak'ta kodda olmayan alan -> atlanir, kalanlar okunur ---
        var extra = Yaml.Parse("fields:\n  Width: 5\n  Ghost: 123\n  Gravity: 2.5\n");
        byte[] baked2 = ObjectBinary.Bake(nameof(ObjTestLevel), extra.Get("fields"), catalog, _ => { }, null);
        var l3 = (ObjTestLevel)ObjectBinary.Read(baked2, catalog, db2);
        Check(l3 != null && l3.Width == 5 && l3.Gravity == 2.5f, "semada olmayan alan atlanir, kalan okunur");

        // --- Katalog degisince cache duser ---
        db2.Catalog = new TypeCatalog();
        Check(db2.LoadObject("Defs/L1.asset") == null, "katalog reload -> cache temiz");
    }

    static string SaveYaml(object obj, AssetDatabase db)
    {
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(obj.GetType().Name));
        root.Add("fields", ObjectSerializer.ToNode(obj, db));
        return Yaml.Write(root);
    }
}
#endif
