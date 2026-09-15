using System;
using DigitoyEngine;

namespace DigitoyEditor;

// Uretilmis katalog (CatalogWriter -> RegistryCompiler) dogrulamasi.
// CatalogsEqual her RebuildCatalog'da paranoid diff olarak kosar;
// Run acilista fonksiyonel smoke (Create/CopyTo/GetFloat/SetFloat/ShowIf).
public static class RegistryTests
{
    // Yapisal kiyas: entry kumesi + Flags/Previewable/Type + sema derin esitligi + alias'lar.
    // GetFloat/SetFloat kiyaslanmaz (reflection katalogda TEMBEL doldurulur — null olabilir).
    public static bool CatalogsEqual(TypeCatalog a, TypeCatalog b, out string diff)
    {
        foreach (var ea in a.Entries)
        {
            var eb = b.Find(ea.Name);
            if (eb == null) { diff = "eksik entry: " + ea.Name; return false; }
            if (ea.Type != eb.Type) { diff = ea.Name + ": Type " + ea.Type + " != " + eb.Type; return false; }
            if (ea.Flags != eb.Flags) { diff = ea.Name + ": Flags " + ea.Flags + " != " + eb.Flags; return false; }
            if (ea.Previewable != eb.Previewable) { diff = ea.Name + ": Previewable farki"; return false; }
            if (!SchemaEqual(ea.Schema, eb.Schema, ea.Name, out diff)) return false;
        }
        int ca = 0, cb = 0;
        foreach (var _ in a.Entries) ca++;
        foreach (var _ in b.Entries) cb++;
        if (ca != cb) { diff = "entry sayisi " + ca + " != " + cb; return false; }
        foreach (var kv in a.Aliases)
        {
            if (!b.Aliases.TryGetValue(kv.Key, out var v) || v != kv.Value)
            { diff = "alias eksik/farkli: " + kv.Key; return false; }
        }
        diff = null;
        return true;
    }

    static bool SchemaEqual(SerializedType.FieldSchema[] a, SerializedType.FieldSchema[] b, string owner, out string diff)
    {
        if ((a?.Length ?? 0) != (b?.Length ?? 0))
        { diff = owner + ": sema uzunlugu " + (a?.Length ?? 0) + " != " + (b?.Length ?? 0); return false; }
        for (int i = 0; i < (a?.Length ?? 0); i++)
        {
            var fa = a[i]; var fb = b[i];
            string at = owner + "." + fa.Name;
            if (fa.Name != fb.Name || fa.FormerName != fb.FormerName)
            { diff = at + ": ad/FormerName farki (" + fb.Name + ")"; return false; }
            if (fa.Kind != fb.Kind || fa.ElementKind != fb.ElementKind || fa.ElementType != fb.ElementType)
            { diff = at + ": kind/element farki"; return false; }
            if (fa.FieldType != fb.FieldType || fa.DeclaringType != fb.DeclaringType)
            { diff = at + ": FieldType/DeclaringType farki"; return false; }
            if (fb.Get == null || fb.Set == null)
            { diff = at + ": boxed Get/Set eksik"; return false; }
            if ((fa.NewList != null) != (fb.NewList != null)
                || (fa.NewArray != null) != (fb.NewArray != null)
                || (fa.NewElement != null) != (fb.NewElement != null))
            { diff = at + ": kurucu delege farki"; return false; }
            int sa = fa.ShowIf == null ? -1 : Array.IndexOf(a, fa.ShowIf);
            int sb = fb.ShowIf == null ? -1 : Array.IndexOf(b, fb.ShowIf);
            if (sa != sb || fa.ShowIfScalar != fb.ShowIfScalar)
            { diff = at + ": ShowIf farki"; return false; }
            if (!SchemaEqual(fa.Nested, fb.Nested, at, out diff))
                return false;
        }
        diff = null;
        return true;
    }

    // --- acilis fonksiyonel smoke ---

    static int _pass, _fail;

    static void Check(bool ok, string name)
    {
        if (ok) _pass++;
        else { _fail++; Console.WriteLine("[registrytest] FAIL: " + name); }
    }

    public static void Run(TypeCatalog cat)
    {
        _pass = 0; _fail = 0;

        Check(App.RegistryActive, "uretilmis katalog aktif (derleme/diff basarili)");

        var e = cat.Find("SpriteRenderer");
        Check(e != null, "SpriteRenderer entry var");
        if (e != null)
        {
            // Create uretilmis 'new' yolundan
            var inst = e.Create();
            Check(inst is SpriteRenderer, "Create dogru tip");

            // GetFloat/SetFloat uretilmis lambda'lar (public float alan)
            var wf = SerializedType.Find(e.Schema, "Width");
            Check(wf != null && wf.Kind == SerializedType.Kind.Float, "Width semasi Float");
            if (wf != null && inst is SpriteRenderer sr)
            {
                Check(wf.SetFloat != null && wf.GetFloat != null, "float erisimciler onceden dolu");
                wf.SetFloat(sr, 123.5f);
                Check(sr.Width == 123.5f && wf.GetFloat(sr) == 123.5f, "SetFloat/GetFloat calisir");

                // CopyTo uretilmis duz atamalar
                var dst = (SpriteRenderer)e.Create();
                e.CopyTo(sr, dst);
                Check(dst.Width == 123.5f, "CopyTo public alan kopyalar");

                // Boxed cekirdek erisimciler (release serializer yolu)
                Check(wf.Get != null && wf.Set != null, "boxed Get/Set dolu");
                wf.Set(dst, 44f);
                Check(dst.Width == 44f && (float)wf.Get(dst) == 44f, "boxed Set/Get calisir");
            }
        }

        // Private [SerializeField] alanlar: UnsafeAccessor thunk'lari (LayoutBox)
        var lb = cat.Find("LayoutBox");
        Check(lb != null, "LayoutBox entry var");
        if (lb != null)
        {
            var wf = SerializedType.Find(lb.Schema, "width");
            Check(wf != null && wf.Kind == SerializedType.Kind.Float, "LayoutBox.width semasi");
            var src = lb.Create();
            var dst = lb.Create();
            if (wf != null)
            {
                wf.SetFloat(src, 77f);
                lb.CopyTo(src, dst);
                Check(wf.GetFloat(dst) == 77f, "private alan CopyTo + thunk erisimcileri");
            }
            // ShowIf cozumu uretilmis kodda korunur (LayoutBox.Text: text* alanlari font'a bagli)
            var tf = SerializedType.Find(lb.Schema, "text");
            Check(tf == null || tf.ShowIf != null, "ShowIf referansi bagli");
        }

        Console.WriteLine($"[registrytest] {_pass} PASS {_fail} FAIL");
    }
}
