// de_app.c — de_app.h'in oyun tarafi: runtime kurulumu, olay kuyrugu, GC safepoint.
// Platformsuz; her host bunu oyun kutuphanesiyle birlikte derler. Managed'a giris
// yalniz generated.c'deki de_managed_* kopruleriyle.
#include "de_app.h"
#include "de_fs.h"
#include "vmrt.h"
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#ifdef _WIN32
#include <direct.h>
#define de_mkdir(p) _mkdir(p)
#else
#include <sys/stat.h>
#define de_mkdir(p) mkdir(p, 0755)
#endif

// SPSC/MPSC guvenli basit kuyruk: uretici(ler) spinlock ile yazar, tuketici Frame basinda
// tek seferde bosaltir. Olay hacmi kucuk (dokunma/klavye), lock maliyeti onemsiz.
#define DE_APP_QUEUE 1024
static DeEvent de_app_q[DE_APP_QUEUE];
static int de_app_q_n;
static atomic_flag de_app_q_lock = ATOMIC_FLAG_INIT;
static int de_app_dropped;

static void de_app_lock(void) { while (atomic_flag_test_and_set_explicit(&de_app_q_lock, memory_order_acquire)) { } }
static void de_app_unlock(void) { atomic_flag_clear_explicit(&de_app_q_lock, memory_order_release); }

void de_app_event(const DeEvent* e)
{
    de_app_lock();
    // Ayni pointer'in ardisik MOVE'lari tek olaya iner: frame basina en fazla 1 drag
    // dispatch (yuksek Hz fare / tarayici mousemove seli). Down/Up sirasi bozulmaz.
    if (e->type == DE_EV_POINTER_MOVE && de_app_q_n > 0)
    {
        DeEvent* last = &de_app_q[de_app_q_n - 1];
        if (last->type == DE_EV_POINTER_MOVE && last->id == e->id)
        {
            *last = *e;
            de_app_unlock();
            return;
        }
    }
    if (de_app_q_n < DE_APP_QUEUE) de_app_q[de_app_q_n++] = *e;
    else de_app_dropped++;
    de_app_unlock();
}

static void de_app_drain(void)
{
    static DeEvent local[DE_APP_QUEUE];
    de_app_lock();
    int n = de_app_q_n;
    if (n) memcpy(local, de_app_q, (size_t)n * sizeof(DeEvent));
    de_app_q_n = 0;
    int dropped = de_app_dropped; de_app_dropped = 0;
    de_app_unlock();
    if (dropped) fprintf(stderr, "[app] %d olay dusuruldu (kuyruk dolu)\n", dropped);
    for (int i = 0; i < n; i++)
        de_managed_event(local[i].type, local[i].id, local[i].x, local[i].y, local[i].a, local[i].b);
}

// ---- ANR watchdog: ana thread'in kalp atisi (de_app_frame giris/cikis) + ayri thread'den shadow stack dokumu ----
// Donma = kalp atisi DE_ANR_MS'den (varsayilan 2000) eski. Dokum stderr'e: "[anr] ..." + managed frame'ler (dosya:satir:kolon)
// + DEBUG'da son adimlar halkasi (dongu/selale tekrar eden adimlarda gorunur). Donma surdukce her esikte tekrar (en cok
// DE_ANR_MAX_DUMPS), kalp atisi donunce "[anr] recovered". Yalniz OKUR: GC/heap'e dokunmaz; ust frame yaris halinde bayat
// olabilir (MethodInfo statik, pointer hep gecerli) - tanilama icin kabul. Maliyet: frame basina 2 atomik store.
// wasm: thread yok, derlenmez. DE_ANR_MS=0 kapatir.
#ifndef __EMSCRIPTEN__
#define DE_ANR_MAX_DUMPS 5
#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
static long long de_anr_now_ms(void)
{
    static LARGE_INTEGER freq;
    LARGE_INTEGER c;
    if (!freq.QuadPart) QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&c);
    return (long long)(c.QuadPart * 1000 / freq.QuadPart);
}
static void de_anr_sleep_ms(int ms) { Sleep((DWORD)ms); }
#else
#include <pthread.h>
#include <time.h>
#include <unistd.h>
static long long de_anr_now_ms(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (long long)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}
static void de_anr_sleep_ms(int ms) { usleep((useconds_t)ms * 1000); }
#endif
static _Atomic long long de_anr_beat;   // son kalp atisi (ms); 0 = henuz frame yok
static _Atomic int de_anr_in_frame;     // 1 = de_managed_frame icinde (native host bekleme ile ayirt etmek icin)
static int de_anr_threshold_ms = 2000;

