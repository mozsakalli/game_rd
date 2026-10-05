#ifndef VMRT_H
#define VMRT_H
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <setjmp.h>
#include <stddef.h>
#include <limits.h>
#include <math.h> // uretilen kod: float % -> fmodf/fmod
typedef struct GCHeader GCHeader;
typedef struct Type Type;
typedef struct DigitoyEngineMember DigitoyEngineMember;
struct VmString; // runtime adlari (Type.name/MethodInfo.name) UTF-16 VmString'e isaret eder
typedef void (*TraceFn)(GCHeader *);
typedef void (*FrameTrace)(void *);
typedef void (*FinalizeFn)(GCHeader *);
typedef GCHeader *(*ReflectGetFn)(GCHeader *target);
typedef void (*ReflectSetFn)(GCHeader *target, GCHeader *value);
typedef int (*EnumParseFn)(struct VmString *value, int *result);
enum
{
    DIGITOYENGINE_MEMBER_FIELD = 1,
    DIGITOYENGINE_MEMBER_PROPERTY = 2
};
struct DigitoyEngineMember
{
    const struct VmString *name;
    const Type *declaringType;
    const Type *valueType;
    ReflectGetFn get;
    ReflectSetFn set;  /* 0 = readonly/write-only unsupported */
    GCHeader *wrapper; /* lazy, rooted FieldInfo/PropertyInfo cache */
    unsigned char kind;
    unsigned char isStatic;
    unsigned char isInitOnly;
};
// interface implementasyon kaydi: iface kimligi (benzersiz sembol adresi) -> method tablosu
typedef struct IfaceImpl
{
    const void *iface;        // &IFoo_iface (adres = kimlik)
    const void *const *table; // iface method tablosu (slot -> fn ptr; cagride imzaya cast edilir)
} IfaceImpl;
// PAYLASIMLI tip descriptor: trace/finalize/size/atomic burada tutulur (her nesnede DEGIL)
// Alanlar APPEND-ONLY buyur: eski pozisyonel init'ler (vmarray_*) trailing zero-init ile gecerli kalir.
struct Type
{
    TraceFn trace;
    FinalizeFn finalize;
    unsigned short size;
    unsigned char atomic;
    const Type *base;             // ust tip (kalitim zinciri); root icin 0. is/as bu zinciri yurur.
    const struct VmString *name;  // RTTI/debug adi (strpool'da GC_IMMORTAL; ToString dogrudan dondurur)
    const void *const *vtable;    // sanal method tablosu (slot -> fn ptr, void* olarak; cagride imzaya cast edilir). yoksa 0
    unsigned short nvtable;       // vtable slot sayisi (0 = sanal method yok)
    const IfaceImpl *itables;     // implement edilen interface'ler (DUZLESTIRILMIS: base'inkiler dahil). yoksa 0
    unsigned short nitables;      // itables kayit sayisi
    unsigned short tindex;        // reflection wrapper indeksi (0 = wrapper yok; digitoyengine_type_wrapper)
    const Type *alias;            // unbox denkligi: enum descriptor'inda underlying tip (int); yoksa 0
    DigitoyEngineMember *members; // AOT reflection metadata (yalniz bildirilen field/property'ler)
    unsigned short nmembers;
    const Type *const *generic_args; // kapali generic type arguman descriptor'lari
    unsigned short ngeneric_args;
    EnumParseFn enum_parse; // enum adi -> underlying int; enum disinda 0
};
// nesne header'i artik sadece: type ptr + next + version + age + idhash  (padding'e sigar, 24 byte)
struct GCHeader
{
    const Type *type;
    GCHeader *next;
    unsigned char version;
    unsigned char age;
    int idhash; // kimlik hash'i (ilk hashCode'da atanir, GC boyunca stabil); 0 = henuz atanmadi
};
#define GC_TENURE 3
// age sentinel: static const (interned string vb.) nesne -> GC hic dokunmaz.
// Bu nesneler gc_young/gc_old listelerinde DEGIL (sweep gormez) + gc_shade onlari erken atlar.
#define GC_IMMORTAL 255
// is/as: nesnenin runtime tipi (t) hedef tipi (target) kalitim zincirinde tasiyor mu?
static inline int DIGITOYENGINE_is(const Type *t, const Type *target)
{
    while (t)
    {
        if (t == target)
            return 1;
        t = t->base;
    }
    return 0;
}
// iface is/as: tip iface'i implement ediyor mu? (itables duzlestirilmis, zincir yurumek gerekmez)
static inline int DIGITOYENGINE_implements(const Type *t, const void *iface)
{
    for (int i = 0; i < t->nitables; i++)
        if (t->itables[i].iface == iface)
            return 1;
    return 0;
}
// iface dispatch: tipin bu iface icin method tablosu (yoksa 0)
#ifdef __GNUC__
#define DIGITOYENGINE_ITBL_NORETURN __attribute__((noreturn))
#else
#define DIGITOYENGINE_ITBL_NORETURN
#endif
DIGITOYENGINE_ITBL_NORETURN void DIGITOYENGINE_cast_fail(const Type *from, const Type *to); // ileri bildirim (asagida da var)
static inline const void *const *DIGITOYENGINE_itable(const Type *t, const void *iface)
{
    for (int i = 0; i < t->nitables; i++)
        if (t->itables[i].iface == iface)
            return t->itables[i].table;
    DIGITOYENGINE_cast_fail(t, (const Type *)iface); // arayuzu implement etmeyen tip: InvalidCast (NULL deref/segfault yerine)
    return 0;
}

