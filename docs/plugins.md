# Plugin Modeli — platform SDK entegrasyonu

Hedef: reklam / billing / login / analytics / crash gibi platform SDK'larını **engine'e gömmeden**, Unity'deki bağımlılık
cehennemine girmeden, bir plugin eklemek "klasör at + Kotlin/Swift sınıfı yaz" kadar basit olsun. Kararlar 2026-10-17
(eski Haxe `digiplay` extension mekanizmasının sadeleştirilmiş sentezi; bkz. engine-notes.md).

## İlkeler
1. **Push yok, pull var.** Host managed'a hiçbir zaman kendiliğinden bir şey iletmez; biriktirir, managed frame başında çeker.
   "Dinlemeden önce fire oldu" sınıfı sorun tanım gereği yoktur.
2. **Core protokolü = iki yönde UTF-8 string.** JSON mu düz metin mi — plugin'in kararı. Core parse etmez, `id/method` bilmez.
3. **Çözücü biz değiliz.** Android bağımlılığı Gradle (plugin = gerçek Gradle library modülü), iOS bağımlılığı SPM. İkinci bir
   çözücü (EDM4U, CocoaPods) yok. Trim platformun işi (R8 / dead-strip), SDK kendi `consumer-rules.pro`'sunu taşır.
4. **Ayar = C# `[Serializable]` sınıf.** JSON şema yok. Editör `ObjectDrawer` ile otomatik çizer, `ProjectSettings/Plugins/<Ad>.asset`.
5. **Build bilgisi runtime'a girmez.** `Editor/` içindeki `I*ProjectHook` sınıfları üretir; `Runtime/` yalnız API + ayar tipi.
6. **Plugin hatası oyunu düşürmez** — core'un çağırdığı her yolda garanti; SDK'nın kendi thread'inden çağırdığı yazar kodunda sınır var
   (aşağıda "Garanti sınırları").

## Klasör konvansiyonu (sabit kök `Assets/Plugins/<Ad>/`)
```
Assets/Plugins/Facebook/
  Runtime/Facebook.cs          Plugin<Facebook.Config> türevi: Settings sınıfı + tipli API (Login, …). Oyun assembly'sine girer, AOT'ye girer.
  Editor/FacebookAndroid.cs    IAndroidProjectHook  (editör assembly'si; AOT'ye GİRMEZ)
  Editor/FacebookIos.cs        IIosProjectHook
  Editor/FacebookMock.cs       PluginMock<Facebook> (editörde Play için; isteğe bağlı)
  Android/                     Gradle LIBRARY modülü (build.gradle.kts, src/main/AndroidManifest.xml, java|kotlin/…, consumer-rules.pro)
  iOS/                         .m / .swift / .c (+ .xcframework); DEPlugin türevi + DE_REGISTER_PLUGIN
  WASM/                        .js (plugin sınıfı + DE.register)
```
Plugin adı = C# sınıfının **kısa adı** (`Facebook`); host sınıfı aynı adla kaydolur. Aynı adla iki plugin → build hatası.
`Runtime/`'da `#if` ve `DllImport` **kullanılmaz**: platform özgüllüğü tamamen motorun köprüsünde; plugin C#'ı her hedefte (editör,
Windows, desteklenmeyen platform) aynen derlenir. Host'ta plugin yoksa `Send` sessizce düşer (`Available == false`, bir kez uyarı).
`Android/ iOS/ WASM/` içeriği asset değildir (importer dışlar).

