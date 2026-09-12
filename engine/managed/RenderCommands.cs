using System;
using System.Runtime.InteropServices;

namespace DigitoyEngine;

// C# tarafi render komut modeli (encoder). Native decoder BURADA YOK; burasi
// yalnizca frame boyunca komutlari sifir-alloc bir bayt akisina yazar. Frame
// sonunda akis TEK bir FFI ile native'e verilir (Submit -> de_sokol_render_execute),
// native taraf (sokol_shim.c) akisi gezip sokol_gfx cagrilarini yapar.
//
// ILKE:
//   - Kaynaklar (texture/pipeline/buffer) AKISTA DEGIL. Onlar dirty olunca
//     dogrudan FFI ile senkronlanir (Texture._Sync gibi) ve akisa yalnizca
//     'uint id' olarak referans verilir. Akis saf id + state + draw icerir.
//   - Boylece editor "ilk N komutu replay et" = frame capture bedava gelir.
//
// AKIS FORMATI: her komut = 1 bayt opcode + o opcode'a ozel sabit payload.
// SetUniforms'ta payload'in sonuna 'byteSize' kadar uniform verisi INLINE yazilir;
// decoder bu veriyi akis icindeki pointer'dan okur (submit boyunca gecerli).
// Bu layout, ileride yazilacak native decoder ile birebir eslesmeli.

public enum RenderCmd : byte
{
    BeginPass = 1,  // byte clearColor, f32 r,g,b,a, byte clearDepth, f32 depth, i32 w,h,sampleCount, u32 framebuffer
    EndPass = 2,    // (payload yok)
    SetPipeline = 3,  // u32 pipeline
    SetBindings = 4,  // u32 vbo, u32 instVbo, i32 instOffset, u32 ibo, u32 texView, u32 sampler
    SetUniforms = 5,  // i32 slot, i32 byteSize, [byteSize bayt inline]
    SetViewport = 6,  // i32 x,y,w,h, byte originTopLeft
    SetScissor = 7,   // i32 x,y,w,h, byte originTopLeft
    Draw = 8,         // i32 baseElement, numElements, numInstances
    DrawEx = 9,       // i32 base, num, inst, baseVertex, baseInstance
    UploadInstances = 10, // i32 byteSize, [byteSize bayt inline] — frame instance buffer'a eklenir
    BeginPassOffscreen = 11, // byte clearColor, f32 r,g,b,a, byte clearDepth, f32 depth, u32 colorView, u32 depthView
}

// Sifir-alloc komut yazici. Frame basi Begin() ile cursor sifirlanir; ayni
// unmanaged tampon tekrar kullanilir (steady-state alloc yok). Tampon yetmezse
// bir kereligine buyur (ReAlloc) — nadir.
public sealed unsafe class CommandBuffer
{
    byte* _base;
    int _pos;
    int _cap;

    public CommandBuffer(int initialCapacity = 64 * 1024)
    {
        _cap = initialCapacity;
        _base = (byte*)Marshal.AllocHGlobal(_cap);
        _pos = 0;
    }

    // Native RenderExecute'a verilecek akis ve uzunlugu.
    public byte* Ptr => _base;
    public int Length => _pos;

    // Frame basi: yaziciyi sifirla (tamponu ellemez, sadece cursor).
    public void Begin() => _pos = 0;

    // Frame sonu: akisi tek FFI ile native decoder'a yurut.
    public void Submit()
    {
        if (_pos > 0)
            Sokol.RenderExecute(_base, _pos);
    }

    // --- Komut yazicilar ---

    public void BeginPass(
        bool clearColor, float r, float g, float b, float a,
        bool clearDepth, float depthValue,
        int width, int height, int sampleCount, uint framebuffer)
    {
        EnsureSpace(1 + 1 + 16 + 1 + 4 + 12 + 4);
        Op(RenderCmd.BeginPass);
        Byte(clearColor ? (byte)1 : (byte)0);
        F32(r); F32(g); F32(b); F32(a);
        Byte(clearDepth ? (byte)1 : (byte)0);
        F32(depthValue);
        I32(width); I32(height); I32(sampleCount);
        U32(framebuffer);
    }

    public void EndPass()
    {
        EnsureSpace(1);
        Op(RenderCmd.EndPass);
    }

