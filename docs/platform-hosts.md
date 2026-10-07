# Platform Host Modeli — Plan

## Özet
Her platformda **host** (pencere/surface, döngü, girdi, yaşam döngüsü, platform servisleri) platformun kendi diliyle/araçlarıyla
yazılır; **oyun** (generated.c + runtime + sokol_gfx + shim'ler) tek bir C kütüphanesidir ve host ile **sabit, küçük bir C ABI**
üzerinden konuşur. Yeni platform = yeni host + mevcut C kütüphanesi.

| Platform | Host | Oyun kütüphanesi | Publish çıktısı | Derleyen |
|---|---|---|---|---|
| Windows / macOS | `host_desktop.c` (GLFW + sokol_gfx GL/Metal) — bugünkü yol | statik link → tek exe | `Build/<Ad>.exe` / `.app` | editör (gömülü llvm-mingw / Xcode clang) |
| Android | Kotlin/Java `GameActivity` + `GLSurfaceView` (EGL/GLES3), JNI | `libgame.so` (CMake, NDK) | **Android Studio projesi** (kaynak + CMakeLists + Gradle) | kullanıcı: Android Studio |
| iOS | Swift/ObjC `GameViewController` + `CAMetalLayer` + `CADisplayLink` | statik `libgame.a` (Xcode target) | **Xcode projesi** | kullanıcı: Xcode |
| wasm32 | `index.html` + `host.js` + `host_wasm.c` (emscripten main loop, WebGL2) | `game.wasm` | `index.html + game.js + game.wasm + game.pak` | editör (indirilen emsdk — Platform Support) |

**Toolchain indirilenler yalnız:** llvm-mingw (Windows) ve emsdk (wasm). Android/iOS için NDK/Xcode kullanıcıda zaten var; biz **kaynak
veren proje** üretiriz. Bu, `editor-distribution.md`'deki Docker android/ios hedeflerini **iptal eder** (Docker en fazla linux-x64 için kalır).

## Oyun ABI'si (`de_app.h`, C, platform bağımsız)
```c
// Host -> oyun. HEPSİ TEK THREAD'DEN (render thread) çağrılır. Managed koda giriş yalnız buradan.
void de_app_init(const char* dataRoot, int fbw, int fbh, float scale);   // runtime init + managed Init
int  de_app_frame(float dt, int fbw, int fbh, float scale);            // managed Frame + GC safepoint; 0 = çık
void de_app_event(const DeEvent* e);                                   // kuyruğa yazar (thread-safe), Frame başında managed boşaltır
void de_app_pause(void); void de_app_resume(void);                     // ses sustur / dt sıfırla / (Android) GL kaynak kaybı bildirimi
void de_app_low_memory(void);
void de_app_shutdown(void);

// Olaylar: pointer(down/move/up/cancel, id, x, y), key, text, resize, back, focus. Mobil multitouch = id.
// Oyun -> host (host sağlar, oyun kütüphanesi extern bekler; eksikse link hatası = açık sözleşme):
const char* de_host_data_root(void);        // pak yolu kökü (desktop ".."; iOS bundle; Android: aşağıya bk.)
const char* de_host_writable_root(void);    // kayıt/log/crash
void        de_host_log(int level, const char*);
void        de_host_open_url(const char*);
int         de_host_message(const char* channel, const char* json);   // genel amaçlı oyun->host kanalı (SDK'lar için)
```
Host→oyun geri kanalı: `de_app_message(channel, json)` kuyruğa yazar; managed `Platform.OnMessage(channel, json)` olayıyla
Frame içinde alır. Böylece SDK callback'leri (reklam kapandı, satın alma bitti) thread/GC kuralını bozmadan oyuna ulaşır.

### Desteklenen kurallar (mevcut modelden değişmez)
- GC safepoint: toplama `de_app_frame` içinde managed Frame döndükten sonra (host_desktop'taki `gc_maybe_major` buraya taşınır).
- Girdi **poll değil olay**: host biriktirir (lock-free SPSC ring ya da mutex; UI thread → render thread), Frame başında boşaltılır.
  `GLFW.GetCursorPos/GetMouseButton/GetTime` managed'dan kalkar; zaman `dt` olarak gelir.
- Swap/drawable host'un: GL'de SwapBuffers, Metal'de drawable sunumu, wasm'da tarayıcı. Managed yalnız `Sokol.Commit()`.
- Exception = setjmp/longjmp + shadow stack: arm64 Android/iOS/wasm'da çalışır (wasm'da emscripten setjmp desteği var; `-sSUPPORT_LONGJMP`).
  Sadece **64-bit** hedefler (arm64-v8a, x86_64 emülatör; armeabi-v7a yok).

## Managed tarafı: `DigitoyEngine.GameHost` (PlayerApp'in platformsuz hâli)
`Boot` (pak → ProjectBinary → katalog → SceneLoader), `Frame(dt, w, h, scale)` (olay kuyruğunu boşalt → `Scene.UpdateAll` →
pointer → `DriveCameras`/encode/`Commit` → `Module.TickAll`), `Pause/Resume`, `Shutdown`. GLFW referansı **yok**.
Katalog: `de_game_register` extern'i → aotcompiler bunu oyunun `Generated.Registry$RegisterAll`'ına bağlar (DigitoyPlayer.aot.dll kalkar).
`player/` dizini **.NET dev host** olarak kalır: GLFW pencere açan ince `Main`, aynı `GameHost`'u sürer; `DE_EDITOR` loose-Assets yolu burada.

## Platform servisleri: ince köprü, SDK'lar oyun projesinde
Engine'e **SDK gömülmez**. İki mekanizma:
1. **Statik extern** (tercih): oyun kodu `[DllImport("__Internal")] static extern void Ads_Show(string placement);` bildirir; C/ObjC/JNI gövdesini
   oyun projesi **proje üretim kancasıyla** host'a enjekte eder. AOT'de DllImport zaten linker'da statik çözülür → ek runtime mekanizma yok.
   Android'de gövde `de_jni_env()` / `de_jni_activity()` ile Java'ya atlar; iOS'ta ObjC/Swift `@_cdecl`.
2. **Mesaj kanalı**: `Platform.Send(channel, json)` / `Platform.OnMessage` — kod yazmadan (yalnız Kotlin/Swift tarafı) entegrasyon için.
Engine'in verdiği "thin bridge" yalnızca: `de_jni_env/activity`, main-thread dispatch (`de_host_run_on_ui`), mesaj kuyruğu, ObjC tarafında
`DEHostBridge` singleton'ı. Müzik (`de_music_*`) de bu modelin ilk müşterisi: Android MediaPlayer / iOS AVPlayer host'ta.

## Proje üretimi: oyun kodundan genişletilebilir
`Assets/Editor/**/*.cs` → editörde ayrı Roslyn derlemesi `<Ad>.Editor.dll` (ref: `DigitoyEditor.Build` API; **AOT'ye ve Game.dll'e girmez**).
Kancalar:
```csharp
class MyAdsHook : IAndroidProjectHook {
    public void OnGenerate(AndroidProject p) {
        p.AddGradleDependency("com.google.android.gms:play-services-ads:23.0.0");
        p.AddManifestElement("application", "<meta-data android:name=\"...APPLICATION_ID\" android:value=\"...\"/>");
        p.AddPermission("android.permission.INTERNET");
        p.AddSource("Assets/Platforms/Android/AdsBridge.kt");           // Kotlin
        p.AddNativeSource("Assets/Platforms/Android/ads_bridge.c");      // DllImport gövdeleri
        p.AddActivityCallback(ActivityEvent.OnActivityResult, "AdsBridge.onActivityResult");
    }
}
class MyAdsHookIos : IIosProjectHook {
    public void OnGenerate(IosProject p) { p.AddSwiftPackage(url, version); p.AddPlist("GADApplicationIdentifier", id); p.AddSource(...); p.AddFramework("StoreKit"); p.AddCapability(...); }
}
```
Üretim **idempotent ve iki katmanlı**: `Build/android/` içinde `generated/` (her build silinip yazılır) + `user/` (ilk üretimde şablondan
kopyalanır, sonra **dokunulmaz**: imza, ikon, splash, elle Gradle ayarları). Kancalar yalnız `generated/`'a yazar; `user/` kullanıcıya ait.
Aynı kural `Build/ios/`, `Build/wasm/` için.

## Platform başına notlar / riskler
- **Android**
  - `libgame.so` Android Studio'nun NDK'sıyla **projenin içinde** derlenir (`externalNativeBuild` + CMakeLists): generated.c + `c_runtime/*.c` +
    `sokol_shim.c/audio_shim.c/de_fs.c` kaynak olarak projeye kopyalanır (`native/src/`), prebuilt yok → ABI uyumsuzluğu riski yok.
  - `System.loadLibrary("game")` APK içinden (Play Store dışı .so indirmek yok). Tek thread kuralı: `GLSurfaceView.Renderer.onDrawFrame` →
    `de_app_frame`; dokunmalar UI thread'den `de_app_event` kuyruğuna.
  - GL context kaybı: `setPreserveEGLContextOnPause(true)`; buna rağmen kaybolursa `de_app_resume(lost=1)` → sokol_gfx yeniden kurulur
    (texture/atlas yeniden yükleme: AssetDatabase'de "GPU kaynaklarını yeniden bağla" yolu gerekir — **ayrı iş**, ilk dilimde kabul edilen kısıt).
  - `game.pak` APK `assets/`'inde sıkıştırılmadan (`noCompress "pak"`) → `AAssetManager_openFileDescriptor` ile offset+len alıp mevcut
    `de_fs` dosya yolu (fd + offset) kullanılır; ya da ilk açılışta files dizinine kopya (basit, 2 kat disk). İlki hedef.
  - Ses: SFX sokol_audio → OpenSLES/AAudio hazır. Müzik → host MediaPlayer.
- **iOS**: dinamik .so/.dylib yok (App Store) → `libgame.a` Xcode target'ı olarak statik. Metal backend var (editörde kullanılıyor). Pak bundle
  resource. `CADisplayLink` → `de_app_frame`; `touchesBegan/Moved/Ended/Cancelled` → kuyruk. Arka plan: `applicationWillResignActive` → pause.
- **wasm**: emsdk Platform Support olarak indirilir (llvm-mingw gibi cache'te). Tek thread, `emscripten_set_main_loop` → `de_app_frame`.
  Pak: başlangıçta `fetch` + `MEMFS`'e yaz (ya da `--preload-file`); `de_fs` worker thread'i yok → senkron okuma (zaten bellekte). GL = WebGL2
  (sokol `SOKOL_GLES3`). `de_music_*` → `HTMLAudioElement` JS shim. Shader'lar: bugün GLCORE GLSL; GLES3 varyantı gerekir (sokol-shdc ya da elle `#version 300 es`).
  **En büyük risk** bu; spike ilk yapılır.
- **Desktop**: `host_desktop.c` runtime kaynağına girer (`c_runtime/host_desktop.c`), aotcompiler `main` üretmez; `_mkdir`/crash dizini host'ta.
  macOS'ta aynı dosya (GLFW + Metal yolu sokol_shim'de zaten var).

## Fazlar (editor-distribution.md Faz 3'ün yerini alır)
| Faz | İş | Kabul |
|---|---|---|
| H1 | `de_app.h` ABI + `host_desktop.c` + `GameHost` (managed) + olay kuyruğu; aotcompiler `main` üretmez, `de_game_register` bağlar; `player/` dev host | Sandbox.exe davranış birebir (Windows); .NET dev host çalışır |
| H2 | aotcompiler: Game.dll Roslyn in-process, corelib/engine prebuilt IL (dev'de taze), `DigitoyPlayer.aot.dll` kalkar; publisher `sdk/` güncel | paketten SDK'sız player build |
| H3 | Proje üretim altyapısı: `Build/<platform>/{generated,user}` kuralı, `Assets/Editor` → `<Ad>.Editor.dll`, `I*ProjectHook` API, `Platform.Send/OnMessage` + `de_host_message` | Windows'ta sahte kanca ile uçtan uca |
| H4 | wasm spike → wasm host + emsdk Platform Support + GLES3 shader yolu | Sandbox tarayıcıda |
| H5 | Android: Kotlin host şablonu + CMakeLists + Gradle + JNI köprü + AAsset pak + `de_music_*` MediaPlayer | Android Studio'da aç → derle → cihazda Sandbox |
| H6 | iOS: Xcode proje şablonu (xcodegen ya da elle pbxproj şablonu) + Swift host + Metal + AVPlayer | Xcode'da aç → derle → cihazda Sandbox |
| H7 | GL kaynak kaybı sonrası GPU kaynaklarını yeniden bağlama (Android) | pause/resume turu sorunsuz |

## Durum
- **H1 BİTTİ**: `engine/managed/GameHost.cs` (platformsuz: Init/Event/Frame/Pause/Resume/Shutdown, async Boot, `de_game_register`),
  `c_runtime/de_app.h` (ABI) + `de_app.c` (olay kuyruğu, runtime init, GC safepoint) + `host_desktop.c` (GLFW pencere/GL ctx,
  callback → kuyruk, `main`). aotcompiler `main` üretmez; `de_managed_*` + `de_game_register(cat)` köprülerini generated.c'ye ekler,
  stub kapısı host girişlerinden başlar. `player/PlayerApp.cs` → ince .NET dev host (GLFW poll → `GameHost.Event`; pak yoksa
  `GameHost.InitLoose`, DE_EDITOR). AOT'de `DigitoyPlayer` = `OutputType Library` (yalnız Registry + scriptler; H2'de Game.dll).
  Doğrulama: Sandbox.exe (clang) pak→registry→sahne→temiz çıkış; .NET dev host aynı; mingw spike player.exe linklendi.
- **H2 BİTTİ**: `aotcompiler/source/RoslynCompiler.cs` (ortak in-process Roslyn; editör `GameCode` de Link ile aynı dosyayı kullanır).
  aotcompiler `player`: `dotnet build` YOK — CoreLib (`c_runtime/corelib`, NoStdLib) ve Engine (`engine/managed`, NoStdLib+CoreLib,
  `DE_AOT`+renderer) dev'de Roslyn ile `obj/aot-il/` altına mtime cache'li; kurulu modda (`Digitoy.CoreLib.dll` aotcompiler.dll'in
  yanında, `c_runtime/corelib` yok) prebuilt IL okunur. Oyun = `Registry.g.cs` + `Assets/Scripts` (Editor/ hariç) → `<Ad>.Game.dll`
  her build. `aotcompiler sdk-il <dir>` publisher için prebuilt IL üretir → `sdk/aotcompiler/`. Engine/player csproj'lardan
  GameProject/AotCoreLib/NoStdLib blokları kalktı; `player/` saf .NET dev host. Doğrulama: Sandbox.exe aynı; selftest 50/53;
  editör compile/hata/hot-reload aynı; paketten `aotcompiler player` kurulu modda IL→C transpile'a kadar gidiyor, native statik
  lib + clang (Platform Support, editor-distribution Faz 0.5/4) eksik olduğu için orada duruyor — beklenen.
- **Native kaynak paketi**: publisher `sdk/native/{c_runtime,engine}` (85 .c/.h/.m: runtime + de_app/host_desktop + sokol/audio/de_fs
  shim'leri + sokol/ + glfw-master include/src; 5.5 MB). Player için **prebuilt native YOK** — aotcompiler `BuildNativeStaticLib`
  kaynaktan `obj/native-static/*.a` (mtime cache) derler; `build_native_static.cmd` bağımlılığı kalktı (dosya elle kullanım için duruyor).
  Doğrulama: paketten `dotnet sdk/aotcompiler/aotcompiler.dll player Sandbox` → kurulu mod, native kaynaktan derlendi, Sandbox.exe çalıştı
  (sistem clang'ı ile; paket içi toolchain seçimi = editor-distribution Faz 4).
- **Toolchain (Windows)**: Platform Support paketi YOK. `aotcompiler/source/Toolchain.cs` `MingwToolchain.Ensure`: llvm-mingw yoksa
  `%LOCALAPPDATA%\DigitoyEngine\toolchains\` altına indirir (HttpClient + ZipFile), budar (~330 MB), kullanır; publisher aynı dosyayı Link ile
  paylaşır (eski `C:\DigitoyPublish\cache\llvm-mingw` kullanılmıyor, silinebilir). `ClangPath`: `AOT_CLANG` > Windows mingw > PATH `clang`.
  Native statik lib cache'i toolchain'e göre damgalanır (MSVC/mingw nesneleri karışmaz). Doğrulama: sıfırdan indirme → Sandbox.exe çalıştı.
- **H5 (Android) İLK DİLİM BİTTİ**: Editör `Project ▸ Build Android Project` / RPC `player.build {target:"android"}` → `aotcompiler player --target android
  --app-id/--app-name/--app-version/--orientation` (PlayerSettings'ten) → `<proje>/Build/android/`: `generated/` (cpp: generated.c + c_runtime + native +
  CMakeLists; java: `com.digitoy.host.{GameLib,GameActivity}`; assets: game.pak — her build yeniden) + kabuk (ilk kez: Gradle kts, manifest, strings;
  sonra kullanıcıya ait). Kotlin host: GLSurfaceView (EGL3, `preserveEGLContextOnPause`), Renderer → `GameLib.frame`, dokunma → `event` (kuyruk),
  pause/resume, back → `EV_BACK` (`GameHost.BackRequested` yoksa Frame false → finish), `onDestroy` → süreç biter (runtime tek init'lik), pak assets →
  filesDir kopyası. C: `host_android.c` (JNI + stdout/stderr→logcat). Engine: `DE_RENDERER_GLES3` (`#version 300 es`), `DE_DESKTOP` (GLFW.cs),
  `NativeWindow.cs` tümü DE_EDITOR, `sokol_shim.c` Cocoa bloğu `__APPLE__`. Doğrulama (cihaz, arm64): NDK 27 libgame.so (strip 8.6 MB), Gradle 8.9
  headless debug+release APK (19 MB), logcat pak→registry→16 asset→`sahne hazir`, çizim OK (GLES3 shader), yatay yön, back→yeniden açılış temiz süreç,
  home→geri dönüş sorunsuz. Release = `assembleRelease` (NDK -O3, generated.c -O1, strip); debug varyantı da -O1 (CMake `$<CONFIG:Debug>`).
- **Android kalanlar (sırayla)**: (1) multitouch → `Pointer` id'li; (2) `de_music_*` → MediaPlayer köprüsü (H3 mesaj kanalı/`de_host_*` ile);
  (3) klavye/EV_TEXT → soft keyboard; (4) GL context kaybı → GPU kaynak yenileme (H7); (5) pak'ı kopyalamadan AAsset fd+offset ile okuma; (6) ikon/splash.
- Sırada: H3 (proje üretim altyapısı + kancalar), editor-distribution Faz 1 kalanları, Faz 4.