## Managed yüzey (engine/managed/Plugin.cs)
```csharp
public abstract class Plugin
{
    public string Name { get; }                        // GetType().Name
    public bool Available { get; }                     // host'ta kayıtlı mı (init'te bir kez)
    protected void Send(string msg);                   // → host DEPlugin.onMessage (main thread'e dispatch)
    protected virtual void OnMessage(string msg) { }   // ← host DEPlugin.send; frame başında, ana thread, sırayla; exception → LogError, zincir devam
    public static T Get<T>() where T : Plugin, new();  // ilk çağrıda yaratılır (reflection yok, statik generic); kullanım = Get
}
public abstract class Plugin<TSettings> : Plugin where TSettings : class, new()
{
    public TSettings Settings { get; }                 // pak'tan (ProjectBinary) ya da editörde ProjectSettings/Plugins/<Ad>.asset; yoksa new()
}
```
`await` core'da yok: isteyen plugin `TaskCompletionSource` + `Wait.Timeout` + kendi `id` alanıyla kurar (Http/NativeAsync deseni).
Örnek:
```csharp
public sealed class Ads : Plugin<Ads.Config>
{
    [Serializable] public sealed class Config { [Required] public string appIdAndroid = ""; [Required] public string appIdIos = ""; public bool testMode = true; }
    readonly Dictionary<int, TaskCompletionSource<JsonNode>> _pending = new(); int _next;
    public async Task<AdResult> ShowRewarded(string placement, float timeout = 60f)
    {
        if (!Available) return AdResult.Unavailable;
        int id = ++_next; var tcs = new TaskCompletionSource<JsonNode>(); _pending[id] = tcs;
        using var cts = Wait.Timeout(timeout); using var reg = cts.Token.Register(() => { _pending.Remove(id); tcs.TrySetResult(null); });
        Send($"{{\"id\":{id},\"method\":\"showRewarded\",\"placement\":\"{placement}\"}}");
        var r = await tcs.Task;
        return r == null ? AdResult.Timeout : r["rewarded"].Bool ? AdResult.Rewarded : AdResult.Closed;
    }
    protected override void OnMessage(string msg)
    {
        var j = Json.Parse(msg);
        if (j["id"] != null && _pending.Remove(j["id"].Int, out var tcs)) tcs.TrySetResult(j);
        else if (j["event"] == "purchaseRestored") { /* istenmemiş mesaj */ }
    }
}
```
Oyun kodu: `var r = await Plugin.Get<Ads>().ShowRewarded("double_coins");` — SDK hataları **sonuç değeridir**, exception değil.

## Host yüzeyi
Her platformda aynı şekil; base sınıf `try/catch` + main-thread dispatch + inbox yazımını saklar, plugin yazarı C'ye dokunmaz.

