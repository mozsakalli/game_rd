# Registry'nin Kaldırılması — Reflection Tabanlı Tek Katalog

## Neden
Android player build'inde `generated.c` (28 MB, 306k satır) tek çeviri birimi olarak NDK clang'de **984 s** sürüyor;
diğer tüm dosyalar toplam ~15 s. Profil: 2.000 satırı aşan 6 C fonksiyonunun **tamamı** `Registry.Reg_*`
(`CatalogWriter` çıktısı; `Reg_4` = `ParticleSystem`, 230 alan → 19.796 satır C, 1.782 yerel, 257 delegate).
LLVM geçişleri fonksiyon boyutunda süper-lineer; `-O1` bile kurtarmıyor. Üstelik release'te `generated.c` için
`-O1` override'ı var (CMakeLists) — release ilkemize aykırı.

Daha derin sorun: `Registry.g.cs`, editörün **veri modelini** (`SerializedType.FieldSchema`: Inspector, Undo,
prefab diff için gereken her şey) AOT'a kod olarak döküyor; release'te bunun yalnız iki parçası okunuyor
(`SchemaHash`, `RemapRefs`). Registry'nin "AOT'ta reflection yok" varsayımı artık geçerli değil: runtime her tip
için `DigitoyEngineMember` tablosu (ad, tip, **offset**, size, tag) emit ediyor (`vmrt.h`, `CTranspiler.HasReflectionMembers`),
corelib'de `Type.GetFields/GetField/GetMethod`, `FieldInfo.GetValue/SetValue`, `Activator.CreateInstance` var.
Registry aynı metaveriyi ikinci kez, bu sefer kod olarak taşıyor.

## Hedef
- `Registry.g.cs`, `CatalogWriter`, `RegistryCompiler`, `RegistryTests`, `Digitoy.Registry.dll` **yok**.
- Editör ve player (AOT + .NET) **aynı** reflection kataloğunu kullanır (`TypeCatalog.FromReflection`).
- AOT sıcak yolu (tween/MovieClip `AnimProperty.Set`) **boxing'siz** kalır: alan offset'ine tipli yazma intrinsic'i.
- Pişmiş sahne formatı **ada dayalı ve toleranslı**: alan ekle/çıkar/taşı/rename sahneyi bozmaz; modül ve host
  farklı zamanlarda build edilebilir; iki runtime'ın `GetFields` sırası önemsiz.
- Attribute'lar (`[SerializeField]`, `[ShowIf]`, `[Animatable]`, `[FormerlySerializedAs]`, …) **editör-only** kalır:
  neyin serileşeceğine pak karar verir (eski Digiplay `XmlLoader`/`Reflect` modeli), rename alias'ları pak'ta veri olur.
  Transpiler'a attribute tablosu **eklenmez**.

## Sabit kararlar
| Karar | Gerekçe |
|---|---|
| Tipli erişim `FieldInfo.GetRef<T>(object)` intrinsic'i (AOT: `(T*)((char*)obj + offset)`) | Registry lambda'sıyla aynı makine kodu; generic monomorph → `GetRef<float>`, `GetRef<Vec3>` ayrı düz deref |
| Pak kendini tarif eder: tip başına alan tablosu (ad + kind), okuyucu açılışta `pakIdx → alan` eşler | Sıra bağımsız; FormerName burada çalışır; uyumsuz alan `byteLen` ile atlanır (component değil **alan** düzeyinde tolerans) |
| `SerializedType.Build` AOT'ta yalnız `ad → FieldInfo/kind` kurar; serileşen küme pak'tan | Attribute reflection'ı AOT'a taşımaya gerek kalmaz |
| `[Animatable]` property (6 kullanım) için `MethodInfo.CreateDelegate<T>()` tipli delegate; gerekirse alan+metoda çevirip ertele | Mevcut şekil-thunk/trampoline altyapısının küçük uzantısı |
| Tip keşfi: corelib `Type.GetSubtypes(baseType)` (EmitMeta tip tablosundan) | `FromReflection`'ın AOT karşılığı; Registry tip listesine gerek yok |
| `generated.c` shard'lama + release `-flto=thin` ayrı iş (Faz 5); Registry kalkınca zorunlu değil | Motor büyüdükçe paralellik yine şart; Windows/wasm da yararlanır |

