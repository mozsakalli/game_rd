// macOS: ucontext.h (M9g sinyal handler'i) _XOPEN_SOURCE ister; _DARWIN_C_SOURCE
// Darwin uzantilarini (SIGBUS alanlari vb.) geri acar. Tum include'lardan ONCE olmali.
#if defined(__APPLE__)
#define _XOPEN_SOURCE 700
#define _DARWIN_C_SOURCE
#endif
#include "vmrt.h"
#define GC_BLOCK 4096
#define GC_ALIGN 16
#define GC_CLASSES 64
typedef struct FreeSlot
{
    struct FreeSlot *next;
} FreeSlot;
static FreeSlot *gc_free[GC_CLASSES];
int gc_blocks = 0;
static int gc_class(size_t sz)
{
    int c = (int)((sz + GC_ALIGN - 1) / GC_ALIGN) - 1;
    return c < 0 ? 0 : c;
}
#ifdef DIGITOYENGINE_GC_POISON
static void gc_freed_forget(void *p); // yeniden kullanilan adres artik 'serbest' degil (verifier yanlis pozitifi)
#endif
static void *block_alloc(size_t sz)
{
    int c = gc_class(sz);
    if (c >= GC_CLASSES)
    {
        void *m = malloc(sz);
#ifdef DIGITOYENGINE_GC_POISON
        gc_freed_forget(m);
#endif
        return m;
    }
    size_t slot = (size_t)(c + 1) * GC_ALIGN;
    if (!gc_free[c])
    {
        char *blk = (char *)malloc(GC_BLOCK);
        gc_blocks++;
        int n = GC_BLOCK / (int)slot;
        for (int i = 0; i < n; i++)
        {
            FreeSlot *s = (FreeSlot *)(blk + i * slot);
            s->next = gc_free[c];
            gc_free[c] = s;
        }
    }
    FreeSlot *s = gc_free[c];
    gc_free[c] = s->next;
#ifdef DIGITOYENGINE_GC_POISON
    gc_freed_forget(s);
#endif
    return s;
}
static void block_free(void *p, unsigned short sz)
{
#ifdef DIGITOYENGINE_GC_POISON
    // Tani modu: KARANTINA. Govde zehirlenir (0xDD), header (tip) korunur, blok free-list'e DONMEZ:
    // use-after-free ilk dokunusta deterministik [native fault] + managed iz; yeniden kullanimla
    // sifirlanmis bellek 'null' gibi gorunup teshisi saptiramaz. (Bellek sizar; yalniz tani.)
    if (sz > sizeof(GCHeader)) memset((char *)p + sizeof(GCHeader), 0xDD, sz - sizeof(GCHeader));
    return;
#endif
    int c = gc_class(sz);
    if (c >= GC_CLASSES)
    {
        free(p);
        return;
    }
    FreeSlot *s = (FreeSlot *)p;
    s->next = gc_free[c];
    gc_free[c] = s;
}
// ===========================================================================
// GC: TEK artimli mark-sweep (gc.c 2020 modeli). Kusak/age/remembered set YOK.
//   Her frame host gc_maybe_major(budget) cagirir: idle ise esik asildiginda yeni dongu baslar;
//   dongu MARK (gri yigini butceyle tuket) -> SWEEP (listeyi butceyle gez) fazlarinda frame'lere
//   yayilir. Kok seti dongu basinda alinir (statikler + acik root'lar); managed frame yokken.
//   Dogruluk (tri-color): MARK fazinda yazilan her referans grilenir (gc_write_barrier -> uretilen
//   kodun her ref yaziminda), yeni nesneler siyah dogar (version = gc_version). Baska kural yok.
// ===========================================================================
static GCHeader *gc_objects = 0; // tum nesneler (tek liste)
static GCHeader **gc_gray = 0;
static int gc_gray_n = 0, gc_gray_cap = 0;
static GCHeader **gc_roots = 0;
static int gc_roots_n = 0, gc_roots_cap = 0;
static void **gc_frames = 0;
static FrameTrace *gc_ftraces = 0;
static int gc_frame_n = 0, gc_frame_cap = 0;
static unsigned char gc_version = 1;
static unsigned gc_next_id = 0;         // nesne kimligi (idhash) sayaci
static long long gc_heap_bytes = 0;      // canli header baytlari (string/array yan verisi haric)
static long long gc_major_baseline = 0; // son dongu bitiminde kalan canli boyut
enum
{
    GCM_IDLE,
    GCM_MARK,
    GCM_SWEEP
};
static int gcm_phase = GCM_IDLE;
static GCHeader **gcm_pp = 0;
void gc_shade(GCHeader *o);
void gc_add_root(GCHeader *o)
{
    if (!o)
        return;
    if (gc_roots_n == gc_roots_cap)
    {
        gc_roots_cap = gc_roots_cap ? gc_roots_cap * 2 : 8;
        gc_roots = (GCHeader **)realloc(gc_roots, gc_roots_cap * sizeof(GCHeader *));
    }
    gc_roots[gc_roots_n++] = o;
    if (gcm_phase == GCM_MARK)
        gc_shade(o); // dongu ortasinda eklenen kok kacirilmasin
}
void gc_remove_root(GCHeader *o)
{
    for (int i = 0; i < gc_roots_n; i++)
    {
        if (gc_roots[i] == o)
        {
            gc_roots[i] = gc_roots[--gc_roots_n];
            return;
        }
    }
}
void gc_add_frame_root(void *f, FrameTrace t)
{
    if (!f)
        return;
    if (gc_frame_n == gc_frame_cap)
    {
        gc_frame_cap = gc_frame_cap ? gc_frame_cap * 2 : 8;
        gc_frames = (void **)realloc(gc_frames, gc_frame_cap * sizeof(void *));
        gc_ftraces = (FrameTrace *)realloc(gc_ftraces, gc_frame_cap * sizeof(FrameTrace));
    }
    gc_frames[gc_frame_n] = f;
    gc_ftraces[gc_frame_n] = t;
    gc_frame_n++;
}
void *gc_alloc(const Type *t)
{
    GCHeader *o = (GCHeader *)block_alloc(t->size);
    memset(o, 0, t->size);
    gc_heap_bytes += t->size;
    o->type = t;
    o->version = gc_version; // siyah dogar: suren dongu bunu supurmez
    o->age = 0;
    o->next = gc_objects;
    gc_objects = o;
    if (t->module)
        t->module->live++; // dinamik modul tipi: unload sonrasi free karari bu sayaca bakar
    return o;
}
#ifdef DIGITOYENGINE_GC_POISON
// ---- TANI: dangling verifier. Donguda serbest birakilan adresler tutulur; sweep bitince (herkes canli)
// tum nesnelerin trace'i dogrulama modunda kosulur; serbest nesneye isaret eden alan varsa sahip tipi +
// kacinci referans yolu oldugu basilir -> GC'nin kacirdigi kenar tek koşuda gorunur.
static void **gc_freed = 0;
static int gc_freed_n = 0, gc_freed_cap = 0;
static int gc_verify_mode = 0, gc_verify_slot = 0;
static GCHeader *gc_verify_owner = 0;
static void gc_freed_add(void *p)
{
    if (gc_freed_n == gc_freed_cap)
    {
        gc_freed_cap = gc_freed_cap ? gc_freed_cap * 2 : 1024;
        gc_freed = (void **)realloc(gc_freed, gc_freed_cap * sizeof(void *));
    }
    gc_freed[gc_freed_n++] = p;
}
static void gc_freed_forget(void *p)
{
    for (int i = 0; i < gc_freed_n; i++)
        if (gc_freed[i] == p) { gc_freed[i] = gc_freed[--gc_freed_n]; return; }
}
static int gc_is_freed(void *p)
{
    for (int i = 0; i < gc_freed_n; i++)
        if (gc_freed[i] == p)
            return 1;
    return 0;
}
static void DIGITOYENGINE_put_name(FILE *f, const VmString *s);
// Tani: fault aninda bu dongude serbest birakilan son nesnelerin tipleri (header karantinada korunur).
static void gc_dump_recent_freed(FILE *f)
{
    fprintf(f, "[gc-poison] bu donguda serbest birakilan: %d nesne; son 24:\n", gc_freed_n);
    for (int i = gc_freed_n - 1; i >= 0 && i >= gc_freed_n - 24; i--)
    {
        GCHeader *o = (GCHeader *)gc_freed[i];
        fprintf(f, "  %p ", (void *)o);
        if (o->type && o->type->name) DIGITOYENGINE_put_name(f, o->type->name); else fprintf(f, "?");
        fprintf(f, "\n");
    }
}
static void gc_verify_check(GCHeader *o)
{
    gc_verify_slot++;
    if (!o || !gc_is_freed(o))
        return;
    fprintf(stderr, "[gc-verify] DANGLING: owner ");
    if (gc_verify_owner)
    {
        DIGITOYENGINE_put_name(stderr, gc_verify_owner->type->name);
        fprintf(stderr, " (%p) ref#%d", (void *)gc_verify_owner, gc_verify_slot);
    }
    else
        fprintf(stderr, "<root/static> ref#%d", gc_verify_slot);
    fprintf(stderr, " -> freed %p\n", (void *)o);
}
static void gc_verify_all(void)
{
    gc_verify_mode = 1;
    gc_verify_owner = 0; gc_verify_slot = 0;
    for (int i = 0; i < gc_frame_n; i++) gc_ftraces[i](gc_frames[i]);
    for (int i = 0; i < gc_roots_n; i++) gc_verify_check(gc_roots[i]);
    for (GCHeader *o = gc_objects; o; o = o->next)
        if (o->type->trace) { gc_verify_owner = o; gc_verify_slot = 0; o->type->trace(o); }
    gc_verify_mode = 0;
    gc_freed_n = 0;
}
#endif
void gc_shade(GCHeader *o)
{
#ifdef DIGITOYENGINE_GC_POISON
    if (gc_verify_mode) { gc_verify_check(o); return; }
#endif
    if (!o || o->age == GC_IMMORTAL || o->version == gc_version)
        return; // immortal (static const): isaretleme, izleme, sweep YOK
    o->version = gc_version;
    if (o->type->atomic || !o->type->trace)
        return;
    if (gc_gray_n == gc_gray_cap)
    {
        gc_gray_cap = gc_gray_cap ? gc_gray_cap * 2 : 64;
        gc_gray = (GCHeader **)realloc(gc_gray, gc_gray_cap * sizeof(GCHeader *));
    }
    gc_gray[gc_gray_n++] = o;
}
// Tri-color insertion barrier: MARK fazinda heap'e yazilan referans grilenir. obj kullanilmaz
// (API uyumu; sahip bilgisi gerekmez). Uretilen kod her alan/eleman/indirect ref yaziminda cagirir.
void gc_write_barrier(GCHeader *obj, GCHeader *val)
{
    (void)obj;
    if (gcm_phase == GCM_MARK)
        gc_shade(val);
}
static void gc_finalize_free(GCHeader *o)
{
#ifdef DIGITOYENGINE_GC_POISON
    gc_freed_add(o);
#endif
    if (o->type->finalize)
        o->type->finalize(o);
    if (o->type->module)
        o->type->module->live--;
    gc_heap_bytes -= o->type->size;
    block_free(o, o->type->size);
}
static void gc_mark_roots(void)
{
    for (int i = 0; i < gc_roots_n; i++)
        gc_shade(gc_roots[i]);
    for (int i = 0; i < gc_frame_n; i++)
        gc_ftraces[i](gc_frames[i]);
}
static void gc_major_start(void)
{
    gc_version++;
    gc_gray_n = 0;
    gc_mark_roots();
    gcm_phase = GCM_MARK;
}
// Bir dilim is: donus 1 = dongu suruyor, 0 = dongu bitti/idle.
int gc_major_step(int budget)
{
    switch (gcm_phase)
    {
    case GCM_IDLE:
        gc_major_start();
        return 1;
    case GCM_MARK:
        while (budget-- > 0 && gc_gray_n > 0)
        {
            GCHeader *o = gc_gray[--gc_gray_n];
            if (o->type->trace)
                o->type->trace(o);
        }
        if (gc_gray_n == 0)
        {
            gcm_phase = GCM_SWEEP;
            gcm_pp = &gc_objects;
        }
        return 1;
    case GCM_SWEEP:
        // Yeni nesneler liste BASINA eklenir (gcm_pp'nin gerisinde): gezilmez, zaten siyah.
        while (budget-- > 0 && *gcm_pp)
        {
            GCHeader *o = *gcm_pp;
            if (o->version == gc_version)
                gcm_pp = &o->next;
            else
            {
                *gcm_pp = o->next;
                gc_finalize_free(o);
            }
        }
        if (*gcm_pp == 0)
        {
            gcm_phase = GCM_IDLE;
            gc_major_baseline = gc_heap_bytes;
#ifdef DIGITOYENGINE_GC_POISON
            gc_verify_all();
#endif
            return 0;
        }
        return 1;
    }
    return 0;
}
// Esik politikasi: heap, son dongudan kalan canlinin 1.5 kati + tabani asinca dongu baslar;
// dongu aktifse butceyle surdurur. Host her frame cagirir.
#define GC_MAJOR_MIN (256 << 10)
int gc_maybe_major(int budget)
{
    if (gcm_phase != GCM_IDLE)
        return gc_major_step(budget);
    if (gc_heap_bytes > gc_major_baseline + (gc_major_baseline >> 1) + GC_MAJOR_MIN)
        return gc_major_step(budget);
    return 0;
}
// Tam dongu (GC.Collect): yalniz managed frame yokken / canli local olmadigi bilinen yerde guvenli.
void gc_major(void)
{
    while (gc_major_step(1000000))
        ;
}
// Test kancasi (GC.Collect(0)): kucuk bir dilim — mutator ile ic ice gecisi (bariyer) sinar.
void gc_minor(void)
{
    gc_major_step(1 << 8);
}
int gc_count_young(void)
{
    int n = 0;
    for (GCHeader *o = gc_objects; o; o = o->next)
        n++;
    return n;
}
int gc_count_old(void) { return 0; }
int gc_hashcode(GCHeader *o)
{
    if (!o)
        return 0;
    if (!o->idhash)
        o->idhash = ++gc_next_id; // saf kimlik: benzersiz, stabil (icerik hash'i corelib override'inin isi)
    return o->idhash;
}

