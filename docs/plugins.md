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
4. **Ayar = C# `[Serializable]` sınıf.** JSON şema yok. Editör `ObjectDrawer` ile otomatik çizer, `ProjectSettings/Games/<oyun>/Plugins/<Ad>.asset`.
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
TUZAK (2026-10-18): VS Code Java LS (redhat.java) `Android/build.gradle.kts`'yi kök Gradle projesi sanıp senkronize eder ve
klasöre `.gradle/` bırakır → editör proje köküne `.vscode/settings.json` (`java.import.gradle.enabled: false`) yazar (yalnız yoksa;
varsa ve anahtar eksikse uyarı), `.gitignore` şablonu Gradle/Studio çöpünü dışlar, aotcompiler `CopyTree` `.gradle/ build/ .idea/
.cxx/ .kotlin/ *.iml local.properties`'i `generated/`'a kopyalamaz.

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
    public TSettings Settings { get; }                 // pak'tan (ProjectBinary) ya da editörde ProjectSettings/Games/<oyun>/Plugins/<Ad>.asset; yoksa new()
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
"Plugins" bölümünde `ObjectDrawer` ile çizilir → `ProjectSettings/Games/<oyun>/Plugins/<Ad>.asset` (git'e girer). Tek attribute: `[Required]`
(boş → kırmızı + build hatası). TUZAK: iç içe ayar sınıfına `Settings` adı verilemez (`Plugin<T>.Settings` özelliğiyle çakışır) → `Config`. **Secret dosyası yok**: tüm ayarlar (imza parolası dahil) `ProjectSettings/` altında git'e girer; gizlilik yalnız AI'a karşı
`[Sensitive]` ile (aşağıda). Facebook App ID / Client Token / AdMob ID zaten APK/IPA içinde okunabilir, Unity'de de düz metindir.
Ayarlar pak'a olduğu gibi girer (küçük; managed'ın da host'un da işine yarar).

## Build kancaları (`DigitoyEngine.Build`, aotcompiler/source; editör Link ile alır)
```csharp
public interface IAndroidProjectHook { void OnGenerate(AndroidProject p); }
public interface IIosProjectHook     { void OnGenerate(IosProject p); }
public interface IWasmProjectHook    { void OnGenerate(WasmProject p); }
```
`Assets/**/Editor/**/*.cs` → `<Ad>.Editor.dll` (aotcompiler ve editör aynı Roslyn derlemesini yapar; normal .NET'e karşı; aotcompiler
`--editor-engine <DigitoyEngine.dll>` ile editörün .NET dll'ini alır, CLI'da dev/kurulu yoldan bulur). Kanca API'si (bu tipler +
`PluginSettingsStore`) ayrı **`DigitoyEngine.Build.dll`** ([aotcompiler/hooks/](../aotcompiler/hooks/DigitoyEngine.Build.csproj), kaynak
`aotcompiler/source/ProjectHooks.cs`); hem aotcompiler hem DigitoyEditor buna ProjectReference verir → Editor.dll derlemesi
DigitoyEngine.Build + DigitoyEditor + DigitoyEngine'i birlikte referanslar, tipler tek kimlikli (CS0433 yok). Editor/ kodu hem
build kancası hem `FieldDrawer` içerebilir. Uygulayan tipler ad sırasıyla
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

## Games — tek proje, N bağımsız oyun (2026-10-18; varyant modelinin yerini aldı)
Hedef (digitoyclient): Gin/Yüzbir/Okey tek repoda, kod ve ortak asset paylaşır, ama **ayrı store/FB/sunucu kimlikleri** → oyun başına
ayrı APK. **Proje = ortak `Assets/` + N oyun tanımı (N ≥ 1).** Mod yok ("varyant varsa/yoksa" yok), taban/overlay yok: her oyunun TAM
ayar dosyası kendi klasöründe. Eski "varyant" modeli (kök PlayerSettings + `Variants/` kısmi overlay + korunan alan + turuncu şerit +
`excludeFolders`) kaldırıldı — ayarların %90'ı oyuna göre farklıyken diff katmanı gereksiz karmaşıklıktı; tema (%5 fark) katman olarak kalır.

