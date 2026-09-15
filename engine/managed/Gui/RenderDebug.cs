#if DE_EDITOR
using System;
using System.Runtime.InteropServices;

namespace DigitoyEngine.Editor;

// Frame debugger cekirdegi (yalniz DE_EDITOR build'lerinde derlenir).
//
// Model: capture istenince frame'in komut akisi kopyalanir ve WALKER ile
// cozulur (draw listesi + batch-break sebepleri + istatistikler). Frozen
// modda kopya her frame native decoder'a REPLAY edilir: ilk MaxDraws draw,
// swapchain pass'leri panelde gosterilen RT'ye yonlendirilmis halde.
// Kaynaklar (pipeline/mesh/texture id) retained oldugu icin replay gecerlidir.
public static unsafe class RenderDebug
{
    public enum BreakReason : byte
    {
        First = 0, NewPass, Pipeline, Texture, Mesh, Sampler, InstanceRange,
    }

    public static readonly string[] ReasonNames =
    {
        "ilk draw", "yeni pass", "pipeline degisti", "doku degisti",
        "mesh degisti", "sampler degisti", "instance araligi",
    };

    public struct DrawEntry
    {
        public int Pass;
        public uint Pipeline, Vbo, Ibo, TexView, Sampler;
        public int NumElements, NumInstances;
        public BreakReason Reason;
    }

    // Yakalanan akis (unmanaged kopya; sadece buyur).
    static byte* _captured;
    static int _capturedLen, _capturedCap;

    public static bool Frozen { get; private set; }
    public static bool CaptureRequested;
    public static int MaxDraws = -1;

    public static DrawEntry[] Draws = new DrawEntry[256];
    public static int DrawCount, PassCount, PipelineBinds, TotalInstances;
    public static int InstanceBytes, UniformBytes, StreamBytes;
    public static int CapturedWidth, CapturedHeight;

    public static Texture ReplayTarget { get; private set; }

    // Oyun kamerasinin akistaki araligi (her frame EditorApp bildirir):
    // capture yalniz bu dilimi kopyalar — editor UI pass'leri disarida kalir.
    static int _gameStart = -1, _gameEnd = -1;
    static int _gameW, _gameH;
    static uint _gameColorView;

    public static void NoteGameRange(int start, int end, Texture target)
    {
        _gameStart = start;
        _gameEnd = end;
        if (target != null)
        {
            _gameW = target.Width;
            _gameH = target.Height;
            _gameColorView = target.ColorAttachmentView;
        }
        else
        {
            _gameW = _gameH = 0;
            _gameColorView = 0;
        }
    }

    // Frame sonunda (Submit'ten sonra) cagrilir; istek varsa akisi kopyalar.
    public static void AfterSubmit(CommandBuffer cb)
    {
        if (!CaptureRequested)
            return;
        CaptureRequested = false;

        int start = 0, len = cb.Length;
        if (_gameStart >= 0 && _gameEnd > _gameStart && _gameEnd <= cb.Length)
        {
            start = _gameStart;
            len = _gameEnd - _gameStart;
        }
        if (len > _capturedCap)
        {
            if (_captured != null)
                Marshal.FreeHGlobal((IntPtr)_captured);
            _capturedCap = len;
            _captured = (byte*)Marshal.AllocHGlobal(_capturedCap);
        }
        Buffer.MemoryCopy(cb.Ptr + start, _captured, _capturedCap, len);
        _capturedLen = len;
        StreamBytes = len;
        Walk();
        // Oyun dilimi offscreen pass'tir (fbo==0 yok) — boyut RT'den gelir.
        if (CapturedWidth == 0 && _gameW > 0)
        {
            CapturedWidth = _gameW;
            CapturedHeight = _gameH;
        }
        EnsureReplayTarget();
        PatchReplayTargets();
        _replayedMaxDraws = int.MinValue; // taze capture: bir kez replay et
        Frozen = true;
        MaxDraws = DrawCount;
    }

    public static void Unfreeze() => Frozen = false;

    // Frozen'ken cagrilir ama replay yalniz GEREKTIGINDE kosar (yeni capture veya
    // MaxDraws degisti): RT arada ekran goruntusu gibi sabit durur — yakalanan
    // kaynaklar her frame yeniden bind edilmez.
    static int _replayedMaxDraws = int.MinValue;

    public static void ReplayIfFrozen()
    {
        if (!Frozen || _capturedLen == 0 || ReplayTarget == null)
            return;
        if (MaxDraws == _replayedMaxDraws)
            return;
        _replayedMaxDraws = MaxDraws;
        Sokol.RenderExecuteDbg(_captured, _capturedLen, MaxDraws,
            ReplayTarget.ColorAttachmentView, ReplayTarget.DepthAttachmentView);
    }

    static void EnsureReplayTarget()
    {
        if (CapturedWidth <= 0 || CapturedHeight <= 0)
            return;
        if (ReplayTarget != null && ReplayTarget.Width == CapturedWidth && ReplayTarget.Height == CapturedHeight)
            return;
        ReplayTarget?.Destroy();
        ReplayTarget = Texture.CreateRenderTarget(CapturedWidth, CapturedHeight);
    }

