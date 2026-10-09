using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DigitoyEngine;

// PISMIS (baked) sahne/prefab formati — RELEASE yolu. Pak build'de YAML doc'u
// (prefab'lar ACILMIS, override'lar uygulanmis) katalog SEMASIYLA cozulur ve
// dogrudan alan degerleri yazilir: skaler metin yok (ham float/int/bayt),
// Go/CompRef onceden (id, sira) olarak cozulmus, enum underlying int.
// Runtime'da DocNode bile kurulmaz: bayt -> alan setter.
//
// v2 (docs/registry-removal.md Faz 2): pak KENDINI TARIF EDER — tip tablosu her tipin alan
// tablosunu (ad + kind, ic ice dahil) tasir; alan kaydindaki u16 idx bu tabloya isaret eder.
// Okuyucu acilista tip basina bir kez pak alani -> kod semasi eslemesi kurar (ad, FormerName).
// Eslesmeyen/kind'i degisen alan ATLANIR (alan duzeyi tolerans: alan ekle/sil/tasi/rename sahneyi
// bozmaz; editor ve player'in alan SIRASI onemsiz). Bilinmeyen component tipi komple atlanir.
//
// Duzen (little-endian):
//   i32 magic "DSCN", i32 version
//   i32 nStr, str[]                     (u16 len + UTF8; asset guid'leri, adlar)
//   i32 nAlias, { i32 oldStr, i32 newStr }[]   (tip adi rename'leri: katalog alias'lari)
//   i32 nType, type[]                   (component tipleri)
//   type:  i32 nameStr, fields
//   fields: u16 nField, { i32 nameStr, u8 kind, u8 elemKind, u8 hasNested, [fields] }[]
//   i32 nObj, obj[]
//   obj: i32 id, i32 parent, i32 nameStr, u8 active, i32 layer, f32 pos[3] rot[3] scale[3]
//        u16 nComp, comp[]
//   comp: u16 type, u8 enabled, i32 byteLen (atlama icin), u16 nField, { u16 idx, value }[]
//   value (Kind'e gore): Float f32 | Int i32 | Bool u8 | String i32 str | Enum i32 |
//        Vec2 2f | Vec3 3f | Vec4 4f | Color 4u8 | Asset i32 str (-1 null) |
//        GoRef i32 id (0 null) | CompRef i32 id, u16 sira | List i32 n (-1 null) + eleman[] |
//        Object u8 var + u16 nField + { u16 idx, value }[]
public static unsafe class SceneBinary
{
    public const int Magic = 0x4E435344; // "DSCN"
    public const int Version = 2;

    public static bool IsBaked(byte[] data)
        => data != null && data.Length >= 8 && ReadI32(data, 0) == Magic;

    // --- Pak alan tablosu (okuyucu tarafi): pak'taki alan -> kod semasindaki alan (null = atla) ---

    sealed class PakField
    {
        public string Name;
        public SerializedType.Kind Kind, ElementKind;
        public PakField[] Nested;                 // hasNested
        public SerializedType.FieldSchema Target; // null = kodda yok / uyumsuz -> deger atlanir
    }

    static PakField[] ReadFieldTable(Reader r)
    {
        int n = r.U16();
        var arr = new PakField[n];
        for (int i = 0; i < n; i++)
        {
            var f = new PakField { Name = r.Strs[r.I32()], Kind = (SerializedType.Kind)r.U8(), ElementKind = (SerializedType.Kind)r.U8() };
            if (r.U8() != 0)
                f.Nested = ReadFieldTable(r);
            arr[i] = f;
        }
        return arr;
    }

    // Ad (ya da FormerName) ile esle; kind/elemKind farkliysa uyumsuz say. Ic ice tablolar ozyinelemeli.
    static void BindFieldTable(PakField[] pak, SerializedType.FieldSchema[] schema, string owner)
    {
        if (schema == null)
            return;
        foreach (var pf in pak)
        {
            var f = SerializedType.Find(schema, pf.Name);
            if (f == null)
            {
                AssetDatabase.LogWarning?.Invoke($"[baked] alan kodda yok, atlandi: {owner}.{pf.Name}");
                continue;
            }
            if (f.Kind != pf.Kind || f.ElementKind != pf.ElementKind)
            {
                AssetDatabase.LogWarning?.Invoke($"[baked] alan turu degisti, atlandi: {owner}.{pf.Name} ({pf.Kind}/{pf.ElementKind} -> {f.Kind}/{f.ElementKind})");
                continue;
            }
            pf.Target = f;
            if (pf.Nested != null)
                BindFieldTable(pf.Nested, f.Nested, owner + "." + pf.Name);
        }
    }

