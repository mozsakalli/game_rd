using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// .fx asset'i: core shader'in urettigi rengi isleyen kucuk DSL govdesi.
// Imza: 'VEC4 fx(VEC4 c, VEC2 uv)' — c DUZ (straight) renk, uv LOKAL 0..1.
// Govde FXP (Renderer.FxParams, per-instance vec4) ve TIME (global vec4: x=sn,
// y=dt, z=fract(sn), w=frame) okuyabilir; core'un USER kanalina erisemez.
// Renderer.Effects listesindeki sirayla tek shader'a compose edilir (ek pass yok).
// [Asset] SAF VERI: tek alan govde metni; FxImporter .fx dosyasini bu nesneye cevirir, reload yerinde
// (Body referansi degisir -> compose cache tazelenir). Derleme ilk kullanimda (FxCompose).
[Serializable, Asset]
public sealed class PixelEffect
{
    public string Body = "";

    // Derleme hatalari buraya akar; editor Console'a baglar, standalone stderr.
    public static Action<string> LogError = msg => Console.Error.WriteLine(msg);

    internal void SetBody(string body) => Body = body;
}

// Icerik-anahtarli compose cache: ayni (core, efekt dizisi) her renderer'da AYNI
// composed shader'i paylasir (cift derleme yok, batch merge korunur). Frame yolu
// yalniz referans kiyasli lineer tarama — alloc/string yok.
static class FxCompose
{
    struct Entry
    {
        public Shader Core;
        public PixelEffect[] Fx;
        public string[] Bodies;  // derlendigi andaki govde referanslari (degisti = bayat)
        public Shader Composed; // null = derleme basarisiz -> core kullanilir
    }

    static readonly List<Entry> _cache = new();

    // Zincir derlenemezse TAMAMI devre disi kalir ve core doner (yarim zincir yok).
    internal static Shader Get(Shader core, List<PixelEffect> fx)
    {
        int n = fx.Count;
        for (int i = 0; i < _cache.Count; i++)
        {
            var e = _cache[i];
            if (!ReferenceEquals(e.Core, core) || e.Fx.Length != n)
                continue;
            bool refsMatch = true, versMatch = true;
            for (int j = 0; j < n; j++)
            {
                if (!ReferenceEquals(e.Fx[j], fx[j])) { refsMatch = false; break; }
                if (!ReferenceEquals(e.Bodies[j], fx[j]?.Body)) versMatch = false;
            }
            if (!refsMatch)
                continue;
            if (versMatch)
                return e.Composed ?? core;
            Rebuild(ref e); // .fx degisti: ayni girdiyi yerinde tazele
            _cache[i] = e;
            return e.Composed ?? core;
        }
        var ne = new Entry { Core = core, Fx = new PixelEffect[n], Bodies = new string[n] };
        for (int j = 0; j < n; j++)
            ne.Fx[j] = fx[j];
        Rebuild(ref ne);
        _cache.Add(ne);
        return ne.Composed ?? core;
    }

    static void Rebuild(ref Entry e)
    {
        int live = 0;
        for (int j = 0; j < e.Fx.Length; j++)
        {
            e.Bodies[j] = e.Fx[j]?.Body;
            if (e.Fx[j] != null)
                live++;
        }
        if (live == 0)
        {
            e.Composed = null; // bos/nul zincir = sessizce core
            return;
        }
        var bodies = new string[live];
        int k = 0;
        for (int j = 0; j < e.Fx.Length; j++)
            if (e.Fx[j] != null)
                bodies[k++] = e.Fx[j].Body ?? "";
        e.Composed = Shader.ComposeEffects(e.Core.Body, bodies, out string error);
        if (e.Composed == null)
            PixelEffect.LogError($"[fx] efekt zinciri derlenemedi ({e.Fx.Length} efekt): {error} — efektsiz cizilecek");
    }
}
