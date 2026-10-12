using System.Runtime.InteropServices;

namespace MsdfPort;

// Referans: editor native DLL'i (digitoyengine_native.dll) — engine'e dokunmadan yalniz okunur.
public static unsafe class NativeReference
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib)] static extern void* de_msdf_font_open(byte* ttf, int length);
    [DllImport(Lib)] static extern void de_msdf_font_close(void* handle);
    [DllImport(Lib)]
    static extern int de_msdf_font_glyph(void* handle, int codepoint, float sdfSize, int pad, float pixelDistScale,
        double angleThreshold, byte* buffer, int bufferSize, float* out5);
    [DllImport(Lib)] static extern void* de_sdf_font_open(byte* ttf, int length);
    [DllImport(Lib)] static extern void de_sdf_font_close(void* handle);
    [DllImport(Lib)] static extern int de_sdf_font_metrics(void* handle, float sdfSize, float* out5);
    [DllImport(Lib)] static extern int de_sdf_font_kern_table(void* handle, int* codepoints, int count, short* outTable);

    public static void Configure(string dllPath)
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeReference).Assembly, (name, asm, path) =>
            name == Lib ? NativeLibrary.Load(dllPath) : IntPtr.Zero);
    }

    public sealed class Handle : IDisposable
    {
        public void* Msdf, Sdf;
        public Handle(byte[] ttf)
        {
            fixed (byte* p = ttf)
            {
                Msdf = de_msdf_font_open(p, ttf.Length);
                Sdf = de_sdf_font_open(p, ttf.Length);
            }
            if (Msdf == null || Sdf == null)
                throw new InvalidDataException("native font acilamadi");
        }

        public float[] Metrics(float sdfSize)
        {
            var o = new float[5];
            fixed (float* p = o)
                de_sdf_font_metrics(Sdf, sdfSize, p);
            return o;
        }

        public short[] KernTable(int[] cps)
        {
            var k = new short[cps.Length * cps.Length];
            fixed (int* pc = cps)
            fixed (short* pk = k)
                de_sdf_font_kern_table(Sdf, pc, cps.Length, pk);
            return k;
        }

        static readonly byte[] s_buf = new byte[(1 << 20) * 4];

        public byte[] Glyph(int cp, float sdfSize, int pad, float pds, double angle, float[] out5, out int ok)
        {
            fixed (byte* pb = s_buf)
            fixed (float* po = out5)
                ok = de_msdf_font_glyph(Msdf, cp, sdfSize, pad, pds, angle, pb, s_buf.Length, po);
            int w = (int)out5[3], h = (int)out5[4];
            if (ok == 0 || w <= 0 || h <= 0)
                return null;
            var r = new byte[w * h * 4];
            Buffer.BlockCopy(s_buf, 0, r, 0, r.Length);
            return r;
        }

        public void Dispose()
        {
            if (Msdf != null) de_msdf_font_close(Msdf);
            if (Sdf != null) de_sdf_font_close(Sdf);
            Msdf = Sdf = null;
        }
    }
}
