using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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

// Bagimlilik grafigi yuklemesinin ilerlemesi (dugum sayisi; preload ekrani okur).
public sealed class LoadProgress
{
    public int Total { get; private set; }
    public int Done { get; private set; }
    internal int Pending; // baska yukleme tarafindan baslatilmis, bizim de bekledigimiz
    public float Value => Total == 0 ? 1f : (float)Done / Total;
    internal void Begin() => Total++;
    internal void End() => Done++;
}

// Asset anahtari = kok klasore GORELI yol ("orb.png"). GUID/.meta sonra.
// Yukleme ASYNC-VARSAYILAN: handle ANINDA doner (sahne kurulumu beklemez),
// dosya+decode native worker'da kosar, Tick frame basina butceli baglar —
// texture gelene kadar 1x1 beyaz placeholder cizilir, oyun dongusu takilmaz.
public sealed class AssetDatabase
{
    // Bekleyen texture yuklemesi; tamamlanma Pump'ta (Listen) buraya duser, Tick butceli baglar.
    sealed class Pending
    {
        public int Job;
        public Texture Target;
        public LoadOp Op;
        public bool Done;
        public Completion Result;
    }

    readonly string _root; // yalniz loose kaynakta dolu (editor yollari icin)
    readonly AssetSource _source;
    readonly Dictionary<string, Texture> _textures = new();
    readonly Dictionary<string, Sprite> _sprites = new();
    readonly Dictionary<string, Prefab> _prefabs = new();
    readonly List<Pending> _pending = new();
    readonly Dictionary<string, string> _guidToPath = new();
    readonly Dictionary<string, string> _pathToGuid = new();

    // Import edilen asset'lerin baytlari editorde Library/Artifacts'ten gelir
    // (ImportPipeline baglar); release'te null -> pak zaten artifact'i tasir.
    public Func<string, byte[]> ArtifactResolver;

    // Preload tamponu: PreloadAsync ile indirilen ham baytlar (font/clip/prefab/atlas/fx).
    // Senkron Load* API'leri release'te YALNIZ buradan beslenir (bloklayan okuma yok);
    // sahne yukleyici bagimlilik listesini Spawn'dan once buraya doldurur.
    readonly Dictionary<string, byte[]> _blobs = new();
    readonly Dictionary<string, Task<byte[]>> _blobLoads = new();

#if DE_EDITOR
    // Editor/dev: loose dosyalar (ScanMetas ile guid tablosu kurulur).
    public AssetDatabase(string root)
    {
        _root = root;
        _source = new LooseFileSource(root);
    }
#endif

    // Release: pak gibi kaynaklar guid tablosunu kendileri getirir; tarama yok.
    public AssetDatabase(AssetSource source)
    {
        _source = source;
#if DE_EDITOR
        _root = (source as LooseFileSource)?.Root;
#endif
        source.FillGuidTable(_guidToPath, _pathToGuid);
    }

    public string Root => _root;
    public AssetSource Source => _source;

    // GUID kimligi: her asset dosyasinin yaninda <ad>.meta (guid: hex32). Sahneler
    // GUID referanslar — dosya tasinsa/adi degisse referans kirilmaz (Unity modeli).
    // createMissing=true yalniz editorde: eksik meta uretilir, sahipsiz meta silinir.
#if DE_EDITOR
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

    public string PathToGuid(string relPath) => relPath != null && _pathToGuid.TryGetValue(relPath, out var g) ? g : null;

    // Editor asset taramasi (Proje paneli): gorelipath -> guid.
    public IReadOnlyDictionary<string, string> AllAssets => _pathToGuid;
#endif

    // Uzanti -> yuklenebilir runtime tipi (importer kaydi tohumu). Yeni asset
    // turleri buraya kayitla gelir — panel/inspector kodu tip bilmez.
    // Anahtarlar kucuk harf (ToLowerInvariant ile aranir): StringComparer AOT corelib'de yok.
    static readonly Dictionary<string, Type> _importers = new()
    {
        [".png"] = typeof(Sprite),
        [".jpg"] = typeof(Sprite),
        [".jpeg"] = typeof(Sprite),
        [".prefab"] = typeof(Prefab),
        [".ttf"] = typeof(Font),
        [".otf"] = typeof(Font),
        [".fx"] = typeof(PixelEffect),
        [".mp3"] = typeof(AudioClip),
        [".ogg"] = typeof(AudioClip),
        [".wav"] = typeof(AudioClip),
    };

