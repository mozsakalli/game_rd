using System;
using System.Runtime.InteropServices;

namespace DigitoyEngine;

// C engine.c'deki cizim kuyrugu + engine_flush karsiligi (managed beyin).
// Frame boyunca DrawMesh cagrilarini toplar; her cizim = Mesh + per-instance
// (model mat4 + uvRect + tint). Flush'ta item'lar 64-bit sort key ile siralanir,
// sirali instance verisi CommandBuffer'a inline yazilir ve (pip,mesh,tex,sampler)
// ayni olanlar TEK instanced Draw'a batch'lenir. Sokol cagrisi YOK; her sey
// CommandBuffer'a encode edilir, tek RenderExecute ile native'e gider.
//
// Kaynak senkronu (texture) burada eager yapilir (akista degil).
public sealed unsafe class RenderQueue
{
    struct DrawItem
    {
        public ulong Key;
        public int Instance;   // _instances icindeki indeks
        public ushort Mesh;    // mesh registry slot
        public ushort Pip;     // PipelineCache slot
        public uint TexView;
        public SamplerType Sampler;
    }

    Instance* _instances;  // cizim sirasinda biriken (unsorted)
    Instance* _upload;     // sort sonrasi sirali kopya
    DrawItem[] _items;
    DrawItem[] _scratch;   // radix sort ping-pong tamponu
    int _count;
    int _capacity;

    // Sampler nesneleri global; ilk kullanimda kurulur (sokol setup sonrasi).
    static uint _linearSampler;
    static uint _nearestSampler;
    internal static uint LinearSampler => _linearSampler != 0 ? _linearSampler
        : _linearSampler = Sokol.MakeSampler(
            SG.FilterLinear, SG.FilterLinear, SG.FilterDefault,
            SG.WrapClampToEdge, SG.WrapClampToEdge, SG.WrapClampToEdge, SG.CompareFuncDefault);
    internal static uint NearestSampler => _nearestSampler != 0 ? _nearestSampler
        : _nearestSampler = Sokol.MakeSampler(
            SG.FilterNearest, SG.FilterNearest, SG.FilterDefault,
            SG.WrapClampToEdge, SG.WrapClampToEdge, SG.WrapClampToEdge, SG.CompareFuncDefault);

    public RenderQueue(int capacity = 4096)
    {
        _capacity = capacity < 16 ? 16 : capacity;
        _instances = (Instance*)Marshal.AllocHGlobal(sizeof(Instance) * _capacity);
        _upload = (Instance*)Marshal.AllocHGlobal(sizeof(Instance) * _capacity);
        _items = new DrawItem[_capacity];
        _scratch = new DrawItem[_capacity];
    }

    // Frame basi: kuyrugu sifirla (tamponlar yeniden kullanilir).
    public void Begin() => _count = 0;

    // Biriken draw sayisi (testler quad emisyonunu dogrular).
    internal int Count => _count;

    public void DrawMesh(
        Mesh mesh, Material material, in Mat4 model, Color tint,
        float u0, float v0, float u1, float v1, int layer)
        => DrawMesh(mesh, material, in model, tint, tint, tint, tint, default, u0, v0, u1, v1, layer);

    // Kose renkleri (uv uzayinda 0=(0,0) 1=(1,0) 2=(1,1) 3=(0,1)) + USER parametresi.
    public void DrawMesh(
        Mesh mesh, Material material, in Mat4 model,
        Color tint0, Color tint1, Color tint2, Color tint3, in Vec4 user,
        float u0, float v0, float u1, float v1, int layer)
    {
        if (mesh == null || material == null)
            return;

        mesh._Sync();                  // damga + evict edildiyse geri yukleme
        material.MainTexture?._Sync(); // kaynak eager senkron (akista degil) + damga

        EnsureCapacity(_count + 1);
        int idx = _count++;

        Instance* inst = &_instances[idx];
        fixed (float* mp = model.m)
            for (int k = 0; k < 16; k++)
                inst->model[k] = mp[k];
        inst->uvRect[0] = u0;
        inst->uvRect[1] = v0;
        inst->uvRect[2] = u1 - u0;
        inst->uvRect[3] = v1 - v0;
        inst->tint0 = tint0;
        inst->tint1 = tint1;
        inst->tint2 = tint2;
        inst->tint3 = tint3;
        inst->user[0] = user.x; // buffer reuse: her alan acikca yazilir (bayat deger kalmasin)
        inst->user[1] = user.y;
        inst->user[2] = user.z;
        inst->user[3] = user.w;

        int pip = PipelineCache.GetPipeline(material, mesh.Index32);
        uint texView = material.MainTexture != null ? material.MainTexture.TextureView : 0u;

        // Transparent/Ui: ayni layer'da SUBMIT sirasi korunur (painter; Unity paritesi).
        // State tie-break'leri (pip/mesh/tex) key'e girmez — handle numaralari rastgele
        // oldugundan siralamayi bozar (pip<<40 Ui layer bitleriyle de CAKISIR); bitisik
        // ayni-state run'lar radix STABLE oldugu icin yine tek batch'e merge olur.
        // Diger modlarda state gruplama kalir.
        var mode = material.SortMode;
        ulong key = MakeSortKey(mode, material, mesh, layer);
        if (mode != SortMode.Transparent && mode != SortMode.Ui)
            key |= ((ulong)(byte)pip << 40) | (texView & 0xFFFFFFu);

        _items[idx] = new DrawItem
        {
            Instance = idx,
            Mesh = (ushort)mesh.Slot,
            Pip = (ushort)pip,
            TexView = texView,
            Sampler = material.SamplerType,
            Key = key,
        };
    }

