// Dinamik modul interpreter'i (docs/modules.md Faz C) — bkz. vmint.h. Format: ModuleWriter.cs basligi.
// Tasarim:
//  * Deger = VmVal { DeSlot v; VmT *t }: statik tip (CTranspiler'in CVal.Type'i gibi) runtime'da tasinir; boxing/
//    terfi/nullable/pointer kurallari CTranspiler.EmitFunction ile BIREBIR ayni (o referans uygulamadir).
//  * Struct degerleri frame arena'sinda blob (v.p); okumada kopyalanir (deger semantigi), yazimda memcpy.
//  * Frame'ler tek bir VM yiginda (bump) yasar; longjmp sonrasi handler VM yigin tepesini geri alir.
//  * GC yalniz safepoint'te adim atar (frame yokken) -> interpreter frame'leri kok DEGIL; yalniz modul statikleri kok.
//  * Dis cagri: host export fn + imza-sekli thunk'i. Host -> modul: uretilen trampoline -> vmint_enter_*.
#include "vmint.h"
#include <stdint.h>

// ---- OpType (Code.cs sirasi; ModuleWriter.OpCount ile dogrulanir) ----
enum
{
    OP_GetArg, OP_SetArg, OP_GetLocal, OP_SetLocal, OP_GetField, OP_SetField, OP_GetIndex, OP_SetIndex, OP_New, OP_Dup,
    OP_Return, OP_Push, OP_Pop, OP_Mul, OP_Div, OP_Add, OP_Sub, OP_Mod, OP_Neg, OP_And, OP_Or, OP_Not, OP_Xor, OP_Shl, OP_Shr,
    OP_Call, OP_Label, OP_Br, OP_Brtrue, OP_Brfalse, OP_Ceq, OP_Cne, OP_Cgt, OP_Cge, OP_Clt, OP_Cle,
    OP_AddrLocal, OP_AddrArg, OP_AddrField, OP_AddrElement, OP_ArrayDataAddr, OP_AddrStatic, OP_LoadInd, OP_StoreInd,
    OP_CallVirtual, OP_GetStatic, OP_SetStatic, OP_NewArray, OP_ArrayLength, OP_StackAlloc, OP_SizeOf, OP_Conv,
    OP_IsType, OP_IsValueType, OP_AsType, OP_CastClass, OP_GenericCast, OP_TypeOf, OP_Box, OP_Unbox, OP_UnboxOrDefault,
    OP_NullableWrap, OP_NullableHasValue, OP_NullableValue, OP_NullableBinary, OP_DelegateNew, OP_DelegateCombine,
    OP_DelegateRemove, OP_CallIndirect, OP_Default, OP_Throw, OP_TryBegin, OP_TryEnd, OP_ExIs, OP_ExBind, OP_Rethrow,
    OP_TryUnwind, OP_Yield, OP__COUNT
};
enum { TK_SCALAR, TK_ARRAY, TK_FIXED, TK_POINTER, TK_EXTERN, TK_ENUM, TK_CLASS, TK_STRUCT, TK_IFACE, TK_DELEGATE };
#define DMOD_MAGIC 0x444F4D44u
#define DMOD_VERSION 1u

typedef struct VmT VmT;
typedef struct VmMethod VmMethod;
typedef struct VmField { const char *name; VmT *type; int isStatic; int offset; void *staddr; } VmField;
typedef struct VmFieldRef { VmT *owner; const char *name; int isStatic; VmT *type; int offset; void *addr; } VmFieldRef;
typedef struct VmVSlot { VmMethod *decl, *impl; } VmVSlot;
typedef struct VmITable { VmT *iface; int n; VmMethod **imethod, **impl; } VmITable;
typedef struct VmEnumMember { const char *name; int value; } VmEnumMember;
typedef struct VmLocalType { Type type; VmT *t; } VmLocalType; // Type* <-> VmT* (Type.module != 0 ise bu yapinin ici)
struct VmT
{
    unsigned char kind, tag, preferHost, flags, isLocal, resolved, resolving, isNullable;
    const char *name, *display;
    VmT *elem; int rank, fixedSize;
    const Type *type;            // descriptor (host/yerel); yoksa 0
    const DeTypeExport *host;    // host export (alan offset/slot icin)
    VmT *parent; int nifaces; VmT **ifaces;
    int nfields; VmField *fields; // bildirilen alanlar (instance + static)
    int size, align;              // deger boyutu: class/ref -> pointer; struct -> yerlesim boyutu
    int objSize;                  // class: nesne govde boyutu (header dahil; host: export size) - tureyen yerlesimi buradan devam eder
    int nvslots; VmVSlot *vslots; int nitables; VmITable *itables;
    VmT *dret; int ndparams; VmT **dparams;
    int nenum; VmEnumMember *enums;
    VmLocalType *ltype;           // yerel descriptor
    int nrefs; int *refs;         // GC referans offset'leri (class: header dahil, yerel atalar dahil; struct: 0-tabanli)
    TraceFn hostTrace;            // en yakin host atanin trace'i (class)
    VmT *ptrType;                 // *this (cache)
    VmT *arrOf;                   // this[] (cache)
    VmT *nullValue; int nullHasOff, nullValOff; // Nullable<T>
    const Type *arrType;          // ref tasiyan struct elemanli dizi descriptor'i (lazy)
    char *statics;                // yerel statik depo
    const void **vtable; VmMethod **vimpl; int nvtable; // cozulmus vtable (fn + yerel impl)
    IfaceImpl *iimpls; int niimpls;
    struct VmModule *mod;
};
typedef struct VmArg { VmT *type; unsigned char isRef, isOut; } VmArg;
typedef struct VmOp
{
    unsigned char op, vtag; int slot, line;
    VmFieldRef *field; VmMethod *code; VmT *prim;
    union { int i; float f; double d; long long l; int str; } val;
} VmOp;
struct VmMethod
{
    unsigned char local, isStatic, isVirtual, preferHost, isCtor, marker, resolvedExtern;
    const char *name; VmT *owner; VmT *ret; int nargs; VmArg *args;
    const char *display, *file, *stubReason; int nlocals; VmT **locals; int nops; VmOp *ops;
    MethodInfo mi; int blobBytes; int *argBlobOff, *localBlobOff; int ntries; int *tryHandler;
    const DeMethodExport *hostm; const void *fn; DeThunk thunk;
    struct VmModule *mod;
};
typedef struct VmClosure { GCHeader gc; GCHeader *target; VmMethod *m; } VmClosure;
struct VmModule
{
    DeModule mod; // ILK uye (DeModule* cast)
    char *strblob; const char **strs; int nstr;
    int ntypes; VmT **types; int nfields; VmFieldRef *fields; int nmethods; VmMethod **methods; int entry;
    VmString **literals; // string havuzu -> rooted VmString (lazy)
    VmString **names;    // Type.name / MethodInfo.name icin immortal (malloc) stringler
    int nnames, capnames;
    VmLocalType closureType; VmLocalType *enumBoxTypes;
    struct VmModule *next;
    const char *name;
    GCHeader **roots; int nroots, caproots;
};

// ---- global durum ----
static VmModule *vm_modules = 0;
static VmT *vm_scalar[128];      // tag -> paylasimli skaler VmT
static VmT *vm_void, *vm_object, *vm_string, *vm_voidptr;
static char *vm_base = 0, *vm_top = 0, *vm_end = 0;
#define VM_STACK_BYTES (16 << 20)
static int vm_shape_built = 0;
typedef struct VmShapeIx { const char *key; int id; } VmShapeIx;
static VmShapeIx *vm_shapes = 0; static unsigned vm_shape_mask = 0;
// fn ptr -> yerel metot (vtable/delegate marker'lari)
static void **vm_mk_keys = 0; static VmMethod **vm_mk_vals = 0; static unsigned vm_mk_mask = 0, vm_mk_n = 0;

static void vm_fail(const char *msg) { fprintf(stderr, "[vmint] OLUMCUL: %s\n", msg); DIGITOYENGINE_throw_io(msg); }
static void *vm_zalloc(size_t n) { void *p = calloc(1, n ? n : 1); if (!p) vm_fail("bellek"); return p; }
static unsigned long long vm_hash(const char *s) { return de_hash64(s); }

static void vm_marker_put(void *key, VmMethod *m)
{
    if ((vm_mk_n + 1) * 2 > vm_mk_mask + 1)
    {
        unsigned ncap = vm_mk_mask ? (vm_mk_mask + 1) * 2 : 1024;
        void **nk = (void **)vm_zalloc(ncap * sizeof(void *)); VmMethod **nv = (VmMethod **)vm_zalloc(ncap * sizeof(VmMethod *));
        for (unsigned i = 0; i <= vm_mk_mask && vm_mk_keys; i++)
            if (vm_mk_keys[i]) { unsigned k = (unsigned)(((uintptr_t)vm_mk_keys[i]) >> 3) & (ncap - 1); while (nk[k]) k = (k + 1) & (ncap - 1); nk[k] = vm_mk_keys[i]; nv[k] = vm_mk_vals[i]; }
        free(vm_mk_keys); free(vm_mk_vals); vm_mk_keys = nk; vm_mk_vals = nv; vm_mk_mask = ncap - 1;
    }
    unsigned k = (unsigned)(((uintptr_t)key) >> 3) & vm_mk_mask;
    while (vm_mk_keys[k] && vm_mk_keys[k] != key) k = (k + 1) & vm_mk_mask;
    if (!vm_mk_keys[k]) vm_mk_n++;
    vm_mk_keys[k] = key; vm_mk_vals[k] = m;
}
static VmMethod *vm_marker_get(const void *key)
{
    if (!vm_mk_keys) return 0;
    unsigned k = (unsigned)(((uintptr_t)key) >> 3) & vm_mk_mask;
    while (vm_mk_keys[k]) { if (vm_mk_keys[k] == key) return vm_mk_vals[k]; k = (k + 1) & vm_mk_mask; }
    return 0;
}