    public static void RegisterImporter(string extension, Type type) => _importers[extension.ToLowerInvariant()] = type;

    // Serilesebilir asset-referans tipi mi: bir uzantidan yuklenebilen IAsset YA DA [Asset] veri sinifi.
    // (Kayit defteri = tek kaynak; yeni asset turu RegisterImporter'la buraya da girer.)
    public static bool IsAssetType(Type t)
    {
        if (Reflect.IsAssetClass(t))
            return true;
        if (!typeof(IAsset).IsAssignableFrom(t))
            return false;
        if (t == typeof(Texture) || t == typeof(IAsset))
            return true; // dogrudan doku alanlari; IAsset = polimorfik referans (tip yoldan cozulur)
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
        // Dosya adi: son '/' veya '\\' sonrasi (System.IO.Path release yolunda yok).
        int slash = path.LastIndexOf('/');
        int bslash = path.LastIndexOf('\\');
        string name = path.Substring((slash > bslash ? slash : bslash) + 1).ToLowerInvariant();
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
        var t = ImportTypeOf(relPath) ?? AssetClassOf(relPath);
        return t != null && fieldType.IsAssignableFrom(t);
    }

    // .asset dosyasinin [Asset] tipi (editor: YAML "type:" basligi; release: DOBJ tip adi). Yoksa null.
    public Type AssetClassOf(string relPath)
    {
        if (relPath == null || Catalog == null || !relPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            return null;
        string tn = null;
#if DE_EDITOR
        if (_root != null)
        {
            string full = Path.Combine(_root, relPath);
            if (File.Exists(full))
                tn = ObjectSerializer.TypeNameOf(full);
        }
#endif
        if (tn == null)
        {
            var bytes = ReadBlob(relPath);
            tn = ObjectBinary.TypeNameOf(bytes);
        }
        var e = tn != null ? Catalog.Find(tn) : null;
        return e != null && e.IsAsset ? e.Type : null;
    }

    // Serilestirmenin asset anahtari: IAsset -> Name; [Asset] veri nesnesi -> yuklendigi anahtar.
    public static string KeyOf(object v, AssetDatabase assets)
    {
        if (v == null)
            return "";
        if (v is IAsset a)
            return a.Name ?? "";
        return assets != null && assets._objectKeys.TryGetValue(v, out var k) ? k : "";
    }

    // Tip katalogu ([Asset] siniflarini cozmek icin): host/editor katalog kurunca atar; reload'da nesne cache'i duser.
    TypeCatalog _catalog;
    public TypeCatalog Catalog
    {
        get => _catalog;
        set
        {
            if (ReferenceEquals(_catalog, value))
                return;
            _catalog = value;
            _objects.Clear();   // eski assembly tipleri koklenmesin
            _objectKeys.Clear();
        }
    }

    readonly Dictionary<string, object> _objects = new();
    readonly Dictionary<object, string> _objectKeys = new();

    // Anahtar guid ise yola cevirir; degilse oldugu gibi (insan elle yol da yazabilir).
    public string ResolvePath(string keyOrGuid)
        => keyOrGuid != null && _guidToPath.TryGetValue(keyOrGuid, out var p) ? p : keyOrGuid;

    public bool Exists(string keyOrGuid)
    {
        var k = ResolvePath(keyOrGuid);
        return !string.IsNullOrEmpty(k) && _source.Exists(k);
    }

    // Serilestirme (Kind.Asset) tek bogazdan yukler. Cekirdegin bildigi uc primitif: Texture (ve ustundeki
    // Sprite bolgesi), AudioClip, [Asset] veri nesnesi (Font/PixelEffect/SpriteTable/oyun siniflari — hepsi DOBJ).
    public object LoadAsset(string keyOrGuid, Type type)
    {
        if (type == typeof(IAsset))
        {
            // Polimorfik referans: somut tip yolun uzantisindan.
            type = ImportTypeOf(ResolvePath(keyOrGuid)) ?? AssetClassOf(ResolvePath(keyOrGuid));
            if (type == null)
                return null;
        }
        if (Reflect.IsAssetClass(type))
        {
            var obj = LoadObject(keyOrGuid, type);
            if (obj is Font f && UiPieces.DefaultFont == null)
                UiPieces.DefaultFont = f;
            return obj;
        }
        if (type == typeof(Sprite))
            return LoadSprite(keyOrGuid);
        if (type == typeof(Texture))
            return LoadTexture(keyOrGuid);
        if (type == typeof(AudioClip))
            return LoadAudioClip(keyOrGuid);
        return null;
    }

    readonly Dictionary<string, AudioClip> _clips = new();

    // [Asset] veri nesnesi: anahtar basina TEK instance (paylasilir). Bayt kaynagi DOBJ (pak / importer
    // artifact) ya da editorde loose YAML (.asset). Instance okumadan ONCE cache'e girer (A->B->A dongusu
    // kilitlenmesin); referanslari assets'ten senkron cozulur. Tip uyusmazligi -> uyari + null.
    public object LoadObject(string keyOrGuid, Type expected = null)
    {
        string key = ResolvePath(keyOrGuid);
        if (string.IsNullOrEmpty(key))
            return null;
        if (!_objects.TryGetValue(key, out var obj))
        {
            var bytes = ReadObjectBytes(key);
            if (bytes == null || Catalog == null)
                return null;
            obj = ReadObject(key, bytes, null);
            if (obj == null)
                return null;
        }
        if (expected != null && !expected.IsInstanceOfType(obj))
        {
            LogWarning?.Invoke($"[assets] tip uyusmazligi: {key} = {obj.GetType().Name}, beklenen {expected.Name}");
            return null;
        }
        return obj;
    }

    // Editor: .asset degisti / reimport -> AYNI instance yerinde tazelenir (component referanslari bozulmaz).
    public bool ReloadObject(string keyOrGuid)
    {
        string key = ResolvePath(keyOrGuid);
        if (string.IsNullOrEmpty(key) || !_objects.TryGetValue(key, out var existing))
            return false;
        var bytes = ReadObjectBytes(key);
        if (bytes == null)
            return false;
        return ReadObject(key, bytes, existing) != null;
    }

    // Elle yazilan .asset: editorde KAYNAK YAML (importer artifact'i degil; AtlasGroup gibi importer'li
    // .asset'lerin artifact'i baska format). Release/importer ciktisi: pismis blob.
    byte[] ReadObjectBytes(string key)
    {
#if DE_EDITOR
        if (_root != null && key.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            return _source.ReadBytes(key);
#endif
        return ReadBlob(key);
    }

    object ReadObject(string key, byte[] bytes, object into)
    {
        try
        {
            if (ObjectBinary.IsBaked(bytes))
            {
                string tn = ObjectBinary.TypeNameOf(bytes);
                var e = tn != null ? Catalog.Find(tn) : null;
                if (e == null || !e.IsAsset)
                {
                    LogWarning?.Invoke($"[assets] bilinmeyen asset tipi: {key} ({tn})");
                    return null;
                }
                var obj = into ?? e.CreateObject();
                Remember(key, obj);
                ObjectBinary.Read(bytes, Catalog, this, obj);
                return obj;
            }
#if DE_EDITOR
            // Loose YAML (.asset): type: + fields:.
            var root = Yaml.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            string typeName = root.GetScalar("type", null);
            var entry = typeName != null ? Catalog.Find(typeName) : null;
            if (entry == null || !entry.IsAsset)
            {
                LogWarning?.Invoke($"[assets] bilinmeyen asset tipi: {key} ({typeName})");
                return null;
            }
            var o = into ?? entry.CreateObject();
            Remember(key, o);
            ObjectSerializer.FromNode(o, root.Get("fields") ?? root, this);
            return o;
#else
            LogWarning?.Invoke("[assets] pismis olmayan asset verisi: " + key);
            return null;
#endif
        }
        catch (Exception ex)
        {
            LogWarning?.Invoke($"[assets] asset okunamadi: {key}: {ex.Message}");
            if (into == null)
                ForgetObject(key);
            return null;
        }
    }

    void Remember(string key, object obj)
    {
        if (_objects.TryGetValue(key, out var old) && !ReferenceEquals(old, obj))
            _objectKeys.Remove(old);
        _objects[key] = obj;
        _objectKeys[obj] = key;
    }

    void ForgetObject(string key)
    {
        if (_objects.TryGetValue(key, out var old))
        {
            _objects.Remove(key);
            _objectKeys.Remove(old);
        }
    }

    // SFX klibi: DPCM artifact (import'ta PCM'e cozulmus) SENKRON okunur ve native
    // mixer'a kopyalanir — klipler kucuk, sahne kurulumunda bir kez. "Stream" tipli
    // (muzik) kaynaklar DPCM degildir -> null (Audio.PlayMusic ile calinir).
    public AudioClip LoadAudioClip(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_clips.TryGetValue(key, out var c))
            return c;
        var bytes = ReadArtifact(key, null);
        c = bytes != null ? AudioClip.FromArtifact(bytes, key) : null;
        if (c == null)
        {
            if (bytes != null)
                LogWarning?.Invoke($"[audio] DPCM degil (Stream tipli muzik mi?): {key}");
            return null;
        }
        _clips[key] = c;
        return c;
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
        // Alt bolge ("hero.ttf#U+0041"): ureticinin kendi tablosu henuz yuklenmemisse tembel yukle.
        bool sub = key.IndexOf('#') > 0;
        if (sub && !_regions.ContainsKey(key))
            EnsureOwnTable(OwnerOf(key));
        // Tablo uyesi: kaynak png hic yuklenmez, sayfaya baglanir.
        if (_regions.TryGetValue(key, out var reg))
        {
            s = Sprite.FromTexture(null, key);
            BindSpriteRegion(s, in reg);
        }
        else
            s = Sprite.FromTexture(sub ? null : LoadTexture(key), key); // alt bolgenin kendi dosyasi yok: tablo gelince baglanir
        _sprites[key] = s;
        return s;
    }

    // Prebaked font: [Asset] nesne (DOBJ / editorde de DOBJ artifact), glyph Sprite referanslari yuklenirken
    // sprite tablosundan baglanir. Ilk yuklenen font varsayilan UI atlasi olur (UiPieces.DefaultFont).
    public Font LoadFont(string key)
    {
        var f = LoadObject(key, typeof(Font)) as Font;
        if (f != null && UiPieces.DefaultFont == null)
            UiPieces.DefaultFont = f;
        return f;
    }

    // --- Artifact erisimi ---

    // Importer ciktisi: "main" (name=null) veya adli artifact ("sheet", "page0").
    // Anahtar bicimi "<asset>#<ad>": editorde ArtifactResolver Library'den cozer,
    // release'te pak ayni anahtarla tasir. Release'te kaynak = preload tamponu;
    // tamponda yoksa null + uyari (bloklayan okuma yok).
    public byte[] ReadArtifact(string key, string name)
    {
        string k = name == null ? key : key + "#" + name;
        return ReadBlob(k);
    }

    byte[] ReadBlob(string k)
    {
        if (_blobs.TryGetValue(k, out var cached))
            return cached;
        var b = ArtifactResolver?.Invoke(k);
        if (b != null)
            return b;
#if DE_EDITOR
        return _source.ReadBytes(k);
#else
        LogWarning?.Invoke("[assets] preload edilmemis senkron erisim: " + k);
        return null;
#endif
    }

    // Anahtarin ham baytlarini tampona indirir (ayni anahtar icin tek job).
    // Artifact adli anahtarlar ("a.ttf#sheet") da gecerlidir.
    public Task<byte[]> PreloadAsync(string keyOrGuid)
    {
        string k = ResolvePath(keyOrGuid);
        if (string.IsNullOrEmpty(k))
            return Completed<byte[]>(null);
        if (_blobs.TryGetValue(k, out var b))
            return Completed(b);
        if (_blobLoads.TryGetValue(k, out var inflight))
            return inflight;
        var art = ArtifactResolver?.Invoke(k);
        if (art != null)
        {
            _blobs[k] = art;
            return Completed(art);
        }
        var task = LoadBlobAsync(k);
        _blobLoads[k] = task;
        return task;
    }

    async Task<byte[]> LoadBlobAsync(string k)
    {
        var bytes = await _source.ReadBytesAsync(k);
        _blobLoads.Remove(k);
        if (bytes != null)
            _blobs[k] = bytes;
        return bytes;
    }

    // Tampondan duser (hot reload / bellek geri alma); yuklu nesneler etkilenmez.
    public void Evict(string key) => _blobs.Remove(ResolvePath(key));

    // --- Bagimlilik grafigi yukleyicisi (genel; tur ozel kod yok) ---
    // Anahtar ve TUM gecisli bagimliliklari hazir olana kadar bekler: once dep'ler
    // (paralel baslar), sonra dugumun kendisi. Ayni anahtar icin tek yukleme (memo).
    // Dugum yukleme = bayt asset'ler tampona, texture'lar decode job'ina; aktivasyonu
    // olan turler (atlas -> bolge tablosu) ic tabloda. progress: dugum sayisi uzerinden.
    readonly Dictionary<string, Task<bool>> _ensured = new();

    public Task<bool> EnsureLoadedAsync(string keyOrGuid, LoadProgress progress = null)
    {
        string k = ResolvePath(keyOrGuid);
        if (string.IsNullOrEmpty(k))
            return Completed(false);
        if (_ensured.TryGetValue(k, out var t))
        {
            if (progress != null && !t.IsCompleted)
                progress.Pending++;
            return t;
        }
        var task = EnsureCore(k, progress);
        _ensured[k] = task;
        return task;
    }

    async Task<bool> EnsureCore(string k, LoadProgress progress)
    {
        progress?.Begin();
        var deps = _source.GetDependencies(k);
        var depTasks = new List<Task<bool>>(deps.Length);
        foreach (var d in deps)
            depTasks.Add(EnsureLoadedAsync(d, progress));
        bool ok = true;
        foreach (var t in depTasks)
            ok &= await t;
        ok &= await LoadNodeAsync(k);
        if (!ok)
            LogWarning?.Invoke("[assets] yuklenemedi: " + k);
        progress?.End();
        return ok;
    }

    async Task<bool> LoadNodeAsync(string k)
    {
        var type = ImportTypeOf(k);
        if (type == typeof(Sprite) || type == typeof(Texture))
        {
            if (_regions.ContainsKey(k))
                return true; // atlas uyesi: pikseller sayfada (atlas dep olarak once yuklendi)
            if (!_source.Exists(k))
                return false;
            var op = new LoadOp();
            LoadTexture(k, op);
            while (!op.IsDone)
                await Frame.Next();
            return !op.Failed;
        }
        var bytes = await PreloadAsync(k);
        if (bytes == null)
            return false;
        if (SpriteTable.IsTable(bytes))
            return LoadSpriteTable(OwnerOf(k), k); // aktivasyon: bolge tablosu + sayfa dokulari (sayfalar dep'ti)
        return true;
    }

    // Yuklu/yuklenmekte sayilan anahtari unutur (sahne boşaltma / hot reload).
    public void Forget(string key) => _ensured.Remove(ResolvePath(key));

    static Task<T> Completed<T>(T value)
    {
        var tcs = new TaskCompletionSource<T>();
        tcs.TrySetResult(value);
        return tcs.Task;
    }

    // --- Sprite tablolari: bolge -> (sayfa dokusu, rect). Tek kanal: png atlasi, font sheet'i, spine sayfasi ---
    //
    // Tablo anahtari = artifact anahtari ("MainAtlas.asset" ya da "hero.ttf#sprites"); sahibi = '#' oncesi.
    // ONCELIK: yabanci tablo (atlas: sahibi bolgenin sahibi degil) OZ tabloyu (fontun kendi sheet'i) ezer; oz tablo
    // yabanciyi asla ezmez -> yukleme sirasi ne olursa olsun deterministik. Tablo dusunce bolge baska bir yuklu
    // tablodan yeniden cozulur, yoksa kaynaga doner (png -> kendi dokusu).

    sealed class LoadedTable
    {
        public string Key, Owner;
        public readonly List<Texture> Pages = new();
        public readonly List<SpriteTable.Region> Regions = new();
        public bool Owns(string name) => name == Owner || name.StartsWith(Owner) && name.Length > Owner.Length && name[Owner.Length] == '#';
    }

    readonly Dictionary<string, SpriteTable.Region> _regions = new();
    readonly Dictionary<string, Texture> _regionPages = new(); // bolge adi -> sayfa dokusu
    readonly Dictionary<string, LoadedTable> _regionTables = new(); // bolge adi -> su an cozuldugu tablo
    readonly Dictionary<string, LoadedTable> _tables = new();

    public bool HasRegion(string name) => _regions.ContainsKey(name);

    // Bolge verisi (editor tooling: boyut sorgusu dokuyu yuklemeden).
    public bool TryGetRegion(string name, out SpriteTable.Region region) => _regions.TryGetValue(name, out region);

    public IEnumerable<string> LoadedAtlases => _tables.Keys;

    static string OwnerOf(string key)
    {
        int h = key.IndexOf('#');
        return h < 0 ? key : key.Substring(0, h);
    }

    // Atlas .asset'i (main artifact'i tablo) yukler.
    public bool LoadAtlas(string key) => LoadSpriteTable(ResolvePath(key), ResolvePath(key));
    public void UnloadAtlas(string key) => UnloadSpriteTable(ResolvePath(key));
    public bool ReloadAtlas(string key) => LoadAtlas(key);

    // Tabloyu (DOBJ SpriteTable) ve sayfa dokularini yukler; bolgeleri birlestirir, YUKLU sprite/font'lari
    // yerinde yeniden baglar (referanslar bozulmaz). owner: sayfa artifact'lerinin sahibi; tableKey: tablo baytlari.
    public bool LoadSpriteTable(string owner, string tableKey)
    {
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(tableKey))
            return false;
        if (Catalog == null)
        {
            LogWarning?.Invoke("[atlas] katalog yok, tablo yuklenemedi: " + tableKey);
            return false;
        }
        if (_tables.ContainsKey(tableKey))
            UnloadSpriteTable(tableKey);
        var bytes = ReadBlob(tableKey);
        if (!SpriteTable.IsTable(bytes))
            return false;
        var data = ObjectBinary.Read(bytes, Catalog, this) as SpriteTable;
        if (data == null)
        {
            LogWarning?.Invoke("[atlas] tablo okunamadi: " + tableKey);
            return false;
        }

        var lt = new LoadedTable { Key = tableKey, Owner = owner };
        for (int i = 0; i < data.Pages.Count; i++)
        {
            var pi = data.Pages[i];
            var blob = ReadArtifact(owner, pi.Artifact);
            Texture tex = MakePageTexture(blob, pi.Width, pi.Height);
            if (tex == null)
            {
                LogWarning?.Invoke($"[atlas] sayfa yok/bozuk: {owner}#{pi.Artifact} ({pi.Width}x{pi.Height}, {blob?.Length ?? -1} bayt)");
                foreach (var t in lt.Pages)
                    t.Destroy();
                return false;
            }
            tex.Name = owner + "#" + pi.Artifact;
            tex.PiecesX = pi.PiecesX;
            tex.PiecesY = pi.PiecesY;
            lt.Pages.Add(tex);
        }

        // Bolge atamasi BindFont'u tetikler, o da EnsureOwnTable'a gelir: tablo ONCE kayitli olmali (ozyineleme yok).
        _tables[tableKey] = lt;
        foreach (var r in data.Regions)
        {
            if (r.Page < 0 || r.Page >= lt.Pages.Count)
                continue;
            lt.Regions.Add(r);
            if (_regionTables.TryGetValue(r.Name, out var cur) && !cur.Owns(r.Name) && lt.Owns(r.Name))
                continue; // yabanci (atlas) tablo zaten sahip; oz tablo ezmez
            if (cur != null && !cur.Owns(r.Name) && !lt.Owns(r.Name))
                LogWarning?.Invoke($"[atlas] bolge iki atlasta: {r.Name} ({cur.Key} vs {tableKey}) — sonuncusu kazanir");
            Assign(lt, in r);
        }
        return true;
    }