    // engine_flush: sirala, sirali instance'lari akisa yaz, batch'leyip encode et.
    public void Flush(CommandBuffer cb, ref Mat4 viewProj)
    {
        int count = _count;
        _count = 0;
        if (count <= 0)
            return;

        RadixSort(count);
        for (int i = 0; i < count; i++)
            _upload[i] = _instances[_items[i].Instance];

        cb.UploadInstances(_upload, count * sizeof(Instance));

        int curPip = -1;
        int p = 0;
        while (p < count)
        {
            DrawItem head = _items[p];
            int q = p + 1;
            while (q < count &&
                   _items[q].Pip == head.Pip &&
                   _items[q].Mesh == head.Mesh &&
                   _items[q].TexView == head.TexView &&
                   _items[q].Sampler == head.Sampler)
            {
                q++;
            }

            Mesh mesh = Mesh.FromSlot(head.Mesh);
            if (mesh != null)
            {
                if (curPip != head.Pip)
                {
                    cb.SetPipeline(PipelineCache.PipelineId(head.Pip));
                    cb.SetUniforms(0, ref viewProj);
                    curPip = head.Pip;
                }
                uint sampler = head.Sampler == SamplerType.Nearest ? NearestSampler : LinearSampler;
                // instanceBuffer=0: frame instance buffer (decoder-owned); offset bu blok icinde.
                cb.SetBindings(mesh.Vbuf, 0u, p * sizeof(Instance), mesh.Ibuf, head.TexView, sampler);
                cb.Draw(0, mesh.IndexCount, q - p);
            }
            p = q;
        }
    }

    // engine.c _engine_make_sort_key ile birebir; ek olarak kova (SortMode) en ust
    // bitlerde: Unity'deki renderQueue gibi kovalar numara sirasiyla cizilir
    // (Opaque -> Transparent -> Ui), kova ici duzen mode'a ozel layout ile.
    static ulong MakeSortKey(SortMode mode, Material mat, Mesh mesh, int layer)
    {
        ulong bucket = (ulong)(byte)mode << 60;
        ulong baseL = (ulong)(ushort)layer << 32;
        ulong materialKey = (ulong)(ushort)((int)mat.CullMode
                          | ((mat.DepthTest ? 1 : 0) << 2)
                          | ((mat.DepthWrite ? 1 : 0) << 3));
        ulong meshKey = (ulong)(ushort)mesh.Slot << 16;

        return bucket | mode switch
        {
            SortMode.Transparent => baseL, // yalniz layer: ayni layer = submit sirasi
            SortMode.Ui => baseL | (materialKey << 8) | meshKey,
            SortMode.Painter => ((ulong)(ushort)layer << 48) | (materialKey << 16) | meshKey,
            _ => meshKey | (materialKey << 8) | baseL, // Opaque / None
        };
    }

    void EnsureCapacity(int needed)
    {
        if (needed <= _capacity)
            return;
        int newCap = _capacity * 2;
        while (newCap < needed)
            newCap *= 2;
        _instances = (Instance*)Marshal.ReAllocHGlobal((IntPtr)_instances, (IntPtr)(sizeof(Instance) * newCap));
        _upload = (Instance*)Marshal.ReAllocHGlobal((IntPtr)_upload, (IntPtr)(sizeof(Instance) * newCap));
        Array.Resize(ref _items, newCap);
        Array.Resize(ref _scratch, newCap);
        _capacity = newCap;
    }

    // 64-bit anahtar uzerinde 8x8-bit LSD radix sort (kararli, alloc'suz).
    // qsort'un sanal/indirek karsilastirmasi yok; ping-pong _items <-> _scratch.
    // 8 gecis cift oldugu icin sonuc yine _items'ta kalir.
    void RadixSort(int n)
    {
        DrawItem[] a = _items;
        DrawItem[] b = _scratch;
        int* buckets = stackalloc int[256];
        for (int shift = 0; shift < 64; shift += 8)
        {
            for (int i = 0; i < 256; i++)
                buckets[i] = 0;
            for (int i = 0; i < n; i++)
                buckets[(int)((a[i].Key >> shift) & 0xFF)]++;
            int sum = 0;
            for (int i = 0; i < 256; i++)
            {
                int c = buckets[i];
                buckets[i] = sum;
                sum += c;
            }
            for (int i = 0; i < n; i++)
            {
                int bucket = (int)((a[i].Key >> shift) & 0xFF);
                b[buckets[bucket]++] = a[i];
            }
            DrawItem[] tmp = a;
            a = b;
            b = tmp;
        }
    }
}