```
ProjectSettings/Games/<ad>/PlayerSettings.asset   oyunun TÜM ayarları: kimlik, scenes/startScene, themes/activeTheme, Display, Build, android/ios
ProjectSettings/Games/<ad>/Plugins/<Ad>.asset      oyunun plugin Config'i (appId vb.)
UserSettings/EditorSettings.asset  workingGame + games[]   kullanıcı: çalışılan oyun + oyun başına son sahne/tema/kamera/cihaz
Assets/Shared/, Assets/Games/<Ad>/                konvansiyon (zorunlu değil): ortak / oyuna özel içerik
```
- Oyun listesi = `Games/*` klasörleri (`PlayerSettings.IsValidGameName`: harf/rakam/_/-). Oyun yoksa editör proje klasörü adıyla üretir.
  Eski yerleşim ilk açılışta otomatik taşınır ([Project.MigrateLegacy](../editor/Project.cs), DocNode düzeyinde: taban + overlay alan
  birleşimi → tam dosya; sahnesiz nötr taban oyun olmaz; `variants/activeVariant/excludeFolders` yazılmaz; eski dosyalar silinir).
- **Tema listesi oyuna ait** (`themes/activeTheme` PlayerSettings içinde). Adlar oyunlar arası ortak sözlük (xmas, ramazan); Shared prefab'taki
  katman adını listesine alan her oyunda etkin, almayanda atıl. `theme.check` → `undeclared` (dosyadaki katman adı oyunun listesinde yok).
- **Kod ortak ve hep derlenir** (dışlama yok): yüzbirdeki kırılma gin'de çalışırken görünür. Pak/trim/plugin canlılığı o oyunun
  `scenes/startScene` kümesinden türer → diğer oyunun asset'i/kodu APK'ya girmez.
- **Çalışılan oyun** (`Project.Game`, toolbar "Game" combosu, `game.set`) = Play/Build/pak bağlamı. Geçiş = çalışma alanı geçişi
  ([App.SetWorkingGame](../editor/EditorApp.cs)): editör durumu `EditorSettings.games[]`'e saklanır/geri gelir (son sahne, working theme, kamera,
  cihaz, yön), plugin ayar cache düşer, **oyun kodu taze ALC ile yeniden yüklenir** (statikler sıfır — `AppBoot.IsReady`/`Game.Current`
  tuzağı yok). Açık sahnede kaydedilmemiş değişiklik varsa sahne değiştirilmez (uyarı). `DigitoyEditor.exe <proje> --game <ad>`: o süreçte
  oyun sabit, EditorSettings'e yazılmaz → aynı projede ikinci editör başka oyunda (iki AI paralel).
- **Player Settings penceresi**: üstte oyun sekmeleri (düzenlenen oyun; `EditorSettings.playerSettingsGame`), çalışılan oyun "(working)".
  Düzenlemek için çalışılan oyunu değiştirmek GEREKMEZ. "Games" sayfası: Work on / Remove / Add (seçili sekmenin kopyası — ortak alanlar
  için şablon). Plugin sayfaları sekmenin oyununa yazar (`PluginSettingsEditor.Entries(project, assets, game)`).
