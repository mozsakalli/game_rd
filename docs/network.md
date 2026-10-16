# Network (HTTP + WebSocket) ve Tek Async Tamamlanma Kuyruğu — Plan

## Hedef
Oyun kodu `await Http.GetAsync(url)` / `WebSocket.ConnectAsync(url)` yazar; her platformda OS'un **hazır** ağ yığını
kullanılır (TLS/sertifika/proxy/cookie/HTTP2 hepsi OS'un, biz yalnız URL veririz). Thread/GC kuralı bozulmaz: managed tek
thread, native tamamlanmalar frame başında ana thread'de toplanır.

## Karar: alt sistem başına Pump YOK → tek `de_async` kuyruğu
Bugün `AsyncJobs.Pump` de_fs'e özel (`de_job_poll(job, data, len, w, h)` — texture decode'un `w/h`'ı ABI'de), müzik
`de_music_state` poll'u, prefs senkron. Network üçüncü desen olurdu. Bunun yerine **tek genel mekanizma**; de_fs ve
network bunun iki müşterisidir, yarınki IAP/reklam/indirme/modül yükleme de öyle.

### Native (`c_runtime/de_async.h/.c`, platformsuz)
```c
typedef struct DeCompletion { int job; int code; void* data; long long len; int a, b; } DeCompletion;
int  de_async_new_job(void);                                              // id tahsisi (de_fs/de_net/... hepsi buradan)
void de_async_complete(int job, int code, void* data, long long len, int a, int b); // HER thread'den; data sahipliği kuyruğa
int  de_async_drain(DeCompletion* out, int max);                          // YALNIZ ana thread, frame başında
```
- MPSC: üreticiler spinlock ile yazar (de_app olay kuyruğu deseni), tüketici tek seferde boşaltır. Dolunca büyür (malloc,
  ana thread drain'de küçültülmez) — tamamlanma düşürülmez (olay kuyruğunun aksine kayıp kabul edilemez).
- `code`: `>= 0` iş sonucu / olay türü, `< 0` hata. `DE_ASYNC_EVENT` biti: tek-sefer değil, çok-sefer olay (WS mesajı,
  ilerleme). Kuyruk payload anlamını bilmez; `a/b` müşteriye özel (decode `w/h`, HTTP `status`).
- `data` malloc'lu; managed kopyalayıp `de_async_free(data)` ile bırakır (ya da pointer'ı sahiplenir: decode pikselleri).

### Managed (`engine/managed/NativeAsync.cs`)
```csharp
public struct Completion { public int Job, Code, A, B; public IntPtr Data; public long Len; public bool Ok => Code >= 0; }
public static class NativeAsync {
    public static Task<Completion> Await(int job);                 // tek sefer
    public static void Listen(int job, Action<Completion> onEvent); // çok sefer (Unlisten ile)
    public static Task<bool> NextFrame();
    public static void Pump();                                      // frame'de TEK yer: drain → TCS/olay; continuation'lar burada
}
```
- `GameHost.Frame` ve editör PlayMode `NativeAsync.Pump()` çağırır (tek çağrı; `AsyncJobs.Pump` kalkar).
- `AsyncJobs.Await/ReadAsync/Release`, `Frame.Next()` ince sarmalayıcı olarak **korunur** (çağıranlar değişmez).
- wasm32: extern imzalarında `IntPtr` yok (`void*`/`byte*`); `Completion.Data` dışa dönük sarmalayıcıda IntPtr.

### de_fs taşınması
- `de_job_t` tablosu + `de_job_poll/de_job_free` kalkar; worker bitince `de_async_complete(job, kind, data, len, w, h)`.
- `de_fs_open/read/decode` id'yi `de_async_new_job`'dan alır. Handle sözleşmesi aynı. wasm: senkron koşup hemen complete.
- Kabul: açılış testleri (Lifecycle/Serialization/...) ve Sandbox (Windows exe, .NET dev host, wasm, Android) birebir.