// ---- M8 shadow stack: exception/crash'te KESIN .cs stack trace (release dahil) ----
// Method girisinde push, cikista pop; satir SADECE firlatabilen op'lardan once yazilir (sabit store).
// frame: coroutine frame ptr (debug protokolu yerel degiskenleri buradan okur; duz fonksiyonda 0).
typedef struct MethodInfo
{
    const struct VmString *name; // gosterim adi (strpool'da; trace UTF-16 basar)
    const struct VmString *file; // kaynak dosya (strpool'da; 0 = dosyasiz IR). Debugger file:line eslesmesi buradan.
    int trypc_off;               // coroutine frame'inde __trypc offseti (-1 = try'siz / duz fonksiyon)
} MethodInfo;
#ifdef DIGITOYENGINE_DEBUG
// debugger local/arg tablosu: uretilen kod fonksiyon girisinde kurar (adresler o cagriya ait).
// name = KAYNAK adi (ASCII; protokol ciktisi), tag = tip etiketi (i/f/d/l/c/h/b/B/o/r/v/?), addr = C degiskeni.
typedef struct RtLocal
{
    const char *name;
    char tag;
    void *addr;
} RtLocal;
#endif
typedef struct RtFrame
{
    const MethodInfo *mi;
    int line;
    void *frame;
#ifdef DIGITOYENGINE_DEBUG
    const RtLocal *dbg_locals; // aktif cagrinin local/arg tablosu (DIGITOYENGINE_DBG_LOCALS kurar)
    int dbg_nlocals;
#endif
} RtFrame;
// max ozyineleme derinligi (frame sayisi). Asilinca DIGITOYENGINE_stack_overflow -> rapor + cikis.
#define DIGITOYENGINE_STACK_MAX 16384
extern RtFrame DIGITOYENGINE_stack[DIGITOYENGINE_STACK_MAX];
extern int DIGITOYENGINE_sp;
void DIGITOYENGINE_dump_stack(void);
#ifdef DIGITOYENGINE_NOTRACE // benchmark build: izleme tamamen kapali
#define DIGITOYENGINE_PUSH(m, fr) ((void)0)
#define DIGITOYENGINE_LINE(n) ((void)0)
#define DIGITOYENGINE_POP() ((void)0)
#define DIGITOYENGINE_STEP(n) ((void)0)
#define DIGITOYENGINE_DBG_LOCALS(t, n) ((void)0)
#elif defined(DIGITOYENGINE_DEBUG)
// debug build: her statement'ta durma noktasi + local tablosu. Release ciktisi TEK kaynak,
// bu makrolar orada bosa acilir (eski derleyicinin cift codegen'i YOK).
extern void (*DIGITOYENGINE_dbg_step)(int line); // debugger kancasi (0 = takili degil)
#define DIGITOYENGINE_PUSH(m, fr) (DIGITOYENGINE_UNLIKELY(DIGITOYENGINE_sp >= DIGITOYENGINE_STACK_MAX) ? DIGITOYENGINE_stack_overflow() : (void)(DIGITOYENGINE_stack[DIGITOYENGINE_sp].mi = (m), DIGITOYENGINE_stack[DIGITOYENGINE_sp].line = 0, DIGITOYENGINE_stack[DIGITOYENGINE_sp].frame = (fr), DIGITOYENGINE_stack[DIGITOYENGINE_sp].dbg_locals = 0, DIGITOYENGINE_stack[DIGITOYENGINE_sp].dbg_nlocals = 0, DIGITOYENGINE_sp++))
#define DIGITOYENGINE_LINE(n) (DIGITOYENGINE_stack[DIGITOYENGINE_sp - 1].line = (n))
#define DIGITOYENGINE_POP() (DIGITOYENGINE_sp--)
#define DIGITOYENGINE_STEP(n) (DIGITOYENGINE_stack[DIGITOYENGINE_sp - 1].line = (n), DIGITOYENGINE_dbg_step ? DIGITOYENGINE_dbg_step(n) : (void)0)
#define DIGITOYENGINE_DBG_LOCALS(t, n) (DIGITOYENGINE_stack[DIGITOYENGINE_sp - 1].dbg_locals = (t), DIGITOYENGINE_stack[DIGITOYENGINE_sp - 1].dbg_nlocals = (n))
#else
#define DIGITOYENGINE_PUSH(m, fr) (DIGITOYENGINE_UNLIKELY(DIGITOYENGINE_sp >= DIGITOYENGINE_STACK_MAX) ? DIGITOYENGINE_stack_overflow() : (void)(DIGITOYENGINE_stack[DIGITOYENGINE_sp].mi = (m), DIGITOYENGINE_stack[DIGITOYENGINE_sp].line = 0, DIGITOYENGINE_stack[DIGITOYENGINE_sp].frame = (fr), DIGITOYENGINE_sp++))
#define DIGITOYENGINE_LINE(n) (DIGITOYENGINE_stack[DIGITOYENGINE_sp - 1].line = (n))
#define DIGITOYENGINE_POP() (DIGITOYENGINE_sp--)
#define DIGITOYENGINE_STEP(n) ((void)0)
#define DIGITOYENGINE_DBG_LOCALS(t, n) ((void)0)
#endif

