// ---------------------------------------------------------------------------
// de_async.c — MPSC tamamlanma kuyrugu (de_async.h). Platformsuz: C11 atomics + malloc.
// Ureticiler spinlock altinda ekler (de_app olay kuyrugu deseni); tuketici (ana
// thread) drain ile tek seferde alir. Tamamlanma DUSURULMEZ: dizi dolunca buyur.
// ---------------------------------------------------------------------------
#include "de_async.h"
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

static DeCompletion *q;
static int q_n, q_cap;
static atomic_flag q_lock = ATOMIC_FLAG_INIT;
static atomic_int next_job = 1;

static void lock(void) { while (atomic_flag_test_and_set_explicit(&q_lock, memory_order_acquire)) { } }
static void unlock(void) { atomic_flag_clear_explicit(&q_lock, memory_order_release); }

DE_ASYNC_API int de_async_new_job(void)
{
    int id = atomic_fetch_add_explicit(&next_job, 1, memory_order_relaxed);
    if (id <= 0) // sarma: 0 ve negatif asla donmez
    {
        atomic_store_explicit(&next_job, 2, memory_order_relaxed);
        id = 1;
    }
    return id;
}

DE_ASYNC_API void de_async_complete(int job, int code, void *data, long long len, int a, int b)
{
    lock();
    if (q_n == q_cap)
    {
        int cap = q_cap ? q_cap * 2 : 64;
        DeCompletion *nq = (DeCompletion *)realloc(q, (size_t)cap * sizeof(DeCompletion));
        if (!nq) { unlock(); free(data); return; } // bellek yok: tamamlanma kaybi (log'suz; cok nadir)
        q = nq;
        q_cap = cap;
    }
    DeCompletion *c = &q[q_n++];
    c->job = job; c->code = code; c->data = data; c->len = len; c->a = a; c->b = b;
    unlock();
}

DE_ASYNC_API int de_async_drain(DeCompletion *out, int max)
{
    lock();
    int n = q_n < max ? q_n : max;
    if (n) memcpy(out, q, (size_t)n * sizeof(DeCompletion));
    if (n < q_n) memmove(q, q + n, (size_t)(q_n - n) * sizeof(DeCompletion));
    q_n -= n;
    unlock();
    return n;
}

DE_ASYNC_API int de_async_take(int job, DeCompletion *out)
{
    int found = 0;
    lock();
    for (int i = 0; i < q_n; i++)
        if (q[i].job == job)
        {
            *out = q[i];
            memmove(q + i, q + i + 1, (size_t)(q_n - i - 1) * sizeof(DeCompletion));
            q_n--;
            found = 1;
            break;
        }
    unlock();
    return found;
}

DE_ASYNC_API int de_async_next(int job, int *outJob, int *code, void **data, long long *len, int *a, int *b)
{
    DeCompletion c;
    int got = job > 0 ? de_async_take(job, &c) : de_async_drain(&c, 1);
    if (!got)
        return 0;
    *outJob = c.job; *code = c.code; *data = c.data; *len = c.len; *a = c.a; *b = c.b;
    return 1;
}

DE_ASYNC_API int de_async_pending(void)
{
    lock();
    int n = q_n;
    unlock();
    return n;
}

DE_ASYNC_API void de_async_free(void *data) { free(data); }
