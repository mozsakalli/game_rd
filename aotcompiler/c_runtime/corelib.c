// corelib'in C govdeleri: corelib/*.cs'teki extern bildirimlerinin implementasyonlari.
// Fonksiyon adlari derleyicinin mangling'iyle SOZLESMELI: CName(Owner$Ad_ArgTipleri)
// (orn. "System.String$op_add_System_String_Int" -> System_String_op_add_System_String_Int).
// Imzalar: this = a0, sonra a1...
// VM esleri source/Intrinsics.cs'te - IKI TARAF AYNI SEMANTIGI vermek zorunda (selftest dogrular).
#include "vmrt.h"

// ---- Object ----
int System_Object_GetHashCode(VmObject *a0)
{
    return gc_hashcode((GCHeader *)a0); // kimlik hash (C# RuntimeHelpers.GetHashCode)
}
int System_Object_Equals_System_Object(VmObject *a0, VmObject *a1)
{
    return a0 == a1; // default: referans esitligi
}
VmString *System_Object_ToString(VmObject *a0)
{
    return (VmString *)((GCHeader *)a0)->type->name; // tip adi strpool'da: SIFIR alloc
}

// ---- string builder: kalici UTF-16 scratch (malloc, GC disi) -> sonuc TEK gc alloc ----
// Tek is parcacigi varsayimi (mevcut runtime modeli). Concat zincirleri/kompozisyon ara
// VmString URETMEZ: parcalar buffer'a yazilir, sb_final tek nihai string tahsis eder.
static unsigned short *sb_buf;
static int sb_len, sb_cap;
static void sb_reset(void)
{
    sb_len = 0;
}
static unsigned short *sb_reserve(int extra)
{
    if (sb_len + extra > sb_cap)
    {
        int cap = sb_cap ? sb_cap : 64;
        while (cap < sb_len + extra)
            cap *= 2;
        sb_buf = (unsigned short *)realloc(sb_buf, (size_t)cap * 2);
        sb_cap = cap;
    }
    return sb_buf + sb_len;
}
static void sb_utf16(const unsigned short *d, int n)
{
    if (n <= 0)
        return;
    memcpy(sb_reserve(n), d, (size_t)n * 2);
    sb_len += n;
}
static void sb_str(VmString *s)
{
    if (s)
        sb_utf16(s->data, s->length); /* C#: null -> bos */
}
static void sb_ascii(const char *s)
{
    int n = (int)strlen(s);
    unsigned short *d = sb_reserve(n);
    for (int i = 0; i < n; i++)
        d[i] = (unsigned short)s[i];
    sb_len += n;
}
static void sb_ch(unsigned short c)
{
    *sb_reserve(1) = c;
    sb_len++;
}
static void sb_int(int v)
{
    char t[16]; /* INT_MIN sigar */
    snprintf(t, sizeof t, "%d", v);
    sb_ascii(t);
}
static VmString *sb_final(void)
{
    VmString *r = vmstring_alloc(sb_len);
    if (sb_len)
        memcpy((void *)r->data, sb_buf, (size_t)sb_len * 2);
    return r;
}

// ---- boxing: [GCHeader][deger]. Kucuk degerler immortal cache (SIFIR alloc, GC listelerine girmez) ----
#include <math.h>
#define DIGITOYENGINE_BOXP(T, o) (*(T *)((char *)(o) + sizeof(GCHeader)))
typedef struct
{
    GCHeader h;
    double pad; // payload alani: header'dan hemen sonra, 8 hizali
} DigitoyEngineBoxSlot;

double System_Math_Ceiling_Double(double value)
{
    return ceil(value);
}

float System_Math_Abs_Float(float value)
{
    return fabsf(value);
}

double System_Math_Abs_Double(double value)
{
    return fabs(value);
}

float System_Math_Max_Float_Float(float x, float y)
{
    return fmaxf(x, y);
}

float System_MathF_Min_Float_Float(float x, float y)
{
    return fminf(x, y);
}

float System_MathF_Max_Float_Float(float x, float y)
{
    return fmaxf(x, y);
}

float System_MathF_Abs_Float(float value) { return fabsf(value); }
float System_MathF_Asin_Float(float value) { return asinf(value); }
float System_MathF_Atan2_Float_Float(float y, float x) { return atan2f(y, x); }
float System_MathF_Ceiling_Float(float value) { return ceilf(value); }
float System_MathF_Cos_Float(float value) { return cosf(value); }
float System_MathF_Exp_Float(float value) { return expf(value); }
float System_MathF_Floor_Float(float value) { return floorf(value); }
float System_MathF_Pow_Float_Float(float x, float y) { return powf(x, y); }
float System_MathF_Round_Float(float value) { return roundf(value); }
float System_MathF_Sin_Float(float value) { return sinf(value); }
float System_MathF_Sqrt_Float(float value) { return sqrtf(value); }
float System_MathF_Tan_Float(float value) { return tanf(value); }

double System_Math_Max_Double_Double(double x, double y)
{
    return fmax(x, y);
}

int System_Math_Sign_Double(double value)
{
    return (value > 0.0) - (value < 0.0);
}

int System_Convert_ToInt32_System_String_Int(VmString *value, int from_base)
{
    DIGITOYENGINE_NULLCHECK(value);
    if (from_base != 2 && from_base != 8 && from_base != 10 && from_base != 16)
        DIGITOYENGINE_throw_io("Convert.ToInt32 invalid base");
    unsigned int result = 0;
    int at = 0, negative = 0;
    if (from_base == 10 && value->length > 0 && (value->data[0] == '+' || value->data[0] == '-'))
    {
        negative = value->data[0] == '-';
        at++;
    }
    if (at == value->length)
        DIGITOYENGINE_throw_io("Convert.ToInt32 invalid value");
    for (; at < value->length; at++)
    {
        unsigned short ch = value->data[at];
        unsigned int digit = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'a' && ch <= 'f' ? ch - 'a' + 10
                                                             : ch >= 'A' && ch <= 'F'   ? ch - 'A' + 10
                                                                                        : 255;
        if (digit >= (unsigned int)from_base || result > (UINT_MAX - digit) / (unsigned int)from_base)
            DIGITOYENGINE_throw_io("Convert.ToInt32 invalid value");
        result = result * (unsigned int)from_base + digit;
    }
    if (from_base == 10)
    {
        unsigned int limit = negative ? (unsigned int)INT_MAX + 1u : (unsigned int)INT_MAX;
        if (result > limit)
            DIGITOYENGINE_throw_io("Convert.ToInt32 overflow");
        return negative ? (result == (unsigned int)INT_MAX + 1u ? INT_MIN : -(int)result) : (int)result;
    }
    return (int)result;
}

unsigned int System_Convert_ToUInt32_Int(int value)
{
    if (value < 0)
        DIGITOYENGINE_throw_io("Convert.ToUInt32 overflow");
    return (unsigned int)value;
}

static volatile int digitoyengine_monitor_gate;
static _Thread_local int digitoyengine_monitor_depth;

void System_Threading_Monitor_Enter_System_Object(VmObject *target)
{
    DIGITOYENGINE_NULLCHECK(target);
    if (digitoyengine_monitor_depth++ > 0)
        return;
    while (__atomic_exchange_n(&digitoyengine_monitor_gate, 1, __ATOMIC_ACQUIRE))
    {
    }
}

void System_Threading_Monitor_Exit_System_Object(VmObject *target)
{
    DIGITOYENGINE_NULLCHECK(target);
    if (digitoyengine_monitor_depth <= 0)
        abort();
    if (--digitoyengine_monitor_depth == 0)
        __atomic_store_n(&digitoyengine_monitor_gate, 0, __ATOMIC_RELEASE);
}

VmString *System_Uri_EscapeDataString_System_String(VmString *value)
{
    static const char hex[] = "0123456789ABCDEF";
    DIGITOYENGINE_NULLCHECK(value);
    VmString *result = vmstring_alloc(value->length * 9);
    unsigned short *output = (unsigned short *)result->data;
    int written = 0;
    for (int index = 0; index < value->length; index++)
    {
        unsigned int codepoint = ((const unsigned short *)value->data)[index];
        if (codepoint >= 0xD800 && codepoint <= 0xDBFF && index + 1 < value->length)
        {
            unsigned int low = ((const unsigned short *)value->data)[index + 1];
            if (low >= 0xDC00 && low <= 0xDFFF)
            {
                codepoint = 0x10000 + ((codepoint - 0xD800) << 10) + (low - 0xDC00);
                index++;
            }
        }
        unsigned char bytes[4];
        int count;
        if (codepoint <= 0x7F)
        {
            bytes[0] = (unsigned char)codepoint;
            count = 1;
        }
        else if (codepoint <= 0x7FF)
        {
            bytes[0] = 0xC0 | (codepoint >> 6);
            bytes[1] = 0x80 | (codepoint & 0x3F);
            count = 2;
        }
        else if (codepoint <= 0xFFFF)
        {
            bytes[0] = 0xE0 | (codepoint >> 12);
            bytes[1] = 0x80 | ((codepoint >> 6) & 0x3F);
            bytes[2] = 0x80 | (codepoint & 0x3F);
            count = 3;
        }
        else
        {
            bytes[0] = 0xF0 | (codepoint >> 18);
            bytes[1] = 0x80 | ((codepoint >> 12) & 0x3F);
            bytes[2] = 0x80 | ((codepoint >> 6) & 0x3F);
            bytes[3] = 0x80 | (codepoint & 0x3F);
            count = 4;
        }
        for (int byteIndex = 0; byteIndex < count; byteIndex++)
        {
            unsigned char byte = bytes[byteIndex];
            if ((byte >= 'A' && byte <= 'Z') || (byte >= 'a' && byte <= 'z') ||
                (byte >= '0' && byte <= '9') || byte == '-' || byte == '_' || byte == '.' || byte == '~')
                output[written++] = byte;
            else
            {
                output[written++] = '%';
                output[written++] = hex[byte >> 4];
                output[written++] = hex[byte & 15];
            }
        }
    }
    result->length = written;
    return result;
}