// ---- Tek meta arama (vmrt.h): tip/metot hash indeksleri ilk aramada kurulur (acik adresleme, 2x kapasite).
// Tablolar uretilen kodda (digitoyengine_types / digitoyengine_methods); vmint.c dis referanslari buradan baglar.
unsigned long long de_hash64_n(const char *s, int n)
{
    unsigned long long h = 1469598103934665603ull;
    for (int i = 0; i < n; i++)
    {
        h ^= (unsigned char)s[i];
        h *= 1099511628211ull;
    }
    return h;
}
unsigned long long de_hash64(const char *s) { return de_hash64_n(s, (int)strlen(s)); }
typedef struct DeIndex
{
    const void **slots;
    unsigned mask;
    int built;
} DeIndex;
static DeIndex de_idx_types, de_idx_methods;
static void de_index_put(DeIndex *ix, unsigned long long h, const void *e)
{
    if (!h)
        return;
    unsigned k = (unsigned)h & ix->mask;
    while (ix->slots[k])
        k = (k + 1) & ix->mask;
    ix->slots[k] = e;
}
static void de_index_alloc(DeIndex *ix, int n)
{
    unsigned cap = 16;
    while (cap < (unsigned)n * 2)
        cap <<= 1;
    ix->slots = (const void **)calloc(cap, sizeof(void *));
    ix->mask = cap - 1;
    ix->built = 1;
}
const Type *digitoyengine_find_type(unsigned long long hash)
{
    if (!de_idx_types.built)
    {
        de_index_alloc(&de_idx_types, digitoyengine_ntypes);
        for (int i = 0; i < digitoyengine_ntypes; i++)
            de_index_put(&de_idx_types, digitoyengine_types[i]->hash, digitoyengine_types[i]);
    }
    unsigned k = (unsigned)hash & de_idx_types.mask;
    while (de_idx_types.slots[k])
    {
        const Type *t = (const Type *)de_idx_types.slots[k];
        if (t->hash == hash)
            return t;
        k = (k + 1) & de_idx_types.mask;
    }
    return 0;
}
const MethodInfo *digitoyengine_find_method(unsigned long long hash)
{
    if (!de_idx_methods.built)
    {
        de_index_alloc(&de_idx_methods, digitoyengine_nmethods);
        for (int i = 0; i < digitoyengine_nmethods; i++)
            de_index_put(&de_idx_methods, digitoyengine_methods[i].hash, &digitoyengine_methods[i]);
    }
    unsigned k = (unsigned)hash & de_idx_methods.mask;
    while (de_idx_methods.slots[k])
    {
        const MethodInfo *m = (const MethodInfo *)de_idx_methods.slots[k];
        if (m->hash == hash)
            return m;
        k = (k + 1) & de_idx_methods.mask;
    }
    return 0;
}
// "Owner$Name" hash'i: bildiren tipten baslayip base zincirini yurur (alanlar bildiren tipin members'inda).
DigitoyEngineMember *digitoyengine_find_field(const Type *t, unsigned long long hash)
{
    for (; t; t = t->base)
        for (int i = 0; i < t->nmembers; i++)
            if (t->members[i].hash == hash)
                return &t->members[i];
    return 0;
}
// Sanal/iface slot kaydi: metot hash'i -> kayit (vslot >= 0) ve bildiren tip t'nin kendisi/atasi ya da t'nin uyguladigi iface.
// Slot indeksleri kalitimda sabittir (parent slotlari once) -> kaydin vslot'u t icin de gecerlidir.
const MethodInfo *digitoyengine_find_vslot(const Type *t, unsigned long long hash)
{
    const MethodInfo *m = digitoyengine_find_method(hash);
    if (!m || m->vslot < 0)
        return 0;
    if (!t || !m->declaringType)
        return m;
    for (const Type *x = t; x; x = x->base)
        if (x == m->declaringType)
            return m;
    return DIGITOYENGINE_implements(t, m->declaringType) ? m : 0;
}
int digitoyengine_find_shape(const char *key)
{
    for (int i = 0; i < digitoyengine_nthunks; i++)
        if (!strcmp(digitoyengine_shapes[i], key))
            return i;
    return -1;
}
// ---- Array ----
void finalize_vmarray(GCHeader *h)
{
    VmArray *a = (VmArray *)h;
    if (a->data)
        free(a->data);
    if (a->dims)
        free(a->dims);
}
static void trace_vmarray(GCHeader *h)
{
    VmArray *a = (VmArray *)h;
    GCHeader **e = (GCHeader **)a->data;
    for (int i = 0; i < a->len; i++)
        gc_shade(e[i]);
}
const Type vmarray_ref_type = {trace_vmarray, finalize_vmarray, sizeof(VmArray), 0};
const Type vmarray_val_type = {0, finalize_vmarray, sizeof(VmArray), 1};

// ---- System.String cekirdegi: yalniz MEKANIZMA (yuzey corelib/String.cs NativeBody'lerinde) ----
static void finalize_vmstring(GCHeader *h)
{
    VmString *s = (VmString *)h;
    if (s->data)
        free((void *)s->data);
}
const void *vmobject_vtable[3]; // digitoyengine_init doldurur (Object GetHashCode/Equals/ToString)
const void *vmstring_vtable[3]; // digitoyengine_init doldurur (String override'lari)
// runtime tip adlari da UTF-16 VmString (uretilen tiplerin k_str havuzuyla ayni desen)
static const unsigned short vmobject_name_d[] = {'S', 'y', 's', 't', 'e', 'm', '.', 'O', 'b', 'j', 'e', 'c', 't'};
static const unsigned short vmstring_name_d[] = {'S', 'y', 's', 't', 'e', 'm', '.', 'S', 't', 'r', 'i', 'n', 'g'};
// const DEGIL: GetHashCode idhash'i header'a cache'ler (k_str havuzu ile ayni sozlesme)
static VmString vmobject_name = {{&vmstring_type, 0, 0, GC_IMMORTAL, 0}, 13, vmobject_name_d};
static VmString vmstring_name = {{&vmstring_type, 0, 0, GC_IMMORTAL, 0}, 13, vmstring_name_d};
const Type vmobject_type = {0, 0, sizeof(VmObject), 1, 0, &vmobject_name, vmobject_vtable, 3, 0, 0, 1, .hash = 10811164153419254939ull}; // System.Object koku (base=0, tindex=1)
const Type vmstring_type = {0, finalize_vmstring, sizeof(VmString), 1, &vmobject_type, &vmstring_name, vmstring_vtable, 3, 0, 0, 2, .hash = 8790253667684771693ull};

