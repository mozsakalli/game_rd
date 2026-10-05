using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DigitoyEngine;

// PISMIS (baked) sahne/prefab formati — RELEASE yolu. Pak build'de YAML doc'u
// (prefab'lar ACILMIS, override'lar uygulanmis) katalog SEMASIYLA cozulur ve
// dogrudan alan degerleri yazilir: alan ADI yok (sema indeksi), skaler metin yok
// (ham float/int/bayt), Go/CompRef onceden (id, sira) olarak cozulmus, enum
// underlying int. Runtime'da DocNode bile kurulmaz: bayt -> alan setter.
//
// Duzen (little-endian):
//   i32 magic "DSCN", i32 version
//   i32 nStr, str[]                     (u16 len + UTF8; asset guid'leri, adlar)
//   i32 nType, { i32 nameStr, u32 schemaHash }[]   (component tipleri)
//   i32 nObj, obj[]
//   obj: i32 id, i32 parent, i32 nameStr, u8 active, i32 layer, f32 pos[3] rot[3] scale[3]
//        u16 nComp, comp[]
//   comp: u16 type, u8 enabled, i32 byteLen (atlama icin), u16 nField, { u16 idx, value }[]
//   value (Kind'e gore): Float f32 | Int i32 | Bool u8 | String i32 str | Enum i32 |
//        Vec2 2f | Vec3 3f | Vec4 4f | Color 4u8 | Asset i32 str (-1 null) |
//        GoRef i32 id (0 null) | CompRef i32 id, u16 sira | List i32 n (-1 null) + eleman[] |
//        Object u8 var + u16 nField + { u16 idx, value }[]
//
// schemaHash: alan adi+kind (ic ice dahil) FNV-1a — pak ile derlenen kod uyusmazsa
// component atlanir ve uyari verilir (crash yok; cozum: pak'i yeniden build et).
public static unsafe class SceneBinary
{
    public const int Magic = 0x4E435344; // "DSCN"
    public const int Version = 1;

    public static bool IsBaked(byte[] data)
        => data != null && data.Length >= 8 && ReadI32(data, 0) == Magic;

    // --- Sema parmak izi (yazici ve okuyucu ayni fonksiyonu kullanir) ---

    public static uint SchemaHash(SerializedType.FieldSchema[] schema)
    {
        uint h = 2166136261u;
        HashSchema(schema, ref h);
        return h;
    }

    static void HashSchema(SerializedType.FieldSchema[] schema, ref uint h)
    {
        if (schema == null)
            return;
        foreach (var f in schema)
        {
            HashStr(f.Name, ref h);
            HashByte((byte)f.Kind, ref h);
            HashByte((byte)f.ElementKind, ref h);
            if (f.Nested != null)
            {
                HashByte(1, ref h);
                HashSchema(f.Nested, ref h);
            }
            HashByte(0xFF, ref h);
        }
    }

    static void HashStr(string s, ref uint h)
    {
        if (s == null)
            return;
        for (int i = 0; i < s.Length; i++)
        {
            HashByte((byte)s[i], ref h);
            HashByte((byte)(s[i] >> 8), ref h);
        }
    }

    static void HashByte(byte b, ref uint h)
    {
        h ^= b;
        h *= 16777619u;
    }

    // ======================= RUNTIME OKUYUCU =======================

    // Pismis baytlardan sahneyi/prefab'i AKTIF sahneye kurar; ilk kok GO'yu dondurur.
    // SceneDoc.Spawn ile ayni sozlesme: once tum GO'lar dogar, referanslar sonra
    // cozulur, aktivasyon en sonda topluca (Awake deserialize edilmis degerleri gorur).
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

