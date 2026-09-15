using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Oyunun PAYLASILAN render hedefi: oyun sahnesi frame'de BIR kez buraya cizilir,
// Game View gosterir. Boyutu frame'in ilk isteyen Game View'u belirler.
static class GameOutput
{
    static Texture _rt;
    static Texture _retired; // frame ortasi destroy yasak: sonraki frame basinda olur
    static int _rtW, _rtH;
    static bool _sizedThisFrame;

    // Oyunun Screen.width/height'i (mantiksal).
    public static float ViewW { get; private set; } = 800f;
    public static float ViewH { get; private set; } = 600f;

    public static Texture EnsureTarget()
    {
        if (_rt == null)
            Resize(800, 600);
        return _rt;
    }

    public static void BeginFrame()
    {
        _retired?.Destroy();
        _retired = null;
        _sizedThisFrame = false;
    }

    // Frame'in ilk istegi kazanir (surucu viewport). w/h mantiksal panel boyutu.
    public static void RequestSize(float w, float h)
    {
        // Frozen capture eski RT view'larini replay eder: resize = destroy = olu handle.
        if (_sizedThisFrame || w < 8 || h < 8 || RenderDebug.Frozen)
            return;
        _sizedThisFrame = true;
        ViewW = w;
        ViewH = h;
        float s = Gui.Scale > 0 ? Gui.Scale : 1f;
        int pw = Math.Max(8, (int)(w * s));
        int ph = Math.Max(8, (int)(h * s));
        if (pw != _rtW || ph != _rtH)
            Resize(pw, ph);
    }

    static void Resize(int w, int h)
    {
        _retired ??= _rt;
        _rt = Texture.CreateRenderTarget(w, h);
        _rtW = w;
        _rtH = h;
    }
}
