using System.Threading.Tasks;
using DigitoyEngine;

// Plugin kullanim ornegi (docs/plugins.md): Get<T>() ile plugin CANLI olur (AOT + native proje). Editorde FacebookMock yanit verir.
public sealed class PluginProbe : Component
{
    public bool loginOnStart = true;

    protected override void Start() { _ = Run(); }

    async Task Run()
    {
        var fb = Plugin.Get<Facebook>();
        var cr = Plugin.Get<Crashlytics>();
        Debug.Log("[probe] Facebook available=" + fb.Available + " appId='" + fb.Settings.appId + "' crashlytics=" + cr.Available);
        if (!string.IsNullOrEmpty(fb.PendingDeepLink)) Debug.Log("[probe] deep link: " + fb.PendingDeepLink);
        if (!loginOnStart) return;
        var r = await fb.Login(10f);
        Debug.Log("[probe] login " + r.Status + (r.Token != null ? " token=" + r.Token : "") + (r.Error != null ? " error=" + r.Error : ""));
        cr.Log("probe login " + r.Status);
    }
}
