using System.Collections.Generic;

namespace DigitoyEngine;

// C '_engine_Mesh' karsiligi. 2D quad da 3D model de AYNI yapi: vertex + index
// buffer + index genisligi + registry slot'u (sort key'e girer). Buffer'lar
// olusturulurken dogrudan FFI ile (immutable) GPU'ya yuklenir — kaynak senkronu
// akista degil, eager yapilir.
//
// IKI OMUR, TEK SINIF:
//   Statik  = asset (DMSH artifact / kod): CPU tarifi tutulur, evict -> geri yukleme.
//   Dinamik = runtime (Spine, trail, prosedurel): sabit kapasiteli dynamic buffer,
//             frame'de EN FAZLA BIR update (sokol kurali), serilesmez, evict edilmez.
// Vertex duzeni her iki omurde ayni (pos/uv/color) — motor tek yapi bilir.
public sealed unsafe class Mesh : GpuResource
{
    internal uint Vbuf;
    internal uint Ibuf;
    public int IndexCount { get; private set; }
    public int VertexCount { get; private set; }
    public bool Index32 { get; private set; }
    public int Slot { get; private set; }

    // Lokal AABB (statikte DMSH/tariften; dinamikte cagiran SetBounds ile verir).
    public Vec3 BoundsMin { get; private set; }
    public Vec3 BoundsMax { get; private set; }
    public bool HasBounds { get; private set; }

    // CPU tarifi: evict sonrasi GPU'ya geri yukleme icin tutulur.
    Vertex[] _vertices;
    ushort[] _indices16;
    uint[] _indices32;

    // Dinamik mesh: sabit kapasiteli GPU buffer, icerik frame'de EN FAZLA BIR KEZ
    // UpdateDynamic ile yazilir (sokol kurali). CPU tarifi yok -> evict edilemez.
    bool _dynamic;
    int _vertexCapacity, _indexCapacity;
    int _lastUpdateFrame = -1;
    public bool IsDynamic => _dynamic;
    public int VertexCapacity => _vertexCapacity;
    public int IndexCapacity => _indexCapacity;

    static readonly List<Mesh> _registry = new();
    static Mesh _quad;

    Mesh() { }

    public static Mesh Create(Vertex[] vertices, ushort[] indices)
    {
        var mesh = new Mesh { Index32 = false, IndexCount = indices.Length, VertexCount = vertices.Length, _vertices = vertices, _indices16 = indices };
        MeshBinary.ComputeBounds(vertices, out var mn, out var mx);
        mesh.SetBounds(mn, mx);
        mesh.UploadBuffers();
        Register(mesh);
        return mesh;
    }

    public static Mesh Create(Vertex[] vertices, uint[] indices)
    {
        var mesh = new Mesh { Index32 = true, IndexCount = indices.Length, VertexCount = vertices.Length, _vertices = vertices, _indices32 = indices };
        MeshBinary.ComputeBounds(vertices, out var mn, out var mx);
        mesh.SetBounds(mn, mx);
        mesh.UploadBuffers();
        Register(mesh);
        return mesh;
    }

    // DMSH blobundan (importer artifact / pak). Gecersiz blob = null.
    public static Mesh FromArtifact(byte[] blob, string name)
    {
        if (!MeshBinary.Read(blob, out var v, out var idx, out var mn, out var mx))
            return null;
        var mesh = new Mesh { Index32 = false, IndexCount = idx.Length, VertexCount = v.Length, _vertices = v, _indices16 = idx, Name = name };
        mesh.SetBounds(mn, mx);
        mesh.UploadBuffers();
        Register(mesh);
        return mesh;
    }

    // Reimport: ayni instance yeni icerikle (eski GPU buffer'lar frame sonunda birakilir).
    internal bool Reload(byte[] blob)
    {
        if (_dynamic || !MeshBinary.Read(blob, out var v, out var idx, out var mn, out var mx))
            return false;
        Resources.DeferDestroyBuffer(Vbuf);
        Resources.DeferDestroyBuffer(Ibuf);
        Vbuf = 0; Ibuf = 0;
        Index32 = false;
        _vertices = v; _indices16 = idx; _indices32 = null;
        VertexCount = v.Length; IndexCount = idx.Length;
        SetBounds(mn, mx);
        UploadBuffers();
        return true;
    }