## Bağımlılık haritası (kaldırılacaklar)
| Yer | Bağımlılık |
|---|---|
| `editor/EditorApp.cs BuildRegistryCatalog` | her script derlemesinde Registry üret → Roslyn → ALC → reflection ile diff-assert |
| `editor/CatalogWriter.cs`, `RegistryCompiler.cs`, `RegistryTests.cs` | silinir |
| `editor/GameCode.cs` | `InternalsVisibleTo("Digitoy.Registry")`, `LoadRegistry` |
| `editor/PlayerBuilder.cs`, `AotCompatCheck.cs`, `ModuleBuilder.cs` | Registry.g.cs ön koşulu / derlemeye ekleme |
| `aotcompiler/source/Program.cs GameAotDll`, `RunPlayerBuild` | Registry.g.cs kaynak; `Generated.Registry$RegisterAll` → `de_game_register` köprüsü |
| `aotcompiler/source/cil/CilFrontend.cs` | `[UnsafeAccessor]` thunk desteği (kalabilir, gereksizleşir) |
| `player/PlayerApp.cs` (.NET player) | `Digitoy.Registry.dll` yükler |
| `engine/managed/Module.cs`, `docs/modules.md` | modül giriş noktası `Registry$RegisterAll` IR adı |
| Engine `#if DE_AOT`: `TypeCatalog.cs`, `SerializedType.cs`, `SceneBinary.cs`, `Animation.cs` | reflection yolu AOT dışı |

## Fazlar
Sıra: **önce Registry editörden tamamen sökülür** (editör + .NET player yalnız reflection kataloğu), sonra AOT
eksikleri adım adım kapatılır. Ara dönemde **Windows/Android/wasm player build'i ve modül build'i KIRIK** kalır
(bilinçli: iki yolu paralel yaşatmak yok). AOT tarafı değişiklikleri Sandbox player build'iyle (5 dk) DEĞİL,
aotcompiler selftest'i / küçük test projesiyle (saniyeler) doğrulanır; Sandbox build yalnız faz sonunda.

