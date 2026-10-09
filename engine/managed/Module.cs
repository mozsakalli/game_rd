using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DigitoyEngine;

// Dinamik modul (docs/modules.md): normal bir projenin "Build > Module" ciktisi olan .pak dosyasi
// runtime'da yuklenir. Pak icinde: modulun asset'leri + pismis sahneleri + proje kaydi (.project)
// + yorumlanacak kod (Module.CodeKey, aotcompiler 'module' ciktisi) + registry giris adi (EntryKey).
// Akis: pak ac -> kendi AssetDatabase'i -> kodu vmint'e yukle (host'a isimle baglanir, cctor'lar kosar)
// -> modulun Registry.RegisterAll'i CHILD TypeCatalog'a kayit yapar (yorumlanan kod) -> start sahne
// child katalogla spawn edilir. Unload: sahne yikilir, child katalog zincirden cikar, vmint_unload
// (kalan trampoline cagrilari no-op); bellek canli nesne kalmayinca GC sonrasi serbest (TickAll toplar).
// Modul tarafinda OZEL API YOK: behaviour'lar Component sanallariyla, host'a thunk'larla konusur.
public sealed class Module
{
    public const string CodeKey = "__module/code.dmod";
    public const string EntryKey = "__module/entry"; // eski (Registry$RegisterAll); artik yazilmaz/okunmaz — kayit vmint tip listesinden (reflection)

    public string Path { get; private set; }
    public string Name { get; private set; }
    public string StartScene { get; private set; }
    public AssetDatabase Assets { get; private set; }
    public TypeCatalog Catalog { get; private set; }
    public GameObject Root { get; private set; }
    public bool IsLoaded { get; private set; }
    public string Error { get; private set; }

    PakSource _pak;
    IntPtr _vm;
    static readonly List<Module> _all = new();
    public static IReadOnlyList<Module> All => _all;

    Module() { }

    // pakPath: dosya yolu (NativeFs: desktop dosya, web fetch). parent: sahne kokleri bu transform'un altina.
    // hostCatalog: null = Scene.Active.Catalog. Hata: Error dolu, IsLoaded=false (exception firlatmaz; log'lar).
    public static async Task<Module> LoadAsync(string pakPath, Transform parent = null, TypeCatalog hostCatalog = null)
    {
        var m = new Module { Path = pakPath };
        _all.Add(m); // yukleme boyunca da TickAll asset job'larini pompalamali (SceneLoader bekler)
        try
        {
            m._pak = await PakSource.OpenAsync(pakPath);
            if (m._pak == null)
                return m.Fail("modul pak acilamadi: " + pakPath);
            m.Assets = new AssetDatabase(m._pak);
            var rec = await m._pak.ReadBytesAsync(ProjectBinary.PakKey);
            if (!ProjectBinary.TryRead(rec, out var name, out var startScene))
                return m.Fail("modul pak'ta proje kaydi yok: " + pakPath);
            m.Name = name;
            m.StartScene = startScene;
            var code = await m._pak.ReadBytesAsync(CodeKey);
            if (code == null || code.Length == 0)
                return m.Fail("modul pak'ta kod yok (" + CodeKey + "): Build > Module ile uretin");

            m.Catalog = new TypeCatalog(hostCatalog ?? Scene.Active.Catalog);
            if (!m.LoadCode(code, out var err))
                return m.Fail(err);

            if (!string.IsNullOrEmpty(m.StartScene))
            {
                var op = SceneLoader.LoadAsync(m.StartScene, m.Assets, m.Catalog, parent);
                bool ok = await op.Task;
                if (!ok)
                    return m.Fail("modul sahnesi yuklenemedi: " + op.Error);
                m.Root = op.Root;
            }
            m.IsLoaded = true;
            Console.WriteLine("[module] yuklendi: " + m.Name + " (" + pakPath + ")");
            return m;
        }
        catch (Exception e)
        {
            return m.Fail(e.GetType().Name + ": " + e.Message);
        }
    }

    Module Fail(string why)
    {
        Error = why;
        Console.WriteLine("[module] HATA: " + why);
        _all.Remove(this);
        UnloadCore();
        return this;
    }

    public void Unload()
    {
        if (!IsLoaded)
            return;
        IsLoaded = false;
        _all.Remove(this);
        if (Root != null)
            GameObject.Destroy(Root);
        Root = null;
        UnloadCore();
        Console.WriteLine("[module] unload: " + Name);
    }

    void UnloadCore()
    {
        Catalog?.Detach();
        Catalog = null;
        if (_vm != IntPtr.Zero)
        {
            UnloadVm(_vm);
            _vm = IntPtr.Zero;
        }
    }