// C# BOXING KIMLIGI: her kutulama AYRI nesne (NativeAOT gibi). Kucuk-deger cache YOK -
// (object)7 == (object)7 daima false (referans esitligi), .NET ile birebir. Deger esitligi
// op_eq/Equals uzerinden (icerik) korunur.
void digitoyengine_box_init(void) {}
VmObject *digitoyengine_box_bool(int v)
{
    VmObject *o = (VmObject *)gc_alloc(&vmbool_type);
    DIGITOYENGINE_BOXP(int, o) = v ? 1 : 0;
    return o;
}
VmObject *digitoyengine_box_char(char v)
{
    VmObject *o = (VmObject *)gc_alloc(&vmchar_type);
    DIGITOYENGINE_BOXP(char, o) = v;
    return o;
}
VmObject *digitoyengine_box_i8(signed char v)
{
    VmObject *o = (VmObject *)gc_alloc(&vmsbyte_type);
    DIGITOYENGINE_BOXP(signed char, o) = v;
    return o;
}
VmObject *digitoyengine_box_u8(unsigned char v)
{
    VmObject *o = (VmObject *)gc_alloc(&vmbyte_type);
    DIGITOYENGINE_BOXP(unsigned char, o) = v;
    return o;
}
#define DIGITOYENGINE_BOX_MK(nm, T, TY)          \
    VmObject *digitoyengine_box_##nm(T v)        \
    {                                            \
        VmObject *o = (VmObject *)gc_alloc(&TY); \
        DIGITOYENGINE_BOXP(T, o) = v;            \
        return o;                                \
    }
DIGITOYENGINE_BOX_MK(i16, short, vmint16_type)
DIGITOYENGINE_BOX_MK(u16, unsigned short, vmuint16_type)
DIGITOYENGINE_BOX_MK(i32, int, vmint32_type)
DIGITOYENGINE_BOX_MK(u32, unsigned int, vmuint32_type)
DIGITOYENGINE_BOX_MK(i64, long long, vmint64_type)
DIGITOYENGINE_BOX_MK(u64, unsigned long long, vmuint64_type)
VmObject *digitoyengine_box_f32(float v)
{
    VmObject *o = (VmObject *)gc_alloc(&vmsingle_type);
    DIGITOYENGINE_BOXP(float, o) = v;
    return o;
}
VmObject *digitoyengine_box_f64(double v)
{
    VmObject *o = (VmObject *)gc_alloc(&vmdouble_type);
    DIGITOYENGINE_BOXP(double, o) = v;
    return o;
}
// enum kutusu: tip kimligi enum'un kendi descriptor'i (is/GetType exact; unbox alias ile int'e denk)
VmObject *digitoyengine_box_enum(int v, const Type *t)
{
    VmObject *o = (VmObject *)gc_alloc(t);
    DIGITOYENGINE_BOXP(int, o) = v;
    return o;
}
// kullanici struct kutusu: gc_alloc(t) (t->size header+payload, trace fn struct icindeki referanslari tarar) + kopya
VmObject *digitoyengine_box_struct(const Type *t, const void *src, int size)
{
    VmObject *o = (VmObject *)gc_alloc(t);
    memcpy((char *)o + sizeof(GCHeader), src, (size_t)size);
    return o;
}
int digitoyengine_enumbox_hash(VmObject *s) { return DIGITOYENGINE_BOXP(int, s); } // dotnet Enum: underlying deger
int digitoyengine_enumbox_eq(VmObject *s, VmObject *o)                             // dotnet Enum.Equals: AYNI enum tipi + deger
{
    return o && o->gc.type == s->gc.type && DIGITOYENGINE_BOXP(int, o) == DIGITOYENGINE_BOXP(int, s);
}

// ---- box vtable govdeleri (dotnet birebir; slot: 0=GetHashCode 1=Equals 2=ToString) ----
static void sb_i64(long long v)
{
    char t[24];
    snprintf(t, sizeof t, "%lld", v);
    sb_ascii(t);
}
static void sb_u64(unsigned long long v)
{
    char t[24];
    snprintf(t, sizeof t, "%llu", v);
    sb_ascii(t);
}
VmString *digitoyengine_int_str(int v) // uretilen enum ToString'in tanimsiz-deger dali
{
    sb_reset();
    sb_i64((long long)v);
    return sb_final();
}