// ---- M8b kontroller: null/bounds/div. Tek C ciktisi, platform farki makrolarda. ----
#if defined(__GNUC__) || defined(__clang__)
#define DIGITOYENGINE_UNLIKELY(x) __builtin_expect(!!(x), 0)
#define DIGITOYENGINE_NORETURN __attribute__((noreturn, cold))
#else
#define DIGITOYENGINE_UNLIKELY(x) (x)
#define DIGITOYENGINE_NORETURN
#endif
// ---- M8b-2 exception: setjmp try'da (nadir), throw'da longjmp + shadow-stack restore ----
typedef struct RtTry
{
    jmp_buf buf;
    int sp;             // DIGITOYENGINE_sp restore (shadow stack disiplini)
    int boundary;       // 1 = coroutine resume siniri: pc-redirect sonrasi yeniden giris buradan
    struct RtTry *prev; // ic ice try zinciri
} RtTry;
extern RtTry *DIGITOYENGINE_try_top;
#define DIGITOYENGINE_EX_NULL 1
#define DIGITOYENGINE_EX_BOUNDS 2
#define DIGITOYENGINE_EX_DIV 3
#define DIGITOYENGINE_EX_CAST 4
#define DIGITOYENGINE_EX_USER 5
#define DIGITOYENGINE_EX_IO 6
extern int DIGITOYENGINE_ex_kind;
extern GCHeader *DIGITOYENGINE_ex_obj; // user throw nesnesi (catch bind'e kadar; arada gc_alloc yok -> guvenli)
extern char DIGITOYENGINE_ex_msg[160];
// runtime hatalari (null/bounds/div/cast): firlatma alloc'suz (kind + mesaj); exception NESNESI ilk catch'te
// TAZE uretilir (DIGITOYENGINE_ex_current -> kind tipi + ctor; digitoyengine_init doldurur). Singleton yok:
// ayni nesnenin yeniden kullanimi izi eziyor, orijinal firlatma noktasini kaybettiriyordu.
extern const Type *DIGITOYENGINE_ex_kind_type[8];
extern void (*DIGITOYENGINE_ex_kind_ctor[8])(GCHeader *);
GCHeader *DIGITOYENGINE_ex_current(void); // aktif exception nesnesi (gerekirse kind'dan uretir ve DIGITOYENGINE_ex_obj'a baglar)
// AOT stub (cevrilemeyen govde) ilk cagrida: NotImplementedException(mesaj). Tip/ctor init'te baglanir; yoksa mesajli abort.
extern const Type *DIGITOYENGINE_notimpl_type;
extern void (*DIGITOYENGINE_exception_ctor_msg)(GCHeader *, struct VmString *);
DIGITOYENGINE_NORETURN void DIGITOYENGINE_throw_notimpl(const char *what);
// catch bind: firlatma ani trace'ini exception nesnesinin gomulu (fixed) tamponlarina kopyalar
void DIGITOYENGINE_bind_trace(long long *miArr, int *lineArr, int *count, int cap);
// M9c: firlatma ANINDAKI trace kopyasi (mi+line; frame ptr'lar unwind sonrasi bayat, kullanma).
// DIGITOYENGINE_rethrow KORUR (C# `throw;` gibi) -> rapor hep orijinal firlatma noktasini gosterir.
extern RtFrame DIGITOYENGINE_ex_trace[DIGITOYENGINE_STACK_MAX];
extern int DIGITOYENGINE_ex_trace_n;
DIGITOYENGINE_NORETURN void DIGITOYENGINE_throw(int kind, GCHeader *obj);
DIGITOYENGINE_NORETURN void DIGITOYENGINE_rethrow(void);
DIGITOYENGINE_NORETURN void DIGITOYENGINE_throw_null(void);
DIGITOYENGINE_NORETURN void DIGITOYENGINE_throw_bounds(int i, int n);
DIGITOYENGINE_NORETURN void DIGITOYENGINE_throw_div(void);
DIGITOYENGINE_NORETURN void DIGITOYENGINE_stack_overflow(void);                        // shadow-stack asildi (max ozyineleme)
DIGITOYENGINE_NORETURN void DIGITOYENGINE_cast_fail(const Type *from, const Type *to); // InvalidCast (adlar Type'tan)
DIGITOYENGINE_NORETURN void DIGITOYENGINE_throw_io(const char *msg);                   // IOError (v1: yakalanamaz, mesajli abort)
// wasm: linear memory'de adres 0 GECERLI -> null MMU ile yakalanamaz, explicit check sart.
// native (x86/ARM): sayfa 0 unmapped -> donanim yakalar (M8c handler) -> makro bos, SIFIR maliyet.
#if defined(__EMSCRIPTEN__)
#define DIGITOYENGINE_NULLCHECK(o) ((void)(DIGITOYENGINE_UNLIKELY((o) == 0) && (DIGITOYENGINE_throw_null(), 0)))
#else
#define DIGITOYENGINE_NULLCHECK(o) ((void)0)
#endif
// tek unsigned karsilastirma i<0 VE i>=n'i birden yakalar; branch hic alinmaz (tahminci %100)
#define DIGITOYENGINE_BOUNDS(i, n) ((void)(DIGITOYENGINE_UNLIKELY((unsigned)(i) >= (unsigned)(n)) && (DIGITOYENGINE_throw_bounds((i), (n)), 0)))
// ARM int div trap etmez, wasm trap'i kurtarilamaz -> explicit tek dogru ortak yol (bolme zaten pahali)
#define DIGITOYENGINE_DIVCHECK(d) ((void)(DIGITOYENGINE_UNLIKELY((d) == 0) && (DIGITOYENGINE_throw_div(), 0)))
void *gc_alloc(const Type *t);
void gc_shade(GCHeader *o);
void gc_write_barrier(GCHeader *obj, GCHeader *val); // tri-color: MARK fazinda val grilenir (obj yok sayilir)
int gc_hashcode(GCHeader *o); // type->hashcode varsa onu, yoksa header'daki stabil idhash
void gc_add_root(GCHeader *o);
void gc_remove_root(GCHeader *o);
void gc_add_frame_root(void *f, FrameTrace t);
// ---- reflection cekirdegi: Type descriptor -> System.Type wrapper (lazy, immortal-koklu, kimlik esitligi)
void digitoyengine_reflect_init(const Type *typeType, const Type *fieldInfoType, const Type *propertyInfoType, int nwrappers); // digitoyengine_init cagirir
GCHeader *digitoyengine_type_wrapper(const Type *t);                                                                           // tindex 0 ise 0 doner (dizi tipleri vb.)
GCHeader *digitoyengine_member_lookup(const Type *t, const struct VmString *name, int kind);
GCHeader *digitoyengine_member_get(DigitoyEngineMember *member, GCHeader *target);
void digitoyengine_member_set(DigitoyEngineMember *member, GCHeader *target, GCHeader *value);
GCHeader *digitoyengine_reflect_ref(GCHeader *value, const Type *target);
GCHeader *digitoyengine_reflect_unsupported(void);
void gc_minor(void);
void gc_major(void);
int gc_major_step(int budget);
int gc_maybe_major(int budget);
int gc_count_young(void);
int gc_count_old(void);
extern int gc_blocks;

