using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Acilis smoke: pixel-effect kompozisyonu (GL context kuruluyken kosar).
// Kasitli bozuk fx testi log'u yutmak icin LogError gecici degistirilir.
static class PixelEffectTests
{
    static int _pass, _fail;

    const string GrayBody =
        "VEC4 fx(VEC4 c, VEC2 uv) { FLOAT g = dot(c.rgb, VEC3(0.299, 0.587, 0.114)); return VEC4(g, g, g, c.a); }";

    public static void Run()
    {
        _pass = _fail = 0;
        var oldLog = PixelEffect.LogError;
        int errors = 0;
        PixelEffect.LogError = _ => errors++; // kasitli bozuk test logu yutulur
        try
        {
            // Bisect problari: state sorgusu / localUv plumbing / kompozisyon ayrimi.
            var p1 = Shader.TryCreateEffect("VEC4 fs_main(VEC2 uv, VEC4 color) { return color; }", false, out string e1);
            Check(p1 != null, "probe: trivial shader VALID (" + e1 + ")");
            var p2 = Shader.TryCreateEffect("VEC4 fs_main(VEC2 uv, VEC4 color) { return color; }", true, out string e2);
            Check(p2 != null, "probe: localUv varying (kullanilmadan) VALID (" + e2 + ")");
            var p3 = Shader.TryCreateEffect("VEC4 fs_main(VEC2 uv, VEC4 color) { return color * VEC4(LOCALUV, 1.0, 1.0); }", true, out string e3);
            Check(p3 != null, "probe: LOCALUV kullanimli VALID (" + e3 + ")");

            var fx = new PixelEffect { Name = "test-gray" };
            fx.SetBody(GrayBody);
            var list = new List<PixelEffect> { fx };

            var mat = new Material();
            var m1 = mat.ForEffects(list);
            Check(!ReferenceEquals(m1, mat) && m1.Shader != null
                && Sokol.ShaderValid(m1.Shader.Handle) != 0, "gecerli fx compose olur");

            // Icerik-anahtarli cache: farkli liste NESNESI ayni composed shader'i paylasir.
            var m2 = new Material().ForEffects(new List<PixelEffect> { fx });
            Check(ReferenceEquals(m1.Shader, m2.Shader), "ayni icerik = ayni composed shader");

            // USER'li core (UiShader) ile compose.
            var m3 = UiPieces.SharedMaterial.ForEffects(list);
            Check(!ReferenceEquals(m3, UiPieces.SharedMaterial)
                && Sokol.ShaderValid(m3.Shader.Handle) != 0, "USER'li core compose olur");

            // Bozuk fx: log + efektsiz core'a dusus (crash yok).
            var bad = new PixelEffect { Name = "test-bad" };
            bad.SetBody("VEC4 fx(VEC4 c, VEC2 uv) { bozuk }");
            var badList = new List<PixelEffect> { bad };
            var mb = new Material();
            Check(ReferenceEquals(mb.ForEffects(badList), mb) && errors == 1, "bozuk fx core'a duser + log");

            // Reload iyilesmesi: ayni instance duzeltilince zincir canlanir.
            bad.SetBody(GrayBody);
            Check(!ReferenceEquals(mb.ForEffects(badList), mb), "reload sonrasi iyilesir");

            // Zincir: iki efekt tek shader'da.
            var fx2 = new PixelEffect { Name = "test-invert" };
            fx2.SetBody("VEC4 fx(VEC4 c, VEC2 uv) { return VEC4(1.0 - c.rgb, c.a); }");
            var mc = new Material().ForEffects(new List<PixelEffect> { fx, fx2 });
            Check(mc.Shader != null && Sokol.ShaderValid(mc.Shader.Handle) != 0, "iki efekt zinciri compose olur");
        }
        finally
        {
            PixelEffect.LogError = oldLog;
        }
        Console.WriteLine($"[fx] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[fx] FAIL: {name}"); }
    }
}