// ---- sayisal TryParse'lar (dotnet semantigi: whitespace kabul, invariant, tam tuketim) ----
#include <ctype.h>
#include <errno.h>
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h> /* GetTickCount */
#else
#include <time.h>
#endif
static int digitoyengine_numbuf(VmString *s, char *buf, int cap) /* ascii kopya; sigmaz/ascii-disi -> 0 */
{
    if (!s || s->length >= cap)
        return 0;
    for (int i = 0; i < s->length; i++)
    {
        if (s->data[i] > 127)
            return 0;
        buf[i] = (char)s->data[i];
    }
    buf[s->length] = 0;
    return 1;
}
static int digitoyengine_tail_ok(const char *e)
{
    while (isspace((unsigned char)*e))
        e++;
    return *e == 0;
}
int Int_TryParse_System_String_out_Int(VmString *a0, int *a1)
{
    char b[128];
    char *e;
    *a1 = 0;
    if (!digitoyengine_numbuf(a0, b, sizeof b))
        return 0;
    errno = 0;
    long long v = strtoll(b, &e, 10);
    if (e == b || !digitoyengine_tail_ok(e) || errno == ERANGE || v < -2147483648LL || v > 2147483647LL)
        return 0;
    *a1 = (int)v;
    return 1;
}
int Int_TryParse_System_String_System_Globalization_NumberStyles_System_Object_out_Int(
    VmString *a0, int a1, VmObject *a2, int *a3)
{
    (void)a2;
    char b[128];
    char *e;
    *a3 = 0;
    if (a1 != 515 || !digitoyengine_numbuf(a0, b, sizeof b))
        return 0;
    char *start = b;
    while (isspace((unsigned char)*start))
        start++;
    if (*start == '+' || *start == '-')
        return 0;
    errno = 0;
    unsigned long long value = strtoull(start, &e, 16);
    if (e == start || !digitoyengine_tail_ok(e) || errno == ERANGE || value > 0xffffffffULL)
        return 0;
    *a3 = (int)(unsigned int)value;
    return 1;
}
int UInt_TryParse_System_String_out_UInt(VmString *a0, unsigned int *a1)
{
    char b[128];
    char *e = b;
    *a1 = 0;
    if (!digitoyengine_numbuf(a0, b, sizeof b))
        return 0;
    while (isspace((unsigned char)*e))
        e++;
    if (*e == '-') /* strtoull negatifi sessizce sarar - dotnet false der */
        return 0;
    errno = 0;
    unsigned long long v = strtoull(e, &e, 10);
    if (e == b || !digitoyengine_tail_ok(e) || errno == ERANGE || v > 0xFFFFFFFFull)
        return 0;
    *a1 = (unsigned int)v;
    return 1;
}
int Long_TryParse_System_String_out_Long(VmString *a0, long long *a1)
{
    char b[128];
    char *e;
    *a1 = 0;
    if (!digitoyengine_numbuf(a0, b, sizeof b))
        return 0;
    errno = 0;
    long long v = strtoll(b, &e, 10);
    if (e == b || !digitoyengine_tail_ok(e) || errno == ERANGE)
        return 0;
    *a1 = v;
    return 1;
}
VmString *Int_ToString(int self)
{
    sb_reset();
    sb_int(self);
    return sb_final();
}
VmString *UInt_ToString(unsigned int self)
{
    sb_reset();
    sb_u64(self);
    return sb_final();
}
VmString *Long_ToString(long long self)
{
    sb_reset();
    sb_i64(self);
    return sb_final();
}
int Float_TryParse_System_String_out_Float(VmString *a0, float *a1)
{
    char b[128];
    char *e;
    *a1 = 0;
    if (!digitoyengine_numbuf(a0, b, sizeof b))
        return 0;
    float v = strtof(b, &e); /* tasma -> Inf + true (dotnet Core 3.0+) */
    if (e == b || !digitoyengine_tail_ok(e))
        return 0;
    *a1 = v;
    return 1;
}
int Double_TryParse_System_String_out_Double(VmString *a0, double *a1)
{
    char b[128];
    char *e;
    *a1 = 0;
    if (!digitoyengine_numbuf(a0, b, sizeof b))
        return 0;
    double v = strtod(b, &e);
    if (e == b || !digitoyengine_tail_ok(e))
        return 0;
    *a1 = v;
    return 1;
}
// dotnet double/float.ToString(): en kisa round-trip basamak (deneme: hassasiyet 1..17/9,
// ilk geri-donusen = Ryu basamaklari) + G bicimi (bilimsel esik: usten 16/8, alttan -5; dotnet dogrulandi)
static void sb_double(double v, int isFloat)
{
    if (isnan(v))
    {
        sb_ascii("NaN");
        return;
    }
    if (signbit(v))
    {
        sb_ch('-');
        v = -v;
    }
    if (isinf(v))
    {
        sb_ch(0x221E); /* dotnet: "∞" */
        return;
    }
    if (v == 0)
    {
        sb_ch('0');
        return;
    }
    char buf[40];
    int maxp = isFloat ? 9 : 17, prec = maxp;
    for (int p = 1; p < maxp; p++)
    {
        snprintf(buf, sizeof buf, "%.*e", p - 1, v);
        if (isFloat ? (strtof(buf, 0) == (float)v) : (strtod(buf, 0) == v))
        {
            prec = p;
            break;
        }
    }
    if (prec == maxp)
        snprintf(buf, sizeof buf, "%.*e", maxp - 1, v);
    char digs[24];
    int nd = 0, exp10 = 0;
    const char *q = buf;
    for (; *q && *q != 'e'; q++)
        if (*q >= '0' && *q <= '9')
            digs[nd++] = *q;
    if (*q == 'e')
        exp10 = atoi(q + 1);
    while (nd > 1 && digs[nd - 1] == '0')
        nd--;                                      /* savunmaci: en kisa secimde sondaki sifir olmamali */
    if (exp10 >= (isFloat ? 9 : 17) || exp10 < -4) /* dotnet G-shortest: esik = max round-trip basamak (17/9) */
    {                                              /* bilimsel: d[.ddd]E(+|-)NN (buyuk E, en az 2 hane) */
        sb_ch((unsigned short)digs[0]);
        if (nd > 1)
        {
            sb_ch('.');
            for (int i = 1; i < nd; i++)
                sb_ch((unsigned short)digs[i]);
        }
        sb_ch('E');
        sb_ch(exp10 < 0 ? '-' : '+');
        char et[8];
        snprintf(et, sizeof et, "%02d", exp10 < 0 ? -exp10 : exp10);
        sb_ascii(et);
    }
    else if (exp10 >= nd - 1)
    { /* tam sayi + sifirlar */
        for (int i = 0; i < nd; i++)
            sb_ch((unsigned short)digs[i]);
        for (int i = nd - 1; i < exp10; i++)
            sb_ch('0');
    }
    else if (exp10 >= 0)
    {
        for (int i = 0; i <= exp10; i++)
            sb_ch((unsigned short)digs[i]);
        sb_ch('.');
        for (int i = exp10 + 1; i < nd; i++)
            sb_ch((unsigned short)digs[i]);
    }
    else
    {
        sb_ascii("0.");
        for (int i = 0; i < -exp10 - 1; i++)
            sb_ch('0');
        for (int i = 0; i < nd; i++)
            sb_ch((unsigned short)digs[i]);
    }
}
VmString *Float_ToString(float self)
{
    sb_reset();
    sb_double((double)self, 1);
    return sb_final();
}
VmString *Double_ToString(double self)
{
    sb_reset();
    sb_double(self, 0);
    return sb_final();
}
int Float_IsPositiveInfinity_Float(float value) { return isinf(value) && value > 0.0f; }
long long System_Runtime_InteropServices_Marshal_AllocHGlobal_Int(int cb)
{
    return (long long)(size_t)malloc((size_t)cb);
}
void System_Runtime_InteropServices_Marshal_FreeHGlobal_Long(long long hglobal)
{
    free((void *)(size_t)hglobal);
}
#define DIGITOYENGINE_BOX_HASH_ID(nm, T) \
    static int box_##nm##_hash(VmObject *s) { return (int)DIGITOYENGINE_BOXP(T, s); }
#define DIGITOYENGINE_BOX_EQ(nm, T, TY)                                                        \
    static int box_##nm##_eq(VmObject *s, VmObject *o)                                         \
    {                                                                                          \
        return o && o->gc.type == &TY && DIGITOYENGINE_BOXP(T, o) == DIGITOYENGINE_BOXP(T, s); \
    }
#define DIGITOYENGINE_BOX_STR_I(nm, T)               \
    static VmString *box_##nm##_str(VmObject *s)     \
    {                                                \
        sb_reset();                                  \
        sb_i64((long long)DIGITOYENGINE_BOXP(T, s)); \
        return sb_final();                           \
    }
#define DIGITOYENGINE_BOX_STR_U(nm, T)                        \
    static VmString *box_##nm##_str(VmObject *s)              \
    {                                                         \
        sb_reset();                                           \
        sb_u64((unsigned long long)DIGITOYENGINE_BOXP(T, s)); \
        return sb_final();                                    \
    }
