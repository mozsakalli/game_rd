using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DigitoyEngine;

// Motorun TEK depolama yuzeyi (engine/native/de_fs.h). Managed kod dosya sistemi
// gormez: handle + offset + uzunluk; tum IO/decode native worker'da, sonuc job
// id ile de_async kuyruguna duser ve NativeAsync.Pump'ta toplanir. Desktop/iOS stdio,
// Android AAsset, web fetch — hepsi ayni sozlesme; System.IO AOT corelib'ine girmez.
public static class NativeFs
{
    const string Lib = "digitoyengine_native";

    // Tamamlanma: Ok -> A = handle.
    [DllImport(Lib, EntryPoint = "de_fs_open")]
    public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, EntryPoint = "de_fs_size")]
    public static extern long Size(int handle);

    [DllImport(Lib, EntryPoint = "de_fs_close")]
    public static extern void Close(int handle);

    // handle < 0: path kullanilir (editor loose). length 0 = tamami. rawLength != length: zlib'li.
    [DllImport(Lib, EntryPoint = "de_fs_read")]
    public static extern int Read(int handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, long offset, long length, long rawLength);

    // Tamamlanma: Data = RGBA8 (satir 0 altta), A/B = w/h.
    [DllImport(Lib, EntryPoint = "de_fs_decode")]
    public static extern int Decode(int handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, long offset, long length, long rawLength);
}

// de_fs job'lari icin NativeAsync uzerinde ince sarmalayici (cagiranlarin bildigi Result sekli).
public static class AsyncJobs
{
    public struct Result
    {
        public bool Ok;
        public byte[] Data; // Read: baytlar; Decode: RGBA8 kopyasi istenmez (Pixels ile)
        public IntPtr Pixels;
        public int W, H;    // decode: boyut; open: W = handle
    }

    public static int PendingCount => NativeAsync.PendingCount;

    // Bayt okuma job'unu bekler; sonuc managed byte[] (native tampon serbest birakilir).
    public static async Task<byte[]> ReadAsync(int job)
    {
        var r = await Await(job, copyBytes: true);
        return r.Ok ? r.Data : null;
    }

    // Ham job bekleme (open: W = handle; decode: Pixels/W/H — cagiran kopyalayip Release eder).
    public static async Task<Result> Await(int job, bool copyBytes = false)
    {
        var c = await NativeAsync.Await(job);
        var res = new Result { Ok = c.Ok, W = c.A, H = c.B };
        if (!c.Ok)
        {
            NativeAsync.Free(c.Data);
            return res;
        }
        if (copyBytes)
        {
            int n = (int)c.Len; // AOT: long->int acik (conv.ovf yok)
            res.Data = new byte[n];
            if (n > 0)
                Marshal.Copy(c.Data, res.Data, 0, n);
            NativeAsync.Free(c.Data);
        }
        else
            res.Pixels = c.Data; // cagiran Release(pixels) ile birakir (open: veri yok)
        return res;
    }

    // Decode sonucu pikseller kopyalandiktan sonra native tampon birakilir.
    public static void Release(IntPtr pixels) => NativeAsync.Free(pixels);
}
