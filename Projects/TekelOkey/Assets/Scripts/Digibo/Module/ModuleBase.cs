using DigitoyEngine;

// PORT TABANI: eski Digibo.Module sinifları Group2D/UISprite + IBindingContext idi (gorsel kabuk + `Binding` alanlari;
// cocuk sprite'lar "AlanAdi" ile baglanirdi). Yeni motorda modul GORSEL DEGILDIR: duz Component, yerlesim sahnede
// (LayoutBox vb.). Bagin karsiligi: modul kendi GO'sunun BindingContext MODELI olur — public alanlar ve [Bindable]
// property'ler alt agactaki Binding'lere yol verir (eski `public Binding OnlineCount` → `public int OnlineCount`;
// sahnede Binding.Source="OnlineCount"). Liste: `public List<T> Items` + sahnede ListBinding(Source="Items").
// Model baska bir nesneyse (MEModule → GlobalModel.ME) `Model` override edilir. Pull oldugu icin OnChange/dispatch yok:
// alan degisir, sonraki Sync'te gorunum guncellenir.
namespace Digibo.Module
{
    public abstract class ModuleBase : Component
    {
        // Alt agacin baglandigi model; varsayilan modulun kendisi.
        protected virtual object Model => this;

        protected override void Awake()
        {
            var ctx = GetComponent<BindingContext>() ?? gameObject.AddComponent<BindingContext>();
            ctx.SetModel(Model);
            OnModuleAwake();
        }

        // Turetilenler Awake yerine bunu kullanir (context kurulumu once gelsin).
        protected virtual void OnModuleAwake() { }
    }
}
