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
    // Oyun kodu API'si. parent == null: sahne YENI bir Scene'e yuklenir, hazir olunca aktif olur
    // ve eski aktif sahne atilir (Unity Single). parent != null: aktif sahneye, parent altina (Additive).
    public static SceneLoadOp LoadAsync(string key, Transform parent = null)
    {
        var s = Scene.Active;
        return LoadAsync(key, s.Assets, s.Catalog, parent, replace: parent == null);
    }

    // Alt seviye (host/modul): replace=false ile aktif sahneye eklenir, hicbir sahne atilmaz.
    public static SceneLoadOp LoadAsync(string key, AssetDatabase assets, TypeCatalog catalog, Transform parent = null, bool replace = false)
    {
        var op = new SceneLoadOp();
        _ = Run(op, key, assets, catalog, parent, replace); // async void yok (corelib: yalniz Task builder'lari)
        return op;
    }

    static async Task<bool> Run(SceneLoadOp op, string key, AssetDatabase assets, TypeCatalog catalog, Transform parent, bool replace)
    {
        try
        {
            key = assets.ResolvePath(key);
            if (!assets.Exists(key) && assets.Exists(key + ".scene"))
                key += ".scene"; // Unity gibi uzantisiz sahne adi
            // 1) CPU: bagimlilik grafigi + sahne baytlari
            bool ok = await assets.EnsureLoadedAsync(key, op.Progress);
            var bytes = await assets.PreloadAsync(key);
            if (bytes == null)
            {
                Fail(op, "sahne bulunamadi: " + key);
                return false;
            }
            if (!ok)
                AssetDatabase.LogWarning?.Invoke("[scene] bazi bagimliliklar yuklenemedi: " + key);
            bool baked = SceneBinary.IsBaked(bytes);
#if !DE_EDITOR
            if (!baked)
            {
                Fail(op, "sahne baked degil (pak'i yeniden build edin): " + key);
                return false;
            }
#endif
            // 2) GPU: dokular + shader'lar (ilk cizim frame'ine is kalmasin)
            await assets.UploadAsync();
            // 3) Spawn
            while (!op.AllowActivation)
                await Frame.Next();
            Scene old = null, target;
            if (parent != null)
                target = parent.gameObject.scene;
            else if (!replace)
                target = Scene.Active;
            else
            {
                old = Scene.Active;
                target = Scene.Create(key);
                target.Catalog = catalog;
                target.Assets = assets;
                Scene.SetActive(target); // Spawn aktif sahneye dogar
            }
            // Bu frame'in dt'si son upload frame'inin, sonrakinin dt'si Spawn'in suresi: ikisi de atilir.
            target.DiscardDeltaFrames = 2;
#if DE_EDITOR
            if (!baked)
            {
                var doc = SceneDoc.Parse(System.Text.Encoding.UTF8.GetString(bytes));
                doc.ExpandPrefabs(catalog, assets);
                op.Root = doc.Spawn(parent, catalog, assets);
            }
            else
#endif
                op.Root = SceneBinary.Spawn(bytes, parent, catalog, assets);
            if (old != null && old != target)
                Scene.Unload(old);
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
        AssetDatabase.LogWarning?.Invoke("[scene] " + error);
        op.Error = error;
        op.Failed = true;
        op.IsDone = true;
        op._tcs.TrySetResult(false);
    }
}
