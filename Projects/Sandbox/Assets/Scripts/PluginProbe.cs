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
        var ads = Plugin.Get<AppLovin>();
        Debug.Log("[probe] Facebook available=" + fb.Available + " appId='" + fb.Settings.appId + "' crashlytics=" + cr.Available + " applovin=" + ads.Available);
        // AppLovin MAX ornegi: init -> rewarded yuklenince goster (mock: hemen; cihazda test unit'leri).
        _ = ProbeAds(ads);
        if (!string.IsNullOrEmpty(fb.PendingDeepLink)) Debug.Log("[probe] deep link: " + fb.PendingDeepLink);
        if (!loginOnStart) return;
        var r = await fb.Login(10f);
        Debug.Log("[probe] login " + r.Status + (r.Token != null ? " token=" + r.Token : "") + (r.Error != null ? " error=" + r.Error : ""));
        cr.Log("probe login " + r.Status);
    }

    static async Task ProbeAds(AppLovin ads)
    {
        bool ok = await ads.Init();
        Debug.Log("[probe] applovin init=" + ok + " geo=" + ads.ConsentFlowUserGeography);
        if (!ok) return;
        for (int i = 0; i < 600 && !ads.Rewarded.IsReady; i++) await Frame.Next(); // en fazla ~10 s
        var show = await ads.Rewarded.Show("probe");
        Debug.Log("[probe] rewarded show=" + show.Status + " reward=" + show.RewardAmount + " " + show.RewardLabel + (show.Error != null ? " error=" + show.Error : ""));
    }
}
