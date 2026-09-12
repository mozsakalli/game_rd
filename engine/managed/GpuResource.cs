using System;

namespace DigitoyEngine;

// Yuklenebilir asset sozlesmesi: Name = AssetDatabase anahtari (kok'e goreli yol).
// Serilestirme (Kind.Asset) ve editor drag-drop tip bilmeden bunun uzerinden calisir.
public interface IAsset
{
    string Name { get; }
}

// Unity Object modeli: IDisposable YOK, finalizer YOK (GC thread'inden sokol
// cagrilamaz). GPU kaynaklari kayit defterindedir; kullanim DrawMesh/_Sync
// noktasinda damgalanir (LastUsedFrame). Temizlik yalniz belirli anlarda
// (sahne gecisi / elle Collect) kosar — frame ortasinda asla.
public abstract class GpuResource : IAsset
{
    public string Name { get; set; }

    // Toplama politikasi disinda tutar (font atlasi, RT gibi).
    public bool Persistent;

    internal int LastUsedFrame;
    internal int _resSlot = -1;

    protected GpuResource() => Resources.Register(this);

    internal void Stamp() => LastUsedFrame = Time.frameCount;

    // "Yakinda lazim, ama su an cizilmiyor" — damgayi elle tazeler.
    public void KeepAlive() => Stamp();

    // GPU tarafi birakilinca tarif (CPU verisi) uzerinden geri yuklenebilir mi?
    internal abstract bool CanEvict { get; }

    // Yalniz GPU nesnelerini birakir; tarif kalir, sonraki kullanimda geri yukler.
    internal abstract void ReleaseGpu();

    // Tam yok etme: GPU + kayit. Turevler CPU tarifini de burada birakir.
    // GPU tarafi HEMEN birakilmaz: bu frame'in kuyruklari/encode'u ve frozen
    // capture ham handle'lari referansliyor olabilir — birakma sonraki frame
    // basina (Resources.Tick) ertelenir, frozen'ken bekletilir.
    public virtual void Destroy()
    {
        if (_resSlot < 0)
            return; // zaten yok edildi
        Resources.Unregister(this);
        Resources.DeferRelease(this);
    }
}

// Kayit defteri + toplayici. Otomatik sweep YOK: Collect yalniz sahne gecisi
// gibi dogal duraklarda kosar (yavas cihazda oyun ici evict/reload hickirigi
// yasanmasin). Scene.Unload birkac frame sonrasina ertelenmis Collect planlar
// (hemen toplansa diger sahnelerin 1 frame onceki damgalari da suprulurdu).
public static class Resources
{
    static GpuResource[] _all = new GpuResource[256];
    static int _count;
    static int _collectAtFrame = -1;

    internal static void Register(GpuResource r)
    {
        if (_count == _all.Length)
            Array.Resize(ref _all, _count * 2);
        _all[_count] = r;
        r._resSlot = _count++;
    }

    internal static void Unregister(GpuResource r)
    {
        int i = r._resSlot;
        if (i < 0)
            return;
        r._resSlot = -1;
        var last = _all[--_count];
        _all[_count] = null;
        if (last != r)
        {
            _all[i] = last;
            last._resSlot = i;
        }
    }

    // maxAgeFrames'ten uzun suredir kullanilmayan, geri yuklenebilir kaynaklarin
    // GPU tarafini birakir. Nesne gecerli kalir; sonraki kullanimda geri yukler.
    public static void Collect(int maxAgeFrames = 3)
    {
#if DE_EDITOR
        if (RenderDebug.Frozen)
            return; // frozen capture eski handle'lari replay eder — silme yasak
#endif
        int now = Time.frameCount;
        for (int i = 0; i < _count; i++)
        {
            var r = _all[i];
            if (!r.Persistent && r.CanEvict && now - r.LastUsedFrame > maxAgeFrames)
                r.ReleaseGpu();
        }
    }

    internal static void ScheduleCollect(int delayFrames) => _collectAtFrame = Time.frameCount + delayFrames;

    // Destroy edilen kaynaklarin GPU tarafi buraya birikir; frame basinda
    // (onceki frame'in stream'i calistiktan sonra) topluca birakilir.
    static GpuResource[] _pendingRelease = new GpuResource[64];
    static int _pendingCount;

    internal static void DeferRelease(GpuResource r)
    {
        if (_pendingCount == _pendingRelease.Length)
            Array.Resize(ref _pendingRelease, _pendingCount * 2);
        _pendingRelease[_pendingCount++] = r;
    }

    static void FlushReleases()
    {
#if DE_EDITOR
        if (RenderDebug.Frozen)
            return; // frozen capture ham handle'lari replay eder — silme yasak
#endif
        for (int i = 0; i < _pendingCount; i++)
        {
            _pendingRelease[i].ReleaseGpu();
            _pendingRelease[i] = null;
        }
        _pendingCount = 0;
    }

    // Her frame Scene.UpdateAll'dan cagrilir; ertelenmis birakma + planlanmis toplama.
    internal static void Tick()
    {
        FlushReleases();
        if (_collectAtFrame < 0 || Time.frameCount < _collectAtFrame)
            return;
        _collectAtFrame = -1;
        Collect();
    }
}