    // Bolgeyi tabloya yazar; YUKLU sprite (png ya da glyph "x#U+0041") yerinde yeniden baglanir.
    void Assign(LoadedTable lt, in SpriteTable.Region r)
    {
        _regions[r.Name] = r;
        _regionPages[r.Name] = lt.Pages[r.Page];
        _regionTables[r.Name] = lt;
        if (_sprites.TryGetValue(r.Name, out var s))
            BindSpriteRegion(s, in r);
    }

    // Tablo duser: bolgeler baska yuklu tablodan yeniden cozulur, yoksa kaynaga doner (png -> kendi dokusu;
    // alt bolgenin kaynagi yok -> sayfasiz kalir).
    public void UnloadSpriteTable(string tableKey)
    {
        if (tableKey == null || !_tables.TryGetValue(tableKey, out var lt))
            return;
        _tables.Remove(tableKey);
        foreach (var r in lt.Regions)
        {
            string name = r.Name;
            if (!_regionTables.TryGetValue(name, out var cur) || cur != lt)
                continue; // baska tablo bu adi sonradan almis
            _regions.Remove(name);
            _regionPages.Remove(name);
            _regionTables.Remove(name);
            if (FindRegion(name, out var other, out var orr))
            {
                Assign(other, in orr);
                continue;
            }
            if (_sprites.TryGetValue(name, out var s))
                s.BindFull(name.IndexOf('#') < 0 ? LoadTexture(name) : null);
        }
        foreach (var t in lt.Pages)
            t.Destroy();
    }