    // ======================= RUNTIME OKUYUCU =======================

    // Pismis baytlardan sahneyi/prefab'i AKTIF sahneye kurar; ilk kok GO'yu dondurur.
    // SceneDoc.Spawn ile ayni sozlesme: once tum GO'lar dogar, referanslar sonra
    // cozulur, aktivasyon en sonda topluca (Awake deserialize edilmis degerleri gorur).
    // Test kancasi (docs/registry-removal.md Faz 0): referanslar cozuldukten sonra, Awake'ten ONCE cagrilir.
    public static Action BeforeActivate;

    public static GameObject Spawn(byte[] data, Transform parent, TypeCatalog catalog, AssetDatabase assets)
    {
        var r = new Reader(data, assets);
        if (r.I32() != Magic)
            throw new InvalidOperationException("baked sahne: magic uyusmuyor");
        int ver = r.I32();
        if (ver != Version)
            throw new InvalidOperationException($"baked sahne: surum {ver} desteklenmiyor (beklenen {Version})");

        int nStr = r.I32();
        var strs = new string[nStr];
        for (int i = 0; i < nStr; i++)
            strs[i] = r.Str();
        r.Strs = strs;

        // Alias'lar tip tablosundan ONCE: pak'ta eski adla yazilmis tip yeni ada cozulsun.
        int nAlias = r.I32();
        for (int i = 0; i < nAlias; i++)
        {
            string oldName = strs[r.I32()], newName = strs[r.I32()];
            if (catalog.Find(oldName) == null)
                catalog.RegisterAlias(oldName, newName);
        }
        int nType = r.I32();
        var types = new TypeCatalog.Entry[nType];
        var tables = new PakField[nType][];
        for (int i = 0; i < nType; i++)
        {
            string name = strs[r.I32()];
            var table = ReadFieldTable(r);
            var e = catalog.Find(name);
            if (e == null)
                AssetDatabase.LogWarning?.Invoke("[baked] bilinmeyen component tipi atlandi: " + name);
            else
                BindFieldTable(table, e.Schema, name);
            types[i] = e;
            tables[i] = table;
        }

        int nObj = r.I32();
        var byId = new Dictionary<int, GameObject>();
        var compsById = new Dictionary<int, Component[]>();
        var parents = new int[nObj];
        var gos = new GameObject[nObj];
        var inactive = new List<GameObject>();
        var deferred = r.Deferred;
        GameObject first = null;

        for (int oi = 0; oi < nObj; oi++)
        {
            int id = r.I32();
            parents[oi] = r.I32();
            var go = new GameObject(strs[r.I32()]);
            bool active = r.U8() != 0;
            go.layer = r.I32();
            go.transform.localPosition = r.V3();
            go.transform.localEulerAngles = r.V3();
            go.transform.localScale = r.V3();
            gos[oi] = go;
            byId[id] = go;
            first ??= go;

            int nComp = r.U16();
            var comps = new Component[nComp];
            compsById[id] = comps;
            for (int ci = 0; ci < nComp; ci++)
            {
                int ti = r.U16();
                var entry = types[ti];
                bool enabled = r.U8() != 0;
                int len = r.I32();
                int end = r.Pos + len;
                if (entry == null)
                {
                    r.Pos = end; // bilinmeyen tip: govdeyi atla
                    continue;
                }
                var c = go.AddComponentRaw(entry);
                c._enabled = enabled;
                comps[ci] = c;
                ReadFields(r, c, tables[ti], assets, deferred);
                r.Pos = end;
            }
            if (!active)
                inactive.Add(go);
        }

        // Hiyerarsi: liste sirasindan bagimsiz (cocuk parent'tan once gelebilir).
        for (int oi = 0; oi < nObj; oi++)
        {
            var go = gos[oi];
            if (parents[oi] != 0 && byId.TryGetValue(parents[oi], out var p))
                go.transform.SetParent(p.transform, false);
            else if (parent != null)
                go.transform.SetParent(parent, false);
        }
        foreach (var go in inactive)
            go.SetActive(false);

        foreach (var d in deferred)
        {
            object v = null;
            if (d.GoId != 0 && byId.TryGetValue(d.GoId, out var target))
            {
                if (d.Ordinal < 0)
                    v = target;
                else if (d.Ordinal == TransformOrdinal)
                    v = target.transform;
                else if (compsById.TryGetValue(d.GoId, out var arr) && d.Ordinal < arr.Length)
                    v = arr[d.Ordinal];
            }
            d.Set(v);
        }
        BeforeActivate?.Invoke(); // test kancasi: saf deserialize durumu (Awake henuz kosmadi)
        for (int oi = 0; oi < nObj; oi++)
            gos[oi].ActivateComponentsNow();
        return first;
    }