DIGITOYENGINE_BOX_HASH_ID(i8, signed char)
DIGITOYENGINE_BOX_HASH_ID(u8, unsigned char)
DIGITOYENGINE_BOX_HASH_ID(i16, short)
DIGITOYENGINE_BOX_HASH_ID(u16, unsigned short)
DIGITOYENGINE_BOX_HASH_ID(i32, int)
DIGITOYENGINE_BOX_HASH_ID(u32, unsigned int)
DIGITOYENGINE_BOX_HASH_ID(bool, int)
static int box_char_hash(VmObject *s) /* dotnet Char: v | v<<16 */
{
    int v = (int)(unsigned char)DIGITOYENGINE_BOXP(char, s);
    return v | (v << 16);
}
static int box_i64_hash(VmObject *s) /* dotnet Int64: alt ^ ust */
{
    long long v = DIGITOYENGINE_BOXP(long long, s);
    return (int)v ^ (int)(v >> 32);
}
static int box_u64_hash(VmObject *s)
{
    unsigned long long v = DIGITOYENGINE_BOXP(unsigned long long, s);
    return (int)v ^ (int)(v >> 32);
}
static int box_f32_hash(VmObject *s) /* dotnet Single: bits, NaN/-0 normalize */
{
    int bits;
    memcpy(&bits, (char *)s + sizeof(GCHeader), 4);
    if (((bits - 1) & 0x7FFFFFFF) >= 0x7F800000)
        bits &= 0x7F800000;
    return bits;
}
static int box_f64_hash(VmObject *s) /* dotnet Double */
{
    long long bits;
    memcpy(&bits, (char *)s + sizeof(GCHeader), 8);
    if (((bits - 1) & 0x7FFFFFFFFFFFFFFFLL) >= 0x7FF0000000000000LL)
        bits &= 0x7FF0000000000000LL;
    return (int)bits ^ (int)(bits >> 32);
}
// deger halleri (kisitsiz T'nin somutlamalari - box'suz)
int digitoyengine_valhash_i64(long long v) { return (int)v ^ (int)(v >> 32); }
int digitoyengine_valhash_f32(float v)
{
    int bits;
    memcpy(&bits, &v, 4);
    if (((bits - 1) & 0x7FFFFFFF) >= 0x7F800000)
        bits &= 0x7F800000;
    return bits;
}
int digitoyengine_valhash_f64(double v)
{
    long long bits;
    memcpy(&bits, &v, 8);
    if (((bits - 1) & 0x7FFFFFFFFFFFFFFFLL) >= 0x7FF0000000000000LL)
        bits &= 0x7FF0000000000000LL;
    return (int)bits ^ (int)(bits >> 32);
}
int digitoyengine_valeq_f32(float a, float b) { return a == b || (isnan(a) && isnan(b)); }
int digitoyengine_valeq_f64(double a, double b) { return a == b || (isnan(a) && isnan(b)); }
DIGITOYENGINE_BOX_EQ(bool, int, vmbool_type)
DIGITOYENGINE_BOX_EQ(char, char, vmchar_type)
DIGITOYENGINE_BOX_EQ(i8, signed char, vmsbyte_type)
DIGITOYENGINE_BOX_EQ(u8, unsigned char, vmbyte_type)
DIGITOYENGINE_BOX_EQ(i16, short, vmint16_type)
DIGITOYENGINE_BOX_EQ(u16, unsigned short, vmuint16_type)
DIGITOYENGINE_BOX_EQ(i32, int, vmint32_type)
DIGITOYENGINE_BOX_EQ(u32, unsigned int, vmuint32_type)
DIGITOYENGINE_BOX_EQ(i64, long long, vmint64_type)
DIGITOYENGINE_BOX_EQ(u64, unsigned long long, vmuint64_type)
static int box_f32_eq(VmObject *s, VmObject *o) /* dotnet: NaN.Equals(NaN)=true */
{
    if (!o || o->gc.type != &vmsingle_type)
        return 0;
    float a = DIGITOYENGINE_BOXP(float, s), b = DIGITOYENGINE_BOXP(float, o);
    return a == b || (isnan(a) && isnan(b));
}
static int box_f64_eq(VmObject *s, VmObject *o)
{
    if (!o || o->gc.type != &vmdouble_type)
        return 0;
    double a = DIGITOYENGINE_BOXP(double, s), b = DIGITOYENGINE_BOXP(double, o);
    return a == b || (isnan(a) && isnan(b));
}
DIGITOYENGINE_BOX_STR_I(i8, signed char)
DIGITOYENGINE_BOX_STR_U(u8, unsigned char)
DIGITOYENGINE_BOX_STR_I(i16, short)
DIGITOYENGINE_BOX_STR_U(u16, unsigned short)
DIGITOYENGINE_BOX_STR_I(i32, int)
DIGITOYENGINE_BOX_STR_U(u32, unsigned int)
DIGITOYENGINE_BOX_STR_I(i64, long long)
DIGITOYENGINE_BOX_STR_U(u64, unsigned long long)
static VmString *box_bool_str(VmObject *s)
{
    sb_reset();
    sb_ascii(DIGITOYENGINE_BOXP(int, s) ? "True" : "False");
    return sb_final();
}
static VmString *box_char_str(VmObject *s)
{
    VmString *r = vmstring_alloc(1);
    ((unsigned short *)r->data)[0] = (unsigned short)(unsigned char)DIGITOYENGINE_BOXP(char, s);
    return r;
}
static VmString *box_f32_str(VmObject *s)
{
    sb_reset();
    sb_double((double)DIGITOYENGINE_BOXP(float, s), 1);
    return sb_final();
}
static VmString *box_f64_str(VmObject *s)
{
    sb_reset();
    sb_double(DIGITOYENGINE_BOXP(double, s), 0);
    return sb_final();
}
#define DIGITOYENGINE_BOX_VT(sym, nm) \
    const void *sym[3] = {(const void *)&box_##nm##_hash, (const void *)&box_##nm##_eq, (const void *)&box_##nm##_str};
DIGITOYENGINE_BOX_VT(vmbool_vtable, bool)
DIGITOYENGINE_BOX_VT(vmchar_vtable, char)
DIGITOYENGINE_BOX_VT(vmsbyte_vtable, i8)
DIGITOYENGINE_BOX_VT(vmbyte_vtable, u8)
DIGITOYENGINE_BOX_VT(vmint16_vtable, i16)
DIGITOYENGINE_BOX_VT(vmuint16_vtable, u16)
DIGITOYENGINE_BOX_VT(vmint32_vtable, i32)
DIGITOYENGINE_BOX_VT(vmuint32_vtable, u32)
DIGITOYENGINE_BOX_VT(vmint64_vtable, i64)
DIGITOYENGINE_BOX_VT(vmuint64_vtable, u64)
DIGITOYENGINE_BOX_VT(vmsingle_vtable, f32)
DIGITOYENGINE_BOX_VT(vmdouble_vtable, f64)

// ---- String ----
static VmString *digitoyengine_concat(VmString *a, VmString *b)
{
    int al = a ? a->length : 0, bl = b ? b->length : 0; /* C#: null + s = s */
    VmString *r = vmstring_alloc(al + bl);
    unsigned short *d = (unsigned short *)r->data;
    if (al)
        memcpy(d, a->data, (size_t)al * 2);
    if (bl)
        memcpy(d + al, b->data, (size_t)bl * 2);
    return r;
}
int System_String_get_Length(VmString *a0)
{
    return vmstring_length(a0);
}
char System_String_get_Chars_Int(VmString *a0, int a1)
{
    return (char)vmstring_get(a0, a1);
}
VmString *System_String_Substring_Int(VmString *a0, int a1)
{
    DIGITOYENGINE_NULLCHECK(a0);
    int n = a0->length;
    if ((unsigned)a1 > (unsigned)n)
        DIGITOYENGINE_throw_bounds(a1, n);
    int len = n - a1;
    VmString *r = vmstring_alloc(len);
    if (len)
        memcpy((void *)r->data, a0->data + a1, (size_t)len * 2);
    return r;
}
VmString *System_String_Substring_Int_Int(VmString *a0, int a1, int a2)
{
    DIGITOYENGINE_NULLCHECK(a0);
    int n = a0->length;
    if ((unsigned)a1 > (unsigned)n)
        DIGITOYENGINE_throw_bounds(a1, n);
    if (a2 < 0 || a1 + a2 > n)
        DIGITOYENGINE_throw_bounds(a1 + a2, n);
    VmString *r = vmstring_alloc(a2);
    if (a2)
        memcpy((void *)r->data, a0->data + a1, (size_t)a2 * 2);
    return r;
}
static int digitoyengine_char_is_whitespace(unsigned short c)
{
    return (c >= 0x0009 && c <= 0x000d) || c == 0x0020 || c == 0x0085 || c == 0x00a0 ||
           c == 0x1680 || (c >= 0x2000 && c <= 0x200a) || c == 0x2028 || c == 0x2029 ||
           c == 0x202f || c == 0x205f || c == 0x3000;
}
VmString *System_String_Trim(VmString *a0)
{
    DIGITOYENGINE_NULLCHECK(a0);
    int start = 0, end = a0->length;
    while (start < end && digitoyengine_char_is_whitespace(a0->data[start]))
        start++;
    while (end > start && digitoyengine_char_is_whitespace(a0->data[end - 1]))
        end--;
    if (start == 0 && end == a0->length)
        return a0;
    VmString *result = vmstring_alloc(end - start);
    if (end > start)
        memcpy((void *)result->data, a0->data + start, (size_t)(end - start) * 2);
    return result;
}
static VmString *digitoyengine_string_to_lower(VmString *value)
{
    DIGITOYENGINE_NULLCHECK(value);
    VmString *result = vmstring_alloc(value->length);
    unsigned short *destination = (unsigned short *)result->data;
    for (int i = 0; i < value->length; i++)
    {
        unsigned short character = value->data[i];
        destination[i] = character >= 'A' && character <= 'Z' ? character + ('a' - 'A') : character;
    }
    return result;
}
VmString *System_String_ToLower(VmString *a0) { return digitoyengine_string_to_lower(a0); }
VmString *System_String_ToLowerInvariant(VmString *a0) { return digitoyengine_string_to_lower(a0); }
VmString *System_String_ToUpper(VmString *a0)
{
    DIGITOYENGINE_NULLCHECK(a0);
    VmString *result = vmstring_alloc(a0->length);
    unsigned short *destination = (unsigned short *)result->data;
    for (int i = 0; i < a0->length; i++)
    {
        unsigned short character = a0->data[i];
        destination[i] = character >= 'a' && character <= 'z' ? character - ('a' - 'A') : character;
    }
    return result;
}
VmArray *System_String_ToCharArray(VmString *a0)
{
    DIGITOYENGINE_NULLCHECK(a0);
    VmArray *result = vmarray_new(a0->length, sizeof(char), 0);
    char *destination = (char *)result->data;
    for (int i = 0; i < a0->length; i++)
        destination[i] = (char)a0->data[i];
    return result;
}
static VmString *digitoyengine_format_bytes(VmString *format, const unsigned char *args, int count)
{
    DIGITOYENGINE_NULLCHECK(format);
    sb_reset();
    for (int i = 0; i < format->length; i++)
    {
        unsigned short ch = format->data[i];
        if (ch == '{' && i + 1 < format->length && format->data[i + 1] == '{')
        {
            sb_ch('{');
            i++;
            continue;
        }
        if (ch == '}' && i + 1 < format->length && format->data[i + 1] == '}')
        {
            sb_ch('}');
            i++;
            continue;
        }
        if (ch != '{')
        {
            sb_ch(ch);
            continue;
        }

        int index = 0;
        i++;
        while (i < format->length && format->data[i] >= '0' && format->data[i] <= '9')
        {
            index = index * 10 + format->data[i] - '0';
            i++;
        }
        while (i < format->length && format->data[i] != ':' && format->data[i] != '}')
            i++;
        unsigned short specifier = 0;
        int width = 0;
        if (i < format->length && format->data[i] == ':')
        {
            i++;
            if (i < format->length)
                specifier = format->data[i++];
            while (i < format->length && format->data[i] >= '0' && format->data[i] <= '9')
            {
                width = width * 10 + format->data[i] - '0';
                i++;
            }
            while (i < format->length && format->data[i] != '}')
                i++;
        }
        if (index < 0 || index >= count || i >= format->length)
            DIGITOYENGINE_throw_bounds(index, count);
        unsigned int value = args[index];
        if (specifier == 'x' || specifier == 'X')
        {
            char text[9];
            snprintf(text, sizeof text, specifier == 'x' ? "%0*x" : "%0*X", width, value);
            sb_ascii(text);
        }
        else
            sb_int((int)value);
    }
    return sb_final();
}
VmString *System_String_Format_System_String_Byte(VmString *a0, unsigned char a1)
{
    return digitoyengine_format_bytes(a0, &a1, 1);
}
VmString *System_String_Format_System_String_Byte_Byte_Byte_Byte(VmString *a0, unsigned char a1, unsigned char a2, unsigned char a3, unsigned char a4)
{
    unsigned char args[] = {a1, a2, a3, a4};
    return digitoyengine_format_bytes(a0, args, 4);
}
static int digitoyengine_indexof(VmString *s, VmString *t, int from)
{
    if (from < 0)
        from = 0;
    int n = s->length, m = t->length;
    if (m == 0)
        return from <= n ? from : -1; /* C#: bos aranan = from */
    for (int i = from; i + m <= n; i++)
        if (memcmp(s->data + i, t->data, (size_t)m * 2) == 0)
            return i;
    return -1;
}
VmString *System_String_Replace_System_String_System_String(VmString *a0, VmString *a1, VmString *a2)
{
    DIGITOYENGINE_NULLCHECK(a0);
    DIGITOYENGINE_NULLCHECK(a1);
    int n = a0->length, old_len = a1->length, new_len = a2 ? a2->length : 0;
    if (old_len == 0)
        DIGITOYENGINE_throw_io("String.Replace oldValue cannot be empty");
    int count = 0;
    for (int at = 0; (at = digitoyengine_indexof(a0, a1, at)) >= 0; at += old_len)
        count++;
    if (count == 0)
        return a0;
    VmString *r = vmstring_alloc(n + count * (new_len - old_len));
    unsigned short *dst = (unsigned short *)r->data;
    int src_at = 0, dst_at = 0, match;
    while ((match = digitoyengine_indexof(a0, a1, src_at)) >= 0)
    {
        int prefix = match - src_at;
        if (prefix)
            memcpy(dst + dst_at, a0->data + src_at, (size_t)prefix * 2);
        dst_at += prefix;
        if (new_len)
            memcpy(dst + dst_at, a2->data, (size_t)new_len * 2);
        dst_at += new_len;
        src_at = match + old_len;
    }
    if (src_at < n)
        memcpy(dst + dst_at, a0->data + src_at, (size_t)(n - src_at) * 2);
    return r;
}
VmString *System_String_Replace_Char_Char(VmString *a0, char a1, char a2)
{
    DIGITOYENGINE_NULLCHECK(a0);
    VmString *r = vmstring_alloc(a0->length);
    unsigned short *dst = (unsigned short *)r->data;
    for (int i = 0; i < a0->length; i++)
        dst[i] = a0->data[i] == (unsigned char)a1 ? (unsigned char)a2 : a0->data[i];
    return r;
}
static int digitoyengine_split_index(VmString *value, const unsigned short *separator, int separator_length, int start)
{
    for (int i = start; i + separator_length <= value->length; i++)
        if (memcmp(value->data + i, separator, (size_t)separator_length * 2) == 0)
            return i;
    return -1;
}
static VmArray *digitoyengine_split(VmString *value, const unsigned short *separator, int separator_length, int count)
{
    DIGITOYENGINE_NULLCHECK(value);
    if (separator_length <= 0)
        DIGITOYENGINE_throw_io("String.Split separator cannot be empty");
    if (count < 0)
        DIGITOYENGINE_throw_io("String.Split count cannot be negative");
    if (count == 0)
        return vmarray_new(0, sizeof(GCHeader *), 1);
    int matches = 0, at = 0;
    while (matches + 1 < count)
    {
        int match = digitoyengine_split_index(value, separator, separator_length, at);
        if (match < 0)
            break;
        matches++;
        at = match + separator_length;
    }
    VmArray *result = vmarray_new(matches + 1, sizeof(GCHeader *), 1);
    gc_add_root(&result->gc);
    GCHeader **items = (GCHeader **)result->data;
    at = 0;
    for (int i = 0; i <= matches; i++)
    {
        int end = i == matches ? value->length : digitoyengine_split_index(value, separator, separator_length, at);
        VmString *part = vmstring_alloc(end - at);
        if (end > at)
            memcpy((void *)part->data, value->data + at, (size_t)(end - at) * 2);
        items[i] = &part->gc;
        gc_write_barrier(&result->gc, &part->gc);
        at = end + separator_length;
    }
    gc_remove_root(&result->gc);
    return result;
}
VmArray *System_String_Split_System_String(VmString *a0, VmString *a1)
{
    DIGITOYENGINE_NULLCHECK(a1);
    return digitoyengine_split(a0, a1->data, a1->length, 0x7fffffff);
}
VmArray *System_String_Split_System_String_Int(VmString *a0, VmString *a1, int a2)
{
    DIGITOYENGINE_NULLCHECK(a1);
    return digitoyengine_split(a0, a1->data, a1->length, a2);
}
VmArray *System_String_Split_Char(VmString *a0, unsigned short a1)
{
    return digitoyengine_split(a0, &a1, 1, 0x7fffffff);
}
int System_String_IndexOf_System_String(VmString *a0, VmString *a1)
{
    DIGITOYENGINE_NULLCHECK(a0);
    DIGITOYENGINE_NULLCHECK(a1);
    return digitoyengine_indexof(a0, a1, 0);
}
int System_String_IndexOf_System_String_Int(VmString *a0, VmString *a1, int a2)
{
    DIGITOYENGINE_NULLCHECK(a0);
    DIGITOYENGINE_NULLCHECK(a1);
    return digitoyengine_indexof(a0, a1, a2);
}
int System_String_IndexOf_System_String_System_StringComparison(VmString *a0, VmString *a1, int a2)
{
    (void)a2;
    DIGITOYENGINE_NULLCHECK(a0);
    DIGITOYENGINE_NULLCHECK(a1);
    return digitoyengine_indexof(a0, a1, 0);
}
int System_String_Contains_System_String(VmString *a0, VmString *a1)
{
    DIGITOYENGINE_NULLCHECK(a0);
    DIGITOYENGINE_NULLCHECK(a1);
    return digitoyengine_indexof(a0, a1, 0) >= 0;
}
int System_String_Contains_Char(VmString *a0, unsigned short a1)
{
    DIGITOYENGINE_NULLCHECK(a0);
    for (int i = 0; i < a0->length; i++)
        if (a0->data[i] == a1)
            return 1;
    return 0;
}
int System_String_StartsWith_System_String(VmString *a0, VmString *a1)
{
    DIGITOYENGINE_NULLCHECK(a0);
    DIGITOYENGINE_NULLCHECK(a1);
    if (a1->length > a0->length)
        return 0;
    return a1->length == 0 || memcmp(a0->data, a1->data, (size_t)a1->length * sizeof(unsigned short)) == 0;
}
int System_String_StartsWith_System_String_System_StringComparison(VmString *a0, VmString *a1, int a2)
{
    (void)a2;
    return System_String_StartsWith_System_String(a0, a1);
}
int System_String_StartsWith_Char(VmString *a0, unsigned short a1)
{
    DIGITOYENGINE_NULLCHECK(a0);
    return a0->length > 0 && a0->data[0] == a1;
}
int System_String_EndsWith_System_String(VmString *a0, VmString *a1)
{
    DIGITOYENGINE_NULLCHECK(a0);
    DIGITOYENGINE_NULLCHECK(a1);
    if (a1->length > a0->length)
        return 0;
    return a1->length == 0 || memcmp(a0->data + a0->length - a1->length, a1->data, (size_t)a1->length * sizeof(unsigned short)) == 0;
}
int System_String_EndsWith_System_String_System_StringComparison(VmString *a0, VmString *a1, int a2)
{
    (void)a2;
    return System_String_EndsWith_System_String(a0, a1);
}
int System_String_GetHashCode(VmString *a0)
{
    GCHeader *h = (GCHeader *)a0;
    if (!h->idhash)
    {
        int hv = 0;
        for (int i = 0; i < a0->length; i++)
            hv = hv * 31 + a0->data[i];
        h->idhash = hv == 0 ? 1 : hv; /* 0 = atanmadi sentineli */
    }
    return h->idhash;
}
int System_String_Equals_System_Object(VmString *a0, VmObject *a1)
{
    return (a1 && ((GCHeader *)a1)->type == &vmstring_type) ? vmstring_eq(a0, (VmString *)a1) : 0;
}
VmString *System_String_ToString(VmString *a0)
{
    return a0;
}
VmString *System_String_op_add_System_String_System_String(VmString *a0, VmString *a1)
{
    return digitoyengine_concat(a0, a1);
}
VmString *System_String_Concat_System_String_System_String_System_String(VmString *a0, VmString *a1, VmString *a2)
{
    return digitoyengine_concat(digitoyengine_concat(a0, a1), a2);
}
int System_String_Equals_System_String(VmString *a0, VmString *a1)
{
    return vmstring_eq(a0, a1);
}
VmString *System_String_op_add_System_String_Int(VmString *a0, int a1)
{
    sb_reset();
    sb_str(a0);
    sb_int(a1);
    return sb_final(); /* ara VmString yok: tek alloc */
}
VmString *System_String_op_add_Int_System_String(int a0, VmString *a1)
{
    sb_reset();
    sb_int(a0);
    sb_str(a1);
    return sb_final();
}
VmString *System_String_op_add_System_String_Char(VmString *a0, char a1)
{
    sb_reset();
    sb_str(a0);
    sb_ch((unsigned short)a1);
    return sb_final();
}
VmString *System_String_op_add_System_String_Bool(VmString *a0, int a1)
{
    sb_reset();
    sb_str(a0);
    sb_ascii(a1 ? "True" : "False");
    return sb_final();
}
VmString *System_String_op_add_System_String_Float(VmString *a0, float a1)
{
    sb_reset();
    sb_str(a0);
    sb_double((double)a1, 1);
    return sb_final();
}
VmString *System_String_op_add_Float_System_String(float a0, VmString *a1)
{
    sb_reset();
    sb_double((double)a0, 1);
    sb_str(a1);
    return sb_final();
}
VmString *System_String_op_add_System_String_Double(VmString *a0, double a1)
{
    sb_reset();
    sb_str(a0);
    sb_double(a1, 0);
    return sb_final();
}
static VmString *object_to_string(VmObject *value)
{
    if (!value)
        return 0;
    const Type *type = value->gc.type;
    return ((VmString * (*)(VmObject *)) type->vtable[2])(value);
}
VmString *System_String_op_add_System_String_System_Object(VmString *a0, VmObject *a1)
{
    VmString *text = object_to_string(a1);
    sb_reset();
    sb_str(a0);
    sb_str(text);
    return sb_final();
}
VmString *System_String_op_add_System_Object_System_String(VmObject *a0, VmString *a1)
{
    VmString *text = object_to_string(a0);
    sb_reset();
    sb_str(text);
    sb_str(a1);
    return sb_final();
}
int System_String_op_eq_System_String_System_String(VmString *a0, VmString *a1)
{
    return vmstring_eq(a0, a1);
}
int System_String_op_ne_System_String_System_String(VmString *a0, VmString *a1)
{
    return !vmstring_eq(a0, a1);
}

// ---- Console ----
void System_Console_Write_System_String(VmString *a0)
{
    if (a0)
        vm_write_utf8(a0->data, a0->length); /* C#: null yazim no-op */
}
void System_Console_Write_Int(int a0)
{
    printf("%d", a0);
}
void System_Console_WriteLine_System_String(VmString *a0)
{
    if (a0)
        vm_write_utf8(a0->data, a0->length);
    fputc('\n', stdout);
    fflush(stdout);
}
void System_IO_TextWriter_WriteLine_System_String(VmObject *self, VmString *value)
{
    (void)self;
    if (value)
        vm_write_utf8_to(stderr, value->data, value->length);
    fputc('\n', stderr);
}
void System_IO_TextWriter_WriteLine_Int(VmObject *self, int value)
{
    (void)self;
    fprintf(stderr, "%d\n", value);
}
void System_Console_WriteLine_Int(int a0)
{
    printf("%d\n", a0);
    fflush(stdout);
}
void System_Console_WriteLine(void)
{
    fputc('\n', stdout);
    fflush(stdout);
}

// ---- GC ----
void System_GC_Collect(void)
{
    gc_major(); /* tam dongu (finalizer'lar kosulur). Yalniz canli managed local olmayan noktada guvenli. */
}
// ---- Type (reflection cekirdegi): handle = Type* (ILK alan, digitoyengine_type_wrapper sozlesmesi) ----
typedef struct DigitoyEngineTypeObj
{
    GCHeader gc;
    long long handle;
} DigitoyEngineTypeObj;
static const Type *type_of(DigitoyEngineTypeObj *t)
{
    return (const Type *)(size_t)t->handle;
}
VmString *System_Type_get_FullName(DigitoyEngineTypeObj *a0)
{
    return (VmString *)type_of(a0)->name; /* havuz nesnesi: sifir alloc */
}
VmString *System_Type_get_Name(DigitoyEngineTypeObj *a0)
{
    const VmString *n = type_of(a0)->name;
    int cut = 0;
    for (int i = 0; i < n->length; i++)
        if (n->data[i] == '.')
            cut = i + 1;
    sb_reset();
    sb_utf16(n->data + cut, n->length - cut);
    return sb_final();
}
DigitoyEngineTypeObj *System_Type_get_BaseType(DigitoyEngineTypeObj *a0)
{
    const Type *b = type_of(a0)->base;
    return b ? (DigitoyEngineTypeObj *)digitoyengine_type_wrapper(b) : 0;
}
int System_Type_get_IsPrimitive(DigitoyEngineTypeObj *a0)
{
    unsigned short index = type_of(a0)->tindex;
    return index >= 4 && index <= 15;
}
int System_Type_get_IsEnum(DigitoyEngineTypeObj *a0)
{
    return type_of(a0)->base == &vmenum_type;
}
VmObject *System_Enum_ToObject_System_Type_System_Object(DigitoyEngineTypeObj *enum_type, VmObject *value)
{
    const Type *target = type_of(enum_type);
    if (target->base != &vmenum_type)
        DIGITOYENGINE_cast_fail(target, &vmenum_type);
    if (!value)
        DIGITOYENGINE_throw_null();
    long long number;
    switch (value->gc.type->tindex)
    {
    case 4:
        number = DIGITOYENGINE_BOXP(int, value);
        break;
    case 5:
        number = DIGITOYENGINE_BOXP(unsigned int, value);
        break;
    case 6:
        number = DIGITOYENGINE_BOXP(long long, value);
        break;
    case 7:
        number = (long long)DIGITOYENGINE_BOXP(unsigned long long, value);
        break;
    case 8:
        number = DIGITOYENGINE_BOXP(short, value);
        break;
    case 9:
        number = DIGITOYENGINE_BOXP(unsigned short, value);
        break;
    case 10:
        number = DIGITOYENGINE_BOXP(signed char, value);
        break;
    case 11:
    case 12:
        number = DIGITOYENGINE_BOXP(unsigned char, value);
        break;
    default:
        DIGITOYENGINE_cast_fail(value->gc.type, target);
    }
    return digitoyengine_box_enum((int)number, target);
}
int System_Enum_TryParse_System_String_System_Type_out_System_Object(VmString *value, DigitoyEngineTypeObj *enum_type, VmObject **result)
{
    const Type *target = type_of(enum_type);
    int parsed;
    *result = 0;
    if (!value || target->base != &vmenum_type || !target->enum_parse || !target->enum_parse(value, &parsed))
        return 0;
    *result = digitoyengine_box_enum(parsed, target);
    return 1;
}
VmArray *System_Type_GetGenericArguments(DigitoyEngineTypeObj *a0)
{
    const Type *type = type_of(a0);
    VmArray *result = vmarray_new(type->ngeneric_args, sizeof(GCHeader *), 1);
    GCHeader **items = (GCHeader **)result->data;
    for (int i = 0; i < type->ngeneric_args; i++)
        items[i] = digitoyengine_type_wrapper(type->generic_args[i]);
    return result;
}
int System_Type_GetTypeCode_System_Type(DigitoyEngineTypeObj *a0)
{
    switch (type_of(a0)->tindex)
    {
    case 1:
        return 1; /* Object */
    case 2:
        return 18; /* String */
    case 4:
        return 9; /* Int32 */
    case 5:
        return 10; /* UInt32 */
    case 6:
        return 11; /* Int64 */
    case 7:
        return 12; /* UInt64 */
    case 8:
        return 7; /* Int16 */
    case 9:
        return 8; /* UInt16 */
    case 10:
        return 5; /* SByte */
    case 11:
        return 6; /* Byte */
    case 12:
        return 4; /* Char */
    case 13:
        return 3; /* Boolean */
    case 14:
        return 13; /* Single */
    case 15:
        return 14; /* Double */
    default:
        return 1; /* Object: null olmayan her tip (primitive/string disi) - .NET semantigi */
    }
}
GCHeader *System_Type_GetField_System_String(DigitoyEngineTypeObj *a0, VmString *a1)
{
    return digitoyengine_member_lookup(type_of(a0), a1, DIGITOYENGINE_MEMBER_FIELD);
}
GCHeader *System_Type_GetProperty_System_String(DigitoyEngineTypeObj *a0, VmString *a1)
{
    return digitoyengine_member_lookup(type_of(a0), a1, DIGITOYENGINE_MEMBER_PROPERTY);
}
typedef struct DigitoyEngineMemberObj
{
    GCHeader gc;
    long long handle;
} DigitoyEngineMemberObj;
static DigitoyEngineMember *member_of(DigitoyEngineMemberObj *m) { return (DigitoyEngineMember *)(size_t)m->handle; }
VmString *System_Reflection_MemberInfo_get_Name(DigitoyEngineMemberObj *a0) { return (VmString *)member_of(a0)->name; }
DigitoyEngineTypeObj *System_Reflection_MemberInfo_get_DeclaringType(DigitoyEngineMemberObj *a0) { return (DigitoyEngineTypeObj *)digitoyengine_type_wrapper(member_of(a0)->declaringType); }
DigitoyEngineTypeObj *System_Reflection_FieldInfo_get_FieldType(DigitoyEngineMemberObj *a0) { return (DigitoyEngineTypeObj *)digitoyengine_type_wrapper(member_of(a0)->valueType); }
int System_Reflection_FieldInfo_get_IsStatic(DigitoyEngineMemberObj *a0) { return member_of(a0)->isStatic; }
int System_Reflection_FieldInfo_get_IsInitOnly(DigitoyEngineMemberObj *a0) { return member_of(a0)->isInitOnly; }
VmObject *System_Reflection_FieldInfo_GetValue_System_Object(DigitoyEngineMemberObj *a0, VmObject *a1) { return (VmObject *)digitoyengine_member_get(member_of(a0), (GCHeader *)a1); }
void System_Reflection_FieldInfo_SetValue_System_Object_System_Object(DigitoyEngineMemberObj *a0, VmObject *a1, VmObject *a2) { digitoyengine_member_set(member_of(a0), (GCHeader *)a1, (GCHeader *)a2); }
DigitoyEngineTypeObj *System_Reflection_PropertyInfo_get_PropertyType(DigitoyEngineMemberObj *a0) { return (DigitoyEngineTypeObj *)digitoyengine_type_wrapper(member_of(a0)->valueType); }
int System_Reflection_PropertyInfo_get_IsStatic(DigitoyEngineMemberObj *a0) { return member_of(a0)->isStatic; }
int System_Reflection_PropertyInfo_get_CanRead(DigitoyEngineMemberObj *a0) { return member_of(a0)->get != 0; }
int System_Reflection_PropertyInfo_get_CanWrite(DigitoyEngineMemberObj *a0) { return member_of(a0)->set != 0; }
VmObject *System_Reflection_PropertyInfo_GetValue_System_Object(DigitoyEngineMemberObj *a0, VmObject *a1) { return (VmObject *)digitoyengine_member_get(member_of(a0), (GCHeader *)a1); }
void System_Reflection_PropertyInfo_SetValue_System_Object_System_Object(DigitoyEngineMemberObj *a0, VmObject *a1, VmObject *a2) { digitoyengine_member_set(member_of(a0), (GCHeader *)a1, (GCHeader *)a2); }
DigitoyEngineTypeObj *System_Object_GetType(VmObject *a0)
{
    return (DigitoyEngineTypeObj *)digitoyengine_type_wrapper(((GCHeader *)a0)->type);
}
// ---- Exception ----
// LAYOUT SOZLESMESI: uretilen struct Exception ile birebir (GCHeader + bildirim sirali alanlar).
// corelib.c ayri TU oldugundan uretilen tanimi goremez; kopya burada tutulur.
typedef struct DigitoyEngineException
{
    GCHeader gc;
    VmString *message; /* null -> Message default metni uretir */
    long long traceMi[24];
    int traceLine[24];
    int traceCount;
    GCHeader *innerException;
} DigitoyEngineException;
VmString *System_Exception_get_Message(DigitoyEngineException *a0)
{
    if (a0->message)
        return a0->message;
    const VmString *tn = ((GCHeader *)a0)->type->name;
    sb_reset();
    sb_ascii("Exception of type '");
    sb_utf16(tn->data, tn->length);
    sb_ascii("' was thrown.");
    return sb_final(); /* tek alloc */
}
VmString *System_Exception_get_StackTrace(DigitoyEngineException *a0)
{
    sb_reset();
    for (int i = a0->traceCount - 1; i >= 0; i--)
    {
        const MethodInfo *mi = (const MethodInfo *)(size_t)a0->traceMi[i];
        int l = a0->traceLine[i];
        sb_ascii("  at ");
        sb_utf16(mi->name->data, mi->name->length);
        if (mi->file && mi->file->length)
        {
            sb_ascii(" (");
            sb_utf16(mi->file->data, mi->file->length);
            sb_ch(':');
            sb_int(l >> 10);
            sb_ch(':');
            sb_int(l & 1023);
            sb_ch(')');
        }
        else
        {
            sb_ch(':');
            sb_int(l >> 10);
            sb_ch(':');
            sb_int(l & 1023);
        }
        sb_ch('\n');
    }
    return sb_final(); /* frame basina ara string YOK: tek alloc */
}
void System_Exception_Print(DigitoyEngineException *a0)
{
    const VmString *tn = ((GCHeader *)a0)->type->name;
    fputs("[exception] ", stderr);
    vm_write_utf8_to(stderr, tn->data, tn->length);
    fputc('\n', stderr);
    for (int i = a0->traceCount - 1; i >= 0; i--)
    {
        const MethodInfo *mi = (const MethodInfo *)(size_t)a0->traceMi[i];
        int l = a0->traceLine[i];
        fputs("  at ", stderr);
        vm_write_utf8_to(stderr, mi->name->data, mi->name->length);
        if (mi->file && mi->file->length)
        {
            fputs(" (", stderr);
            vm_write_utf8_to(stderr, mi->file->data, mi->file->length);
            fprintf(stderr, ":%d:%d)\n", l >> 10, l & 1023);
        }
        else
            fprintf(stderr, ":%d:%d\n", l >> 10, l & 1023);
    }
}

// ---- System.Text.Encoding (UTF-8) ----
// UTF-8 -> UTF-16 (surrogate ciftli); gecersiz dizilerde U+FFFD. Once uzunluk sayilir, tek tahsis.
static int digitoyengine_utf8_decode_len(const unsigned char *s, int n)
{
    int units = 0;
    for (int i = 0; i < n;)
    {
        unsigned char b = s[i];
        int len = b < 0x80 ? 1 : (b >> 5) == 6 ? 2 : (b >> 4) == 14 ? 3 : (b >> 3) == 30 ? 4 : 1;
        if (i + len > n) len = 1;
        units += len == 4 ? 2 : 1;
        i += len;
    }
    return units;
}
VmString *System_Text_Encoding_Utf8Decode__Byte_Int_Int(VmArray *bytes, int index, int count)
{
    DIGITOYENGINE_NULLCHECK(bytes);
    if (index < 0 || count < 0 || index + count > bytes->len)
        DIGITOYENGINE_throw_bounds(index + count, bytes->len);
    const unsigned char *s = (const unsigned char *)bytes->data + index;
    VmString *r = vmstring_alloc(digitoyengine_utf8_decode_len(s, count));
    unsigned short *d = (unsigned short *)r->data;
    int o = 0;
    for (int i = 0; i < count;)
    {
        unsigned char b = s[i];
        unsigned cp;
        int len;
        if (b < 0x80) { cp = b; len = 1; }
        else if ((b >> 5) == 6 && i + 1 < count) { cp = ((b & 0x1F) << 6) | (s[i + 1] & 0x3F); len = 2; }
        else if ((b >> 4) == 14 && i + 2 < count) { cp = ((b & 0x0F) << 12) | ((s[i + 1] & 0x3F) << 6) | (s[i + 2] & 0x3F); len = 3; }
        else if ((b >> 3) == 30 && i + 3 < count) { cp = ((b & 0x07) << 18) | ((s[i + 1] & 0x3F) << 12) | ((s[i + 2] & 0x3F) << 6) | (s[i + 3] & 0x3F); len = 4; }
        else { cp = 0xFFFD; len = 1; }
        if (cp >= 0x10000)
        {
            cp -= 0x10000;
            d[o++] = (unsigned short)(0xD800 + (cp >> 10));
            d[o++] = (unsigned short)(0xDC00 + (cp & 0x3FF));
        }
        else
            d[o++] = (unsigned short)cp;
        i += len;
    }
    return r;
}
VmArray *System_Text_Encoding_Utf8Encode_System_String(VmString *s)
{
    DIGITOYENGINE_NULLCHECK(s);
    int n = 0;
    for (int i = 0; i < s->length; i++)
    {
        unsigned c = s->data[i];
        if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s->length) { n += 4; i++; }
        else n += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
    }
    VmArray *a = vmarray_new(n, 1, 0);
    unsigned char *d = (unsigned char *)a->data;
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
    return a;
}