    // Yuklu tablolar arasinda adi tasiyan: once yabanci (atlas), sonra oz.
    bool FindRegion(string name, out LoadedTable table, out SpriteTable.Region region)
    {
        table = null;
        region = default;
        foreach (var lt in _tables.Values)
        {
            foreach (var r in lt.Regions)
            {
                if (r.Name != name)
                    continue;
                if (table == null || (table.Owns(name) && !lt.Owns(name)))
                {
                    table = lt;
                    region = r;
                }
            }
        }
        return table != null;
    }

    // Ureticinin KENDI tablosu ("<owner>#sprites"): font/spine yuklenirken sheet bolgeleri cozulsun diye.
    // Yabanci (atlas) tablo zaten yukluyse yine yuklenir ama ezmez; sheet artifact'i yoksa (yutulmus) sessiz false.
    bool EnsureOwnTable(string owner)
    {
        string tk = owner + "#sprites";
        if (_tables.ContainsKey(tk))
            return true;
        var bytes = ReadBlob(tk);
        return SpriteTable.IsTable(bytes) && LoadSpriteTable(owner, tk);
    }

    // Tum tablo artifact'lerini bulup yukler (acilis, loose/editor): ".asset" anahtarlarindan main'i tablo
    // olanlar. Editorde ArtifactResolver gerekirse once import eder.
    public int LoadAtlases()
    {
        int n = 0;
        foreach (var k in AtlasCandidates())
        {
            var bytes = ReadBlob(k);
            if (SpriteTable.IsTable(bytes) && LoadSpriteTable(k, k))
                n++;
        }
        return n;
    }