    // Offscreen pass: hedef = attachment view'lar (Texture.CreateRenderTarget).
    // Boyut attachment'lardan turetilir; depthView=0 => derinliksiz RT.
    public void BeginPassOffscreen(
        bool clearColor, float r, float g, float b, float a,
        bool clearDepth, float depthValue,
        uint colorView, uint depthView)
    {
        EnsureSpace(1 + 1 + 16 + 1 + 4 + 8);
        Op(RenderCmd.BeginPassOffscreen);
        Byte(clearColor ? (byte)1 : (byte)0);
        F32(r); F32(g); F32(b); F32(a);
        Byte(clearDepth ? (byte)1 : (byte)0);
        F32(depthValue);
        U32(colorView);
        U32(depthView);
    }

    public void SetPipeline(uint pipeline)
    {
        EnsureSpace(1 + 4);
        Op(RenderCmd.SetPipeline);
        U32(pipeline);
    }

    public void SetBindings(
        uint vertexBuffer, uint instanceBuffer, int instanceOffset,
        uint indexBuffer, uint textureView, uint sampler)
    {
        EnsureSpace(1 + 24);
        Op(RenderCmd.SetBindings);
        U32(vertexBuffer);
        U32(instanceBuffer);
        I32(instanceOffset);
        U32(indexBuffer);
        U32(textureView);
        U32(sampler);
    }

    // Ham uniform verisi akisa inline kopyalanir.
    public void SetUniforms(int slot, void* data, int byteSize)
    {
        EnsureSpace(1 + 8 + byteSize);
        Op(RenderCmd.SetUniforms);
        I32(slot);
        I32(byteSize);
        Bytes(data, byteSize);
    }

    // Tipli uniform yardimcisi (blittable struct).
    public void SetUniforms<T>(int slot, ref T value) where T : unmanaged
    {
        fixed (T* p = &value)
            SetUniforms(slot, p, sizeof(T));
    }

    // Bu frame'in sirali instance verisi akisa inline yazilir; decoder kendi
    // frame instance buffer'ina ekler. SetBindings'te instanceBuffer=0 (sentinel)
    // bu buffer'i, instanceOffset bu blok icindeki bayt ofsetini gosterir.
    public void UploadInstances(void* data, int byteSize)
    {
        EnsureSpace(1 + 4 + byteSize);
        Op(RenderCmd.UploadInstances);
        I32(byteSize);
        Bytes(data, byteSize);
    }

    public void SetViewport(int x, int y, int width, int height, bool originTopLeft)
    {
        EnsureSpace(1 + 16 + 1);
        Op(RenderCmd.SetViewport);
        I32(x); I32(y); I32(width); I32(height);
        Byte(originTopLeft ? (byte)1 : (byte)0);
    }

    public void SetScissor(int x, int y, int width, int height, bool originTopLeft)
    {
        EnsureSpace(1 + 16 + 1);
        Op(RenderCmd.SetScissor);
        I32(x); I32(y); I32(width); I32(height);
        Byte(originTopLeft ? (byte)1 : (byte)0);
    }

    public void Draw(int baseElement, int numElements, int numInstances)
    {
        EnsureSpace(1 + 12);
        Op(RenderCmd.Draw);
        I32(baseElement); I32(numElements); I32(numInstances);
    }

    public void DrawEx(int baseElement, int numElements, int numInstances, int baseVertex, int baseInstance)
    {
        EnsureSpace(1 + 20);
        Op(RenderCmd.DrawEx);
        I32(baseElement); I32(numElements); I32(numInstances); I32(baseVertex); I32(baseInstance);
    }

    // --- Dusuk seviyeli yazma (little-endian, hizalamasiz) ---

    void Op(RenderCmd c) => _base[_pos++] = (byte)c;
    void Byte(byte v) => _base[_pos++] = v;

    void I32(int v)
    {
        *(int*)(_base + _pos) = v;
        _pos += 4;
    }

    void U32(uint v)
    {
        *(uint*)(_base + _pos) = v;
        _pos += 4;
    }

    void F32(float v)
    {
        *(float*)(_base + _pos) = v;
        _pos += 4;
    }

    void Bytes(void* src, int n)
    {
        Buffer.MemoryCopy(src, _base + _pos, _cap - _pos, n);
        _pos += n;
    }

    void EnsureSpace(int n)
    {
        int need = _pos + n;
        if (need <= _cap)
            return;
        int newCap = _cap * 2;
        while (newCap < need)
            newCap *= 2;
        _base = (byte*)Marshal.ReAllocHGlobal((IntPtr)_base, (IntPtr)newCap);
        _cap = newCap;
    }
}
