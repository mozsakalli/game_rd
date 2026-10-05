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
static void *block_alloc(size_t sz)
{
    int c = gc_class(sz);
    if (c >= GC_CLASSES)
        return malloc(sz);
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
    return s;
}
static void block_free(void *p, unsigned short sz)
{
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
static GCHeader *gc_young = 0;
static GCHeader *gc_old = 0;
static GCHeader **gc_gray = 0;
static int gc_gray_n = 0, gc_gray_cap = 0;
static GCHeader **gc_roots = 0;
static int gc_roots_n = 0, gc_roots_cap = 0;
static void **gc_frames = 0;
static FrameTrace *gc_ftraces = 0;
static int gc_frame_n = 0, gc_frame_cap = 0;
static GCHeader **gc_rem = 0;
static int gc_rem_n = 0, gc_rem_cap = 0;
static unsigned char gc_version = 1;
static int gc_minor_mode = 0;
static long long gc_heap_bytes = 0;     // canli header baytlari (string/array yan verisi haric)
static long long gc_major_baseline = 0; // son major bitiminde kalan canli boyut
enum
{
    GCM_IDLE,
    GCM_MARK,
    GCM_SWEEP_Y,
    GCM_SWEEP_O
};
static int gcm_phase = GCM_IDLE;
static GCHeader **gcm_pp = 0;
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
    o->version = gc_version;
    o->age = 0;
    o->next = gc_young;
    gc_young = o;
    return o;
}
void gc_shade(GCHeader *o)
{
    if (!o || o->age == GC_IMMORTAL || o->version == gc_version)
        return; // immortal (static const): isaretleme, izleme, sweep YOK
    if (gc_minor_mode && o->age >= GC_TENURE)
        return;
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
static void gc_remember(GCHeader *o)
{
    for (int i = 0; i < gc_rem_n; i++)
        if (gc_rem[i] == o)
            return;
    if (gc_rem_n == gc_rem_cap)
    {
        gc_rem_cap = gc_rem_cap ? gc_rem_cap * 2 : 8;
        gc_rem = (GCHeader **)realloc(gc_rem, gc_rem_cap * sizeof(GCHeader *));
    }
    gc_rem[gc_rem_n++] = o;
}
void gc_write_barrier(GCHeader *obj, GCHeader *val)
{
    if (obj && val && obj->age >= GC_TENURE && val->age < GC_TENURE)
        gc_remember(obj);
    if (gcm_phase == GCM_MARK)
        gc_shade(val);
}
static void gc_finalize_free(GCHeader *o)
{
    if (o->type->finalize)
        o->type->finalize(o);
    gc_heap_bytes -= o->type->size;
    block_free(o, o->type->size);
}
static void gc_drain(void)
{
    while (gc_gray_n > 0)
    {
        GCHeader *o = gc_gray[--gc_gray_n];
        if (o->type->trace)
            o->type->trace(o);
    }
}
static void gc_mark_roots(void)
{
    for (int i = 0; i < gc_roots_n; i++)
    {
        GCHeader *r = gc_roots[i];
        if (!r)
            continue;
        if (gc_minor_mode && r->age >= GC_TENURE)
        {
            if (r->type->trace)
                r->type->trace(r);
        }
        else
            gc_shade(r);
    }
    for (int i = 0; i < gc_frame_n; i++)
        gc_ftraces[i](gc_frames[i]);
}
static void gc_sweep_list(GCHeader **head, int promote)
{
    GCHeader **pp = head;
    while (*pp)
    {
        GCHeader *o = *pp;
        if (o->version == gc_version)
        {
            if (promote && ++o->age >= GC_TENURE)
            {
                *pp = o->next;
                o->next = gc_old;
                gc_old = o;
            }
            else
                pp = &o->next;
        }
        else
        {
            *pp = o->next;
            gc_finalize_free(o);
        }
    }
}
void gc_minor(void)
{
    if (gcm_phase != GCM_IDLE)
    {
        // Artimli major ucusta: minor'un version++/gray sifirlamasi onun isaretlerini bozar.
        // Major genc nesli de supurdugu icin once onu bitirmek hem guvenli hem yeterli.
        while (gc_major_step(1 << 20))
            ;
        return;
    }
    gc_minor_mode = 1;
    gc_version++;
    gc_gray_n = 0;
    gc_mark_roots();
    for (int i = 0; i < gc_rem_n; i++)
    {
        GCHeader *o = gc_rem[i];
        if (o->type->trace)
            o->type->trace(o);
    }
    gc_drain();
    gc_sweep_list(&gc_young, 1);
    gc_rem_n = 0;
    gc_minor_mode = 0;
}
static void gc_major_start(void)
{
    gc_minor_mode = 0;
    gc_version++;
    gc_gray_n = 0;
    gc_mark_roots();
    gcm_phase = GCM_MARK;
}
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
            gcm_phase = GCM_SWEEP_Y;
            gcm_pp = &gc_young;
        }
        return 1;
    case GCM_SWEEP_Y:
        while (budget-- > 0 && *gcm_pp)
        {
            GCHeader *o = *gcm_pp;
            if (o->version == gc_version)
            {
                if (++o->age >= GC_TENURE)
                {
                    *gcm_pp = o->next;
                    o->next = gc_old;
                    gc_old = o;
                }
                else
                    gcm_pp = &o->next;
            }
            else
            {
                *gcm_pp = o->next;
                gc_finalize_free(o);
            }
        }
        if (*gcm_pp == 0)
        {
            gcm_phase = GCM_SWEEP_O;
            gcm_pp = &gc_old;
        }
        return 1;
    case GCM_SWEEP_O:
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
            gc_rem_n = 0;
            gc_major_baseline = gc_heap_bytes;
            return 0;
        }
        return 1;
    }
    return 0;
}
// Esik politikasi (HL gc_check_mark dengi): heap, son major'dan kalan canlinin
// 1.5 kati + tabani asinca major baslar; major aktifse butceyle surdurur.
#define GC_MAJOR_MIN (256 << 10)
int gc_maybe_major(int budget)
{
    if (gcm_phase != GCM_IDLE)
        return gc_major_step(budget);
    if (gc_heap_bytes > gc_major_baseline + (gc_major_baseline >> 1) + GC_MAJOR_MIN)
        return gc_major_step(budget);
    return 0;
}
void gc_major(void)
{
    while (gc_major_step(1000000))
        ;
}
int gc_count_young(void)
{
    int n = 0;
    for (GCHeader *o = gc_young; o; o = o->next)
        n++;
    return n;
}
int gc_count_old(void)
{
    int n = 0;
    for (GCHeader *o = gc_old; o; o = o->next)
        n++;
    return n;
}
static int gc_next_id = 0; // kimlik hash sayaci (pointer daralmasi -> cakisma olmasin diye)
int gc_hashcode(GCHeader *o)
{
    if (!o)
        return 0;
    if (!o->idhash)
        o->idhash = ++gc_next_id; // saf kimlik: benzersiz, stabil (icerik hash'i corelib override'inin isi)
    return o->idhash;
}

