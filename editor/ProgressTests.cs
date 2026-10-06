using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Acilis smoke: ProgressRenderer shader'lari (GL context kuruluyken) derlenir,
// katman emisyonu beklenen quad sayisini uretir, fx zinciriyle compose olur.
static class ProgressTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var bar = Shader.TryCreateEffect(ProgressRenderer.BarFragment, false, out string e1);
        Check(bar != null, "bar shader derlenir (" + e1 + ")");
        var rad = Shader.TryCreateEffect(ProgressRenderer.RadialFragment, false, out string e2);
        Check(rad != null, "radial shader derlenir (" + e2 + ")");

        var prev = Scene.Active;
        var s = Scene.Create("progress-test");
        s.Catalog = catalog;
        Scene.SetActive(s);
        try
        {
            var go = new GameObject("pr");
            var p = go.AddComponent<ProgressRenderer>();
            p.Width = 200; p.Height = 20; p.Value = 0.5f;
            p.Track = Color.Transparent; p.BorderWidth = 0; p.EdgeSize = 0;
            var q = new RenderQueue();
            q.Begin(); p.Encode(q);
            Check(q.Count == 1, "yalniz fill = 1 quad");

            p.Track = new Color(0, 0, 0, 100); p.BorderWidth = 2; p.EdgeSize = 0.05f;
            q.Begin(); p.Encode(q);
            Check(q.Count == 4, "border + track + fill + edge = 4 quad");

            p.Direction = ProgressDirection.CenterOutHorizontal;
            q.Begin(); p.Encode(q);
            Check(q.Count == 5, "center-out uc bandi iki yanda = 5 quad");

            p.Value = 0f;
            q.Begin(); p.Encode(q);
            Check(q.Count == 2, "deger 0: fill/edge yok");

            // Ghost: deger dusunce eski seviye kalir, hizla asagi iner.
            p.Direction = ProgressDirection.LeftToRight;
            p.GhostSpeed = 1f; p.Value = 1f;
            s.Update(1f / 60f);
            p.Value = 0.2f;
            s.Update(0.1f);
            Check(p.GhostValue > 0.2f && p.GhostValue < 1f, "ghost gecikmeli iner");
            q.Begin(); p.Encode(q);
            Check(q.Count == 5, "ghost katmani eklenir");

            // Radial: halka padding'den kapaninca katman dusmeli (crash yok).
            p.Shape = ProgressShape.Radial; p.Width = p.Height = 100; p.Thickness = 6; p.Padding = 4;
            p.GhostSpeed = 0; p.EdgeSize = 0;
            q.Begin(); p.Encode(q);
            Check(q.Count == 2, "radial: border + track (fill halkasi kapandi)");
            p.Padding = 0;
            q.Begin(); p.Encode(q);
            Check(q.Count == 3, "radial: border + track + fill");

            // .fx zinciri: USER'li core compose olur, batch materyali degisir.
            var fx = new PixelEffect { Name = "pr-gray" };
            fx.SetBody("VEC4 fx(VEC4 c, VEC2 uv) { FLOAT g = dot(c.rgb, VEC3(0.299, 0.587, 0.114)); return VEC4(g, g, g, c.a); }");
            p.Effects = new List<PixelEffect> { fx };
            q.Begin(); p.Encode(q);
            Check(q.Count == 3, "fx zinciriyle emisyon degismez");

            GameObject.Destroy(go);
            s.Update(0f);
        }
        finally
        {
            Scene.SetActive(prev);
            Scene.Unload(s);
        }
        Console.WriteLine($"[progress] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[progress] FAIL: {name}"); }
    }
}
