# Unity Demo — Kodsuz Remote Prefab + Runtime Davranış Bağlama

Remote yüklenen prefab'lar (offer dialog, minigame…) **oyun script'i içermez**; davranış oyun kodundan
runtime'da `AddComponent` ile takılır. Prefab ile kod arasındaki tek köprü, **asla değişmeyen** minik bir
assembly'deki `ViewRefs` script'idir. Böylece oyun kodu ne kadar değişirse değişsin bundle/oyun arasında
script hash / type-tree uyumsuzluğu oluşmaz.

## Kurulum (herhangi bir Unity 2020.3+ projesi, uGUI yeterli, ek paket yok)

1. `Assets/` içeriğini projenin `Assets/` altına kopyala.
2. **Tools ▸ ViewBinding Demo ▸ Build Demo Prefabs** → `Assets/Resources/Views/*.prefab` üretilir
   (prefab'lar kodla üretilir; elle YAML yok). Hiyerarşide incele: yalnızca Image/Text/Button + kökte `ViewRefs`.
3. **Tools ▸ ViewBinding Demo ▸ Validate View Prefabs** → sözleşme denetimi (yabancı script yok mu, viewId'nin
   controller'ı var mı, tüm `[Bind]` key'leri çözülüyor mu). Bundle build'inden önce CI'da da koşturulabilir.
4. Boş bir sahneye `DemoBootstrap` component'i koy, Play. "Open Offer" / "Open Wheel" butonları prefab'ı
   yükler, controller'ı takar.

## Yapı

```
Assets/
  Game.ViewContract/          ← DONMUŞ assembly: bir kez yaz, bir daha dokunma
    ViewRefs.cs               key → Object listesi + viewId (prefab köküne konur, drag-drop ile doldurulur)
    link.xml                  IL2CPP stripping'den koru
  Game/Runtime/Views/         ← oyun kodu, istediğin kadar değişir
    ViewBinder.cs             [Bind("key")] alanlarını ViewRefs'ten (yoksa "@key" isimli GO'dan) doldurur
    ViewController.cs         AddComponent ile takılan davranış tabanı (Initialize / Close)
    ViewLoader.cs             ViewRegistry (viewId→tip), IViewSource (Resources demo / Addressables üretim), Attach()
    OfferDialog.cs            örnek dialog davranışı
    WheelMiniGame.cs          örnek minigame davranışı
    DemoBootstrap.cs          demo giriş + controller kayıtları
  Game/Editor/
    DemoPrefabBuilder.cs      demo prefab'larını üretir
    ViewPrefabValidator.cs    sözleşme denetimi
  Resources/Views/            demo prefab çıktısı (üretimde Addressables remote grup)
```

## Akış

```
ViewLoader.Open("OfferDialog", layer, offerData)
  → IViewSource yükler (Resources / Addressables)
  → prefab KAPALI instantiate edilir (controller Awake'i Initialize'dan önce koşmasın)
  → kökteki ViewRefs.viewId → ViewRegistry → controller tipi → AddComponent
  → ViewBinder.Bind: [Bind] alanları ViewRefs'ten / "@isim"den dolar
  → OnInitialize(): metinler, AddListener'lar, intro animasyonu
  → SetActive(true)
```

## Kurallar (prefab tarafı)

- Prefab'ta **yalnızca Unity built-in component'leri + kökte `ViewRefs`**. Oyun assembly'sinden tek bir script bile olmaz.
- `Button.onClick` vb. persistent listener'lar **boş** kalır; her şey kodda `AddListener`.
- Animation Event kullanılabilir: clip'e `OnIntroDone` yazılır, runtime'da takılan controller'daki `void OnIntroDone()`
  isimle çağrılır — script referansı gerekmez. `OnCollision*/OnTrigger*/OnPointer*` da runtime'da eklenen component'te çalışır.
- Referans vermek için iki yol; ikisi birden desteklenir:
  - `ViewRefs.refs` listesine key + drag-drop (isim/hiyerarşi değişimine dayanıklı — **önerilen**),
  - ya da objeyi `@key` diye adlandır (hiç inspector işi yok, isim kırılgan).
- `[Bind("x", Optional = true)]`: prefab'ın eski sürümünde olmayabilecek öğeler için.

## Üretimde Addressables

`IViewSource`'un Addressables uygulaması (Game asmdef'ine `Unity.Addressables` + `Unity.ResourceManager` referansı ekle):

```csharp
public sealed class AddressablesViewSource : IViewSource
{
    public void Load(string address, Action<GameObject> onLoaded)
    {
        Addressables.LoadAssetAsync<GameObject>(address).Completed += h =>
            onLoaded(h.Status == AsyncOperationStatus.Succeeded ? h.Result : null);
        // Not: handle'ı sakla, view kapanınca Addressables.Release(handle).
    }
}
```

Dikkat:
- `Game.ViewContract` asmdef'i remote prefab'ların tek script bağımlılığı; **alan ekleme/çıkarma/yeniden adlandırma yapma**.
  Yeni bir şey lazımsa `refs` listesine yeni key eklenir, tip değişmez.
- Addressables'ta **type tree'yi kapatma** (Disable Type Tree = kapalı kalsın) — alanlar isimle eşlenir.
- Shader / font / ortak materyalleri ayrı bir "shared" gruba al; her prefab bundle'ı kopyasını gömmesin.
- `link.xml` bu klasörde; IL2CPP'de `ViewRefs` strip edilmez.
