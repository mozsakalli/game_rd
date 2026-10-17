// ---------------------------------------------------------------------------
// de_net.c — HTTP + WebSocket backend'leri (de_net.h). Sonuclar de_async kuyruguna.
//   _WIN32          : WinHTTP async (HTTP + WinHttpWebSocket*, Win8.1+). TLS/proxy OS'un.
//   __EMSCRIPTEN__  : EM_JS fetch + JS WebSocket.
//   __ANDROID__     : JNI -> com.digitoy.host.Net (HttpURLConnection + RFC 6455 Socket/SSLSocket; 3. parti yok).
//   diger (Apple)   : faz 4 (docs/network.md); simdilik "desteklenmiyor" tamamlanmasi.
// KOPYA SOZLESMESI (docs/network.md "Zero-GC"): alim dogrudan mesaj tamponuna, FIN'de pointer kuyruga devredilir
// (memcpy yok, 1 malloc/mesaj); gonderim bitisik ring'e tek kopya (tampon yazim bitene dek yasamali).
// Thread: WinHTTP callback'leri kendi thread havuzunda gelir; yalniz de_async_complete + kendi
// baglam kilidi kullanilir, managed'a dokunulmaz. Baglam yasam suresi: HANDLE_CLOSING'de serbest.
// ---------------------------------------------------------------------------
#include "de_net.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define DE_NET_DEFAULT_TIMEOUT_MS 30000
#define DE_NET_CHUNK 16384

static void *de_net_memdup(const void *p, size_t n)
{
    void *d = malloc(n ? n : 1);
    if (d && n)
        memcpy(d, p, n);
    return d;
}

static void de_net_fail(int job, const char *msg)
{
    size_t n = msg ? strlen(msg) : 0;
    de_async_complete(job, -1, n ? de_net_memdup(msg, n) : NULL, (long long)n, 0, 0);
}

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winhttp.h>

// ---- yardimcilar -------------------------------------------------------------------------
static wchar_t *to_wide(const char *s)
{
    if (!s)
        return NULL;
    int n = MultiByteToWideChar(CP_UTF8, 0, s, -1, NULL, 0);
    wchar_t *w = (wchar_t *)malloc((size_t)(n > 0 ? n : 1) * sizeof(wchar_t));
    if (!w)
        return NULL;
    if (n > 0)
        MultiByteToWideChar(CP_UTF8, 0, s, -1, w, n);
    else
        w[0] = 0;
    return w;
}

// wide -> UTF-8 (malloc'lu, *outLen bayt; NUL eklenmez)
static unsigned char *to_utf8(const wchar_t *w, int wlen, int *outLen)
{
    int n = WideCharToMultiByte(CP_UTF8, 0, w, wlen, NULL, 0, NULL, NULL);
    unsigned char *u = (unsigned char *)malloc((size_t)(n > 0 ? n : 1));
    if (u && n > 0)
        WideCharToMultiByte(CP_UTF8, 0, w, wlen, (char *)u, n, NULL, NULL);
    *outLen = n > 0 ? n : 0;
    return u;
}

static void win_error_text(DWORD err, char *out, size_t cap)
{
    HMODULE mod = GetModuleHandleA("winhttp.dll");
    DWORD flags = FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS | (mod ? FORMAT_MESSAGE_FROM_HMODULE : 0);
    DWORD n = FormatMessageA(flags, mod, err, 0, out, (DWORD)cap, NULL);
    if (n == 0)
        snprintf(out, cap, "WinHTTP error %lu", (unsigned long)err);
    else
    {
        while (n > 0 && (out[n - 1] == '\r' || out[n - 1] == '\n' || out[n - 1] == ' '))
            out[--n] = 0;
        size_t l = strlen(out);
        snprintf(out + l, cap > l ? cap - l : 0, " (%lu)", (unsigned long)err);
    }
}

typedef struct
{
    HINTERNET session, conn, req;
    int secure;
    wchar_t *path, *headers;
} de_url_t;

