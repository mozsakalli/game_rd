using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// PISMIS tek nesne formati (DOBJ) — SceneBinary'nin kardesi: [Asset] siniflari (elle yazilan .asset
// ya da importer "main" ciktisi) release'te bu bicimle tasinir; YAML/ObjectSerializer editorde kalir.
// Alan tablosu ve alan kodlamasi SceneBinary ile AYNI yardimcilar (alan ekle/sil/rename toleransi,
// bilinmeyen alan atlanir). Sahne baglami yoktur: Go/CompRef alanlari yazilmaz.
//
// Duzen (little-endian):
//   i32 magic "DOBJ", i32 version
//   i32 nStr, str[]              (u16 len + UTF8; tip adi, string alanlari, asset anahtarlari)
//   i32 typeNameStr
//   fields-table                 (SceneBinary.WriteFieldTable: ad + kind, ic ice)
//   fields                       (SceneBinary.WriteFields: u16 n, { u16 idx, value }[])
public static class ObjectBinary
{
    public const int Magic = 0x4A424F44; // "DOBJ"
    public const int Version = 1;

    public static bool IsBaked(byte[] data)
        => data != null && data.Length >= 8 && I32(data, 0) == Magic;

    // Tip adi (katalog adi): PakRoots / tip cozumu nesneyi kurmadan okur.
    public static string TypeNameOf(byte[] data)
    {
        if (!IsBaked(data))
            return null;
        var r = new SceneBinary.Reader(data, null);
        r.Pos = 8;
        int nStr = r.I32();
        var strs = new string[nStr];
        for (int i = 0; i < nStr; i++)
            strs[i] = r.Str();
        int ti = r.I32();
        return ti >= 0 && ti < nStr ? strs[ti] : null;
    }

    // Baytlardan nesne: into verilirse AYNI instance'a okunur (reimport: canli referanslar kirilmaz),
    // yoksa katalogdan kurulur. Bilinmeyen tip -> null + uyari. Asset alanlari assets'ten senkron cozulur
    // (release'te preload tamponu: bagimlilik grafigi bu nesneden once referanslarini indirir).
    public static object Read(byte[] data, TypeCatalog catalog, AssetDatabase assets, object into = null)
    {
        var r = new SceneBinary.Reader(data, assets);
        if (r.I32() != Magic)
            throw new InvalidOperationException("baked nesne: magic uyusmuyor");
        int ver = r.I32();
        if (ver != Version)
            throw new InvalidOperationException($"baked nesne: surum {ver} desteklenmiyor (beklenen {Version})");
        int nStr = r.I32();
        var strs = new string[nStr];
        for (int i = 0; i < nStr; i++)
            strs[i] = r.Str();
        r.Strs = strs;
        string typeName = strs[r.I32()];
        var table = SceneBinary.ReadFieldTable(r);
        var entry = catalog?.Find(typeName);
        if (entry == null)
        {
            AssetDatabase.LogWarning?.Invoke("[baked] bilinmeyen asset tipi: " + typeName);
            return null;
        }
        object obj = into ?? entry.CreateObject();
        if (into != null && !entry.Type.IsInstanceOfType(into))
            throw new InvalidOperationException($"baked nesne: {typeName} -> {into.GetType().Name} tip uyusmazligi");
        SceneBinary.BindFieldTable(table, entry.Schema, typeName);
        SceneBinary.ReadFields(r, obj, table, assets, r.Deferred);
        return obj;
    }

    // Katalogsuz okuma: sema hedef nesnenin tipinden kurulur (plugin ayarlari gibi katalogda olmayan,
    // ic ice tanimlanmis [Serializable] siniflar). Yazan taraf Bake(object, assets) ile ayni semayi kullanir.
    public static void ReadInto(byte[] data, object into, AssetDatabase assets)
    {
        var r = new SceneBinary.Reader(data, assets);
        if (r.I32() != Magic)
            throw new InvalidOperationException("baked nesne: magic uyusmuyor");
        int ver = r.I32();
        if (ver != Version)
            throw new InvalidOperationException($"baked nesne: surum {ver} desteklenmiyor (beklenen {Version})");
        int nStr = r.I32();
        var strs = new string[nStr];
        for (int i = 0; i < nStr; i++)
            strs[i] = r.Str();
        r.Strs = strs;
        string typeName = strs[r.I32()];
        var table = SceneBinary.ReadFieldTable(r);
        var schema = SerializedType.Build(into.GetType());
        SceneBinary.BindFieldTable(table, schema, typeName);
        SceneBinary.ReadFields(r, into, table, assets, r.Deferred);
    }

