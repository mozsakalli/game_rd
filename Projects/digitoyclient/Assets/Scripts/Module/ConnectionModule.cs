using DigitoyEngine;

namespace Digitoy;

// BINDING ORNEGI (pull): bu component kendi GO'sunun BindingContext MODELI'dir. Alt agactaki Binding'ler
// Source="Status" / "IsConnected" / "IsBusy" ile buradaki property'leri okur; her frame BindingSystem
// degeri ceker, degistiyse hedefe yazar. Setter/event/Update YOK — Server degisir, gorunum kendiliginden guncellenir.
public class ConnectionModule : Component
{
    [Bindable] public string Status => Server.Current.ToString();   // -> TextSprite.Text
    [Bindable] public bool IsConnected => Server.IsConnected;        // -> "Online" ikonunun Transform.Active'i
    [Bindable] public bool IsBusy => !Server.IsConnected;            // -> spinner Active (donusturucu yok, ters property yaz)
    [Bindable] public string Error => Server.LastError;

    protected override void Awake()
    {
        var ctx = GetComponent<BindingContext>() ?? gameObject.AddComponent<BindingContext>();
        ctx.SetModel(this);
    }
}