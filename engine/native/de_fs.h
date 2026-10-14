// ---------------------------------------------------------------------------
// de_fs.h — Platform-bagimsiz asset depolama + async job tablosu
// ---------------------------------------------------------------------------
// Motorun TEK depolama yuzeyi. Managed taraf dosya sistemi gormez (System.IO
// AOT corelib'ine girmez); yalnizca handle + offset + uzunluk konusur.
//
//   Backend'ler (ayni sozlesme):
//     desktop / iOS : stdio (fopen/fseek/fread)             [bu dosya]
//     Android       : stdio + APK assets/ icin fd+offset    [bu dosya, __ANDROID__: "asset:<ad>" yolu]
//     web (wasm)    : host fetch -> heap, de_fs_mount_memory; read = memcpy [bu dosya, __EMSCRIPTEN__]
//
//   Is modeli: TUM IO ve decode worker'da, managed TEK thread. Her cagri aninda
//   bir job id doner; sonucu de_async kuyruguna yazilir (de_async.h), ana thread
//   her frame tek drain ile toplar. Bloklayan cagri YOK (web'de de ayni sozlesme).
//
//   de_fs_open da bir job'dur (web'de ag; desktop'ta aninda biter): tamamlanma
//   a = handle. Handle kapanmadan once ona bagli tum job'lar bitmis olmali.
//   Tamamlanma: code 1 = tamam (read: data/len; decode: data=RGBA8 satir 0 altta, a=w b=h), -1 = hata.
// ---------------------------------------------------------------------------
#ifndef DE_FS_H
#define DE_FS_H

#if defined(DE_BUILD_DLL) && defined(_WIN32)
#define DE_FS_API __declspec(dllexport)
#else
#define DE_FS_API
#endif

#ifdef __cplusplus
extern "C"
{
#endif

    // Dosya acar -> job. Tamamlanma: code 1 -> a = handle (>= 0); -1 -> acilamadi.
    // Android: "asset:<ad>" yolu APK assets/ icinden fd+offset ile acilir (kopya yok; asset SIKISTIRILMAMIS
    // olmali — Gradle `noCompress`). Oncesinde de_fs_set_asset_manager cagrilmis olmali.
    DE_FS_API int de_fs_open(const char *path);
#if defined(__ANDROID__)
    DE_FS_API void de_fs_set_asset_manager(void *aassetManager); // AAssetManager* (host JNI'den)
#endif
    DE_FS_API long long de_fs_size(int handle);
    DE_FS_API void de_fs_close(int handle);
#if defined(__EMSCRIPTEN__)
    // Web: dosya sistemi YOK (-sFILESYSTEM=0). Host, pak'i fetch ile heap'e alir ve bu yolla baglar; de_fs_open(path)
    // ayni yolu gorunce bellek handle'i verir (read = memcpy, decode = stbi_from_memory). Bellek host'a ait (free edilmez).
    DE_FS_API void de_fs_mount_memory(const char *path, const void *data, long long len);
    // Bagli bellek dosyasinin araligi (audio: pak icindeki muzik -> Blob). 1 = var.
    DE_FS_API int de_fs_mem_range(const char *path, const void **data, long long *len);
#endif

    // [offset, offset+length) araligini okur -> job; tamamlanma data/len (malloc'lu, de_async_free).
    // handle < 0 ise `path` kullanilir (editor loose dosyalari). length 0 = dosyanin tamami.
    // rawLength != length ise aralik zlib'lidir: worker inflate eder, len = rawLength.
    DE_FS_API int de_fs_read(int handle, const char *path, long long offset, long long length, long long rawLength);

    // Ayni okuma + piksel decode (stb_image) -> job;
    // tamamlanma data = RGBA8 ya da R8 (gray kaynak; kanal = len / (w*h)), satir 0 altta, a/b = w/h.
    DE_FS_API int de_fs_decode(int handle, const char *path, long long offset, long long length, long long rawLength);

    // --- Kalici anahtar/deger (DigitoyEngine.PlayerPrefs) --------------------------------------
    // iOS CFPreferences, Android SharedPreferences (host JNI_OnLoad de_android_prefs_bind), Web
    // localStorage, desktop <root>/prefs.dat. Degerler UTF-8 string; tip donusumu managed'da.
    DE_FS_API void de_prefs_set_root(const char *root);                // desktop dosya koku (digerleri yok sayar)
    DE_FS_API int de_prefs_get(const char *key, void **out, int *len); // 1 = var (*out malloc'lu, de_prefs_free)
    DE_FS_API void de_prefs_free(void *p);
    DE_FS_API void de_prefs_set(const char *key, const char *value);
    DE_FS_API void de_prefs_delete(const char *key);
    DE_FS_API void de_prefs_delete_all(void);
    DE_FS_API int de_prefs_has(const char *key);
    DE_FS_API void de_prefs_save(void); // desktop: diske yaz; iOS: synchronize; Android: apply
#if defined(__ANDROID__)
    DE_FS_API void de_android_prefs_bind(void *vm, void *env, void *cls); // com/digitoy/host/Prefs
#endif

#ifdef __cplusplus
}
#endif
#endif