// ---- primitive tip descriptor'lari: yalniz typeof kimligi + ad (gc_alloc edilmez, vtable yok) ----
#define DIGITOYENGINE_PNAME(sym, len, ...)                 \
    static const unsigned short sym##_d[] = {__VA_ARGS__}; \
    static VmString sym = {{&vmstring_type, 0, 0, GC_IMMORTAL, 0}, len, sym##_d}
DIGITOYENGINE_PNAME(vt_name, 16, 'S', 'y', 's', 't', 'e', 'm', '.', 'V', 'a', 'l', 'u', 'e', 'T', 'y', 'p', 'e');
DIGITOYENGINE_PNAME(i32_name, 12, 'S', 'y', 's', 't', 'e', 'm', '.', 'I', 'n', 't', '3', '2');
DIGITOYENGINE_PNAME(u32_name, 13, 'S', 'y', 's', 't', 'e', 'm', '.', 'U', 'I', 'n', 't', '3', '2');
DIGITOYENGINE_PNAME(i64_name, 12, 'S', 'y', 's', 't', 'e', 'm', '.', 'I', 'n', 't', '6', '4');
DIGITOYENGINE_PNAME(u64_name, 13, 'S', 'y', 's', 't', 'e', 'm', '.', 'U', 'I', 'n', 't', '6', '4');
DIGITOYENGINE_PNAME(i16_name, 12, 'S', 'y', 's', 't', 'e', 'm', '.', 'I', 'n', 't', '1', '6');
DIGITOYENGINE_PNAME(u16_name, 13, 'S', 'y', 's', 't', 'e', 'm', '.', 'U', 'I', 'n', 't', '1', '6');
DIGITOYENGINE_PNAME(sb_name, 12, 'S', 'y', 's', 't', 'e', 'm', '.', 'S', 'B', 'y', 't', 'e');
DIGITOYENGINE_PNAME(by_name, 11, 'S', 'y', 's', 't', 'e', 'm', '.', 'B', 'y', 't', 'e');
DIGITOYENGINE_PNAME(ch_name, 11, 'S', 'y', 's', 't', 'e', 'm', '.', 'C', 'h', 'a', 'r');
DIGITOYENGINE_PNAME(bo_name, 14, 'S', 'y', 's', 't', 'e', 'm', '.', 'B', 'o', 'o', 'l', 'e', 'a', 'n');
DIGITOYENGINE_PNAME(sg_name, 13, 'S', 'y', 's', 't', 'e', 'm', '.', 'S', 'i', 'n', 'g', 'l', 'e');
DIGITOYENGINE_PNAME(db_name, 13, 'S', 'y', 's', 't', 'e', 'm', '.', 'D', 'o', 'u', 'b', 'l', 'e');
DIGITOYENGINE_PNAME(en_name, 11, 'S', 'y', 's', 't', 'e', 'm', '.', 'E', 'n', 'u', 'm');
DIGITOYENGINE_PNAME(dg_name, 15, 'S', 'y', 's', 't', 'e', 'm', '.', 'D', 'e', 'l', 'e', 'g', 'a', 't', 'e');
DIGITOYENGINE_PNAME(mdg_name, 24, 'S', 'y', 's', 't', 'e', 'm', '.', 'M', 'u', 'l', 't', 'i', 'c', 'a', 's', 't', 'D', 'e', 'l', 'e', 'g', 'a', 't', 'e');
// boyut = box nesne boyutu (gc_alloc kullanir: header + payload); vtable'lar corelib.c'de
const Type vmvaluetype_type = {0, 0, 0, 1, &vmobject_type, &vt_name, 0, 0, 0, 0, 3, .hash = 11398999010424436621ull, .flags = DIGITOYENGINE_TYPE_ABSTRACT};
const Type vmenum_type = {0, 0, 0, 1, &vmvaluetype_type, &en_name, 0, 0, 0, 0, 16, .hash = 15180590948754747561ull, .flags = DIGITOYENGINE_TYPE_ABSTRACT}; // soyut kok: box'lanmaz
void digitoyengine_delegate_trace(GCHeader *o)
{
    VmDelegate *d = (VmDelegate *)o;
    if (d->target)
        gc_shade(d->target);
    if (d->left)
        gc_shade(&d->left->gc);
    if (d->right)
        gc_shade(&d->right->gc);
}
const Type vmdelegate_type = {0, 0, 0, 1, &vmobject_type, &dg_name, 0, 0, 0, 0, 17};
const Type vmmulticastdelegate_type = {0, 0, 0, 1, &vmdelegate_type, &mdg_name, 0, 0, 0, 0, 18};
VmDelegate *digitoyengine_delegate_new(const Type *t, const void *fn, GCHeader *target)
{
    VmDelegate *d = (VmDelegate *)gc_alloc(t);
    d->fn = fn;
    d->target = target;
    if (target)
        gc_write_barrier(&d->gc, target);
    return d;
}
int digitoyengine_delegate_count(VmDelegate *d)
{
    if (!d)
        DIGITOYENGINE_throw_null();
    if (d->fn)
        return 1;
    if (d->right)
        return digitoyengine_delegate_count(d->left) + digitoyengine_delegate_count(d->right);
    return digitoyengine_delegate_count(d->left) - d->skip_count;
}
VmDelegate *digitoyengine_delegate_at(VmDelegate *d, int index)
{
    if (d->fn)
        return d;
    if (d->right)
    {
        int left_count = digitoyengine_delegate_count(d->left);
        return index < left_count ? digitoyengine_delegate_at(d->left, index) : digitoyengine_delegate_at(d->right, index - left_count);
    }
    if (index >= d->skip_start)
        index += d->skip_count;
    return digitoyengine_delegate_at(d->left, index);
}
VmDelegate *digitoyengine_delegate_combine(VmDelegate *left, VmDelegate *right)
{
    if (!left)
        return right;
    if (!right)
        return left;
    VmDelegate *d = (VmDelegate *)gc_alloc(left->gc.type);
    d->left = left;
    d->right = right;
    gc_write_barrier(&d->gc, &left->gc);
    gc_write_barrier(&d->gc, &right->gc);
    return d;
}
VmDelegate *digitoyengine_delegate_remove(VmDelegate *left, VmDelegate *right)
{
    if (!left || !right)
        return left;
    int left_count = digitoyengine_delegate_count(left);
    int right_count = digitoyengine_delegate_count(right);
    for (int start = left_count - right_count; start >= 0; start--)
    {
        int equal = 1;
        for (int i = 0; i < right_count; i++)
        {
            VmDelegate *a = digitoyengine_delegate_at(left, start + i);
            VmDelegate *b = digitoyengine_delegate_at(right, i);
            if (a->fn != b->fn || a->target != b->target)
            {
                equal = 0;
                break;
            }
        }
        if (!equal)
            continue;
        if (start == 0 && right_count == left_count)
            return 0;
        VmDelegate *d = (VmDelegate *)gc_alloc(left->gc.type);
        d->left = left;
        d->skip_start = start;
        d->skip_count = right_count;
        gc_write_barrier(&d->gc, &left->gc);
        return d;
    }
    return left;
}
const Type vmint32_type = {0, 0, sizeof(GCHeader) + 4, 1, &vmvaluetype_type, &i32_name, vmint32_vtable, 3, 0, 0, 4};
const Type vmuint32_type = {0, 0, sizeof(GCHeader) + 4, 1, &vmvaluetype_type, &u32_name, vmuint32_vtable, 3, 0, 0, 5};
const Type vmint64_type = {0, 0, sizeof(GCHeader) + 8, 1, &vmvaluetype_type, &i64_name, vmint64_vtable, 3, 0, 0, 6};
const Type vmuint64_type = {0, 0, sizeof(GCHeader) + 8, 1, &vmvaluetype_type, &u64_name, vmuint64_vtable, 3, 0, 0, 7};
const Type vmint16_type = {0, 0, sizeof(GCHeader) + 2, 1, &vmvaluetype_type, &i16_name, vmint16_vtable, 3, 0, 0, 8};
const Type vmuint16_type = {0, 0, sizeof(GCHeader) + 2, 1, &vmvaluetype_type, &u16_name, vmuint16_vtable, 3, 0, 0, 9};
const Type vmsbyte_type = {0, 0, sizeof(GCHeader) + 1, 1, &vmvaluetype_type, &sb_name, vmsbyte_vtable, 3, 0, 0, 10};
const Type vmbyte_type = {0, 0, sizeof(GCHeader) + 1, 1, &vmvaluetype_type, &by_name, vmbyte_vtable, 3, 0, 0, 11};
const Type vmchar_type = {0, 0, sizeof(GCHeader) + 2, 1, &vmvaluetype_type, &ch_name, vmchar_vtable, 3, 0, 0, 12}; // UTF-16 kod birimi
const Type vmbool_type = {0, 0, sizeof(GCHeader) + 4, 1, &vmvaluetype_type, &bo_name, vmbool_vtable, 3, 0, 0, 13};
const Type vmsingle_type = {0, 0, sizeof(GCHeader) + 4, 1, &vmvaluetype_type, &sg_name, vmsingle_vtable, 3, 0, 0, 14};
const Type vmdouble_type = {0, 0, sizeof(GCHeader) + 8, 1, &vmvaluetype_type, &db_name, vmdouble_vtable, 3, 0, 0, 15};

// unbox: C# exact-tip kurali + CLR enum denkligi: boxed enum <-> underlying int, ayni underlying'li
// iki enum arasi da gecerli. null -> NullRef (native: ->type MMU faultlar; wasm: makro)
void *digitoyengine_unbox(GCHeader *o, const Type *t)
{
    DIGITOYENGINE_NULLCHECK(o);
    const Type *bt = o->type;
    if (DIGITOYENGINE_UNLIKELY(bt != t && bt->alias != t && t->alias != bt && !(bt->alias && bt->alias == t->alias)))
        DIGITOYENGINE_cast_fail(bt, t);
    return (char *)o + sizeof(GCHeader);
}

// ---- reflection wrapper deposu: tindex -> System.Type nesnesi (lazy, gc_add_root'lu) ----
static const Type *digitoyengine_type_type = 0;
static const Type *digitoyengine_fieldinfo_type = 0;
static const Type *digitoyengine_propertyinfo_type = 0;
static const Type *digitoyengine_methodinfo_type = 0;
static const Type *digitoyengine_ctorinfo_type = 0;
static GCHeader **digitoyengine_wrappers = 0;
static int digitoyengine_nwrappers = 0;
void digitoyengine_reflect_init(const Type *typeType, const Type *fieldInfoType, const Type *propertyInfoType, const Type *methodInfoType, const Type *ctorInfoType, int nwrappers)
{
    digitoyengine_type_type = typeType;
    digitoyengine_fieldinfo_type = fieldInfoType;
    digitoyengine_propertyinfo_type = propertyInfoType;
    digitoyengine_methodinfo_type = methodInfoType;
    digitoyengine_ctorinfo_type = ctorInfoType;
    digitoyengine_nwrappers = nwrappers;
    digitoyengine_wrappers = (GCHeader **)calloc((size_t)nwrappers, sizeof(GCHeader *));
}
GCHeader *digitoyengine_type_wrapper(const Type *t)
{
    if (!t || !digitoyengine_type_type)
        return 0;
    if (t->tindex == 0 && t->module) // dinamik modul tipi: wrapper descriptor'in kendisinde (malloc'lu Type, const degil)
    {
        Type *mt = (Type *)t;
        if (!mt->dyn_wrapper)
        {
            GCHeader *w = (GCHeader *)gc_alloc(digitoyengine_type_type);
            *(long long *)((char *)w + sizeof(GCHeader)) = (long long)(size_t)t;
            gc_add_root(w);
            mt->dyn_wrapper = w;
        }
        return mt->dyn_wrapper;
    }
    if (t->tindex == 0 || t->tindex >= digitoyengine_nwrappers)
        return 0;
    GCHeader **slot = &digitoyengine_wrappers[t->tindex];
    if (!*slot)
    {
        GCHeader *w = (GCHeader *)gc_alloc(digitoyengine_type_type);
        *(long long *)((char *)w + sizeof(GCHeader)) = (long long)(size_t)t; // System.Type.handle = ILK alan sozlesmesi
        gc_add_root(w);
        *slot = w;
    }
    return *slot;
}
GCHeader *digitoyengine_member_wrapper(DigitoyEngineMember *member)
{
    if (!member->wrapper)
    {
        const Type *wrapperType = member->kind == DIGITOYENGINE_MEMBER_FIELD ? digitoyengine_fieldinfo_type : digitoyengine_propertyinfo_type;
        if (!wrapperType)
            return 0;
        member->wrapper = (GCHeader *)gc_alloc(wrapperType);
        *(long long *)((char *)member->wrapper + sizeof(GCHeader)) = (long long)(size_t)member;
        gc_add_root(member->wrapper);
    }
    return member->wrapper;
}
GCHeader *digitoyengine_member_lookup(const Type *t, const VmString *name, int kind)
{
    for (; t; t = t->base)
        for (int i = 0; i < t->nmembers; i++)
        {
            DigitoyEngineMember *member = &t->members[i];
            if (member->kind == kind && vmstring_eq((VmString *)member->name, (VmString *)name))
                return digitoyengine_member_wrapper(member);
        }
    return 0;
}
static void digitoyengine_member_target(DigitoyEngineMember *member, GCHeader *target)
{
    if (member->isStatic)
        return;
    DIGITOYENGINE_NULLCHECK(target);
    if (!DIGITOYENGINE_is(target->type, member->declaringType))
        DIGITOYENGINE_cast_fail(target->type, member->declaringType);
}
GCHeader *digitoyengine_member_get(DigitoyEngineMember *member, GCHeader *target)
{
    digitoyengine_member_target(member, target);
    if (!member->get)
        DIGITOYENGINE_throw_io("reflection member is not readable");
    return member->get(target);
}
void digitoyengine_member_set(DigitoyEngineMember *member, GCHeader *target, GCHeader *value)
{
    digitoyengine_member_target(member, target);
    if (!member->set)
        DIGITOYENGINE_throw_io("reflection member is not writable");
    member->set(target, value);
}
GCHeader *digitoyengine_reflect_ref(GCHeader *value, const Type *target)
{
    if (value && !DIGITOYENGINE_is(value->type, target))
        DIGITOYENGINE_cast_fail(value->type, target);
    return value;
}
GCHeader *digitoyengine_reflect_unsupported(void)
{
    DIGITOYENGINE_throw_io("reflection does not support struct or fixed-array values");
    return 0;
}
// ---- metot reflection: MethodInfo kaydi -> System.Reflection.MethodInfo/ConstructorInfo wrapper; Invoke = sekil thunk'u ----
GCHeader *digitoyengine_method_wrapper(MethodInfo *m)
{
    if (!m)
        return 0;
    if (!m->wrapper)
    {
        const Type *wt = (m->flags & DIGITOYENGINE_METHOD_CTOR) ? digitoyengine_ctorinfo_type : digitoyengine_methodinfo_type;
        if (!wt)
            return 0;
        m->wrapper = (GCHeader *)gc_alloc(wt);
        *(long long *)((char *)m->wrapper + sizeof(GCHeader)) = (long long)(size_t)m;
        gc_add_root(m->wrapper);
    }
    return m->wrapper;
}
// Ada gore arama (C# GetMethod(name) / GetConstructor(Type.EmptyTypes)): bildiren tipten base'e. Ad gosterim adidir
// ("Owner.Name(args)"); karsilastirma "Owner." onekinden sonraki, '(' oncesindeki parcayla yapilir.
static int digitoyengine_method_name_eq(const MethodInfo *m, const VmString *name)
{
    if (!m->name || !name)
        return 0;
    int n = m->name->length, start = 0, end = n;
    for (int i = 0; i < n; i++)
        if (m->name->data[i] == '(') { end = i; break; }
    for (int i = end - 1; i >= 0; i--)
        if (m->name->data[i] == '.') { start = i + 1; break; }
    if (end - start != name->length)
        return 0;
    for (int i = 0; i < name->length; i++)
        if (m->name->data[start + i] != name->data[i])
            return 0;
    return 1;
}
GCHeader *digitoyengine_method_lookup(const Type *t, const VmString *name, int ctor)
{
    for (; t; t = t->base)
        for (int i = 0; i < t->nmethods; i++)
        {
            MethodInfo *m = &t->methods[i];
            if (ctor)
            {
                if ((m->flags & DIGITOYENGINE_METHOD_CTOR) && m->nparams == 0)
                    return digitoyengine_method_wrapper(m);
            }
            else if (!(m->flags & DIGITOYENGINE_METHOD_CTOR) && digitoyengine_method_name_eq(m, name))
                return digitoyengine_method_wrapper(m);
        }
    return 0;
}
// Kutulu arguman -> yuva (etiket + descriptor). Skaler/enum: exact-tip unbox; struct: kutu payload adresi; referans: tip denetimi.
static void digitoyengine_slot_from_object(DeSlot *s, unsigned char tag, const Type *pt, GCHeader *o)
{
    s->p = 0;
    switch (tag)
    {
    case 'o':
        if (o && pt && !DIGITOYENGINE_is(o->type, pt) && !(pt->flags & DIGITOYENGINE_TYPE_INTERFACE))
            DIGITOYENGINE_cast_fail(o->type, pt);
        s->p = o;
        return;
    case 'v':
        DIGITOYENGINE_NULLCHECK(o);
        s->p = pt ? digitoyengine_unbox(o, pt) : (char *)o + sizeof(GCHeader);
        return;
    case 'p': s->p = o ? (void *)(size_t) * (long long *)digitoyengine_unbox(o, &vmint64_type) : 0; return;
    case 'r': DIGITOYENGINE_throw_io("MethodInfo.Invoke: ref/out parametre desteklenmiyor"); return;
    default: break;
    }
    DIGITOYENGINE_NULLCHECK(o);
    void *v = pt ? digitoyengine_unbox(o, pt) : (char *)o + sizeof(GCHeader);
    switch (tag)
    {
    case 'i': s->i = *(cil_int *)v; break;
    case 'u': s->u = *(cil_uint *)v; break;
    case 'l': s->l = *(cil_long *)v; break;
    case 'q': s->q = *(cil_ulong *)v; break;
    case 'h': s->i = *(short *)v; break;
    case 'H': s->u = *(unsigned short *)v; break;
    case 'b': s->u = *(unsigned char *)v; break;
    case 'z': s->i = *(signed char *)v; break;
    case 'c': s->u = *(unsigned short *)v; break;
    case 'B': s->i = *(cil_int *)v != 0; break;
    case 'f': s->f = *(cil_float *)v; break;
    case 'd': s->d = *(cil_double *)v; break;
    default: DIGITOYENGINE_throw_io("MethodInfo.Invoke: bilinmeyen parametre etiketi");
    }
}
static GCHeader *digitoyengine_object_from_slot(const DeSlot *s, unsigned char tag, const Type *rt)
{
    if (rt && (rt->flags & DIGITOYENGINE_TYPE_ENUM) && tag != 'o' && tag != 'v')
        return (GCHeader *)digitoyengine_box_enum(s->i, rt);
    switch (tag)
    {
    case 'V': return 0;
    case 'o': return (GCHeader *)s->p;
    case 'v': return (GCHeader *)((char *)s->p - sizeof(GCHeader)); // Invoke kutuyu onceden acti: payload -> kutu
    case 'p': return (GCHeader *)digitoyengine_box_i64((long long)(size_t)s->p);
    case 'i': return (GCHeader *)digitoyengine_box_i32(s->i);
    case 'u': return (GCHeader *)digitoyengine_box_u32(s->u);
    case 'l': return (GCHeader *)digitoyengine_box_i64(s->l);
    case 'q': return (GCHeader *)digitoyengine_box_u64(s->q);
    case 'h': return (GCHeader *)digitoyengine_box_i16((short)s->i);
    case 'H': return (GCHeader *)digitoyengine_box_u16((unsigned short)s->u);
    case 'b': return (GCHeader *)digitoyengine_box_u8((unsigned char)s->u);
    case 'z': return (GCHeader *)digitoyengine_box_i8((signed char)s->i);
    case 'c': return (GCHeader *)digitoyengine_box_char((cil_char)s->u);
    case 'B': return (GCHeader *)digitoyengine_box_bool(s->i);
    case 'f': return (GCHeader *)digitoyengine_box_f32(s->f);
    case 'd': return (GCHeader *)digitoyengine_box_f64(s->d);
    default: DIGITOYENGINE_throw_io("MethodInfo.Invoke: bilinmeyen donus etiketi"); return 0;
    }
}
GCHeader *digitoyengine_method_invoke(const MethodInfo *m, GCHeader *target, struct VmArray *args)
{
    if (!m->fn && !(m->flags & DIGITOYENGINE_METHOD_ABSTRACT))
        DIGITOYENGINE_throw_io("MethodInfo.Invoke: metodun govdesi yok (uygulanmamis extern)");
    int isStatic = (m->flags & DIGITOYENGINE_METHOD_STATIC) != 0;
    int nargs = args ? args->len : 0;
    if (nargs != m->nparams)
        DIGITOYENGINE_throw_io("MethodInfo.Invoke: arguman sayisi uyusmuyor");
    if (!isStatic)
    {
        DIGITOYENGINE_NULLCHECK(target);
        if (m->declaringType && !(m->declaringType->flags & DIGITOYENGINE_TYPE_INTERFACE) && !DIGITOYENGINE_is(target->type, m->declaringType))
            DIGITOYENGINE_cast_fail(target->type, m->declaringType);
    }
    DeSlot a[32], r;
    if (nargs + 1 > 32)
        DIGITOYENGINE_throw_io("MethodInfo.Invoke: 31'den fazla parametre");
    int base = 0;
    if (!isStatic)
    {
        // struct instance metodu: this = kutu payload'u (C imzasi struct*); class: nesne
        a[0].p = (m->declaringType && (m->declaringType->flags & DIGITOYENGINE_TYPE_STRUCT)) ? (void *)((char *)target + sizeof(GCHeader)) : (void *)target;
        base = 1;
    }
    GCHeader **items = args ? (GCHeader **)args->data : 0;
    for (int i = 0; i < nargs; i++)
        digitoyengine_slot_from_object(&a[base + i], m->param_tags ? m->param_tags[i] : 'o', m->param_types ? m->param_types[i] : 0, items[i]);
    // Sanal metot: .NET gibi alicinin runtime tipinden dispatch (vtable / iface tablosu); aksi halde dogrudan fn.
    const void *fn = m->fn;
    if (!isStatic && m->vslot >= 0 && m->declaringType && !(m->declaringType->flags & DIGITOYENGINE_TYPE_STRUCT))
    {
        const Type *rt = target->type;
        if (m->declaringType->flags & DIGITOYENGINE_TYPE_INTERFACE)
            fn = DIGITOYENGINE_itable(rt, m->declaringType)[m->vslot];
        else if (rt->vtable && m->vslot < rt->nvtable)
            fn = rt->vtable[m->vslot];
    }
    if (!fn)
        DIGITOYENGINE_throw_io("MethodInfo.Invoke: metodun govdesi yok (abstract bildirim)");
    r.p = 0;
    if (m->ret_tag == 'v') // struct donusu: kutu onceden ayrilir, thunk payload'a kopyalar
    {
        if (!m->returnType)
            DIGITOYENGINE_throw_io("MethodInfo.Invoke: struct donus descriptor'suz");
        GCHeader *box = (GCHeader *)gc_alloc(m->returnType);
        r.p = (char *)box + sizeof(GCHeader);
    }
    digitoyengine_thunks[m->shape](fn, a, &r);
    return digitoyengine_object_from_slot(&r, m->ret_tag, m->returnType);
}
// vmstring_length/get/eq vmrt.h'da static inline (Mach-O duplicate cozumu)
VmString *vmstring_alloc(int len)
{
    VmString *s = (VmString *)gc_alloc(&vmstring_type); // gc_alloc toplamaz (safepoint modeli) -> arg'lar guvende
    s->length = len;
    s->data = (const unsigned short *)calloc(len > 0 ? len : 1, 2);
    return s;
}
VmString *vmstring_from_cstr(const char *ascii)
{
    int n = (int)strlen(ascii);
    VmString *r = vmstring_alloc(n);
    unsigned short *d = (unsigned short *)r->data;
    for (int i = 0; i < n; i++)
        d[i] = (unsigned short)ascii[i];
    return r;
}

// ---- UTF-16 -> UTF-8 cikis (surrogate'li): platform cikis mekanizmasi (Console + raporlar) ----
void vm_write_utf8_to(FILE *f, const unsigned short *d, int len)
{
    char buf[512];
    int bi = 0;
    for (int i = 0; i < len; i++)
    {
        unsigned int cp = d[i];
        if (cp >= 0xD800 && cp < 0xDC00 && i + 1 < len && d[i + 1] >= 0xDC00 && d[i + 1] < 0xE000)
        {
            cp = 0x10000 + ((cp - 0xD800) << 10) + (d[i + 1] - 0xDC00);
            i++;
        }
        if (bi > 500)
        {
            fwrite(buf, 1, bi, f);
            bi = 0;
        }
        if (cp < 0x80)
            buf[bi++] = (char)cp;
        else if (cp < 0x800)
        {
            buf[bi++] = (char)(0xC0 | (cp >> 6));
            buf[bi++] = (char)(0x80 | (cp & 63));
        }
        else if (cp < 0x10000)
        {
            buf[bi++] = (char)(0xE0 | (cp >> 12));
            buf[bi++] = (char)(0x80 | ((cp >> 6) & 63));
            buf[bi++] = (char)(0x80 | (cp & 63));
        }
        else
        {
            buf[bi++] = (char)(0xF0 | (cp >> 18));
            buf[bi++] = (char)(0x80 | ((cp >> 12) & 63));
            buf[bi++] = (char)(0x80 | ((cp >> 6) & 63));
            buf[bi++] = (char)(0x80 | (cp & 63));
        }
    }
    if (bi)
        fwrite(buf, 1, bi, f);
}
void vm_write_utf8(const unsigned short *d, int len)
{
    vm_write_utf8_to(stdout, d, len);
}
// rapor yardimcisi: VmString adi stderr'e (yoksa "?")
static void DIGITOYENGINE_put_name(FILE *f, const VmString *s)
{
    if (s)
        vm_write_utf8_to(f, s->data, s->length);
    else
        fputs("?", f);
}

// ---- M8 shadow stack ----
RtFrame DIGITOYENGINE_stack[DIGITOYENGINE_STACK_MAX];
int DIGITOYENGINE_sp = 0;
#ifdef DIGITOYENGINE_DEBUG
void (*DIGITOYENGINE_dbg_step)(int line) = 0;
#endif
// trace satiri: "  at Ad (dosya:satir:kolon)" ya da dosyasiz "  at Ad:satir:kolon"
static void DIGITOYENGINE_put_frame(FILE *f, const MethodInfo *mi, int line)
{
    fputs("  at ", f);
    DIGITOYENGINE_put_name(f, mi->name);
    if (mi->file && mi->file->length)
    {
        fputs(" (", f);
        DIGITOYENGINE_put_name(f, mi->file);
        fprintf(f, ":%d:%d)\n", line >> 10, line & 1023);
    }
    else
        fprintf(f, ":%d:%d\n", line >> 10, line & 1023);
}
void DIGITOYENGINE_dump_stack(void)
{
    for (int i = DIGITOYENGINE_sp - 1; i >= 0; i--)
        DIGITOYENGINE_put_frame(stderr, DIGITOYENGINE_stack[i].mi, DIGITOYENGINE_stack[i].line);
}

#ifdef DIGITOYENGINE_DEBUG
#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h> // VirtualQuery (dump_ref: cop pointer deref etmeden once)
#endif
RtFrame DIGITOYENGINE_step_ring[DIGITOYENGINE_STEP_RING];
unsigned DIGITOYENGINE_step_n = 0;

// Adres okunabilir mi? (crash handler icinde cop pointer'i deref etmeden once)
static int DIGITOYENGINE_mem_readable(const void *p, size_t n)
{
    if (!p)
        return 0;
#if defined(_WIN32)
    MEMORY_BASIC_INFORMATION mbi;
    if (!VirtualQuery(p, &mbi, sizeof mbi))
        return 0;
    if (mbi.State != MEM_COMMIT || (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)))
        return 0;
    return (const char *)p + n <= (const char *)mbi.BaseAddress + mbi.RegionSize;
#else
    (void)n;
    return 1; // POSIX: en iyi caba (gc_objects listesi zaten canli nesneyi ayirt eder)
#endif
}
// Referansin durumu: canli heap nesnesi (gc_objects listesinde) / serbest (poison) / heap disi.
static int DIGITOYENGINE_heap_live(const GCHeader *p)
{
    for (const GCHeader *o = gc_objects; o; o = o->next)
        if (o == p)
            return 1;
    return 0;
}
static void DIGITOYENGINE_dump_ref(FILE *f, const GCHeader *o)
{
    if (!o) { fputs("null", f); return; }
    fprintf(f, "%p", (const void *)o);
    if (DIGITOYENGINE_heap_live(o))
        fputs(" [canli", f);
#ifdef DIGITOYENGINE_GC_POISON
    else if (gc_is_freed((void *)o))
    { fputs(" [SERBEST BIRAKILMIS (dangling)]", f); return; }
#endif
    else if (DIGITOYENGINE_mem_readable(o, sizeof(GCHeader)) && o->age == GC_IMMORTAL)
        fputs(" [immortal", f);
    else
    { fputs(" [HEAP DISI / nesne degil]", f); return; }
    const Type *t = DIGITOYENGINE_mem_readable(o, sizeof(GCHeader)) ? o->type : 0;
    if (t && DIGITOYENGINE_mem_readable(t, sizeof(Type)) && t->name && DIGITOYENGINE_mem_readable(t->name, sizeof(VmString)))
    {
        fputs(" ", f);
        DIGITOYENGINE_put_name(f, t->name);
        if (t == &vmstring_type)
        {
            const VmString *s = (const VmString *)o;
            fputs(" \"", f);
            vm_write_utf8_to(f, s->data, s->length < 80 ? s->length : 80);
            fputs(s->length > 80 ? "...\"" : "\"", f);
        }
    }
    else
        fputs(" tip=?", f);
    fputs("]", f);
}
static void DIGITOYENGINE_dump_local(FILE *f, const RtLocal *l)
{
    fprintf(f, "      %s = ", l->name);
    if (!DIGITOYENGINE_mem_readable(l->addr, 8)) { fputs("<adres okunamiyor>\n", f); return; }
    switch (l->tag)
    {
    case 'i': fprintf(f, "%d", *(const int *)l->addr); break;
    case 'u': fprintf(f, "%u", *(const unsigned *)l->addr); break;
    case 'f': fprintf(f, "%g", *(const float *)l->addr); break;
    case 'd': fprintf(f, "%g", *(const double *)l->addr); break;
    case 'l': fprintf(f, "%lld", *(const long long *)l->addr); break;
    case 'q': fprintf(f, "%llu", *(const unsigned long long *)l->addr); break;
    case 'c': fprintf(f, "'%c' (%d)", *(const cil_char *)l->addr < 127 ? (char)*(const cil_char *)l->addr : '?', *(const cil_char *)l->addr); break;
    case 'h': fprintf(f, "%d", *(const short *)l->addr); break;
    case 'H': fprintf(f, "%u", *(const unsigned short *)l->addr); break;
    case 'b': fprintf(f, "%u", *(const unsigned char *)l->addr); break;
    case 'z': fprintf(f, "%d", *(const signed char *)l->addr); break;
    case 'B': fputs(*(const int *)l->addr ? "true" : "false", f); break;
    case 'o': DIGITOYENGINE_dump_ref(f, *(GCHeader *const *)l->addr); break;
    case 'r': fprintf(f, "ref -> %p", *(void *const *)l->addr); break;
    case 'v': fprintf(f, "struct @%p", l->addr); break;
    default: fprintf(f, "? @%p (=%p)", l->addr, *(void *const *)l->addr); break; // pointer tipli arg (ref struct vb.)
    }
    fputc('\n', f);
}
void DIGITOYENGINE_dump_locals(FILE *f)
{
    int lo = DIGITOYENGINE_sp - 12 > 0 ? DIGITOYENGINE_sp - 12 : 0;
    fprintf(f, "[locals] en ustteki %d frame:\n", DIGITOYENGINE_sp - lo);
    for (int i = DIGITOYENGINE_sp - 1; i >= lo; i--)
    {
        const RtFrame *fr = &DIGITOYENGINE_stack[i];
        fputs("  ", f);
        DIGITOYENGINE_put_frame(f, fr->mi, fr->line);
        if (!fr->dbg_locals)
        { fputs("      (local tablosu yok)\n", f); continue; }
        for (int k = 0; k < fr->dbg_nlocals; k++)
            DIGITOYENGINE_dump_local(f, &fr->dbg_locals[k]);
    }
}
void DIGITOYENGINE_dump_steps(FILE *f)
{
    unsigned n = DIGITOYENGINE_step_n < DIGITOYENGINE_STEP_RING ? DIGITOYENGINE_step_n : DIGITOYENGINE_STEP_RING;
    fprintf(f, "[steps] son %u adim (eski -> yeni):\n", n);
    for (unsigned k = 0; k < n; k++)
    {
        const RtFrame *r = &DIGITOYENGINE_step_ring[(DIGITOYENGINE_step_n - n + k) & (DIGITOYENGINE_STEP_RING - 1)];
        if (r->mi)
            DIGITOYENGINE_put_frame(f, r->mi, r->line);
    }
}
#endif

