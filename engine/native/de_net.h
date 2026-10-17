// ---------------------------------------------------------------------------
// de_net.h — HTTP + WebSocket (docs/network.md). Platformun HAZIR ag yigini:
//   Windows WinHTTP (async) | Apple NSURLSession | Android java.net (JNI, Net.kt: HttpURLConnection + RFC 6455) | wasm fetch/WebSocket (EM_JS)
// TLS/sertifika/proxy/cookie OS'un; biz yalniz URL veririz. Bloklayan cagri YOK.
// Sonuclar de_async kuyruguna (de_async.h); burada poll yok. Yalniz ana thread cagirir.
//
// HTTP tamamlanmasi (job = donus degeri):
//   code 1  : a = HTTP status, data = [yanit basliklari UTF-8 "K: V\r\n..."][govde], b = baslik bayt uzunlugu, len = toplam
//   code -1 : a = 0, data = hata metni UTF-8 (olabilir NULL)
// WebSocket (job = baglanti id; de_ws_free'ye kadar gecerli):
//   DE_WS_OPEN    (olay)  : baglandi
//   DE_WS_MESSAGE (olay)  : data/len = mesaj, a = 1 metin / 0 ikili
//   DE_WS_CLOSE   (son)   : a = kapanis kodu, data = sebep UTF-8
//   -1            (son)   : data = hata metni
// ---------------------------------------------------------------------------
#ifndef DE_NET_H
#define DE_NET_H
#include "de_async.h"

#if defined(DE_BUILD_DLL) && defined(_WIN32)
#define DE_NET_API __declspec(dllexport)
#else
#define DE_NET_API
#endif

#ifdef __cplusplus
extern "C"
{
#endif

#define DE_WS_OPEN (DE_ASYNC_EVENT | 1)
#define DE_WS_MESSAGE (DE_ASYNC_EVENT | 2)
#define DE_WS_CLOSE 2

    // headers: "K: V\n" satirlari (NULL/bos olabilir). body NULL/0 = govdesiz. timeoutMs <= 0 = varsayilan (30 s).
    DE_NET_API int de_http_request(const char *method, const char *url, const char *headers, const void *body, int bodyLen, int timeoutMs);
    // Istegi iptal eder; tamamlanma gelmeyebilir (managed kaydi zaten dusurmustur).
    DE_NET_API void de_http_cancel(int job);

    DE_NET_API int de_ws_open(const char *url, const char *headers);
    // Gonderim native kuyruga alinir (tek seferde bir yazim ucar). 0 = kuyruga alindi, -1 = baglanti yok/kapali.
    DE_NET_API int de_ws_send(int ws, const void *data, int len, int isText);
    DE_NET_API void de_ws_close(int ws, int code, const char *reason);
    // CLOSE/-1 tamamlanmasi geldikten (ya da hic acilmadan vazgecildikten) sonra: native kaynaklar birakilir.
    DE_NET_API void de_ws_free(int ws);

#ifdef __cplusplus
}
#endif
#endif