// ---- host sekil indeksi: imza metni -> thunk id (CTranspiler.ShapeKey ile ayni metin) ----
static void vm_shapes_build(void)
{
    unsigned cap = 64; while (cap < (unsigned)de_host_nthunks * 2) cap <<= 1;
    vm_shapes = (VmShapeIx *)vm_zalloc(cap * sizeof(VmShapeIx)); vm_shape_mask = cap - 1;
    for (int i = 0; i < de_host_nthunks; i++)
    {
        unsigned k = (unsigned)vm_hash(de_host_shapes[i]) & vm_shape_mask;
        while (vm_shapes[k].key) k = (k + 1) & vm_shape_mask;
        vm_shapes[k].key = de_host_shapes[i]; vm_shapes[k].id = i;
    }
    vm_shape_built = 1;
}
static int vm_shape_find(const char *key)
{
    if (!vm_shape_built) vm_shapes_build();
    unsigned k = (unsigned)vm_hash(key) & vm_shape_mask;
    while (vm_shapes[k].key) { if (!strcmp(vm_shapes[k].key, key)) return vm_shapes[k].id; k = (k + 1) & vm_shape_mask; }
    return -1;
}
// C tip adi (CTranspiler.CType/ShapeCType): skaler cil_*, pointer/ref void*, struct "struct <CName>"
static void vm_cname(char *out, int cap, const char *name)
{
    int i = 0; for (; name[i] && i < cap - 1; i++) { char c = name[i]; out[i] = ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') ? c : '_'; }
    out[i] = 0;
}
static const char *vm_scalar_ctype(unsigned char tag)
{
    switch (tag)
    {
    case 'i': return "cil_int"; case 'u': return "cil_uint"; case 'l': return "cil_long"; case 'q': return "cil_ulong";
    case 'h': return "cil_short"; case 'H': return "cil_ushort"; case 'b': return "cil_byte"; case 'z': return "cil_sbyte";
    case 'c': return "cil_char"; case 'B': return "cil_bool"; case 'f': return "cil_float"; case 'd': return "cil_double";
    case 'V': return "void"; default: return "void*";
    }
}
static void vm_ctype(char *out, int cap, VmT *t, int byRef)
{
    if (byRef || t->tag == 'o' || t->tag == 'p') { snprintf(out, cap, "void*"); return; }
    if (t->tag == 'v') { char cn[512]; vm_cname(cn, sizeof cn, t->name); snprintf(out, cap, "struct %s", cn); return; }
    snprintf(out, cap, "%s", vm_scalar_ctype(t->tag));
}
static int vm_shape_of(VmT *ret, int nargs, VmArg *args, int closureFirst)
{
    char key[4096], part[600]; int n = 0;
    vm_ctype(part, sizeof part, ret, 0); n += snprintf(key + n, sizeof key - n, "%s(", part);
    int total = nargs + (closureFirst ? 1 : 0);
    if (total == 0) n += snprintf(key + n, sizeof key - n, "void");
    for (int i = 0; i < total; i++)
    {
        if (i) n += snprintf(key + n, sizeof key - n, ",");
        if (closureFirst && i == 0) n += snprintf(key + n, sizeof key - n, "void*");
        else { VmArg *a = &args[i - (closureFirst ? 1 : 0)]; vm_ctype(part, sizeof part, a->type, a->isRef || a->isOut); n += snprintf(key + n, sizeof key - n, "%s", part); }
        if (n >= (int)sizeof key - 8) break;
    }
    snprintf(key + n, sizeof key - n, ")");
    return vm_shape_find(key);
}

// ---- okuyucu ----
typedef struct Rd { const unsigned char *p, *end; int err; } Rd;
static unsigned rd_u32(Rd *r) { if (r->p + 4 > r->end) { r->err = 1; return 0; } unsigned v = r->p[0] | (r->p[1] << 8) | (r->p[2] << 16) | ((unsigned)r->p[3] << 24); r->p += 4; return v; }
static int rd_i32(Rd *r) { return (int)rd_u32(r); }
static unsigned char rd_u8(Rd *r) { if (r->p >= r->end) { r->err = 1; return 0; } return *r->p++; }
static unsigned short rd_u16(Rd *r) { unsigned v = rd_u8(r); return (unsigned short)(v | (rd_u8(r) << 8)); }
static long long rd_i64(Rd *r) { unsigned lo = rd_u32(r); unsigned hi = rd_u32(r); return (long long)(((unsigned long long)hi << 32) | lo); }
static float rd_f32(Rd *r) { unsigned v = rd_u32(r); float f; memcpy(&f, &v, 4); return f; }
static double rd_f64(Rd *r) { long long v = rd_i64(r); double d; memcpy(&d, &v, 8); return d; }

// ---- immortal/rooted stringler ----
static VmString *vm_immortal_str(VmModule *m, const char *utf8)
{
    VmString *s = digitoyengine_from_utf8(utf8); // GC'li; immortal yap + listeden cikarmiyoruz (sweep age'e bakmaz) -> kokle
    if (!s) return 0;
    if (m->nnames == m->capnames) { m->capnames = m->capnames ? m->capnames * 2 : 64; m->names = (VmString **)realloc(m->names, m->capnames * sizeof(VmString *)); }
    m->names[m->nnames++] = s;
    gc_add_root((GCHeader *)s);
    return s;
}
static void vm_root(VmModule *m, GCHeader *o)
{
    if (m->nroots == m->caproots) { m->caproots = m->caproots ? m->caproots * 2 : 64; m->roots = (GCHeader **)realloc(m->roots, m->caproots * sizeof(GCHeader *)); }
    m->roots[m->nroots++] = o; gc_add_root(o);
}
static VmString *vm_literal(VmModule *m, int idx)
{
    if (idx < 0) return 0;
    if (!m->literals[idx]) { m->literals[idx] = digitoyengine_from_utf8(m->strs[idx]); vm_root(m, (GCHeader *)m->literals[idx]); }
    return m->literals[idx];
}

// ---- tip yardimcilari ----
static int vm_tag_size(unsigned char tag)
{
    switch (tag) { case 'b': case 'z': return 1; case 'h': case 'H': case 'c': return 2; case 'i': case 'u': case 'B': case 'f': return 4; case 'l': case 'q': case 'd': return 8; case 'V': return 0; default: return (int)sizeof(void *); }
}
static VmT *vm_scalar_t(unsigned char tag)
{
    if (!vm_scalar[tag])
    {
        VmT *t = (VmT *)vm_zalloc(sizeof(VmT)); t->kind = TK_SCALAR; t->tag = tag; t->resolved = 1; t->size = t->align = vm_tag_size(tag);
        switch (tag)
        {
        case 'i': t->name = "Int"; t->type = &vmint32_type; break; case 'u': t->name = "UInt"; t->type = &vmuint32_type; break;
        case 'l': t->name = "Long"; t->type = &vmint64_type; break; case 'q': t->name = "ULong"; t->type = &vmuint64_type; break;
        case 'h': t->name = "Short"; t->type = &vmint16_type; break; case 'H': t->name = "UShort"; t->type = &vmuint16_type; break;
        case 'b': t->name = "Byte"; t->type = &vmbyte_type; break; case 'z': t->name = "SByte"; t->type = &vmsbyte_type; break;
        case 'c': t->name = "Char"; t->type = &vmchar_type; break; case 'B': t->name = "Bool"; t->type = &vmbool_type; break;
        case 'f': t->name = "Float"; t->type = &vmsingle_type; break; case 'd': t->name = "Double"; t->type = &vmdouble_type; break;
        default: t->name = "Void"; break;
        }
        t->display = t->name;
        vm_scalar[tag] = t;
    }
    return vm_scalar[tag];
}
static VmT *vm_ptr_of(VmT *e)
{
    if (!e->ptrType) { VmT *t = (VmT *)vm_zalloc(sizeof(VmT)); t->kind = TK_POINTER; t->tag = 'p'; t->elem = e; t->resolved = 1; t->size = t->align = sizeof(void *); t->name = t->display = "*"; e->ptrType = t; }
    return e->ptrType;
}
// eleman tipinin dizi VmT'si: once modul tablosunda ara (ModuleWriter yazdiysa), yoksa uret ve cache'le (rank 1)
static VmT *vm_array_of(VmModule *m, VmT *e, int rank)
{
    if (rank == 1 && e->arrOf) return e->arrOf;
    for (int i = 0; i < m->ntypes; i++) if (m->types[i]->kind == TK_ARRAY && m->types[i]->elem == e && m->types[i]->rank == rank) { if (rank == 1) e->arrOf = m->types[i]; return m->types[i]; }
    VmT *t = (VmT *)vm_zalloc(sizeof(VmT)); t->kind = TK_ARRAY; t->tag = 'o'; t->elem = e; t->rank = rank; t->resolved = 1; t->size = t->align = sizeof(void *); t->name = t->display = "[]"; t->type = &vmarray_ref_type; t->mod = m;
    if (rank == 1) e->arrOf = t;
    return t;
}
static int vm_is_numeric(VmT *t) { return t && t->tag && strchr("iulqhHbzcBfd", t->tag) != 0; }
static int vm_is_ref(VmT *t) { return t && t->tag == 'o'; }
static int vm_is_struct(VmT *t) { return t && t->tag == 'v'; }
static int vm_is_ptr(VmT *t) { return t && (t->kind == TK_POINTER || t->kind == TK_FIXED); }
// deger tipi mi (IsNumericVal || struct) - enum Int tabanli skaler sayilir
static int vm_is_value(VmT *t) { return vm_is_numeric(t) || vm_is_struct(t); }
static VmField *vm_find_field(VmT *t, const char *name)
{
    for (VmT *x = t; x; x = x->parent) for (int i = 0; i < x->nfields; i++) if (!strcmp(x->fields[i].name, name)) return &x->fields[i];
    return 0;
}
static const DeFieldExport *vm_host_field(const DeTypeExport *te, const char *ownerName, const char *fname, const DeTypeExport **ownerOut)
{
    char key[1024];
    for (const DeTypeExport *e = te; e;)
    {
        snprintf(key, sizeof key, "%s$%s", e->name, fname);
        unsigned long long h = vm_hash(key);
        for (int i = 0; i < e->nfields; i++) if (e->fields[i].hash == h) { if (ownerOut) *ownerOut = e; return &e->fields[i]; }
        e = e->type && e->type->base ? de_host_type_of(e->type->base) : 0;
    }
    (void)ownerName;
    return 0;
}
static const DeSlotExport *vm_host_slot(const DeTypeExport *te, unsigned long long methodHash)
{
    for (const DeTypeExport *e = te; e;)
    {
        for (int i = 0; i < e->nvslots; i++) if (e->vslots[i].hash == methodHash) return &e->vslots[i];
        e = e->type && e->type->base ? de_host_type_of(e->type->base) : 0;
    }
    return 0;
}

// ---- yukleme: hata toplama ----
typedef struct LoadCtx { VmModule *m; char *err; int errcap; int failed; } LoadCtx;
static void ld_err(LoadCtx *L, const char *fmt, const char *a, const char *b)
{
    if (L->failed) return;
    L->failed = 1;
    if (L->err && L->errcap > 0) snprintf(L->err, L->errcap, fmt, a ? a : "", b ? b : "");
    fprintf(stderr, "[vmint] yukleme hatasi: "); fprintf(stderr, fmt, a ? a : "", b ? b : ""); fprintf(stderr, "\n");
}

static void vm_resolve_type(LoadCtx *L, VmT *t);
static int vm_align_up(int x, int a) { return a > 1 ? (x + a - 1) / a * a : x; }
// alan tipinin bellek boyutu/hizasi (yerlesim icin); struct -> cozulmus boyut
static void vm_field_size(LoadCtx *L, VmT *t, int *size, int *align)
{
    if (t->kind == TK_STRUCT || (t->kind == TK_EXTERN && (t->flags & 1))) { vm_resolve_type(L, t); *size = t->size; *align = t->align ? t->align : 8; return; }
    if (t->kind == TK_FIXED) { int es, ea; vm_field_size(L, t->elem, &es, &ea); *size = es * t->fixedSize; *align = ea; return; }
    *size = *align = vm_tag_size(t->tag);
    if (*size == 0) *size = *align = 1;
}
// struct/class referans offset'lerini toplar (nested struct dahil)
static void vm_collect_refs(LoadCtx *L, VmT *t, int base, int **refs, int *n, int *cap)
{
    for (int i = 0; i < t->nfields; i++)
    {
        VmField *f = &t->fields[i]; if (f->isStatic) continue;
        if (f->type->tag == 'o') { if (*n == *cap) { *cap = *cap ? *cap * 2 : 8; *refs = (int *)realloc(*refs, *cap * sizeof(int)); } (*refs)[(*n)++] = base + f->offset; }
        else if (f->type->tag == 'v') { vm_resolve_type(L, f->type); for (int k = 0; k < f->type->nrefs; k++) { if (*n == *cap) { *cap = *cap ? *cap * 2 : 8; *refs = (int *)realloc(*refs, *cap * sizeof(int)); } (*refs)[(*n)++] = base + f->offset + f->type->refs[k]; } }
    }
}
// host struct export'undan referans offset'leri (tag 'o' alanlar + ic struct'lar)
static void vm_host_struct_refs(const DeTypeExport *e, int base, int **refs, int *n, int *cap)
{
    if (!e) return;
    for (int i = 0; i < e->nfields; i++)
    {
        const DeFieldExport *f = &e->fields[i]; if (f->isStatic) continue;
        if (f->tag == 'o') { if (*n == *cap) { *cap = *cap ? *cap * 2 : 8; *refs = (int *)realloc(*refs, *cap * sizeof(int)); } (*refs)[(*n)++] = base + f->offset; }
        else if (f->tag == 'v' && f->type) vm_host_struct_refs(de_host_type_of(f->type), base + f->offset, refs, n, cap);
    }
}
static void vm_trace_local(GCHeader *o);
static void vm_finalize_local(GCHeader *o);
static void vm_trace_closure(GCHeader *o) { gc_shade(((VmClosure *)o)->target); }
static void vm_trace_struct_array(GCHeader *o);
VmString *vmint_enumstr(VmObject *s);

// tipi coz: host'a bagla (extern / preferHost) ya da yerel yerlesim + descriptor
static void vm_resolve_type(LoadCtx *L, VmT *t)
{
    if (t->resolved || L->failed) return;
    if (t->resolving) { ld_err(L, "dongusel struct yerlesimi: %s", t->name, 0); return; }
    t->resolving = 1;
    switch (t->kind)
    {
    case TK_SCALAR: break;
    case TK_POINTER: case TK_FIXED: vm_resolve_type(L, t->elem); t->size = t->align = sizeof(void *); break;
    case TK_ARRAY: vm_resolve_type(L, t->elem); t->size = t->align = sizeof(void *); t->type = &vmarray_ref_type; break;
    case TK_EXTERN:
    {
        const DeTypeExport *e = de_host_find_type(vm_hash(t->name));
        if (!e) { ld_err(L, "host'ta tip yok: %s", t->name, 0); break; }
        t->host = e; t->type = e->type;
        if (t->flags & 8) { t->tag = 'i'; t->size = t->align = 4; }
        else if (t->flags & 1) { t->size = e->size; t->align = 8; int n = 0, cap = 0; int *refs = 0; vm_host_struct_refs(e, 0, &refs, &n, &cap); t->refs = refs; t->nrefs = n; }
        else { t->size = t->align = sizeof(void *); t->objSize = e->size ? e->size : (int)sizeof(GCHeader); }
        break;
    }
    case TK_ENUM:
    {
        const DeTypeExport *e = t->preferHost ? de_host_find_type(vm_hash(t->name)) : 0;
        if (e) { t->host = e; t->type = e->type; break; }
        VmLocalType *lt = (VmLocalType *)vm_zalloc(sizeof(VmLocalType)); lt->t = t; t->ltype = lt;
        static const void *enumvt[3];
        enumvt[0] = (const void *)&digitoyengine_enumbox_hash; enumvt[1] = (const void *)&digitoyengine_enumbox_eq; enumvt[2] = (const void *)&vmint_enumstr;
        lt->type.size = (unsigned short)(sizeof(GCHeader) + 4); lt->type.atomic = 1; lt->type.base = &vmenum_type;
        lt->type.name = vm_immortal_str(L->m, t->display); lt->type.vtable = enumvt; lt->type.nvtable = 3; lt->type.alias = &vmint32_type;
        lt->type.module = &L->m->mod; t->type = &lt->type; t->isLocal = 1;
        break;
    }
    case TK_CLASS: case TK_STRUCT: case TK_IFACE: case TK_DELEGATE:
    {
        const DeTypeExport *e = t->preferHost ? de_host_find_type(vm_hash(t->name)) : 0;
        if (e) { t->host = e; t->type = e->type; t->kind = TK_EXTERN; t->flags = (unsigned char)((e->isStruct ? 1 : 0) | (e->isInterface ? 2 : 0) | (e->isDelegate ? 4 : 0)); t->tag = e->isStruct ? 'v' : 'o'; t->resolving = 0; t->resolved = 0; vm_resolve_type(L, t); return; }
        t->isLocal = 1;
        if (t->parent) vm_resolve_type(L, t->parent);
        for (int i = 0; i < t->nifaces; i++) vm_resolve_type(L, t->ifaces[i]);
        // yerlesim: class -> parent boyutundan devam; struct -> 0'dan
        int off = t->kind == TK_CLASS ? (t->parent ? t->parent->objSize : (int)sizeof(GCHeader)) : 0, maxAlign = t->kind == TK_CLASS ? 8 : 1;
        for (int i = 0; i < t->nfields; i++)
        {
            VmField *f = &t->fields[i]; int sz, al; vm_field_size(L, f->type, &sz, &al);
            if (f->isStatic) continue;
            off = vm_align_up(off, al); f->offset = off; off += sz; if (al > maxAlign) maxAlign = al;
        }
        t->align = maxAlign;
        if (t->kind == TK_CLASS || t->kind == TK_IFACE || t->kind == TK_DELEGATE) { t->objSize = vm_align_up(off, maxAlign); t->size = sizeof(void *); t->align = 8; }
        else { t->size = vm_align_up(off, maxAlign); if (t->size == 0) t->size = 1; }
        if (t->kind == TK_DELEGATE) t->objSize = (int)sizeof(VmDelegate);
        // referans offset'leri: yerel atalar + kendi; en yakin host ata trace'i
        int n = 0, cap = 0; int *refs = 0;
        if (t->kind == TK_CLASS) { for (VmT *a = t->parent; a; a = a->parent) { if (!a->isLocal) { t->hostTrace = a->type ? a->type->trace : 0; break; } } for (VmT *a = t->parent; a && a->isLocal; a = a->parent) vm_collect_refs(L, a, 0, &refs, &n, &cap); }
        vm_collect_refs(L, t, 0, &refs, &n, &cap);
        t->refs = refs; t->nrefs = n;
        // Nullable<T>: "System.Nullable`1<" + value alani
        if (t->kind == TK_STRUCT && !strncmp(t->name, "System.Nullable`1<", 18)) { VmField *v = vm_find_field(t, "value"), *h = vm_find_field(t, "hasValue"); if (v && h) { t->isNullable = 1; t->nullValue = v->type; t->nullValOff = v->offset; t->nullHasOff = h->offset; } }
        // descriptor
        VmLocalType *lt = (VmLocalType *)vm_zalloc(sizeof(VmLocalType)); lt->t = t; t->ltype = lt; t->type = &lt->type;
        lt->type.name = vm_immortal_str(L->m, t->display); lt->type.module = &L->m->mod;
        if (t->kind == TK_IFACE) { lt->type.atomic = 1; }
        else if (t->kind == TK_DELEGATE) { lt->type.trace = digitoyengine_delegate_trace; lt->type.size = sizeof(VmDelegate); lt->type.base = &vmmulticastdelegate_type; }
        else if (t->kind == TK_STRUCT) { lt->type.size = (unsigned short)(sizeof(GCHeader) + t->size); lt->type.atomic = n == 0; lt->type.trace = n ? vm_trace_local : 0; lt->type.base = &vmvaluetype_type; }
        else { lt->type.size = (unsigned short)t->objSize; lt->type.atomic = (n == 0 && !t->hostTrace); lt->type.trace = (n || t->hostTrace) ? vm_trace_local : 0; lt->type.base = t->parent ? t->parent->type : &vmobject_type; }
        break;
    }
    }
    t->resolving = 0; t->resolved = 1;
}
// host struct icin de ayni (hostTrace yok): struct kutusunun/dizisinin trace'i refs'e bakar
static void vm_trace_local(GCHeader *o)
{
    VmLocalType *lt = (VmLocalType *)o->type; VmT *t = lt->t;
    char *base = (char *)o + (t->kind == TK_STRUCT ? sizeof(GCHeader) : 0);
    if (t->hostTrace) t->hostTrace(o);
    for (int i = 0; i < t->nrefs; i++) gc_shade(*(GCHeader **)(base + t->refs[i]));
}
// ref tasiyan struct elemanli dizi: eleman eleman refs
typedef struct VmArrT { Type type; VmT *elem; } VmArrT;
static void vm_trace_struct_array(GCHeader *o)
{
    VmArray *a = (VmArray *)o; VmArrT *at = (VmArrT *)a->gc.type; VmT *e = at->elem;
    for (int i = 0; i < a->len; i++) { char *base = (char *)a->data + (size_t)i * a->elemsize; for (int k = 0; k < e->nrefs; k++) gc_shade(*(GCHeader **)(base + e->refs[k])); }
}
static const Type *vm_struct_array_type(VmT *elem)
{
    if (!elem->arrType) { VmArrT *at = (VmArrT *)vm_zalloc(sizeof(VmArrT)); at->elem = elem; at->type.trace = vm_trace_struct_array; at->type.finalize = finalize_vmarray; at->type.size = sizeof(VmArray); elem->arrType = &at->type; }
    return elem->arrType;
}
VmString *vmint_enumstr(VmObject *s)
{
    VmLocalType *lt = (VmLocalType *)s->gc.type; int v = *(int *)((char *)s + sizeof(GCHeader));
    for (int i = 0; i < lt->t->nenum; i++) if (lt->t->enums[i].value == v) return vm_immortal_str(lt->t->mod, lt->t->enums[i].name);
    return digitoyengine_int_str(v);
}
static void vm_finalize_local(GCHeader *o) { (void)o; }

// ---- metot cozumu ----
static void vm_resolve_method(LoadCtx *L, VmMethod *m)
{
    if (L->failed) return;
    if (m->local && m->preferHost)
    {
        const DeMethodExport *e = de_host_find_method(vm_hash(m->name));
        if (e) { m->local = 0; m->hostm = e; m->fn = e->fn; m->thunk = de_host_thunks[e->shape]; m->resolvedExtern = 1; return; }
    }
    if (!m->local)
    {
        const DeMethodExport *e = de_host_find_method(vm_hash(m->name));
        if (!e)
        {
            // iface metotlari govdesizdir (host export'ta yok): yalniz slot/sekil icin bildirim; thunk sekilden
            int ifaceOwner = m->owner && (m->owner->kind == TK_IFACE || (m->owner->kind == TK_EXTERN && (m->owner->flags & 2)));
            if (!ifaceOwner) { ld_err(L, "host'ta metot yok: %s", m->name, 0); return; }
            int sh = vm_shape_of(m->ret, m->nargs, m->args, 0);
            m->thunk = sh >= 0 ? de_host_thunks[sh] : 0; m->resolvedExtern = 1; return;
        }
        m->hostm = e; m->fn = e->fn; m->thunk = de_host_thunks[e->shape]; m->resolvedExtern = 1;
        if (!m->fn) ld_err(L, "host'ta extern uygulanmamis (zayif sembol 0): %s", m->name, 0);
        return;
    }
    // yerel: frame blob yerlesimi (struct arg/local depolari)
    int off = 0;
    m->argBlobOff = (int *)vm_zalloc(sizeof(int) * (m->nargs ? m->nargs : 1));
    m->localBlobOff = (int *)vm_zalloc(sizeof(int) * (m->nlocals ? m->nlocals : 1));
    for (int i = 0; i < m->nargs; i++) { m->argBlobOff[i] = -1; VmArg *a = &m->args[i]; if (!a->isRef && !a->isOut && vm_is_struct(a->type)) { vm_resolve_type(L, a->type); off = vm_align_up(off, 8); m->argBlobOff[i] = off; off += a->type->size; } }
    for (int i = 0; i < m->nlocals; i++) { m->localBlobOff[i] = -1; VmT *t = m->locals[i]; if (vm_is_struct(t)) { vm_resolve_type(L, t); off = vm_align_up(off, 8); m->localBlobOff[i] = off; off += t->size; } }
    m->blobBytes = vm_align_up(off, 8);
    m->mi.name = vm_immortal_str(L->m, m->display ? m->display : m->name);
    m->mi.file = m->file ? vm_immortal_str(L->m, m->file) : 0;
    m->mi.trypc_off = -1;
    vm_marker_put(&m->marker, m);
    // op duzeyi: TryBegin'lere sabit try indeksi (vtag; CTranspiler tryIndexOf), CallVirtual/DelegateNew slotlarini host'a remap
    int tries = 0;
    for (int i = 0; i < m->nops; i++) if (m->ops[i].op == OP_TryBegin) tries++;
    m->ntries = tries; m->tryHandler = (int *)vm_zalloc(sizeof(int) * (tries + 1)); tries = 0;
    for (int i = 0; i < m->nops; i++)
    {
        VmOp *o = &m->ops[i];
        if (o->op == OP_TryBegin) { if (tries >= 255) { ld_err(L, "metotta 255'ten fazla try: %s", m->name, 0); return; } m->tryHandler[tries] = o->slot; o->vtag = (unsigned char)tries++; }
        if ((o->op == OP_CallVirtual || (o->op == OP_DelegateNew && o->slot >= 0)) && o->code && o->code->owner && !o->code->owner->isLocal)
        {
            vm_resolve_type(L, o->code->owner);
            const DeSlotExport *hs = o->code->owner->host ? vm_host_slot(o->code->owner->host, vm_hash(o->code->name)) : 0;
            if (!hs) { ld_err(L, "host sanal slotu yok: %s", o->code->name, 0); return; }
            o->slot = hs->slot;
        }
    }
}
// alan referansi coz (op.Field): yerel owner -> VmField; host owner -> export
static void vm_resolve_fieldref(LoadCtx *L, VmFieldRef *fr)
{
    if (L->failed) return;
    vm_resolve_type(L, fr->owner);
    if (fr->owner->isLocal)
    {
        VmField *f = vm_find_field(fr->owner, fr->name);
        if (!f) { ld_err(L, "alan yok: %s.%s", fr->owner->name, fr->name); return; }
        fr->type = f->type; fr->offset = f->offset; fr->addr = f->staddr; fr->isStatic = f->isStatic;
        if (fr->isStatic && !fr->addr) { ld_err(L, "statik depo yok: %s.%s", fr->owner->name, fr->name); }
        return;
    }
    const DeTypeExport *ownerE = 0;
    const DeFieldExport *e = vm_host_field(fr->owner->host, fr->owner->name, fr->name, &ownerE);
    if (!e)
    {
        if (fr->isStatic) { char key[1024]; snprintf(key, sizeof key, "%s$%s", fr->owner->name, fr->name); const DeFieldExport *s = de_host_find_static(vm_hash(key)); if (s) { fr->addr = s->addr; fr->type = fr->type; return; } }
        ld_err(L, "host'ta alan yok: %s.%s", fr->owner->name, fr->name); return;
    }
    fr->offset = e->offset; fr->addr = e->addr; fr->isStatic = e->isStatic;
    if (fr->isStatic && !fr->addr) { char key[1024]; snprintf(key, sizeof key, "%s$%s", ownerE->name, fr->name); const DeFieldExport *s = de_host_find_static(vm_hash(key)); if (!s) { ld_err(L, "host'ta statik yok: %s.%s", fr->owner->name, fr->name); return; } fr->addr = s->addr; }
}

// ================= PARSE =================
static VmT *vm_type_at(VmModule *m, int i) { return i < 0 ? 0 : m->types[i]; }
static VmMethod *vm_method_at(VmModule *m, int i) { return i < 0 ? 0 : m->methods[i]; }
static const char *vm_str_at(VmModule *m, int i) { return i < 0 ? 0 : m->strs[i]; }

static int vm_parse(LoadCtx *L, Rd *r)
{
    VmModule *m = L->m;
    if (rd_u32(r) != DMOD_MAGIC) { ld_err(L, "dmod magic uyusmuyor", 0, 0); return 0; }
    unsigned ver = rd_u32(r);
    if (ver != DMOD_VERSION) { ld_err(L, "dmod surumu desteklenmiyor", 0, 0); return 0; }
    m->nstr = rd_i32(r); m->ntypes = rd_i32(r); m->nfields = rd_i32(r); m->nmethods = rd_i32(r); m->entry = rd_i32(r);
    if (r->err || m->nstr < 0 || m->ntypes < 0) { ld_err(L, "dmod basligi bozuk", 0, 0); return 0; }
    m->strs = (const char **)vm_zalloc(sizeof(char *) * (m->nstr + 1));
    m->literals = (VmString **)vm_zalloc(sizeof(VmString *) * (m->nstr + 1));
    // string havuzu: dosyadaki UTF-8 baytlarini tek blob'a kopyala (NUL sonlu)
    {
        size_t total = 0; const unsigned char *p = r->p;
        for (int i = 0; i < m->nstr; i++) { Rd t = { p, r->end, 0 }; int n = rd_i32(&t); if (t.err || n < 0 || t.p + n + 1 > r->end) { ld_err(L, "string havuzu bozuk", 0, 0); return 0; } total += n + 1; p = t.p + n + 1; }
        m->strblob = (char *)vm_zalloc(total + 1); char *w = m->strblob;
        for (int i = 0; i < m->nstr; i++) { int n = rd_i32(r); memcpy(w, r->p, n); w[n] = 0; m->strs[i] = w; w += n + 1; r->p += n + 1; }
    }
    m->types = (VmT **)vm_zalloc(sizeof(VmT *) * (m->ntypes + 1));
    for (int i = 0; i < m->ntypes; i++) { m->types[i] = (VmT *)vm_zalloc(sizeof(VmT)); m->types[i]->mod = m; }
    m->methods = (VmMethod **)vm_zalloc(sizeof(VmMethod *) * (m->nmethods + 1));
    for (int i = 0; i < m->nmethods; i++) { m->methods[i] = (VmMethod *)vm_zalloc(sizeof(VmMethod)); m->methods[i]->mod = m; }
    m->fields = (VmFieldRef *)vm_zalloc(sizeof(VmFieldRef) * (m->nfields + 1));
    for (int i = 0; i < m->ntypes; i++)
    {
        VmT *t = m->types[i];
        t->kind = rd_u8(r); t->name = vm_str_at(m, rd_i32(r)); t->display = vm_str_at(m, rd_i32(r));
        if (!t->display) t->display = t->name;
        switch (t->kind)
        {
        case TK_SCALAR: { unsigned char tag = rd_u8(r); VmT *s = vm_scalar_t(tag); *t = *s; t->mod = m; m->types[i] = s; break; } // paylasimli
        case TK_ARRAY: t->elem = vm_type_at(m, rd_i32(r)); t->rank = rd_u8(r); t->tag = 'o'; break;
        case TK_FIXED: t->elem = vm_type_at(m, rd_i32(r)); t->fixedSize = rd_i32(r); t->tag = 'p'; break;
        case TK_POINTER: t->elem = vm_type_at(m, rd_i32(r)); t->tag = 'p'; break;
        case TK_EXTERN:
            t->flags = rd_u8(r); t->tag = (t->flags & 1) ? 'v' : (t->flags & 8) ? 'i' : 'o';
            if (t->flags & 4) { t->dret = vm_type_at(m, rd_i32(r)); t->ndparams = rd_i32(r); t->dparams = (VmT **)vm_zalloc(sizeof(VmT *) * (t->ndparams + 1)); for (int k = 0; k < t->ndparams; k++) t->dparams[k] = vm_type_at(m, rd_i32(r)); }
            break;
        case TK_ENUM: t->tag = 'i'; t->size = t->align = 4; t->nenum = rd_i32(r); t->enums = (VmEnumMember *)vm_zalloc(sizeof(VmEnumMember) * (t->nenum + 1)); for (int k = 0; k < t->nenum; k++) { t->enums[k].name = vm_str_at(m, rd_i32(r)); t->enums[k].value = rd_i32(r); } break;
        default:
        {
            t->tag = t->kind == TK_STRUCT ? 'v' : 'o';
            t->preferHost = rd_u8(r); t->parent = vm_type_at(m, rd_i32(r));
            t->nifaces = rd_i32(r); t->ifaces = (VmT **)vm_zalloc(sizeof(VmT *) * (t->nifaces + 1)); for (int k = 0; k < t->nifaces; k++) t->ifaces[k] = vm_type_at(m, rd_i32(r));
            t->nfields = rd_i32(r); t->fields = (VmField *)vm_zalloc(sizeof(VmField) * (t->nfields + 1));
            for (int k = 0; k < t->nfields; k++) { t->fields[k].name = vm_str_at(m, rd_i32(r)); t->fields[k].type = vm_type_at(m, rd_i32(r)); t->fields[k].isStatic = rd_u8(r); }
            t->nvslots = rd_i32(r); t->vslots = (VmVSlot *)vm_zalloc(sizeof(VmVSlot) * (t->nvslots + 1));
            for (int k = 0; k < t->nvslots; k++) { t->vslots[k].decl = vm_method_at(m, rd_i32(r)); t->vslots[k].impl = vm_method_at(m, rd_i32(r)); }
            t->nitables = rd_i32(r); t->itables = (VmITable *)vm_zalloc(sizeof(VmITable) * (t->nitables + 1));
            for (int k = 0; k < t->nitables; k++)
            {
                VmITable *it = &t->itables[k]; it->iface = vm_type_at(m, rd_i32(r)); it->n = rd_i32(r);
                it->imethod = (VmMethod **)vm_zalloc(sizeof(VmMethod *) * (it->n + 1)); it->impl = (VmMethod **)vm_zalloc(sizeof(VmMethod *) * (it->n + 1));
                for (int j = 0; j < it->n; j++) { it->imethod[j] = vm_method_at(m, rd_i32(r)); it->impl[j] = vm_method_at(m, rd_i32(r)); }
            }
            if (t->kind == TK_DELEGATE) { t->dret = vm_type_at(m, rd_i32(r)); t->ndparams = rd_i32(r); t->dparams = (VmT **)vm_zalloc(sizeof(VmT *) * (t->ndparams + 1)); for (int k = 0; k < t->ndparams; k++) t->dparams[k] = vm_type_at(m, rd_i32(r)); }
            break;
        }
        }
        if (r->err) { ld_err(L, "tip tablosu bozuk: %s", t->name, 0); return 0; }
    }
    for (int i = 0; i < m->nfields; i++) { VmFieldRef *f = &m->fields[i]; f->owner = vm_type_at(m, rd_i32(r)); f->name = vm_str_at(m, rd_i32(r)); f->isStatic = rd_u8(r); f->type = vm_type_at(m, rd_i32(r)); }
    for (int i = 0; i < m->nmethods; i++)
    {
        VmMethod *me = m->methods[i];
        me->local = rd_u8(r); me->name = vm_str_at(m, rd_i32(r)); me->owner = vm_type_at(m, rd_i32(r));
        unsigned char fl = rd_u8(r); me->isStatic = fl & 1; me->isVirtual = (fl >> 1) & 1; me->preferHost = (fl >> 2) & 1; me->isCtor = (fl >> 3) & 1;
        me->ret = vm_type_at(m, rd_i32(r)); me->nargs = rd_i32(r); me->args = (VmArg *)vm_zalloc(sizeof(VmArg) * (me->nargs + 1));
        for (int k = 0; k < me->nargs; k++) { me->args[k].type = vm_type_at(m, rd_i32(r)); unsigned char af = rd_u8(r); me->args[k].isRef = af & 1; me->args[k].isOut = (af >> 1) & 1; }
        if (!me->local) continue;
        me->display = vm_str_at(m, rd_i32(r)); me->file = vm_str_at(m, rd_i32(r)); me->stubReason = vm_str_at(m, rd_i32(r));
        me->nlocals = rd_i32(r); me->locals = (VmT **)vm_zalloc(sizeof(VmT *) * (me->nlocals + 1)); for (int k = 0; k < me->nlocals; k++) me->locals[k] = vm_type_at(m, rd_i32(r));
        me->nops = rd_i32(r); me->ops = (VmOp *)vm_zalloc(sizeof(VmOp) * (me->nops + 1));
        for (int k = 0; k < me->nops; k++)
        {
            VmOp *o = &me->ops[k];
            o->op = rd_u8(r); o->slot = rd_i32(r); o->line = rd_i32(r);
            int fi = rd_i32(r); o->field = fi < 0 ? 0 : &m->fields[fi];
            o->code = vm_method_at(m, rd_i32(r)); o->prim = vm_type_at(m, rd_i32(r));
            o->vtag = rd_u8(r);
            switch (o->vtag)
            {
            case 0: break; case 1: o->val.i = rd_i32(r); break; case 2: o->val.f = rd_f32(r); break; case 3: o->val.d = rd_f64(r); break;
            case 4: o->val.l = rd_i64(r); break; case 5: o->val.str = rd_i32(r); break; case 6: o->val.i = rd_u8(r); break; case 7: o->val.i = rd_u16(r); break;
            case 8: o->val.i = (short)rd_u16(r); break; case 9: o->val.i = rd_u8(r); break; case 10: o->val.i = (int)rd_u32(r); break; case 11: o->val.l = rd_i64(r); break;
            case 12: o->val.i = rd_u16(r); break; case 13: o->val.i = (signed char)rd_u8(r); break;
            default: ld_err(L, "bilinmeyen literal etiketi: %s", me->name, 0); return 0;
            }
            if (o->op >= OP__COUNT) { ld_err(L, "bilinmeyen opcode: %s", me->name, 0); return 0; }
        }
        if (r->err) { ld_err(L, "metot tablosu bozuk: %s", me->name, 0); return 0; }
    }
    // skaler kimlik fixup: tablo girdileri kanonik paylasimli VmT ile degistirildi (yukarida), ama daha once okunan
    // referanslar placeholder kopyasina isaret ediyor olabilir -> tum VmT* alanlarini kanonige cek (kimlik karsilastirmalari icin)
#define FIXT(x) do { VmT *_t = (x); if (_t && _t->kind == TK_SCALAR && vm_scalar[_t->tag] && vm_scalar[_t->tag] != _t) (x) = vm_scalar[_t->tag]; } while (0)
    for (int i = 0; i < m->ntypes; i++)
    {
        VmT *t = m->types[i]; if (t->kind == TK_SCALAR) continue;
        FIXT(t->elem); FIXT(t->parent); FIXT(t->dret);
        for (int k = 0; k < t->nifaces; k++) FIXT(t->ifaces[k]);
        for (int k = 0; k < t->nfields; k++) FIXT(t->fields[k].type);
        for (int k = 0; k < t->ndparams; k++) FIXT(t->dparams[k]);
    }
    for (int i = 0; i < m->nfields; i++) { FIXT(m->fields[i].owner); FIXT(m->fields[i].type); }
    for (int i = 0; i < m->nmethods; i++)
    {
        VmMethod *me = m->methods[i]; FIXT(me->owner); FIXT(me->ret);
        for (int k = 0; k < me->nargs; k++) FIXT(me->args[k].type);
        for (int k = 0; k < me->nlocals; k++) FIXT(me->locals[k]);
        for (int k = 0; k < me->nops; k++) FIXT(me->ops[k].prim);
    }
#undef FIXT
    return !r->err;
}

// ================= LINK =================
static VmT *vm_type_by_name(VmModule *m, const char *name) { for (int i = 0; i < m->ntypes; i++) if (m->types[i]->name && !strcmp(m->types[i]->name, name)) return m->types[i]; return 0; }
static VmT *vm_local_of(const Type *t) { return (t && t->module) ? ((VmLocalType *)t)->t : 0; }
static void vm_statics_trace(void *f)
{
    VmModule *m = (VmModule *)f;
    if (m->mod.state >= 2) return; // unload: statikler artik kok degil
    for (int i = 0; i < m->ntypes; i++)
    {
        VmT *t = m->types[i]; if (!t->isLocal || !t->statics) continue;
        for (int k = 0; k < t->nfields; k++) { VmField *fl = &t->fields[k]; if (!fl->isStatic) continue; if (fl->type->tag == 'o') gc_shade(*(GCHeader **)fl->staddr); else if (fl->type->tag == 'v') for (int j = 0; j < fl->type->nrefs; j++) gc_shade(*(GCHeader **)((char *)fl->staddr + fl->type->refs[j])); }
    }
}
static const void *vm_vtramp_for(unsigned long long rootHash) { for (int i = 0; i < de_host_nvtramps; i++) if (de_host_vtramps[i].hash == rootHash) return de_host_vtramps[i].fn; return 0; }
static const void *vm_dtramp_for(const char *delegateName) { unsigned long long h = vm_hash(delegateName); for (int i = 0; i < de_host_ndtramps; i++) if (de_host_dtramps[i].hash == h) return de_host_dtramps[i].fn; return 0; }
static void vm_exec_frame(VmMethod *m, DeSlot *args, DeSlot *ret);

static int vm_link(LoadCtx *L)
{
    VmModule *m = L->m;
    for (int i = 0; i < m->ntypes; i++) { vm_resolve_type(L, m->types[i]); if (L->failed) return 0; }
    // yerel statik depolar (alan referanslari cozulmeden ONCE: adresler oradan okunur)
    for (int i = 0; i < m->ntypes; i++)
    {
        VmT *t = m->types[i]; if (!t->isLocal || t->kind == TK_IFACE) continue;
        int off = 0;
        for (int k = 0; k < t->nfields; k++) { VmField *f = &t->fields[k]; if (!f->isStatic) continue; int sz, al; vm_field_size(L, f->type, &sz, &al); off = vm_align_up(off, al); f->offset = off; off += sz; }
        if (off > 0) { t->statics = (char *)vm_zalloc(off); for (int k = 0; k < t->nfields; k++) if (t->fields[k].isStatic) t->fields[k].staddr = t->statics + t->fields[k].offset; }
    }
    for (int i = 0; i < m->nmethods; i++) { vm_resolve_method(L, m->methods[i]); if (L->failed) return 0; }
    for (int i = 0; i < m->nfields; i++) { vm_resolve_fieldref(L, &m->fields[i]); if (L->failed) return 0; }
    gc_add_frame_root(m, vm_statics_trace);
    // vtable / itable: yerel class'lar
    for (int i = 0; i < m->ntypes; i++)
    {
        VmT *t = m->types[i]; if (!t->isLocal || (t->kind != TK_CLASS && t->kind != TK_DELEGATE)) continue;
        // slot indeksleri: host kok slotlari host'un indeksinde, yeni yerel sanallar sonda. Yerel vtable = parent kopyasi + yeni slotlar.
        int nbase = 0; const void *const *basevt = 0;
        if (t->parent) { if (t->parent->isLocal) { nbase = t->parent->nvtable; basevt = t->parent->vtable; } else if (t->parent->type) { nbase = t->parent->type->nvtable; basevt = t->parent->type->vtable; } }
        // yerel vslots listesi tam vtable'i (parent dahil) tasir (Hierarchy.Build: parent slotlari once). Host parent'in
        // slot sayisi ve miras slotlardaki metot adlari host ile birebir olmali (engine surumu uyusmazligi burada yakalanir).
        if (t->nvslots < nbase) { ld_err(L, "vtable uyusmazligi (host parent daha fazla sanal slot tasiyor): %s", t->name, 0); return 0; }
        int n = t->nvslots;
        t->nvtable = n; t->vtable = (const void **)vm_zalloc(sizeof(void *) * (n + 1)); t->vimpl = (VmMethod **)vm_zalloc(sizeof(VmMethod *) * (n + 1));
        for (int k = 0; k < n; k++)
        {
            VmVSlot *s = &t->vslots[k]; VmMethod *impl = s->impl;
            if (!impl->local)
            {
                // host impl (miras): host parent ayni slotta ayni metodu tasimali
                if (k < nbase && basevt && t->parent && !t->parent->isLocal) { const DeSlotExport *hs = vm_host_slot(t->parent->host, vm_hash(impl->name)); if (!hs || hs->slot != k) { ld_err(L, "miras sanal slot host ile uyusmuyor (engine surumu?): %s", impl->name, 0); return 0; } }
                t->vtable[k] = impl->fn; t->vimpl[k] = 0; continue;
            }
            t->vimpl[k] = impl;
            // host kok: host'un slot indeksi ile dogrula ve trampoline koy
            if (s->decl && !s->decl->local)
            {
                const DeSlotExport *hs = t->parent && t->parent->host ? vm_host_slot(t->parent->host, vm_hash(s->decl->name)) : 0;
                if (!hs) { for (VmT *a = t->parent; a && !hs; a = a->parent) if (a->host) hs = vm_host_slot(a->host, vm_hash(s->decl->name)); }
                if (!hs) { ld_err(L, "host sanal slotu yok: %s (kok %s)", impl->name, s->decl->name); return 0; }
                if (hs->slot != k) { ld_err(L, "sanal slot indeksi host ile uyusmuyor: %s", impl->name, 0); return 0; }
                const void *tr = vm_vtramp_for(vm_hash(s->decl->name));
                if (!tr) { ld_err(L, "host trampoline yok (CTranspiler EmitTrampolines): %s", s->decl->name, 0); return 0; }
                t->vtable[k] = tr;
            }
            else t->vtable[k] = &impl->marker; // modul koklu sanal: yalniz interpreter cagirir
        }
        // itables
        t->niimpls = t->nitables; t->iimpls = (IfaceImpl *)vm_zalloc(sizeof(IfaceImpl) * (t->nitables + 1));
        for (int k = 0; k < t->nitables; k++)
        {
            VmITable *it = &t->itables[k]; const void **tab = (const void **)vm_zalloc(sizeof(void *) * (it->n + 1));
            t->iimpls[k].iface = it->iface->type;
            for (int j = 0; j < it->n; j++)
            {
                VmMethod *impl = it->impl[j];
                if (!impl->local) { tab[j] = impl->fn; continue; }
                if (!it->iface->isLocal)
                {
                    // host iface: slot sirasi host'un iface vslots sirasiyla dogrulanir
                    const DeSlotExport *hs = it->iface->host ? vm_host_slot(it->iface->host, vm_hash(it->imethod[j]->name)) : 0;
                    if (!hs) { ld_err(L, "host iface slotu yok: %s", it->imethod[j]->name, 0); return 0; }
                    if (hs->slot != j) { ld_err(L, "iface slot indeksi host ile uyusmuyor: %s", it->imethod[j]->name, 0); return 0; }
                    const void *tr = vm_vtramp_for(vm_hash(it->imethod[j]->name));
                    if (!tr) { ld_err(L, "host iface trampoline yok: %s", it->imethod[j]->name, 0); return 0; }
                    tab[j] = tr;
                }
                else tab[j] = &impl->marker;
            }
            t->iimpls[k].table = tab;
        }
        if (t->kind == TK_CLASS) { Type *ty = &t->ltype->type; ty->vtable = t->vtable; ty->nvtable = (unsigned short)t->nvtable; ty->itables = t->iimpls; ty->nitables = (unsigned short)t->niimpls; }
    }
    // closure tipi (delegate hedefi yerel metot)
    m->closureType.type.trace = vm_trace_closure; m->closureType.type.size = sizeof(VmClosure); m->closureType.type.base = &vmobject_type; m->closureType.type.module = &m->mod;
    m->closureType.type.name = vm_immortal_str(m, "<closure>");
    vm_string = vm_type_by_name(m, "System.String"); vm_object = vm_type_by_name(m, "System.Object");
    if (!vm_string) { vm_string = (VmT *)vm_zalloc(sizeof(VmT)); vm_string->kind = TK_EXTERN; vm_string->tag = 'o'; vm_string->name = vm_string->display = "System.String"; vm_string->type = &vmstring_type; vm_string->resolved = 1; vm_string->size = vm_string->align = sizeof(void *); vm_string->host = de_host_find_type(vm_hash("System.String")); }
    if (!vm_object) { vm_object = (VmT *)vm_zalloc(sizeof(VmT)); vm_object->kind = TK_EXTERN; vm_object->tag = 'o'; vm_object->name = vm_object->display = "System.Object"; vm_object->type = &vmobject_type; vm_object->resolved = 1; vm_object->size = vm_object->align = sizeof(void *); vm_object->host = de_host_find_type(vm_hash("System.Object")); }
    vm_void = vm_scalar_t('V'); vm_voidptr = vm_ptr_of(vm_void);
    return 1;
}

// ================= INTERPRETER =================
typedef struct VmVal { DeSlot v; VmT *t; } VmVal;
#define VM_OPSTACK 256
typedef struct Frame
{
    VmMethod *m; DeSlot *args; DeSlot *locals; char *blob; VmVal *st; int sp;
    char *tempBase; DeSlot *ret; int ntries; RtTry *tries; char **tryTop; int *openTries; int nopen;
} Frame;
static inline void vm_push(Frame *F, DeSlot v, VmT *t) { if (F->sp >= VM_OPSTACK) vm_fail("interpreter operand stack tasti"); F->st[F->sp].v = v; F->st[F->sp].t = t; F->sp++; }
static inline VmVal vm_pop(Frame *F) { if (F->sp <= 0) vm_fail("interpreter operand stack bos (bozuk IR)"); return F->st[--F->sp]; }
static inline void *vm_temp(Frame *F, int bytes) { (void)F; bytes = vm_align_up(bytes, 16); if (vm_top + bytes > vm_end) vm_fail("interpreter bellegi tasti"); void *p = vm_top; memset(p, 0, bytes); vm_top += bytes; return p; } // 16: jmp_buf (RtTry) hizasi
static inline DeSlot S_i(int i) { DeSlot s; s.l = 0; s.i = i; return s; }
static inline DeSlot S_p(void *p) { DeSlot s; s.l = 0; s.p = p; return s; }
static inline DeSlot S_l(long long l) { DeSlot s; s.l = l; return s; }
static inline DeSlot S_f(float f) { DeSlot s; s.l = 0; s.f = f; return s; }
static inline DeSlot S_d(double d) { DeSlot s; s.d = d; return s; }

// bellekten tipli oku/yaz (alan, eleman, indirect)
static DeSlot vm_load(const void *p, VmT *t)
{
    DeSlot s; s.l = 0;
    switch (t->tag)
    {
    case 'i': s.i = *(const cil_int *)p; break; case 'u': s.u = *(const cil_uint *)p; break;
    case 'l': s.l = *(const cil_long *)p; break; case 'q': s.q = *(const cil_ulong *)p; break;
    case 'h': s.i = *(const cil_short *)p; break; case 'H': s.u = *(const cil_ushort *)p; break;
    case 'b': s.i = *(const cil_byte *)p; break; case 'z': s.i = *(const cil_sbyte *)p; break;
    case 'c': s.i = *(const cil_char *)p; break; case 'B': s.i = *(const cil_bool *)p; break;
    case 'f': s.f = *(const cil_float *)p; break; case 'd': s.d = *(const cil_double *)p; break;
    case 'v': s.p = (void *)p; break; // struct: adres (alias); cagiran gerekirse kopyalar
    default: s.p = *(void *const *)p; break;
    }
    return s;
}
static void vm_store(void *p, VmT *t, DeSlot s)
{
    switch (t->tag)
    {
    case 'i': *(cil_int *)p = s.i; break; case 'u': *(cil_uint *)p = s.u; break;
    case 'l': *(cil_long *)p = s.l; break; case 'q': *(cil_ulong *)p = s.q; break;
    case 'h': *(cil_short *)p = (cil_short)s.i; break; case 'H': *(cil_ushort *)p = (cil_ushort)s.u; break;
    case 'b': *(cil_byte *)p = (cil_byte)s.i; break; case 'z': *(cil_sbyte *)p = (cil_sbyte)s.i; break;
    case 'c': *(cil_char *)p = (cil_char)s.i; break; case 'B': *(cil_bool *)p = s.i; break;
    case 'f': *(cil_float *)p = s.f; break; case 'd': *(cil_double *)p = s.d; break;
    case 'v': if (s.p != p) memmove(p, s.p, t->size); for (int k = 0; k < t->nrefs; k++) gc_write_barrier(0, *(GCHeader **)((char *)p + t->refs[k])); break;
    case 'o': *(void **)p = s.p; gc_write_barrier(0, (GCHeader *)s.p); break;
    default: *(void **)p = s.p; break;
    }
}
static int vm_elemsize(VmT *t) { return t->tag == 'v' ? t->size : t->tag == 'V' ? 1 : vm_tag_size(t->tag); }
// struct degeri tempe kopyala (deger semantigi)
static DeSlot vm_copy_struct(Frame *F, DeSlot s, VmT *t) { void *d = vm_temp(F, t->size); memcpy(d, s.p, t->size); return S_p(d); }

// ---- sayisal terfi (Primitive.PromoteNumeric) ----
static int vm_is_signed_small(unsigned char g) { return g == 'i' || g == 'h' || g == 'z'; }
static unsigned char vm_promote(unsigned char a, unsigned char b)
{
    if (a == 'd' || b == 'd') return 'd'; if (a == 'f' || b == 'f') return 'f'; if (a == 'q' || b == 'q') return 'q'; if (a == 'l' || b == 'l') return 'l';
    int au = a == 'u', bu = b == 'u';
    if (au && bu) return 'u'; if (au) return vm_is_signed_small(b) ? 'l' : 'u'; if (bu) return vm_is_signed_small(a) ? 'l' : 'u';
    return 'i';
}
static long long vm_as_i64(DeSlot s, unsigned char g) { switch (g) { case 'u': case 'H': return (long long)s.u; case 'l': return s.l; case 'q': return (long long)s.q; case 'f': return (long long)s.f; case 'd': return (long long)s.d; default: return (long long)s.i; } }
static double vm_as_f64(DeSlot s, unsigned char g) { switch (g) { case 'u': case 'H': return (double)s.u; case 'l': return (double)s.l; case 'q': return (double)s.q; case 'f': return (double)s.f; case 'd': return s.d; default: return (double)s.i; } }
// C cast semantigi: kaynak tagdan hedef taga ((T)(v))
static DeSlot vm_conv(DeSlot s, unsigned char from, unsigned char to)
{
    DeSlot r; r.l = 0;
    int ff = (from == 'f' || from == 'd');
    switch (to)
    {
    case 'i': r.i = ff ? (cil_int)vm_as_f64(s, from) : (cil_int)vm_as_i64(s, from); break;
    case 'u': r.u = ff ? (cil_uint)vm_as_f64(s, from) : (cil_uint)vm_as_i64(s, from); break;
    case 'l': r.l = ff ? (cil_long)vm_as_f64(s, from) : vm_as_i64(s, from); break;
    case 'q': r.q = ff ? (cil_ulong)vm_as_f64(s, from) : (cil_ulong)vm_as_i64(s, from); break;
    case 'h': r.i = (cil_short)(ff ? (cil_int)vm_as_f64(s, from) : (cil_int)vm_as_i64(s, from)); break;
    case 'H': r.u = (cil_ushort)(ff ? (cil_int)vm_as_f64(s, from) : (cil_int)vm_as_i64(s, from)); break;
    case 'b': r.i = (cil_byte)(ff ? (cil_int)vm_as_f64(s, from) : (cil_int)vm_as_i64(s, from)); break;
    case 'z': r.i = (cil_sbyte)(ff ? (cil_int)vm_as_f64(s, from) : (cil_int)vm_as_i64(s, from)); break;
    case 'c': r.i = (cil_char)(ff ? (cil_int)vm_as_f64(s, from) : (cil_int)vm_as_i64(s, from)); break;
    case 'B': r.i = ff ? (vm_as_f64(s, from) != 0) : (int)vm_as_i64(s, from); break;
    case 'f': r.f = ff ? (cil_float)vm_as_f64(s, from) : (from == 'q' ? (cil_float)s.q : from == 'u' ? (cil_float)s.u : (cil_float)vm_as_i64(s, from)); break;
    case 'd': r.d = ff ? vm_as_f64(s, from) : (from == 'q' ? (cil_double)s.q : from == 'u' ? (cil_double)s.u : (cil_double)vm_as_i64(s, from)); break;
    default: r = s; break;
    }
    return r;
}
static DeSlot vm_numbin(unsigned char op, DeSlot a, unsigned char ga, DeSlot b, unsigned char gb, unsigned char *gout)
{
    unsigned char g = vm_promote(ga, gb); *gout = g; DeSlot r; r.l = 0;
    DeSlot ca = vm_conv(a, ga, g), cb = vm_conv(b, gb, g);
    switch (g)
    {
    case 'd': { double x = ca.d, y = cb.d; switch (op) { case OP_Add: r.d = x + y; break; case OP_Sub: r.d = x - y; break; case OP_Mul: r.d = x * y; break; case OP_Div: r.d = x / y; break; case OP_Mod: r.d = fmod(x, y); break; default: vm_fail("double bit islemi"); } break; }
    case 'f': { float x = ca.f, y = cb.f; switch (op) { case OP_Add: r.f = x + y; break; case OP_Sub: r.f = x - y; break; case OP_Mul: r.f = x * y; break; case OP_Div: r.f = x / y; break; case OP_Mod: r.f = fmodf(x, y); break; default: vm_fail("float bit islemi"); } break; }
    case 'q': { cil_ulong x = ca.q, y = cb.q; switch (op) { case OP_Add: r.q = x + y; break; case OP_Sub: r.q = x - y; break; case OP_Mul: r.q = x * y; break; case OP_Div: r.q = x / y; break; case OP_Mod: r.q = x % y; break; case OP_And: r.q = x & y; break; case OP_Or: r.q = x | y; break; case OP_Xor: r.q = x ^ y; break; } break; }
    case 'l': { cil_long x = ca.l, y = cb.l; switch (op) { case OP_Add: r.l = x + y; break; case OP_Sub: r.l = x - y; break; case OP_Mul: r.l = x * y; break; case OP_Div: r.l = (y == -1) ? -x : x / y; break; case OP_Mod: r.l = (y == -1) ? 0 : x % y; break; case OP_And: r.l = x & y; break; case OP_Or: r.l = x | y; break; case OP_Xor: r.l = x ^ y; break; } break; }
    case 'u': { cil_uint x = ca.u, y = cb.u; switch (op) { case OP_Add: r.u = x + y; break; case OP_Sub: r.u = x - y; break; case OP_Mul: r.u = x * y; break; case OP_Div: r.u = x / y; break; case OP_Mod: r.u = x % y; break; case OP_And: r.u = x & y; break; case OP_Or: r.u = x | y; break; case OP_Xor: r.u = x ^ y; break; } break; }
    default: { cil_int x = ca.i, y = cb.i; switch (op) { case OP_Add: r.i = (cil_int)((cil_uint)x + (cil_uint)y); break; case OP_Sub: r.i = (cil_int)((cil_uint)x - (cil_uint)y); break; case OP_Mul: r.i = (cil_int)((cil_uint)x * (cil_uint)y); break; case OP_Div: r.i = (y == -1) ? (cil_int)(0u - (cil_uint)x) : x / y; break; case OP_Mod: r.i = (y == -1) ? 0 : x % y; break; case OP_And: r.i = x & y; break; case OP_Or: r.i = x | y; break; case OP_Xor: r.i = x ^ y; break; } break; }
    }
    return r;
}
static int vm_cmp(unsigned char op, VmVal a, VmVal b)
{
    if (!vm_is_numeric(a.t) || !vm_is_numeric(b.t))
    {
        // referans/pointer/null: ham karsilastirma (null Void -> 0)
        uintptr_t x = vm_is_numeric(a.t) ? (uintptr_t)vm_as_i64(a.v, a.t->tag) : (uintptr_t)a.v.p, y = vm_is_numeric(b.t) ? (uintptr_t)vm_as_i64(b.v, b.t->tag) : (uintptr_t)b.v.p;
        switch (op) { case OP_Ceq: return x == y; case OP_Cne: return x != y; case OP_Cgt: return x > y; case OP_Cge: return x >= y; case OP_Clt: return x < y; default: return x <= y; }
    }
    unsigned char g = vm_promote(a.t->tag, b.t->tag);
    DeSlot ca = vm_conv(a.v, a.t->tag, g), cb = vm_conv(b.v, b.t->tag, g);
    switch (g)
    {
    case 'd': switch (op) { case OP_Ceq: return ca.d == cb.d; case OP_Cne: return ca.d != cb.d; case OP_Cgt: return ca.d > cb.d; case OP_Cge: return ca.d >= cb.d; case OP_Clt: return ca.d < cb.d; default: return ca.d <= cb.d; }
    case 'f': switch (op) { case OP_Ceq: return ca.f == cb.f; case OP_Cne: return ca.f != cb.f; case OP_Cgt: return ca.f > cb.f; case OP_Cge: return ca.f >= cb.f; case OP_Clt: return ca.f < cb.f; default: return ca.f <= cb.f; }
    case 'q': switch (op) { case OP_Ceq: return ca.q == cb.q; case OP_Cne: return ca.q != cb.q; case OP_Cgt: return ca.q > cb.q; case OP_Cge: return ca.q >= cb.q; case OP_Clt: return ca.q < cb.q; default: return ca.q <= cb.q; }
    case 'l': switch (op) { case OP_Ceq: return ca.l == cb.l; case OP_Cne: return ca.l != cb.l; case OP_Cgt: return ca.l > cb.l; case OP_Cge: return ca.l >= cb.l; case OP_Clt: return ca.l < cb.l; default: return ca.l <= cb.l; }
    case 'u': switch (op) { case OP_Ceq: return ca.u == cb.u; case OP_Cne: return ca.u != cb.u; case OP_Cgt: return ca.u > cb.u; case OP_Cge: return ca.u >= cb.u; case OP_Clt: return ca.u < cb.u; default: return ca.u <= cb.u; }
    default: switch (op) { case OP_Ceq: return ca.i == cb.i; case OP_Cne: return ca.i != cb.i; case OP_Cgt: return ca.i > cb.i; case OP_Cge: return ca.i >= cb.i; case OP_Clt: return ca.i < cb.i; default: return ca.i <= cb.i; }
    }
}

// ---- kutulama ----
static GCHeader *vm_box(Frame *F, VmVal v)
{
    VmT *t = v.t; (void)F;
    if (t->kind == TK_ENUM || (t->kind == TK_SCALAR && t->type && t->type->base == &vmenum_type)) return (GCHeader *)digitoyengine_box_enum(v.v.i, t->type);
    switch (t->tag)
    {
    case 'i': return (GCHeader *)digitoyengine_box_i32(v.v.i); case 'u': return (GCHeader *)digitoyengine_box_u32(v.v.u);
    case 'l': return (GCHeader *)digitoyengine_box_i64(v.v.l); case 'q': return (GCHeader *)digitoyengine_box_u64(v.v.q);
    case 'h': return (GCHeader *)digitoyengine_box_i16((short)v.v.i); case 'H': return (GCHeader *)digitoyengine_box_u16((unsigned short)v.v.u);
    case 'b': return (GCHeader *)digitoyengine_box_u8((unsigned char)v.v.i); case 'z': return (GCHeader *)digitoyengine_box_i8((signed char)v.v.i);
    case 'c': return (GCHeader *)digitoyengine_box_char((cil_char)v.v.i); case 'B': return (GCHeader *)digitoyengine_box_bool(v.v.i);
    case 'f': return (GCHeader *)digitoyengine_box_f32(v.v.f); case 'd': return (GCHeader *)digitoyengine_box_f64(v.v.d);
    case 'v': if (!t->type) vm_fail("struct kutulama descriptor'suz"); return (GCHeader *)digitoyengine_box_struct(t->type, v.v.p, t->size);
    default: return (GCHeader *)v.v.p;
    }
}
static int vm_type_test(const Type *rt, VmT *target)
{
    if (!target->type) return 0;
    return (target->kind == TK_IFACE || (target->kind == TK_EXTERN && (target->flags & 2))) ? DIGITOYENGINE_implements(rt, target->type) : DIGITOYENGINE_is(rt, target->type);
}
// deger -> hedef tipe donusum (CTranspiler.Coerce): boxing, nullable sarma, pointer<->native int; aksi aynen
static DeSlot vm_coerce(Frame *F, VmVal v, VmT *target)
{
    if (!target) return v.v;
    if (target->isNullable)
    {
        if (v.t == target) return v.v;
        if (vm_is_ptr(v.t) && v.t->elem == target) return v.v;
        char *d = (char *)vm_temp(F, target->size);
        if (v.t->tag == 'V') return S_p(d);
        *(cil_int *)(d + target->nullHasOff) = 1;
        VmVal inner = v; DeSlot cv = vm_coerce(F, inner, target->nullValue); vm_store(d + target->nullValOff, target->nullValue, cv);
        return S_p(d);
    }
    if (target->tag == 'o' && v.t && v.t != target && vm_is_value(v.t)) return S_p(vm_box(F, v));
    if (target->tag == 'v' && v.t && v.t->tag == 'v' && v.t != target && vm_is_ptr(v.t) == 0) return v.v;
    if (target->kind == TK_POINTER && v.t && (v.t->tag == 'l' || v.t->tag == 'q')) return S_p((void *)(size_t)v.v.l);
    if ((target->tag == 'l' || target->tag == 'q') && v.t && vm_is_ptr(v.t)) return S_l((long long)(size_t)v.v.p);
    if (vm_is_numeric(target) && vm_is_numeric(v.t) && target->tag != v.t->tag) return vm_conv(v.v, v.t->tag, target->tag);
    return v.v;
}
// cagri argumani (CTranspiler.MaterializeCallArg)
static DeSlot vm_call_arg(Frame *F, VmVal v, VmArg *decl)
{
    if ((decl->isRef || decl->isOut) && vm_is_ptr(v.t)) return v.v;
    if (!(decl->isRef || decl->isOut) && vm_is_ptr(v.t) && v.t->elem == decl->type) { VmVal d; d.t = decl->type; d.v = vm_load(v.v.p, decl->type); return vm_coerce(F, d, decl->type); }
    if ((decl->isRef || decl->isOut) && decl->type->tag == 'v' && v.t == decl->type) { DeSlot c = vm_copy_struct(F, v.v, decl->type); return c; } // &temp kopya
    if ((decl->isRef || decl->isOut) && !vm_is_ptr(v.t)) return v.v; // pointer bekleniyor ama deger: oldugu gibi (adres tasiyan slot)
    return vm_coerce(F, v, decl->type);
}

// ---- cagri yollari ----
static void vm_invoke(Frame *F, VmMethod *m, DeSlot *a, DeSlot *r)
{
    if (m->local) { vm_exec_frame(m, a, r); return; }
    if (!m->fn) vm_fail("cozulmemis dis metot");
    m->thunk(m->fn, a, r);
    (void)F;
}
static void vm_push_ret(Frame *F, VmT *ret, DeSlot r) { if (ret->tag != 'V') vm_push(F, r, ret); }
// donus struct ise cagiran blob ayirir (r.p)
static DeSlot vm_prep_ret(Frame *F, VmT *ret) { DeSlot r; r.l = 0; if (ret->tag == 'v') r.p = vm_temp(F, ret->size); return r; }

// sanal cagri: alici tipine gore yerel impl ya da host fn
static void vm_call_virtual(Frame *F, VmMethod *decl, int slot, GCHeader *recv, DeSlot *a, DeSlot *r)
{
    const Type *rt = recv->type; VmT *lt = vm_local_of(rt);
    int isIface = decl->owner && (decl->owner->kind == TK_IFACE || (decl->owner->kind == TK_EXTERN && (decl->owner->flags & 2)));
    if (lt && lt->kind == TK_CLASS)
    {
        if (!isIface) { if (slot < lt->nvtable && lt->vimpl[slot]) { vm_exec_frame(lt->vimpl[slot], a, r); return; } const void *fn = lt->vtable[slot]; decl->thunk ? decl->thunk(fn, a, r) : vm_fail("sanal cagri thunk'i yok"); return; }
        for (int k = 0; k < lt->nitables; k++) if (lt->itables[k].iface == decl->owner || lt->itables[k].iface->type == decl->owner->type) { VmMethod *impl = lt->itables[k].impl[slot]; if (impl->local) vm_exec_frame(impl, a, r); else impl->thunk(impl->fn, a, r); return; }
        DIGITOYENGINE_cast_fail(rt, decl->owner->type);
    }
    const void *fn = isIface ? DIGITOYENGINE_itable(rt, decl->owner->type)[slot] : rt->vtable[slot];
    VmMethod *lm = vm_marker_get(fn); if (lm) { vm_exec_frame(lm, a, r); return; }
    if (!decl->thunk) { int sh = vm_shape_of(decl->ret, decl->nargs, decl->args, 0); if (sh < 0) vm_fail("sanal cagri icin host sekli yok"); decl->thunk = de_host_thunks[sh]; }
    decl->thunk(fn, a, r);
}
// delegate cagrisi (tek dugum)
static int vm_is_closure(GCHeader *o) { return o && o->type && o->type->module && o->type->trace == vm_trace_closure; }
static void vm_call_delegate_leaf(Frame *F, VmT *dt, VmDelegate *d, DeSlot *a, DeSlot *r)
{
    int n = dt->ndparams;
    if (vm_is_closure(d->target)) // yerel hedef: closure {target, m} (fn marker ya da host trampoline'i olabilir)
    {
        VmClosure *c = (VmClosure *)d->target; VmMethod *lm = c->m;
        if (c->target) { memmove(a + 1, a, sizeof(DeSlot) * n); a[0].p = c->target; vm_exec_frame(lm, a, r); }
        else vm_exec_frame(lm, a, r);
        return;
    }
    VmMethod *lm = vm_marker_get(d->fn);
    if (lm) { if (d->target) { memmove(a + 1, a, sizeof(DeSlot) * n); a[0].p = d->target; } vm_exec_frame(lm, a, r); return; }
    // host fn
    VmArg tmp[64]; for (int i = 0; i < n && i < 64; i++) { tmp[i].type = dt->dparams[i]; tmp[i].isRef = tmp[i].isOut = 0; }
    if (d->target) { int sh = vm_shape_of(dt->dret, n, tmp, 1); if (sh < 0) vm_fail("delegate (instance) icin host sekli yok"); memmove(a + 1, a, sizeof(DeSlot) * n); a[0].p = d->target; de_host_thunks[sh](d->fn, a, r); }
    else { int sh = vm_shape_of(dt->dret, n, tmp, 0); if (sh < 0) vm_fail("delegate (static) icin host sekli yok"); de_host_thunks[sh](d->fn, a, r); }
    (void)F;
}

// ExBind trace baglama: exception tipinin host export'unda traceCount/trace.mi/trace.line
static void vm_bind_trace(GCHeader *e, VmT *catchT)
{
    const DeTypeExport *te = catchT->host ? catchT->host : (catchT->type ? de_host_type_of(catchT->type) : 0);
    for (VmT *a = catchT; !te && a; a = a->parent) te = a->host;
    if (!te) return;
    const DeTypeExport *o1 = 0, *o2 = 0;
    const DeFieldExport *tc = vm_host_field(te, 0, "traceCount", &o1), *tr = vm_host_field(te, 0, "trace", &o2);
    if (!tc || !tr || !tr->type) return;
    const DeTypeExport *ts = de_host_type_of(tr->type); if (!ts) return;
    const DeFieldExport *mi = 0, *ln = 0;
    for (int i = 0; i < ts->nfields; i++) { if (!strcmp(ts->fields[i].name + strlen(ts->name) + 1, "mi")) mi = &ts->fields[i]; if (!strcmp(ts->fields[i].name + strlen(ts->name) + 1, "line")) ln = &ts->fields[i]; }
    if (!mi || !ln) return;
    int cap = mi->size / 8; if (cap <= 0) return;
    int *count = (int *)((char *)e + tc->offset);
    if (*count == 0) DIGITOYENGINE_bind_trace((long long *)((char *)e + tr->offset + mi->offset), (int *)((char *)e + tr->offset + ln->offset), count, cap);
}

// ================= EXEC =================
static VmT *vm_prim_type(Frame *F) { (void)F; static VmT *st; if (!st) { VmModule *m = vm_modules; st = m ? vm_type_by_name(m, "System.Type") : 0; } return st ? st : vm_object; }
static GCHeader *vm_nullcheck(GCHeader *o) { if (DIGITOYENGINE_UNLIKELY(!o)) DIGITOYENGINE_throw_null(); return o; }
static void *vm_array_elem(VmArray *a, Frame *F, int rank, int *off)
{
    vm_nullcheck((GCHeader *)a);
    if (a->rank != rank) DIGITOYENGINE_throw_bounds(rank, a->rank);
    int o = 0;
    for (int i = 0; i < rank; i++) { int idx = F->st[F->sp - rank + i].v.i; int dim = rank == 1 ? a->len : a->dims[i]; DIGITOYENGINE_BOUNDS(idx, dim); o = o * dim + idx; }
    F->sp -= rank; *off = o;
    return (char *)a->data + (size_t)o * a->elemsize;
}
#define POP() vm_pop(F)
#define PUSH(v, t) vm_push(F, (v), (t))
#define LINE() (DIGITOYENGINE_LINE(op->line))

static const char *vm_opname[] = {
    "GetArg","SetArg","GetLocal","SetLocal","GetField","SetField","GetIndex","SetIndex","New","Dup","Return","Push","Pop","Mul","Div","Add","Sub","Mod","Neg","And","Or","Not","Xor","Shl","Shr",
    "Call","Label","Br","Brtrue","Brfalse","Ceq","Cne","Cgt","Cge","Clt","Cle","AddrLocal","AddrArg","AddrField","AddrElement","ArrayDataAddr","AddrStatic","LoadInd","StoreInd",
    "CallVirtual","GetStatic","SetStatic","NewArray","ArrayLength","StackAlloc","SizeOf","Conv","IsType","IsValueType","AsType","CastClass","GenericCast","TypeOf","Box","Unbox","UnboxOrDefault",
    "NullableWrap","NullableHasValue","NullableValue","NullableBinary","DelegateNew","DelegateCombine","DelegateRemove","CallIndirect","Default","Throw","TryBegin","TryEnd","ExIs","ExBind","Rethrow","TryUnwind","Yield" };
static int vm_trace_ops = -1;
static void vm_report_exception(const char *when)
{
    fprintf(stderr, "[vmint] %s: exception kind=%d msg=%s", when, DIGITOYENGINE_ex_kind, DIGITOYENGINE_ex_msg);
    if (DIGITOYENGINE_ex_obj && DIGITOYENGINE_ex_obj->type && DIGITOYENGINE_ex_obj->type->name) { fputs(" tip=", stderr); vm_write_utf8_to(stderr, DIGITOYENGINE_ex_obj->type->name->data, DIGITOYENGINE_ex_obj->type->name->length); }
    fputc('\n', stderr);
    for (int i = DIGITOYENGINE_ex_trace_n - 1; i >= 0 && i >= DIGITOYENGINE_ex_trace_n - 12; i--)
    {
        const RtFrame *f = &DIGITOYENGINE_ex_trace[i]; fputs("    at ", stderr);
        if (f->mi && f->mi->name) vm_write_utf8_to(stderr, f->mi->name->data, f->mi->name->length);
        fprintf(stderr, " :%d\n", f->line >> 10);
    }
}

static void vm_exec_frame(VmMethod *m, DeSlot *args, DeSlot *ret)
{
    if (m->mod->mod.state == 2) return; // unload edilmis modul: no-op (kalan delegate/sanal cagrilar)
    if (m->stubReason) // cevrilemeyen govde (CTranspiler ile ayni sozlesme): sessiz sifir YASAK
    {
        char what[512]; snprintf(what, sizeof what, "AOT stub: %s -- %s", m->display ? m->display : m->name, m->stubReason);
        DIGITOYENGINE_throw_notimpl(what);
    }
    if (vm_trace_ops < 0) vm_trace_ops = getenv("VMINT_TRACE") != 0;
    // frame bellegi
    char *saveTop = vm_top;
    Frame frameStorage; Frame *volatile F = &frameStorage; memset(F, 0, sizeof(Frame));
    F->m = m; F->ret = ret;
    F->args = (DeSlot *)vm_temp(F, sizeof(DeSlot) * (m->nargs + 1));
    F->locals = (DeSlot *)vm_temp(F, sizeof(DeSlot) * (m->nlocals + 1));
    F->blob = (char *)vm_temp(F, m->blobBytes);
    F->st = (VmVal *)vm_temp(F, sizeof(VmVal) * VM_OPSTACK);
    int ntries = m->ntries;
    F->ntries = ntries; F->tries = (RtTry *)vm_temp(F, sizeof(RtTry) * (ntries + 1)); F->tryTop = (char **)vm_temp(F, sizeof(char *) * (ntries + 1)); F->openTries = (int *)vm_temp(F, sizeof(int) * (ntries + 1));
    for (int i = 0; i < m->nargs; i++) { F->args[i] = args[i]; if (m->argBlobOff[i] >= 0) { memcpy(F->blob + m->argBlobOff[i], args[i].p, m->args[i].type->size); F->args[i].p = F->blob + m->argBlobOff[i]; } }
    for (int i = 0; i < m->nlocals; i++) { F->locals[i].l = 0; if (m->localBlobOff[i] >= 0) F->locals[i].p = F->blob + m->localBlobOff[i]; }
    F->tempBase = vm_top;
    DIGITOYENGINE_PUSH(&m->mi, 0);
    VmOp *ops = m->ops; volatile int vpc = 0;
    for (;;)
    {
        int pc = vpc;
        if (pc >= m->nops) vm_fail("metot sonu Return'suz");
        VmOp *op = &ops[pc]; vpc = pc + 1;
        if (vm_trace_ops) fprintf(stderr, "[vmint]   %s @%d %s slot=%d sp=%d top=%lld\n", m->name, pc, vm_opname[op->op], op->slot, F->sp, F->sp > 0 ? F->st[F->sp - 1].v.l : 0LL);
        if (F->sp == 0) vm_top = F->tempBase; // statement siniri: gecici struct alani sifirla
        switch (op->op)
        {
        case OP_Label: break;
        case OP_GetArg: { VmArg *a = &m->args[op->slot]; PUSH(F->args[op->slot], (a->isRef || a->isOut) ? vm_ptr_of(a->type) : a->type); break; }
        case OP_SetArg: { VmVal v = POP(); VmArg *a = &m->args[op->slot]; DeSlot c = vm_coerce(F, v, a->type); if (a->type->tag == 'v' && !a->isRef && !a->isOut) memcpy(F->args[op->slot].p, c.p, a->type->size); else F->args[op->slot] = c; break; }
        case OP_GetLocal: PUSH(F->locals[op->slot], m->locals[op->slot]); break;
        case OP_SetLocal: { VmVal v = POP(); VmT *t = m->locals[op->slot]; DeSlot c = vm_coerce(F, v, t); if (t->tag == 'v') { if (c.p && c.p != F->locals[op->slot].p) memcpy(F->locals[op->slot].p, c.p, t->size); } else F->locals[op->slot] = c; break; }
        case OP_AddrLocal: PUSH(S_p(m->locals[op->slot]->tag == 'v' ? F->locals[op->slot].p : (void *)&F->locals[op->slot]), vm_ptr_of(m->locals[op->slot])); break;
        case OP_AddrArg: { VmArg *a = &m->args[op->slot]; PUSH(S_p(a->type->tag == 'v' && !a->isRef && !a->isOut ? F->args[op->slot].p : (void *)&F->args[op->slot]), vm_ptr_of(a->type)); break; }
        case OP_GetField:
        {
            VmVal inst = POP(); VmFieldRef *f = op->field; char *base;
            if (vm_is_ptr(inst.t) || inst.t->tag == 'v') base = (char *)inst.v.p;
            else { LINE(); base = (char *)vm_nullcheck((GCHeader *)inst.v.p); }
            DeSlot v = vm_load(base + f->offset, f->type);
            if (f->type->tag == 'v' && !(vm_is_ptr(inst.t) || inst.t->tag == 'v')) v = vm_copy_struct(F, v, f->type);
            PUSH(v, f->type); break;
        }
        case OP_SetField:
        {
            VmVal v = POP(); VmVal inst = POP(); VmFieldRef *f = op->field; char *base;
            if (vm_is_ptr(inst.t) || inst.t->tag == 'v') base = (char *)inst.v.p; else { LINE(); base = (char *)vm_nullcheck((GCHeader *)inst.v.p); }
            vm_store(base + f->offset, f->type, vm_coerce(F, v, f->type)); break;
        }
        case OP_AddrField:
        {
            VmVal inst = POP(); VmFieldRef *f = op->field; char *base;
            if (vm_is_ptr(inst.t) || inst.t->tag == 'v') base = (char *)inst.v.p; else { LINE(); base = (char *)vm_nullcheck((GCHeader *)inst.v.p); }
            VmT *rt = f->type->kind == TK_FIXED ? vm_ptr_of(f->type->elem) : vm_ptr_of(f->type);
            PUSH(S_p(base + f->offset), rt); break;
        }
        case OP_AddrStatic: PUSH(S_p(op->field->addr), vm_ptr_of(op->field->type)); break;
        case OP_GetStatic: PUSH(vm_load(op->field->addr, op->field->type), op->field->type); break;
        case OP_SetStatic: { VmVal v = POP(); vm_store(op->field->addr, op->field->type, vm_coerce(F, v, op->field->type)); break; }
        case OP_GetIndex:
        {
            int rank = op->slot > 0 ? op->slot : 1; VmVal arr = F->st[F->sp - rank - 1]; LINE();
            if (arr.t->kind == TK_FIXED) { int idx = F->st[F->sp - 1].v.i; F->sp -= 2; DIGITOYENGINE_BOUNDS(idx, arr.t->fixedSize); PUSH(vm_load((char *)arr.v.p + (size_t)idx * vm_elemsize(arr.t->elem), arr.t->elem), arr.t->elem); break; }
            if (arr.t->kind == TK_POINTER) { int idx = F->st[F->sp - 1].v.i; F->sp -= 2; PUSH(vm_load((char *)arr.v.p + (size_t)idx * vm_elemsize(arr.t->elem), arr.t->elem), arr.t->elem); break; }
            int off; void *e = vm_array_elem((VmArray *)arr.v.p, F, rank, &off); F->sp--; VmT *et = arr.t->elem;
            DeSlot v = vm_load(e, et); if (et->tag == 'v') v = vm_copy_struct(F, v, et); PUSH(v, et); break;
        }
        case OP_SetIndex:
        {
            VmVal v = POP(); int rank = op->slot > 0 ? op->slot : 1; VmVal arr = F->st[F->sp - rank - 1]; LINE();
            if (arr.t->kind == TK_FIXED || arr.t->kind == TK_POINTER) { int idx = F->st[F->sp - 1].v.i; F->sp -= 2; if (arr.t->kind == TK_FIXED) DIGITOYENGINE_BOUNDS(idx, arr.t->fixedSize); vm_store((char *)arr.v.p + (size_t)idx * vm_elemsize(arr.t->elem), arr.t->elem, vm_coerce(F, v, arr.t->elem)); break; }
            int off; void *e = vm_array_elem((VmArray *)arr.v.p, F, rank, &off); F->sp--; vm_store(e, arr.t->elem, vm_coerce(F, v, arr.t->elem)); break;
        }
        case OP_AddrElement:
        {
            int rank = op->slot > 0 ? op->slot : 1; VmVal arr = F->st[F->sp - rank - 1]; LINE();
            if (arr.t->kind == TK_FIXED) { int idx = F->st[F->sp - 1].v.i; F->sp -= 2; DIGITOYENGINE_BOUNDS(idx, arr.t->fixedSize); PUSH(S_p((char *)arr.v.p + (size_t)idx * vm_elemsize(arr.t->elem)), vm_ptr_of(arr.t->elem)); break; }
            int off; void *e = vm_array_elem((VmArray *)arr.v.p, F, rank, &off); F->sp--; PUSH(S_p(e), vm_ptr_of(arr.t->elem)); break;
        }
        case OP_ArrayDataAddr:
        {
            VmVal arr = POP();
            if (arr.t == vm_string) { VmString *s = (VmString *)arr.v.p; PUSH(S_p(s ? (void *)s->data : 0), vm_ptr_of(vm_scalar_t('c'))); break; }
            if (arr.t->kind == TK_ARRAY) { VmArray *a = (VmArray *)arr.v.p; PUSH(S_p(a ? a->data : 0), vm_ptr_of(arr.t->elem)); break; }
            PUSH(S_p(arr.v.p), vm_ptr_of(arr.t->elem)); break;
        }
        case OP_ArrayLength: { VmVal arr = POP(); LINE(); VmArray *a = (VmArray *)vm_nullcheck((GCHeader *)arr.v.p); PUSH(S_i(a->len), vm_scalar_t('i')); break; }
        case OP_NewArray:
        {
            int rank = op->slot > 0 ? op->slot : 1; int dims[16]; if (rank > 16) vm_fail("dizi rank > 16");
            for (int i = rank - 1; i >= 0; i--) dims[i] = POP().v.i;
            VmT *et = op->prim; int isref = et->tag == 'o'; int es = vm_elemsize(et);
            VmArray *a = (et->tag == 'v' && et->nrefs > 0) ? vmarray_new_rank_t(rank, dims, (unsigned short)es, vm_struct_array_type(et)) : vmarray_new_rank(rank, dims, (unsigned short)es, isref);
            PUSH(S_p(a), vm_array_of(m->mod, et, rank)); break;
        }
        case OP_StackAlloc: { VmVal n = POP(); void *p = vm_temp(F, n.v.i); F->tempBase = vm_top; PUSH(S_p(p), vm_voidptr); break; } // frame omru: temp sifirlamasi bunun ustunden baslar
        case OP_SizeOf: PUSH(S_i(vm_elemsize(op->prim)), vm_scalar_t('i')); break;
        case OP_Conv: { VmVal v = POP(); VmT *t = op->prim; DeSlot r; if (vm_is_numeric(t) && vm_is_numeric(v.t)) r = vm_conv(v.v, v.t->tag, t->tag); else if (vm_is_numeric(t) && vm_is_ptr(v.t)) r = vm_conv(S_l((long long)(size_t)v.v.p), 'l', t->tag); else if (vm_is_ptr(t) && vm_is_numeric(v.t)) r = S_p((void *)(size_t)vm_as_i64(v.v, v.t->tag)); else r = v.v; PUSH(r, t); break; }
        case OP_IsType: { VmVal o = POP(); GCHeader *h = (GCHeader *)o.v.p; PUSH(S_i(h && vm_type_test(h->type, op->prim) ? 1 : 0), vm_scalar_t('i')); break; }
        case OP_IsValueType: { POP(); PUSH(S_i(vm_is_value(op->prim) ? 1 : 0), vm_scalar_t('B')); break; }
        case OP_AsType:
        {
            VmVal o = POP(); VmT *tt = op->prim;
            if (vm_is_value(o.t)) { if (tt == vm_object || (tt->type == &vmvaluetype_type)) PUSH(S_p(vm_box(F, o)), tt); else PUSH(S_p(0), tt); break; }
            GCHeader *h = (GCHeader *)o.v.p; int ok = h && vm_type_test(h->type, tt);
            if (vm_is_value(tt)) { PUSH(S_p(ok ? h : 0), vm_object); break; }
            PUSH(S_p(ok ? h : 0), tt); break;
        }
        case OP_CastClass:
        {
            VmVal o = POP(); VmT *tt = op->prim; GCHeader *h = (GCHeader *)o.v.p;
            if (tt->kind == TK_ARRAY) { PUSH(S_p(h), tt); break; }
            if (h && !vm_type_test(h->type, tt)) { LINE(); DIGITOYENGINE_cast_fail(h->type, tt->type); }
            PUSH(S_p(h), tt); break;
        }
        case OP_GenericCast: vm_fail("GenericCast op'u somutlastirilmamis IR'da");
        case OP_Default: { VmT *t = op->prim; if (t->tag == 'v') PUSH(S_p(vm_temp(F, t->size)), t); else PUSH(S_l(0), t); break; }
        case OP_TypeOf: { if (!op->prim->type) vm_fail("typeof: descriptor'suz tip"); PUSH(S_p(digitoyengine_type_wrapper(op->prim->type)), vm_prim_type(F)); break; }
        case OP_Box: { VmVal v = POP(); VmT *st = op->prim; if (st->tag == 'o') { PUSH(v.v, st); break; } VmVal bv = v; bv.t = st; PUSH(S_p(vm_box(F, bv)), vm_object); break; }
        case OP_Unbox: { LINE(); VmVal o = POP(); VmT *ut = op->prim; void *p = digitoyengine_unbox((GCHeader *)o.v.p, ut->type); DeSlot v = vm_load(p, ut); if (ut->tag == 'v') v = vm_copy_struct(F, v, ut); PUSH(v, ut); break; }
        case OP_UnboxOrDefault: { VmVal o = POP(); VmT *ut = op->prim; GCHeader *h = (GCHeader *)o.v.p; if (h && h->type == ut->type) { DeSlot v = vm_load((char *)h + sizeof(GCHeader), ut); if (ut->tag == 'v') v = vm_copy_struct(F, v, ut); PUSH(v, ut); } else if (ut->tag == 'v') PUSH(S_p(vm_temp(F, ut->size)), ut); else PUSH(S_l(0), ut); break; }
        case OP_NullableWrap: { VmVal v = POP(); PUSH(vm_coerce(F, v, op->prim), op->prim); break; }
        case OP_NullableHasValue: { VmVal v = POP(); if (!v.t->isNullable) vm_fail("NullableHasValue Nullable<T> ister"); PUSH(S_i(*(cil_int *)((char *)v.v.p + v.t->nullHasOff) ? 1 : 0), vm_scalar_t('B')); break; }
        case OP_NullableValue: { VmVal v = POP(); if (!v.t->isNullable) vm_fail("NullableValue Nullable<T> ister"); DeSlot r = vm_load((char *)v.v.p + v.t->nullValOff, v.t->nullValue); if (v.t->nullValue->tag == 'v') r = vm_copy_struct(F, r, v.t->nullValue); PUSH(r, v.t->nullValue); break; }
        case OP_NullableBinary:
        {
            VmVal rv = POP(), lv = POP(); const char *ot = vm_str_at(m->mod, op->val.str);
            int ln = lv.t->isNullable, rn = rv.t->isNullable;
            VmT *lt = ln ? lv.t->nullValue : lv.t, *rt = rn ? rv.t->nullValue : rv.t;
            int lp = ln ? *(cil_int *)((char *)lv.v.p + lv.t->nullHasOff) : lv.t->tag != 'V', rp = rn ? *(cil_int *)((char *)rv.v.p + rv.t->nullHasOff) : rv.t->tag != 'V';
            VmVal a, b; a.t = lt->tag == 'V' ? rt : lt; a.v = ln ? vm_load((char *)lv.v.p + lv.t->nullValOff, lt) : lv.v; b.t = rt->tag == 'V' ? lt : rt; b.v = rn ? vm_load((char *)rv.v.p + rv.t->nullValOff, rt) : rv.v;
            if (!strcmp(ot, "==")) { PUSH(S_i(((!lp && !rp) || (lp && rp && vm_cmp(OP_Ceq, a, b))) ? 1 : 0), vm_scalar_t('B')); break; }
            if (!strcmp(ot, "!=")) { PUSH(S_i(((lp != rp) || (lp && rp && vm_cmp(OP_Cne, a, b))) ? 1 : 0), vm_scalar_t('B')); break; }
            if (!strcmp(ot, "<") || !strcmp(ot, ">") || !strcmp(ot, "<=") || !strcmp(ot, ">=")) { unsigned char c = !strcmp(ot, "<") ? OP_Clt : !strcmp(ot, ">") ? OP_Cgt : !strcmp(ot, "<=") ? OP_Cle : OP_Cge; PUSH(S_i((lp && rp && vm_cmp(c, a, b)) ? 1 : 0), vm_scalar_t('B')); break; }
            unsigned char bop = !strcmp(ot, "+") ? OP_Add : !strcmp(ot, "-") ? OP_Sub : !strcmp(ot, "*") ? OP_Mul : !strcmp(ot, "/") ? OP_Div : !strcmp(ot, "%") ? OP_Mod : !strcmp(ot, "&") ? OP_And : !strcmp(ot, "|") ? OP_Or : OP_Xor;
            VmT *res = op->prim; char *d = (char *)vm_temp(F, res->size); *(cil_int *)(d + res->nullHasOff) = lp && rp;
            unsigned char g; DeSlot r = vm_numbin(bop, a.v, a.t->tag, b.v, b.t->tag, &g); vm_store(d + res->nullValOff, res->nullValue, vm_conv(r, g, res->nullValue->tag)); PUSH(S_p(d), res); break;
        }
        case OP_New:
        {
            VmT *t = op->prim;
            if (t->tag == 'v') { PUSH(S_p(vm_temp(F, t->size)), t); break; }
            if (!t->type) vm_fail("New: descriptor'suz tip");
            PUSH(S_p(gc_alloc(t->type)), t); break;
        }
        case OP_Dup: { VmVal v = POP(); if (v.t->tag == 'v') v.v = vm_copy_struct(F, v.v, v.t); if (v.t->tag == 'V') v.t = vm_object; PUSH(v.v, v.t); PUSH(v.v, v.t); break; }
        case OP_Pop: POP(); break;
        case OP_Push:
        {
            switch (op->vtag)
            {
            case 0: PUSH(S_l(0), vm_void); break; case 1: PUSH(S_i(op->val.i), vm_scalar_t('i')); break; case 2: PUSH(S_f(op->val.f), vm_scalar_t('f')); break;
            case 3: PUSH(S_d(op->val.d), vm_scalar_t('d')); break; case 4: PUSH(S_l(op->val.l), vm_scalar_t('l')); break; case 5: PUSH(S_p(vm_literal(m->mod, op->val.str)), vm_string); break;
            case 6: PUSH(S_i(op->val.i), vm_scalar_t('B')); break; case 7: PUSH(S_i(op->val.i), vm_scalar_t('c')); break; case 8: PUSH(S_i(op->val.i), vm_scalar_t('h')); break;
            case 9: PUSH(S_i(op->val.i), vm_scalar_t('b')); break; case 10: PUSH(S_i(op->val.i), vm_scalar_t('u')); break; case 11: PUSH(S_l(op->val.l), vm_scalar_t('q')); break;
            case 12: PUSH(S_i(op->val.i), vm_scalar_t('H')); break; default: PUSH(S_i(op->val.i), vm_scalar_t('z')); break;
            }
            break;
        }
        case OP_Return:
        {
            if (F->nopen > 0) DIGITOYENGINE_try_top = F->tries[F->openTries[0]].prev;
            if (m->ret->tag != 'V')
            {
                DeSlot r; r.l = 0;
                if (F->sp > 0) { VmVal v = POP(); r = vm_coerce(F, v, m->ret); }
                if (m->ret->tag == 'v') { if (r.p && ret->p) memcpy(ret->p, r.p, m->ret->size); } else *ret = r;
            }
            DIGITOYENGINE_POP(); vm_top = saveTop; return;
        }
        case OP_Call:
        {
            LINE(); VmMethod *c = op->code; DeSlot a[64]; if (c->nargs > 64) vm_fail("arguman > 64");
            for (int i = c->nargs - 1; i >= 0; i--) a[i] = vm_call_arg(F, POP(), &c->args[i]);
            DeSlot r = vm_prep_ret(F, c->ret); vm_invoke(F, c, a, &r); vm_push_ret(F, c->ret, r); break;
        }
        case OP_CallVirtual:
        {
            LINE(); VmMethod *c = op->code; int n = c->nargs; if (n > 64) vm_fail("arguman > 64");
            VmVal *recv = &F->st[F->sp - n];
            if (vm_is_ptr(recv->t) && recv->t->elem) { recv->v = vm_load(recv->v.p, recv->t->elem); recv->t = recv->t->elem; }
            if (vm_is_numeric(recv->t) && c->owner == vm_object)
            {
                if (!strcmp(c->name, "System.Object$GetHashCode") && n == 1) { VmVal hv = POP(); int hx; switch (hv.t->tag) { case 'l': case 'q': hx = digitoyengine_valhash_i64(hv.v.l); break; case 'f': hx = digitoyengine_valhash_f32(hv.v.f); break; case 'd': hx = digitoyengine_valhash_f64(hv.v.d); break; case 'c': hx = hv.v.i | (hv.v.i << 16); break; default: hx = hv.v.i; } PUSH(S_i(hx), vm_scalar_t('i')); break; }
                if (!strncmp(c->name, "System.Object$Equals", 20) && n == 2 && F->st[F->sp - 1].t == recv->t) { VmVal eb = POP(), ea = POP(); int ex = ea.t->tag == 'f' ? digitoyengine_valeq_f32(ea.v.f, eb.v.f) : ea.t->tag == 'd' ? digitoyengine_valeq_f64(ea.v.d, eb.v.d) : vm_cmp(OP_Ceq, ea, eb); PUSH(S_i(ex), vm_scalar_t('B')); break; }
                recv->v = S_p(vm_box(F, *recv)); recv->t = vm_object;
            }
            DeSlot a[64];
            for (int i = n - 1; i >= 1; i--) { VmVal v = POP(); VmArg *d = &c->args[i]; a[i] = ((d->isRef || d->isOut) && vm_is_ptr(v.t)) ? v.v : vm_coerce(F, v, d->type); }
            VmVal rv = POP(); a[0] = vm_coerce(F, rv, c->args[0].type);
            GCHeader *self = (GCHeader *)vm_nullcheck((GCHeader *)a[0].p);
            DeSlot r = vm_prep_ret(F, c->ret); vm_call_virtual(F, c, op->slot, self, a, &r); vm_push_ret(F, c->ret, r); break;
        }
        case OP_CallIndirect:
        {
            LINE(); VmT *dt = op->prim; int n = dt->ndparams; if (n > 62) vm_fail("delegate arguman > 62");
            DeSlot a[64]; VmArg tmpd; for (int i = n - 1; i >= 0; i--) { VmVal v = POP(); tmpd.type = dt->dparams[i]; tmpd.isRef = tmpd.isOut = 0; a[i] = vm_call_arg(F, v, &tmpd); }
            VmVal dv = POP(); VmDelegate *d = (VmDelegate *)vm_nullcheck((GCHeader *)dv.v.p);
            int cnt = digitoyengine_delegate_count(d); DeSlot r = vm_prep_ret(F, dt->dret);
            if (vm_trace_ops) fprintf(stderr, "[vmint]     CallIndirect %s cnt=%d fn=%p target=%p closure=%d marker=%p\n", dt->name, cnt, d->fn, (void *)d->target, vm_is_closure(d->target), (void *)vm_marker_get(d->fn));
            for (int i = 0; i < cnt; i++) { VmDelegate *leaf = digitoyengine_delegate_at(d, i); DeSlot args2[64]; memcpy(args2, a, sizeof(DeSlot) * (n + 1)); vm_call_delegate_leaf(F, dt, leaf, args2, &r); }
            vm_push_ret(F, dt->dret, r); break;
        }
        case OP_DelegateNew:
        {
            LINE(); VmT *dt = op->prim; VmMethod *tm = op->code; GCHeader *recv = 0;
            if (!tm->isStatic) { VmVal rv = POP(); recv = (GCHeader *)vm_coerce(F, rv, tm->args[0].type).p; vm_nullcheck(recv); }
            // sanal hedef: alicinin gercek impl'i
            VmMethod *impl = tm; const void *hostfn = tm->fn;
            if (op->slot >= 0 && recv) { VmT *lt = vm_local_of(recv->type); if (lt && lt->kind == TK_CLASS && op->slot < lt->nvtable && lt->vimpl[op->slot]) impl = lt->vimpl[op->slot]; else { hostfn = recv->type->vtable[op->slot]; VmMethod *lm = vm_marker_get(hostfn); if (lm) impl = lm; else impl = 0; } }
            if (impl && impl->local)
            {
                VmClosure *c = (VmClosure *)gc_alloc(&m->mod->closureType.type); c->target = recv; c->m = impl;
                const void *tr = dt->isLocal ? 0 : vm_dtramp_for(dt->name);
                PUSH(S_p(digitoyengine_delegate_new(dt->type, tr ? tr : (const void *)&impl->marker, (GCHeader *)c)), dt); break;
            }
            PUSH(S_p(digitoyengine_delegate_new(dt->type, hostfn, recv)), dt); break;
        }
        case OP_DelegateCombine: case OP_DelegateRemove: { VmVal r = POP(), l = POP(); VmDelegate *x = op->op == OP_DelegateCombine ? digitoyengine_delegate_combine((VmDelegate *)l.v.p, (VmDelegate *)r.v.p) : digitoyengine_delegate_remove((VmDelegate *)l.v.p, (VmDelegate *)r.v.p); PUSH(S_p(x), op->prim); break; }
        case OP_Br: vpc = op->slot; break;
        case OP_Brtrue: { VmVal c = POP(); if (vm_is_numeric(c.t) ? vm_as_i64(c.v, c.t->tag) != 0 : c.v.p != 0) vpc = op->slot; break; }
        case OP_Brfalse: { VmVal c = POP(); if (!(vm_is_numeric(c.t) ? vm_as_i64(c.v, c.t->tag) != 0 : c.v.p != 0)) vpc = op->slot; break; }
        case OP_TryBegin:
        {
            int k = op->vtag; if (k >= F->ntries) vm_fail("try indeksi");
            RtTry *t = &F->tries[k]; F->openTries[F->nopen++] = k; F->tryTop[k] = vm_top;
            t->sp = DIGITOYENGINE_sp; t->boundary = 0; t->prev = DIGITOYENGINE_try_top; DIGITOYENGINE_try_top = t;
            if (setjmp(t->buf))
            {
                // handler girisi. Yakalayan try: dispatch try_top'u t->prev'e cekti -> acik try'lar icinde prev'i buna esit olan en icteki.
                Frame *FF = F; int kk = -1;
                for (int q = FF->nopen - 1; q >= 0; q--) { int cand = FF->openTries[q]; if (FF->tries[cand].prev == DIGITOYENGINE_try_top) { kk = cand; FF->nopen = q; break; } }
                if (kk < 0) vm_fail("handler: yakalayan try bulunamadi");
                vm_top = FF->tryTop[kk]; FF->sp = 0;
                vpc = FF->m->tryHandler[kk];
            }
            break;
        }
        case OP_TryEnd: { int k = F->openTries[--F->nopen]; DIGITOYENGINE_try_top = F->tries[k].prev; break; }
        case OP_TryUnwind: { int k = F->openTries[F->nopen - op->slot]; DIGITOYENGINE_try_top = F->tries[k].prev; break; }
        case OP_Throw: { VmVal v = POP(); LINE(); DIGITOYENGINE_throw(DIGITOYENGINE_EX_USER, (GCHeader *)v.v.p); }
        case OP_Rethrow: DIGITOYENGINE_rethrow();
        case OP_ExIs: { GCHeader *e = DIGITOYENGINE_ex_current(); PUSH(S_i(e && vm_type_test(e->type, op->prim) ? 1 : 0), vm_scalar_t('i')); break; }
        case OP_ExBind: { GCHeader *e = DIGITOYENGINE_ex_current(); if (!e || !vm_type_test(e->type, op->prim)) DIGITOYENGINE_rethrow(); vm_bind_trace(e, op->prim); PUSH(S_p(e), op->prim); break; }
        case OP_Ceq: case OP_Cne: case OP_Cgt: case OP_Cge: case OP_Clt: case OP_Cle: { VmVal b = POP(), a = POP(); PUSH(S_i(vm_cmp(op->op, a, b) ? 1 : 0), vm_scalar_t('i')); break; }
        case OP_LoadInd:
        {
            VmVal p = POP(); VmT *pe = vm_is_ptr(p.t) ? p.t->elem : p.t; if (op->prim && pe != op->prim) pe = op->prim;
            DeSlot v = vm_load(p.v.p, pe); PUSH(v, pe); break; // struct: alias (C: *(p) lvalue)
        }
        case OP_StoreInd:
        {
            VmVal v = POP(), p = POP(); VmT *et = vm_is_ptr(p.t) ? p.t->elem : v.t; if (op->prim && et != op->prim) et = op->prim;
            vm_store(p.v.p, et, vm_coerce(F, v, et)); break;
        }
        case OP_Add: case OP_Sub: case OP_Mul: case OP_And: case OP_Or: case OP_Xor:
        {
            VmVal b = POP(), a = POP();
            if (vm_is_ptr(a.t)) { PUSH(S_p((char *)a.v.p + (op->op == OP_Sub ? -vm_as_i64(b.v, b.t->tag) : vm_as_i64(b.v, b.t->tag))), a.t); break; }
            if (vm_is_ptr(b.t) && op->op == OP_Add) { PUSH(S_p((char *)b.v.p + vm_as_i64(a.v, a.t->tag)), b.t); break; }
            unsigned char g; DeSlot r = vm_numbin(op->op, a.v, a.t->tag == 'V' ? 'i' : a.t->tag, b.v, b.t->tag == 'V' ? 'i' : b.t->tag, &g); PUSH(r, vm_scalar_t(g)); break;
        }
        case OP_Div: case OP_Mod:
        {
            VmVal b = POP(), a = POP(); unsigned char gb = b.t->tag;
            if (gb != 'f' && gb != 'd') { LINE(); if (vm_as_i64(b.v, gb) == 0) DIGITOYENGINE_throw_div(); }
            unsigned char g; DeSlot r = vm_numbin(op->op, a.v, a.t->tag, b.v, gb, &g); PUSH(r, vm_scalar_t(g)); break;
        }
        case OP_Neg: { VmVal a = POP(); unsigned char g = a.t->tag == 'u' ? 'l' : a.t->tag; DeSlot c = vm_conv(a.v, a.t->tag, g); DeSlot r; r.l = 0; switch (g) { case 'd': r.d = -c.d; break; case 'f': r.f = -c.f; break; case 'l': r.l = -c.l; break; case 'q': r.q = (cil_ulong)0 - c.q; break; default: r.i = (cil_int)(0u - (cil_uint)c.i); } PUSH(r, vm_scalar_t(g)); break; }
        case OP_Shl: case OP_Shr:
        {
            VmVal b = POP(), a = POP(); unsigned char g = a.t->tag == 'V' ? 'i' : a.t->tag; int sh = (int)vm_as_i64(b.v, b.t->tag); DeSlot r; r.l = 0;
            switch (g) { case 'l': r.l = op->op == OP_Shl ? a.v.l << (sh & 63) : a.v.l >> (sh & 63); break; case 'q': r.q = op->op == OP_Shl ? a.v.q << (sh & 63) : a.v.q >> (sh & 63); break; case 'u': case 'H': r.u = op->op == OP_Shl ? a.v.u << (sh & 31) : a.v.u >> (sh & 31); break; default: r.i = op->op == OP_Shl ? (cil_int)((cil_uint)a.v.i << (sh & 31)) : a.v.i >> (sh & 31); }
            PUSH(r, vm_scalar_t(g == 'H' ? 'i' : g)); break;
        }
        case OP_Not: { VmVal a = POP(); PUSH(S_i((vm_is_numeric(a.t) ? vm_as_i64(a.v, a.t->tag) == 0 : a.v.p == 0) ? 1 : 0), vm_scalar_t('i')); break; }
        case OP_Yield: vm_fail("Yield op'u");
        default: vm_fail("bilinmeyen opcode");
        }
    }
}

// ================= HOST GIRISLERI =================
// Her giris kendi boundary'sini kurar: sizan exception'da VM yigini geri alinir, yeniden firlatilir.
static void vm_enter(VmMethod *m, DeSlot *a, DeSlot *r)
{
    char *volatile top = vm_top;
    RtTry t; t.sp = DIGITOYENGINE_sp; t.boundary = 0; t.prev = DIGITOYENGINE_try_top; DIGITOYENGINE_try_top = &t;
    if (setjmp(t.buf)) { vm_top = top; DIGITOYENGINE_rethrow(); }
    vm_exec_frame(m, a, r);
    DIGITOYENGINE_try_top = t.prev;
}
void vmint_enter_virtual(GCHeader *self, unsigned long long rootHash, DeSlot *a, DeSlot *r)
{
    VmT *lt = vm_local_of(self->type);
    for (VmT *t = lt; t; t = t->parent)
    {
        if (!t->isLocal) break;
        for (int k = 0; k < t->nvslots; k++) if (t->vslots[k].decl && vm_hash(t->vslots[k].decl->name) == rootHash && t->vslots[k].impl->local) { vm_enter(t->vslots[k].impl, a, r); return; }
        for (int k = 0; k < t->nitables; k++) for (int j = 0; j < t->itables[k].n; j++) if (vm_hash(t->itables[k].imethod[j]->name) == rootHash && t->itables[k].impl[j]->local) { vm_enter(t->itables[k].impl[j], a, r); return; }
    }
    vm_fail("vmint_enter_virtual: impl bulunamadi");
}
void vmint_enter_delegate(GCHeader *closure, DeSlot *a, DeSlot *r)
{
    VmClosure *c = (VmClosure *)closure; VmMethod *m = c->m;
    if (m->mod->mod.state == 2) return;
    if (c->target) { DeSlot b[65]; b[0].p = c->target; memcpy(b + 1, a, sizeof(DeSlot) * (m->nargs > 0 ? m->nargs - 1 : 0)); vm_enter(m, b, r); }
    else vm_enter(m, a, r);
}

// cctor sirasi (CTranspiler.EmitInit ile ayni kural): bir cctor baska tipin statik alanina (dogrudan ya da cagirdigi
// yerel statik metotlar uzerinden) dokunuyorsa o tipin cctor'u ONCE kosar; dongu -> kayit sirasi.
static VmMethod *vm_cctor_of(VmModule *m, VmT *t) { for (int i = 0; i < m->nmethods; i++) { VmMethod *me = m->methods[i]; if (me->local && me->owner == t) { const char *d = strrchr(me->name, '$'); if (d && !strcmp(d, "$cctor")) return me; } } return 0; }
static void vm_run_cctor(VmModule *m, VmT *t, unsigned char *state /* 0 yok 1 suruyor 2 bitti */)
{
    int ti = -1; for (int i = 0; i < m->ntypes; i++) if (m->types[i] == t) { ti = i; break; }
    if (ti < 0 || state[ti]) return;
    VmMethod *cc = vm_cctor_of(m, t); if (!cc) { state[ti] = 2; return; }
    state[ti] = 1;
    // bagimliliklar: op grafi (yerel statik cagrilar uzerinden), ziyaret kumesi metot isaretcisi
    VmMethod *stack[256]; int sp = 0; VmMethod *seen[1024]; int nseen = 0; stack[sp++] = cc;
    while (sp > 0)
    {
        VmMethod *c = stack[--sp]; int dup = 0; for (int i = 0; i < nseen; i++) if (seen[i] == c) { dup = 1; break; } if (dup) continue; if (nseen < 1024) seen[nseen++] = c;
        for (int i = 0; i < c->nops; i++)
        {
            VmOp *o = &c->ops[i];
            if (o->field && (o->op == OP_GetStatic || o->op == OP_SetStatic || o->op == OP_AddrStatic) && o->field->owner && o->field->owner != t && o->field->owner->isLocal) vm_run_cctor(m, o->field->owner, state);
            if (o->code && o->code->local && o->code->isStatic && o->code != cc && sp < 256) { const char *d = strrchr(o->code->name, '$'); if (!(d && !strcmp(d, "$cctor"))) stack[sp++] = o->code; }
        }
    }
    DeSlot rr; rr.l = 0; vm_exec_frame(cc, 0, &rr);
    state[ti] = 2;
}
VmModule *vmint_load(const unsigned char *data, int len, char *err, int errcap)
{
    if (!vm_base) { vm_base = (char *)malloc(VM_STACK_BYTES); vm_top = vm_base; vm_end = vm_base + VM_STACK_BYTES; }
    VmModule *m = (VmModule *)vm_zalloc(sizeof(VmModule)); m->mod.state = 0; m->name = "module";
    LoadCtx L = { m, err, errcap, 0 };
    Rd r = { data, data + len, 0 };
    m->next = vm_modules; vm_modules = m; // vm_prim_type vb. icin erken kayit
    if (!vm_parse(&L, &r) || !vm_link(&L)) { vm_modules = m->next; return 0; }
    // cctor'lar: dosya sirasiyla (ModuleWriter: tip kayit sirasi). Kosarken exception -> yukleme hatasi.
    volatile int ok = 1; RtTry t; t.sp = DIGITOYENGINE_sp; t.boundary = 0; t.prev = DIGITOYENGINE_try_top; DIGITOYENGINE_try_top = &t;
    if (setjmp(t.buf)) { ok = 0; vm_report_exception("statik ctor"); }
    else { unsigned char *st = (unsigned char *)vm_zalloc(m->ntypes + 1); for (int i = 0; i < m->ntypes; i++) if (m->types[i]->isLocal) vm_run_cctor(m, m->types[i], st); free(st); }
    DIGITOYENGINE_try_top = t.prev;
    if (!ok) { ld_err(&L, "statik ctor exception firlatti", 0, 0); vm_modules = m->next; return 0; }
    m->mod.state = 1;
    return m;
}
void vmint_unload(VmModule *m)
{
    if (!m || m->mod.state == 2) return;
    m->mod.state = 2;
    for (int i = 0; i < m->nroots; i++) gc_remove_root(m->roots[i]); // literaller: canli nesneler tutuyorsa onlar uzerinden yasar
    m->nroots = 0;
    // Type.name/MethodInfo.name stringleri (m->names) descriptor'lar serbest kalana kadar koklu kalir (vm_free_module)
}
static void vm_free_module(VmModule *m)
{
    for (int i = 0; i < m->ntypes; i++) { VmT *t = m->types[i]; if (t->kind == TK_SCALAR && vm_scalar[t->tag] == t) continue; if (t->ltype && t->ltype->type.dyn_wrapper) gc_remove_root(t->ltype->type.dyn_wrapper); }
    for (int i = 0; i < m->nnames; i++) gc_remove_root((GCHeader *)m->names[i]);
    // VmModule govdesi kalir (statics trace kaydi state'e bakar); buyuk bloklar serbest
    free(m->strblob); free(m->strs); free(m->literals); free(m->fields); free(m->roots); free(m->names);
    m->strs = 0; m->literals = 0; m->fields = 0; m->roots = 0; m->names = 0; m->nnames = 0;
    m->mod.state = 3;
}
int vmint_collect(void)
{
    int n = 0; VmModule **pp = &vm_modules;
    while (*pp) { VmModule *m = *pp; if (m->mod.state == 2 && m->mod.live == 0) { *pp = m->next; vm_free_module(m); n++; } else pp = &m->next; }
    return n;
}
int vmint_run_entry(VmModule *m)
{
    if (!m || m->entry < 0) return -1;
    DeSlot r; r.l = 0; vm_enter(m->methods[m->entry], 0, &r); return 0;
}
int vmint_call(VmModule *m, const char *encodedName, DeSlot *args, DeSlot *ret)
{
    for (int i = 0; i < m->nmethods; i++) if (m->methods[i]->local && !strcmp(m->methods[i]->name, encodedName)) { DeSlot r; r.l = 0; vm_enter(m->methods[i], args, ret ? ret : &r); return 1; }
    return 0;
}
int vmint_call_obj(VmModule *m, const char *encodedName, void *arg0)
{
    DeSlot a[1], r; a[0].p = arg0; r.l = 0;
    return vmint_call(m, encodedName, a, &r);
}
void vmint_set_name(VmModule *m, const char *name) { if (m && name) { char *c = (char *)malloc(strlen(name) + 1); strcpy(c, name); m->name = c; } }
int vmint_live(VmModule *m) { return m ? m->mod.live : 0; }
const char *vmint_name(VmModule *m) { return m ? m->name : ""; }