// ================= crash dump (tum platformlar, benzersiz dosya) =================
// Host baslangicta yazilabilir dizini verir. Benzersiz yol BiR KEZ hesaplanir (time+pid ->
// birden fazla crash/launch birbirini EZMEZ). Yazim low-level open/write ile yapilir:
// malloc/stdio yok -> POSIX sinyal handler'indan ve Windows VEH'ten de guvenli (async-signal-safe).
// dir verilmezse (crash_ready=0) DIGITOYENGINE_crash_dump sessizce no-op olur.
#include <time.h>
#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#elif !defined(__EMSCRIPTEN__)
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <sys/types.h>
#endif

static char DIGITOYENGINE_crash_path[600];
static int DIGITOYENGINE_crash_ready = 0;
static void DIGITOYENGINE_narrow_name(char *dst, size_t cap, const VmString *s); // asagida tanimli

void digitoyengine_crash_init(const char *dir)
{
    DIGITOYENGINE_crash_ready = 0;
    if (!dir || !*dir)
        return;
    long t = (long)time(NULL);
#if defined(_WIN32)
    long pid = (long)GetCurrentProcessId();
#elif defined(__EMSCRIPTEN__)
    long pid = 0;
#else
    long pid = (long)getpid();
#endif
    // <dir>/crash_<unixtime>_<pid>.txt -> time+pid benzersizligi ezmeyi onler
    int n = snprintf(DIGITOYENGINE_crash_path, sizeof DIGITOYENGINE_crash_path,
                     "%s/crash_%ld_%ld.txt", dir, t, pid);
    DIGITOYENGINE_crash_ready = (n > 0 && n < (int)sizeof DIGITOYENGINE_crash_path);
}