int System_String_IsNullOrEmpty_System_String(VmString *s) { return !s || s->length == 0; }
int System_String_IsNullOrWhiteSpace_System_String(VmString *s)
{
    if (!s) return 1;
    for (int i = 0; i < s->length; i++)
        if (!digitoyengine_char_is_whitespace(s->data[i])) return 0;
    return 1;
}

// Type.IsAssignableFrom(other): other == this VEYA other'in kalitim zincirinde VEYA this bir arayuz ve other onu implement eder
int System_Type_IsAssignableFrom_System_Type(DigitoyEngineTypeObj *self, DigitoyEngineTypeObj *other)
{
    if (!other) return 0;
    const Type *t = type_of(self), *o = type_of(other);
    if (DIGITOYENGINE_is(o, t)) return 1;
    return DIGITOYENGINE_implements(o, t);
}
void System_Runtime_InteropServices_Marshal_Copy_Long__Byte_Int_Int(long long source, VmArray *destination, int startIndex, int length)
{
    DIGITOYENGINE_NULLCHECK(destination);
    if (startIndex < 0 || length < 0 || startIndex + length > destination->len)
        DIGITOYENGINE_throw_bounds(startIndex + length, destination->len);
    if (length > 0)
        memcpy((unsigned char *)destination->data + startIndex, (const void *)(size_t)source, (size_t)length);
}

