using System;
using System.Collections.Generic;
using System.IO;

namespace DigitoyEngine;

// Unity AsyncOperation muadili — POLL tabanli (bloklamaz). Custom AOT'de await
// coroutine'e indigi icin motor tarafinda poll edilebilir olmasi yeterli;
// WASM'de de ayni sozlesme calisir (thread yok, platform async'i tamamlar).
public sealed class LoadOp
{
    public bool IsDone { get; internal set; }
    public bool Failed { get; internal set; }
    internal LoadOp Next; // ayni asset'i bekleyen op zinciri
}

// Asset anahtari = kok klasore GORELI yol ("orb.png"). GUID/.meta sonra.
// Yukleme ASYNC-VARSAYILAN: handle ANINDA doner (sahne kurulumu beklemez),
// dosya+decode native worker'da kosar, Tick frame basina butceli baglar —
// texture gelene kadar 1x1 beyaz placeholder cizilir, oyun dongusu takilmaz.
public sealed class AssetDatabase
{
    struct Pending
    {
        public int Job;
        public Texture Target;
        public LoadOp Op;
    }

    readonly string _root; // yalniz loose kaynakta dolu (editor yollari icin)
    readonly AssetSource _source;
    readonly Dictionary<string, Texture> _textures = new();
    readonly Dictionary<string, Sprite> _sprites = new();
    readonly Dictionary<string, Prefab> _prefabs = new();
    readonly Dictionary<string, Font> _fonts = new();
    readonly List<Pending> _pending = new();
    readonly Dictionary<string, string> _guidToPath = new();
    readonly Dictionary<string, string> _pathToGuid = new();

    // Import edilen asset'lerin baytlari editorde Library/Artifacts'ten gelir
    // (ImportPipeline baglar); release'te null -> pak zaten artifact'i tasir.
    public Func<string, byte[]> ArtifactResolver;

    // Editor/dev: loose dosyalar (ScanMetas ile guid tablosu kurulur).
    public AssetDatabase(string root)
    {
        _root = root;
        _source = new LooseFileSource(root);
    }

    // Release: pak gibi kaynaklar guid tablosunu kendileri getirir; tarama yok.
    public AssetDatabase(AssetSource source)
    {
        _source = source;
        _root = (source as LooseFileSource)?.Root;
        source.FillGuidTable(_guidToPath, _pathToGuid);
    }

    public string Root => _root;
    public AssetSource Source => _source;

