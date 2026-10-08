// ---------------------------------------------------------------------------
// de_fs.h — Platform-bagimsiz asset depolama + async job tablosu
// ---------------------------------------------------------------------------
// Motorun TEK depolama yuzeyi. Managed taraf dosya sistemi gormez (System.IO
// AOT corelib'ine girmez); yalnizca handle + offset + uzunluk konusur.
//
//   Backend'ler (ayni sozlesme):
//     desktop / iOS : stdio (fopen/fseek/fread)             [bu dosya]
//     Android       : stdio + APK assets/ icin fd+offset    [bu dosya, __ANDROID__: "asset:<ad>" yolu]
//     web (wasm)    : fetch -> bellek, read = memcpy          [de_fs_web.c, ileride]
//
//   Is modeli: TUM IO ve decode worker'da, managed TEK thread. Her cagri aninda
//   bir job id doner; sonucu ana thread her frame de_job_poll ile ceker. Bloklayan
//   cagri YOK (web'de de ayni sozlesme: fetch callback'i job'i tamamlar).
//
//   de_fs_open da bir job'dur (web'de ag; desktop'ta aninda biter): poll sonucu
//   *w = handle. Handle kapanmadan once ona bagli tum job'lar bitmis olmali.
// ---------------------------------------------------------------------------
#ifndef DE_FS_H
#define DE_FS_H

#if defined(DE_BUILD_DLL) && defined(_WIN32)
#define DE_FS_API __declspec(dllexport)
#else
#define DE_FS_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Dosya acar -> job. poll: 1 -> *w = handle (>= 0); -1 -> acilamadi.
// Android: "asset:<ad>" yolu APK assets/ icinden fd+offset ile acilir (kopya yok; asset SIKISTIRILMAMIS
// olmali — Gradle `noCompress`). Oncesinde de_fs_set_asset_manager cagrilmis olmali.
DE_FS_API int de_fs_open(const char *path);
#if defined(__ANDROID__)
DE_FS_API void de_fs_set_asset_manager(void *aassetManager); // AAssetManager* (host JNI'den)
#endif
DE_FS_API long long de_fs_size(int handle);
DE_FS_API void de_fs_close(int handle);

// [offset, offset+length) araligini okur -> job; poll: *data/*len (malloc'lu, job ile serbest).
// handle < 0 ise `path` kullanilir (editor loose dosyalari). length 0 = dosyanin tamami.
// rawLength != length ise aralik zlib'lidir: worker inflate eder, *len = rawLength.
DE_FS_API int de_fs_read(int handle, const char *path, long long offset, long long length, long long rawLength);

// Ayni okuma + piksel decode (DTEX hizli yol, degilse stb_image) -> job;
// poll: *data = RGBA8 (satir 0 altta), *w/*h boyut.
DE_FS_API int de_fs_decode(int handle, const char *path, long long offset, long long length, long long rawLength);

// 0 = bekliyor, 1 = tamam, -1 = hata. Yalniz ana thread cagirir.
DE_FS_API int de_job_poll(int job, void **data, long long *len, int *w, int *h);
// Sonuc tamponunu serbest birakir ve slotu bosaltir (poll 1/-1 sonrasi SART).
DE_FS_API void de_job_free(int job);

#ifdef __cplusplus
}
#endif
#endif
