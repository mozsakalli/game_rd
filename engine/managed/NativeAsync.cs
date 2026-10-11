using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DigitoyEngine;

// Native'den gelen tek tamamlanma/olay (de_async.h DeCompletion). Data malloc'lu: ya
// NativeAsync.Free ile birak ya da pointer'i sahiplen (Texture pikselleri gibi).
public struct Completion
{
    public int Job, Code, A, B;
    public IntPtr Data;
    public long Len;
    public bool Ok => Code >= 0;
    public bool IsEvent => Code >= 0 && (Code & NativeAsync.EventBit) != 0; // hata (<0) her zaman son tamamlanmadir
}

// Motorun TEK async koprusu (docs/network.md). Alt sistem basina pump YOK: de_fs, de_net, platform
// SDK callback'leri hepsi native de_async kuyruguna yazar; Pump her frame BIR yerden (GameHost.Frame /
// editor PlayMode) kuyrugu bosaltir, job'a bagli Task'i (Await) ya da dinleyiciyi (Listen) cozer.
// Thread YOK: continuation'lar burada, ana thread'de, frame sinirinda kosar (AOT Task main-thread-only).
public static unsafe class NativeAsync
{
    const string Lib = "digitoyengine_native";
    public const int EventBit = 0x40000000;

    [DllImport(Lib, EntryPoint = "de_async_new_job")]
    public static extern int NewJob();

    [DllImport(Lib, EntryPoint = "de_async_complete")]
    static extern void de_async_complete(int job, int code, void* data, long len, int a, int b);
    // Testler / managed kaynakli tamamlanma (data = null).
    public static void Complete(int job, int code, int a = 0, int b = 0) => de_async_complete(job, code, null, 0, a, b);

    [DllImport(Lib, EntryPoint = "de_async_next")]
    static extern int de_async_next(int job, int* outJob, int* code, void** data, long* len, int* a, int* b);

    [DllImport(Lib, EntryPoint = "de_async_pending")]
    public static extern int NativePending();

    [DllImport(Lib, EntryPoint = "de_async_free")]
    static extern void de_async_free(void* data);
    public static void Free(IntPtr data) { if (data != IntPtr.Zero) de_async_free((void*)data); }

    static bool Next(int job, out Completion c)
    {
        int j = 0, code = 0, a = 0, b = 0; void* p = null; long l = 0;
        int r = de_async_next(job, &j, &code, &p, &l, &a, &b);
        c = new Completion { Job = j, Code = code, A = a, B = b, Data = (IntPtr)p, Len = l };
        return r == 1;
    }

    sealed class AwaitEntry
    {
        public TaskCompletionSource<Completion> Tcs;
        public CancellationTokenRegistration Reg;
    }
    sealed class ListenEntry
    {
        public Action<Completion> OnEvent;
        public CancellationTokenRegistration Reg;
    }
    sealed class FrameEntry
    {
        public TaskCompletionSource<bool> Tcs;
        public CancellationTokenRegistration Reg;
        public float Remaining; // Wait.Seconds: kalan sure; Frame.Next icin 0
    }

    static readonly Dictionary<int, AwaitEntry> _awaits = new();
    static readonly Dictionary<int, ListenEntry> _listeners = new();
    static List<FrameEntry> _frame = new(), _frameSwap = new();
    static readonly List<FrameEntry> _timers = new();

    public static int PendingCount => _awaits.Count;

    // job'un tek (son) tamamlanmasini bekler. job <= 0 -> hemen basarisiz.
    // ct iptal edilirse: kayit duser, onCancel (native iptal: de_http_cancel gibi) cagrilir, Task iptal olur;
    // native sonradan tamamlanma yazsa bile sahipsiz kalir ve Pump tamponu birakir.
    public static Task<Completion> Await(int job) => Await(job, CancellationToken.None, null);
    public static Task<Completion> Await(int job, CancellationToken ct, Action onCancel = null)
    {
        var tcs = new TaskCompletionSource<Completion>();
        if (job <= 0)
        {
            tcs.TrySetResult(new Completion { Job = job, Code = -1 });
            return tcs.Task;
        }
        if (ct.IsCancellationRequested)
        {
            onCancel?.Invoke();
            tcs.TrySetCanceled(ct);
            return tcs.Task;
        }
        var e = new AwaitEntry { Tcs = tcs };
        _awaits[job] = e;
        if (ct.CanBeCanceled)
            e.Reg = ct.Register(() =>
            {
                if (!_awaits.TryGetValue(job, out var cur) || cur != e) return; // zaten bitti
                _awaits.Remove(job);
                onCancel?.Invoke();
                tcs.TrySetCanceled(ct);
            });
        return tcs.Task;
    }

    // job'un cok-sefer olaylarini (EventBit) ve son tamamlanmasini dinler; son tamamlanmada kayit duser.
    // ct iptal edilirse: dinleme biter, onCancel cagrilir (dinleyiciye ayrica olay gelmez).
    public static void Listen(int job, Action<Completion> onEvent) => Listen(job, onEvent, CancellationToken.None, null);
    public static void Listen(int job, Action<Completion> onEvent, CancellationToken ct, Action onCancel = null)
    {
        if (job <= 0 || onEvent == null) return;
        if (ct.IsCancellationRequested) { onCancel?.Invoke(); return; }
        var e = new ListenEntry { OnEvent = onEvent };
        _listeners[job] = e;
        if (ct.CanBeCanceled)
            e.Reg = ct.Register(() =>
            {
                if (!_listeners.TryGetValue(job, out var cur) || cur != e) return;
                _listeners.Remove(job);
                onCancel?.Invoke();
            });
    }
    public static void Unlisten(int job)
    {
        if (_listeners.TryGetValue(job, out var e)) { _listeners.Remove(job); e.Reg.Dispose(); }
    }