void System_GC_Collect_Int(int generation)
{
    if (generation == 0) { gc_minor(); return; } /* test kancasi: kucuk artimli dilim (bariyer/ic ice gecis sinavi) */
    System_GC_Collect();
}

/* --- AOT player eksikleri: Int.CompareTo/ToString(format), Marshal IntPtr overload'lari, Environment.TickCount, BitConverter --- */
int Int_CompareTo_Int(int self, int value) { return self < value ? -1 : self > value ? 1 : 0; }
VmString *Int_ToString_System_String(int self, VmString *format)
{
    char fmt[16] = "%d";
    if (format && format->length > 0)
    {
        char f0 = (char)format->data[0];
        int width = 0;
        for (int i = 1; i < format->length; i++) width = width * 10 + ((int)format->data[i] - '0');
        if (f0 == 'X' || f0 == 'x') snprintf(fmt, sizeof fmt, width > 0 ? "%%0%d%c" : "%%%c", width > 0 ? width : (int)f0, f0);
        else if (f0 == 'D' || f0 == 'd') { if (width > 0) snprintf(fmt, sizeof fmt, "%%0%dd", width); }
    }
    char buf[40];
    snprintf(buf, sizeof buf, fmt, self);
    return digitoyengine_from_utf8(buf);
}
long long System_Runtime_InteropServices_Marshal_AllocHGlobal_Long(long long cb) { return (long long)(size_t)malloc((size_t)(cb > 0 ? cb : 1)); }
long long System_Runtime_InteropServices_Marshal_ReAllocHGlobal_Long_Long(long long pv, long long cb) { return (long long)(size_t)realloc((void *)(size_t)pv, (size_t)(cb > 0 ? cb : 1)); }
VmString *System_Runtime_InteropServices_Marshal_PtrToStringAnsi_Long(long long ptr) { return ptr ? digitoyengine_from_utf8((const char *)(size_t)ptr) : 0; }
int System_Environment_get_TickCount(void)
{
#ifdef _WIN32
    return (int)GetTickCount();
#else
    struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int)(ts.tv_sec * 1000 + ts.tv_nsec / 1000000);