    // Host her frame cagirir: modul asset job'lari + fault'lu modulleri kaldir + unload edilmis modullerin bellegi (GC sonrasi live==0).
    public static void TickAll()
    {
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            var m = _all[i];
            if (m.IsLoaded && m.IsFaulted)
            {
                // Yorumlanan koddan host'ta hicbir handler yokken sizan exception: vmint modulu no-op'a aldi, rapor stderr'de.
                Console.WriteLine("[module] FAULT -> unload: " + m.Name);
                m.Error = "modul kodu yakalanmamis exception firlatti (ayrintilar stderr: [vmint] FAULT)";
                m.Unload();
                continue;
            }
            if (m.Assets != null)
                m.Assets.Tick();
        }
        CollectVm();
    }

    bool IsFaulted => _vm != IntPtr.Zero && VmFaulted(_vm) != 0;

#if DE_AOT
    const string Lib = "digitoyengine_native"; // AOT: semboller statik (c_runtime/vmint.c); lib adi yok sayilir

    // NOT: handle'lar C'de pointer; P/Invoke imzalarinda IntPtr DEGIL void* kullanilir (AOT IR'de IntPtr = 64-bit long;
    // wasm32'de pointer 32-bit -> wasm-ld imza uyusmazligi/trap). IntPtr yalniz managed yuzeyde, cast ile.
    [DllImport(Lib, EntryPoint = "vmint_load")]
    static extern unsafe void* vmint_load(byte* data, int len, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte* err, int errcap);
    [DllImport(Lib, EntryPoint = "vmint_unload")]
    static extern unsafe void vmint_unload(void* m);
    [DllImport(Lib, EntryPoint = "vmint_collect")]
    static extern int VmCollect();
    [DllImport(Lib, EntryPoint = "vmint_faulted")]
    static extern unsafe int vmint_faulted(void* m);
    [DllImport(Lib, EntryPoint = "vmint_type_count")]
    static extern unsafe int vmint_type_count(void* m);
    [DllImport(Lib, EntryPoint = "vmint_type_at")]
    static extern unsafe Type vmint_type_at(void* m, int i);
    static unsafe IntPtr VmLoad(byte* data, int len, string name, byte* err, int errcap) => (IntPtr)vmint_load(data, len, name, err, errcap);
    static unsafe void VmUnload(IntPtr m) => vmint_unload((void*)m);
    static unsafe int VmFaulted(IntPtr m) => vmint_faulted((void*)m);

    // Modul kodu yuklenir; modulun somut Component tipleri (vmint tip listesi, tek meta) child kataloga REFLECTION ile kaydedilir —
    // host'un kendi tipleri icin yaptiginin aynisi (TypeCatalog.RegisterReflective). Uretilmis kayit kodu / giris metodu yok.
    unsafe bool LoadCode(byte[] code, out string error)
    {
        var err = new byte[512];
        fixed (byte* p = code)
        fixed (byte* e = err)
            _vm = VmLoad(p, code.Length, Name ?? "module", e, err.Length);
        if (_vm == IntPtr.Zero)
        {
            int n = 0;
            while (n < err.Length && err[n] != 0) n++;
            error = "modul kodu yuklenemedi: " + System.Text.Encoding.UTF8.GetString(err, 0, n);
            return false;
        }
        int count = vmint_type_count((void*)_vm), registered = 0;
        for (int i = 0; i < count; i++)
        {
            var t = vmint_type_at((void*)_vm, i);
            if (t == null || t.IsAbstract || !typeof(Component).IsAssignableFrom(t) || t == typeof(Transform))
                continue;
            Catalog.RegisterReflective(t);
            registered++;
        }
        Console.WriteLine("[module] " + registered + " component tipi kaydedildi (reflection)");
        error = null;
        return true;
    }

    static void UnloadVm(IntPtr vm) => VmUnload(vm);
    static void CollectVm() => VmCollect();
#else
    // .NET (editor/dev player): yorumlayici yok. Faz F: bundled assembly'ler child AssemblyLoadContext'e
    // yuklenip ayni Module API'siyle calisacak. Simdilik acik hata.
    bool LoadCode(byte[] code, out string error)
    {
        error = "Module: .NET host'ta modul yukleme henuz yok (yalniz AOT player; docs/modules.md Faz F)";
        return false;
    }
    static void UnloadVm(IntPtr vm) { }
    static void CollectVm() { }
    static int VmFaulted(IntPtr vm) => 0;
#endif
}
