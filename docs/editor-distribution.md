# Editör Dağıtımı (Publish) — Plan

## Hedef
Editör bir ürün gibi dağıtılır: zip indir → aç → `DigitoyEditor.exe`. Makinede yalnız **.NET 9 Runtime**.
Player/modül build için **hiçbir kurulum yok**: toolchain (clang+lld, mingw-w64 CRT/import lib'leri) pakette gömülü.
Repo, kaynak kod, `dotnet build`, `.csproj` üretimi son kullanıcı makinesinde yok.

## Kararlar
| Karar | Seçim | Gerekçe |
|---|---|---|
| Çıktı yeri | Repo DIŞI sabit kök: Windows `C:\DigitoyPublish\DigitoyEditor-<VERSION>\`, macOS `~/DigitoyPublish/...`; `DIGITOY_PUBLISH_DIR` ile değiştirilebilir | git şişmesin; makineler arası elle kopyala/birleştir |
| Sürüm | repodaki `VERSION` dosyası (elle bump) | commit hash'i makineler arası değişir, sürüm sabit kalmalı |
| Publisher | `tools/EditorPublisher` .NET aracı, **parametresiz**, idempotent ("eksiği/bayatı tamamla") | her OS kendi üretebildiğini üretir; sıra bağımsız |
| Toolchain | **llvm-mingw**, pakete GİRMEZ: Windows builder yoksa `%LOCALAPPDATA%\DigitoyEngine\toolchains\llvm-mingw-<tag>-ucrt-x86_64\` altına indirir, budar (~330 MB), kullanır (`aotcompiler/source/Toolchain.cs`; publisher aynı dosyayı Link ile kullanır). `AOT_CLANG` / `DIGITOY_TOOLCHAINS` ile ezilebilir | Windows SDK dağıtılamaz; Windows için Platform Support paketi gereksiz — tek adım: indir-kullan |
| Runtime | **kaynak**: `sdk/native/{c_runtime,engine}` (.c/.h); player native'i kullanıcı makinesinde aotcompiler tarafından clang ile derlenir (mtime cache). Prebuilt yalnız editörün kendi `digitoyengine_native.dll/.dylib`'i | Android/iOS modeliyle aynı (kaynak proje), ABI uyumsuzluğu riski yok, varyant yönetimi yok |
| ABI kuralı | generated.c + runtime + native aynı clang çağrısı/aynı define setiyle derlenir (`DIGITOYENGINE_DEBUG` RtFrame yerleşimini değiştirir) | sessiz bellek bozulmasını önler |
| Cross hedefler | linux-x64: Windows'ta Docker (opsiyonel). **android/ios: toolchain YOK** — Android Studio / Xcode projesi üretilir, kullanıcı derler. wasm32: emsdk Platform Support olarak indirilir. Bkz. `platform-hosts.md` | NDK/Xcode kullanıcıda zaten var |
| macOS/iOS | yalnız Mac'te (Xcode) | ObjC/Metal/Apple SDK |
| Managed | framework-dependent publish, `Deterministic` + `PathMap` (iki OS'ta aynı IL) ; apphost'lar her RID için Windows'tan üretilir | RID'den bağımsız tek kopya |
| Zip | tool yapmaz; birleştirme sonrası elle | |

## Paket düzeni
```
DigitoyEditor-<VERSION>/
  DigitoyEditor.exe  DigitoyEditor(mac apphost)  *.dll *.pdb  digitoyengine_native.dll  libdigitoyengine_native.dylib
  sdk/aotcompiler/  aotcompiler.dll(+deps, Roslyn)  Digitoy.CoreLib.dll  DigitoyEngine.dll(DE_AOT)  (+pdb)   ← prebuilt IL, aotcompiler yanında
  sdk/native/c_runtime/  vmrt vmint corelib de_app host_desktop (.c/.h)        ← player/host KAYNAK (prebuilt yok)
  sdk/native/engine/     sokol_shim audio_shim de_fs (+stb/dr .h)  sokol/  glfw-master/{include,src}
  native/include/ vmrt.h vmint.h
  native/<rid>/   digitoyengine_static_release.lib  digitoyengine_static_debug.lib  (+sembol)  manifest.json
                  rid: win-x64 win-arm64 linux-x64 android-arm64 wasm32 osx-arm64 ios-arm64
  toolchains/llvm-mingw/   (Windows hedefleri; Linux/Mac host'tan Windows'a cross da buradan)
  templates/NewProject/
  manifest.json   (VERSION, mevcut RID'ler, eksikler + gerekli host/toolchain)
```
Her bileşen kapalı birim: kendi `manifest.json`'u (girdi kaynak hash'i, toolchain, define seti, VERSION). Birleştirme = klasör kopyası.
Publisher kökü mevcut klasörleri tarayıp yeniden yazar; bir makinede birleştirme sonrası tekrar koşmak paketi "tam" mühürler.

## Akış
```
Windows: publisher → managed + apphost'lar + win natives (+ docker: linux/android/wasm) → C:\DigitoyPublish\...
macOS:   publisher → osx/ios natives + editör dylib → ~/DigitoyPublish/...      (sıra fark etmez)
Siz:     dizinleri birleştir → zip
```

## Editörün bu düzenden çalışması için gereken (repo'dan arınma)
1. `SdkLayout`: exe yanında `sdk/` → kurulu mod; yoksa repo dev modu (bugünkü akış). Tüm `RepoRoot` kullanımları buradan.
2. Oyun kodu derlemesi: csproj + `dotnet build` → **in-process Roslyn** (SDK gereksinimi kalkar; PDB üretilir).
3. Player çekirdeğinin oyundan ayrılması → **`platform-hosts.md` H1–H2**: `PlayerApp` → engine `GameHost` (platformsuz) + `host_desktop.c`
   (GLFW döngüsü, `main`, crash dizini) + `de_app_*` ABI; `DigitoyPlayer.aot.dll` kalkar. Oyun scriptleri Roslyn ile
   `<Ad>.Game.dll`; katalog AOT reflection ile kurulur (Registry/`de_game_register` yok — `registry-removal.md`).
4. aotcompiler kurulu mod: corelib + engine **prebuilt IL** (`sdk/`), runtime prebuilt lib, c_runtime kaynağı yok (dev modda Roslyn ile taze).
5. Açılış testleri yalnız dev modda/bayrakla; `Projects/Sandbox` varsayılanı yerine kurulu modda Open/New Project; log/crash dosyaları kullanıcı dizinine.

## Fazlar
| Faz | İş | Süre |
|---|---|---|
| 0a | **mingw spike**: llvm-mingw ile vmrt/corelib/vmint + sokol/glfw + selftest derlenip çalışıyor mu (VEH, longjmp, PDB) | 1 gün |
| 0b | `VERSION`, `tools/EditorPublisher` (kök kuralı, manifest/hash, host tespiti, win-x64 lib release+debug, header, managed publish + apphost'lar, toolchain indirme/yerleştirme, şablon, kök manifest), `SdkLayout` | 2 gün |
| 0c | Docker: yalnız linux-x64 (opsiyonel; android/wasm buradan çıktı → `platform-hosts.md`) | 1 gün |
| 0d | macOS ayağı (Mac'te koşup doğrulama) | 1 gün |
| 1 | RepoRoot→SdkLayout göçü, testler bayrak arkası, New Project + şablon | 1–2 gün |
| 2 | GameCode → Roslyn in-process | 1–2 gün |
| 3 | → `platform-hosts.md` H1 (GameHost + host_desktop + ABI) ve H2 (aotcompiler Roslyn + prebuilt IL) | 3–4 gün |
| 4 | Clang seçimi (paket içi → kurulu → hata), README/gereksinimler | 1 gün |

Kabul (her faz): publish dizinini başka klasöre kopyala → oradan editörü aç → proje aç → script derle → player build.

## Durum
- **Faz 0a mingw spike GEÇTİ**: llvm-mingw ile runtime + AOT selftest + interpreter MSVC ile birebir (50/53); player native'leri
  (sokol/glfw/ses/de_fs) derlendi, player exe pak+sahne+modülle 12 s çalıştı. Gerekenler: `audio_shim.c`'de mingw için
  `MFCreateMFByteStreamOnStream` prototipi, link'e `-luuid`. Script: `aotcompiler/tests/mingw_spike.ps1`.
- **Model revizyonu (Unity Hub gibi)**: Editör çekirdeği (managed + yalnız editörün açılması için şart olan native DLL) ayrı;
  **Platform Support modülleri** (toolchain + prebuilt statik lib + header) ayrı paketler, editör içinden indirilir. Toolchain
  publisher'ın **cache**'inde durur (`C:\DigitoyPublish\cache\llvm-mingw`, budanmış ~330 MB), pakete girmez.
- **Çekirdek publisher çalışıyor**: `dotnet run --project tools/EditorPublisher` → `C:\DigitoyPublish\<VERSION>\DigitoyEditor-<VERSION>\`
  (12 MB: editör + Roslyn + engine + mingw ile derlenmiş `digitoyengine_native.dll` + `sdk/` IL + şablon + manifest). İdempotent.
  Paket başka klasöre kopyalanıp çalıştırıldı: Sandbox açıldı, oyun kodu derlendi, tüm açılış testleri geçti, RPC ayağa kalktı.
- **Faz 2 BİTTİ — GameCode in-process Roslyn**: `editor/GameCode.cs` artık csproj üretmiyor, `dotnet build` çağırmıyor.
  Referanslar: paylaşılan framework TPA + `DigitoyEngine.dll`; `DEBUG;TRACE;DE_GAME`; portable PDB; tanılar csc biçiminde
  (`path(l,c): error CSxxxx:`) → AssetWatcher hata yolu aynen çalışıyor. İlk yükleme, hata raporu ve hot-reload doğrulandı;
  paket (12.2 MB) yabancı dizinden SDK çağrısı olmadan Sandbox'ı derledi. Sahne editleme + kod yazma için makinede yalnız
  **.NET Runtime** yeterli; SDK yalnız player/modül build'i için (Faz 3'e kadar) gerekiyor.
- Engine csproj: `-p:DigitoyEditorBuild=true` → Release'te DE_EDITOR (editör publish'i için).
- **Faz 1 (kısmi) — `editor/SdkLayout.cs`**: `Installed` (exe yanında `sdk/aotcompiler/aotcompiler.dll`) → aotcompiler `dotnet <dll>` ile,
  çalışma dizini `%LOCALAPPDATA%\DigitoyEngine\aot-work` (obj/ oraya); dev → `dotnet run --project` (csproj yalnız dev'de). PlayerBuilder,
  ModuleBuilder, AotCompatCheck (`sdk/aotcompiler` prebuilt IL / dev `aotcompiler/obj/aot-il`) buradan geçer. Doğrulama: paketli editörden
  RPC `player.build` → kurulu sdk + mingw ile Sandbox.exe 45 s. Kalan: `ProjectSwitcher` Projects/ listesi, DefaultProjectPath, açılış testleri bayrağı, New Project.
- **Sırada**: Faz 0.5 Platform Support modülü (publisher `modules/win-x64-support` + editör içi indirici), sonra Faz 1–4.