    List<string> AtlasCandidates()
    {
        var keys = new List<string>();
        foreach (var k in _source.Keys)
            if (k.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                keys.Add(k);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    // Uyari kanali (editor Console'a baglar; runtime'da null = sessiz).
    public static Action<string> LogWarning;

    static unsafe Texture MakePageTexture(byte[] blob, int w, int h)
    {
        if (!PixelBlob.TryParse(blob, out int ch, out int bw, out int bh) || bw != w || bh != h)
            return null;
        if (ch == 1)
        {
            Texture t;
            fixed (byte* p = &blob[PixelBlob.HeaderSize])
                t = Texture.FromAlpha(w, h, p);
            t.Persistent = true; // R8: CPU kopyasi yok, evict edilirse geri yuklenemez
            return t;
        }
        return Texture.FromRgba(w, h, blob.AsSpan(PixelBlob.HeaderSize));
    }

    void BindSpriteRegion(Sprite s, in SpriteTable.Region r)
    {
        if (!_regionPages.TryGetValue(r.Name, out var page))
            return;
        s.BindRegion(page, r.X, r.Y, r.W, r.H, r.OffX, r.OffY, r.OrigW, r.OrigH);
    }

    // Font sayfasi artik glyph Sprite'larindan gelir (Font.Page); tablo yukle/bosalt glyph sprite'larini
    // diger sprite'lar gibi yerinde baglar — font'a ozel yol yok.

    // Dis degisiklik (reimport): pixel effect yerinde tazelenir (Version++ compose cache'leri bayatlatir);
    // [Asset] nesneler (font dahil) once kendi sprite tablosu, sonra nesne yerinde (ReloadObject).
    public bool InvalidateImported(string relPath)
    {
        if (_clips.TryGetValue(relPath, out var clip))
        {
            // Klip yerinde tazelenir (referanslar bozulmaz): eski native klip serbest,
            // yeni DPCM yuklenir; calan voice'lar fade ile duser.
            var bytes = ReadArtifact(relPath, null);
            var fresh = bytes != null ? AudioClip.FromArtifact(bytes, relPath) : null;
            clip.Destroy();
            if (fresh != null)
            {
                clip.Handle = fresh.Handle;
                fresh.Handle = -1;
            }
            else
                _clips.Remove(relPath);
            return true;
        }
        bool any = false;
        string tk = relPath + "#sprites";
        if (_tables.ContainsKey(tk))
            any |= LoadSpriteTable(relPath, tk);
        if (_objects.ContainsKey(relPath))
            any |= ReloadObject(relPath);
        return any;
    }

    public PixelEffect LoadPixelEffect(string key) => LoadObject(key, typeof(PixelEffect)) as PixelEffect;

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
        Track(job, t, op);
        return t;
    }

    void Track(int job, Texture t, LoadOp op)
    {
        var p = new Pending { Job = job, Target = t, Op = op };
        NativeAsync.Listen(job, c => { p.Done = true; p.Result = c; });
        _pending.Add(p);
    }

    // Prefab = alt agac. Pak'ta pismis bayt (SceneBinary), loose'ta YAML SceneDoc;
    // magic'e bakilir. Release'te baytlar preload tamponundan (sahne bagimliligi).
    public Prefab LoadPrefab(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_prefabs.TryGetValue(key, out var p))
            return p;
        var bytes = ReadBlob(key);
        if (bytes == null)
            return null;
        p = MakePrefab(key, bytes);
        _prefabs[key] = p;
        return p;
    }

