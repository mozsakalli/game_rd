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
        [".fx"] = typeof(PixelEffect),
        [".mp3"] = typeof(AudioClip),
        [".ogg"] = typeof(AudioClip),
        [".wav"] = typeof(AudioClip),
    };

    public static void RegisterImporter(string extension, Type type) => _importers[extension] = type;

    // Serilesebilir asset-referans tipi mi: bir uzantidan yuklenebilen IAsset.
    // (Kayit defteri = tek kaynak; yeni asset turu RegisterImporter'la buraya da girer.)
    public static bool IsAssetType(Type t)
    {
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
        if (type == typeof(IAsset))
        {
            // Polimorfik referans: somut tip yolun uzantisindan.
            type = ImportTypeOf(ResolvePath(keyOrGuid));
            if (type == null)
                return null;
        }
        if (type == typeof(Sprite))
            return LoadSprite(keyOrGuid);
        if (type == typeof(Texture))
            return LoadTexture(keyOrGuid);
        if (type == typeof(Font))
            return LoadFont(keyOrGuid);
        if (type == typeof(PixelEffect))
            return LoadPixelEffect(keyOrGuid);
        if (type == typeof(AudioClip))
            return LoadAudioClip(keyOrGuid);
        return null;
    }

    readonly Dictionary<string, AudioClip> _clips = new();

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
        // Atlas uyesi: kaynak png hic yuklenmez, sayfaya baglanir.
        if (_regions.TryGetValue(key, out var reg))
        {
            s = Sprite.FromTexture(null, key);
            BindSpriteRegion(s, in reg);
        }
        else
            s = Sprite.FromTexture(LoadTexture(key), key);
        _sprites[key] = s;
        return s;
    }

    // Prebaked font: baytlar artifact'ten (editor: Library, release: pak) SENKRON
    // gelir — metrikler layout girdisi oldugu icin async placeholder anlamsiz.
    // Glyph sayfasi: atlas grubu bu fontu almissa onun sayfasi, yoksa kendi sheet'i.
    public Font LoadFont(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_fonts.TryGetValue(key, out var f))
            return f;
        var bytes = ReadArtifact(key, null);
        f = bytes != null ? Font.FromArtifact(bytes, key) : null;
        if (f == null)
            return null; // basarisiz yukleme cache'lenmez (sonraki deneme taze okur)
        _fonts[key] = f;
        BindFont(f);
        return f;
    }

    // --- Artifact erisimi ---

    // Importer ciktisi: "main" (name=null) veya adli artifact ("sheet", "page0").
    // Anahtar bicimi "<asset>#<ad>": editorde ArtifactResolver Library'den cozer,
    // release'te pak ayni anahtarla tasir.
    public byte[] ReadArtifact(string key, string name)
    {
        string k = name == null ? key : key + "#" + name;
        return ArtifactResolver?.Invoke(k) ?? _source.ReadBytes(k);
    }

    // --- Atlas: bolge tablosu + sayfa dokulari ---

    sealed class LoadedAtlas
    {
        public string Key;
        public readonly List<Texture> Pages = new();
        public readonly List<string> RegionNames = new();
    }

    readonly Dictionary<string, AtlasRegion> _regions = new();
    readonly Dictionary<string, Texture> _regionPages = new(); // bolge adi -> sayfa dokusu
    readonly Dictionary<string, LoadedAtlas> _atlases = new();

    public bool HasRegion(string name) => _regions.ContainsKey(name);

    public IEnumerable<string> LoadedAtlases => _atlases.Keys;

    // Atlas artifact'ini (DATL "main" + "pageN" DPIX) yukler; bolgeleri tabloya
    // yazar ve YUKLU sprite/font'lari yerinde yeniden baglar (referanslar bozulmaz).
    public bool LoadAtlas(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return false;
        if (_atlases.ContainsKey(key))
            UnloadAtlas(key);
        var bytes = ReadArtifact(key, null);
        var data = bytes != null ? AtlasData.Parse(bytes) : null;
        if (data == null)
            return false;

        var la = new LoadedAtlas { Key = key };
        for (int i = 0; i < data.Pages.Count; i++)
        {
            var pi = data.Pages[i];
            var blob = ReadArtifact(key, AtlasData.PageArtifact(i));
            Texture tex = MakePageTexture(blob, pi.Width, pi.Height);
            if (tex == null)
            {
                foreach (var t in la.Pages)
                    t.Destroy();
                return false;
            }
            tex.Name = key + "#" + AtlasData.PageArtifact(i);
            tex.PiecesX = pi.PiecesX;
            tex.PiecesY = pi.PiecesY;
            la.Pages.Add(tex);
        }

        var touchedFonts = new HashSet<string>();
        foreach (var r in data.Regions)
        {
            if (r.Page < 0 || r.Page >= la.Pages.Count)
                continue;
            if (_regions.TryGetValue(r.Name, out var old) && _regionPages.TryGetValue(r.Name, out var oldPage))
                LogWarning?.Invoke($"[atlas] bolge iki atlasta: {r.Name} ({oldPage.Name} vs {key}) — sonuncusu kazanir");
            _regions[r.Name] = r;
            _regionPages[r.Name] = la.Pages[r.Page];
            la.RegionNames.Add(r.Name);
            int hash = r.Name.IndexOf('#');
            if (hash < 0)
            {
                if (_sprites.TryGetValue(r.Name, out var s))
                    BindSpriteRegion(s, in r);
            }
            else
                touchedFonts.Add(r.Name.Substring(0, hash));
        }
        foreach (var fk in touchedFonts)
            if (_fonts.TryGetValue(fk, out var f))
                BindFont(f);
        _atlases[key] = la;
        return true;
    }

    // Atlas duser: uyeler kaynaklarina geri doner (png -> kendi dokusu, font -> sheet).
    public void UnloadAtlas(string key)
    {
        key = ResolvePath(key);
        if (key == null || !_atlases.TryGetValue(key, out var la))
            return;
        _atlases.Remove(key);
        var touchedFonts = new HashSet<string>();
        foreach (var name in la.RegionNames)
        {
            if (!_regionPages.TryGetValue(name, out var page) || !la.Pages.Contains(page))
                continue; // baska atlas bu adi sonradan almis
            _regions.Remove(name);
            _regionPages.Remove(name);
            int hash = name.IndexOf('#');
            if (hash < 0)
            {
                if (_sprites.TryGetValue(name, out var s))
                    s.BindFull(LoadTexture(name));
            }
            else
                touchedFonts.Add(name.Substring(0, hash));
        }
        foreach (var fk in touchedFonts)
            if (_fonts.TryGetValue(fk, out var f))
                BindFont(f);
        foreach (var t in la.Pages)
            t.Destroy();
    }

    public bool ReloadAtlas(string key) => LoadAtlas(key);

    // Tum atlas tanimlarini bulup yukler (acilis): ".asset" anahtarlarindan artifact'i
    // DATL olanlar. Editorde ArtifactResolver gerekirse once import eder.
    public int LoadAtlases()
    {
        int n = 0;
        var keys = new List<string>();
        foreach (var k in _source.Keys)
            if (k.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                keys.Add(k);
        keys.Sort(StringComparer.Ordinal);
        foreach (var k in keys)
        {
            var bytes = ReadArtifact(k, null);
            if (AtlasData.IsAtlas(bytes) && LoadAtlas(k))
                n++;
        }
        return n;
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

    void BindSpriteRegion(Sprite s, in AtlasRegion r)
    {
        if (!_regionPages.TryGetValue(r.Name, out var page))
            return;
        s.BindRegion(page, r.X, r.Y, r.W, r.H, r.OffX, r.OffY, r.OrigW, r.OrigH);
    }

    // Font sayfasi: atlas bolgeleri varsa (pikselli ilk glyph'in anahtari tabloda)
    // atlas sayfasi, yoksa fontun kendi "sheet" artifact'i.
    void BindFont(Font f)
    {
        if (f.GlyphCount == 0)
            return;
        Texture page = null;
        for (int i = 0; i < f.GlyphCount && page == null; i++)
        {
            ref readonly var g = ref f.GlyphAt(i);
            if (g.W > 0 && g.H > 0)
                _regionPages.TryGetValue(f.Name + "#" + AtlasData.GlyphName(g.Codepoint), out page);
        }
        if (page != null)
        {
            f.BeginAtlasBind(page);
            for (int i = 0; i < f.GlyphCount; i++)
            {
                string gk = f.Name + "#" + AtlasData.GlyphName(f.GlyphAt(i).Codepoint);
                if (_regions.TryGetValue(gk, out var r))
                    f.SetGlyphPosition(i, r.X, r.Y);
            }
            return;
        }
        var sheet = ReadArtifact(f.Name, "sheet");
        if (sheet == null || !f.BindSheet(sheet))
        {
            f.Unbind();
            LogWarning?.Invoke($"[font] sayfa yok: {f.Name} (sheet artifact'i eksik, atlas uyesi degil)");
        }
    }

    // Dis degisiklik (reimport): font AYNI instance'ta yerinde tazelenir ve sayfasi
    // yeniden baglanir (BindVersion artar, metin quad'lari yenilenir); pixel effect
    // de yerinde tazelenir (Version++ compose cache'leri bayatlatir).
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
        if (_fx.TryGetValue(relPath, out var fx))
        {
            var bytes = ArtifactResolver?.Invoke(relPath) ?? _source.ReadBytes(relPath);
            fx.SetBody(bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : null);
            return true;
        }
        if (_fonts.TryGetValue(relPath, out var f))
        {
            var bytes = ReadArtifact(relPath, null);
            if (bytes != null && f.ReloadFrom(bytes))
            {
                BindFont(f);
                return true;
            }
            _fonts.Remove(relPath);
            return true;
        }
        return false;
    }

    readonly Dictionary<string, PixelEffect> _fx = new();

    // .fx govdesi SENKRON yuklenir (kucuk metin); instance kalici, reload yerinde.
    public PixelEffect LoadPixelEffect(string key)
    {
        key = ResolvePath(key);
        if (string.IsNullOrEmpty(key))
            return null;
        if (_fx.TryGetValue(key, out var fx))
            return fx;
        var bytes = ArtifactResolver?.Invoke(key) ?? _source.ReadBytes(key);
        if (bytes == null)
            return null;
        fx = new PixelEffect { Name = key };
        fx.SetBody(System.Text.Encoding.UTF8.GetString(bytes));
        _fx[key] = fx;
        return fx;
    }

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

    // Prefab = alt agac. Pak'ta pismis bayt (SceneBinary), loose'ta YAML SceneDoc;
    // magic'e bakilir. Kucuk oldugu icin senkron okunur.
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
        p = SceneBinary.IsBaked(bytes)
            ? new Prefab { Key = key, Baked = bytes, Assets = this }
            : new Prefab { Key = key, Doc = SceneDoc.Parse(System.Text.Encoding.UTF8.GetString(bytes)), Assets = this };
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
