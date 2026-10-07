// Faz A dogrulama: host export tablolari (vmrt.h DeTypeExport/DeMethodExport) ve imza-sekli thunk'lari.
// Derleme (aotcompiler/ dizininden, selftest generated.c uretildikten sonra, AOT_MODULES=1):
//   clang -O1 -w -Ic_runtime -Dmain=digitoyengine_program_main obj/selftest-c/generated.c c_runtime/vmrt.c
//         c_runtime/corelib.c tests/export_check.c -o obj/selftest-c/export_check.exe
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
    fprintf(stderr, "[export-check] types=%d methods=%d statics=%d thunks=%d\n", de_host_ntypes, de_host_nmethods, de_host_nstatics, de_host_nthunks);
    CHECK(de_host_ntypes > 0 && de_host_nmethods > 0 && de_host_nthunks > 0, "tablolar dolu");

    // tip: System.Object -> descriptor vmobject_type, vtable slot adlari
    const DeTypeExport *obj = de_host_find_type(de_hash64("System.Object"));
    CHECK(obj && obj->type == &vmobject_type, "System.Object export -> &vmobject_type");
    int tostringSlot = -1;
    if (obj)
        for (int i = 0; i < obj->nvslots; i++)
            if (obj->vslots[i].hash == de_hash64("System.Object$ToString"))
                tostringSlot = obj->vslots[i].slot;
    CHECK(tostringSlot == 2, "System.Object$ToString slot == 2");
    CHECK(obj && de_host_type_of(obj->type) == obj, "de_host_type_of geri eslesme");

    // tip: generic somutlama alan offset'i (List<Int>.size): header'dan sonra, nesne boyutundan kucuk
    const DeTypeExport *list = de_host_find_type(de_hash64("System.Collections.Generic.List`1<Int>"));
    CHECK(list != 0, "List`1<Int> export var");
    if (list)
    {
        const DeFieldExport *size = 0, *items = 0;
        for (int i = 0; i < list->nfields; i++)
        {
            if (list->fields[i].hash == de_hash64("System.Collections.Generic.List`1<Int>$size")) size = &list->fields[i];
            if (list->fields[i].hash == de_hash64("System.Collections.Generic.List`1<Int>$items")) items = &list->fields[i];
        }
        CHECK(size && size->tag == 'i' && size->size == 4 && size->offset >= sizeof(GCHeader) && size->offset < list->size, "List<Int>.size offset/tag/size");
        CHECK(items && items->tag == 'o' && items->size == sizeof(void *), "List<Int>.items ref alani");
        CHECK(list->type && list->type->size == list->size, "List<Int> Type.size == export size");
    }

    // metot + thunk: System.Object$ToString'i export'tan al, bir string nesnesi uzerinde thunk ile cagir
    const DeMethodExport *ts = de_host_find_method(de_hash64("System.Object$ToString"));
    CHECK(ts && ts->fn && ts->shape < de_host_nthunks, "System.Object$ToString export");
    if (ts)
    {
        VmString *s = vmstring_from_cstr("merhaba");
        // sanal: nesnenin runtime vtable'indan impl (string override'i) -> ayni sekil thunk'i
        const void *impl = s->gc.type->vtable[tostringSlot];
        DeSlot a[1], r;
        a[0].p = s;
        r.p = 0;
        de_host_thunks[ts->shape](impl, a, &r);
        CHECK(r.p == s, "String.ToString() thunk ile cagri: ayni string dondu");
        fprintf(stderr, "  sekil: %s\n", de_host_shapes[ts->shape]);
    }

    // statik alan: herhangi biri adresli ve bulunabilir
    if (de_host_nstatics > 0)
    {
        const DeFieldExport *st = de_host_find_static(de_host_statics[0].hash);
        CHECK(st == &de_host_statics[0] && st->addr != 0 && st->isStatic, "statik alan lookup");
    }
    // olmayan sembol
    CHECK(de_host_find_method(de_hash64("Yok$Boyle_Bir_Metot")) == 0, "olmayan metot -> 0");

    fprintf(stderr, "[export-check] %s (%d fail)\n", fails ? "FAIL" : "PASS", fails);
    return fails ? 1 : 0;
}
