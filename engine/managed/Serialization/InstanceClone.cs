using System;
using System.Collections;
using System.Collections.Generic;

namespace DigitoyEngine;

// GameObject.Instantiate'in sema tabanli tamamlayicisi (Unity semantigi, Unity maliyeti DEGIL):
//   Entry.CopyTo shallow kopyalar; bu sinif yalniz PLANDAKI alanlara dokunur:
//   - Deep: serilesen liste / ic ice nesne → yeni ornek (sablonla paylasim yok; MovieClip.Tracks, Binding.Binds).
//   - Refs: Go/CompRef (ust duzey ya da liste/nesne icinde) → klon haritasiyla remap (klon ici isaretler klona).
//   Plan tip basina BIR KEZ kurulur (katalogla yasar); plansiz tipte sifir ek is, reflection yok (sema delegate'leri).
public static class InstanceClone
{
    public struct Field
    {
        public SerializedType.FieldSchema F;
        public bool Deep;  // yeni ornek gerekir (liste / class nesne / icinde liste tasiyan struct)
        public bool Refs;  // alt agacinda Go/CompRef var
    }

    public static Field[] PlanOf(TypeCatalog.Entry e)
    {
        if (e.ClonePlan != null)
            return e.ClonePlan;
        var list = new List<Field>();
        if (e.Schema != null)
            foreach (var f in e.Schema)
            {
                bool deep = NeedsDeep(f);
                bool refs = HasRef(f);
                if (deep || refs)
                    list.Add(new Field { F = f, Deep = deep, Refs = refs });
            }
        return e.ClonePlan = list.ToArray();
    }

    static bool NeedsDeep(SerializedType.FieldSchema f)
    {
        if (f.Kind == SerializedType.Kind.List)
            return true;
        if (f.Kind != SerializedType.Kind.Object)
            return false;
        if (!f.ElementType.IsValueType)
            return true; // class nesne: referans paylasilmasin
        if (f.Nested != null)
            foreach (var n in f.Nested)
                if (NeedsDeep(n))
                    return true; // struct icinde liste/nesne
        return false;
    }

    static bool HasRef(SerializedType.FieldSchema f)
    {
        if (f.Kind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef)
            return true;
        if (f.Kind == SerializedType.Kind.List && f.ElementKind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef)
            return true;
        if (f.Nested != null)
            foreach (var n in f.Nested)
                if (HasRef(n))
                    return true;
        return false;
    }

    // Shallow CopyTo sonrasi: plandaki derin alanlari yeni orneklerle degistirir.
    public static void DeepCopy(Field[] plan, object dst)
    {
        for (int i = 0; i < plan.Length; i++)
            if (plan[i].Deep)
                plan[i].F.Set(dst, CloneValue(plan[i].F.Get(dst), plan[i].F));
    }

    static object CloneValue(object v, SerializedType.FieldSchema f)
    {
        if (v == null)
            return null;
        switch (f.Kind)
        {
            case SerializedType.Kind.List:
                {
                    var src = (IList)v;
                    int n = src.Count;
                    bool objElem = f.ElementKind == SerializedType.Kind.Object;
                    if (f.NewArray != null)
                    {
                        var arr = (Array)f.NewArray(n);
                        for (int i = 0; i < n; i++)
                            arr.SetValue(objElem ? CloneObject(src[i], f) : src[i], i);
                        return arr;
                    }
                    var list = (IList)f.NewList();
                    for (int i = 0; i < n; i++)
                        list.Add(objElem ? CloneObject(src[i], f) : src[i]);
                    return list;
                }
            case SerializedType.Kind.Object:
                return CloneObject(v, f);
            default:
                return v;
        }
    }

    // Ic ice nesne: class → yeni ornek + alan alan kopya; struct → kutulu kopya zaten yeni, yalniz derin alanlari.
    static object CloneObject(object v, SerializedType.FieldSchema f)
    {
        if (v == null || f.Nested == null)
            return v;
        object dst;
        if (f.ElementType.IsValueType)
            dst = v; // Get/indeksleme kutuladi = kopya
        else
        {
            dst = f.NewElement();
            foreach (var n in f.Nested)
                n.Set(dst, n.Get(v));
        }
        foreach (var n in f.Nested)
            if (NeedsDeep(n))
                n.Set(dst, CloneValue(n.Get(dst), n));
        return dst;
    }

    // Klon ici referanslari haritayla degistirir (haritada olmayan = dis referans, aynen kalir).
    public static void Remap(Field[] plan, object dst,
        Dictionary<Component, Component> compMap, Dictionary<GameObject, GameObject> goMap)
    {
        for (int i = 0; i < plan.Length; i++)
            if (plan[i].Refs)
                RemapField(dst, plan[i].F, compMap, goMap);
    }

    static void RemapField(object owner, SerializedType.FieldSchema f,
        Dictionary<Component, Component> compMap, Dictionary<GameObject, GameObject> goMap)
    {
        switch (f.Kind)
        {
            case SerializedType.Kind.CompRef:
                if (f.Get(owner) is Component rc && compMap.TryGetValue(rc, out var mc))
                    f.Set(owner, mc);
                return;
            case SerializedType.Kind.GoRef:
                if (f.Get(owner) is GameObject rg && goMap.TryGetValue(rg, out var mg))
                    f.Set(owner, mg);
                return;
            case SerializedType.Kind.List:
                {
                    if (f.Get(owner) is not IList list)
                        return;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        switch (f.ElementKind)
                        {
                            case SerializedType.Kind.CompRef:
                                if (item is Component c && compMap.TryGetValue(c, out var m)) list[i] = m;
                                break;
                            case SerializedType.Kind.GoRef:
                                if (item is GameObject g && goMap.TryGetValue(g, out var mgo)) list[i] = mgo;
                                break;
                            case SerializedType.Kind.Object:
                                if (item != null && f.Nested != null && !f.ElementType.IsValueType) // struct ref tasiyamaz
                                    RemapNested(item, f.Nested, compMap, goMap);
                                break;
                        }
                    }
                    return;
                }
            case SerializedType.Kind.Object:
                {
                    var v = f.Get(owner);
                    if (v != null && f.Nested != null && !f.ElementType.IsValueType)
                        RemapNested(v, f.Nested, compMap, goMap);
                    return;
                }
        }
    }

    static void RemapNested(object owner, SerializedType.FieldSchema[] nested,
        Dictionary<Component, Component> compMap, Dictionary<GameObject, GameObject> goMap)
    {
        foreach (var n in nested)
            if (HasRef(n))
                RemapField(owner, n, compMap, goMap);
    }
}
