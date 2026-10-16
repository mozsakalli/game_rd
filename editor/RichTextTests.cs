#if DE_EDITOR
using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// Rich text smoke testleri (docs/engine-notes.md RICH TEXT): tarayici (font gerektirmez) + LayoutBox yerlesimi
// (projede font varsa; yoksa SKIP). Font tablosu testte gecici MODUL tablosu olarak kaydedilir (host'u ezme yolu
// da test edilir) ve sonunda silinir.
public static class RichTextTests
{
    static int _pass, _fail;

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[richtext] FAIL: {name}"); }
    }

    static bool Near(float a, float b, float eps = 0.01f) => MathF.Abs(a - b) < eps;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        Scanner();
        Layout(catalog);
        Console.WriteLine($"[richtext] {_pass} PASS, {_fail} FAIL");
    }

    // Tag'siz gezinti: (glyph, durum) listesi.
    static List<(int Cp, RichText.State St)> Walk(string s, Font f = null)
    {
        var list = new List<(int, RichText.State)>();
        RichText.ForEachGlyph(s, 32f, f, (cp, st) => list.Add((cp, st)));
        return list;
    }

    static string Plain(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (cp, _) in Walk(s)) sb.Append(char.ConvertFromUtf32(cp));
        return sb.ToString();
    }

    static void Scanner()
    {
        Check(Plain("a<b>b</b>c") == "abc", "b tag metinden duser");
        var w = Walk("a<b>b</b>c");
        Check(!w[0].St.Bold && w[1].St.Bold && !w[2].St.Bold, "bold durumu yalniz span icinde");
        w = Walk("<i><u>x</u>y</i>z");
        Check(w[0].St.Italic && w[0].St.Underline && w[1].St.Italic && !w[1].St.Underline && !w[2].St.Italic, "ic ice i/u");
        w = Walk("<b><i>x</b>y</i>");
        Check(w[0].St.Bold && w[0].St.Italic && !w[1].St.Bold && !w[1].St.Italic, "yanlis sirali kapanis aradakini de kapatir");
        Check(Plain("a </b> b") == "a  b", "eslesmeyen kapanis yutulur");
        Check(Plain("a < b") == "a < b" && Plain("a <x> b") == "a <x> b" && Plain("1<2 and 3>2") == "1<2 and 3>2", "gecersiz tag literal");
        Check(Plain("<b") == "<b" && Plain("<") == "<" && Plain("<>") == "<>", "kapanmamis/bos tag literal");
        Check(Plain("<color=#12> x") == "<color=#12> x" && Plain("<size=abc>x") == "<size=abc>x", "bozuk arguman literal");
        Check(Plain("<color>x") == "<color>x" && Plain("<link>x") == "<link>x", "arguman zorunlu");
        w = Walk("<color=#FF8000>x</color><color=red>y</color><color=#00000080>z</color>q");
        Check(w[0].St.HasColor && w[0].St.Color.r == 255 && w[0].St.Color.g == 128 && w[0].St.Color.b == 0 && w[0].St.Color.a == 255, "color #RRGGBB");
        Check(w[1].St.HasColor && w[1].St.Color.r == 255 && w[1].St.Color.g == 0, "color ad");
        Check(w[2].St.HasColor && w[2].St.Color.a == 128 && !w[3].St.HasColor, "color #RRGGBBAA + kapanis");
        w = Walk("<size=40>a</size><size=+8>b</size><size=-8>c</size><size=50%>d</size>e");
        Check(Near(w[0].St.Size, 40) && Near(w[1].St.Size, 40) && Near(w[2].St.Size, 24) && Near(w[3].St.Size, 16) && Near(w[4].St.Size, 32), "size N/+N/-N/N%");
        w = Walk("<size=20><size=50%>a</size>b</size>");
        Check(Near(w[0].St.Size, 16) && Near(w[1].St.Size, 20), "ic ice size (yuzde/+- tabani her zaman textSize, TMP gibi)");
        w = Walk("<link=privacy>x</link>y<link=\"t\">z</link>");
        string src = "<link=privacy>x</link>y<link=\"t\">z</link>";
        Check(w[0].St.InLink && src.Substring(w[0].St.LinkStart, w[0].St.LinkLen) == "privacy" && !w[1].St.InLink
            && src.Substring(w[2].St.LinkStart, w[2].St.LinkLen) == "t", "link id araligi (tirnakli dahil)");
        Check(Plain("<noparse><b>x</b></noparse><b>y</b>") == "<b>x</b>y", "noparse icinde literal, sonrasinda tag");
        Check(RichText.Escape("a<b>c") == "<noparse>a<b>c</noparse>" && RichText.Escape("abc") == "abc" && RichText.Escape(null) == "", "Escape");
        Check(Plain("<B>x</B><COLOR=RED>y</COLOR>") == "xy" && Walk("<B>x</B>")[0].St.Bold, "tag adi buyuk-kucuk harf duyarsiz");
        Check(Plain("<font=NoSuchFont>x</font>") == "<font=NoSuchFont>x", "tanimsiz font: acilis literal (gorunur hata), kapanis yutulur");
        // Yigin tasmasi: 20 ic ice <b> + metin; cokme yok, metin cikar.
        var deep = new System.Text.StringBuilder();
        for (int i = 0; i < 20; i++) deep.Append("<b>");
        deep.Append("x");
        for (int i = 0; i < 20; i++) deep.Append("</b>");
        Check(Plain(deep.ToString()) == "x", "derin yigin");
        Check(Plain("a\nb<b>\n</b>c") == "a\nb\nc", "satir sonu korunur");
    }

    const string TestTableKey = "Modules/__richtest/Fonts.asset";

    static void Layout(TypeCatalog catalog)
    {
        var assets = App.Assets;
        Font f1 = null, f2 = null;
        if (assets != null)
            foreach (var kv in assets.AllAssets)
            {
                string k = kv.Key;
                if (!(k.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (assets.LoadAsset(k, typeof(Font)) is not Font f || f.Glyphs == null || f.Glyphs.Length == 0 || f.SdfSize <= 0)
                    continue;
                if (f1 == null) f1 = f;
                else if (f2 == null) { f2 = f; break; }
            }
        if (f1 == null)
        {
            Console.WriteLine("[richtext] SKIP: projede font yok (yerlesim testleri)");
            return;
        }
        var table = new FontTable { DefaultFont = "T1" };
        table.Entries.Add(new FontEntry { Name = "T1", Font = f1 });
        if (f2 != null)
            table.Entries.Add(new FontEntry { Name = "T2", Font = f2 });
        Fonts.RegisterTable(TestTableKey, table);

        var prev = Scene.Active;
        var s = Scene.Create("richtext-test");
        s.Catalog = catalog;
        s.ScreenWidth = 800;
        s.ScreenHeight = 600;
        Scene.SetActive(s);
        try
        {
            Check(ReferenceEquals(Fonts.Resolve("T1"), f1) && ReferenceEquals(Fonts.Resolve(""), f1), "modul tablosu cozumu + varsayilan");

            LayoutBox Box(string text, float width = 0, bool wrap = true)
            {
                var b = new GameObject("t").AddComponent<LayoutBox>();
                b.Pivot = new Vec2(0, 0);
                b.Text = text;
                b.TextSize = 32;
                b.TextWrap = wrap;
                b.TextEllipsis = false;
                if (width > 0) b.Width = width;
                return b;
            }
            var plain = Box("Hello World");
            float pw = plain.RectWidth, ph = plain.RectHeight;
            float lineH = f1.LineHeight * (32f / f1.EmPx);
            Check(pw > 0 && Near(ph, lineH), "duz metin tek satir yuksekligi = LineHeight*scale");
            Check(Near(Box("<b>Hello</b> <i>World</i>").RectWidth, pw) && Near(Box("<u>Hello World</u>").RectHeight, ph), "b/i/u olcume girmez");
            Check(Near(Box("<color=red>Hello</color> World").RectWidth, pw), "color olcume girmez");
            Check(Near(Box("<link=x>Hello World</link>").RectWidth, pw), "link olcume girmez");
            Check(Near(Box("<noparse>Hello World</noparse>").RectWidth, pw), "noparse olcum = duz");
            Check(Near(Box("<b>Hello World").RectWidth, pw) && Near(Box("<x>Hello World").RectWidth, Box("<x>Hello World").RectWidth), "kapanmamis tag zararsiz");

            var big = Box("<size=200%>Hello World</size>");
            Check(Near(big.RectHeight, 2 * lineH) && Near(big.RectWidth, 2 * pw, 0.5f), "size 200%: yukseklik ve genislik 2x");
            var mixed = Box("Hello <size=200%>W</size>orld");
            Check(Near(mixed.RectHeight, 2 * lineH), "karisik boyutta satir yuksekligi = maks");
            Check(Near(Box("Hello <size=50%>World</size>").RectHeight, lineH), "kucuk span satiri kucultmez");

            // Wrap: duz metin tek satira sigiyor, buyuk span ikinci satira duser.
            var wrapPlain = Box("Hello World", pw + 4);
            var wrapBig = Box("Hello <size=200%>World</size>", pw + 4);
            Check(Near(wrapPlain.RectHeight, lineH) && Near(wrapBig.RectHeight, lineH + 2 * lineH), "size ile wrap: satir yukseklikleri toplami (kirilimda metrik snapshot'a doner)");

            // Kurulum (quad/link/font sayilari) Encode ile olusur.
            var q = new RenderQueue();
            void Build(LayoutBox b) { q.Begin(); b.Encode(q); }

            var link = Box("<link=privacy>Hello</link> World");
            Build(link);
            Check(link.HasLinks && link.LinkCount == 1, "tek satir link = 1 rect");
            Check(link.LinkAtLocal(new Vec2(2f, lineH * 0.5f)) == "privacy", "link vurusu (ilk glyph)");
            Check(link.LinkAtLocal(new Vec2(link.RectWidth - 2f, lineH * 0.5f)) == null, "link disi null");
            var link2 = Box("<link=a>Hello World</link>", pw - 4);
            Build(link2);
            Check(link2.LinkCount == 2 && Near(link2.RectHeight, 2 * lineH), "wrap'ta link iki rect");
            Check(link2.LinkAtLocal(new Vec2(2f, lineH * 1.5f)) == "a", "ikinci satir link vurusu");

            var ul = Box("<u>Hello</u> World");
            Build(ul);
            var noUl = Box("Hello World");
            Build(noUl);
            Check(ul.TextQuadCount == noUl.TextQuadCount + (f1.Page != null && f1.Page.HasPieces ? 1 : 0), "alt cizgi = +1 quad (sayfa varsa)");

            var ell = Box("<b>Hello</b> <color=red>World and more text</color>", 60);
            ell.TextEllipsis = true;
            ell.OverflowY = OverflowMode.Hidden; // Grow'da yukseklik icerige acilirdi
            ell.Height = lineH;
            Build(ell);
            Check(ell.TextQuadCount > 0 && ell.TextQuadCount <= noUl.TextQuadCount + 3 && Near(ell.RectHeight, lineH), "ellipsis + tag cokmez, tek satira kirpar");

            if (f2 != null)
            {
                var two = Box("Hello <font=T2>World</font>");
                Build(two);
                Check(two.TextFontCount == 2, "font span = 2 font grubu");
                Check(two.RectHeight >= lineH - 0.01f, "font span satir yuksekligi >= taban");
            }
            Check(Box("<font=Nope>Hello World</font>").RectWidth > pw, "tanimsiz font literal cizilir (daha genis)");
        }
        finally
        {
            Scene.SetActive(prev);
            Scene.Unload(s);
            Fonts.RegisterTable(TestTableKey, null);
        }
    }
}
#endif