        int nType = r.I32();
        var types = new TypeCatalog.Entry[nType];
        for (int i = 0; i < nType; i++)
        {
            string name = strs[r.I32()];
            uint hash = r.U32();
            var e = catalog.Find(name);
            if (e == null)
                AssetDatabase.LogWarning?.Invoke("[baked] bilinmeyen component tipi atlandi: " + name);
            else if (SchemaHash(e.Schema) != hash)
            {
                AssetDatabase.LogWarning?.Invoke("[baked] sema uyusmazligi, component atlandi (pak'i yeniden build edin): " + name);
                e = null;
            }
            types[i] = e;
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
                var entry = types[r.U16()];
                bool enabled = r.U8() != 0;
                int len = r.I32();
                int end = r.Pos + len;
                if (entry == null)
                {
                    r.Pos = end; // bilinmeyen/uyumsuz tip: govdeyi atla
                    continue;
                }
                var c = go.AddComponentRaw(entry);
                c._enabled = enabled;
                comps[ci] = c;
                if (entry.ReadBaked != null)
                    entry.ReadBaked(c, r); // uretilmis tipli okuyucu (boxing yok)
                else
#if DE_AOT
                    throw new InvalidOperationException("baked okuyucu yok: " + entry.Name);
#else
                    ReadFields(r, c, entry.Schema, assets, deferred); // reflection katalogu (editor)
#endif
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

#if !DE_AOT // object/boxing yolu: FieldSchema.Get/Set (reflection katalogu)
    static void ReadFields(Reader r, object owner, SerializedType.FieldSchema[] schema,
        AssetDatabase assets, List<DeferredRef> deferred)
    {
        int n = r.U16();
        for (int i = 0; i < n; i++)
        {
            var f = schema[r.U16()];
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
                                ReadElement(r, f, assets, deferred, arr, k);
                            f.Set(owner, arr);
                        }
                        else
                        {
                            var list = (System.Collections.IList)f.NewList();
                            for (int k = 0; k < count; k++)
                            {
                                list.Add(f.ElementType.IsValueType ? f.NewElement() : null);
                                ReadElement(r, f, assets, deferred, list, k);
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
                        ReadFields(r, inst, f.Nested, assets, deferred);
                        f.Set(owner, inst); // struct: boxed kopya geri yazilir
                        break;
                    }
                default:
                    f.Set(owner, r.Scalar(f.Kind, f.FieldType, assets));
                    break;
            }
        }
    }

    static void ReadElement(Reader r, SerializedType.FieldSchema f, AssetDatabase assets,
        List<DeferredRef> deferred, object container, int index)
    {
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
                        ReadFields(r, inst, f.Nested, assets, deferred);
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

#endif
    static int ReadI32(byte[] b, int p)
        => b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);

    // Uretilmis tipli okuyucu (CatalogWriter): alan indeksi -> dogrudan atama.
    public delegate void BakedReader(Component c, Reader r);

    // Bagimliliksiz imlec: BinaryReader/Stream yok (AOT corelib yuzeyi dar kalsin).
    // Uretilmis okuyucular (Registry.Read_N) bu API'yi cagirir; bellek duzeni
    // dosya basindaki yorumla birebir. Class: spawn basina tek nesne, ref-param yok.
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
        var strArr = new string[strs.Count];
        foreach (var kv in strs)
            strArr[kv.Value] = kv.Key;
        w.I32(strArr.Length);
        foreach (var s in strArr)
            w.Str(s);
        w.I32(typeList.Count);
        foreach (var e in typeList)
        {
            w.I32(Str(e.Name));
            w.U32(SchemaHash(e.Schema));
        }
        w.Append(body);
        return w.ToArray();
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
// runtime project.yaml OKUMAZ. Duzen: i32 magic "DPRJ", i32 version, str name, str startScene.
public static class ProjectBinary
{
    public const string PakKey = ".project";
    public const int Magic = 0x4A525044; // "DPRJ"
    public const int Version = 1;

    public static bool TryRead(byte[] data, out string name, out string startScene)
    {
        name = startScene = null;
        if (data == null || data.Length < 8)
            return false;
        int pos = 0;
        if (I32(data, ref pos) != Magic || I32(data, ref pos) != Version)
            return false;
        name = Str(data, ref pos);
        startScene = Str(data, ref pos);
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
    public static byte[] Write(string name, string startScene)
    {
        var n = Encoding.UTF8.GetBytes(name ?? "");
        var s = Encoding.UTF8.GetBytes(startScene ?? "");
        var b = new byte[8 + 2 + n.Length + 2 + s.Length];
        int p = 0;
        Put(b, ref p, Magic);
        Put(b, ref p, Version);
        b[p++] = (byte)n.Length; b[p++] = (byte)(n.Length >> 8);
        Buffer.BlockCopy(n, 0, b, p, n.Length); p += n.Length;
        b[p++] = (byte)s.Length; b[p++] = (byte)(s.Length >> 8);
        Buffer.BlockCopy(s, 0, b, p, s.Length);
        return b;
    }

    static void Put(byte[] b, ref int p, int v)
    {
        b[p++] = (byte)v; b[p++] = (byte)(v >> 8); b[p++] = (byte)(v >> 16); b[p++] = (byte)(v >> 24);
    }
#endif
}