// ---- Array ----
static void finalize_vmarray(GCHeader *h)
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
const Type vmobject_type = {0, 0, sizeof(VmObject), 1, 0, &vmobject_name, vmobject_vtable, 3, 0, 0, 1}; // System.Object koku (base=0, tindex=1)
const Type vmstring_type = {0, finalize_vmstring, sizeof(VmString), 1, &vmobject_type, &vmstring_name, vmstring_vtable, 3, 0, 0, 2};

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
const Type vmvaluetype_type = {0, 0, 0, 1, &vmobject_type, &vt_name, 0, 0, 0, 0, 3};
const Type vmenum_type = {0, 0, 0, 1, &vmvaluetype_type, &en_name, 0, 0, 0, 0, 16}; // soyut kok: box'lanmaz
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
const Type vmchar_type = {0, 0, sizeof(GCHeader) + 1, 1, &vmvaluetype_type, &ch_name, vmchar_vtable, 3, 0, 0, 12};
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
static GCHeader **digitoyengine_wrappers = 0;
static int digitoyengine_nwrappers = 0;
void digitoyengine_reflect_init(const Type *typeType, const Type *fieldInfoType, const Type *propertyInfoType, int nwrappers)
{
    digitoyengine_type_type = typeType;
    digitoyengine_fieldinfo_type = fieldInfoType;
    digitoyengine_propertyinfo_type = propertyInfoType;
    digitoyengine_nwrappers = nwrappers;
    digitoyengine_wrappers = (GCHeader **)calloc((size_t)nwrappers, sizeof(GCHeader *));
}
GCHeader *digitoyengine_type_wrapper(const Type *t)
{
    if (!t || !digitoyengine_type_type || t->tindex == 0 || t->tindex >= digitoyengine_nwrappers)
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
static GCHeader *digitoyengine_member_wrapper(DigitoyEngineMember *member)
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
GCHeader *DIGITOYENGINE_ex_singleton[8];
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
        DIGITOYENGINE_put_name(stderr, DIGITOYENGINE_ex_obj ? DIGITOYENGINE_ex_obj->type->name : 0);
    else
        fputs(DIGITOYENGINE_ex_msg, stderr);
    fputc('\n', stderr);
    for (int i = DIGITOYENGINE_ex_trace_n - 1; i >= 0; i--)
        DIGITOYENGINE_put_frame(stderr, DIGITOYENGINE_ex_trace[i].mi, DIGITOYENGINE_ex_trace[i].line);
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
    // bizim null-guard'imiz degil: gercek erisim ihlali -> cikmadan crash'i benzersiz dosyaya yaz
    if (ep->ExceptionRecord->ExceptionCode == EXCEPTION_ACCESS_VIOLATION)
        DIGITOYENGINE_crash_dump("native fault (access violation)", DIGITOYENGINE_stack, DIGITOYENGINE_sp);
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
    VmArray *a = (VmArray *)gc_alloc(isref ? &vmarray_ref_type : &vmarray_val_type);
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
    // generational barrier: tenured ref-dizisine young referans tasinabilir -> remembered set
    if (dst->gc.type == &vmarray_ref_type && dst->gc.age >= GC_TENURE && dst->gc.age != GC_IMMORTAL)
        gc_remember(&dst->gc);
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