static void de_anr_dump(long long stalled_ms, int nth)
{
    int sp = DIGITOYENGINE_sp; // once sp, sonra frame'ler (yazar sp'yi en son artirir)
    fprintf(stderr, "[anr] main thread %lld ms yanitsiz (dump %d/%d, in_frame=%d, shadow depth %d)\n",
            stalled_ms, nth, DE_ANR_MAX_DUMPS, atomic_load(&de_anr_in_frame), sp);
    if (sp <= 0)
        fputs("  (managed frame yok: native/host tarafinda bekliyor)\n", stderr);
    else
        DIGITOYENGINE_dump_stack();
#ifdef DIGITOYENGINE_DEBUG
    DIGITOYENGINE_dump_steps(stderr);
#endif
    fflush(stderr);
}

static void de_anr_loop(void)
{
    int dumps = 0;
    long long stall_start = 0;
    for (;;)
    {
        de_anr_sleep_ms(250);
        long long beat = atomic_load(&de_anr_beat);
        if (!beat) continue;
        long long age = de_anr_now_ms() - beat;
        if (age < de_anr_threshold_ms)
        {
            if (dumps) fprintf(stderr, "[anr] recovered after %lld ms\n", de_anr_now_ms() - stall_start), fflush(stderr);
            dumps = 0;
            continue;
        }
        if (dumps == 0) stall_start = beat;
        if (dumps < DE_ANR_MAX_DUMPS && age >= (long long)de_anr_threshold_ms * (dumps + 1))
            de_anr_dump(age, ++dumps);
    }
}
#ifdef _WIN32
static DWORD WINAPI de_anr_thread(LPVOID p) { (void)p; de_anr_loop(); return 0; }
#else
static void* de_anr_thread(void* p) { (void)p; de_anr_loop(); return 0; }
#endif

static void de_anr_start(void)
{
    const char* e = getenv("DE_ANR_MS");
    if (e && *e) de_anr_threshold_ms = atoi(e);
    if (de_anr_threshold_ms <= 0) return;
#ifdef _WIN32
    HANDLE h = CreateThread(0, 0, de_anr_thread, 0, 0, 0);
    if (h) CloseHandle(h);
#else
    pthread_t t;
    if (pthread_create(&t, 0, de_anr_thread, 0) == 0) pthread_detach(t);
#endif
}
static inline void de_anr_heartbeat(int inFrame)
{
    atomic_store(&de_anr_in_frame, inFrame);
    atomic_store(&de_anr_beat, de_anr_now_ms());
}
#else
static void de_anr_start(void) {}
static inline void de_anr_heartbeat(int inFrame) { (void)inFrame; }
#endif

static void de_app_atexit(void)
{
    fprintf(stderr, "[app] exit (shadow stack depth %d)\n", DIGITOYENGINE_sp);
    if (DIGITOYENGINE_sp > 0) DIGITOYENGINE_crash_dump("exit inside managed frames", DIGITOYENGINE_stack, DIGITOYENGINE_sp);
}

void de_app_init(const char* dataRoot, const char* writableRoot, int fbw, int fbh, float scale)
{
    char crashDir[1024];
    snprintf(crashDir, sizeof crashDir, "%s/crash", writableRoot && *writableRoot ? writableRoot : ".");
    de_mkdir(crashDir);
    digitoyengine_crash_init(crashDir);
    de_prefs_set_root(writableRoot && *writableRoot ? writableRoot : "."); // PlayerPrefs (desktop dosya koku)
    atexit(de_app_atexit);
    fprintf(stderr, "[app] start\n");
    digitoyengine_init();
    de_anr_start();
    de_anr_heartbeat(1); // init de managed'a girer (uzun pak yukleme donma degil ama gorunsun)
    de_managed_init(dataRoot, fbw, fbh, scale);
    de_anr_heartbeat(0);
}

int de_app_frame(float dt, int fbw, int fbh, float scale)
{
    de_app_drain();
    de_anr_heartbeat(1);
    int keep = de_managed_frame(dt, fbw, fbh, scale);
    // Safepoint: managed frame YOKKEN artimli GC dilimi (fps'i oldurmez).
    if (!getenv("AOT_NOGC")) gc_maybe_major(1 << 14);
    de_anr_heartbeat(0);
    return keep;
}

void de_app_pause(void) { de_managed_pause(); }
void de_app_resume(void) { de_managed_resume(); }
void de_app_low_memory(void) { gc_major(); }

void de_app_shutdown(void)
{
    de_managed_shutdown();
    gc_major();
    fprintf(stderr, "[app] shutdown\n");
}
