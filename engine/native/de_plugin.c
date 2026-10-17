// de_plugin.c — plugin koprusu (de_plugin.h). Platformsuz: kayit tablosu + inbox + dispatcher kancasi.
// Kilit: de_async.c ile ayni spinlock deseni (olay hacmi kucuk).
#include "de_plugin.h"
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// ---- kayit tablosu (yalniz main/init thread'den degisir; okuma send'de) ----
#define DE_PLUGIN_MAX 64
typedef struct { char name[64]; DePluginOnMessage fn; void *user; } Reg;
static Reg regs[DE_PLUGIN_MAX];
static int reg_n;

static Reg *find(const char *name)
{
    if (!name) return 0;
    for (int i = 0; i < reg_n; i++)
        if (strcmp(regs[i].name, name) == 0) return &regs[i];
    return 0;
}

DE_PLUGIN_API void de_plugin_register(const char *name, DePluginOnMessage onMessage, void *user)
{
    if (!name || !*name || strlen(name) >= sizeof regs[0].name) { fprintf(stderr, "[plugin] gecersiz ad\n"); return; }
    Reg *r = find(name);
    if (!r)
    {
        if (reg_n >= DE_PLUGIN_MAX) { fprintf(stderr, "[plugin] kayit tablosu dolu (%s)\n", name); return; }
        r = &regs[reg_n++];
        strcpy(r->name, name);
    }
    r->fn = onMessage;
    r->user = user;
}

DE_PLUGIN_API int de_plugin_available(const char *name) { return find(name) != 0; }

// ---- dispatcher (host saglar; varsayilan: hemen cagir — tek thread'li host'lar) ----
static void dispatch_now(void *user, const char *name, const char *msg)
{
    Reg *r = find(name);
    if (r && r->fn) r->fn(r->user, msg);
}
static void run_now(DePluginDispatchFn fn, void *user, char *name, char *msg)
{
    fn(user, name, msg);
    de_plugin_free_dispatch(name, msg);
}
static void (*run_on_main)(DePluginDispatchFn, void *, char *, char *) = run_now;

DE_PLUGIN_API void de_plugin_set_dispatcher(void (*f)(DePluginDispatchFn fn, void *user, char *name, char *msg))
{
    run_on_main = f ? f : run_now;
}
DE_PLUGIN_API void de_plugin_free_dispatch(char *name, char *msg) { free(name); free(msg); }

static char *dup(const char *s) { size_t n = strlen(s) + 1; char *d = (char *)malloc(n); if (d) memcpy(d, s, n); return d; }

DE_PLUGIN_API int de_plugin_send(const char *name, const char *msg)
{
    if (!find(name) || !msg) return 0;
    char *n = dup(name), *m = dup(msg);
    if (!n || !m) { free(n); free(m); return 0; }
    run_on_main(dispatch_now, 0, n, m);
    return 1;
}

// ---- inbox (host -> managed): tek bagli liste, FIFO ----
typedef struct Item { struct Item *next; int name_len, msg_len; char data[1]; } Item; // data = name\0msg
static Item *head, *tail;
static int count;
static atomic_flag q_lock = ATOMIC_FLAG_INIT;
static void lock(void) { while (atomic_flag_test_and_set_explicit(&q_lock, memory_order_acquire)) { } }
static void unlock(void) { atomic_flag_clear_explicit(&q_lock, memory_order_release); }

DE_PLUGIN_API void de_plugin_post(const char *name, const char *msg)
{
    if (!name || !msg) return;
    int nl = (int)strlen(name), ml = (int)strlen(msg);
    Item *it = (Item *)malloc(sizeof(Item) + (size_t)nl + 1 + (size_t)ml);
    if (!it) return;
    it->next = 0; it->name_len = nl; it->msg_len = ml;
    memcpy(it->data, name, (size_t)nl); it->data[nl] = 0; memcpy(it->data + nl + 1, msg, (size_t)ml);
    lock();
    if (count >= DE_PLUGIN_INBOX_CAP && head)
    {
        Item *old = head; head = old->next; if (!head) tail = 0; count--;
        free(old);
        fprintf(stderr, "[plugin] inbox dolu, en eski mesaj dusuruldu\n");
    }
    if (tail) tail->next = it; else head = it;
    tail = it; count++;
    unlock();
}

DE_PLUGIN_API int de_plugin_peek(void)
{
    lock();
    int n = head ? head->name_len + 1 + head->msg_len : 0;
    unlock();
    return n;
}

DE_PLUGIN_API int de_plugin_pop(char *buf, int cap)
{
    lock();
    Item *it = head;
    if (!it) { unlock(); return 0; }
    int n = it->name_len + 1 + it->msg_len;
    if (n > cap) { unlock(); return 0; }
    memcpy(buf, it->data, (size_t)n);
    head = it->next; if (!head) tail = 0; count--;
    unlock();
    free(it);
    return n;
}

DE_PLUGIN_API int de_plugin_pending(void) { lock(); int c = count; unlock(); return c; }