// URL'yi parcalar, session+connect+request acar. 0 = ok, aksi GetLastError.
static DWORD open_request(const char *method, const char *url, const char *headers, int timeoutMs, int websocket, de_url_t *out)
{
    memset(out, 0, sizeof *out);
    // WinHTTP ws:// / wss:// semalarini tanimaz: http(s) olarak parcala (upgrade istek secenegiyle yapilir)
    char *mapped = NULL;
    if (strncmp(url, "ws://", 5) == 0 || strncmp(url, "wss://", 6) == 0)
    {
        int wss = url[2] == 's';
        const char *rest = url + (wss ? 6 : 5);
        mapped = (char *)malloc(strlen(rest) + 9);
        if (!mapped)
            return ERROR_OUTOFMEMORY;
        strcpy(mapped, wss ? "https://" : "http://");
        strcat(mapped, rest);
        url = mapped;
    }
    wchar_t *wurl = to_wide(url);
    free(mapped);
    if (!wurl)
        return ERROR_OUTOFMEMORY;
    URL_COMPONENTS uc;
    memset(&uc, 0, sizeof uc);
    uc.dwStructSize = sizeof uc;
    uc.dwSchemeLength = uc.dwHostNameLength = uc.dwUrlPathLength = uc.dwExtraInfoLength = (DWORD)-1;
    if (!WinHttpCrackUrl(wurl, 0, 0, &uc))
    {
        DWORD e = GetLastError();
        free(wurl);
        return e;
    }
    int secure = uc.nScheme == INTERNET_SCHEME_HTTPS;
    wchar_t host[256];
    size_t hl = uc.dwHostNameLength < 255 ? uc.dwHostNameLength : 255;
    wcsncpy(host, uc.lpszHostName, hl);
    host[hl] = 0;
    size_t pl = uc.dwUrlPathLength + uc.dwExtraInfoLength;
    out->path = (wchar_t *)malloc((pl + 2) * sizeof(wchar_t));
    if (!out->path)
    {
        free(wurl);
        return ERROR_OUTOFMEMORY;
    }
    if (pl)
        wcsncpy(out->path, uc.lpszUrlPath, pl); // path+query bitisik
    out->path[pl] = 0;
    if (pl == 0)
    {
        out->path[0] = L'/';
        out->path[1] = 0;
    }
    INTERNET_PORT port = uc.nPort;
    free(wurl);

    out->session = WinHttpOpen(L"DigitoyEngine/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, WINHTTP_FLAG_ASYNC);
    if (!out->session)
        out->session = WinHttpOpen(L"DigitoyEngine/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, WINHTTP_FLAG_ASYNC);
    if (!out->session)
        return GetLastError();
    if (timeoutMs <= 0)
        timeoutMs = DE_NET_DEFAULT_TIMEOUT_MS;
    WinHttpSetTimeouts(out->session, timeoutMs, timeoutMs, timeoutMs, websocket ? 86400000 : timeoutMs); // ws: alma pratikte suresiz
    out->conn = WinHttpConnect(out->session, host, port, 0);
    if (!out->conn)
        return GetLastError();
    wchar_t *wmethod = to_wide(method && *method ? method : "GET");
    out->req = WinHttpOpenRequest(out->conn, wmethod, out->path, NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, secure ? WINHTTP_FLAG_SECURE : 0);
    free(wmethod);
    if (!out->req)
        return GetLastError();
    out->secure = secure;
    if (headers && *headers)
    {
        // "K: V\n" -> "K: V\r\n" (WinHTTP CRLF ister)
        size_t n = strlen(headers), j = 0;
        char *crlf = (char *)malloc(n * 2 + 3);
        if (!crlf)
            return ERROR_OUTOFMEMORY;
        for (size_t i = 0; i < n; i++)
        {
            if (headers[i] == '\r')
                continue;
            if (headers[i] == '\n')
            {
                crlf[j++] = '\r';
                crlf[j++] = '\n';
            }
            else
                crlf[j++] = headers[i];
        }
        if (j >= 2 && !(crlf[j - 2] == '\r' && crlf[j - 1] == '\n'))
        {
            crlf[j++] = '\r';
            crlf[j++] = '\n';
        }
        crlf[j] = 0;
        out->headers = to_wide(crlf);
        free(crlf);
    }
    return 0;
}

static void close_url(de_url_t *u)
{
    if (u->conn)
        WinHttpCloseHandle(u->conn);
    if (u->session)
        WinHttpCloseHandle(u->session);
    free(u->path);
    free(u->headers);
    u->conn = u->session = NULL;
    u->path = u->headers = NULL;
}

// ---- id -> baglam tablosu (ana thread yazar; callback'ler tabloya dokunmaz) --------------------
#define DE_NET_MAX 256
typedef struct
{
    int job;
    void *ctx;
} de_slot_t;
static de_slot_t http_slots[DE_NET_MAX], ws_slots[DE_NET_MAX];
static CRITICAL_SECTION net_lock;
static int net_started;
static void net_ensure(void)
{
    if (!net_started)
    {
        InitializeCriticalSection(&net_lock);
        net_started = 1;
    }
}
static int slot_put(de_slot_t *slots, int job, void *ctx)
{
    for (int i = 0; i < DE_NET_MAX; i++)
        if (!slots[i].ctx)
        {
            slots[i].job = job;
            slots[i].ctx = ctx;
            return 1;
        }
    return 0;
}
static void *slot_get(de_slot_t *slots, int job)
{
    for (int i = 0; i < DE_NET_MAX; i++)
        if (slots[i].ctx && slots[i].job == job)
            return slots[i].ctx;
    return NULL;
}
static void slot_del(de_slot_t *slots, void *ctx)
{
    for (int i = 0; i < DE_NET_MAX; i++)
        if (slots[i].ctx == ctx)
        {
            slots[i].ctx = NULL;
            return;
        }
}

// =========================================================================================
// HTTP
// =========================================================================================
typedef struct
{
    int job;
    de_url_t u;
    unsigned char *body; // istek govdesi kopyasi (SendRequest tamamlanana dek canli)
    unsigned char *buf;  // [basliklar][yanit govdesi]; ReadData dogrudan kuyruguna okur (ara chunk/memcpy yok)
    size_t len, cap;
    int headerLen, status;
    volatile LONG done; // tamamlanma yazildi (ya da iptal) -> callback'ler API cagirmaz
} http_t;

// buf'ta en az n bayt bos yer garantiler (realloc; amortize).
static int http_reserve(http_t *h, size_t n)
{
    if (h->len + n <= h->cap)
        return 1;
    size_t cap = h->cap ? h->cap * 2 : 65536;
    while (cap < h->len + n)
        cap *= 2;
    unsigned char *nb = (unsigned char *)realloc(h->buf, cap);
    if (!nb)
        return 0;
    h->buf = nb;
    h->cap = cap;
    return 1;
}

static int http_append(http_t *h, const void *p, size_t n)
{
    if (!http_reserve(h, n))
        return 0;
    memcpy(h->buf + h->len, p, n);
    h->len += n;
    return 1;
}

static void http_finish(http_t *h, int ok, DWORD err)
{
    if (InterlockedExchange(&h->done, 1))
        return;
    if (ok)
    {
        de_async_complete(h->job, 1, h->buf, (long long)h->len, h->status, h->headerLen);
        h->buf = NULL;
        h->len = h->cap = 0;
    }
    else
    {
        char msg[512];
        win_error_text(err, msg, sizeof msg);
        de_net_fail(h->job, msg);
    }
    WinHttpCloseHandle(h->u.req); // -> HANDLE_CLOSING: kalan kaynaklar
}

static void CALLBACK http_cb(HINTERNET hReq, DWORD_PTR ctx, DWORD status, LPVOID info, DWORD infoLen)
{
    http_t *h = (http_t *)ctx;
    if (!h)
        return;
    if (status == WINHTTP_CALLBACK_STATUS_HANDLE_CLOSING)
    {
        EnterCriticalSection(&net_lock);
        slot_del(http_slots, h);
        LeaveCriticalSection(&net_lock);
        h->u.req = NULL;
        close_url(&h->u);
        free(h->body);
        free(h->buf);
        free(h);
        return;
    }
    if (h->done)
        return;
    switch (status)
    {
    case WINHTTP_CALLBACK_STATUS_SENDREQUEST_COMPLETE:
        if (!WinHttpReceiveResponse(hReq, NULL))
            http_finish(h, 0, GetLastError());
        break;
    case WINHTTP_CALLBACK_STATUS_HEADERS_AVAILABLE:
    {
        DWORD code = 0, sz = sizeof code;
        WinHttpQueryHeaders(hReq, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &code, &sz, WINHTTP_NO_HEADER_INDEX);
        h->status = (int)code;
        DWORD hsz = 0;
        WinHttpQueryHeaders(hReq, WINHTTP_QUERY_RAW_HEADERS_CRLF, WINHTTP_HEADER_NAME_BY_INDEX, NULL, &hsz, WINHTTP_NO_HEADER_INDEX);
        if (hsz > 0)
        {
            wchar_t *wh = (wchar_t *)malloc(hsz);
            if (wh && WinHttpQueryHeaders(hReq, WINHTTP_QUERY_RAW_HEADERS_CRLF, WINHTTP_HEADER_NAME_BY_INDEX, wh, &hsz, WINHTTP_NO_HEADER_INDEX))
            {
                int ulen = 0;
                unsigned char *u = to_utf8(wh, (int)(hsz / sizeof(wchar_t)) - 1, &ulen);
                if (u)
                {
                    http_append(h, u, (size_t)ulen);
                    h->headerLen = ulen;
                    free(u);
                }
            }
            free(wh);
        }
        if (!WinHttpQueryDataAvailable(hReq, NULL))
            http_finish(h, 0, GetLastError());
        break;
    }
    case WINHTTP_CALLBACK_STATUS_DATA_AVAILABLE:
    {
        DWORD avail = info ? *(DWORD *)info : 0;
        if (avail == 0)
        {
            http_finish(h, 1, 0);
            break;
        }
        if (avail > DE_NET_CHUNK)
            avail = DE_NET_CHUNK;
        if (!http_reserve(h, avail))
        {
            http_finish(h, 0, ERROR_OUTOFMEMORY);
            break;
        }
        if (!WinHttpReadData(hReq, h->buf + h->len, avail, NULL))
            http_finish(h, 0, GetLastError());
        break;
    }
    case WINHTTP_CALLBACK_STATUS_READ_COMPLETE:
        if (infoLen == 0)
        {
            http_finish(h, 1, 0);
            break;
        }
        h->len += infoLen; // dogrudan buf kuyruguna okundu
        if (!WinHttpQueryDataAvailable(hReq, NULL))
            http_finish(h, 0, GetLastError());
        break;
    case WINHTTP_CALLBACK_STATUS_REQUEST_ERROR:
    {
        WINHTTP_ASYNC_RESULT *r = (WINHTTP_ASYNC_RESULT *)info;
        http_finish(h, 0, r ? r->dwError : ERROR_WINHTTP_INTERNAL_ERROR);
        break;
    }
    default:
        break;
    }
}

DE_NET_API int de_http_request(const char *method, const char *url, const char *headers, const void *body, int bodyLen, int timeoutMs)
{
    net_ensure();
    int job = de_async_new_job();
    http_t *h = (http_t *)calloc(1, sizeof(http_t));
    if (!h)
    {
        de_net_fail(job, "out of memory");
        return job;
    }
    h->job = job;
    DWORD err = open_request(method, url, headers, timeoutMs, 0, &h->u);
    if (err == 0 && body && bodyLen > 0)
    {
        h->body = (unsigned char *)de_net_memdup(body, (size_t)bodyLen);
        if (!h->body)
            err = ERROR_OUTOFMEMORY;
    }
    if (err == 0)
    {
        EnterCriticalSection(&net_lock);
        int ok = slot_put(http_slots, job, h);
        LeaveCriticalSection(&net_lock);
        if (!ok)
            err = ERROR_TOO_MANY_OPEN_FILES;
    }
    if (err != 0)
    {
        char msg[512];
        win_error_text(err, msg, sizeof msg);
        de_net_fail(job, msg);
        if (h->u.req)
            WinHttpCloseHandle(h->u.req);
        close_url(&h->u);
        free(h->body);
        free(h);
        return job;
    }
    WinHttpSetStatusCallback(h->u.req, http_cb, WINHTTP_CALLBACK_FLAG_ALL_NOTIFICATIONS, 0);
    if (!WinHttpSendRequest(h->u.req, h->u.headers ? h->u.headers : WINHTTP_NO_ADDITIONAL_HEADERS, h->u.headers ? (DWORD)-1L : 0,
                            h->body, (DWORD)(h->body ? bodyLen : 0), (DWORD)(h->body ? bodyLen : 0), (DWORD_PTR)h))
        http_finish(h, 0, GetLastError()); // HANDLE_CLOSING temizler
    return job;
}

DE_NET_API void de_http_cancel(int job)
{
    net_ensure();
    EnterCriticalSection(&net_lock);
    http_t *h = (http_t *)slot_get(http_slots, job);
    LeaveCriticalSection(&net_lock);
    if (!h)
        return;
    if (InterlockedExchange(&h->done, 1))
        return; // zaten bitti (tamamlanma kuyrukta; managed dusurur)
    WinHttpCloseHandle(h->u.req);
}

// =========================================================================================
// WEBSOCKET
// =========================================================================================
// Gonderim: baglanti basina BITISIK ring (sbuf) + dairesel segment listesi. Kopya 1 (WinHTTP tamponun
// WRITE_COMPLETE'e dek yasamasini ister), malloc amortize 0 (yalniz buyumede). Bas segment ucan yazimdir.
typedef struct
{
    int off, len, isText;
} ws_seg_t;

typedef struct
{
    int job;
    de_url_t u;
    HINTERNET ws;
    unsigned char *rbuf;
    size_t rlen, rcap; // mesaj tamponu: Receive DOGRUDAN kuyruguna okur; FIN'de pointer kuyruga devredilir (memcpy yok)
    CRITICAL_SECTION lock;
    unsigned char *sbuf;
    size_t scap;
    ws_seg_t *segs;
    int segCap, segHead, segCount;
    int sending;            // bas segment WinHTTP'de ucuyor
    volatile LONG finished; // CLOSE ya da -1 yazildi
    volatile LONG freeReq;  // de_ws_free cagrildi: tum handle'lar kapaninca baglam serbest
    int closeSent;          // biz Shutdown gonderdik
    int haveClose;          // closeCode/closeReason dolu (CLOSE frame'inden okundu)
    USHORT closeCode;
    char closeReason[124];
    DWORD closeReasonLen;
} ws_t;

static void ws_destroy(ws_t *w)
{
    EnterCriticalSection(&net_lock);
    slot_del(ws_slots, w);
    LeaveCriticalSection(&net_lock);
    close_url(&w->u);
    free(w->sbuf);
    free(w->segs);
    free(w->rbuf);
    DeleteCriticalSection(&w->lock);
    free(w);
}

// Ring'e len baytlik yer acar; ofseti doner (-1 = bellek yok). lock tutulurken cagrilir.
static int ws_ring_alloc(ws_t *w, int len)
{
    if (len < 1)
        len = 1;
    if (w->segCount == 0)
    {
        if ((size_t)len > w->scap)
        {
            size_t cap = w->scap ? w->scap : 4096;
            while (cap < (size_t)len)
                cap *= 2;
            unsigned char *nb = (unsigned char *)realloc(w->sbuf, cap);
            if (!nb)
                return -1;
            w->sbuf = nb;
            w->scap = cap;
        }
        return 0;
    }
    ws_seg_t *first = &w->segs[w->segHead];
    ws_seg_t *last = &w->segs[(w->segHead + w->segCount - 1) % w->segCap];
    int rpos = first->off, wpos = last->off + last->len;
    if (wpos >= rpos)
    {
        if ((size_t)(wpos + len) <= w->scap)
            return wpos;
        if (len < rpos)
            return 0; // sarmala
    }
    else if (wpos + len < rpos)
        return wpos;
    // sigmadi: buyut, segmentleri dogrusal yerlestir (tek seferlik kopya, amortize)
    size_t need = 0;
    for (int i = 0; i < w->segCount; i++)
        need += (size_t)w->segs[(w->segHead + i) % w->segCap].len;
    need += (size_t)len;
    size_t cap = w->scap ? w->scap * 2 : 4096;
    while (cap < need)
        cap *= 2;
    unsigned char *nb = (unsigned char *)malloc(cap);
    if (!nb)
        return -1;
    int pos = 0;
    for (int i = 0; i < w->segCount; i++)
    {
        ws_seg_t *s = &w->segs[(w->segHead + i) % w->segCap];
        memcpy(nb + pos, w->sbuf + s->off, (size_t)s->len);
        s->off = pos;
        pos += s->len;
    }
    free(w->sbuf);
    w->sbuf = nb;
    w->scap = cap;
    return pos;
}

static int ws_seg_push(ws_t *w, int off, int len, int isText)
{
    if (w->segCount == w->segCap)
    {
        int cap = w->segCap ? w->segCap * 2 : 16;
        ws_seg_t *ns = (ws_seg_t *)malloc((size_t)cap * sizeof(ws_seg_t));
        if (!ns)
            return 0;
        for (int i = 0; i < w->segCount; i++)
            ns[i] = w->segs[(w->segHead + i) % w->segCap];
        free(w->segs);
        w->segs = ns;
        w->segCap = cap;
        w->segHead = 0;
    }
    ws_seg_t *s = &w->segs[(w->segHead + w->segCount) % w->segCap];
    s->off = off;
    s->len = len;
    s->isText = isText;
    w->segCount++;
    return 1;
}

static void ws_seg_pop(ws_t *w)
{
    if (w->segCount == 0)
        return;
    w->segHead = (w->segHead + 1) % w->segCap;
    w->segCount--;
}

static void ws_fail(ws_t *w, DWORD err)
{
    if (InterlockedExchange(&w->finished, 1))
        return;
    char msg[512];
    win_error_text(err, msg, sizeof msg);
    de_net_fail(w->job, msg);
}

// Kapanis kodu/sebebi: CLOSE frame'i geldikten sonra sorgulanabilir; bir kez okunur, saklanir.
static void ws_query_close(ws_t *w)
{
    if (w->haveClose || !w->ws)
        return;
    USHORT code = 0;
    DWORD consumed = 0;
    if (WinHttpWebSocketQueryCloseStatus(w->ws, &code, w->closeReason, sizeof w->closeReason, &consumed) == NO_ERROR) // DWORD doner, BOOL degil
    {
        w->closeCode = code;
        w->closeReasonLen = consumed;
        w->haveClose = 1;
    }
}

static void ws_closed(ws_t *w)
{
    if (InterlockedExchange(&w->finished, 1))
        return;
    ws_query_close(w);
    USHORT code = w->haveClose ? w->closeCode : 1006; // 1006: el sikisma tamamlanmadan koptu
    DWORD n = w->haveClose ? w->closeReasonLen : 0;
    de_async_complete(w->job, DE_WS_CLOSE, n ? de_net_memdup(w->closeReason, n) : NULL, n, code, 0);
}

static void ws_pump_send(ws_t *w)
{
    // lock tutulurken cagrilir; ucan yazim yoksa bas segmenti ucurur (segment WRITE_COMPLETE'e dek kuyrukta kalir)
    if (w->sending || w->segCount == 0 || !w->ws || w->finished)
        return;
    ws_seg_t *s = &w->segs[w->segHead];
    w->sending = 1;
    DWORD r = WinHttpWebSocketSend(w->ws, s->isText ? WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE : WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE, w->sbuf + s->off, (DWORD)s->len);
    if (r != NO_ERROR)
    {
        w->sending = 0;
        ws_seg_pop(w);
        ws_fail(w, r);
    }
}

// Mesaj tamponunda en az DE_NET_CHUNK bos yer garantiler ve almayi oraya baslatir (ara tampon yok).
static void ws_receive(ws_t *w)
{
    if (w->rlen + DE_NET_CHUNK > w->rcap)
    {
        size_t cap = w->rcap ? w->rcap * 2 : DE_NET_CHUNK;
        while (cap < w->rlen + DE_NET_CHUNK)
            cap *= 2;
        unsigned char *nb = (unsigned char *)realloc(w->rbuf, cap);
        if (!nb)
        {
            ws_fail(w, ERROR_OUTOFMEMORY);
            return;
        }
        w->rbuf = nb;
        w->rcap = cap;
    }
    DWORD r = WinHttpWebSocketReceive(w->ws, w->rbuf + w->rlen, DE_NET_CHUNK, NULL, NULL);
    if (r != NO_ERROR && r != ERROR_INVALID_OPERATION)
        ws_fail(w, r);
}

static void CALLBACK ws_cb(HINTERNET h, DWORD_PTR ctx, DWORD status, LPVOID info, DWORD infoLen)
{
    ws_t *w = (ws_t *)ctx;
    if (!w)
        return;
    if (status == WINHTTP_CALLBACK_STATUS_HANDLE_CLOSING)
    {
        int last;
        EnterCriticalSection(&w->lock);
        if (h == w->u.req)
            w->u.req = NULL;
        if (h == w->ws)
            w->ws = NULL;
        last = !w->u.req && !w->ws && w->freeReq;
        LeaveCriticalSection(&w->lock);
        if (last)
            ws_destroy(w);
        return;
    }
    switch (status)
    {
    case WINHTTP_CALLBACK_STATUS_SENDREQUEST_COMPLETE:
        if (!WinHttpReceiveResponse(h, NULL))
            ws_fail(w, GetLastError());
        break;
    case WINHTTP_CALLBACK_STATUS_HEADERS_AVAILABLE:
    {
        DWORD code = 0, sz = sizeof code;
        WinHttpQueryHeaders(h, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &code, &sz, WINHTTP_NO_HEADER_INDEX);
        if (code != 101)
        {
            ws_fail(w, ERROR_WINHTTP_INVALID_SERVER_RESPONSE);
            break;
        }
        HINTERNET ws = WinHttpWebSocketCompleteUpgrade(h, (DWORD_PTR)w);
        if (!ws)
        {
            ws_fail(w, GetLastError());
            break;
        }
        WinHttpSetStatusCallback(ws, ws_cb, WINHTTP_CALLBACK_FLAG_ALL_NOTIFICATIONS, 0);
        EnterCriticalSection(&w->lock);
        w->ws = ws;
        LeaveCriticalSection(&w->lock);
        WinHttpCloseHandle(h); // istek handle'i bitti (HANDLE_CLOSING -> req=NULL)
        de_async_complete(w->job, DE_WS_OPEN, NULL, 0, 0, 0);
        EnterCriticalSection(&w->lock);
        ws_pump_send(w); // baglanmadan once kuyruga alinanlar
        LeaveCriticalSection(&w->lock);
        ws_receive(w);
        break;
    }
    case WINHTTP_CALLBACK_STATUS_READ_COMPLETE:
    {
        WINHTTP_WEB_SOCKET_STATUS *st = (WINHTTP_WEB_SOCKET_STATUS *)info;
        if (!st || w->finished)
            break;
        if (st->eBufferType == WINHTTP_WEB_SOCKET_CLOSE_BUFFER_TYPE)
        {
            ws_query_close(w);
            if (!w->closeSent)
            {
                // Karsi taraf kapatti: ayni kodla yanit ver (Shutdown: bekleyen alma ile uyumlu, SHUTDOWN_COMPLETE gelir)
                w->closeSent = 1;
                WinHttpWebSocketShutdown(w->ws, w->haveClose ? w->closeCode : WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS, NULL, 0);
            }
            ws_closed(w);
            break;
        }
        DWORD n = st->dwBytesTransferred;
        w->rlen += n; // dogrudan rbuf kuyruguna okundu
        if (st->eBufferType == WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE || st->eBufferType == WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE)
        {
            int isText = st->eBufferType == WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE;
            // Mesaj bitti: tamponu kuyruga DEVRET (kopya yok). Kucult: realloc kucultme yerinde olur.
            unsigned char *msg = w->rbuf;
            if (msg && w->rlen < w->rcap)
            {
                unsigned char *shr = (unsigned char *)realloc(msg, w->rlen ? w->rlen : 1);
                if (shr)
                    msg = shr;
            }
            de_async_complete(w->job, DE_WS_MESSAGE, msg ? msg : de_net_memdup("", 0), (long long)w->rlen, isText, 0);
            w->rbuf = NULL;
            w->rlen = w->rcap = 0;
        }
        ws_receive(w);
        break;
    }
    case WINHTTP_CALLBACK_STATUS_WRITE_COMPLETE:
        EnterCriticalSection(&w->lock);
        if (w->sending)
        {
            w->sending = 0;
            ws_seg_pop(w);
        }
        ws_pump_send(w);
        LeaveCriticalSection(&w->lock);
        break;
    case WINHTTP_CALLBACK_STATUS_CLOSE_COMPLETE:
    case WINHTTP_CALLBACK_STATUS_SHUTDOWN_COMPLETE:
        if (w->closeSent && w->haveClose)
            ws_closed(w); // karsi tarafin CLOSE'u zaten geldiyse bitir; yoksa alma tamamlanmasini bekle
        break;
    case WINHTTP_CALLBACK_STATUS_REQUEST_ERROR:
    {
        WINHTTP_ASYNC_RESULT *r = (WINHTTP_ASYNC_RESULT *)info;
        DWORD err = r ? r->dwError : ERROR_WINHTTP_INTERNAL_ERROR;
        if (w->finished)
            break;
        if (w->closeSent && w->ws)
        {
            ws_closed(w);
            break;
        } // kapanis sirasinda ucan alma iptali: kapanis olarak raporla
        ws_fail(w, err);
        break;
    }
    default:
        break;
    }
}

DE_NET_API int de_ws_open(const char *url, const char *headers)
{
    net_ensure();
    int job = de_async_new_job();
    ws_t *w = (ws_t *)calloc(1, sizeof(ws_t));
    if (!w)
    {
        de_net_fail(job, "out of memory");
        return job;
    }
    w->job = job;
    InitializeCriticalSection(&w->lock);
    DWORD err = open_request("GET", url, headers, 0, 1, &w->u);
    if (err == 0 && !WinHttpSetOption(w->u.req, WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET, NULL, 0))
        err = GetLastError();
    if (err == 0)
    {
        EnterCriticalSection(&net_lock);
        int ok = slot_put(ws_slots, job, w);
        LeaveCriticalSection(&net_lock);
        if (!ok)
            err = ERROR_TOO_MANY_OPEN_FILES;
    }
    if (err != 0)
    {
        ws_fail(w, err);
        if (w->u.req)
        {
            WinHttpCloseHandle(w->u.req);
            w->u.req = NULL;
        }
        w->freeReq = 1; // handle yok: de_ws_free hemen yok eder; tabloya girmediyse burada
        EnterCriticalSection(&net_lock);
        int inTable = slot_get(ws_slots, job) == w;
        LeaveCriticalSection(&net_lock);
        if (!inTable)
        {
            ws_destroy(w);
        }
        return job;
    }
    WinHttpSetStatusCallback(w->u.req, ws_cb, WINHTTP_CALLBACK_FLAG_ALL_NOTIFICATIONS, 0);
    if (!WinHttpSendRequest(w->u.req, w->u.headers ? w->u.headers : WINHTTP_NO_ADDITIONAL_HEADERS, w->u.headers ? (DWORD)-1L : 0, NULL, 0, 0, (DWORD_PTR)w))
        ws_fail(w, GetLastError());
    return job;
}

static ws_t *ws_get(int ws)
{
    net_ensure();
    EnterCriticalSection(&net_lock);
    ws_t *w = (ws_t *)slot_get(ws_slots, ws);
    LeaveCriticalSection(&net_lock);
    return w;
}

DE_NET_API int de_ws_send(int ws, const void *data, int len, int isText)
{
    ws_t *w = ws_get(ws);
    if (!w || w->finished)
        return -1;
    if (len < 0)
        len = 0;
    EnterCriticalSection(&w->lock);
    int off = ws_ring_alloc(w, len);
    if (off < 0 || !ws_seg_push(w, off, len, isText))
    {
        LeaveCriticalSection(&w->lock);
        return -1;
    }
    if (len)
        memcpy(w->sbuf + off, data, (size_t)len); // tek kopya: ring WRITE_COMPLETE'e dek yasar
    ws_pump_send(w); // baglanti henuz yoksa kuyrukta bekler, OPEN'da ucar
    LeaveCriticalSection(&w->lock);
    return 0;
}

DE_NET_API void de_ws_close(int ws, int code, const char *reason)
{
    ws_t *w = ws_get(ws);
    if (!w || w->finished)
        return;
    EnterCriticalSection(&w->lock);
    HINTERNET h = w->ws;
    int already = w->closeSent;
    w->closeSent = 1;
    LeaveCriticalSection(&w->lock);
    if (already)
        return;
    if (!h)
    {
        ws_fail(w, ERROR_WINHTTP_OPERATION_CANCELLED);
        return;
    } // daha baglanmadi: vazgec
    size_t rl = reason ? strlen(reason) : 0;
    if (rl > 123)
        rl = 123;
    // Shutdown: CLOSE frame'i gonderir, bekleyen WinHttpWebSocketReceive acik kalir; karsi tarafin CLOSE'u
    // o alma ile gelir (READ_COMPLETE/CLOSE tipi) -> ws_closed. (WinHttpWebSocketClose bekleyen alma varken hata verir.)
    DWORD r = WinHttpWebSocketShutdown(h, (USHORT)(code > 0 ? code : WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS), rl ? (PVOID)reason : NULL, (DWORD)rl);
    if (r != NO_ERROR)
        ws_fail(w, r);
}

DE_NET_API void de_ws_free(int ws)
{
    ws_t *w = ws_get(ws);
    if (!w)
        return;
    InterlockedExchange(&w->finished, 1);
    EnterCriticalSection(&w->lock);
    w->freeReq = 1;
    HINTERNET hw = w->ws, hr = w->u.req;
    LeaveCriticalSection(&w->lock);
    if (hw)
        WinHttpCloseHandle(hw);
    if (hr)
        WinHttpCloseHandle(hr);
    if (!hw && !hr)
        ws_destroy(w);
}

#elif defined(__EMSCRIPTEN__)
// ---- Web: fetch + WebSocket (tarayici; TLS/CORS tarayicinin). Tek thread: callback'ler ana thread'de
// ama yine de de_async kuyruguna yazilir (ayni sozlesme: managed yalniz Pump'ta gorur).
// Baslik destegi: fetch istek basliklari OK; WebSocket'te tarayici ozel baslik vermez (yok sayilir).
#include <emscripten/emscripten.h>
// EM_JS govdeleri JS'tir: formatter `===`i bolup calisma zamani soz dizimi hatasi uretti; bu bolge formatlanmaz.
// clang-format off

// JS -> C geri cagri (EM_JS 64-bit len tasiyamaz: int). KEEPALIVE: `_de_net_done` olarak gorunur.
EMSCRIPTEN_KEEPALIVE void de_net_done(int job, int code, void *data, int len, int a, int b)
{
    de_async_complete(job, code, data, (long long)len, a, b);
}

EM_JS(void, de_js_net_init, (void), {
    if (Module.deNet)
        return;
    Module.deNet = {http : {}, ws : {}};
    Module.deNet.bytes = function(u8)
    {
        var p = _malloc(u8.length || 1);
        HEAPU8.set(u8, p);
        return p;
    };
    Module.deNet.str = function(s)
    {
        var n = lengthBytesUTF8(s);
        var p = _malloc(n + 1);
        stringToUTF8(s, p, n + 1);
        return [ p, n ];
    };
    Module.deNet.fail = function(job, msg)
    {
        var r = Module.deNet.str(String(msg));
        _de_net_done(job, -1, r[0], r[1], 0, 0);
    };
});

EM_JS(void, de_js_http, (int job, const char *method, const char *url, const char *headers, const void *body, int bodyLen, int timeoutMs), {
    var N = Module.deNet;
    var ctl = new AbortController();
    var init = {method : UTF8ToString(method), signal : ctl.signal, headers : {}};
    if (headers)
    {
        UTF8ToString(headers).split('\n').forEach(function(line) {
            var i = line.indexOf(':');
            if (i > 0)
                init.headers[line.substring(0, i).trim()] = line.substring(i + 1).trim();
        });
    }
    if (body && bodyLen > 0)
        init.body = HEAPU8.slice(body, body + bodyLen);
    var timer = setTimeout(function() { ctl.abort('timeout'); }, timeoutMs > 0 ? timeoutMs : 30000);
    N.http[job] = ctl;
    var status = 0, hdr = '';
    fetch(UTF8ToString(url), init).then(function(res) {
                                      status = res.status;
                                      hdr = 'HTTP/1.1 ' + res.status + ' ' + res.statusText + '\r\n';
                                      res.headers.forEach(function(v, k) { hdr += k + ': ' + v + '\r\n'; });
                                      hdr += '\r\n';
                                      return res.arrayBuffer();
                                  })
        .then(function(ab) {
            clearTimeout(timer);
            if (!N.http[job])
                return; // iptal edildi
            delete N.http[job];
            var h = new TextEncoder().encode(hdr), b = new Uint8Array(ab);
            var all = new Uint8Array(h.length + b.length);
            all.set(h, 0);
            all.set(b, h.length);
            _de_net_done(job, 1, N.bytes(all), all.length, status, h.length);
        })
        .catch(function(e) {
            clearTimeout(timer);
            if (!N.http[job])
                return;
            delete N.http[job];
            N.fail(job, e && e.message ? e.message : e);
        });
});

EM_JS(void, de_js_http_cancel, (int job), {
    var N = Module.deNet, c = N.http[job];
    if (c)
    {
        delete N.http[job];
        c.abort('canceled');
    }
});

EM_JS(void, de_js_ws_open, (int job, const char *url), {
    var N = Module.deNet;
    var ws;
    try { ws = new WebSocket(UTF8ToString(url)); }
    catch(e)
    {
        N.fail(job, e && e.message ? e.message : e);
        return;
    }
    ws.binaryType = 'arraybuffer';
    N.ws[job] = ws;
    ws.onopen = function()
    {
        if (N.ws[job])
            _de_net_done(job, 0x40000001, 0, 0, 0, 0);
    };
    ws.onmessage = function(ev)
    {
        if (!N.ws[job])
            return;
        if (typeof ev.data === 'string')
        {
            var r = N.str(ev.data);
            _de_net_done(job, 0x40000002, r[0], r[1], 1, 0);
        }
        else
        {
            var u = new Uint8Array(ev.data);
            _de_net_done(job, 0x40000002, N.bytes(u), u.length, 0, 0);
        }
    };
    ws.onerror = function(){}; // tarayici ardindan hep close(1006) verir
    ws.onclose = function(ev)
    {
        if (!N.ws[job])
            return;
        delete N.ws[job];
        var r = N.str(ev.reason || '');
        _de_net_done(job, 2, r[0], r[1], ev.code || 1006, 0);
    };
});

EM_JS(int, de_js_ws_send, (int ws, const void *data, int len, int isText), {
    var s = Module.deNet.ws[ws];
    if (!s || s.readyState > 1)
        return -1;
    try
    {
        if (isText)
            s.send(UTF8ToString(data, len));
        else
            s.send(HEAPU8.slice(data, data + len));
    }
    catch(e) { return -1; }
    return 0;
});

EM_JS(void, de_js_ws_close, (int ws, int code, const char *reason), {
    var s = Module.deNet.ws[ws];
    if (!s)
        return;
    try { s.close(code, UTF8ToString(reason)); }
    catch(e)
    {
        try { s.close(); }
        catch(e2) {}
    } // tarayici yalniz 1000/3000-4999 kabul eder
});

EM_JS(void, de_js_ws_free, (int ws), {
    var N = Module.deNet, s = N.ws[ws];
    if (!s)
        return;
    delete N.ws[ws];
    try { s.close(); }
    catch(e) {}
});

DE_NET_API int de_http_request(const char *method, const char *url, const char *headers, const void *body, int bodyLen, int timeoutMs)
{
    de_js_net_init();
    int job = de_async_new_job();
    de_js_http(job, method && *method ? method : "GET", url, headers, body, bodyLen, timeoutMs);
    return job;
}
DE_NET_API void de_http_cancel(int job)
{
    de_js_net_init();
    de_js_http_cancel(job);
}
DE_NET_API int de_ws_open(const char *url, const char *headers)
{
    (void)headers;
    de_js_net_init();
    int job = de_async_new_job();
    de_js_ws_open(job, url);
    return job;
}
DE_NET_API int de_ws_send(int ws, const void *data, int len, int isText) { return de_js_ws_send(ws, data, len, isText); }
DE_NET_API void de_ws_close(int ws, int code, const char *reason) { de_js_ws_close(ws, code, reason ? reason : ""); }
DE_NET_API void de_ws_free(int ws) { de_js_ws_free(ws); }
// clang-format on

#elif defined(__ANDROID__)
// ---- Android: JNI -> com.digitoy.host.Net (Kotlin; HttpURLConnection + RFC 6455 Socket/SSLSocket, 3. parti yok).
// Host JNI_OnLoad de_android_net_bind(vm, env, cls) cagirir (sinif APP classloader'indan). Baglanmamissa
// her istek "bagli degil" hatasiyla biter. Tamamlanmalar Java'dan Net.done -> asagidaki JNI ucu ->
// de_async_complete (GetByteArrayRegion ile tek kopya; Java tamponu hemen yeniden kullanilir).
// Gonderim: Java scratch dizisi (wsScratch) SetByteArrayRegion ile doldurulur -> wsSend frame'ler (Java alloc yok).
#include <jni.h>
static JavaVM *de_net_vm;
static jclass de_net_cls;
static jmethodID de_nm_request, de_nm_cancel, de_nm_ws_open, de_nm_ws_scratch, de_nm_ws_send, de_nm_ws_close, de_nm_ws_free;

DE_NET_API void de_android_net_bind(void *vm, void *env_, void *cls_)
{
    JNIEnv *env = (JNIEnv *)env_;
    jclass cls = (jclass)cls_;
    de_net_vm = (JavaVM *)vm;
    de_net_cls = (jclass)(*env)->NewGlobalRef(env, cls);
    de_nm_request = (*env)->GetStaticMethodID(env, cls, "request", "(ILjava/lang/String;Ljava/lang/String;Ljava/lang/String;[BI)V");
    de_nm_cancel = (*env)->GetStaticMethodID(env, cls, "cancel", "(I)V");
    de_nm_ws_open = (*env)->GetStaticMethodID(env, cls, "wsOpen", "(ILjava/lang/String;Ljava/lang/String;)V");
    de_nm_ws_scratch = (*env)->GetStaticMethodID(env, cls, "wsScratch", "(II)[B");
    de_nm_ws_send = (*env)->GetStaticMethodID(env, cls, "wsSend", "(IIZ)I");
    de_nm_ws_close = (*env)->GetStaticMethodID(env, cls, "wsClose", "(IILjava/lang/String;)V");
    de_nm_ws_free = (*env)->GetStaticMethodID(env, cls, "wsFree", "(I)V");
    if ((*env)->ExceptionCheck(env))
    {
        (*env)->ExceptionClear(env);
        de_net_cls = NULL; // imza uyusmazligi: kopru kapali
    }
}

static JNIEnv *de_net_env(void)
{
    if (!de_net_vm || !de_net_cls)
        return NULL;
    JNIEnv *env = NULL;
    if ((*de_net_vm)->GetEnv(de_net_vm, (void **)&env, JNI_VERSION_1_6) == JNI_OK)
        return env;
    return (*de_net_vm)->AttachCurrentThread(de_net_vm, &env, NULL) == JNI_OK ? env : NULL;
}

static void de_net_jclear(JNIEnv *env)
{
    if ((*env)->ExceptionCheck(env))
    {
        (*env)->ExceptionDescribe(env);
        (*env)->ExceptionClear(env);
    }
}

// Java -> native tamamlanma (Net.done; Java'nin ag thread'lerinden gelir, de_async MPSC guvenli).
JNIEXPORT void JNICALL Java_com_digitoy_host_Net_done(JNIEnv *env, jclass cls, jint job, jint code, jbyteArray data, jint off, jint len, jint a, jint b)
{
    (void)cls;
    void *buf = NULL;
    if (data && len > 0)
    {
        buf = malloc((size_t)len);
        if (buf)
            (*env)->GetByteArrayRegion(env, data, off, len, (jbyte *)buf);
        else
            len = 0;
    }
    de_async_complete(job, code, buf, (long long)len, a, b);
}

DE_NET_API int de_http_request(const char *method, const char *url, const char *headers, const void *body, int bodyLen, int timeoutMs)
{
    int job = de_async_new_job();
    JNIEnv *env = de_net_env();
    if (!env)
    {
        de_net_fail(job, "de_net: Android koprusu bagli degil");
        return job;
    }
    jstring jm = (*env)->NewStringUTF(env, method ? method : "GET");
    jstring ju = (*env)->NewStringUTF(env, url ? url : "");
    jstring jh = headers ? (*env)->NewStringUTF(env, headers) : NULL;
    jbyteArray jb = NULL;
    if (body && bodyLen > 0)
    {
        jb = (*env)->NewByteArray(env, bodyLen);
        if (jb)
            (*env)->SetByteArrayRegion(env, jb, 0, bodyLen, (const jbyte *)body);
    }
    (*env)->CallStaticVoidMethod(env, de_net_cls, de_nm_request, (jint)job, jm, ju, jh, jb, (jint)timeoutMs);
    de_net_jclear(env);
    if (jm) (*env)->DeleteLocalRef(env, jm);
    if (ju) (*env)->DeleteLocalRef(env, ju);
    if (jh) (*env)->DeleteLocalRef(env, jh);
    if (jb) (*env)->DeleteLocalRef(env, jb);
    return job;
}

DE_NET_API void de_http_cancel(int job)
{
    JNIEnv *env = de_net_env();
    if (!env)
        return;
    (*env)->CallStaticVoidMethod(env, de_net_cls, de_nm_cancel, (jint)job);
    de_net_jclear(env);
}

DE_NET_API int de_ws_open(const char *url, const char *headers)
{
    int job = de_async_new_job();
    JNIEnv *env = de_net_env();
    if (!env)
    {
        de_net_fail(job, "de_net: Android koprusu bagli degil");
        return job;
    }
    jstring ju = (*env)->NewStringUTF(env, url ? url : "");
    jstring jh = headers ? (*env)->NewStringUTF(env, headers) : NULL;
    (*env)->CallStaticVoidMethod(env, de_net_cls, de_nm_ws_open, (jint)job, ju, jh);
    de_net_jclear(env);
    if (ju) (*env)->DeleteLocalRef(env, ju);
    if (jh) (*env)->DeleteLocalRef(env, jh);
    return job;
}

DE_NET_API int de_ws_send(int ws, const void *data, int len, int isText)
{
    JNIEnv *env = de_net_env();
    if (!env)
        return -1;
    if (len < 0)
        len = 0;
    jbyteArray arr = (jbyteArray)(*env)->CallStaticObjectMethod(env, de_net_cls, de_nm_ws_scratch, (jint)ws, (jint)len);
    de_net_jclear(env);
    if (!arr)
        return -1;
    if (len)
        (*env)->SetByteArrayRegion(env, arr, 0, len, (const jbyte *)data); // native -> Java scratch (1 kopya)
    jint r = (*env)->CallStaticIntMethod(env, de_net_cls, de_nm_ws_send, (jint)ws, (jint)len, (jboolean)(isText ? JNI_TRUE : JNI_FALSE));
    de_net_jclear(env);
    (*env)->DeleteLocalRef(env, arr);
    return r;
}

DE_NET_API void de_ws_close(int ws, int code, const char *reason)
{
    JNIEnv *env = de_net_env();
    if (!env)
        return;
    jstring jr = (*env)->NewStringUTF(env, reason ? reason : "");
    (*env)->CallStaticVoidMethod(env, de_net_cls, de_nm_ws_close, (jint)ws, (jint)code, jr);
    de_net_jclear(env);
    if (jr) (*env)->DeleteLocalRef(env, jr);
}

DE_NET_API void de_ws_free(int ws)
{
    JNIEnv *env = de_net_env();
    if (!env)
        return;
    (*env)->CallStaticVoidMethod(env, de_net_cls, de_nm_ws_free, (jint)ws);
    de_net_jclear(env);
}

#else // ---- diger platformlar: faz 4 (Apple) ----------------------------------------------------------

DE_NET_API int de_http_request(const char *method, const char *url, const char *headers, const void *body, int bodyLen, int timeoutMs)
{
    (void)method;
    (void)url;
    (void)headers;
    (void)body;
    (void)bodyLen;
    (void)timeoutMs;
    int job = de_async_new_job();
    de_net_fail(job, "de_net: bu platformda henuz desteklenmiyor");
    return job;
}
DE_NET_API void de_http_cancel(int job) { (void)job; }
DE_NET_API int de_ws_open(const char *url, const char *headers)
{
    (void)url;
    (void)headers;
    int job = de_async_new_job();
    de_net_fail(job, "de_net: bu platformda henuz desteklenmiyor");
    return job;
}
DE_NET_API int de_ws_send(int ws, const void *data, int len, int isText)
{
    (void)ws;
    (void)data;
    (void)len;
    (void)isText;
    return -1;
}
DE_NET_API void de_ws_close(int ws, int code, const char *reason)
{
    (void)ws;
    (void)code;
    (void)reason;
}
DE_NET_API void de_ws_free(int ws) { (void)ws; }

#endif