    // Editor bloklayan bekleme (build araclari): yalniz bu job'un sonucunu kuyruktan ceker,
    // baska tamamlanmalara/continuation'lara dokunmaz. 0 = henuz yok.
    public static bool TryTake(int job, out Completion c) => Next(job, out c);

    // Bir sonraki Pump'ta tamamlanan Task: `await Frame.Next()`.
    public static Task<bool> NextFrame() => NextFrame(CancellationToken.None);
    public static Task<bool> NextFrame(CancellationToken ct)
    {
        var e = NewTimed(ct, 0f);
        if (e != null) _frame.Add(e);
        return e != null ? e.Tcs.Task : CanceledBool(ct);
    }

    // Gercek zaman (Pump dt'si, olceksiz) ile `seconds` sonra tamamlanan Task: `await Wait.Seconds(1.5f)`.
    public static Task<bool> WaitSeconds(float seconds, CancellationToken ct)
    {
        if (seconds <= 0f) return NextFrame(ct);
        var e = NewTimed(ct, seconds);
        if (e != null) _timers.Add(e);
        return e != null ? e.Tcs.Task : CanceledBool(ct);
    }

    static Task<bool> CanceledBool(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        tcs.TrySetCanceled(ct);
        return tcs.Task;
    }

    // null = ct zaten iptal. Iptal: Task iptal olur, kayit listede kalir ve Pump'ta (TrySetResult false) dusurulur.
    static FrameEntry NewTimed(CancellationToken ct, float seconds)
    {
        if (ct.IsCancellationRequested) return null;
        var e = new FrameEntry { Tcs = new TaskCompletionSource<bool>(), Remaining = seconds };
        if (ct.CanBeCanceled)
        {
            var tcs = e.Tcs;
            e.Reg = ct.Register(() => tcs.TrySetCanceled(ct));
        }
        return e;
    }

    // Her frame bir kez. Kuyrugu bosaltir, Task/dinleyici cozer, frame bekleyenleri ve suresi dolan
    // zamanlayicilari uyandirir. Continuation'lar burada kosar: yeni bekleyenler sonraki frame'e.
    public static void Pump() => Pump(0f);
    public static void Pump(float dt)
    {
        while (Next(0, out var c))
        {
            if (c.IsEvent)
            {
                if (_listeners.TryGetValue(c.Job, out var l)) l.OnEvent(c);
                else Free(c.Data);
                continue;
            }
            bool handled = false;
            if (_awaits.TryGetValue(c.Job, out var a))
            {
                _awaits.Remove(c.Job);
                a.Reg.Dispose();
                handled = true;
                a.Tcs.TrySetResult(c);
            }
            if (_listeners.TryGetValue(c.Job, out var fin))
            {
                _listeners.Remove(c.Job);
                fin.Reg.Dispose();
                handled = true;
                fin.OnEvent(c);
            }
            if (!handled)
                Free(c.Data); // sahipsiz tamamlanma (bekleyen yok): tampon sizmasin
        }

        if (_frame.Count > 0)
        {
            (_frame, _frameSwap) = (_frameSwap, _frame);
            for (int i = 0; i < _frameSwap.Count; i++)
                Fire(_frameSwap[i]);
            _frameSwap.Clear();
        }

        if (_timers.Count > 0 && dt > 0f)
        {
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                var t = _timers[i];
                if (t.Tcs.Task.IsCompleted) { _timers.RemoveAt(i); t.Reg.Dispose(); continue; } // iptal edilmis
                t.Remaining -= dt;
                if (t.Remaining > 0f) continue;
                _timers.RemoveAt(i);
                Fire(t);
            }
        }
    }

    static void Fire(FrameEntry e)
    {
        e.Reg.Dispose();
        e.Tcs.TrySetResult(true); // iptal edilmisse false doner, sessiz
    }
}

public static class Frame
{
    public static Task<bool> Next() => NativeAsync.NextFrame();
    public static Task<bool> Next(CancellationToken ct) => NativeAsync.NextFrame(ct);
}

// Zaman bekleme (olceksiz gercek zaman; Time.timeScale'den bagimsiz). Zaman asimi deseni:
// `var cts = Wait.Timeout(10f); await Http.GetAsync(url, cts.Token);`
public static class Wait
{
    public static Task<bool> Seconds(float seconds) => NativeAsync.WaitSeconds(seconds, CancellationToken.None);
    public static Task<bool> Seconds(float seconds, CancellationToken ct) => NativeAsync.WaitSeconds(seconds, ct);

    // `seconds` sonra kendini iptal eden kaynak (CancellationTokenSource.CancelAfter karsiligi; corelib'de saat yok).
    public static CancellationTokenSource Timeout(float seconds)
    {
        var cts = new CancellationTokenSource();
        _ = CancelAfter(seconds, cts);
        return cts;
    }

    // await: devam ana thread'de inline (.NET'te de). ContinueWith KULLANILMAZ: .NET'te thread pool'a gider.
    static async Task CancelAfter(float seconds, CancellationTokenSource cts)
    {
        await NativeAsync.WaitSeconds(seconds, CancellationToken.None);
        cts.Cancel();
    }
}
