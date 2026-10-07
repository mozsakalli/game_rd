# Dinamik Modüller (ModuleContext) — Tasarım ve Uygulama Planı

## Hedef
Normal bir proje (sahneler + behaviour'lar + asset'ler) **Build → Module** ile tek `.pak`'a publish edilir.
Release player bunu runtime'da (local/remote) yükler, start sahnesini kurar, kodunu yorumlar,
`Unload()` ile tamamen atar. Modül tarafında özel API yok; host tarafında tek yeni sınıf `Module`.

Kullanım alanı: offer diyalogları, mini oyunlar, mod benzeri küçük içerikler. Yüksek performans
hedefi yok; gameplay glue hızı yeterli.

## Sabit kararlar
| Karar | Gerekçe |
|---|---|
| Yorumlanan şey **Op IR** (`aotcompiler/source/Code.cs` OpType), CIL değil | IR zaten generic'siz, iterator/async lowering'li, tipleri çözülmüş; CIL yorumlamak frontend'i C'de yeniden yazmak olur |
| Bağlama **load zamanında, isimle, parent-first** (ModuleContext → HostContext) | Publish zamanı manifest yok; modül belirli bir player build'ine değil API yüzeyine bağlı; layout değişikliklerine dayanıklı |
| Modül **dokunduğu tüm generic somutlamaların IR'ını gömer**, host'ta varsa load'da atılır | `GetComponent<MyPanel>`, `List<MyItem>` kaçınılmaz; mekanizma tekdüze |
| Referans kapsamı: **provided** (host'ta var; engine, host oyun projesi) / **bundled** (modülle gelir) | Maven modeli; yanlış kapsam doğruluğu bozmaz, yalnız boyutu şişirir |
| Her şey **tanımlayan assembly**ye göre karar verir; sabit assembly adı yok | Oyun çok assembly'li olabilir |
| Tip adı çakışması → player build hatası (hash'e assembly adı katılmaz) | Tipi assembly'ler arası taşımak modülleri kırmasın |
| Host → modül: **trampoline**; modül → host: **imza-şekli thunk'ları**; ikisi de CTranspiler üretir | Elle yazılan ABI kodu yok |
| Unload = classloader GC'si: sahne destroy + katalog geri alma + canlı örnek sayacı 0 → free | Dangling yok, anında değil "bir sonraki GC'de" |
| Modüller arası referans **yok** (düz iki katman) | OSGi karmaşıklığından kaçın |
| Bağlama **eager** (load'da tüm semboller çözülür) | Eksik sembol diyaloğun ortasında değil load'da patlar |
| Trampoline girişi = `RtTry boundary`; sızan exception → log + `Unload` | Mod oyunu çökertmez |

## Çalışma zamanı modeli
```
ModuleContext (child, vmint.c)              HostContext (parent, AOT C)
  tipler: modülün sınıfları (dinamik Type)    export tabloları: tip/alan/slot/metot/statik
  metotlar: yorumlanan IR                     fn ptr + thunk şekli
  generic inst: gömülü IR (prefer-host)       AOT somutlamalar
          │ bulamazsa ↑ parent'a sor
```
- GC tek heap; modül nesneleri `gc_alloc(Type*)` ile, `Type.module` host için 0.
- GC yalnız safepoint'te (managed/interpreter frame yokken) adım atar → interpreter frame'leri root
  değildir; yalnız modül statikleri root (`gc_add_frame_root`). Yazımlarda `gc_write_barrier` şart.
- Exception: aynı `RtTry` setjmp/longjmp zinciri; shadow stack'e `MethodInfo` push → birleşik trace.
- Async: state machine zaten class; `IAsyncStateMachine.MoveNext` için iface trampoline'i yeter.

## Fazlar
### A — Host export tabloları ("parent loader") · vmrt.h / vmrt.c / CTranspiler
- `Type`'a append-only `module` alanı.
- `DeTypeExport` (hash, ad, Type*, alanlar: offset/tag/tip, vslot adları, struct boyutu, bayraklar),
  `DeMethodExport` (hash, ad, fn, shape), `DeFieldExport` statikler için adres, `de_host_thunks[]`.
- Tüm alanlar/metotlar (internal/private dahil). `CTranspiler.EmitModuleExports` bayrağı; player'da açık.
- Thunk: `void thunk(const void* fn, DeSlot* args, DeSlot* ret)`; şekil = C imza metni.
- Kabul: C testi `Component.Update` slotunu ve `GameObject._components` offset'ini tablodan bulur.

### B — `.dmod` formatı + `aotcompiler module` modu · ModuleWriter.cs / Program.cs
- Girdi: assembly listesi + kapsam. Kök: `Registry.RegisterAll` + bundled assembly tipleri.
- Erişilebilirlik: tanımlayan assembly bundled → yerel IR; generic somutlama → gömülü prefer-host; aksi → dış ref.
- Format: string pool · tip tanımları · metot gövdeleri (u8 opcode + operand int'ler, local tipleri,
  try tablosu, satır tablosu) · dış ref tablosu. Label'lar pc'ye çözülmüş. Stub kapısı aynen.
- Kabul: Sandbox ve selftest `.dmod`'a çevrilir; dump aracı içeriği listeler.

### C — Interpreter `vmint.c`
- Loader: parse → dış ref çöz (eager) → gömülü generic'leri host'la eşle → yerleşim → dinamik `Type`
  (generic trace, vtable = base kopyası + override trampoline'leri, itables) → statik depo + root.
- Exec: computed-goto; frame = adreslenebilir slot dizisi + operand stack; struct değerler blob.
- Çağrı: yerel → frame; dış → `de_host_thunks[shape]`; `CallVirtual` → `vtable[slot]`.
- Kabul: **selftest `--interp`**: corelib-only host + vmint, selftest `.dmod`'u .NET baseline ile birebir.

### D — Engine entegrasyonu · engine/managed/Module.cs, TypeCatalog, SceneLoader, editör
- Trampoline kök kümesi: `Component` vtable slotları + `Func<Component>`, `Action<Component,Component>`,
  `BakedReader`, `Action`, `Action<object>`, `Func<Task>`, `IAsyncStateMachine`.
- Modül tipleri için dinamik `System.Type` wrapper. `TypeCatalog(parent)` zinciri.
- `Module.LoadAsync(pak, parent)` / `Unload()`; `#if DE_AOT` vmint, değilse ALC (Faz F).
- Editör: proje ayarlarında referans listesi + kapsam; `module.build` RPC; "Build → Module".
- Kabul (dikey dilim): offer projesi — sahne, `OfferPanel : Component`, tıklamada host oyun metodu,
  kapat → `Unload`; GC sonrası `live=0`.

### E — Sağlamlaştırma
- `.dmod` sürüm başlığı; load hata raporu (eksik sembol + assembly); modül frame'leri trace'te `[mod:x]`.
- Opsiyonel deny-list (NativeFs, P/Invoke). Perf: inline cache, superinstruction (gerekirse).

### F — Editörde .NET ile modül deneme (sonra)
- Aynı `Module` API'si, child `AssemblyLoadContext` + `TypeCatalog.FromAssemblies`; bundled set ALC'ye yüklenir.

## Sıra
```
A ──┐
    ├─→ C (selftest ile doğrulanır) ──→ D ──→ E ──→ F
B ──┘
```

## Durum (uygulama günlüğü)
- **Faz A tamam**: `vmrt.h` De*Export yapıları + `Type.module/dyn_wrapper`; `CTranspiler.Exports.cs` tip/alan/slot/metot/statik
  tabloları, imza-şekli thunk'ları (`void*` normalize; 204 şekil / 1544 metot), host→modül trampoline'leri (sanal kök +
  delegate tipi başına). Host'un çağırmadığı extern'ler zayıf sembol. `tests/export_check.c` PASS. Player build export'lu derleniyor.
- **Faz B tamam**: `Primitive.Assembly`/`Code.Assembly`/`Code.Template` (frontend + generic klon), `ModuleWriter.cs` (.dmod v1),
  `aotcompiler module <çıktı> --bundled a.dll;b.dll [--provided ...]`. Selftest `AOT_MODULES=1` ile `selftest.dmod` + rapor yazar.
- **Faz C tamam**: `c_runtime/vmint.c` (+`vmint.h`). Selftest **interp yolu** (corelib-only host + vmint): 50/53 PASS = AOT ile
  birebir (3 FAIL önceden var olan corelib stub'ları). Unload: 2. GC döngüsünde `live=0`, `vmint_collect` modülü serbest bırakır.
  Hızlı döngü: `tests/interp_quick.ps1`. Op izleme: `VMINT_TRACE=1`.
- **Faz D (çekirdek) tamam**: `engine/managed/Module.cs` (`LoadAsync`/`Unload`/`TickAll`; `#if DE_AOT` vmint externleri,
  .NET'te Faz F'ye kadar açık hata), `TypeCatalog(parent)` zinciri (`Find(name)` çocuk→parent, `Find(Type)` parent→çocuklar),
  `PakSource.DepsOf/GuidOf`, `PlayerSettings.moduleHostDll`, `CatalogWriter.Write(..., ns)`, `AssetPackBuilder.Build(outPath, extra)`,
  editör `Project > Build Module` (`editor/ModuleBuilder.cs`: Roslyn → `aotcompiler module` → pak), `aotcompiler module` engine'i
  otomatik provided ekler. Player `Frame`'de `Module.TickAll()`; dev kancası: `Build/autoload.module.pak` varsa yüklenir.
  **Dikey dilim doğrulandı**: Sandbox'ın kendisi modül olarak Sandbox player'ına yüklendi (`tools/modpak` ile paketlenip);
  `RegisterAll` + baked `Read_N` okuyucuları + `Spinner.Update`/`ProgressDemo.Update`/`Awake`/ctor her frame yorumlandı.
- **Faz E (çekirdek) tamam**: trampoline sınırında izolasyon — host'ta handler varsa (RtTry / coroutine `__trypc`) exception
  yeniden fırlatılır (Task fault, SceneLoader try/catch gibi meşru yollar korunur); hiç handler yoksa (alternatif: süreç çökmesi)
  rapor + modül FAULT (no-op) → `Module.TickAll` fark edip `Unload` eder. `.dmod` v2 başlığında üretici/engine sürüm metni
  (yükleme hatalarında basılır), `vmint_load(name)` ile `[mod:ad]` trace etiketi. Selftest harness fault testi: `faulted=1`.
  **Ertelenen**: deny-list (güvenilmeyen remote modüller için pointer op / NativeFs / P-Invoke referansı reddi).
- Registry filtresi: `ModuleBuilder` yalnız modül assembly'sinin tiplerini yazar (`t.Assembly == gameAsm`); `tools/modpak`
  test yolu tam Registry.g.cs kullandığı için orada engine tipleri de girer (yalnız test).
- **Sırada**: editör menüsünden gerçek publish denemesi; Faz F (.NET/editörde ALC ile deneme); deny-list.

### Öğrenilenler (C tarafı)
- `jmp_buf` 16 hizalı olmalı (VM arena tahsisleri 16'ya yuvarlanır).
- Yakalayan try, longjmp sonrası `RtTry.prev == DIGITOYENGINE_try_top` ile bulunur (vpc-1 TryBegin değildir: throw callee'den gelebilir).
- Host sınıfından türeyen modül tipi yerleşimi parent'ın **nesne** boyutundan (export `size`) başlar, referans boyutundan değil.
- Skaler VmT'ler kanonik/paylaşımlı; parse sonrası fixup geçişi kimlik eşitliklerini garanti eder.
- Delegate hedefi yerel metot ise `VmClosure{target,m}`; host için `de_tramp_d_<Tip>`; interpreter closure'ı fn'den önce tanır.

## Riskler
| Risk | Erken sinyal | Önlem |
|---|---|---|
| Gömülü generic IR host internal alanlarına offset'le erişir | Faz C selftest (`List<T>`) | Faz A tüm alanları export eder |
| Struct by-value thunk şekilleri (Vec2, PointerEvent, TaskAwaiter) | Faz A/C | Şekil struct tipini içerir |
| İç içe interpreter girişi + longjmp | Faz C/D | Her `vmint_invoke` kendi boundary RtTry'ı |
| `Type` kimliği (modül `typeof` vs host wrapper) | Faz D | Dinamik wrapper tek instance, immortal root |
| Unload sonrası dangling delegate | Faz D | state guard + sayaç log'u |