    // CompRef sira sentineli: Transform serilesen component listesinde yer almaz
    // (GO ile dogar); referansi go.transform'a cozulur.
    const int TransformOrdinal = 0xFFFF;

    public struct DeferredRef
    {
        public int GoId;
        public int Ordinal; // -1 = GameObject referansi
        public Action<object> Set;
    }

    // Boxed okuma yolu (FieldSchema.Get/Set) — editor, .NET player ve AOT ayni kod. Sahne yukleme tek seferlik;
    // sicak yol degil (animasyon Reflect tipli erisimcilerle). Faz 4+: alan basina boxing istenirse GetRef'e gecilir.
    static void ReadFields(Reader r, object owner, PakField[] table, AssetDatabase assets, List<DeferredRef> deferred)
    {
        int n = r.U16();
        for (int i = 0; i < n; i++)
        {
            var pf = table[r.U16()];
            var f = pf.Target;
            if (f == null)
            {
                SkipValue(r, pf, pf.Kind); // kodda olmayan / turu degisen alan
                continue;
            }
            switch (f.Kind)
            {
                case SerializedType.Kind.GoRef:
                case SerializedType.Kind.CompRef:
                    {
                        var d = r.Ref(f.Kind);
                        var ff = f;
                        var o = owner;
                        d.Set = v => ff.Set(o, v);
                        deferred.Add(d);
                        break;
                    }
                case SerializedType.Kind.List:
                    {
                        int count = r.I32();
                        if (count < 0)
                            break;
                        if (f.NewArray != null)
                        {
                            var arr = (Array)f.NewArray(count);
                            for (int k = 0; k < count; k++)
                                ReadElement(r, pf, assets, deferred, arr, k);
                            f.Set(owner, arr);
                        }
                        else
                        {
                            var list = (System.Collections.IList)f.NewList();
                            for (int k = 0; k < count; k++)
                            {
                                list.Add(f.ElementType.IsValueType ? f.NewElement() : null);
                                ReadElement(r, pf, assets, deferred, list, k);
                            }
                            f.Set(owner, list);
                        }
                        break;
                    }
                case SerializedType.Kind.Object:
                    {
                        if (r.U8() == 0)
                            break;
                        object inst = f.Get(owner) ?? f.NewElement();
                        ReadFields(r, inst, pf.Nested, assets, deferred);
                        f.Set(owner, inst); // struct: boxed kopya geri yazilir
                        break;
                    }
                default:
                    f.Set(owner, r.Scalar(f.Kind, f.FieldType, assets));
                    break;
            }
        }
    }

    static void ReadElement(Reader r, PakField pf, AssetDatabase assets,
        List<DeferredRef> deferred, object container, int index)
    {
        var f = pf.Target;
        switch (f.ElementKind)
        {
            case SerializedType.Kind.GoRef:
            case SerializedType.Kind.CompRef:
                {
                    var d = r.Ref(f.ElementKind);
                    d.Set = v => SetAt(container, index, v);
                    deferred.Add(d);
                    break;
                }
            case SerializedType.Kind.Object:
                {
                    object inst = f.NewElement();
                    if (r.U8() != 0)
                        ReadFields(r, inst, pf.Nested, assets, deferred);
                    SetAt(container, index, inst);
                    break;
                }
            default:
                SetAt(container, index, r.Scalar(f.ElementKind, f.ElementType, assets));
                break;
        }
    }

