using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// .fx asset'i: core shader'in urettigi rengi isleyen kucuk DSL govdesi.
// Imza: 'VEC4 fx(VEC4 c, VEC2 uv)' — c DUZ (straight) renk, uv LOKAL 0..1.
// Renderer.Effects listesindeki sirayla tek shader'a compose edilir (ek pass yok).
public sealed class PixelEffect : IAsset
{
    public string Name { get; set; }

    internal string BodyText;
    internal int Version; // reload'da ++ (compose cache'leri bayatlatir)

    // Derleme hatalari buraya akar; editor Console'a baglar, standalone stderr.
    public static Action<string> LogError = msg => Console.Error.WriteLine(msg);

    internal void SetBody(string body)
    {
        BodyText = body;
        Version++;
    }
}

// Icerik-anahtarli compose cache: ayni (core, efekt dizisi) her renderer'da AYNI
// composed shader'i paylasir (cift derleme yok, batch merge korunur). Frame yolu
// yalniz referans + int kiyasli lineer tarama — alloc/string yok.
static class FxCompose
{
    struct Entry
    {
        public Shader Core;
        public PixelEffect[] Fx;
        public int[] Vers;
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
                if (e.Vers[j] != (fx[j]?.Version ?? 0)) versMatch = false;
            }
            if (!refsMatch)
                continue;
            if (versMatch)
                return e.Composed ?? core;
            Rebuild(ref e); // .fx degisti: ayni girdiyi yerinde tazele
            _cache[i] = e;
            return e.Composed ?? core;
        }
        var ne = new Entry { Core = core, Fx = new PixelEffect[n], Vers = new int[n] };
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
            e.Vers[j] = e.Fx[j]?.Version ?? 0;
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
                bodies[k++] = e.Fx[j].BodyText ?? "";
        e.Composed = Shader.ComposeEffects(e.Core.Body, bodies, out string error);
        if (e.Composed == null)
            PixelEffect.LogError($"[fx] efekt zinciri derlenemedi ({Names(e.Fx)}): {error} — efektsiz cizilecek");
    }

    static string Names(PixelEffect[] fx)
        => string.Join(" > ", Array.ConvertAll(fx, f => f?.Name ?? "?"));
}
