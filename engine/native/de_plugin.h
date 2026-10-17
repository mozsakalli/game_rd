// ---------------------------------------------------------------------------
// de_plugin.h — Platform SDK plugin koprusu (docs/plugins.md)
// ---------------------------------------------------------------------------
// Core protokolu iki yonde UTF-8 string. PUSH YOK:
//   managed -> host : de_plugin_send(name, msg)  -> host run_on_main -> plugin onMessage(msg)
//   host -> managed : de_plugin_post(name, msg)  -> surec omurlu INBOX (her thread'den yazilir)
//                     -> frame basinda ana thread de_plugin_peek/pop ile ceker (Plugin.PumpInbox)
// Inbox statik: de_app_init'ten BAGIMSIZ var olur -> soguk baslangicta (Application.onCreate,
// didFinishLaunching) post edilen deep link / push kaybolmaz. Plugin basina sinir: DE_PLUGIN_INBOX_CAP;
// dolunca en eski duser (stderr'e bir satir).
// Kayit tablosu host'un: her platform host'u (Kotlin/ObjC/JS tabani) plugin'lerini
// de_plugin_register ile bildirir; onMessage callback'ini host MAIN THREAD'de cagirir
// (dispatch host'ta: Handler/dispatch_async/anında). Desktop'ta kayit yok -> available 0.
// ---------------------------------------------------------------------------
#ifndef DE_PLUGIN_H
#define DE_PLUGIN_H

#if defined(DE_BUILD_DLL) && defined(_WIN32)
#define DE_PLUGIN_API __declspec(dllexport)
#else
#define DE_PLUGIN_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define DE_PLUGIN_INBOX_CAP 1024

// Host plugin'in mesaj alicisi. Host bunu main thread'de cagirmakla yukumlu (de_plugin_send host'un
// dispatch'ine gider: de_host_plugin_dispatch). msg yalniz cagri suresince gecerli.
typedef void (*DePluginOnMessage)(void *user, const char *msg);

// Host -> kayit (init'te, de_app_init'ten once ya da sonra fark etmez). Ayni ad ikinci kez -> ustune yazar.
DE_PLUGIN_API void de_plugin_register(const char *name, DePluginOnMessage onMessage, void *user);
// Kayitli mi (managed Plugin.Available).
DE_PLUGIN_API int de_plugin_available(const char *name);
// Managed -> host. Kayitli degilse 0. Kopyalar; host'un main-thread dispatch'ine verir.
DE_PLUGIN_API int de_plugin_send(const char *name, const char *msg);
// Host -> managed inbox. HER thread'den. Kopyalar.
DE_PLUGIN_API void de_plugin_post(const char *name, const char *msg);
// Inbox: siradaki kaydin toplam bayt sayisi ("name\0msg", sonlandirici yok); 0 = bos. YALNIZ ana thread.
DE_PLUGIN_API int de_plugin_peek(void);
// Siradaki kaydi buf'a kopyalar ve kuyruktan cikarir; donus = yazilan bayt, cap yetmezse 0 (kayit kalir).
DE_PLUGIN_API int de_plugin_pop(char *buf, int cap);
// Bekleyen kayit sayisi (test).
DE_PLUGIN_API int de_plugin_pending(void);

// HOST SAGLAR (de_app.h tarzi sozlesme): fn(user, name, msg)'yi main thread'de cagir. de_plugin_send bunu
// kullanir; name/msg malloc'lu kopyadir, host cagridan sonra de_plugin_free_dispatch ile birakir.
// Desktop/wasm: tek thread -> dogrudan cagirip birakabilir. Zayif varsayilan: hemen cagirir.
typedef void (*DePluginDispatchFn)(void *user, const char *name, const char *msg);
DE_PLUGIN_API void de_plugin_set_dispatcher(void (*run_on_main)(DePluginDispatchFn fn, void *user, char *name, char *msg));
DE_PLUGIN_API void de_plugin_free_dispatch(char *name, char *msg);

#ifdef __cplusplus
}
#endif
#endif