| Faz | İş | Süre |
|---|---|---|
| 0 | Ölçüm + güvenlik ağı: baseline sayılar; sahne **alan dump** testi (`SceneDump`, RPC `scene.dumpFields`, player `Build/scene-dump.flag`, `tools/scene-dump-test.ps1`) | ½ gün ✔ |
| 1 | **Registry editörden sökülür**: `EditorApp.BuildRegistryCatalog` → `TypeCatalog.FromAssemblies(engine, game)`; `CatalogWriter`/`RegistryCompiler`/`RegistryTests`/`GameCode.LoadRegistry`/IVT silinir; `PlayerApp` (.NET) `FromAssemblies`; `PlayerBuilder`/`AotCompatCheck`/`ModuleBuilder` Registry ön koşulları kalkar (build'ler kırık). Dump testi editörde (edit ↔ play) aynı | ½ gün |
| 2 | `SceneBinary` v2: tip başına alan tablosu (ad + kind), `pakIdx → alan` eşleme, alan düzeyi tolerans, alias tablosu pak'ta; `SchemaHash`/`ReadBaked` kalkar; `ReadFields` tek okuma yolu. Editör play modu + .NET player ile doğrulanır | 1 gün |
| 3 | Engine tek yol: `#if !DE_AOT` kalkanları kalkar (`TypeCatalog`, `SerializedType`, `SceneBinary`, `Animation`); attribute okumaları editör-only yardımcıya; `FieldSchema` erişimcileri soyutlanır (`GetRef<T>` tabanlı); `CopyTo`/`AnimRegistry` kind-switch. Hâlâ yalnız .NET'te doğrulanır | 1 gün |
| 4 | **AOT reflection = .NET yüzeyi** (engine'in kullandığı alt küme; `Reflect.cs` tek fonksiyon çiftine iner, tamamı conformance testinde). Dilimler: **4a** `VmArray.elem` (dizi eleman tipi: `is T[]`, `GetType`, `Array.CreateInstance/GetValue/SetValue`); **4b** attribute tablosu (`IsDefined`, `GetCustomAttribute(s)`, sabit argümanlar) + `Type/FieldInfo.Attributes` bitleri; **4c** `BindingFlags` overload'ları, açık generic descriptor'ları (`typeof(List<>)`), sahte `Assembly`, `Activator(nonPublic)`, `List<T>: IList`, `Array.Length`; **4d** `RefAt<T>`/`Offset` intrinsic'i, `MethodInfo.CreateDelegate`, engine düz reflection'a döner. **Kapsam dışı (bilinçli):** `MakeGenericType/Method`, `Emit`, `Expression`, `Assembly.Load`, indexer/`ref` Invoke | 3–4 gün |
| 5 | AOT player ayağa: `GameHost` kataloğu `GetSubtypes`'tan; `de_game_register` kalkar; aotcompiler `GameAotDll` yalnız scripts; Windows build → dump testi editörle birebir; sonra Android/wasm | ½ gün |
| 6 | Modüller: giriş `Registry$RegisterAll` yerine tip listesi; modpak testi; docs | ½ gün |
| 7 | Derleme hattı: CMake release `-O1` override'ı kalkar; **tip başına `.c/.h` + artımlı derleme** (shard/ThinLTO yerine; aşağıda "Faz 5+ / derleme hattı") | ½ gün ✔ |

Kabul (her faz): dump testi — Faz 1–3'te editör edit ↔ play (.NET), Faz 5'ten itibaren editör ↔ AOT player.

## Riskler
| Risk | Karşılık |
|---|---|
| AOT `GetFields` sırası/adı .NET'ten farklı | Faz 2 ile sıra önemsiz; ad eşleşmesi dump testiyle doğrulanır |
| Struct'ların runtime `Type`'ı yok (`Type.cs` sınırı) | `FieldInfo.FieldType` için `reflectedValueStructs`; iç içe struct'ta Go/CompRef zaten yasak |
| Property setter tipli delegate | 6 kullanım; gerekirse alan+metot çiftine çevirerek ertele |
| Stub kapısı reflection çağrılarını göremez | ctor'lar zaten hepsi emit; `List<T>`/`T[]` tablosu açıkça eklenir |
| Modül giriş sözleşmesi değişir | modpak testi yeniden koşulur |
| Tüm `Component` türevlerinin ctor + alan tablosu "canlı" | zaten emit ediliyordu; Registry kodu gidince net küçülme |

## Beklenen sonuç
- Android `generated.c`: 984 s → ~1 dk mertebesi; release gerçekten -O3.
- Editör hot-reload: Registry derleme + diff adımı kalkar.
- Editörde çalışan = player'da çalışan (tek kod yolu).
- Sahne formatı alan değişikliklerine dayanıklı; modül/host farklı build olabilir.

## Durum
- **Faz 0 BİTTİ.** Baseline: Android `generated.c` 984 s (NDK -O1 -g), `libgame.so` 34 MB; Windows `clang+link` 318–333 s
  (-O1, llvm-mingw; eski 45 s artık geçerli değil); `generated.c` 28.8 MB, `Reg_4`=ParticleSystem 19.8k satır C.
  Dump: editör 1832 satır = AOT player 1832 satır; **component alanları birebir**, 30 fark yalnız Transform pos
  (editörde layout koşmuş) + 1 `active` → test betiği bunları gürültü sayar.
  AOT reflection boşlukları (Faz 4 girdisi): `x is T[]` → transpiler var olmayan `_T_type` sembolü üretiyor;
  `System.Array.Length` (`Array` referansı üzerinden) → `System_Array_get_Length` tanımsız; AOT dizileri
  `IList`/`IEnumerable` değil, `List<T>` non-generic `IList` değil. `SceneDump` dizileri şimdilik `<dizi: Faz 1>` yazar.
- **Faz 1 BİTTİ (editör Registry'siz).** Silinen: `CatalogWriter.cs`, `RegistryCompiler.cs`; `RegistryTests` → `CatalogTests`
  (yalnız fonksiyonel smoke, 13 PASS). `EditorApp.RebuildCatalog` doğrudan `FromAssemblies`; `GameCode` IVT/`LoadRegistry` kalktı;
  `AotCompatCheck` Registry parametresi kalktı; `PlayerApp` (.NET) `FromAssemblies`. **Bilinçli kırık:** `PlayerBuilder.Start`
  ve `ModuleBuilder.Start` açık hata mesajıyla erken çıkar (Faz 5/6'da geri gelir). Doğrulama: açılış testleri hepsi PASS;
  edit dump Registry'li/Registry'siz 1832/1832 satır fark 0; play modu (instantiate + 30 frame + dump) çalışıyor.
- **Faz 2 BİTTİ (SceneBinary v2).** Pak tip başına alan tablosu (ad+kind+elemKind, iç içe özyinelemeli) + alias tablosu
  taşır; okuyucu açılışta `PakField → FieldSchema` eşler (ad/FormerName; kind farkı = uyumsuz), eşleşmeyen alan
  `SkipValue` ile atlanır + uyarı. `SchemaHash`, `ReadBaked`, `BakedReader`, `Entry.ReadBaked` kalktı; `ReadFields`
  tek okuma yolu (`#if !DE_AOT`, Faz 3'te açılır; AOT dalı şimdilik açık `throw`). Doğrulama: açılış testleri PASS;
  yeni `SerializationTests.BakedTolerance` (eski şemayla bake → alan eklenmiş/silinmiş/taşınmış/türü değişmiş/iç içe
  silinmiş "V2" tipe spawn) 10/10; Sandbox v2 pak → .NET player dump = editör dump (1832/1832, fark 0).
- **Faz 3 BİTTİ (engine tek yol).** Yeni `engine/managed/Serialization/Reflect.cs`: motorun reflection'a dokunduğu tek kapı
  (.NET dalı = bugünkü davranış; AOT dalı dosya başındaki **AOT sözleşmesine** karşı yazılı, Faz 4 corelib'de uygular).
  `#if !DE_AOT` kalkanları kalktı: `SerializedType.Build/BuildField`, `TypeCatalog.FromAssemblies/RegisterReflective`,
  `Component.ComputeFlags`, `GameObject.AddComponent`, `AnimRegistry.BuildReflective` (expression yerine
  `Reflect.FieldGetter<T>/FieldSetter<T>` kind-switch; alan zinciri → .NET expression / AOT offset yolu),
  `SceneBinary.ReadFields`, `SceneDump`. `GameHost.LoadCatalog` (AOT) `de_game_register` yerine `FromAssemblies()`.
  `CopyTo` kapsamı korundu (TÜM public + [SerializeField], şemadan geniş). Doğrulama: açılış testleri hepsi PASS
  (movieclip 61 — anim erişimcileri), edit dump 1832 fark 0, play 60 frame ok, v2 pak → .NET player dump fark 0.
  **Sapma notu:** AOT'ta attribute *tablosu* yok ama 3 bit + 1 string attribute-türevi bilgi gerekiyor
  (`FieldInfo.IsSerialized/IsAnimatable/FormerName`, `Type.IsSerializable`) — transpiler CIL metadata'dan tek seferde yazar.
- **Faz 4a BİTTİ (dizi eleman tipi).** `VmArray.elem` (eleman descriptor'ı; `vmarray_new_rank_te/_e`), `Type.elem_type/rank/array_of`
  + `DIGITOYENGINE_TYPE_ARRAY`; runtime dizi tahsis tipleri init'te `System.Array`'den türer (`digitoyengine_array_init`),
  `typeof(T[])` descriptor'ları ARRAY bayraklı, olmayan `T[]` kimliği runtime'da sentezlenir (aynı (elem,rank) = aynı Type*).
  corelib: `Array.Length/Rank/GetValue/SetValue/CreateInstance`, `Type.IsArray/GetElementType/GetArrayRank`, `Object.GetType()`
  dizide `T[]` döner; `is/as T[]` exact (deger) / kovaryant (ref); `(T[])x` cast'i eleman tipi biliniyorsa denetimli. corelib.c
  dizileri eleman tipli (`char[]`, `string[]`, `byte[]`, `FieldInfo[]`…). vmint (modül) dizileri `elem=0` → `is` false,
  cast hoşgörülü (Faz 6). Test: `Cases/Test56.cs` 27/27 bit; selftest 52/55 (3 FAIL önceden var: InlineArray, Delegate.Combine,
  StringSplitOptions).
- **Faz 4b BİTTİ (custom attribute'lar = .NET yüzeyi).** CIL attribute blob çözümü (`CustomAttribute.DecodeValue`), `PrimitiveAttribute`
  (tip + ctor + sabit/named arg'lar), transpiler lazy kurucu fonksiyonlar + `DeAttr` tabloları (tip/alan/property/metot;
  `Type/DigitoyEngineMember/MethodInfo.attrs`), runtime `digitoyengine_attr_instance` (ilk istekte kurulur, rooted cache —
  **bilinçli fark:** .NET her çağrıda yeni nesne üretir). corelib: `MemberInfo.IsDefined/GetCustomAttributes`,
  `CustomAttributeExtensions.GetCustomAttribute<T>` (inherit default true, `AmbiguousMatchException`),
  `Attribute.IsDefined/GetCustomAttribute`, `FieldAttributes/MethodAttributes/TypeAttributes/PropertyAttributes` enum'ları +
  `Attributes` property'leri (`IsPublic/IsPrivate/IsNotSerialized/IsInitOnly/IsSerializable/IsSealed…`), pseudo-attribute sentezi
  ([Serializable]/[NonSerialized]), `bool.ToString()`. CIL'den **property metadata'sı** (`GetProperties` artık dolu; eskiden boştu)
  ve `readonly` → `IsInitOnly`. Testler: Test57 24/24, Test56 28/28; Test32 eskiden iki tarafta da NRE atıp "geçiyordu" —
  .NET semantiğine düzeltildi (kalıtılan static üye `GetField/GetProperty(name)` ile dönmez), artık gerçekten geçiyor.
  Selftest **53/56** (3 FAIL önceden var: InlineArray, `Delegate.Combine`, `StringSplitOptions`).
  **GC notu:** runtime GC'si managed frame local'lerini kök saymaz (`gc_major` yalnız statikler + açık root'lar; host frame
  safepoint'inde koşar) → "GC.Collect + local'den oku" testleri geçersiz; Test56'dan çıkarıldı.
- **Faz 4c BİTTİ (.NET üye seçimi + generic/Assembly).** `BindingFlags` ve .NET kuralları corelib C#'ta (`Type.Pass`: kalıtılan
  private asla, kalıtılan static yalnız `FlattenHierarchy`, `DeclaredOnly`, override gizleme; ham listeler C'den
  `GetFieldsRaw/GetPropertiesRaw/GetMethodsRaw`), `GetMethod(name, flags, binder, Type[], mods)`, `GetConstructor(flags…)`;
  açık generic descriptor'ları (`DIGITOYENGINE_TYPE_GENERIC_DEF`, `Type.generic_def`, `Primitive.InstantiatedFrom`;
  `IsGenericType/IsGenericTypeDefinition/GetGenericTypeDefinition`, `typeof(List<>)`); sahte `Assembly` (`Type.Assembly`,
  `GetTypes`, `AppDomain.CurrentDomain.GetAssemblies`, `==`); `List<T> : IList`; `Activator.CreateInstance(Type, nonPublic)`.
  **Debugger (lldb) ile bulunan 3 gerçek hata:** (1) `MemberInfo.DeclaringType` C'de handle'ı hep alan struct'ı sayıyordu →
  metot wrapper'ında çöp (polimorfik yapıldı); (2) `GenericInstantiator` Display adını `` ` `` karakterinde kesiyordu →
  `List<int>` ile `List<int>.Enumerator` **aynı adı** alıyor, `Type.GetType(ad)`/`Activator` yanlış descriptor buluyordu
  (yalnız arity eki silinir); (3) generic `(T)obj` (`unbox.any !T`) şablonda `CastClass` üretilip `T=int` somutlamasında
  pointer→int cast oluyordu → `CastClass` hedefi değer tipiyse unbox (evrensel kural). Test58 23/23; selftest **54/57**.
  lldb için `python311` PATH'te olmalı (`C:\Users\<u>\AppData\Local\Programs\Python\Python311`); debug exe:
  `clang -O0 -g -gcodeview generated.c vmrt.c corelib.c vmint.c`.
- **Faz 4d BİTTİ (engine tek reflection yolu).** corelib `FieldInfo.Offset` + `static ref T RefAt<T>(object, int)` (CilFrontend
  intrinsic'i: `(T*)((long)target + offset)`, Span deseniyle). `engine/managed/Serialization/Reflect.cs` yeniden yazıldı:
  tüm tip/üye sorguları, attribute'lar, kurucular .NET ve AOT'ta **aynı kod** (düz `System.Reflection`); ikili kalan yalnız
  `FieldGetter/FieldSetter<T>` (.NET expression / AOT `AotPath`+`RefAt`) ve property/metot delegeleri (AOT boxed Invoke —
  `MethodInfo.CreateDelegate` ertelendi, 6 kullanım). Doğrulama: editör testleri hepsi PASS, dump 1832 fark 0; engine
  `sdk-il` ile DE_AOT/corelib'e karşı 3 hedefte derleniyor; selftest 54/57. `RefAt` çalışma zamanı doğrulaması Faz 5
  (Windows AOT player: dump + anim round-trip).
- **Faz 5 BİTTİ (AOT player ayağa) + derleme hattı yeniden kuruldu.** `GameAotDll` yalnız scripts, `de_game_register` kalktı,
  `vmvoid_type` (`typeof(void)`). İlk Registry'siz Windows build çalıştı ama tek TU `generated.c` (28 MB) clang+link **487 s**
  → kök neden "büyük fonksiyon" değil tek çeviri biriminde toplam hacim. Karar: **tip başına `.h/.c`** + değişmeyen dosyaya
  dokunmama + paralel/artımlı derleme. Bunun ön koşulu, dosya içeriğine sızan **build-sırası bağımlı global numaraların**
  kaldırılmasıydı (A adımı), sonra emisyon (B), sürücü (C), platformlar (D):
  - **A:** `Type.tindex` kalktı → `Type.wrapper` (her descriptor'da lazy `System.Type`, `gc_add_root`; descriptor'lar artık
    `const` değil — zaten `.data.rel.ro`'daydı, maliyet yok) + `Type.rootid` (yalnız runtime kökleri 1..19, sabit; primitive
    kimliği ve "üretilen tip" ölçütü `rootid == 0`) + `DIGITOYENGINE_TYPE_SYNTH_ARRAY`. Düz `digitoyengine_methods[]` kalktı →
    tip başına `X_methods[]` + `digitoyengine_methods_misc[]` (Object/String sahipli); hash indeksi init'te tiplerden kurulur.
    String havuzu `_strpool_<FNV64 hex>` (içerik hash'i; çakışmada `_N` eki), thunk'lar `de_thunk_<şekil hash>`,
    `MethodInfo.shape` indeksi → `DeThunk thunk` pointer'ı; `mp_/mattrs_` metot sembolüyle adlanır. Kabul: aynı girdi → bayt bayt
    aynı çıktı (SHA256 eşit). Selftest 54/57, `tests/meta_check.c` PASS.
  - **B:** `CTranspiler.Units.cs` — birim = tip: `T_<cn>.h` (layout; değer-bağımlılığı `#include`, referanslar `struct X;` ileri
    bildirim — header metninden otomatik; `extern Type X_type`, `X_methods[N]`, statik `extern`'ler, wrapper prototipleri,
    `static inline New_X`/`ebox`) + `T_<cn>.c` (descriptor, fonksiyonlar, reflection üyeleri, attr/metot tabloları,
    trampoline'ler; kendi `extern VmString _strpool_…`/`de_thunk_…` satırları; include'lar gövdede geçen birim adlarından —
    kapsayıcı tarama, fazlası zararsız). Paylaşımlı: `gen_shared.h` (runtime kök/orphan/P-Invoke prototipleri, generic-def ve
    `typeof(T[])` descriptor'ları), `gen_misc.c`, `gen_strings.c`, `gen_meta.c` (tip tablosu, thunk'lar, misc metotlar,
    `digitoyengine_init`). Tek-dosya modu = aynı parçaların birleştirilmesi (selftest; `AOT_FILES=1` çok-dosya modunu da koşar).
    `WriteFiles`: içerik aynıysa dokunmaz, artık üretilmeyen `T_*`/`gen_*` siler. **Derleme listesi her zaman açık listedir,
    dizin taraması (GLOB) yok** — artımlı çalışmada eski dosya işe karışmaz.
  - **C:** dış araç yok (ninja denendi, kaldırıldı): `CcBuild.cs` yerleşik sürücü — `clang -MD` `.d` dosyaları + bayrak damgası
    ile "ne derlenecek", `Parallel.ForEach`, rsp dosyasıyla link, yetim `.o/.d/.flags` temizliği, `Tool` soyutlaması (emcc).
    Release: `-O2 -ffunction-sections -fdata-sections`, link `-s -Wl,--gc-sections`. Exe 10.3 → **6.7 MB** (3.4 MB strip edilmemiş
    COFF sembol tablosuydu; kalan `.text` 2.5 / `.rdata` 1.9 / `.data` 1.9 MB = kod + reflection metası; tip tablosu her şeye kök
    olduğu için gc-sections metayı atamaz — küçültme ayrı iş: const ayrımı, `MethodInfo` sıkıştırma).
  - **D:** Android: `generated/cpp` silinmez; `gen_sources.cmake` açık liste, CMake Release NDK `-O3` (eski `-O1` override'ları
    kalktı), `--gc-sections`. wasm: aynı sürücü, emcc `-O2`, rsp link.
  - **Ölçümler (Sandbox, 12 çekirdek):** Windows temiz 49.7 s (önce 318–487 s), değişiklik yok 9 s toplam, tek script gövdesi
    1 dosya/≈9 s; Android arm64 Debug temiz 56 s (önce `generated.c.o` tek başına 984 s), değişiklik yok 0 s; wasm temiz
    112 s + 14 s link.
  - **E (çalışma zamanı doğrulaması, hepsi lldb/üretilen kod kanıtıyla):** (1) `GameHost.LoadCatalog` `FromAssemblies()` boş
    params → katalog hiç kurulmuyordu → `FromReflection()`; (2) `enum : byte` `ref/out` yazımı 1 bayt (`stind.i1`), AOT enum
    4 bayt → üst baytlar çöp; frontend pointee enum ise tam int (Test59); (3) generic metot içindeki lambda `ldftn
    DisplayClass<!!0>::b__0` GenCtx'siz çözülüyordu → şablon stub → **instantiation'lar sessizce boş gövde** (tanımsız dönüş);
    `ldftn` gc ile + `DelegateNew.TypeArguments`, `GenericInstantiator` delegate tipini düz ikame eder, stub şablonun klonları
    `UntranslatableReason` devralır (stub kapısı yakalar); stub tanılarına IL konumu (`[IL_0011 Ldftn]`). Dump testi
    (`tools/scene-dump-test.ps1`, exe dizininden koşar): editör ↔ AOT player **fark 0**, baseline yazıldı.
- **Faz 6 BİTTİ (modüller).** Modül tipleri **tam reflection metası** taşır (host→modül yönü de "tek meta"): `.dmod v3`
  (tip/alan/metot custom attribute'ları + `cilattrs`), vmint yüklemede yerel tipler için `DigitoyEngineMember[]` /
  `MethodInfo[]` (bitişik; `VmMethod.mi` pointer) / `DeAttr[]` (`DeAttr.data`, instance lazy: host attribute ctor'u kutulu
  arg'larla, named arg'lar alan/setter ile), `vmint_type_count/at`, `vmint_try_invoke` (`MethodInfo.Invoke`/`Activator` modül
  metodu işaretçisi görünce yorumlar), vmrt'de get/set fonksiyonsuz üyeler için tag/offset genel erişim; unload'da wrapper/
  instance kökleri kalkar. `Module.LoadAsync`: `RegisterAll`/`EntryKey` yok — vmint tip listesinden somut `Component` türevleri
  `TypeCatalog.RegisterReflective` ile child kataloğa (host ile aynı yol). `ModuleBuilder` açık (Roslyn PDB için encoding'li
  kaynak). **Eski hata:** `TypeCatalog.Find` `TryGetValue(name, out name)` bulamayınca parent'a `null` gönderiyordu — child
  katalog zinciri hiç çalışmamıştı (eski test yolu engine tiplerini modüle de yazdığı için gizli). Doğrulama: Sandbox modül
  olarak Sandbox player'a (`Build/autoload.module.pak`): 5 script tipi reflection ile kayıt, modül sahnesi engine tipleri dahil
  uyarısız, `Update`'ler yorumlanıyor; AOT selftest 55/58, interp 50/58 (= önceki baseline; 55/57 iyileşti), editör testleri
  PASS, dump testi PASS. **Açık (kapsam dışı):** yorumlanan modül kodunun kendi tipleri üzerinde reflection'ı (interp
  Test32/55/56/57/58, önceden de yoktu; meta artık mevcut).
- **Faz 7 BİTTİ** (yukarıda C/D: tüm hedeflerde tam optimizasyon, `-O1` override'ları kalktı; ThinLTO gereksiz).
- **Ertelenen:** `MethodInfo.CreateDelegate` (6 kullanım, boxed Invoke ile çalışıyor); 3 eski selftest FAIL (InlineArray,
  `Delegate.Combine`, `StringSplitOptions`); exe meta boyutu küçültme; `.d` kontrolü (1450 dosya ≈3.5 s) hızlandırma.
