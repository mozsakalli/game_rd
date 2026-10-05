using System.Threading.Tasks;

namespace DigitoyEngine;

// Sahne yukleme islemi (Unity AsyncOperation benzeri). Oyun kodu her frame
// Progress/IsDone okur; AllowActivation=false ile hazir sahneyi bekletir
// (preload ekrani: "devam" tusuna kadar Spawn edilmez).
public sealed class SceneLoadOp
{
    public readonly LoadProgress Progress = new();
    public bool IsDone { get; internal set; }
    public bool Failed { get; internal set; }
    public string Error { get; internal set; }
    public bool AllowActivation = true;
    public GameObject Root { get; internal set; } // Spawn'in dondurdugu ilk kok

    public Task<bool> Task => _tcs.Task;
    internal readonly TaskCompletionSource<bool> _tcs = new();
}

// Release sahne yukleyicisi: pak bagimlilik grafigi uzerinden sahnenin TUM
// asset'leri (atlas/prefab/font/ses/texture) hazir olana kadar bekler, sonra
// pismis baytlardan tek frame'de Spawn eder. Hicbir adim bloklamaz (web uyumlu).
public static class SceneLoader
{
    public static SceneLoadOp LoadAsync(string key, AssetDatabase assets, TypeCatalog catalog, Transform parent = null)
    {
        var op = new SceneLoadOp();
        _ = Run(op, key, assets, catalog, parent); // async void yok (corelib: yalniz Task builder'lari)
        return op;
    }

    static async Task<bool> Run(SceneLoadOp op, string key, AssetDatabase assets, TypeCatalog catalog, Transform parent)
    {
        try
        {
            key = assets.ResolvePath(key);
            bool ok = await assets.EnsureLoadedAsync(key, op.Progress);
            var bytes = await assets.PreloadAsync(key);
            if (bytes == null)
            {
                Fail(op, "sahne bulunamadi: " + key);
                return false;
            }
            if (!ok)
                AssetDatabase.LogWarning?.Invoke("[scene] bazi bagimliliklar yuklenemedi: " + key);
            if (!SceneBinary.IsBaked(bytes))
            {
                Fail(op, "sahne baked degil (pak'i yeniden build edin): " + key);
                return false;
            }
            while (!op.AllowActivation)
                await Frame.Next();
            op.Root = SceneBinary.Spawn(bytes, parent, catalog, assets);
            op.IsDone = true;
            op._tcs.TrySetResult(true);
            return true;
        }
        catch (System.Exception e)
        {
            Fail(op, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            return false;
        }
    }

    static void Fail(SceneLoadOp op, string error)
    {
        op.Error = error;
        op.Failed = true;
        op.IsDone = true;
        op._tcs.TrySetResult(false);
    }
}
