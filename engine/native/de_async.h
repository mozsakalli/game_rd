// ---------------------------------------------------------------------------
// de_async.h — Motorun TEK async tamamlanma kuyrugu (docs/network.md)
// ---------------------------------------------------------------------------
// Alt sistem basina poll/pump YOK. Dosya IO (de_fs), ag (de_net), platform SDK
// callback'leri: hepsi isi bitince buraya POD bir tamamlanma yazar. Managed taraf
// her frame basinda TEK yerden (NativeAsync.Pump) kuyrugu bosaltir, job id'ye
// bagli Task'i/dinleyiciyi cozer. Managed koda yabanci thread'den giris yok.
//
//   job  : de_async_new_job ile alinan id (tum alt sistemler ayni sayactan).
//   code : >= 0 sonuc/olay turu, < 0 hata (hata her zaman job'un SON tamamlanmasidir).
//          DE_ASYNC_EVENT biti (yalniz code >= 0): job'u bitirmeyen cok-sefer olay
//          (WS mesaji, ilerleme); yoksa job'un tek ve son sonucu.
//   data : malloc'lu tampon; sahipligi kuyruga, oradan managed'a gecer
//          (de_async_free ile birakilir ya da managed pointer'i sahiplenir).
//   a, b : musteriye ozel (decode: w,h; HTTP: status; ...). Kuyruk anlam bilmez.
// ---------------------------------------------------------------------------
#ifndef DE_ASYNC_H
#define DE_ASYNC_H

#if defined(DE_BUILD_DLL) && defined(_WIN32)
#define DE_ASYNC_API __declspec(dllexport)
#else
#define DE_ASYNC_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define DE_ASYNC_EVENT 0x40000000

typedef struct DeCompletion
{
    int job;
    int code;
    void *data;
    long long len;
    int a, b;
} DeCompletion;

// Yeni job id (> 0). Her thread'den.
DE_ASYNC_API int de_async_new_job(void);
// Tamamlanma/olay yazar. HER thread'den. data sahipligi kuyruga gecer.
DE_ASYNC_API void de_async_complete(int job, int code, void *data, long long len, int a, int b);
// Biriken tamamlanmalari FIFO sirayla out'a kopyalar, kuyruktan cikarir; donus = adet. YALNIZ ana thread.
DE_ASYNC_API int de_async_drain(DeCompletion *out, int max);
// Belirli job'un ilk tamamlanmasini cikarir (editor bloklayan bekleme). 1 = bulundu. Yalniz ana thread.
DE_ASYNC_API int de_async_take(int job, DeCompletion *out);
// Managed yuzey (struct marshal yok): sirayla bir tamamlanma pop eder, 1 = alindi 0 = bos.
// `job` > 0 ise yalniz o job'un tamamlanmasini arar (take), 0 ise FIFO basi (drain).
DE_ASYNC_API int de_async_next(int job, int *outJob, int *code, void **data, long long *len, int *a, int *b);
// Kuyrukta bekleyen adet (test).
DE_ASYNC_API int de_async_pending(void);
// data tamponunu birakir (malloc/free).
DE_ASYNC_API void de_async_free(void *data);

#ifdef __cplusplus
}
#endif
#endif
