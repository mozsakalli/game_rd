using DigitoyEngine;

// ORNEK PLUGIN (docs/plugins.md): Firebase Crashlytics — KODSUZ. Unhandled managed exception zaten Java/NSException'a cevrildigi
// (de_host_fatal) icin Crashlytics bunu kendi yakalar; plugin yalniz bagimliliklari (Editor/ kancalari) ve core'un yakaladigi
// plugin hatalarini non-fatal olarak iletmeyi (Android/, iOS/ DEPlugin.errorReporter) ekler. Runtime API yok; Get<Crashlytics>()
// cagrisi yalniz plugin'i CANLI yapmak icin (kullanilmayan plugin projeye girmez).
public sealed class Crashlytics : Plugin<Crashlytics.Config>
{
    [System.Serializable]
    public sealed class Config
    {
        public bool collectionEnabled = true;
    }

    public void Log(string message) => Send("{\"method\":\"log\",\"message\":\"" + message.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
    public void SetUserId(string id) => Send("{\"method\":\"userId\",\"id\":\"" + id + "\"}");
}
