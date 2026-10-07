// de_app.c — de_app.h'in oyun tarafi: runtime kurulumu, olay kuyrugu, GC safepoint.
// Platformsuz; her host bunu oyun kutuphanesiyle birlikte derler. Managed'a giris
// yalniz generated.c'deki de_managed_* kopruleriyle.
#include "de_app.h"
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
    atexit(de_app_atexit);
    fprintf(stderr, "[app] start\n");
    digitoyengine_init();
    de_managed_init(dataRoot, fbw, fbh, scale);
}

int de_app_frame(float dt, int fbw, int fbh, float scale)
{
    de_app_drain();
    int keep = de_managed_frame(dt, fbw, fbh, scale);
    // Safepoint: managed frame YOKKEN artimli GC dilimi (fps'i oldurmez).
    if (!getenv("AOT_NOGC")) gc_maybe_major(1 << 14);
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