    // Yakalanan dilimdeki oyun RT'sine yazan offscreen pass'leri ReplayTarget'a
    // yonlendirir (kopya uzerinde kalici patch; canli RT frozen'ken hic yazilmaz).
    static void PatchReplayTargets()
    {
        if (ReplayTarget == null || _gameColorView == 0)
            return;
        byte* p = _captured;
        byte* end = _captured + _capturedLen;
        while (p < end)
        {
            var op = (RenderCmd)(*p++);
            switch (op)
            {
                case RenderCmd.BeginPass: p += 38; break;
                case RenderCmd.EndPass: break;
                case RenderCmd.SetPipeline: p += 4; break;
                case RenderCmd.SetBindings: p += 24; break;
                case RenderCmd.SetUniforms: { p += 4; int size = ReadI32(ref p); p += size; break; }
                case RenderCmd.SetViewport:
                case RenderCmd.SetScissor: p += 17; break;
                case RenderCmd.Draw: p += 12; break;
                case RenderCmd.DrawEx: p += 20; break;
                case RenderCmd.UploadInstances: { int size = ReadI32(ref p); p += size; break; }
                case RenderCmd.BeginPassOffscreen:
                    // payload: byte clearColor + 4xf32 + byte clearDepth + f32 = 22 bayt, sonra iki u32 view
                    if (*(uint*)(p + 22) == _gameColorView)
                    {
                        *(uint*)(p + 22) = ReplayTarget.ColorAttachmentView;
                        *(uint*)(p + 26) = ReplayTarget.DepthAttachmentView;
                    }
                    p += 30;
                    break;
                default: return; // bozuk akis
            }
        }
    }

    // --- Akis walker'i: RenderCmd formatiyla birebir (RenderCommands.cs) ---

    static void Walk()
    {
        DrawCount = PassCount = PipelineBinds = TotalInstances = 0;
        InstanceBytes = UniformBytes = 0;
        CapturedWidth = CapturedHeight = 0;

        byte* p = _captured;
        byte* end = _captured + _capturedLen;
        uint curPip = 0, curVbo = 0, curIbo = 0, curTex = 0, curSmp = 0;
        int curPass = -1;
        bool havePrev = false;
        uint prevPip = 0, prevVbo = 0, prevTex = 0, prevSmp = 0;
        int prevPass = -1;

        while (p < end)
        {
            var op = (RenderCmd)(*p++);
            switch (op)
            {
                case RenderCmd.BeginPass:
                    {
                        p += 1 + 16 + 1 + 4; // clear alanlari
                        int w = ReadI32(ref p), h = ReadI32(ref p);
                        p += 4; // sampleCount
                        uint fbo = ReadU32(ref p);
                        if (fbo == 0 && CapturedWidth == 0) { CapturedWidth = w; CapturedHeight = h; }
                        curPass = PassCount++;
                        break;
                    }
                case RenderCmd.EndPass:
                    break;
                case RenderCmd.SetPipeline:
                    curPip = ReadU32(ref p);
                    PipelineBinds++;
                    break;
                case RenderCmd.SetBindings:
                    curVbo = ReadU32(ref p);
                    p += 4 + 4; // instVbo + instOffset
                    curIbo = ReadU32(ref p);
                    curTex = ReadU32(ref p);
                    curSmp = ReadU32(ref p);
                    break;
                case RenderCmd.SetUniforms:
                    {
                        p += 4;
                        int size = ReadI32(ref p);
                        UniformBytes += size;
                        p += size;
                        break;
                    }
                case RenderCmd.SetViewport:
                case RenderCmd.SetScissor:
                    p += 17;
                    break;
                case RenderCmd.Draw:
                case RenderCmd.DrawEx:
                    {
                        p += 4; // baseElement
                        int num = ReadI32(ref p);
                        int inst = ReadI32(ref p);
                        if (op == RenderCmd.DrawEx)
                            p += 8;

                        BreakReason reason;
                        if (!havePrev) reason = BreakReason.First;
                        else if (curPass != prevPass) reason = BreakReason.NewPass;
                        else if (curPip != prevPip) reason = BreakReason.Pipeline;
                        else if (curTex != prevTex) reason = BreakReason.Texture;
                        else if (curVbo != prevVbo) reason = BreakReason.Mesh;
                        else if (curSmp != prevSmp) reason = BreakReason.Sampler;
                        else reason = BreakReason.InstanceRange;

                        if (DrawCount == Draws.Length)
                            Array.Resize(ref Draws, Draws.Length * 2);
                        Draws[DrawCount++] = new DrawEntry
                        {
                            Pass = curPass,
                            Pipeline = curPip,
                            Vbo = curVbo,
                            Ibo = curIbo,
                            TexView = curTex,
                            Sampler = curSmp,
                            NumElements = num,
                            NumInstances = inst,
                            Reason = reason,
                        };
                        TotalInstances += inst;
                        havePrev = true;
                        prevPip = curPip; prevVbo = curVbo; prevTex = curTex; prevSmp = curSmp;
                        prevPass = curPass;
                        break;
                    }
                case RenderCmd.UploadInstances:
                    {
                        int size = ReadI32(ref p);
                        InstanceBytes += size;
                        p += size;
                        break;
                    }
                case RenderCmd.BeginPassOffscreen:
                    p += 1 + 16 + 1 + 4 + 8;
                    curPass = PassCount++;
                    break;
                default:
                    return; // bozuk akis
            }
        }
    }

    static int ReadI32(ref byte* p) { int v = *(int*)p; p += 4; return v; }
    static uint ReadU32(ref byte* p) { uint v = *(uint*)p; p += 4; return v; }
}
#endif