    public static Mesh CreateDynamic(int vertexCapacity, int indexCapacity)
    {
        var mesh = new Mesh
        {
            Index32 = false,
            IndexCount = 0,
            _dynamic = true,
            Persistent = true, // CPU tarifi yok, evict = veri kaybi
        };
        mesh.AllocDynamic(vertexCapacity, indexCapacity);
        Register(mesh);
        return mesh;
    }

    void AllocDynamic(int vertexCapacity, int indexCapacity)
    {
        _vertexCapacity = vertexCapacity < 4 ? 4 : vertexCapacity;
        _indexCapacity = indexCapacity < 6 ? 6 : indexCapacity;
        Vbuf = Sokol.MakeBuffer(null, _vertexCapacity * sizeof(Vertex), SG.BufferVertex, SG.UsageDynamic);
        Ibuf = Sokol.MakeBuffer(null, _indexCapacity * sizeof(ushort), SG.BufferIndex, SG.UsageDynamic);
    }

    // Kapasiteyi garanti eder (buyutme = yeni buffer; eskisi frame sonunda birakilir —
    // bu frame'in akisi/frozen capture eski handle'i referansliyor olabilir).
    public void EnsureDynamicCapacity(int vertexCount, int indexCount)
    {
        if (!_dynamic || (vertexCount <= _vertexCapacity && indexCount <= _indexCapacity))
            return;
        int vc = _vertexCapacity, ic = _indexCapacity;
        while (vc < vertexCount) vc *= 2;
        while (ic < indexCount) ic *= 2;
        if (vc > ushort.MaxValue + 1)
            vc = ushort.MaxValue + 1; // 16-bit indeks siniri; asan yazim UpdateDynamic'te kirpilir
        Resources.DeferDestroyBuffer(Vbuf);
        Resources.DeferDestroyBuffer(Ibuf);
        AllocDynamic(vc, ic);
    }

    // Dinamik icerik yazimi: frame'de EN FAZLA BIR KEZ (ikinci cagri yok sayilir + log).
    // Kapasite asimi kirpilir (cagiran EnsureDynamicCapacity ile buyutur). Bounds verilmez;
    // picking/secim icin SetBounds ayrica cagrilir.
    public void UpdateDynamic(Vertex* vertices, int vertexCount, ushort* indices, int indexCount)
    {
        if (!_dynamic || Vbuf == 0)
            return;
        if (_lastUpdateFrame == Time.frameCount)
        {
            Debug.LogWarning("[mesh] dinamik mesh ayni frame'de ikinci kez yazildi (yok sayildi)");
            return;
        }
        _lastUpdateFrame = Time.frameCount;
        if (vertexCount > _vertexCapacity) vertexCount = _vertexCapacity;
        if (indexCount > _indexCapacity) indexCount = _indexCapacity;
        if (vertexCount > 0)
            Sokol.UpdateBuffer(Vbuf, vertices, vertexCount * sizeof(Vertex));
        if (indexCount > 0)
            Sokol.UpdateBuffer(Ibuf, indices, indexCount * sizeof(ushort));
        VertexCount = vertexCount;
        IndexCount = indexCount;
        Stamp();
    }

    public void UpdateDynamic(Vertex[] vertices, int vertexCount, ushort[] indices, int indexCount)
    {
        fixed (Vertex* vp = vertices)
        fixed (ushort* ip = indices)
            UpdateDynamic(vp, vertexCount, ip, indexCount);
    }

    public void SetBounds(Vec3 min, Vec3 max)
    {
        BoundsMin = min;
        BoundsMax = max;
        HasBounds = true;
    }