- **RPC**: `game.list/set/create/remove`; `player.build` çalışılan oyunu kurar; tema komutları oyunun listesine yazar.
- **Build**: `PlayerBuilder` → `--game <ad>` (zorunlu; aotcompiler [Game.cs](../aotcompiler/source/Game.cs): boşsa tek oyunlu projede o, çok
  oyunluda hata). Çıktı `Build/<game>/{game.pak, <Ad>.exe, android/, ios/, wasm/}`, AOT cache `Library/aot/<game>/<target>`.
  `PluginSettingsStore(root, engineNet, game)` → `Games/<game>/Plugins/` (`ObjectSerializer.LoadInto` reflection ile). Pak `.project` v3 son
  alan artık oyun adı → `GameHost.GameName` (eski pak'ta boş). Desktop host pak'ı önce exe'nin yanında arar (`./game.pak`).
  TUZAK: `Run in Browser` statik kökü hâlâ `Build/wasm`.
- **Test**: `GameTests` (23): eski yerleşimden geçiş (taban+2 overlay+plugin birleşimi), keşif, SetGame/kayıt, CreateGame kopya, RemoveGame
  (son oyun korunur), bilinmeyen/boş proje, ProjectBinary game, EditorSettings stash/restore + round-trip. Uçtan uca: Sandbox → `Games/Sandbox/`
  geçişi, `player.build windows` → `Build/Sandbox/Sandbox.exe`, `[host] game: Sandbox`; digitoyclient → `Games/{ginrummy,yuzbir}`,
  `game.set` + Yüzbir login→lobby, `Build/yuzbir/game.pak`.

## AppLovin MAX — Faz 1: Android, interstitial + rewarded, AppLovin + AdMob adapter (2026-10-18)
Kaynak: digitoyclient `Assets/Plugins/AppLovin/` (örnek kopyası `Projects/Sandbox/Assets/Plugins/AppLovin/` + `Scripts/PluginProbe.cs`).
Facebook kalıbı birebir; motor/editör/aotcompiler **değişmedi**.
- **Runtime/AppLovin.cs** `Plugin<AppLovin.Config>`: `Config` = `[Required, Sensitive] sdkKey`, `AdUnits android/ios` (interstitial/rewarded),
  `admobAppIdAndroid/Ios`, `testDeviceIds`, `verboseLogging`, `sdkVersionAndroid/Ios`, `List<Adapter> mediation {network, android, ios}`.
  API: `Init()` (bir kez; başarıda unit'ler yüklenir), `Interstitial`/`Rewarded : FullscreenAd` (`Load`, `IsReady`, `Task<ShowResult> Show(placement)`
  → `Shown|Rewarded|NotReady|Failed|Unavailable|Timeout|Busy`), `SetPrivacy`, `ShowMediationDebugger`, `OnLoaded/OnLoadFailed/OnRevenue` (ILRD).
  **Oto-retry managed'da** (2^n s, ≤64 s), hidden'da yeniden yükleme managed'da → host ince. Platform seçimi HOST'ta: init mesajı
  `units.android` + `units.ios` taşır, host kendininkini alır (managed platform bilmez). Protokol dosya başlığında.
- **Editor/AppLovinHooks.cs**: `AppLovinAndroid` → `ResString admob_app_id`, `Dependency applovin-sdk:<sdkVersionAndroid>` + her adapter için
  `com.applovin.mediation:<network>-adapter:<android>`, network'e göre ek `MavenRepo` (pangle/mintegral/ironsource/chartboost…). **Bağımlılıklar
  library modülünde değil hook'ta** (Config'e bağlı liste app seviyesine) — sürüm güncelleme = Config'i düzenle, "Integration Manager" yok.
  `AppLovinIos` iskelet (SPM core, GADApplicationIdentifier, NSUserTrackingUsageDescription, SKAdNetwork tek giriş) — Faz 2'de adapter'lar.
  `AppLovinMock`: init ok, load→loaded, show→displayed/revenue/rewarded/hidden; `NoFill/FailShow/SkipReward` anahtarları.
