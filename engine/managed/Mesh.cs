using System.Collections.Generic;

namespace DigitoyEngine;

// C '_engine_Mesh' karsiligi. 2D quad da 3D model de AYNI yapi: vertex + index
// buffer + index genisligi + registry slot'u (sort key'e girer). Buffer'lar
// olusturulurken dogrudan FFI ile (immutable) GPU'ya yuklenir — kaynak senkronu
// akista degil, eager yapilir.
public sealed unsafe class Mesh : GpuResource
{
    internal uint Vbuf;
    internal uint Ibuf;
    public int IndexCount { get; private set; }
    public bool Index32 { get; private set; }
    public int Slot { get; private set; }

    // CPU tarifi: evict sonrasi GPU'ya geri yukleme icin tutulur.
    Vertex[] _vertices;
    ushort[] _indices16;
    uint[] _indices32;

    // Dinamik mesh: sabit kapasiteli GPU buffer, icerik frame'de EN FAZLA BIR KEZ
    // UpdateDynamic ile yazilir (sokol kurali). CPU tarifi yok -> evict edilemez.
    bool _dynamic;
    int _vertexCapacity, _indexCapacity;
    internal int VertexCapacity => _vertexCapacity;
    internal int IndexCapacity => _indexCapacity;

    static readonly List<Mesh> _registry = new();
    static Mesh _quad;

    Mesh() { }

    public static Mesh Create(Vertex[] vertices, ushort[] indices)
    {
        var mesh = new Mesh { Index32 = false, IndexCount = indices.Length, _vertices = vertices, _indices16 = indices };
        mesh.UploadBuffers();
        Register(mesh);
        return mesh;
    }

    public static Mesh Create(Vertex[] vertices, uint[] indices)
    {
        var mesh = new Mesh { Index32 = true, IndexCount = indices.Length, _vertices = vertices, _indices32 = indices };
        mesh.UploadBuffers();
        Register(mesh);
        return mesh;
    }

    public static Mesh CreateDynamic(int vertexCapacity, int indexCapacity)
    {
        var mesh = new Mesh
        {
            Index32 = false,
            IndexCount = 0,
            _dynamic = true,
            _vertexCapacity = vertexCapacity,
            _indexCapacity = indexCapacity,
            Persistent = true, // CPU tarifi yok, evict = veri kaybi
        };
        mesh.Vbuf = Sokol.MakeBuffer(null, vertexCapacity * sizeof(Vertex), SG.BufferVertex, SG.UsageDynamic);
        mesh.Ibuf = Sokol.MakeBuffer(null, indexCapacity * sizeof(ushort), SG.BufferIndex, SG.UsageDynamic);
        Register(mesh);
        return mesh;
    }

    // Dinamik icerik yazimi (frame'de bir kez). Kapasite asimi cagiranin sorumlulugu.
    internal void UpdateDynamic(Vertex* vertices, int vertexCount, ushort* indices, int indexCount)
    {
        if (!_dynamic || Vbuf == 0)
            return;
        Sokol.UpdateBuffer(Vbuf, vertices, vertexCount * sizeof(Vertex));
        Sokol.UpdateBuffer(Ibuf, indices, indexCount * sizeof(ushort));
        IndexCount = indexCount;
        Stamp();
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