// ---- System.Object koku: header-only. base'siz tum class'larin Type.base'i &vmobject_type olur
// (DIGITOYENGINE_is zinciri object'e ulasir). `object` degiskeni = VmObject* -> ->gc.type her model gibi calisir.
typedef struct VmObject
{
    GCHeader gc;
} VmObject;
extern const Type vmobject_type;
// object'in sanal methodlari (slot sozlesmesi corelib/Object.cs bildirim sirasi: 0=GetHashCode,
// 1=Equals, 2=ToString). Icerik digitoyengine_init'te transpile edilen impl adresleriyle doldurulur.
extern const void *vmobject_vtable[3];

// ---- System.String cekirdegi: DEGISMEZ UTF-16 (C# uyumlu). Literaller strpool'da GC_IMMORTAL.
// data ayri malloc'lu tampon (finalize free eder); atomic tip (GC izlemez).
// String'in C# yuzeyi (Length/Substring/IndexOf/...) corelib/String.cs'tedir (NativeBody);
// burada yalniz MEKANIZMA kalir: tahsis, C-string koprusu, sicak inline'lar, UTF-8 cikis.
typedef struct VmString
{
    GCHeader gc;
    int length; // UTF-16 kod birimi sayisi (C# String.Length)
    const unsigned short *data;
} VmString;
extern const Type vmstring_type;
extern const void *vmstring_vtable[3]; // String override'lari (digitoyengine_init doldurur; slotlar vmobject_vtable ile ayni)
// ---- primitive tip descriptor'lari (reflection + boxing: kimlik, ad, box vtable/boyut) ----
// tindex rezervasyonu: 1=object 2=string 3=ValueType 4..15=primitive 16=Enum 17=Delegate 18=MulticastDelegate; uretilen tipler 19'dan baslar
extern const Type vmvaluetype_type; // typeof(int).BaseType == System.ValueType (C# zinciri)
extern const Type vmenum_type;      // typeof(MyEnum).BaseType == System.Enum (descriptor'lar uretilir)
extern const Type vmint32_type, vmuint32_type, vmint64_type, vmuint64_type;
extern const Type vmint16_type, vmuint16_type, vmsbyte_type, vmbyte_type;
extern const Type vmchar_type, vmbool_type, vmsingle_type, vmdouble_type;
// box vtable'lari (corelib.c; slot sozlesmesi Object.cs: 0=GetHashCode 1=Equals 2=ToString)
extern const void *vmbool_vtable[3], *vmchar_vtable[3], *vmsbyte_vtable[3], *vmbyte_vtable[3];
extern const void *vmint16_vtable[3], *vmuint16_vtable[3], *vmint32_vtable[3], *vmuint32_vtable[3];
extern const void *vmint64_vtable[3], *vmuint64_vtable[3], *vmsingle_vtable[3], *vmdouble_vtable[3];
// ---- boxing: [GCHeader][deger] (NativeAOT yerlesimi). Kucuk degerler immortal cache'ten
// (SIFIR alloc; kimlik farki C#'a gore: ReferenceEquals((object)1,(object)1) bizde true - spec izinli).
void digitoyengine_box_init(void); // cache blogu; digitoyengine_init cagirir
VmObject *digitoyengine_box_bool(int v);
VmObject *digitoyengine_box_char(char v);
VmObject *digitoyengine_box_i8(signed char v);
VmObject *digitoyengine_box_u8(unsigned char v);
VmObject *digitoyengine_box_i16(short v);
VmObject *digitoyengine_box_u16(unsigned short v);
VmObject *digitoyengine_box_i32(int v);
VmObject *digitoyengine_box_u32(unsigned int v);
VmObject *digitoyengine_box_i64(long long v);
VmObject *digitoyengine_box_u64(unsigned long long v);
VmObject *digitoyengine_box_f32(float v);
VmObject *digitoyengine_box_f64(double v);
VmObject *digitoyengine_box_enum(int v, const Type *t); // enum kutusu (tip kimligi enum descriptor'i; cache'siz)
VmObject *digitoyengine_box_struct(const Type *t, const void *src, int size); // kullanici struct kutusu: payload kopyasi (t->size = header + struct)
int digitoyengine_enumbox_hash(VmObject *s);            // ortak enum vtable govdeleri (ToString uretilir)
int digitoyengine_enumbox_eq(VmObject *s, VmObject *o);
VmString *digitoyengine_int_str(int v); // uretilen enum ToString'in tanimsiz-deger dali kullanir// kisitsiz T'nin deger-tipi somutlamalari: boxing'siz GetHashCode/Equals (dotnet birebir; corelib.c)
int digitoyengine_valhash_i64(long long v);
int digitoyengine_valhash_f32(float v);
int digitoyengine_valhash_f64(double v);
int digitoyengine_valeq_f32(float a, float b);
int digitoyengine_valeq_f64(double a, double b);
void *digitoyengine_unbox(GCHeader *o, const Type *t); // exact tip (+enum<->underlying alias denkligi); null->NullRef, uyusmazlik->InvalidCast; payload ptr
// ---- delegate: leaf method referansi ya da immutable combine/remove invocation-list dugumu.
// target=0 static; instance'ta fn cagri yerinde target ilk arguman olarak gecer (runtime dali).
typedef struct VmDelegate
{
    GCHeader gc;
    const void *fn;
    GCHeader *target;
    struct VmDelegate *left;
    struct VmDelegate *right;
    int skip_start;
    int skip_count;
} VmDelegate;
extern const Type vmdelegate_type;          // System.Delegate (soyut zincir halkasi)
extern const Type vmmulticastdelegate_type; // System.MulticastDelegate (uretilen delegate'lerin base'i - C# zinciri)
void digitoyengine_delegate_trace(GCHeader *o);
VmDelegate *digitoyengine_delegate_new(const Type *t, const void *fn, GCHeader *target);
VmDelegate *digitoyengine_delegate_combine(VmDelegate *left, VmDelegate *right);
VmDelegate *digitoyengine_delegate_remove(VmDelegate *left, VmDelegate *right);
int digitoyengine_delegate_count(VmDelegate *delegate);
VmDelegate *digitoyengine_delegate_at(VmDelegate *delegate, int index);
VmString *vmstring_alloc(int len);                                // kurulum icin data yazilabilir tahsis edilir, sonrasi degismez
VmString *vmstring_from_cstr(const char *ascii);                  // C-string koprusu (host/dis dunya; sicak yolda KULLANMA)
char *digitoyengine_to_utf8(const VmString *s);                   // P/Invoke string arg: malloc'lu UTF-8 (NULL -> NULL); cagiran free eder
VmString *digitoyengine_from_utf8(const char *s);                 // P/Invoke string donus: UTF-8 -> VmString (NULL -> NULL)
void vm_write_utf8(const unsigned short *d, int len);             // UTF-16 -> UTF-8 stdout (surrogate'li)
void vm_write_utf8_to(FILE *f, const unsigned short *d, int len); // ayni donusum, verilen stream'e (stderr raporlari)
// ---- crash dump: host baslangicta yazilabilir dizini verir; her crash BENZERSIZ dosyaya
// yazilir (time+pid -> ezme yok). Yazim low-level open/write (signal-safe, malloc yok).
void digitoyengine_crash_init(const char *dir);
void DIGITOYENGINE_crash_dump(const char *reason, const RtFrame *frames, int count);
// sicak yol: static inline (TU basina kopya) -> cagri yerinde acilir. Uretilen extern prototip
// (String_ext.h) static'ten SONRA gelir -> C99 6.2.2p4 ile ic baglantiyi devralir (Mach-O duplicate cozumu).
static inline int vmstring_length(VmString *s)
{
    DIGITOYENGINE_NULLCHECK(s);
    return s->length;
}
static inline int vmstring_get(VmString *s, int i)
{
    DIGITOYENGINE_NULLCHECK(s);
    DIGITOYENGINE_BOUNDS(i, s->length);
    return s->data[i];
}
static inline int vmstring_eq(VmString *a, VmString *b)
{
    if (a == b)
        return 1;
    if (!a || !b || a->length != b->length)
        return 0;
    return memcmp(a->data, b->data, (size_t)a->length * 2) == 0;
}