**Android** (`com.digitoy.host.DEPlugin`, Kotlin — `:dehost` library modülü `generated/dehost/`; plugin modülü `implementation(project(":dehost"))`):
```kotlin
abstract class DEPlugin(val name: String) {
    val activity: FragmentActivity; val overlay: FrameLayout; val application: Application
    abstract fun onMessage(msg: String)                              // main thread; PluginHost try/catch(Throwable)
    fun send(msg: String)                                            // her thread; → inbox
    open fun onApplicationCreate(app: Application) {}
    open fun onCreate(saved: Bundle?) {}; onStart(); onResume(); onPause(); onStop(); onDestroy()
    open fun onActivityResult(req: Int, res: Int, data: Intent?) {}; onNewIntent(i: Intent); onRequestPermissionsResult(...)
    open fun onBackPressed(): Boolean = false                        // true = tüketti
    companion object { var errorReporter: ((Throwable) -> Unit)? }   // Crashlytics plugin'i doldurur
}
```
Kayıt: üretilen `generated/java/com/digitoy/host/Plugins.kt` (aotcompiler `Android/` modülünde `: DEPlugin(` türevini tarar).
`PluginHost` (app): dispatch + lifecycle fan-out + uncaught handler zinciri (yalnız rapor). `GameApplication` manifestte.
**iOS** (`DEPlugin` ObjC base, `c_runtime/DEPlugin.h`; Swift türevler ObjC dispatch'ten geçer): `viewController`, `overlayView`,
`onMessage:`, `send:`, `didFinishLaunching:`, `openURL:options:`, `continueUserActivity:`, `didRegisterForRemoteNotifications:` /
`didFail…` / `didReceive…`, `didBecomeActive` / `willResignActive` / `didEnterBackground` / `willEnterForeground`.
Türev `- (instancetype)init { return [super initWithName:@"Ads"]; }` + dosya sonunda `DE_REGISTER_PLUGIN(AdsPlugin)`.
`DEPluginHost.errorReporter` (block) Crashlytics için.
**WASM**: `DE.register({ name: 'Ads', onMessage(msg) {…} })`, `DE.send(name, msg)`; `plugins.js` üretilir, `index.html` onu game.js'den
ÖNCE senkron yükler; `window.DEConfig` (hook `ConfigValue`).
**Desktop**: köprü var, plugin yok → `Available=false`.

### Thread / zamanlama sözleşmesi
- `Send` → `de_plugin_send(name, utf8)` → host `run_on_main` → `onMessage`. Kayıt yoksa `0` döner.
- `send` → `de_plugin_post(name, utf8)` → süreç ömürlü **inbox** (statik, `de_app_init`'ten bağımsız; soğuk başlangıç mesajları kaybolmaz;
  plugin başına 1024 sınır, dolunca en eski düşer + stderr).
- `de_app_frame` başında `de_plugin_drain` → managed `Plugin.OnMessage` sırayla. Managed'a giriş yalnız buradan (de_app.h kuralı).
- Senkron çağrı **yok** (deadlock riski; main thread ↔ render thread). Senkron ihtiyaç → `Settings` ya da init'te bir kez `Send`/`OnMessage`.

## Ayarlar
`Plugin<TSettings>`'in `TSettings`'i (ya da plugin'siz `[ProjectSetting]` attribute'lu `[Serializable]` sınıf) → Player Settings penceresi
"Plugins" bölümünde `ObjectDrawer` ile çizilir → `ProjectSettings/Plugins/<Ad>.asset` (git'e girer). Tek attribute: `[Required]`
(boş → kırmızı + build hatası). TUZAK: iç içe ayar sınıfına `Settings` adı verilemez (`Plugin<T>.Settings` özelliğiyle çakışır) → `Config`. **Secret kavramı yok**: Facebook App ID / Client Token / AdMob ID gibi değerler APK/IPA içinde zaten okunabilir,
Unity'de de düz metindir. Gerçekten gizli olan tek şey imza parolasıdır ve o `UserSettings/AndroidSigning.asset`'te (ayrı mekanizma).
Ayarlar pak'a olduğu gibi girer (küçük; managed'ın da host'un da işine yarar).

## Build kancaları (`DigitoyEngine.Build`, aotcompiler/source; editör Link ile alır)
```csharp
public interface IAndroidProjectHook { void OnGenerate(AndroidProject p); }
public interface IIosProjectHook     { void OnGenerate(IosProject p); }
public interface IWasmProjectHook    { void OnGenerate(WasmProject p); }
```
`Assets/**/Editor/**/*.cs` → `<Ad>.Editor.dll` (aotcompiler ve editör aynı Roslyn derlemesini yapar; normal .NET'e karşı; aotcompiler
`--editor-engine <DigitoyEngine.dll>` ile editörün .NET dll'ini alır, CLI'da dev/kurulu yoldan bulur). Uygulayan tipler ad sırasıyla
çağrılır — yalnız **canlı plugin'lerin** kancaları + plugin dışı (oyunun kendi) kancalar. **Kanca → plugin eşlemesi**: sınıf adı plugin
adıyla başlar (`FacebookAndroid`, `FacebookIos`) ya da `[PluginHook("Facebook")]`; eşleşmeyen = oyunun kendi kancası (her zaman çalışır). `p.Settings<T>()` → doğrulanmış ayar nesnesi (dosya yoksa `new T()`; `[Required]` boşsa build hatası, `generated/`'a dokunulmadan).
Native kaynak kopyası (Gradle modülü include, iOS kaynakları, WASM js) konvansiyonla **otomatik**; kanca yalnız bunun ötesini söyler.
- `AndroidProject`: `ResString(name, value)`, `ManifestPlaceholder(name, value)` (→ `app.properties placeholder.<ad>`), `GradlePlugin(id, version)`
  (app seviyesi; aynı id → en yüksek sürüm + log, major farkı → hata; classpath `generated/gradle-plugins.properties`), `AppFile(pluginRel)`
  (google-services.json gibi `app/`'e kopya; `HasPluginFile` ile kontrol, `Warn`), `Dependency(coord)` (app seviyesi; nadiren — normalde
  modülün kendi build.gradle'ında), `MavenRepo(url)` (→ `plugins.repos.txt`), `ForceVersion(group:artifact, version)` (çakışma kırıcı;
  `plugins.deps.gradle.kts` `constraints { strictly }`).
- `IosProject`: `SwiftPackage(url, from, params string[] products)` (**product listesi zorunlu** = iOS'ta asıl trim; aynı url → en yüksek `from`; `exact` yok),
  `XcFramework(relPath)`, `Framework(name)`, `LdFlag(flag)`, `Plist[key] = value` (string/array/dict), `UrlScheme(scheme)`, `Entitlement(key, value)`,
  `BackgroundMode(mode)`.
- `WasmProject`: `Script(url)`, `PreJs(relPath)`, `PostJs(relPath)`, `Config(key, value)` (→ `window.DEConfig`).

### Üretim çıktıları (hepsi `generated/`, kabuk dokunulmaz)
- Android: `generated/dehost/` (host API modülü), `generated/plugins/<Ad>/` (modül kopyası), `plugins.settings.gradle.kts` (`include` +
  projectDir), `plugins.deps.gradle.kts` (`implementation(project)`, `apply(plugin=)`, `constraints`), `gradle-plugins.properties`
  (plugin classpath sürümleri; kök `buildscript` okur), `plugins.repos.txt`, `res/values/plugin_strings.xml`, `app.properties`
  `placeholder.*`, `java/.../Plugins.kt`, `app/<appFiles>`. Kabuk v3: `settings.gradle.kts` `:dehost` + `apply(from=)`, kök
  `buildscript` classpath döngüsü, `app/build.gradle.kts` `apply(from=plugins.deps)`, `GameApplication`, `FragmentActivity`, release
  `isMinifyEnabled=true` + `proguard-rules.pro` (JNI keep'leri: `GameLib`, `PluginLib`, `PluginHost.dispatch`, `Music`, `Prefs`,
  `ManagedException`); debug minify kapalı.
- iOS: `generated/DEPlugins/Package.swift` (tek ürün `DEPlugins`; `.package(from:)` + `.product` + `binaryTarget`; `Sources/DEPlugins`
  placeholder), `generated/Info.plist` (kabuk `Game/Info.plist` tabanı + merge), `generated/Game.entitlements`, `app.xcconfig`
  (`INFOPLIST_FILE`, `CODE_SIGN_ENTITLEMENTS`, framework/ldflags, `SWIFT_OPTIMIZATION_LEVEL=-Osize`), plugin kaynakları
  `generated/cpp/plugins/<Ad>/` (synchronized folder derler; `DE_REGISTER_PLUGIN` statik init). Kabuk v2: pbxproj `XCLocalSwiftPackageReference
  generated/DEPlugins` + `DEPlugins` ürün bağımlılığı (bir kez). TUZAK: `Package.swift` değişince Xcode bazen cache yenilemez →
  File ▸ Packages ▸ Reset Package Caches.
- WASM: plugin `WASM/*.c` derlemeye, `*.js` → `Build/wasm/plugins.js` (DEConfig + `Script()` document.write + birleşik js), pre/post-js link.
Merge kuralı (plist): çakışan anahtar → son kazanır + log. Build logu: `plugin: <Ad> [canli|kullanilmiyor] android ios wasm`, `kanca: ...`.

## Trim (platformun işi)
- Android R8: release'te açık; her SDK AAR'ı `consumer-rules.pro` taşır (R8 birleştirir); plugin modülü de taşıyabilir. Granüler artefakt seç
  (`facebook-login`, tüm `facebook-android-sdk` değil). Kotlin plugin kodu kullanılmayan metot R8'de düşer.
- iOS: `-dead_strip` açık; ObjC'de sınıf/metot stripping YOK (runtime dispatch) — trim **SPM product seçimiyle** olur (`FacebookLogin` yalnız
  LoginKit+CoreKit). `-ObjC` isteyen SDK tüm ObjC sembolü çeker; statik SPM tercih.
- WASM: JS SDK'ları trim edilmez (web normali); `plugins.js` yalnız istenenleri yükler.
- Managed: `Plugin` türevleri **kök DEĞİLDİR**. Oyun kodu `Plugin.Get<T>()` demiyorsa tip, ayarı ve instance'ı AOT'ye girmez (RTA düşürür).
  **Native taraf aynı kümeyi izler**: aotcompiler canlı `Plugin` türevlerini TrimAnalysis'ten alır → yalnız onların `Android/ iOS/ WASM/` klasörleri
  kopyalanır, yalnız onların hook'ları çağrılır. `Assets/Plugins/` altında duran ama kullanılmayan plugin APK/IPA'ya hiç girmez. (`full` profil: hepsi canlı.)

## Garanti sınırları (plugin yazarına iş bırakmadan core'un verdiği)
| Hata yeri | Android | iOS | WASM |
|---|---|---|---|
| Core'un çağırdığı plugin metodu (`onMessage`, lifecycle) | ✅ yakalanır, loglanır, zincir devam | ✅ (`@try/@catch`) | ✅ |
| SDK callback'inde yazar kodu (SDK'nın thread'i / main) | ❌ uncaught handler zinciri yalnız raporlar (Crashlytics varsa `recordException`), süreç ölür | ❌ (ObjC exception raporlanır; Swift trap yakalanamaz) | ✅ `onerror`/`unhandledrejection` |
| SDK'nın iç hatası / native crash | ❌ platform reporter | ❌ | n/a |
| Plugin C# sarmalayıcısı (`OnMessage`, continuation) | ✅ `LogError`, diğer plugin'ler etkilenmez | ✅ | ✅ |
| Runtime dependency çakışması (`NoClassDefFoundError`) | ✅ (`Throwable`) | link-time (build) | ✅ |
Looper yeniden sürme (Cockroach) gibi hileler **bilinçli olarak yok** (Activity yarım durumda kalır).

## Hata raporlama (Crashlytics plugin'i)
Core'un yakaladığı plugin hataları `DEPlugin.errorReporter` statik kancasına gider (host tarafı); Crashlytics plugin'i bunu `recordException`'a
bağlar, yoksa yalnız logcat/NSLog/console. Unhandled managed exception ayrıca `de_host_fatal` ile platform-native crash'e çevrilir
(Java `ManagedException` + `setStackTrace`, `NSException`, JS `throw`) → Play vitals / Crashlytics / Organizer satır bilgili trace gösterir.

## Editör
- `Assets/**/Editor/**/*.cs` → `<Ad>.Editor.dll` (GameCode ikinci Roslyn derlemesi, aynı ALC). `PluginMock<T>` türevleri Play'de host taklidi:
  `OnMessage(string)` + `Send(string)`; reload'da mock kaydı sıfırlanır (ALC kaçağı yok).
- Player Settings ▸ Plugins: `Assets/Plugins/` altındaki her `Plugin<TSettings>` bir foldout (kullanımdan önce de ayar yazılabilsin); oyun kodunda
  `Get<T>` ile referans edilmeyenler "not referenced by game code" etiketiyle soluk. `[Required]` boş → kırmızı (yalnız canlı plugin'de build hatası).
- Mock yoksa `Available=false` → plugin API'si "unavailable" sonucu döner; Play ölmez.

## Bilinen sınırlar
İki SDK'nın uyumsuz transitive sürümü hiçbir araçla çözülmez → `ForceVersion` tek satır. CocoaPods-only SDK → xcframework düşür. Kabuğu değiştirmeyi
dayatan SDK (özel Activity tabanı, SceneDelegate) → kullanıcı kabuğu elle düzenler (kabuk zaten kullanıcıya ait).

## Durum (2026-10-17) — uygulandı
- Motor: [Plugin.cs](../engine/managed/Plugin.cs) (`Plugin`, `Plugin<T>`, `Get<T>`, inbox pompası, ayar blobu, `PluginMock<T>`/`MockHost` DE_EDITOR),
  `ObjectBinary.ReadInto/Bake(obj)` katalogsuz, `GameHost` Boot `.plugins` + Frame `PumpInbox`, `AssetDatabase.IsPluginNativePath`.
- Native: [de_plugin.c/h](../engine/native/de_plugin.h); host'lar: Android (`PluginLib` JNI, `PluginHost.dispatch`, `ManagedException` + longjmp
  `de_host_fatal`), iOS (`DEPlugin.h`, `DEOverlayView`, AppDelegate forward, `NSException`), WASM (`DE.*` EM_JS, `de_js_fatal`), desktop (weak no-op).
- aotcompiler: [ProjectHooks.cs](../aotcompiler/source/ProjectHooks.cs) (API), [PluginBuild.cs](../aotcompiler/source/PluginBuild.cs) (canlı plugin'ler
  TrimAnalysis'ten, Editor.dll, üretim), `Program.cs` Emit* entegrasyonu; kabuklar Android v3 / iOS v2.
- Editör: `GameCode.LoadEditorCode` (Editor.dll + mock kaydı), `PluginSettingsEditor` + Player Settings ▸ Plugins, pak blobu, `PluginTests` (19 PASS).
- Örnekler: `Projects/Sandbox/Assets/Plugins/{Facebook,Crashlytics}` + `Scripts/PluginProbe.cs`. Üç hedef üretimi Windows'ta doğrulandı
  (Plugins.kt, gradle dosyaları, Package.swift, Info.plist, plugins.js). **Cihaz/Studio/Xcode/tarayıcı doğrulaması bekliyor.**
- Kalan: WASM `onerror` ekranı/sendBeacon örneği; editörde "referenced" etiketi kaba metin taraması (kesin kaynak build logu).
