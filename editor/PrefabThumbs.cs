using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DigitoyEngine;

namespace DigitoyEditor;

// Prefab kucuk resmi: prefab gecici bir sahneye spawn edilir, renderer bbox'lari
// kadraja alinir ve 128x128 RT'ye cizilir; frame sonunda (submit sonrasi) RT okunup
// ThumbnailCache'e konur. Frame basina EN FAZLA BIR prefab (bloklayici spawn + readback).
// Tek RT omur boyu yasar (frame ortasi GPU destroy yok); gecici sahne ayni frame'de kaldirilir
// (GO destroy GPU kaynagi silmez; Resources.ScheduleCollect ertelenmis toplar).
// Editor core tipleri tanimaz: yalniz Renderer.GetLocalSelectionBounds + Scene/Camera API'si.
static unsafe class PrefabThumbs
{
    const int Size = ThumbnailCache.MaxSize;
    const float Pad = 0.08f; // kadraj kenar payi (oran)

    static readonly List<string> _queue = new();
    static readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    static Texture _rt;
    static Camera _cam;
    static Scene _scene;   // bu frame cizilen gecici sahne (AfterSubmit kaldirir)
    static string _key;    // bu frame cizilen anahtar
    static bool _rendered;

    public static void Request(string key)
    {
        if (string.IsNullOrEmpty(key) || !_queued.Add(key))
            return;
        _queue.Add(key);
    }

    public static void ClearQueue()
    {
        _queue.Clear();
        _queued.Clear();
    }

    // Encode asamasi (cb.Begin sonrasi, kameralardan once): kuyruktan bir prefab al, cizdir.
    public static void Encode(CommandBuffer cb)
    {
        _rendered = false;
        _key = null;
        if (_queue.Count == 0 || App.Assets == null || App.Catalog == null)
            return;
        string key = _queue[^1];
        _queue.RemoveAt(_queue.Count - 1);
        _queued.Remove(key);
        var prefab = App.Assets.LoadPrefab(key);
        if (prefab == null)
        {
            ThumbnailCache.Put(key, null, 0, 0); // kaynak yok: slot "denendi" kalsin
            return;
        }
        _rt ??= Texture.CreateRenderTarget(Size, Size);
        _cam ??= new Camera { Order = -100, ClearColor = true, ClearDepth = true, BackgroundColor = new Color(0, 0, 0, 0) };
        _cam.Target = _rt;

        var prevActive = Scene.Active;
        var scene = Scene.Create("thumb:" + key);
        scene.Catalog = App.Catalog;
        scene.Assets = App.Assets;
        scene.ExternallyDriven = true;
        scene.EditMode = true;
        scene.ScreenWidth = GameOutput.ViewW;
        scene.ScreenHeight = GameOutput.ViewH;
        Scene.SetActive(scene);
        try
        {
            prefab.Instantiate();
            scene.Update(0f, simulate: false); // lifecycle + layout cozumu
            if (!Bounds(scene, out float x0, out float y0, out float x1, out float y1))
            {
                ThumbnailCache.Put(key, null, 0, 0);
                return;
            }
            float w = x1 - x0, h = y1 - y0;
            float s = MathF.Max(MathF.Max(w, h), 1f) * (1f + Pad * 2f);
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            _cam.SetOrtho(cx - s * 0.5f, cx + s * 0.5f, cy - s * 0.5f, cy + s * 0.5f);
            scene.Render(_cam.Queue);
            _cam.Encode(cb, Size, Size);
            _scene = scene;
            _key = key;
            _rendered = true;
        }
        catch (Exception e)
        {
            EditorLog.Warning($"[thumb] prefab {key}: {e.Message}");
            ThumbnailCache.Put(key, null, 0, 0);
        }
        finally
        {
            if (Scene.Active == scene)
                Scene.SetActive(prevActive);
            if (!_rendered)
            {
                Scene.Unload(scene);
                _scene = null;
            }
        }
    }

    // Submit sonrasi: RT'yi oku, onbellege koy, gecici sahneyi kaldir.
    public static void AfterSubmit()
    {
        if (!_rendered)
            return;
        _rendered = false;
        var scene = _scene;
        string key = _key;
        _scene = null;
        _key = null;
        try
        {
            var px = new byte[Size * Size * 4];
            fixed (byte* p = px)
            {
                if (!GameOutput.ReadRgba(_rt, Size, Size, p))
                {
                    ThumbnailCache.Put(key, null, 0, 0);
                    return;
                }
            }
            // RT'de karisim premultiplied birikir (seffaf arka plan); atlas duz alfa bekler.
            bool anyVisible = false;
            for (int i = 0; i < px.Length; i += 4)
            {
                int a = px[i + 3];
                if (a == 0) continue;
                anyVisible = true;
                if (a == 255) continue;
                px[i] = (byte)Math.Min(255, px[i] * 255 / a);
                px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a);
                px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a);
            }
            // Tamamen saydam (dolgusuz kap: Row/Column/Spacer) -> kucuk resim yok, panel harf rozeti cizer.
            if (!anyVisible)
            {
                ThumbnailCache.Put(key, null, 0, 0);
                return;
            }
            // Olculdu: readback atlas hucresine ters dusuyor (metin bas asagi) -> satirlari cevir.
            int stride = Size * 4;
            var row = new byte[stride];
            for (int y = 0; y < Size / 2; y++)
            {
                int a = y * stride, b = (Size - 1 - y) * stride;
                Buffer.BlockCopy(px, a, row, 0, stride);
                Buffer.BlockCopy(px, b, px, a, stride);
                Buffer.BlockCopy(row, 0, px, b, stride);
            }
            ThumbnailCache.Put(key, px, Size, Size);
        }
        finally
        {
            if (scene != null)
                Scene.Unload(scene);
        }
    }

    // Sahnedeki tum renderer bbox'larinin dunya birlesimi (2D).
    static bool Bounds(Scene scene, out float x0, out float y0, out float x1, out float y1)
    {
        x0 = y0 = float.MaxValue; x1 = y1 = float.MinValue;
        bool any = false;
        for (int i = 0; i < scene.RootCount; i++)
            Walk(scene.GetRoot(i).transform, ref x0, ref y0, ref x1, ref y1, ref any);
        return any;
    }

    static void Walk(Transform t, ref float x0, ref float y0, ref float x1, ref float y1, ref bool any)
    {
        var go = t.gameObject;
        if (go.activeInHierarchy)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null && r.GetLocalSelectionBounds(out var c, out var h))
            {
                ref var m = ref t._getWorldMatrix();
                for (int k = 0; k < 4; k++)
                {
                    float lx = c.x + ((k & 1) == 0 ? -h.x : h.x);
                    float ly = c.y + ((k & 2) == 0 ? -h.y : h.y);
                    float wx = m.m[0] * lx + m.m[4] * ly + m.m[12];
                    float wy = m.m[1] * lx + m.m[5] * ly + m.m[13];
                    if (wx < x0) x0 = wx; if (wx > x1) x1 = wx;
                    if (wy < y0) y0 = wy; if (wy > y1) y1 = wy;
                }
                any = true;
            }
        }
        for (int i = 0; i < t.childCount; i++)
            Walk(t.GetChild(i), ref x0, ref y0, ref x1, ref y1, ref any);
    }
}
