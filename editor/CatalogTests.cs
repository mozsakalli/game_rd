using System;
using DigitoyEngine;

namespace DigitoyEditor;

// Reflection katalogu fonksiyonel smoke (Create/CopyTo/Anim Get-Set/boxed erisimci/ShowIf).
// Eski RegistryTests'in uretilmis-katalog kiyasi kalkti (docs/registry-removal.md Faz 1): tek katalog var.
public static class CatalogTests
{
    static int _pass, _fail;

    static void Check(bool ok, string name)
    {
        if (ok) _pass++;
        else { _fail++; Console.WriteLine("[catalogtest] FAIL: " + name); }
    }

    public static void Run(TypeCatalog cat)
    {
        _pass = 0; _fail = 0;

        var e = cat.Find("SpriteRenderer");
        Check(e != null, "SpriteRenderer entry var");
        if (e != null)
        {
            var inst = e.Create();
            Check(inst is SpriteRenderer, "Create dogru tip");

            var wf = SerializedType.Find(e.Schema, "Width");
            Check(wf != null && wf.Kind == SerializedType.Kind.Float, "Width semasi Float");
            var wa = AnimRegistry.Find(cat, typeof(SpriteRenderer), "Width");
            Check(wa != null && wa.Kind == AnimKind.Float && wa.Get != null && wa.Set != null, "Width anim girisi dolu");
            if (wf != null && wa != null && inst is SpriteRenderer sr)
            {
                wa.Set(sr, AnimValue.FromFloat(123.5f));
                Check(sr.Width == 123.5f && wa.Get(sr).ToFloat() == 123.5f, "anim Set/Get calisir");

                var dst = (SpriteRenderer)e.Create();
                e.CopyTo(sr, dst);
                Check(dst.Width == 123.5f, "CopyTo public alan kopyalar");

                Check(wf.Get != null && wf.Set != null, "boxed Get/Set dolu");
                wf.Set(dst, 44f);
                Check(dst.Width == 44f && (float)wf.Get(dst) == 44f, "boxed Set/Get calisir");
            }
        }

        // Private [SerializeField] alanlar (LayoutBox)
        var lb = cat.Find("LayoutBox");
        Check(lb != null, "LayoutBox entry var");
        if (lb != null)
        {
            var wf = SerializedType.Find(lb.Schema, "width");
            Check(wf != null && wf.Kind == SerializedType.Kind.Float, "LayoutBox.width semasi");
            var wa = AnimRegistry.Find(cat, lb.Type, "width");
            Check(wa != null, "LayoutBox.width anim girisi (private alan)");
            var src = lb.Create();
            var dst = lb.Create();
            if (wa != null)
            {
                wa.Set(src, AnimValue.FromFloat(77f));
                lb.CopyTo(src, dst);
                Check(wa.Get(dst).ToFloat() == 77f, "private alan CopyTo + erisimciler");
            }
            var tf = SerializedType.Find(lb.Schema, "text");
            Check(tf == null || tf.ShowIf != null, "ShowIf referansi bagli");
        }

        Console.WriteLine($"[catalogtest] {_pass} PASS {_fail} FAIL");
    }
}