    void UploadBuffers()
    {
        fixed (Vertex* vp = _vertices)
            Vbuf = Sokol.MakeBuffer(vp, _vertices.Length * sizeof(Vertex), SG.BufferVertex, SG.UsageImmutable);
        if (Index32)
        {
            fixed (uint* ip = _indices32)
                Ibuf = Sokol.MakeBuffer(ip, _indices32.Length * sizeof(uint), SG.BufferIndex, SG.UsageImmutable);
        }
        else
        {
            fixed (ushort* ip = _indices16)
                Ibuf = Sokol.MakeBuffer(ip, _indices16.Length * sizeof(ushort), SG.BufferIndex, SG.UsageImmutable);
        }
    }

    // Kullanim damgasi + evict edildiyse GPU'ya geri yukleme (DrawMesh'ten cagrilir).
    internal void _Sync()
    {
        Stamp();
        if (Vbuf == 0 && _vertices != null)
            UploadBuffers();
    }

    internal override bool CanEvict => _vertices != null;

    internal override void ReleaseGpu()
    {
        if (Vbuf != 0) { Sokol.DestroyBuffer(Vbuf); Vbuf = 0; }
        if (Ibuf != 0) { Sokol.DestroyBuffer(Ibuf); Ibuf = 0; }
    }

    public override void Destroy()
    {
        base.Destroy();
        _vertices = null;
        _indices16 = null;
        _indices32 = null;
        if (Slot >= 0 && Slot < _registry.Count && _registry[Slot] == this)
            _registry[Slot] = null;
        Slot = -1;
    }

    // Lokal AABB ile isin kesisimi (slab). Donus: giris mesafesi (isin icerideyse 0).
    public bool RayIntersectsBounds(in Vec3 origin, in Vec3 dir, out float t)
    {
        t = 0;
        if (!HasBounds)
            return false;
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        if (!Slab(origin.x, dir.x, BoundsMin.x, BoundsMax.x, ref tmin, ref tmax)) return false;
        if (!Slab(origin.y, dir.y, BoundsMin.y, BoundsMax.y, ref tmin, ref tmax)) return false;
        if (!Slab(origin.z, dir.z, BoundsMin.z, BoundsMax.z, ref tmin, ref tmax)) return false;
        if (tmax < 0)
            return false;
        t = tmin < 0 ? 0 : tmin;
        return true;
    }

    static bool Slab(float o, float d, float lo, float hi, ref float tmin, ref float tmax)
    {
        if (d > -1e-8f && d < 1e-8f)
            return o >= lo && o <= hi;
        float inv = 1f / d;
        float t0 = (lo - o) * inv, t1 = (hi - o) * inv;
        if (t0 > t1) { (t0, t1) = (t1, t0); }
        if (t0 > tmin) tmin = t0;
        if (t1 < tmax) tmax = t1;
        return tmin <= tmax;
    }

    // Birim kare, merkez (0,0): 2D sprite ve 3D billboard ayni mesh (engine.c _engine_make_quad).
    public static Mesh Quad()
    {
        if (_quad != null)
            return _quad;
        var white = new Color(255, 255, 255, 255);
        var v = new Vertex[]
        {
            new(new Vec3(-0.5f, -0.5f, 0f), new Vec2(0f, 1f), white),
            new(new Vec3( 0.5f, -0.5f, 0f), new Vec2(1f, 1f), white),
            new(new Vec3( 0.5f,  0.5f, 0f), new Vec2(1f, 0f), white),
            new(new Vec3(-0.5f,  0.5f, 0f), new Vec2(0f, 0f), white),
        };
        ushort[] idx = { 0, 1, 2, 0, 2, 3 };
        _quad = Create(v, idx);
        _quad.Persistent = true; // motor cekirdegi kullanir (GUI dahil), toplama disi
        return _quad;
    }

    static void Register(Mesh mesh)
    {
        mesh.Slot = _registry.Count;
        _registry.Add(mesh);
    }

    // Sort key'deki mesh slot'undan mesh'i cozer (flush sirasinda).
    internal static Mesh FromSlot(int slot)
        => (slot >= 0 && slot < _registry.Count) ? _registry[slot] : null;
}