    static void SetAt(object container, int index, object value)
    {
        if (container is Array arr)
            arr.SetValue(value, index);
        else
            ((System.Collections.IList)container)[index] = value;
    }

    // Degeri okumadan gecer (alan kodda yok). Kodlama kind'den belirlidir; ic ice tablolar pak'tan.
    static void SkipValue(Reader r, PakField pf, SerializedType.Kind kind)
    {
        switch (kind)
        {
            case SerializedType.Kind.Float: case SerializedType.Kind.Int: case SerializedType.Kind.String:
            case SerializedType.Kind.Enum: case SerializedType.Kind.Asset: case SerializedType.Kind.GoRef:
                r.Pos += 4; break;
            case SerializedType.Kind.Bool: r.Pos += 1; break;
            case SerializedType.Kind.Vec2: r.Pos += 8; break;
            case SerializedType.Kind.Vec3: r.Pos += 12; break;
            case SerializedType.Kind.Vec4: r.Pos += 16; break;
            case SerializedType.Kind.Color: r.Pos += 4; break;
            case SerializedType.Kind.CompRef: r.Pos += 6; break;
            case SerializedType.Kind.List:
                {
                    int count = r.I32();
                    for (int k = 0; k < count; k++)
                        SkipValue(r, pf, pf.ElementKind);
                    break;
                }
            case SerializedType.Kind.Object:
                if (r.U8() != 0)
                    SkipFields(r, pf.Nested);
                break;
            default:
                throw new InvalidOperationException("baked sahne: atlanamayan kind " + kind);
        }
    }

    static void SkipFields(Reader r, PakField[] table)
    {
        int n = r.U16();
        for (int i = 0; i < n; i++)
        {
            var pf = table[r.U16()];
            SkipValue(r, pf, pf.Kind);
        }
    }

    static int ReadI32(byte[] b, int p)
        => b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);

    // Bagimliliksiz imlec: BinaryReader/Stream yok (AOT corelib yuzeyi dar kalsin).
    // Bellek duzeni dosya basindaki yorumla birebir. Class: spawn basina tek nesne, ref-param yok.
    public sealed class Reader
    {
        readonly byte[] _b;
        public int Pos;
        internal string[] Strs;
        internal readonly AssetDatabase Assets;
        internal readonly List<DeferredRef> Deferred = new();

        internal Reader(byte[] b, AssetDatabase assets) { _b = b; Pos = 0; Assets = assets; }

        public byte U8() => _b[Pos++];
        public ushort U16() { int v = _b[Pos] | (_b[Pos + 1] << 8); Pos += 2; return (ushort)v; }
        public int I32() { int v = ReadI32(_b, Pos); Pos += 4; return v; }
        public uint U32() => (uint)I32();
        public bool Bool() => U8() != 0;

        // String alani: tablo indeksi (-1 null).
        public string String() { int i = I32(); return i < 0 ? null : Strs[i]; }

        // Asset alani: guid/yol tablodan, yukleme AssetDatabase'den (preload edilmis).
        public object Asset(Type fieldType)
        {
            int i = I32();
            return i < 0 || Assets == null ? null : Assets.LoadAsset(Strs[i], fieldType);
        }

        // Go/CompRef: tum nesneler dogduktan sonra set cagrilir (null olabilir).
        public void GoRef(Action<object> set)
        {
            var d = Ref(SerializedType.Kind.GoRef);
            d.Set = set;
            Deferred.Add(d);
        }

        public void CompRef(Action<object> set)
        {
            var d = Ref(SerializedType.Kind.CompRef);
            d.Set = set;
            Deferred.Add(d);
        }
        public float F32() { int v = I32(); return *(float*)&v; }
        public Vec2 V2() => new Vec2(F32(), F32());
        public Vec3 V3() => new Vec3(F32(), F32(), F32());
        public Vec4 V4() => new Vec4(F32(), F32(), F32(), F32());
        public Color Col() => new Color(U8(), U8(), U8(), U8());

        public string Str()
        {
            int len = U16();
            var s = Encoding.UTF8.GetString(_b, Pos, len);
            Pos += len;
            return s;
        }

        internal DeferredRef Ref(SerializedType.Kind kind)
        {
            var d = new DeferredRef { GoId = I32(), Ordinal = -1 };
            if (kind == SerializedType.Kind.CompRef)
                d.Ordinal = U16();
            return d;
        }

        internal object Scalar(SerializedType.Kind k, Type fieldType, AssetDatabase assets)
        {
            switch (k)
            {
                case SerializedType.Kind.Float: return F32();
                case SerializedType.Kind.Int: return I32();
                case SerializedType.Kind.Bool: return U8() != 0;
                case SerializedType.Kind.String: { int i = I32(); return i < 0 ? null : Strs[i]; }
                case SerializedType.Kind.Enum: return Enum.ToObject(fieldType, (object)I32());
                case SerializedType.Kind.Vec2: return V2();
                case SerializedType.Kind.Vec3: return V3();
                case SerializedType.Kind.Vec4: return V4();
                case SerializedType.Kind.Color: return Col();
                case SerializedType.Kind.Asset:
                    {
                        int i = I32();
                        return i < 0 || assets == null ? null : assets.LoadAsset(Strs[i], fieldType);
                    }
                default:
                    throw new InvalidOperationException("baked sahne: beklenmeyen skaler kind " + k);
            }
        }
    }