#endif
}
int System_BitConverter_SingleToInt32Bits_Float(float value) { int r; memcpy(&r, &value, 4); return r; }
float System_BitConverter_Int32BitsToSingle_Int(int value) { float r; memcpy(&r, &value, 4); return r; }
long long System_BitConverter_DoubleToInt64Bits_Double(double value) { long long r; memcpy(&r, &value, 8); return r; }
double System_BitConverter_Int64BitsToDouble_Long(long long value) { double r; memcpy(&r, &value, 8); return r; }
int System_Math_Min_Int_Int(int x, int y) { return x < y ? x : y; }
int System_Math_Max_Int_Int(int x, int y) { return x > y ? x : y; }
void System_SpanOps_Copy_Long_Long_Int(long long dst, long long src, int bytes) { if (bytes > 0) memmove((void *)(size_t)dst, (const void *)(size_t)src, (size_t)bytes); }
void System_SpanOps_Fill_Long_Int_Byte(long long dst, int bytes, unsigned char value) { if (bytes > 0) memset((void *)(size_t)dst, value, (size_t)bytes); }
void System_SpanOps_StoreInt16_Long_Short(long long dst, short value) { memcpy((void *)(size_t)dst, &value, 2); }
void System_SpanOps_StoreInt32_Long_Int(long long dst, int value) { memcpy((void *)(size_t)dst, &value, 4); }