## Ağ ABI'si (`engine/native/de_net.h`)
```c
// HTTP: istek -> job; sonuç de_async: code = 1 bitti / <0 hata, a = HTTP status, data = gövde, len = uzunluk.
// Yanıt başlıkları gövdenin ÖNÜNDE "K: V\n...\n\n" olarak (tek tampon; b = başlık bayt uzunluğu).
int  de_http_request(const char* method, const char* url, const char* headers /* "K: V\n" */,
                     const void* body, int bodyLen, int timeoutMs);
void de_http_cancel(int job);

// WebSocket: job = bağlantı. Olaylar de_async EVENT: DE_WS_OPEN, DE_WS_MESSAGE (data/len, a = isText), DE_WS_CLOSE (a = kod, data = sebep), DE_WS_ERROR.
int  de_ws_open(const char* url, const char* headers);
int  de_ws_send(int ws, const void* data, int len, int isText);   // native kuyruğa, bloklamaz
void de_ws_close(int ws, int code, const char* reason);
void de_ws_free(int ws);                                           // CLOSE/ERROR sonrası
```
Yeni poll ABI'si YOK: ağ yalnız `de_async_complete` çağırır.

## Platform backend'leri (3. parti yok; tek istisna Android)
| Platform | HTTP | WebSocket | Yer |
|---|---|---|---|
| Windows (editör + player) | WinHTTP async (`WINHTTP_FLAG_ASYNC`, status callback) | WinHTTP WS (`WinHttpWebSocketCompleteUpgrade`, Win8.1+) | `de_net.c` `_WIN32` |
| macOS / iOS | `NSURLSession dataTask` | `NSURLSessionWebSocketTask` (iOS 13 / macOS 10.15+) | `__APPLE__ && __OBJC__` (audio_shim deseni) |
| Android | OkHttp | OkHttp (framework'te WS yok) | `platforms/android/java/.../Net.kt` + JNI bind (`Music.kt`/`Prefs` deseni, `JNI_OnLoad`); Gradle dep + manifest `INTERNET` |
| wasm | `EM_JS fetch` (AbortController: timeout/cancel) | JS `WebSocket` | `__EMSCRIPTEN__` EM_JS |

Editör ve .NET dev host **aynı** `digitoyengine_native.dll` (WinHTTP) → editör Play ile release birebir; `.NET HttpClient` yok.

## Managed yüzey (`engine/managed/Net.cs`), ince
```csharp
public static class Http {
    public static Task<HttpResponse> GetAsync(string url, HttpOptions opt = null);
    public static Task<HttpResponse> PostAsync(string url, byte[] body, string contentType = "application/json", HttpOptions opt = null);
    public static Task<HttpResponse> SendAsync(HttpRequest req);   // Method, Url, Headers, Body, TimeoutMs
}
public sealed class HttpResponse { public int Status; public bool Ok; public byte[] Body; public string Text; public string Error; public string Header(string name); }
public sealed class HttpOptions { public int TimeoutMs = 30000; public Dictionary<string,string> Headers; public CancelToken Cancel; } // corelib'de CancellationToken yok

public sealed class WebSocket : IDisposable {
    public static Task<WebSocket> ConnectAsync(string url, HttpOptions opt = null);
    public WebSocketState State;                                   // Connecting/Open/Closed
    public void Send(string text); public void Send(byte[] bin);
    public Task<WsMessage> ReceiveAsync();                         // await döngüsü (iç kuyruk)
    public event Action<WsMessage> OnMessage; public event Action<int,string> OnClose; // olay (Pump'ta, ana thread)
    public Task CloseAsync(int code = 1000, string reason = "");
}
```
Her ikisi de yalnız `NativeAsync.Await/Listen` kullanır; kendi poll'u yok.

## Kapsam dışı (bilinçli)
Cert pinning, proxy/cookie yönetimi, HTTP server, UDP/raw socket. **JSON**: `engine/managed/Serialization/Json.cs`
(`Json.Parse` → `JsonNode`, `JsonWriter` UTF-8, `Json.FromJson<T>/ToJson` reflection binder, `[JsonField]`) — engine-notes "JSON" bölümü.

## Fazlar
| Faz | İş | Kabul |
|---|---|---|
| 0a | `de_async` + `NativeAsync` (altyapı; de_fs'e dokunulmaz) | birim test: çok thread'den complete → tek drain sıra/kayıp yok |
| 0b | de_fs → de_async; `AsyncJobs` sarmalayıcı; tek Pump | açılış testleri + Sandbox (Win exe, dev host, wasm, Android) birebir |
| 1 | `de_net` Windows WinHTTP (HTTP+WS) + `Net.cs` + editör testi (localhost `HttpListener` + WS echo) | editör Play'de GET/POST/WS round-trip |
| 2 | wasm EM_JS backend | Sandbox tarayıcıda aynı test |
| 3 | Android `Net.kt` (OkHttp) + JNI + manifest | cihazda aynı test |
| 4 | Apple NSURLSession (H6 iOS host ile) | cihazda aynı test |
| 5 (ops.) | `de_http_download(url, writablePath)` + ilerleme olayı; remote `Module.LoadAsync` | büyük pak byte[]'siz iner |

## Durum
- 2026-10-11: plan kabul edildi (kullanıcı: "itiraz yok").
- **Faz 0a+0b BİTTİ**: `engine/native/de_async.{h,c}` (MPSC, spinlock, büyüyen dizi; `de_async_new_job/complete/drain/take/next/
  pending/free`; managed yüzey `de_async_next` tek-tek pop — struct marshal yok, wasm32 pointer boyutu sorunu olmaz),
  `engine/managed/NativeAsync.cs` (`Completion`, `Await/Listen/Unlisten/TryTake/NextFrame/Pump`, `Frame.Next`).
  `de_fs.c`: job slot tablosu + `de_job_poll/de_job_free` **kalktı**; malloc'lu job FIFO (mutex+sem) → worker `de_async_complete`
  (read: data/len; decode: data=RGBA8, a=w b=h; open: a=handle). `AsyncJobs` NativeAsync üstünde ince sarmalayıcı
  (`Release(IntPtr)` artık pointer alır). `AssetDatabase.Pending` → class + `Listen` ile sonuç; Tick bütçeli bağlama korundu.
  `Sokol.AssetPoll/AssetFreeJob` + `de_asset_poll/de_asset_free_job` silindi; editör bloklayan bekleme `NativeAsync.TryTake`
  (yalnız o job, Pump koşmaz). Tek Pump: `GameHost.Frame`/`EditorApp` → `AssetDatabase.Tick` → `NativeAsync.Pump`.
  Hata kodu (<0) EventBit taşımaz: `IsEvent` yalnız `Code >= 0` iken (ilk test bunu yakaladı).
  Doğrulama: `editor/AsyncQueueTests.cs` 12 PASS (Await/Listen/TryTake/8×500 çok thread kayıpsız-sıralı/Frame.Next); diğer
  açılış testleri birebir; editör Login sahnesi texture/atlas/font OK; Sandbox.exe (AOT) pak→17 asset→sahne; wasm tarayıcıda OK.
  Android derlemesi denenmedi (CMakeLists'e `de_async.c` eklendi; stdatomic NDK'da var).
- **Faz 0c (iptal) BİTTİ**: corelib `CancellationToken/CTS` (alt küme, LIFO callback), Task iptal durumu + WhenAll/WhenAny,
  `NativeAsync.Await/Listen(..., ct, onCancel)`, `Frame.Next(ct)`, `Wait.Seconds/Timeout`, `Component.destroyCancellationToken`.
  Yan ürün: aotcompiler GenericInstantiator ertelenmiş-klon düzeltmesi (engine-notes). HTTP/WS API'si `ct` alır; zaman aşımı
  native'de (`TimeoutMs`), token yalnız kullanıcı/yaşam döngüsü iptali.
- **Faz 1 BİTTİ (Windows WinHTTP + Net.cs)**: `engine/native/de_net.{h,c}` — `_WIN32`: WinHTTP async (HTTP: Send→Receive→
  QueryDataAvailable/ReadData döngüsü, başlıklar+gövde tek tampon b=başlık uzunluğu; WS: `WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET` →
  `WinHttpWebSocketCompleteUpgrade`, gönderim kuyruğu tek uçan yazım, parçalı mesaj birikimi). `ws://`/`wss://` → http(s) eşlenir
  (WinHTTP tanımaz). Diğer platformlar: "desteklenmiyor" tamamlanması (faz 2-4). Link `-lwinhttp` (editor DLL, player exe, publisher).
  `engine/managed/Net.cs`: `Http.Get/Post/SendAsync(req, ct)` → `HttpResponse{Status,Ok,Body,Text,Error,Header()}`;
  `WebSocket.ConnectAsync(url, ct)`, `Send(string|byte[])`, `ReceiveAsync(ct)` / `OnMessage`, `CloseAsync`, `OnClose`, `Dispose`.
  `editor/NetTests.cs` 18 PASS (localhost HttpListener: GET/başlık/POST echo/JSON/404/300 KB/bağlantı hatası/iptal/3 paralel;
  WS metin+ikili echo, kuyruk sırası, kapanış el sıkışma 1000, sunucu kapanışı 4000, iptal, upgrade reddi). Sandbox.exe AOT linklendi
  (Net.cs stub'suz); AOT çalışma zamanında gerçek istek denemesi henüz yok (Sandbox'ta ağ kullanan script yok).
  **WinHTTP dersleri**: `WinHttpWebSocketClose` bekleyen Receive varken hata verir → `Shutdown` kullan, karşı CLOSE frame'i
  bekleyen alma ile gelir; `WinHttpWebSocketQueryCloseStatus` DWORD döner (NO_ERROR=0; BOOL gibi okuma = hep 1006).
  Editörde DLL `dotnet build` ile kopyalanır — yalnız `build_editor.cmd` koşturmak BAYAT DLL çalıştırır.
  AOT frontend: struct üzerinde `??=` + `this` spill hatalı C üretti → `WsMessage` class yapıldı (frontend hatası açık).
- **AOT çalışma zamanı doğrulandı (Windows)**: `GameHost` dev kancası — PlayerPrefs `net.probe` = taban URL ise boot sonrası GET/POST/WS
  round-trip loglar (`[net-probe] ...`). Sunucu: editör `DE_NET_SERVER_PORT=18500` ile açılırsa NetTests sunucusu CORS açık kalır.
  Sandbox.exe (`Build/prefs.dat`: `net.probe<TAB>http://127.0.0.1:18500`) → GET 200 / POST echo / WS echo+ikili / close 1000 OK.
- **Faz 2 (wasm) YAZILDI, derlendi, TEST EDİLMEDİ** (kullanıcı: sonra). `de_net.c` `__EMSCRIPTEN__`: EM_JS `fetch` (AbortController
  zaman aşımı/iptal, başlıklar "HTTP/1.1 st" + k: v satırları + gövde tek tampon) + JS `WebSocket` (binaryType arraybuffer;
  onclose → DE_WS_CLOSE kod/sebep, onerror yok sayılır — tarayıcı ardından close(1006) verir; özel başlık tarayıcıda yok).
  JS→C geri çağrı: `EMSCRIPTEN_KEEPALIVE de_net_done(job, code, data, int len, a, b)` (`_de_net_done`); tamponlar `_malloc`.
  TLS/CORS tarayıcının (https/wss bedava). Test için: tarayıcı konsolunda `localStorage['de:/index.html:net.probe']='http://127.0.0.1:18500'`
  (PlayerPrefs web anahtar öneki) + editör `DE_NET_SERVER_PORT=18500` → sayfayı yenile, konsolda `[net-probe]`.