#if DE_EDITOR
    // ======================= BUILD-TIME YAZICI (editor) =======================

    // doc ACILMIS olmali (ExpandPrefabs cagrilmis): prefab delta kayitlari burada
    // cozulmez. Bilinmeyen component tipi/alan uyari ile atlanir.
    // assetRefs: sahnenin referansladigi asset anahtarlari (guid/yol, ham) — pak
    // builder bunlari bagimlilik grafigine kenar olarak yazar (cozum orada, burada degil).
    public static byte[] Bake(SceneDoc doc, TypeCatalog catalog, Action<string> warn = null,
        List<string> assetRefs = null)
    {
        var w = new Writer();
        var strs = new Dictionary<string, int>();
        var types = new Dictionary<TypeCatalog.Entry, int>();
        var typeList = new List<TypeCatalog.Entry>();
        assetRefs ??= new List<string>();
        int Str(string s)
        {
            s ??= "";
            if (!strs.TryGetValue(s, out int i))
            {
                i = strs.Count;
                strs[s] = i;
            }
            return i;
        }
        int AssetStr(string s)
        {
            if (!assetRefs.Contains(s))
                assetRefs.Add(s);
            return Str(s);
        }
        int TypeIdx(TypeCatalog.Entry e)
        {
            if (!types.TryGetValue(e, out int i))
            {
                i = typeList.Count;
                types[e] = i;
                typeList.Add(e);
                Str(e.Name);
            }
            return i;
        }

        // Component'ler once cozulur (bilinmeyenler dusur) — CompRef sirasi bu
        // listeye gore hesaplanir, runtime da ayni sirayla olusturur.
        var baked = new Dictionary<int, List<(SceneDoc.CompDoc Doc, TypeCatalog.Entry Entry)>>();
        foreach (var g in doc.Objects)
        {
            var list = new List<(SceneDoc.CompDoc, TypeCatalog.Entry)>();
            foreach (var cd in g.Components)
            {
                var e = catalog.Find(cd.Type);
                if (e == null)
                {
                    warn?.Invoke($"[bake] bilinmeyen component tipi atlandi: {cd.Type} (go {g.Id} '{g.Name}')");
                    continue;
                }
                TypeIdx(e);
                list.Add((cd, e));
            }
            baked[g.Id] = list;
        }

        // Govde once ayri tampona: string/tip tablolari govde yazilirken dolar.
        var body = new Writer();
        body.I32(doc.Objects.Count);
        foreach (var g in doc.Objects)
        {
            body.I32(g.Id);
            body.I32(g.Parent);
            body.I32(Str(g.Name));
            body.U8((byte)(g.Active ? 1 : 0));
            body.I32(g.Layer);
            body.V3(g.Pos);
            body.V3(g.Rot);
            body.V3(g.Scale);
            var comps = baked[g.Id];
            body.U16((ushort)comps.Count);
            foreach (var (cd, e) in comps)
            {
                body.U16((ushort)TypeIdx(e));
                body.U8((byte)(cd.Enabled ? 1 : 0));
                int lenPos = body.Reserve4();
                int start = body.Length;
                WriteFields(body, cd.Props, e.Schema, doc, baked, Str, AssetStr, warn, cd.Type);
                body.Patch4(lenPos, body.Length - start);
            }
        }

        w.I32(Magic);
        w.I32(Version);
        // Alias ve tip tablosu string'leri body'den sonra ama string tablosundan once eklenmeli.
        var aliasList = new List<(int Old, int New)>();
        foreach (var kv in catalog.Aliases)
            aliasList.Add((Str(kv.Key), Str(kv.Value)));
        var typeTables = new Writer();
        typeTables.I32(typeList.Count);
        foreach (var e in typeList)
        {
            typeTables.I32(Str(e.Name));
            WriteFieldTable(typeTables, e.Schema, Str);
        }
        var strArr = new string[strs.Count];
        foreach (var kv in strs)
            strArr[kv.Value] = kv.Key;
        w.I32(strArr.Length);
        foreach (var s in strArr)
            w.Str(s);
        w.I32(aliasList.Count);
        foreach (var (o, n) in aliasList) { w.I32(o); w.I32(n); }
        w.Append(typeTables);
        w.Append(body);
        return w.ToArray();
    }

    // Pak alan tablosu: ad + kind (+ ic ice). Indeksler kod semasiyla ayni sirada -> WriteFields'in
    // Array.IndexOf(schema, f) indeksi dogrudan bu tabloya isaret eder.
    static void WriteFieldTable(Writer w, SerializedType.FieldSchema[] schema, Func<string, int> str)
    {
        int n = schema?.Length ?? 0;
        w.U16((ushort)n);
        for (int i = 0; i < n; i++)
        {
            var f = schema[i];
            w.I32(str(f.Name));
            w.U8((byte)f.Kind);
            w.U8((byte)f.ElementKind);
            w.U8((byte)(f.Nested != null ? 1 : 0));
            if (f.Nested != null)
                WriteFieldTable(w, f.Nested, str);
        }
    }

    static void WriteFields(Writer w, List<KeyValuePair<string, DocNode>> props, SerializedType.FieldSchema[] schema,
        SceneDoc doc, Dictionary<int, List<(SceneDoc.CompDoc Doc, TypeCatalog.Entry Entry)>> baked,
        Func<string, int> str, Func<string, int> assetStr, Action<string> warn, string ctx)
    {
        int countPos = w.Reserve2();
        int n = 0;
        foreach (var kv in props)
        {
            var f = SerializedType.Find(schema, kv.Key);
            if (f == null)
            {
                warn?.Invoke($"[bake] semada olmayan alan atlandi: {ctx}.{kv.Key}");
                continue;
            }
            w.U16((ushort)Array.IndexOf(schema, f));
            WriteValue(w, kv.Value, f, f.Kind, doc, baked, str, assetStr, warn, ctx + "." + f.Name);
            n++;
        }
        w.Patch2(countPos, n);
    }

    static void WriteValue(Writer w, DocNode node, SerializedType.FieldSchema f, SerializedType.Kind kind,
        SceneDoc doc, Dictionary<int, List<(SceneDoc.CompDoc Doc, TypeCatalog.Entry Entry)>> baked,
        Func<string, int> str, Func<string, int> assetStr, Action<string> warn, string ctx)
    {
        string s = node?.Scalar ?? "";
        switch (kind)
        {
            case SerializedType.Kind.Float: w.F32((float)SerializedType.Parse(s, kind, null, null)); break;
            case SerializedType.Kind.Int: w.I32((int)SerializedType.Parse(s, kind, null, null)); break;
            case SerializedType.Kind.Bool: w.U8((byte)(s == "true" ? 1 : 0)); break;
            case SerializedType.Kind.String: w.I32(node?.Scalar == null ? -1 : str(s)); break;
            case SerializedType.Kind.Enum:
                {
                    var ft = kind == f.Kind ? f.FieldType : f.ElementType;
                    object ev = Enum.TryParse(ft, s, out var parsed) ? parsed : Enum.ToObject(ft, 0);
                    w.I32(Convert.ToInt32(ev, CultureInfo.InvariantCulture));
                    break;
                }
            case SerializedType.Kind.Vec2: w.V2(SerializedType.ParseVec2(s)); break;
            case SerializedType.Kind.Vec3: w.V3(SerializedType.ParseVec3(s)); break;
            case SerializedType.Kind.Vec4: w.V4(SerializedType.ParseVec4(s)); break;
            case SerializedType.Kind.Color: w.Col(SerializedType.ParseColor(s)); break;
            case SerializedType.Kind.Asset: w.I32(string.IsNullOrEmpty(s) ? -1 : assetStr(s)); break;
            case SerializedType.Kind.GoRef:
                w.I32(int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int goId) ? goId : 0);
                break;
            case SerializedType.Kind.CompRef:
                {
                    var (id, ord) = ResolveCompRef(s, baked);
                    if (id != 0 && ord < 0)
                    {
                        warn?.Invoke($"[bake] component referansi cozulemedi: {ctx} = '{s}'");
                        id = 0;
                        ord = 0;
                    }
                    w.I32(id);
                    w.U16((ushort)Math.Max(0, ord));
                    break;
                }
            case SerializedType.Kind.List:
                {
                    if (node?.Items == null)
                    {
                        w.I32(-1);
                        break;
                    }
                    w.I32(node.Items.Count);
                    for (int i = 0; i < node.Items.Count; i++)
                        WriteValue(w, node.Items[i], f, f.ElementKind, doc, baked, str, assetStr, warn, ctx + "[" + i + "]");
                    break;
                }
            case SerializedType.Kind.Object:
                {
                    if (node?.Fields == null)
                    {
                        w.U8(0);
                        break;
                    }
                    w.U8(1);
                    WriteFields(w, node.Fields, f.Nested, doc, baked, str, assetStr, warn, ctx);
                    break;
                }
            default:
                throw new InvalidOperationException("bake: beklenmeyen kind " + kind);
        }
    }

    // "goId:TypeName:ayniTipSira" -> (goId, pismis component listesinde sira). Sira
    // yalniz kataloga cozulmus component'ler uzerinden sayilir (runtime ile birebir).
    static (int Id, int Ordinal) ResolveCompRef(string s,
        Dictionary<int, List<(SceneDoc.CompDoc Doc, TypeCatalog.Entry Entry)>> baked)
    {
        if (string.IsNullOrEmpty(s))
            return (0, 0);
        var p = s.Split(':');
        if (p.Length != 3 || !int.TryParse(p[0], out int id) || !int.TryParse(p[2], out int want))
            return (0, 0);
        if (!baked.TryGetValue(id, out var comps))
            return (id, -1);
        if (p[1] == nameof(Transform))
            return (id, TransformOrdinal);
        int idx = 0;
        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i].Entry.Type.Name != p[1])
                continue;
            if (idx == want)
                return (id, i);
            idx++;
        }
        return (id, -1);
    }

    sealed class Writer
    {
        byte[] _b = new byte[4096];
        public int Length;

        void Ensure(int n)
        {
            if (Length + n > _b.Length)
                Array.Resize(ref _b, Math.Max(_b.Length * 2, Length + n));
        }
        public void U8(byte v) { Ensure(1); _b[Length++] = v; }
        public void U16(ushort v) { Ensure(2); _b[Length++] = (byte)v; _b[Length++] = (byte)(v >> 8); }
        public void I32(int v)
        {
            Ensure(4);
            _b[Length++] = (byte)v; _b[Length++] = (byte)(v >> 8);
            _b[Length++] = (byte)(v >> 16); _b[Length++] = (byte)(v >> 24);
        }
        public void U32(uint v) => I32((int)v);
        public void F32(float v) => I32(*(int*)&v);
        public void V2(Vec2 v) { F32(v.x); F32(v.y); }
        public void V3(Vec3 v) { F32(v.x); F32(v.y); F32(v.z); }
        public void V4(Vec4 v) { F32(v.x); F32(v.y); F32(v.z); F32(v.w); }
        public void Col(Color c) { U8(c.r); U8(c.g); U8(c.b); U8(c.a); }
        public void Str(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            if (bytes.Length > ushort.MaxValue)
                throw new InvalidOperationException("bake: string cok uzun (>64K)");
            U16((ushort)bytes.Length);
            Ensure(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _b, Length, bytes.Length);
            Length += bytes.Length;
        }
        public int Reserve2() { int p = Length; U16(0); return p; }
        public int Reserve4() { int p = Length; I32(0); return p; }
        public void Patch2(int p, int v) { _b[p] = (byte)v; _b[p + 1] = (byte)(v >> 8); }
        public void Patch4(int p, int v)
        {
            _b[p] = (byte)v; _b[p + 1] = (byte)(v >> 8); _b[p + 2] = (byte)(v >> 16); _b[p + 3] = (byte)(v >> 24);
        }
        public void Append(Writer o) { Ensure(o.Length); Buffer.BlockCopy(o._b, 0, _b, Length, o.Length); Length += o.Length; }
        public byte[] ToArray()
        {
            var r = new byte[Length];
            Buffer.BlockCopy(_b, 0, r, 0, Length);
            return r;
        }
    }
