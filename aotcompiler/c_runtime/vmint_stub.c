// vmint yerine derlenen bos uygulama (trim profili lean: modul host KAPALI, docs/engine-notes.md TRIM).
// Module.cs P/Invoke'lari ve vmrt.h'in vmint kancalari baglansin; yukleme her zaman "moduller kapali" hatasi verir.
#include "vmint.h"
#include <string.h>

VmModule *vmint_load(const unsigned char *data, int len, const char *name, char *err, int errcap)
{
    (void)data;
    (void)len;
    (void)name;
    if (err && errcap > 0)
    {
        strncpy(err, "dynamic modules disabled (lean build)", (size_t)errcap - 1);
        err[errcap - 1] = 0;
    }
    return 0;
}
void vmint_unload(VmModule *m) { (void)m; }
int vmint_faulted(VmModule *m)
{
    (void)m;
    return 0;
}
int vmint_collect(void) { return 0; }
int vmint_run_entry(VmModule *m)
{
    (void)m;
    return -1;
}
int vmint_call(VmModule *m, const char *encodedName, DeSlot *args, DeSlot *ret)
{
    (void)m;
    (void)encodedName;
    (void)args;
    (void)ret;
    return 0;
}
int vmint_call_obj(VmModule *m, const char *encodedName, void *arg0)
{
    (void)m;
    (void)encodedName;
    (void)arg0;
    return 0;
}
int vmint_type_count(VmModule *m)
{
    (void)m;
    return 0;
}
GCHeader *vmint_type_at(VmModule *m, int i)
{
    (void)m;
    (void)i;
    return 0;
}
void vmint_set_name(VmModule *m, const char *name)
{
    (void)m;
    (void)name;
}
void vmint_enter_virtual(GCHeader *self, unsigned long long rootHash, DeSlot *a, DeSlot *r)
{
    (void)self;
    (void)rootHash;
    (void)a;
    (void)r;
}
void vmint_enter_delegate(GCHeader *closure, DeSlot *a, DeSlot *r)
{
    (void)closure;
    (void)a;
    (void)r;
}
int vmint_live(VmModule *m)
{
    (void)m;
    return 0;
}
const char *vmint_name(VmModule *m)
{
    (void)m;
    return "";
}
GCHeader *vmint_attr_create(DeAttr *a)
{
    (void)a;
    return 0;
}
int vmint_try_invoke(const void *fn, DeSlot *a, DeSlot *r)
{
    (void)fn;
    (void)a;
    (void)r;
    return 0;
}