// ---- Array: GC'li dinamik/rectangular dizi. Veri row-major tek tampon, dims runtime'a aittir.
// isref=1 ise elemanlar GC pointer'i -> trace her elemani shade eder. finalize tamponu free eder.
typedef struct VmArray
{
    GCHeader gc;
    int len;
    unsigned short elemsize;
    void *data;
    unsigned short rank;
    int *dims;
} VmArray;
extern const Type vmarray_ref_type; // eleman = GC pointer (izlenir)
extern const Type vmarray_val_type; // eleman = deger tipi (atomic)
VmArray *vmarray_new(int len, unsigned short elemsize, int isref);
VmArray *vmarray_new_rank(int rank, const int *dims, unsigned short elemsize, int isref);
VmArray *vmarray_new_rank_t(int rank, const int *dims, unsigned short elemsize, const Type *t); // uretilen dizi tipi (ref tasiyan struct elemanlar)
void finalize_vmarray(GCHeader *h); // uretilen dizi Type'lari icin
// System.Array yardimcilari: eleman tipinden bagimsiz (elemsize'i dizi tasir), memmove tabanli.
void vmarray_copy(VmArray *src, int srcIndex, VmArray *dst, int dstIndex, int len); // overlap guvenli
VmArray *vmarray_resize(VmArray *a, int newLen);                                    // yeni dizi dondurur (C# ref emulasyonu: a = Resize(a,n))
void vmarray_clear(VmArray *a, int index, int len);                                 // elemanlari sifirlar

#ifdef VM_DEBUG
enum
{
    OI_END,
    OI_INT,
    OI_DBL,
    OI_PTR
};
void opinfo(const char *id, ...);
#define OPINFO(id, ...) opinfo(id, __VA_ARGS__)
#endif
#endif