    // GUID kimligi: her asset dosyasinin yaninda <ad>.meta (guid: hex32). Sahneler
    // GUID referanslar — dosya tasinsa/adi degisse referans kirilmaz (Unity modeli).
    // createMissing=true yalniz editorde: eksik meta uretilir, sahipsiz meta silinir.
    public void ScanMetas(bool createMissing)
    {
        if (_root == null)
            return; // pak kaynagi: tablo ctor'da kaynaktan geldi
        _guidToPath.Clear();
        _pathToGuid.Clear();
        if (!Directory.Exists(_root))
            return;
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                if (createMissing && !File.Exists(file.Substring(0, file.Length - 5)))
                    File.Delete(file); // sahipsiz meta
                continue;
            }
            string meta = file + ".meta";
            string guid = null;
            if (File.Exists(meta))
                guid = Yaml.Parse(File.ReadAllText(meta)).GetScalar("guid");
            if (string.IsNullOrEmpty(guid))
            {
                if (!createMissing)
                    continue;
                guid = Guid.NewGuid().ToString("N");
                File.WriteAllText(meta, "guid: " + guid + "\n");
            }
            string rel = Path.GetRelativePath(_root, file).Replace('\\', '/');
            _guidToPath[guid] = rel;
            _pathToGuid[rel] = guid;
        }
    }

    public string PathToGuid(string relPath) => _pathToGuid.GetValueOrDefault(relPath);

    // Editor asset taramasi (Proje paneli): gorelipath -> guid.
    public IReadOnlyDictionary<string, string> AllAssets => _pathToGuid;

    // Uzanti -> yuklenebilir runtime tipi (importer kaydi tohumu). Yeni asset
    // turleri buraya kayitla gelir — panel/inspector kodu tip bilmez.
    static readonly Dictionary<string, Type> _importers = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = typeof(Sprite),
        [".jpg"] = typeof(Sprite),
        [".jpeg"] = typeof(Sprite),
        [".prefab"] = typeof(Prefab),
        [".ttf"] = typeof(Font),
        [".otf"] = typeof(Font),
    };

    public static void RegisterImporter(string extension, Type type) => _importers[extension] = type;

    // Serilesebilir asset-referans tipi mi: bir uzantidan yuklenebilen IAsset.
    // (Kayit defteri = tek kaynak; yeni asset turu RegisterImporter'la buraya da girer.)
    public static bool IsAssetType(Type t)
    {
        if (!typeof(IAsset).IsAssignableFrom(t))
            return false;
        if (t == typeof(Texture))
            return true; // dogrudan doku alanlari (RT hedefi vs.) asset-referans kalir
        foreach (var v in _importers.Values)
            if (v == t)
                return true;
        return false;
    }

    // Bilesik uzantilar da denenir (".prefab.yaml" gibi): ilk noktadan itibaren.
    public static Type ImportTypeOf(string path)
    {
        if (path == null)
            return null;
        string name = Path.GetFileName(path);
        int i = name.IndexOf('.');
        while (i >= 0)
        {
            if (_importers.TryGetValue(name.Substring(i), out var t))
                return t;
            i = name.IndexOf('.', i + 1);
        }
        return null;
    }

    // Bu asset verilen alana atanabilir mi (alan tipi, asset'in yuklenebilir tipi).
    public bool IsAssignable(string relPath, Type fieldType)
    {
        var t = ImportTypeOf(relPath);
        return t != null && fieldType.IsAssignableFrom(t);
    }

    // Anahtar guid ise yola cevirir; degilse oldugu gibi (insan elle yol da yazabilir).
    public string ResolvePath(string keyOrGuid)
        => keyOrGuid != null && _guidToPath.TryGetValue(keyOrGuid, out var p) ? p : keyOrGuid;

    // Serilestirme (Kind.Asset) tek bogazdan yukler — alan tipi hangi asset'se o.
    public object LoadAsset(string keyOrGuid, Type type)
    {
        if (type == typeof(Sprite))
            return LoadSprite(keyOrGuid);
        if (type == typeof(Texture))
            return LoadTexture(keyOrGuid);
        if (type == typeof(Font))
            return LoadFont(keyOrGuid);
        return null;
    }

    // Cizilebilir bolge: varsayilan tum-sayfa (kaynak png). Atlas builder/loader
    // ayni instance'i BindRegion ile yerinde yeniden baglar — referanslar bozulmaz.
    public Sprite LoadSprite(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_sprites.TryGetValue(key, out var s))
            return s;
        s = Sprite.FromTexture(LoadTexture(key), key);
        _sprites[key] = s;
        return s;
    }

    // Prebaked font: baytlar artifact'ten (editor: Library, release: pak) SENKRON
    // gelir — metrikler layout girdisi oldugu icin async placeholder anlamsiz.
    public Font LoadFont(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_fonts.TryGetValue(key, out var f))
            return f;
        var bytes = ArtifactResolver?.Invoke(key) ?? _source.ReadBytes(key);
        f = bytes != null ? Font.FromArtifact(bytes, key) : null;
        if (f != null)
            _fonts[key] = f; // basarisiz yukleme cache'lenmez (sonraki deneme taze okur)
        return f;
    }

    // Dis degisiklik (reimport): cache dusurulur, sonraki LoadFont taze artifact okur.
    // Canli referanslar eski Font'ta kalir (sahne reload'unda tazelenir).
    public bool InvalidateImported(string relPath) => _fonts.Remove(relPath);

    public Texture LoadTexture(string key, LoadOp op = null)
    {
        key = ResolvePath(key); // guid -> yol (cache anahtari hep yol)
        if (string.IsNullOrEmpty(key))
            return null;
        if (_textures.TryGetValue(key, out var t))
        {
            if (op != null)
                AttachOp(t, op); // hala yukleniyorsa zincire, degilse hemen done
            return t;
        }

        t = Texture.CreatePending();
        t.Name = key;
        _textures[key] = t;
        int job = _source.StartLoad(key);
        if (job < 0)
        {
            t.Loading = false; // kuyruk dolu: placeholder'da kalir
            if (op != null) { op.IsDone = true; op.Failed = true; }
            return t;
        }
        _pending.Add(new Pending { Job = job, Target = t, Op = op });
        return t;
    }

    // Prefab = alt agac SceneDoc'u. Metin kucuk oldugu icin simdilik senkron okunur.
    public Prefab LoadPrefab(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_prefabs.TryGetValue(key, out var p))
            return p;
        var bytes = _source.ReadBytes(key);
        if (bytes == null)
            return null;
        p = new Prefab { Key = key, Doc = SceneDoc.Parse(System.Text.Encoding.UTF8.GetString(bytes)), Assets = this };
        _prefabs[key] = p;
        return p;
    }

    // Dis degisiklik: YUKLU texture'in pikselleri yerinde tazelenir — sahnedeki
    // referanslar ve play durumu hic bozulmaz. Yuklu degilse is yok (sonraki
    // LoadTexture zaten diskten taze okur).
    public bool ReloadTexture(string relPath)
    {
        if (!_textures.TryGetValue(relPath, out var t))
            return false;
        int job = _source.StartLoad(relPath);
        if (job < 0)
            return false;
        _pending.Add(new Pending { Job = job, Target = t });
        return true;
    }

    // Dis degisiklik: prefab cache'i duser, sonraki LoadPrefab diskten okur.
    public bool InvalidatePrefab(string relPath) => _prefabs.Remove(relPath);

    // Bekleyen async yukleme sayisi (atlas pack gibi toplu isler "hepsi indi mi" diye bakar).
    public int PendingCount => _pending.Count;

    // Frame basi cagrilir; frame basina en fazla `budget` sonuc baglanir
    // (yavas cihazda spike yerine birkac frame'e yayilir).
    public void Tick(int budget = 2)
    {
        for (int i = _pending.Count - 1; i >= 0 && budget > 0; i--)
        {
            var p = _pending[i];
            int r = Sokol.AssetPoll(p.Job, out var pixels, out int w, out int h);
            if (r == 0)
                continue;
            if (r == 1)
                p.Target._AttachPixels(pixels, w, h);
            else
                p.Target.Loading = false; // hata: placeholder'da kalir
            Sokol.AssetFreeJob(p.Job);
            Complete(p.Op, r < 0);
            _pending.RemoveAt(i);
            budget--;
        }
    }

    void AttachOp(Texture t, LoadOp op)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].Target != t)
                continue;
            var p = _pending[i];
            op.Next = p.Op;
            p.Op = op;
            _pending[i] = p;
            return;
        }
        op.IsDone = true; // zaten yuklu
    }

    static void Complete(LoadOp op, bool failed)
    {
        while (op != null)
        {
            op.IsDone = true;
            op.Failed = failed;
            op = op.Next;
        }
    }
}
