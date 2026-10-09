// Tek meta dogrulama (docs/modules.md): Type/DigitoyEngineMember/MethodInfo kayitlari + sekil thunk'lari + Invoke.
// Derleme (aotcompiler/ dizininden, selftest generated.c uretildikten sonra):
//   clang -O1 -w -Ic_runtime -Dmain=digitoyengine_program_main obj/selftest-c/generated.c c_runtime/vmrt.c
//         c_runtime/corelib.c c_runtime/vmint.c tests/meta_check.c -o obj/selftest-c/meta_check.exe
#include "vmrt.h"
#undef main
static int fails = 0;
#define CHECK(cond, msg)                                       \
    do                                                         \
    {                                                          \
        if (cond)                                              \
            fprintf(stderr, "  ok   %s\n", msg);               \
        else                                                   \
        {                                                      \
            fprintf(stderr, "  FAIL %s\n", msg);               \
            fails++;                                           \
        }                                                      \
    } while (0)

void digitoyengine_init(void);

int main(void)
{
    digitoyengine_init();
    int nmethods = digitoyengine_nmethods_misc;
    for (int i = 0; i < digitoyengine_ntypes; i++)
        nmethods += digitoyengine_types[i]->nmethods;
    fprintf(stderr, "[meta-check] types=%d methods=%d (misc %d) thunks=%d\n", digitoyengine_ntypes, nmethods, digitoyengine_nmethods_misc, digitoyengine_nthunks);
    CHECK(digitoyengine_ntypes > 0 && nmethods > 0 && digitoyengine_nthunks > 0, "tablolar dolu");

    // tip: System.Object -> runtime descriptor; ToString sanal slotu kayittan
    const Type *obj = digitoyengine_find_type(de_hash64("System.Object"));
    CHECK(obj == &vmobject_type, "System.Object hash -> &vmobject_type");
    const MethodInfo *ts = digitoyengine_find_method(de_hash64("System.Object$ToString"));
    CHECK(ts && ts->vslot == 2 && ts->declaringType == &vmobject_type, "System.Object$ToString kaydi: slot 2, sahip object");
    CHECK(digitoyengine_find_vslot(&vmstring_type, de_hash64("System.Object$ToString")) == ts, "string uzerinden slot arama (base zinciri)");

    // tip: generic somutlama alan yerlesimi (List<Int>.size): header'dan sonra, nesne boyutundan kucuk
    const Type *list = digitoyengine_find_type(de_hash64("System.Collections.Generic.List`1<Int>"));
    CHECK(list != 0, "List`1<Int> descriptor var");
    if (list)
    {
        DigitoyEngineMember *size = digitoyengine_find_field(list, de_hash64("size"));
        DigitoyEngineMember *items = digitoyengine_find_field(list, de_hash64("items"));
        CHECK(size && size->tag == 'i' && size->size == 4 && size->offset >= sizeof(GCHeader) && size->offset < list->size, "List<Int>.size offset/tag/size");
        CHECK(items && items->tag == 'o' && items->size == sizeof(void *), "List<Int>.items ref alani");
        CHECK(list->nmethods > 0 && list->methods[0].declaringType == list, "List<Int>.methods sahibine isaret ediyor");
    }

    // metot + thunk: ToString'i string nesnesi uzerinde runtime vtable impl'iyle thunk ile cagir
    CHECK(ts && ts->fn && ts->thunk, "System.Object$ToString fn/thunk");
    if (ts)
    {
        VmString *s = vmstring_from_cstr("merhaba");
        const void *impl = s->gc.type->vtable[ts->vslot];
        DeSlot a[1], r;
        a[0].p = s;
        r.p = 0;
        ts->thunk(impl, a, &r);
        CHECK(r.p == s, "String.ToString() thunk ile cagri: ayni string dondu");
        for (int i = 0; i < digitoyengine_nthunks; i++)
            if (digitoyengine_thunks[i] == ts->thunk) fprintf(stderr, "  sekil: %s\n", digitoyengine_shapes[i]);
        // reflection Invoke yolu: ayni kayit, kutulu arguman yok, donus referans
        GCHeader *rr = digitoyengine_method_invoke(ts, (GCHeader *)s, 0);
        CHECK(rr == (GCHeader *)s, "MethodInfo.Invoke(ToString) ayni string dondu");
    }
    // statik alan: herhangi bir tipte adresli
    int staticsSeen = 0;
    for (int i = 0; i < digitoyengine_ntypes && !staticsSeen; i++)
        for (int k = 0; k < digitoyengine_types[i]->nmembers; k++)
            if (digitoyengine_types[i]->members[k].isStatic && digitoyengine_types[i]->members[k].addr) { staticsSeen = 1; break; }
    CHECK(staticsSeen, "statik alan adresi meta'da");
    // olmayan sembol
    CHECK(digitoyengine_find_method(de_hash64("Yok$Boyle_Bir_Metot")) == 0, "olmayan metot -> 0");

    fprintf(stderr, "[meta-check] %s (%d fail)\n", fails ? "FAIL" : "PASS", fails);
    return fails ? 1 : 0;
}
