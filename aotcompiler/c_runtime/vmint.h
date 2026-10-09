// Dinamik modul interpreter'i (docs/modules.md Faz C). .dmod (ModuleWriter) yukler, dis referanslari host export
// tablolarina (vmrt.h De*Export) parent-first baglar, yerel tipler icin dinamik Type descriptor'lari kurar ve Op IR'i
// yorumlar. GC/exception/shadow-stack mekanizmasi AOT koduyla AYNIDIR (ayni heap, ayni RtTry zinciri).
#ifndef VMINT_H
#define VMINT_H
#include "vmrt.h"

typedef struct VmModule VmModule;

// Yukle + bagla (eager). name: tani etiketi ([mod:name] trace, loglar). Hata: 0 doner, err'e tek satir aciklama.
VmModule *vmint_load(const unsigned char *data, int len, const char *name, char *err, int errcap);
// Modulu kapat: state=unloaded (trampoline'ler no-op), kokler birakilir; bellek canli nesne kalmayinca (GC) serbest.
void vmint_unload(VmModule *m);
// Host->modul sinirinda yakalanmamis exception (host'ta hicbir handler yokken) -> modul FAULT: no-op'a alinir, rapor
// basilir, host bunu gorup Unload eder (Module.TickAll). Donus 1 = fault'lu.
int vmint_faulted(VmModule *m);
// Unload edilmis ve live==0 moduller icin bellegi serbest birakir (host GC adimi sonrasi cagirir). Donus: serbest kalan sayisi.
int vmint_collect(void);
// Giris metodu (.dmod entry; selftest Main gibi parametresiz static). Yoksa -1.
int vmint_run_entry(VmModule *m);
// Isimle metot bul + cagir (DeSlot ABI: thunk sozlesmesiyle ayni). Donus 0 = bulunamadi.
int vmint_call(VmModule *m, const char *encodedName, DeSlot *args, DeSlot *ret);
// Tek referans argumanli static yerel metot. Donus 0 = bulunamadi.
int vmint_call_obj(VmModule *m, const char *encodedName, void *arg0);
// Modul tipleri (host katalog kaydi, Module.cs): i. tipin System.Type wrapper'i; yerel class/struct degilse 0.
int vmint_type_count(VmModule *m);
GCHeader *vmint_type_at(VmModule *m, int i);
void vmint_set_name(VmModule *m, const char *name);
// host -> modul girisleri (uretilen trampoline'ler cagirir)
void vmint_enter_virtual(GCHeader *self, unsigned long long rootHash, DeSlot *a, DeSlot *r);
void vmint_enter_delegate(GCHeader *closure, DeSlot *a, DeSlot *r);
// tani
int vmint_live(VmModule *m);
const char *vmint_name(VmModule *m);
#endif