    public async Task<Prefab> LoadPrefabAsync(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_prefabs.TryGetValue(key, out var p))
            return p;
        var bytes = await PreloadAsync(key);
        if (bytes == null)
            return null;
        if (_prefabs.TryGetValue(key, out p)) // bekleme sirasinda baska yol kurmus olabilir
            return p;
        p = MakePrefab(key, bytes);
        _prefabs[key] = p;
        return p;
    }

    Prefab MakePrefab(string key, byte[] bytes)
    {
        if (SceneBinary.IsBaked(bytes))
            return new Prefab { Key = key, Baked = bytes, Assets = this };
#if DE_EDITOR
        return new Prefab { Key = key, Doc = SceneDoc.Parse(System.Text.Encoding.UTF8.GetString(bytes)), Assets = this };
#else
        LogWarning?.Invoke("[prefab] baked degil (YAML release'te desteklenmez): " + key);
        return null;
#endif
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
        Track(job, t, null);
        return true;
    }

    // Dis degisiklik: prefab cache'i duser, sonraki LoadPrefab diskten okur.
    public bool InvalidatePrefab(string relPath) => _prefabs.Remove(relPath);

    // Bekleyen async yukleme sayisi (atlas pack gibi toplu isler "hepsi indi mi" diye bakar).
    public int PendingCount => _pending.Count;

    // Yuklu ama henuz GPU'ya cikmamis dokulari frame basina `perFrame` adet yukler, ortak
    // shader'lari derler. Sahne Spawn'dan once bekler: ilk cizim frame'inde upload/compile kalmaz.
    public async Task UploadAsync(int perFrame = 2)
    {
        _ = Shader.Default;
        _ = UiPieces.UiShader;
        _ = UiPieces.UiShaderMsdf;
        var list = new List<Texture>(); // bekleme sirasinda sozlukler degisebilir: once topla
        foreach (var t in _textures.Values)
            if (t.NeedsUpload) list.Add(t);
        foreach (var la in _tables.Values)
            foreach (var t in la.Pages)
                if (t.NeedsUpload) list.Add(t);
        for (int i = 0; i < list.Count; i++)
        {
            list[i]._Sync();
            if ((i + 1) % perFrame == 0)
                await Frame.Next();
        }
    }

    // Frame basi cagrilir; frame basina en fazla `budget` sonuc baglanir
    // (yavas cihazda spike yerine birkac frame'e yayilir). Async kuyrugunu da pompalar (dt: Wait zamanlayicilari).
    public void Tick(int budget = 2, float dt = 0f)
    {
        NativeAsync.Pump(dt);
        for (int i = _pending.Count - 1; i >= 0 && budget > 0; i--)
        {
            var p = _pending[i];
            if (!p.Done)
                continue;
            if (p.Result.Ok)
                p.Target._AttachPixels(p.Result.Data, p.Result.A, p.Result.B);
            else
                p.Target.Loading = false; // hata: placeholder'da kalir
            NativeAsync.Free(p.Result.Data);
            Complete(p.Op, !p.Result.Ok);
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