#endif
}

// Proje ayarlari (release): pak icinde ".project" anahtariyla pismis kayit;
// runtime project.yaml OKUMAZ. Duzen (v2): i32 magic "DPRJ", i32 version, str name,
// str startScene, i32 nScenes, str[] scenes (build'e dahil sahneler; startScene ilk).
public static class ProjectBinary
{
    public const string PakKey = ".project";
    public const int Magic = 0x4A525044; // "DPRJ"
    public const int Version = 2;

    public sealed class Record
    {
        public string Name;
        public string StartScene;
        public string[] Scenes = Array.Empty<string>();
    }

    public static bool TryRead(byte[] data, out string name, out string startScene)
    {
        bool ok = TryRead(data, out var rec);
        name = rec?.Name;
        startScene = rec?.StartScene;
        return ok;
    }

    public static bool TryRead(byte[] data, out Record rec)
    {
        rec = null;
        if (data == null || data.Length < 8)
            return false;
        int pos = 0;
        if (I32(data, ref pos) != Magic)
            return false;
        int version = I32(data, ref pos);
        if (version < 1 || version > Version)
            return false;
        rec = new Record { Name = Str(data, ref pos), StartScene = Str(data, ref pos) };
        if (version >= 2)
        {
            int n = I32(data, ref pos);
            rec.Scenes = new string[n];
            for (int i = 0; i < n; i++)
                rec.Scenes[i] = Str(data, ref pos);
        }
        else
            rec.Scenes = new[] { rec.StartScene };
        return true;
    }

