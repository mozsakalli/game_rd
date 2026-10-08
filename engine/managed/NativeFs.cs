using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DigitoyEngine;

// Motorun TEK depolama yuzeyi (engine/native/de_fs.h). Managed kod dosya sistemi
// gormez: handle + offset + uzunluk; tum IO/decode native worker'da, sonuc job
// id ile her frame AsyncJobs.Pump'ta toplanir. Desktop/iOS stdio, Android AAsset,
// web fetch — hepsi ayni sozlesme; System.IO AOT corelib'ine girmez.
public static class NativeFs
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib, EntryPoint = "de_fs_open")]
    public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, EntryPoint = "de_fs_size")]
    public static extern long Size(int handle);

    [DllImport(Lib, EntryPoint = "de_fs_close")]
    public static extern void Close(int handle);

    // handle < 0: path kullanilir (editor loose). length 0 = tamami. rawLength != length: zlib'li.
    [DllImport(Lib, EntryPoint = "de_fs_read")]
    public static extern int Read(int handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, long offset, long length, long rawLength);

    [DllImport(Lib, EntryPoint = "de_fs_decode")]
    public static extern int Decode(int handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, long offset, long length, long rawLength);

    // 0 bekliyor, 1 tamam, -1 hata. data C'de void** (P/Invoke'ta pointer; IntPtr wasm32'de 8 bayt slot/4 bayt yazim uyusmaz).
    [DllImport(Lib, EntryPoint = "de_job_poll")]
    static extern unsafe int de_job_poll(int job, void** data, long* len, int* w, int* h);
    public static unsafe int Poll(int job, out IntPtr data, out long len, out int w, out int h)
    {
        void* p = null; long l = 0; int ww = 0, hh = 0;
        int r = de_job_poll(job, &p, &l, &ww, &hh);
        data = (IntPtr)p; len = l; w = ww; h = hh;
        return r;
    }

    [DllImport(Lib, EntryPoint = "de_job_free")]
    public static extern void Free(int job);
}

// Native job -> Task koprusu + frame zamanlayicisi. Thread YOK: Pump ana thread'de
// her frame kosar, biten job'lari Task'a cevirir; continuation'lar (await devami)
// burada, ana thread'de, frame sinirinda calisir (Unity modeli; AOT/web uyumlu).
public static class AsyncJobs
{
    public struct Result
    {
        public bool Ok;
        public byte[] Data; // Read: baytlar; Decode: RGBA8 kopyasi istenmez (Pixels ile)
        public IntPtr Pixels;
        public int W, H;
    }

    sealed class Entry
    {
        public int Job;
        public bool CopyBytes;
        public TaskCompletionSource<Result> Tcs;
    }

    static readonly List<Entry> _entries = new();
    static List<TaskCompletionSource<bool>> _frame = new(), _frameSwap = new();

    public static int PendingCount => _entries.Count;

    // Bayt okuma job'unu bekler; sonuc managed byte[] (native tampon serbest birakilir).
    public static async Task<byte[]> ReadAsync(int job)
    {
        var r = await Await(job, copyBytes: true);
        return r.Ok ? r.Data : null;
    }

    // Ham job bekleme (open: W = handle; decode: Pixels/W/H — cagiran kopyalayip Release eder).
    public static Task<Result> Await(int job, bool copyBytes = false)
    {
        var tcs = new TaskCompletionSource<Result>();
        if (job < 0)
        {
            tcs.TrySetResult(new Result { Ok = false });
            return tcs.Task;
        }
        _entries.Add(new Entry { Job = job, CopyBytes = copyBytes, Tcs = tcs });
        return tcs.Task;
    }

    // Decode sonucu pikseller kopyalandiktan sonra slot serbest birakilir.
    public static void Release(int job) => NativeFs.Free(job);

    // Bir sonraki frame'de (Pump'ta) tamamlanan Task: `await Frame.Next()`.
    public static Task<bool> NextFrame()
    {
        var tcs = new TaskCompletionSource<bool>();
        _frame.Add(tcs);
        return tcs.Task;
    }

    // Her frame bir kez (asset Tick'inden once). Biten job'lari Task'a cevirir, frame
    // bekleyenleri uyandirir. Continuation'lar burada kosar: yeni bekleyenler sonraki frame'e.
    public static void Pump()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            int r = NativeFs.Poll(e.Job, out var data, out long len, out int w, out int h);
            if (r == 0)
                continue;
            _entries.RemoveAt(i);
            var res = new Result { Ok = r == 1, W = w, H = h };
            if (r == 1)
            {
                if (e.CopyBytes)
                {
                    int n = (int)len; // AOT: long->int acik (conv.ovf yok)
                    res.Data = new byte[n];
                    if (n > 0)
                        Marshal.Copy(data, res.Data, 0, n);
                    NativeFs.Free(e.Job);
                }
                else
                    res.Pixels = data; // cagiran Release(job) ile birakir (open: veri yok)
            }
            else
                NativeFs.Free(e.Job);
            e.Tcs.TrySetResult(res);
        }

        if (_frame.Count > 0)
        {
            (_frame, _frameSwap) = (_frameSwap, _frame);
            for (int i = 0; i < _frameSwap.Count; i++)
                _frameSwap[i].TrySetResult(true);
            _frameSwap.Clear();
        }
    }
}

public static class Frame
{
    public static Task<bool> Next() => AsyncJobs.NextFrame();
}