    static int I32(byte[] b, int p)
        => b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);

#if DE_EDITOR
    // RELEASE pak'i pisirilirken true (AssetPackBuilder): [DevOnly] alanlar DocNode'dan dusurulur -> default'la okunur; deger
    // (test sunucu URL'si gibi) ikiliye/pak'a HIC girmez. Dev build ve editor: false.
    public static bool StripDevOnly;

    // Alan haritasindan (YAML "fields:" / ObjectSerializer.ToNode) pisirir. assetRefs: referanslanan
    // asset anahtarlari (ham guid/yol) — pak builder bagimlilik kenari yazar.
    public static byte[] Bake(string typeName, DocNode fields, TypeCatalog catalog, Action<string> warn = null,
        List<string> assetRefs = null)
    {
        var entry = catalog.Find(typeName)
            ?? throw new InvalidOperationException("bake: katalogda asset tipi yok: " + typeName);
        return Bake(entry.Name, entry.Schema, fields, warn, assetRefs);
    }

    // Katalogsuz pisirme (ReadInto'nun esi): tip adi yalniz etiket, sema nesnenin tipinden.
    public static byte[] Bake(object obj, AssetDatabase assets, Action<string> warn = null)
        => Bake(obj.GetType().Name, SerializedType.Build(obj.GetType()), ObjectSerializer.ToNode(obj, assets), warn, null);

    // [DevOnly] alanlari (ic ice nesneler dahil) cikarilmis kopya; StripDevOnly kapaliysa aynen.
    static DocNode StripDev(DocNode fields, SerializedType.FieldSchema[] schema)
    {
        if (!StripDevOnly || fields?.Fields == null || schema == null) return fields;
        var r = DocNode.Map();
        foreach (var kv in fields.Fields)
        {
            var f = SerializedType.Find(schema, kv.Key);
            if (f != null && f.DevOnly) continue;
            r.Add(kv.Key, f != null && f.Kind == SerializedType.Kind.Object && f.Nested != null ? StripDev(kv.Value, f.Nested) : kv.Value);
        }
        return r;
    }

    static byte[] Bake(string typeName, SerializedType.FieldSchema[] schema, DocNode fields, Action<string> warn,
        List<string> assetRefs)
    {
        fields = StripDev(fields, schema);
        var strs = new Dictionary<string, int>();
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

        var body = new SceneBinary.Writer();
        int typeIdx = Str(typeName);
        SceneBinary.WriteFieldTable(body, schema, Str);
        SceneBinary.WriteFields(body, fields?.Fields ?? new List<KeyValuePair<string, DocNode>>(), schema,
            null, null, Str, AssetStr, warn, typeName);

        var w = new SceneBinary.Writer();
        w.I32(Magic);
        w.I32(Version);
        var strArr = new string[strs.Count];
        foreach (var kv in strs)
            strArr[kv.Value] = kv.Key;
        w.I32(strArr.Length);
        foreach (var s in strArr)
            w.Str(s);
        w.I32(typeIdx);
        w.Append(body);
        return w.ToArray();
    }

    // Canli nesneden pisirir (importer "main" ciktisi): once kanonik DocNode, sonra ayni yazici.
    public static byte[] Bake(object obj, TypeCatalog catalog, AssetDatabase assets, Action<string> warn = null,
        List<string> assetRefs = null)
        => Bake(obj.GetType().Name, ObjectSerializer.ToNode(obj, assets), catalog, warn, assetRefs);
#endif
}