// System.Array non-generic yuzeyi: eleman tipinden bagimsiz (VmArray elemsize). Parametre tipi
// uretilen kodda `struct System_Array*` gorunur; ayni GC nesnesi (VmArray) — ayri TU, isim baglar.
void System_Array_Copy_System_Array_Int_System_Array_Int_Int(VmArray *src, int srcIndex, VmArray *dst, int dstIndex, int length)
{
    DIGITOYENGINE_NULLCHECK(src);
    DIGITOYENGINE_NULLCHECK(dst);
    if (srcIndex < 0 || dstIndex < 0 || length < 0 || srcIndex + length > src->len || dstIndex + length > dst->len || src->elemsize != dst->elemsize)
        DIGITOYENGINE_throw_bounds(srcIndex + length, src->len);
    vmarray_copy(src, srcIndex, dst, dstIndex, length);
}
void System_Array_Copy_System_Array_System_Array_Int(VmArray *src, VmArray *dst, int length)
{
    System_Array_Copy_System_Array_Int_System_Array_Int_Int(src, 0, dst, 0, length);
}
void System_Array_Copy__Byte__Byte_Int(VmArray *src, VmArray *dst, int length)
{
    System_Array_Copy_System_Array_Int_System_Array_Int_Int(src, 0, dst, 0, length);
}
void System_Array_Copy__Byte_Int__Byte_Int_Int(VmArray *src, int srcIndex, VmArray *dst, int dstIndex, int length)
{
    System_Array_Copy_System_Array_Int_System_Array_Int_Int(src, srcIndex, dst, dstIndex, length);
}
void System_Array_Clear_System_Array_Int_Int(VmArray *a, int index, int length)
{
    DIGITOYENGINE_NULLCHECK(a);
    if (index < 0 || length < 0 || index + length > a->len)
        DIGITOYENGINE_throw_bounds(index + length, a->len);
    vmarray_clear(a, index, length);
}
void System_Array_Clear_System_Array(VmArray *a)
{
    DIGITOYENGINE_NULLCHECK(a);
    vmarray_clear(a, 0, a->len);
}

// System.OperatingSystem: derleme hedefi sabitleri.
#if defined(_WIN32)
#define DIGITOYENGINE_OS_WINDOWS 1
#elif defined(__EMSCRIPTEN__)
#define DIGITOYENGINE_OS_BROWSER 1
#elif defined(__ANDROID__)
#define DIGITOYENGINE_OS_ANDROID 1
#elif defined(__APPLE__)
#include <TargetConditionals.h>
#if TARGET_OS_IPHONE
#define DIGITOYENGINE_OS_IOS 1
#else
#define DIGITOYENGINE_OS_MACOS 1
#endif
#elif defined(__linux__)
#define DIGITOYENGINE_OS_LINUX 1
#endif
#ifndef DIGITOYENGINE_OS_WINDOWS
#define DIGITOYENGINE_OS_WINDOWS 0
#endif
#ifndef DIGITOYENGINE_OS_BROWSER
#define DIGITOYENGINE_OS_BROWSER 0
#endif
#ifndef DIGITOYENGINE_OS_ANDROID
#define DIGITOYENGINE_OS_ANDROID 0
#endif
#ifndef DIGITOYENGINE_OS_IOS
#define DIGITOYENGINE_OS_IOS 0
#endif
#ifndef DIGITOYENGINE_OS_MACOS
#define DIGITOYENGINE_OS_MACOS 0
#endif
#ifndef DIGITOYENGINE_OS_LINUX
#define DIGITOYENGINE_OS_LINUX 0
#endif
int System_OperatingSystem_IsWindows(void) { return DIGITOYENGINE_OS_WINDOWS; }
int System_OperatingSystem_IsMacOS(void) { return DIGITOYENGINE_OS_MACOS; }
int System_OperatingSystem_IsIOS(void) { return DIGITOYENGINE_OS_IOS; }
int System_OperatingSystem_IsAndroid(void) { return DIGITOYENGINE_OS_ANDROID; }
int System_OperatingSystem_IsLinux(void) { return DIGITOYENGINE_OS_LINUX; }
int System_OperatingSystem_IsBrowser(void) { return DIGITOYENGINE_OS_BROWSER; }