    static int I32(byte[] b, ref int p)
    {
        int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
        p += 4;
        return v;
    }

    static string Str(byte[] b, ref int p)
    {
        int len = b[p] | (b[p + 1] << 8);
        p += 2;
        var s = Encoding.UTF8.GetString(b, p, len);
        p += len;
        return s;
    }

#if DE_EDITOR
    public static byte[] Write(string name, string startScene, IReadOnlyList<string> scenes = null)
    {
        var strs = new List<byte[]> { Encoding.UTF8.GetBytes(name ?? ""), Encoding.UTF8.GetBytes(startScene ?? "") };
        int nScenes = scenes?.Count ?? 0;
        for (int i = 0; i < nScenes; i++)
            strs.Add(Encoding.UTF8.GetBytes(scenes[i] ?? ""));
        int size = 8 + 4;
        foreach (var s in strs)
            size += 2 + s.Length;
        var b = new byte[size];
        int p = 0;
        Put(b, ref p, Magic);
        Put(b, ref p, Version);
        PutStr(b, ref p, strs[0]);
        PutStr(b, ref p, strs[1]);
        Put(b, ref p, nScenes);
        for (int i = 0; i < nScenes; i++)
            PutStr(b, ref p, strs[2 + i]);
        return b;
    }

    static void PutStr(byte[] b, ref int p, byte[] s)
    {
        b[p++] = (byte)s.Length; b[p++] = (byte)(s.Length >> 8);
        Buffer.BlockCopy(s, 0, b, p, s.Length); p += s.Length;
    }

    static void Put(byte[] b, ref int p, int v)
    {
        b[p++] = (byte)v; b[p++] = (byte)(v >> 8); b[p++] = (byte)(v >> 16); b[p++] = (byte)(v >> 24);
    }
#endif
}