- **Android/**: library modülü (`compileOnly applovin-sdk` yalnız derleme için), manifest `com.google.android.gms.ads.APPLICATION_ID` + `AD_ID`,
  Kotlin `AppLovinPlugin : DEPlugin("AppLovin")` (`AppLovinSdkInitializationConfiguration` sdkKey mesajdan, `MaxInterstitialAd`/`MaxRewardedAd`
  listener → `send(event)`, `MaxAdRevenueListener`). SDK 13'te `setIsAgeRestrictedUser` yok (bayrak yok sayılır).
- **Kullanım**: digitoyclient `Shared/Services/AdService.cs` (`ShowRewarded(placement) → Task<bool>`, oyun kodu AppLovin tipini bilmez), `AppBoot`
  `AdService.I.Init().Forget()`. Config oyun başına `Games/<ad>/Plugins/AppLovin.asset`.
- **Doğrulama**: editör mock ile `[ads] loaded interstitial/rewarded (Mock)`; Sandbox `player.build android` → `plugins.deps.gradle.kts`'te
  applovin-sdk + google-adapter, `plugin_strings.xml` admob id, `Plugins.kt` kaydı; Gradle 8.9 `:plugins:AppLovin:compileDebugKotlin` **gerçek SDK
  13.6.4'e karşı hatasız**; `:app:dependencies` → applovin 13.6.4 + play-services-ads 25.5.0 + facebook 17.0.0 çakışmasız (`ForceVersion` gerekmedi).
  Cihaz testi (gerçek sdkKey + MAX test unit'leri) ve iOS Faz 2 bekliyor. Sürümler Maven metadata'dan okundu (tahmin değil).

## IGameBoot — oyun girişi, sahneden bağımsız (2026-10-19)
Sorun: init (`AppBoot.Ensure`) Login sahnesindeki `LoginFlow` alanlarından tetikleniyordu → Lobby'den Play'de servisler yok; ayarlar
(`Game.asset`, `Settings.asset`) ayrı dosyalara saçılmıştı. Tartışılan ve REDDEDİLEN yollar: kalıcı Boot sahnesi + `Scene.Persistent`
(render/input yalnız aktif sahne; aktif-düşüş koruması gerekti → fazla), Boot prefab'ı her sahneye instantiate (GO gereksiz),
`PlayerSettings` türetme (motor tipinin oyun kodunda türemesi Project yüklemesini kırar, çok derine işler).
- **Model:** `IGameBoot { void Boot(); }` ([PlayerSettings.cs](../engine/managed/PlayerSettings.cs)); `PlayerSettings.boot` = `[Serializable, Asset]`
  bir sınıfın `.asset`'i (arayüz tipli alan = polimorfik asset referansı; Inspector asset seçici). Alanlar **gömülü** (GameDefinition,
  NetworkSettings `[Asset]` değil) → oyun başına TEK dosya `Assets/Games/<Oyun>/Boot.asset`. Sahne/GO/prefab yok.
- **Çağrı:** `GameHost.RunGameBoot(assets, key)` — release'te `BootWith` katalog+pak hazır, **startScene'den önce**; editörde
  `PlayMode.SpawnPlayScene` doc spawn'dan önce (açık sahne ne olursa olsun). Hata loglanır, oyun düşmez. `GameHost.GameBoot` nesneyi tutar.
  `Boot()` içinde `Scene.Active`/GO kullanma; idempotent ol (tekrar Play, hot reload).
- **Pak:** boot anahtarı kök (nesne + referansları pak'a girer), `ProjectBinary` **v4** `boot` alanı (v3 okunur). Trim: `[Serializable]`
  pak tipi `Inline` kökü, `Boot()` arayüz dispatch'le erişilebilir → ek kök gerekmedi.
- **Editör tuzağı (yaşandı):** `Project.Load` PlayerSettings'i oyun kodu derlenmeden okur → `boot` (oyun tipi) null kalıyordu. Çözüm:
  `Project.BindAssets(assets)` — `RebuildCatalog` sonrası ayarlar `App.Assets` ile yeniden okunur (Player instance değişir; pencere Bind'i yeniler).
- **İkinci tuzak (yaşandı, eski):** `PluginTests` finally'de `MockHost.Clear()` → açılışta tüm oyun mock'ları siliniyordu ("AppLovin: host'ta
  kayıtlı değil"); Login'den Play yalnız watcher reload'ı sonrası çalışıyordu. Fix: `MockHost.Unregister(name)`, test yalnız kendi mock'unu kaldırır.
- digitoyclient: `Shared/Services/ClientBoot.cs` (`game`, `network`, `appVersion` → `AppBoot.Ensure`), `Games/{GinRummy,Yuzbir}/Boot.asset`,
  `Game.asset`/`Shared/Settings.asset` silindi, `LoginFlow` boot alanları kalktı (hazır değilse hata logu). Doğrulama: Lobby'den Play →
  `[host] boot: ClientBoot` + `[ads] loaded (Mock)`; Login'den Play eskisi gibi.


Proje/plugin `Editor/` kodu `<Ad>.Editor.dll`'e derlenir ve artık **DigitoyEditor.dll'e referans verir** → editörün uzantı noktalarını
kullanır; motor hiç değişmez. [FieldDrawers.cs](../editor/FieldDrawers.cs):
- `[CustomFieldDrawer(typeof(Sahip), "alan")] class X : FieldDrawer { Measure(ctx); Draw(ctx, ref y, right) → değişti mi }` — ObjectDrawer o
  alanı çizmeden önce kayda bakar (Inspector, Player Settings, plugin sayfaları aynı yol). `ctx`: Owner, Field (şema), Path (UI anahtarı),
  LabelW/Indent, ProjectRoot/LibraryPath (önbellek), `Value/SetValue`. Attribute Editor kodunda; Runtime tipi/alanı dokunulmaz.
- `EditorUi` public yüz: `Label/Text/RowH/LabelFont/SmallFont/LabelColor/DimColor/WarnColor/ErrorColor/Log/Warn/Error` (Inspector iç üyeleri
  internal kaldığı için). `Gui.*`/`GuiRenderer` motorun `DigitoyEngine.Editor` namespace'inde (using gerekir).
- Kayıt: `GameCode.LoadEditorCode` Editor.dll'i yükleyince `[CustomFieldDrawer]` tipleri taranır (mock kaydıyla birlikte), reload'da temizlenir.
  Instance (tip, alan) başına tek; aynı alan iki nesnede çiziliyorsa durumu `ctx.Path` ile ayır. İstisna Console'a, alan tek satır olarak düşer.
- Watcher: `Editor/` altındaki `.cs` değişimi de derleme+reload tetikler (önceden yok sayılıyordu → hook/mock/drawer değişikliği için kapat-aç gerekiyordu).
- IDE: `<Ad>.Editor.csproj` üretilir (`Assets/**/Editor/**`, DigitoyEditor + DigitoyEngine + hook API + `<Ad>.Game.csproj` referansı) → completion.
- İlk kullanıcı: AppLovin `mediation` listesi ([AppLovinMediationDrawer.cs](../../digitoyclient/Assets/Plugins/AppLovin/Editor/AppLovinMediationDrawer.cs)):
  AppLovin'in Unity Integration Manager JSON'u (`unity.applovin.com/max/1.0/integration_manager_info`) → 26 network + güncel Android/iOS
  sürümleri + SDK sürümü; `Library/AppLovin/networks.json` önbelleği (çevrimdışı çalışır, 24 saatte tazelenir, Refresh düğmesi). Satır
  "Google AdMob — android 25.5.0.0 / ios 13.11.0.0", güncel değilse `Update`, `×`; `Add network…` combosu (ekli olanlar gizli); elle yazım yok.
  Artefakt adı DownloadUrl'den (`com/applovin/mediation/<x>-adapter`). `sdkVersionAndroid/Ios` için de "latest → Update".
- TUZAK (otomasyon): utility penceredeki combo popup'ına `ui.click window=…` tıkı gitmiyordu (eşleme yalnız ana pencere için) → düzeltildi.


`[Sensitive]` alan attribute'u ([Attributes.cs](../engine/managed/Serialization/Attributes.cs)); isim sezgisi YOK, yalnız attribute. Varsayılan işaretliler:
`AndroidSettings.keystorePassword/keyPassword` (PlayerSettings.android; eski `UserSettings/AndroidSigning.asset` ilk açılışta buraya taşınır ve
silinir — 2026-10-18); plugin yazarı Config'inde `[Required, Sensitive] clientToken` gibi işaretler.
- **RPC/MCP çıkış filtresi** ([RpcRedact.cs](../editor/RpcRedact.cs)): editör + motor + oyun/plugin assembly'lerindeki `[Sensitive]` alan adları kümesi
  (reload'da `RebuildCatalog` tazeler); `RpcHost.Ok` **her** yanıtı (deferred dahil) süzer: DocNode ağacında hassas anahtarın değeri `***`,
  tüm string skalerlerde satır içi `key: v` / `"key": v` / `key=v` → `***` (asset.read YAML, log.tail, scene.dump metinleri). Kapsam alan adı
  bazlı: aynı adlı masum alanlar da maskelenir (kabul; yanlış-negatiften iyi).
- **Girdi**: `prop.set`/`scene.apply` hassas alana `***` yazma → hata; `asset.write` içeriğinde `key: ***` satırı → hata (maske dosyaya girmesin).
  Gerçek değer yazmak serbest (AI yazabilir, okuyamaz).
- **Inspector**: `ObjectDrawer` hassas string'i `••••` çizer, "Show/Hide" ile açılır-düzenlenir.
- `rpc.guide` rules'a kural eklendi. `RedactTests` (12 PASS); canlı: `asset.write` → `asset.read` `clientToken: ***`, maskeyi geri yazma reddi.
- SINIR: pak/APK içeriğine girer (cihazda zaten okunabilir). `UserSettings/` secret dosyası KALDIRILDI (kullanıcı kararı): imza parolaları
  dahil her ayar `ProjectSettings/`'te git'e girer (oyun başına Games/<ad>/); gizlilik yalnız AI'a karşı. MCP köprüsü pass-through.

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
  (Plugins.kt, gradle dosyaları, Package.swift, Info.plist, plugins.js).
- **Android cihaz doğrulaması (2026-10-18, digitoyclient `Assets/Plugins/Facebook`, ANE-LX1):** gerçek `facebook-login:17.0.0` ile
  `:dehost` + `:plugins:Facebook` Gradle library modülü derlendi, `Plugins.kt`/`plugin_strings.xml`/`CustomTabActivity` intent-filter üretildi,
  `Plugin.Send → PluginHost.dispatch → LoginManager` açıldı, `onActivityResult` forward'ı ve `send → inbox → OnMessage` yanıt yolu çalıştı
  (iptal → `FacebookAuthCancelled`; placeholder App ID ile SDK "invalid app id" = beklenen). Motor/aotcompiler/kabukta değişiklik gerekmedi.
  iOS/Xcode/tarayıcı doğrulaması hâlâ bekliyor.
- Kalan: WASM `onerror` ekranı/sendBeacon örneği; editörde "referenced" etiketi kaba metin taraması (kesin kaynak build logu).
