using System;

namespace DigitoyEngine;

// DMSH: motorun TEK mesh asset formati. Importer'lar (FBX bugun, baska kaynaklar yarin)
// yalnizca bunu uretir; runtime yalnizca bunu okur (kaynak format bilgisi yok).
// Duzen: GPU'ya gidecek Vertex dizisi + 16-bit indeksler oldugu gibi (iki blok kopya = yukleme).
//
//   'DMSH' u32 | version u16 | flags u16 | vertexCount u32 | indexCount u32
//   | boundsMin 3f | boundsMax 3f | Vertex[vertexCount] (24 B) | ushort[indexCount]
//
// >65535 vertex importer'da reddedilir (parcalama yok; bilincli sinir).
public static unsafe class MeshBinary
{
    const uint Magic = 0x48534D44; // 'DMSH' little-endian
    const ushort Version = 1;
    const int HeaderSize = 4 + 2 + 2 + 4 + 4 + 12 + 12;

    public static bool IsMesh(byte[] data)
        => data != null && data.Length >= HeaderSize && BitConverter.ToUInt32(data, 0) == Magic;

    public static byte[] Write(Vertex[] vertices, ushort[] indices)
    {
        if (vertices == null || indices == null)
            throw new ArgumentNullException();
        if (vertices.Length > ushort.MaxValue + 1)
            throw new ArgumentException("DMSH: vertex sayisi 16-bit indeks sinirini asiyor (" + vertices.Length + ")");
        ComputeBounds(vertices, out var min, out var max);
        int vbytes = vertices.Length * sizeof(Vertex);
        int ibytes = indices.Length * sizeof(ushort);
        var data = new byte[HeaderSize + vbytes + ibytes];
        fixed (byte* p = data)
        {
            *(uint*)p = Magic;
            *(ushort*)(p + 4) = Version;
            *(ushort*)(p + 6) = 0;
            *(int*)(p + 8) = vertices.Length;
            *(int*)(p + 12) = indices.Length;
            float* f = (float*)(p + 16);
            f[0] = min.x; f[1] = min.y; f[2] = min.z;
            f[3] = max.x; f[4] = max.y; f[5] = max.z;
            fixed (Vertex* vp = vertices)
                Buffer.MemoryCopy(vp, p + HeaderSize, vbytes, vbytes);
            fixed (ushort* ip = indices)
                Buffer.MemoryCopy(ip, p + HeaderSize + vbytes, ibytes, ibytes);
        }
        return data;
    }

    // Gecersiz/bozuk blob = false (cagiran uyarir).
    public static bool Read(byte[] data, out Vertex[] vertices, out ushort[] indices, out Vec3 min, out Vec3 max)
    {
        vertices = null; indices = null; min = default; max = default;
        if (!IsMesh(data))
            return false;
        fixed (byte* p = data)
        {
            if (*(ushort*)(p + 4) != Version)
                return false;
            int vcount = *(int*)(p + 8);
            int icount = *(int*)(p + 12);
            if (vcount < 0 || icount < 0)
                return false;
            long need = HeaderSize + (long)vcount * sizeof(Vertex) + (long)icount * sizeof(ushort);
            if (need != data.Length)
                return false;
            float* f = (float*)(p + 16);
            min = new Vec3(f[0], f[1], f[2]);
            max = new Vec3(f[3], f[4], f[5]);
            vertices = new Vertex[vcount];
            indices = new ushort[icount];
            int vbytes = vcount * sizeof(Vertex);
            fixed (Vertex* vp = vertices)
                Buffer.MemoryCopy(p + HeaderSize, vp, vbytes, vbytes);
            fixed (ushort* ip = indices)
                Buffer.MemoryCopy(p + HeaderSize + vbytes, ip, icount * sizeof(ushort), icount * sizeof(ushort));
        }
        return true;
    }

    public static void ComputeBounds(Vertex[] vertices, out Vec3 min, out Vec3 max)
    {
        if (vertices == null || vertices.Length == 0)
        {
            min = max = default;
            return;
        }
        min = max = vertices[0].position;
        for (int i = 1; i < vertices.Length; i++)
        {
            var p = vertices[i].position;
            if (p.x < min.x) min.x = p.x; if (p.x > max.x) max.x = p.x;
            if (p.y < min.y) min.y = p.y; if (p.y > max.y) max.y = p.y;
            if (p.z < min.z) min.z = p.z; if (p.z > max.z) max.z = p.z;
        }
    }
}