#if !defined(__EMSCRIPTEN__)
#if defined(_WIN32)
typedef HANDLE DIGITOYENGINE_cfd;
#define DIGITOYENGINE_CFD_BAD INVALID_HANDLE_VALUE
#else
typedef int DIGITOYENGINE_cfd;
#define DIGITOYENGINE_CFD_BAD (-1)
#endif
static DIGITOYENGINE_cfd DIGITOYENGINE_crash_openfd(void)
{
    if (!DIGITOYENGINE_crash_ready)
        return DIGITOYENGINE_CFD_BAD;
#if defined(_WIN32)
    return CreateFileA(DIGITOYENGINE_crash_path, GENERIC_WRITE, 0, NULL,
                       CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
#else
    return open(DIGITOYENGINE_crash_path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
#endif
}
static void DIGITOYENGINE_crash_wr(DIGITOYENGINE_cfd fd, const char *s, int len)
{
    if (fd == DIGITOYENGINE_CFD_BAD || len <= 0)
        return;
#if defined(_WIN32)
    DWORD wr;
    WriteFile(fd, s, (DWORD)len, &wr, NULL);
#else
    const char *p = s;
    int left = len;
    while (left > 0)
    {
        ssize_t r = write(fd, p, (size_t)left);
        if (r < 0)
        {
            if (errno == EINTR)
                continue;
            break;
        }
        p += r;
        left -= (int)r;
    }
#endif
}
static void DIGITOYENGINE_crash_close(DIGITOYENGINE_cfd fd)
{
    if (fd == DIGITOYENGINE_CFD_BAD)
        return;
#if defined(_WIN32)
    CloseHandle(fd);
#else
    close(fd);
#endif
}
static void DIGITOYENGINE_crash_ws(DIGITOYENGINE_cfd fd, const char *s)
{
    DIGITOYENGINE_crash_wr(fd, s, (int)strlen(s));
}
static void DIGITOYENGINE_crash_wl(DIGITOYENGINE_cfd fd, long v) // long -> ondalik (signal-safe, snprintf yok)
{
    char b[24];
    int i = (int)sizeof b;
    int neg = v < 0;
    unsigned long u = neg ? (unsigned long)(-(v + 1)) + 1UL : (unsigned long)v;
    if (!u)
        b[--i] = '0';
    while (u)
    {
        b[--i] = (char)('0' + u % 10);
        u /= 10;
    }
    if (neg)
        b[--i] = '-';
    DIGITOYENGINE_crash_wr(fd, b + i, (int)sizeof b - i);
}
static void DIGITOYENGINE_crash_wname(DIGITOYENGINE_cfd fd, const VmString *s) // VmString UTF-16 -> ASCII daraltma
{
    if (!s || s->length <= 0)
    {
        DIGITOYENGINE_crash_wr(fd, "?", 1);
        return;
    }
    char b[128];
    int n = 0;
    for (int i = 0; i < s->length && n < (int)sizeof b; i++)
    {
        unsigned short c = s->data[i];
        b[n++] = (c >= 32 && c < 127) ? (char)c : '?';
    }
    DIGITOYENGINE_crash_wr(fd, b, n);
}
static void DIGITOYENGINE_crash_frame(DIGITOYENGINE_cfd fd, const MethodInfo *mi, int line)
{
    int hasfile = mi->file && mi->file->length;
    DIGITOYENGINE_crash_ws(fd, "  at ");
    DIGITOYENGINE_crash_wname(fd, mi->name);
    if (hasfile)
    {
        DIGITOYENGINE_crash_ws(fd, " (");
        DIGITOYENGINE_crash_wname(fd, mi->file);
    }
    DIGITOYENGINE_crash_wr(fd, ":", 1);
    DIGITOYENGINE_crash_wl(fd, line >> 10);
    DIGITOYENGINE_crash_wr(fd, ":", 1);
    DIGITOYENGINE_crash_wl(fd, line & 1023);
    if (hasfile)
        DIGITOYENGINE_crash_wr(fd, ")", 1);
    DIGITOYENGINE_crash_wr(fd, "\n", 1);
}
#endif // !__EMSCRIPTEN__

// Crash'i benzersiz dosyaya dok (reason + en ustteki ~64 frame). Signal-safe.
void DIGITOYENGINE_crash_dump(const char *reason, const RtFrame *frames, int count)
{
#if defined(__EMSCRIPTEN__)
    (void)reason;
    (void)frames;
    (void)count; // WASM: kalici FS yok -> ileride host JS hook'u (sendBeacon/IDBFS) ile cozulecek
#else
    DIGITOYENGINE_cfd fd = DIGITOYENGINE_crash_openfd();
    if (fd == DIGITOYENGINE_CFD_BAD)
        return;
    DIGITOYENGINE_crash_ws(fd, "reason: ");
    DIGITOYENGINE_crash_ws(fd, reason ? reason : "?");
    DIGITOYENGINE_crash_wr(fd, "\n", 1);
    int lo = count - 64 > 0 ? count - 64 : 0;
    for (int i = count - 1; i >= lo; i--)
        DIGITOYENGINE_crash_frame(fd, frames[i].mi, frames[i].line);
    DIGITOYENGINE_crash_close(fd);
#endif
}
// M8b-2: exception durumu + firlat/yeniden-firlat
RtTry *DIGITOYENGINE_try_top = 0;
int DIGITOYENGINE_ex_kind = 0;
GCHeader *DIGITOYENGINE_ex_obj = 0;
char DIGITOYENGINE_ex_msg[160];
RtFrame DIGITOYENGINE_ex_trace[DIGITOYENGINE_STACK_MAX];
int DIGITOYENGINE_ex_trace_n = 0;
const Type *DIGITOYENGINE_ex_kind_type[8];
void (*DIGITOYENGINE_ex_kind_ctor[8])(GCHeader *);
const Type *DIGITOYENGINE_notimpl_type = 0;
void (*DIGITOYENGINE_exception_ctor_msg)(GCHeader *, VmString *) = 0;
GCHeader *DIGITOYENGINE_ex_current(void)
{
    if (DIGITOYENGINE_ex_obj)
        return DIGITOYENGINE_ex_obj;
    int k = DIGITOYENGINE_ex_kind;
    if (k <= 0 || k >= 8 || !DIGITOYENGINE_ex_kind_type[k])
        return 0;
    GCHeader *o = (GCHeader *)gc_alloc(DIGITOYENGINE_ex_kind_type[k]);
    DIGITOYENGINE_ex_obj = o; // ctor/bind sirasinda GC calismaz (safepoint yok); ayni handler zincirinde ayni nesne
    if (DIGITOYENGINE_ex_kind_ctor[k])
        DIGITOYENGINE_ex_kind_ctor[k](o);
    return o;
}
void DIGITOYENGINE_throw_notimpl(const char *what)
{
    if (!DIGITOYENGINE_notimpl_type || !DIGITOYENGINE_exception_ctor_msg)
        DIGITOYENGINE_throw_io(what); // corelib yuzeyi yok: en azindan mesajli, sessiz olmayan cikis
    GCHeader *o = (GCHeader *)gc_alloc(DIGITOYENGINE_notimpl_type);
    DIGITOYENGINE_exception_ctor_msg(o, digitoyengine_from_utf8(what));
    DIGITOYENGINE_throw(DIGITOYENGINE_EX_USER, o);
}
void DIGITOYENGINE_bind_trace(long long *miArr, int *lineArr, int *count, int cap)
{
    int n = DIGITOYENGINE_ex_trace_n < cap ? DIGITOYENGINE_ex_trace_n : cap;
    for (int i = 0; i < n; i++)
    {
        miArr[i] = (long long)(size_t)DIGITOYENGINE_ex_trace[i].mi; // intptr yerine size_t: stdint kosulsuz dahil degil
        lineArr[i] = DIGITOYENGINE_ex_trace[i].line;                // PAKETLI saklanir ((satir<<10)|kolon); gosterim cozer
    }
    *count = n;
}
// unwind/dispatch: trace YAKALAMAZ (DIGITOYENGINE_rethrow orijinal kopyayi korur)
#ifdef _WIN32
// msvcrt longjmp Frame!=0 ise RtlUnwindEx ile SEH unwind yapar; VEH pc-redirect'in sahte
// cercevesinde bu STATUS_BAD_FUNCTION_TABLE ile cokebilir (stack copune bagli flaky).
// Frame (jmp_buf ilk alani) = 0 -> unwind'siz longjmp. SEH unwind'e ihtiyacimiz yok:
// shadow stack (DIGITOYENGINE_sp) elle restore ediliyor, C'de destructor yok. Wine/JS motoru deseni.
#define DIGITOYENGINE_LJ_NOUNWIND(b) (((void **)(b))[0] = 0)
#else
#define DIGITOYENGINE_LJ_NOUNWIND(b) ((void)0)
#endif
static void DIGITOYENGINE_dispatch(void)
{
    // 1) coroutine handler ara: shadow'u icten disa tara (frame'li giris + aktif __trypc)
    int ci = -1;
    for (int i = DIGITOYENGINE_sp - 1; i >= 0; i--)
    {
        RtFrame *fr = &DIGITOYENGINE_stack[i];
        if (fr->frame && fr->mi->trypc_off >= 0 && *(int *)((char *)fr->frame + fr->mi->trypc_off) != 0)
        {
            ci = i;
            break;
        }
    }
    // 2) daha ICTEKI duz (setjmp) handler kazanir; sahibi = index t->sp-1. boundary sayilmaz.
    RtTry *t = DIGITOYENGINE_try_top;
    if (t && !t->boundary && t->sp - 1 >= ci)
    {
        DIGITOYENGINE_try_top = t->prev;
        DIGITOYENGINE_sp = t->sp;
        DIGITOYENGINE_LJ_NOUNWIND(t->buf);
        longjmp(t->buf, 1);
    }
    if (ci >= 0)
    {
        // hedef frame'in pc'sini catch resume noktasina yonlendir (pc frame'in ILK alani).
        // Boundary'ye donunce scheduler agaca yeniden girer; atalar await'te asagi iner, catch calisir.
        RtFrame *fr = &DIGITOYENGINE_stack[ci];
        *(int *)fr->frame = *(int *)((char *)fr->frame + fr->mi->trypc_off);
        while (DIGITOYENGINE_try_top && !DIGITOYENGINE_try_top->boundary)
            DIGITOYENGINE_try_top = DIGITOYENGINE_try_top->prev; // handler ustunde kalan olu duz try'lar
        if (DIGITOYENGINE_try_top)
        {
            RtTry *bd = DIGITOYENGINE_try_top;
            DIGITOYENGINE_try_top = bd->prev; // resume dongusu basinda yeniden kurulur
            DIGITOYENGINE_sp = bd->sp;
            DIGITOYENGINE_LJ_NOUNWIND(bd->buf);
            longjmp(bd->buf, 1);
        }
    }
    fputs("[exception] ", stderr);
    if (DIGITOYENGINE_ex_kind == DIGITOYENGINE_EX_USER)
    {
        DIGITOYENGINE_put_name(stderr, DIGITOYENGINE_ex_obj ? DIGITOYENGINE_ex_obj->type->name : 0);
        // LAYOUT SOZLESMESI (corelib.c DigitoyEngineException): GCHeader'dan hemen sonra VmString *message.
        const VmString *msg = DIGITOYENGINE_ex_obj ? *(const VmString **)((const char *)DIGITOYENGINE_ex_obj + sizeof(GCHeader)) : 0;
        if (msg) { fputs(": ", stderr); DIGITOYENGINE_put_name(stderr, msg); }
    }
    else
        fputs(DIGITOYENGINE_ex_msg, stderr);
    fputc('\n', stderr);
    for (int i = DIGITOYENGINE_ex_trace_n - 1; i >= 0; i--)
        DIGITOYENGINE_put_frame(stderr, DIGITOYENGINE_ex_trace[i].mi, DIGITOYENGINE_ex_trace[i].line);
#ifdef DIGITOYENGINE_DEBUG
    if (DIGITOYENGINE_sp > 0) // canli frame'ler (try'siz firlatma: C stack'i hala ayakta)
    {
        DIGITOYENGINE_dump_locals(stderr);
        DIGITOYENGINE_dump_steps(stderr);
    }
#endif
    // benzersiz crash dosyasina da yaz (mobilde disk kuyruguna girer, sonraki acilista gonderilir)
    char DIGITOYENGINE_reason[128];
    if (DIGITOYENGINE_ex_kind == DIGITOYENGINE_EX_USER)
        DIGITOYENGINE_narrow_name(DIGITOYENGINE_reason, sizeof DIGITOYENGINE_reason, DIGITOYENGINE_ex_obj ? DIGITOYENGINE_ex_obj->type->name : 0);
    else
        snprintf(DIGITOYENGINE_reason, sizeof DIGITOYENGINE_reason, "%s", DIGITOYENGINE_ex_msg);
    DIGITOYENGINE_crash_dump(DIGITOYENGINE_reason, DIGITOYENGINE_ex_trace, DIGITOYENGINE_ex_trace_n);
    exit(134);
}
void DIGITOYENGINE_throw(int kind, GCHeader *obj)
{
    DIGITOYENGINE_ex_kind = kind;
    DIGITOYENGINE_ex_obj = obj;
    // firlatma ANI trace'i sabitle (unwind DIGITOYENGINE_sp'yi bozar; rapor/catch bunu kullanir)
    memcpy(DIGITOYENGINE_ex_trace, DIGITOYENGINE_stack, (size_t)DIGITOYENGINE_sp * sizeof(RtFrame));
    DIGITOYENGINE_ex_trace_n = DIGITOYENGINE_sp;
    DIGITOYENGINE_dispatch();
}
void DIGITOYENGINE_rethrow(void)
{
    DIGITOYENGINE_dispatch(); // kopya korunur: rapor orijinal firlatma noktasini gosterir
}
void DIGITOYENGINE_throw_null(void)
{
    snprintf(DIGITOYENGINE_ex_msg, sizeof(DIGITOYENGINE_ex_msg), "NullReference");
    DIGITOYENGINE_throw(DIGITOYENGINE_EX_NULL, 0);
}
void DIGITOYENGINE_throw_bounds(int i, int n)
{
    snprintf(DIGITOYENGINE_ex_msg, sizeof(DIGITOYENGINE_ex_msg), "IndexOutOfRange: %d (len %d)", i, n);
    DIGITOYENGINE_throw(DIGITOYENGINE_EX_BOUNDS, 0);
}
void DIGITOYENGINE_throw_div(void)
{
    snprintf(DIGITOYENGINE_ex_msg, sizeof(DIGITOYENGINE_ex_msg), "DivideByZero");
    DIGITOYENGINE_throw(DIGITOYENGINE_EX_DIV, 0);
}
// .NET StackOverflowException gibi: yakalanamaz, sureci sonlandirir (unwind guvenli degil).
void DIGITOYENGINE_stack_overflow(void)
{
    fputs("[stack overflow] max recursion depth exceeded\n", stderr);
    int top = DIGITOYENGINE_sp; // en ustteki ~40 frame yeter
    int lo = top - 40 > 0 ? top - 40 : 0;
    for (int i = top - 1; i >= lo; i--)
        DIGITOYENGINE_put_frame(stderr, DIGITOYENGINE_stack[i].mi, DIGITOYENGINE_stack[i].line);
    DIGITOYENGINE_crash_dump("stack overflow", DIGITOYENGINE_stack, DIGITOYENGINE_sp);
    exit(134);
}
// ASCII daraltma: DIGITOYENGINE_ex_msg char tamponu icin (tip adlari ASCII; rapor yolu, soguk)
static void DIGITOYENGINE_narrow_name(char *dst, size_t cap, const VmString *s)
{
    size_t n = 0;
    if (s)
        for (; n < cap - 1 && (int)n < s->length; n++)
            dst[n] = (char)s->data[n];
    else if (cap > 1)
        dst[n++] = '?';
    dst[n] = 0;
}
void DIGITOYENGINE_cast_fail(const Type *from, const Type *to)
{
    char a[64], b[64];
    DIGITOYENGINE_narrow_name(a, sizeof a, from ? from->name : 0);
    DIGITOYENGINE_narrow_name(b, sizeof b, to ? to->name : 0);
    snprintf(DIGITOYENGINE_ex_msg, sizeof(DIGITOYENGINE_ex_msg), "InvalidCast: %s -> %s", a, b);
    DIGITOYENGINE_throw(DIGITOYENGINE_EX_CAST, 0);
}
void DIGITOYENGINE_throw_io(const char *msg)
{
    snprintf(DIGITOYENGINE_ex_msg, sizeof(DIGITOYENGINE_ex_msg), "IOError: %s", msg);
    DIGITOYENGINE_throw(DIGITOYENGINE_EX_IO, 0);
}

// ---- M8c: donanim null-fault -> NullReference (CoreCLR/HotSpot deseni) ----
// Native'de DIGITOYENGINE_NULLCHECK bos: null deref sayfa-0'a dokunur (alan offset < 4096), MMU fault verir.
// Handler fault'u dogrular (adres guard sayfada + VM kodundayiz) ve RIP/PC'yi DIGITOYENGINE_throw_null'a
// yonlendirir -> normal exception akisi (try varsa longjmp, yoksa rapor+trace+exit).
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
static LONG WINAPI DIGITOYENGINE_veh(EXCEPTION_POINTERS *ep)
{
    if (ep->ExceptionRecord->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && ep->ExceptionRecord->NumberParameters >= 2 && ep->ExceptionRecord->ExceptionInformation[1] < 4096 && DIGITOYENGINE_sp > 0)
    {
#if defined(_M_X64) || defined(__x86_64__)
        ep->ContextRecord->Rsp = (ep->ContextRecord->Rsp & ~15ULL) - 8; // call hizalamasi taklidi
        ep->ContextRecord->Rip = (DWORD64)(ULONG_PTR)DIGITOYENGINE_throw_null;
        return EXCEPTION_CONTINUE_EXECUTION;
#elif defined(_M_ARM64) || defined(__aarch64__)
        ep->ContextRecord->Sp &= ~15ULL;
        ep->ContextRecord->Pc = (DWORD64)(ULONG_PTR)DIGITOYENGINE_throw_null;
        return EXCEPTION_CONTINUE_EXECUTION;
#endif
    }
    // bizim null-guard'imiz degil: gercek erisim ihlali -> cikmadan crash'i benzersiz dosyaya VE stderr'e yaz
    if (ep->ExceptionRecord->ExceptionCode == EXCEPTION_ACCESS_VIOLATION)
    {
        fprintf(stderr, "[native fault] access violation at %p (addr %p), managed trace (%d frames):\n",
                (void *)ep->ExceptionRecord->ExceptionAddress,
                ep->ExceptionRecord->NumberParameters >= 2 ? (void *)ep->ExceptionRecord->ExceptionInformation[1] : 0, DIGITOYENGINE_sp);
        for (int i = DIGITOYENGINE_sp - 1; i >= 0 && i >= DIGITOYENGINE_sp - 40; i--)
            DIGITOYENGINE_put_frame(stderr, DIGITOYENGINE_stack[i].mi, DIGITOYENGINE_stack[i].line);
#ifdef DIGITOYENGINE_GC_POISON
        gc_dump_recent_freed(stderr);
#endif
#ifdef DIGITOYENGINE_DEBUG
        DIGITOYENGINE_dump_locals(stderr);
        DIGITOYENGINE_dump_steps(stderr);
#endif
        fflush(stderr);
        DIGITOYENGINE_crash_dump("native fault (access violation)", DIGITOYENGINE_stack, DIGITOYENGINE_sp);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
__attribute__((constructor)) static void DIGITOYENGINE_install_veh(void)
{
    AddVectoredExceptionHandler(1, DIGITOYENGINE_veh);
}
#elif !defined(__EMSCRIPTEN__)
// M9g POSIX (mac/linux/android/ios): sigaction + ucontext PC-redirect. Windows VEH ile ayni desen.
// NOT: Windows gelistirme ortaminda derlenmiyor -> mac/linux'ta HENUZ TEST EDILMEDI.
#include <signal.h>
#include <stdint.h>
#include <ucontext.h>
static void DIGITOYENGINE_sig(int sig, siginfo_t *si, void *uctx)
{
    if ((uintptr_t)si->si_addr < 4096 && DIGITOYENGINE_sp > 0)
    {
        ucontext_t *uc = (ucontext_t *)uctx;
#if defined(__APPLE__) && defined(__aarch64__)
        uc->uc_mcontext->__ss.__sp &= ~(unsigned long long)15;
        uc->uc_mcontext->__ss.__pc = (unsigned long long)(uintptr_t)DIGITOYENGINE_throw_null;
        return;
#elif defined(__APPLE__) && defined(__x86_64__)
        uc->uc_mcontext->__ss.__rsp = (uc->uc_mcontext->__ss.__rsp & ~15ULL) - 8;
        uc->uc_mcontext->__ss.__rip = (unsigned long long)(uintptr_t)DIGITOYENGINE_throw_null;
        return;
#elif defined(__linux__) && defined(__x86_64__) && defined(REG_RSP) // REG_* icin _GNU_SOURCE gerekebilir
        uc->uc_mcontext.gregs[REG_RSP] = (uc->uc_mcontext.gregs[REG_RSP] & ~15LL) - 8;
        uc->uc_mcontext.gregs[REG_RIP] = (long long)(uintptr_t)DIGITOYENGINE_throw_null;
        return;
#elif defined(__linux__) && defined(__aarch64__)
        uc->uc_mcontext.sp &= ~(unsigned long long)15;
        uc->uc_mcontext.pc = (unsigned long long)(uintptr_t)DIGITOYENGINE_throw_null;
        return;
#endif
    }
    signal(sig, SIG_DFL); // bizim degil: varsayilan isleyiciyle gercek crash
#ifdef DIGITOYENGINE_DEBUG
    // debug build: zaten cokuyoruz, stdio riski kabul -> local/adim dokumu (release'te signal-safe yol korunur)
    DIGITOYENGINE_dump_stack();
    DIGITOYENGINE_dump_locals(stderr);
    DIGITOYENGINE_dump_steps(stderr);
    fflush(stderr);
#endif
    // gercek native fault: cikmadan once crash'i benzersiz dosyaya yaz (signal-safe open/write)
    DIGITOYENGINE_crash_dump("native fault (signal)", DIGITOYENGINE_stack, DIGITOYENGINE_sp);
    raise(sig);
}
__attribute__((constructor)) static void DIGITOYENGINE_install_sig(void)
{
    struct sigaction sa;
    memset(&sa, 0, sizeof(sa));
    sa.sa_sigaction = DIGITOYENGINE_sig;
    sa.sa_flags = SA_SIGINFO;
    sigaction(SIGSEGV, &sa, 0);
    sigaction(SIGBUS, &sa, 0);
}
#endif
VmArray *vmarray_new(int len, unsigned short elemsize, int isref)
{
    return vmarray_new_rank(1, &len, elemsize, isref);
}
VmArray *vmarray_new_rank(int rank, const int *dims, unsigned short elemsize, int isref)
{
    if (rank < 1)
        DIGITOYENGINE_throw_bounds(rank, 1);
    size_t len = 1;
    for (int i = 0; i < rank; i++)
    {
        if (dims[i] < 0 || (size_t)dims[i] != 0 && len > (size_t)INT_MAX / (size_t)dims[i])
            DIGITOYENGINE_throw_bounds(dims[i], 0);
        len *= (size_t)dims[i];
    }
    return vmarray_new_rank_t(rank, dims, elemsize, isref ? &vmarray_ref_type : &vmarray_val_type);
}
// Eleman tipi struct ve icinde referans var: uretilen Type (trace eleman eleman ref yollarini shade eder,
// atomic=0). Transpiler NewArray'de bu yolu secer; ref/atomic diziler yukaridaki iki runtime tipiyle kalir.
VmArray *vmarray_new_rank_t(int rank, const int *dims, unsigned short elemsize, const Type *t)
{
    size_t len = 1;
    for (int i = 0; i < rank; i++)
        len *= (size_t)dims[i];
    VmArray *a = (VmArray *)gc_alloc(t);
    a->len = (int)len;
    a->elemsize = elemsize;
    a->rank = (unsigned short)rank;
    a->dims = (int *)malloc((size_t)rank * sizeof(int));
    memcpy(a->dims, dims, (size_t)rank * sizeof(int));
    a->data = calloc(a->len > 0 ? a->len : 1, elemsize);
    return a;
}

// ---- System.Array yardimcilari: tek memmove/memset, eleman dongusu YOK (sicak yol) ----
void vmarray_copy(VmArray *src, int srcIndex, VmArray *dst, int dstIndex, int len)
{
    DIGITOYENGINE_NULLCHECK(src);
    DIGITOYENGINE_NULLCHECK(dst);
    if (len < 0 || srcIndex < 0 || srcIndex + len > src->len)
        DIGITOYENGINE_throw_bounds(srcIndex + len, src->len);
    if (dstIndex < 0 || dstIndex + len > dst->len)
        DIGITOYENGINE_throw_bounds(dstIndex + len, dst->len);
    if (src->elemsize != dst->elemsize)
        DIGITOYENGINE_cast_fail(src->gc.type, dst->gc.type); // elemsize uyusmazligi (vmarray tip adi yok -> "?")
    memmove((char *)dst->data + (size_t)dstIndex * dst->elemsize,
            (char *)src->data + (size_t)srcIndex * src->elemsize, (size_t)len * src->elemsize);
    // tri-color barrier: MARK fazinda hedefe tasinan referanslar grilenir (ref ya da ref-tasiyan struct dizisi)
    if (!dst->gc.type->atomic && gcm_phase == GCM_MARK && dst->gc.type->trace)
        dst->gc.type->trace(&dst->gc);
}

// IN-PLACE buyume (C# Resize'dan sapma: takma-adli referanslar da yeni boyutu gorur; hiz icin).
VmArray *vmarray_resize(VmArray *a, int newLen)
{
    DIGITOYENGINE_NULLCHECK(a);
    if (newLen < 0)
        DIGITOYENGINE_throw_bounds(newLen, a->len);
    if (newLen == a->len)
        return a;
    a->data = realloc(a->data, (size_t)(newLen > 0 ? newLen : 1) * a->elemsize);
    if (newLen > a->len) // realloc buyuyen kismi SIFIRLAMAZ (tahsis calloc idi) -> elle sifirla
        memset((char *)a->data + (size_t)a->len * a->elemsize, 0, (size_t)(newLen - a->len) * a->elemsize);
    a->len = newLen;
    return a;
}

void vmarray_clear(VmArray *a, int index, int len)
{
    DIGITOYENGINE_NULLCHECK(a);
    if (len < 0 || index < 0 || index + len > a->len)
        DIGITOYENGINE_throw_bounds(index + len, a->len);
    memset((char *)a->data + (size_t)index * a->elemsize, 0, (size_t)len * a->elemsize);
}

#ifdef VM_DEBUG
void opinfo(const char *id, ...)
{
    va_list ap;
    va_start(ap, id);
    printf("[opinfo] %s:", id);
    for (;;)
    {
        int tag = va_arg(ap, int);
        if (tag == OI_END)
            break;
        const char *nm = va_arg(ap, const char *);
        if (tag == OI_DBL)
        {
            double d = va_arg(ap, double);
            printf(" %s=%g", nm, d);
        }
        else if (tag == OI_PTR)
        {
            void *p = va_arg(ap, void *);
            printf(" %s=%p", nm, p);
        }
        else
        {
            int v = va_arg(ap, int);
            printf(" %s=%d", nm, v);
        }
    }
    printf("\n");
    va_end(ap);
}
#endif

// ---- P/Invoke string koprusu (statik linkli native API'ler const char* konusur) ----
char *digitoyengine_to_utf8(const VmString *s)
{
    if (!s) return NULL;
    int n = 0;
    for (int i = 0; i < s->length; i++)
    {
        unsigned c = s->data[i];
        if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s->length) { n += 4; i++; }
        else n += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
    }
    char *out = (char *)malloc((size_t)n + 1);
    unsigned char *d = (unsigned char *)out;
    int o = 0;
    for (int i = 0; i < s->length; i++)
    {
        unsigned c = s->data[i];
        if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s->length)
        {
            unsigned cp = 0x10000 + ((c - 0xD800) << 10) + (s->data[i + 1] - 0xDC00);
            i++;
            d[o++] = (unsigned char)(0xF0 | (cp >> 18));
            d[o++] = (unsigned char)(0x80 | ((cp >> 12) & 0x3F));
            d[o++] = (unsigned char)(0x80 | ((cp >> 6) & 0x3F));
            d[o++] = (unsigned char)(0x80 | (cp & 0x3F));
        }
        else if (c < 0x80) d[o++] = (unsigned char)c;
        else if (c < 0x800) { d[o++] = (unsigned char)(0xC0 | (c >> 6)); d[o++] = (unsigned char)(0x80 | (c & 0x3F)); }
        else { d[o++] = (unsigned char)(0xE0 | (c >> 12)); d[o++] = (unsigned char)(0x80 | ((c >> 6) & 0x3F)); d[o++] = (unsigned char)(0x80 | (c & 0x3F)); }
    }
    d[o] = 0;
    return out;
}
VmString *digitoyengine_from_utf8(const char *s)
{
    if (!s) return NULL;
    int n = (int)strlen(s);
    const unsigned char *b = (const unsigned char *)s;
    int units = 0;
    for (int i = 0; i < n;)
    {
        unsigned char c = b[i];
        int len = c < 0x80 ? 1 : (c >> 5) == 6 ? 2 : (c >> 4) == 14 ? 3 : (c >> 3) == 30 ? 4 : 1;
        if (i + len > n) len = 1;
        units += len == 4 ? 2 : 1;
        i += len;
    }
    VmString *r = vmstring_alloc(units);
    unsigned short *d = (unsigned short *)r->data;
    int o = 0;
    for (int i = 0; i < n;)
    {
        unsigned char c = b[i];
        unsigned cp; int len;
        if (c < 0x80) { cp = c; len = 1; }
        else if ((c >> 5) == 6 && i + 1 < n) { cp = ((c & 0x1F) << 6) | (b[i + 1] & 0x3F); len = 2; }
        else if ((c >> 4) == 14 && i + 2 < n) { cp = ((c & 0x0F) << 12) | ((b[i + 1] & 0x3F) << 6) | (b[i + 2] & 0x3F); len = 3; }
        else if ((c >> 3) == 30 && i + 3 < n) { cp = ((c & 0x07) << 18) | ((b[i + 1] & 0x3F) << 12) | ((b[i + 2] & 0x3F) << 6) | (b[i + 3] & 0x3F); len = 4; }
        else { cp = 0xFFFD; len = 1; }
        if (cp >= 0x10000) { cp -= 0x10000; d[o++] = (unsigned short)(0xD800 + (cp >> 10)); d[o++] = (unsigned short)(0xDC00 + (cp & 0x3FF)); }
        else d[o++] = (unsigned short)cp;
        i += len;
    }
    return r;
}
