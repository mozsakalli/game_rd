using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DigitoyEngine;

// Platform SDK plugin tabani (docs/plugins.md). Core protokolu iki yonde UTF-8 string:
//   Send(msg)      -> host DEPlugin.onMessage (host main thread'e dispatch eder)
//   OnMessage(msg) <- host DEPlugin.send -> surec omurlu native inbox -> frame basinda PumpInbox -> burada
// PUSH YOK: host managed'a kendiliginden girmez; mesaj kuyrukta bekler, managed ceker. Dinleyici kaydi
// olmadigi icin "dinlemeden once fire oldu" sinifi sorun yoktur. await/id/korelasyon core'da degil:
// isteyen plugin TaskCompletionSource + Wait.Timeout + kendi id alaniyla kurar (Http deseni).
//
// Kullanim = Plugin.Get<T>(): ilk cagrida yaratilir (statik generic, reflection yok). Oyun kodu Get demiyorsa
// tip AOT'ye girmez (trim koku DEGIL) ve native tarafi da projeye kopyalanmaz.
public abstract partial class Plugin
{
    // Host erisimi: AOT/native = de_plugin_*; editor Play'de PluginMock bunu degistirir (Faz 4).
    public interface IHost
    {
        bool Available(string plugin);
        bool Send(string plugin, string msg);
        bool Next(out string plugin, out string msg); // inbox'tan siradaki; false = bos
    }

    public static IHost Host = NativeHost.Instance;

    public string Name { get; }

    // Host'ta bu adla kayitli plugin var mi (ilk Get'te bir kez sorulur). Yoksa Send sessizce duser.
    public bool Available { get; private set; }

    static readonly Dictionary<string, Plugin> _instances = new();
    static readonly List<Plugin> _list = new();
    // Instance henuz yokken gelen mesajlar (soguk baslangic: Application.onCreate'te post edilen deep link vb.).
    static readonly Dictionary<string, List<string>> _undelivered = new();
    const int UndeliveredCap = 256;

    protected Plugin()
    {
        Name = GetType().Name;
    }

    public static T Get<T>() where T : Plugin, new()
    {
        string name = typeof(T).Name;
        if (_instances.TryGetValue(name, out var p))
            return (T)p;
        var t = new T();
        t.Available = Host != null && Host.Available(name);
        if (!t.Available)
            Debug.LogWarning("[plugin] " + name + ": host'ta kayitli degil (bu platformda yok ya da mock yok) — Send dusurulur");
        _instances[name] = t;
        _list.Add(t);
        if (_undelivered.Remove(name, out var queued))
            for (int i = 0; i < queued.Count; i++)
                t.Deliver(queued[i]);
        return t;
    }

    // Host'a mesaj. Kayitli plugin yoksa false (bir kez uyarildi, tekrar log yok).
    protected bool Send(string msg)
    {
        if (!Available || msg == null)
            return false;
        return Host.Send(Name, msg);
    }

    // Host'tan mesaj: frame basinda, ana thread, sirayla. Exception burada yakalanir (PumpInbox).
    protected virtual void OnMessage(string msg) { }

    void Deliver(string msg)
    {
        try { OnMessage(msg); }
        catch (Exception e) { Debug.LogError("[plugin] " + Name + ".OnMessage: " + e); }
    }

    // Frame basinda (GameHost.Frame / editor Play) bir kez: native inbox'u bosalt, sahiplerine dagit.
    public static void PumpInbox()
    {
        while (Host.Next(out string name, out string msg))
            Dispatch(name, msg);
    }

    // Mock/test yolu da buradan girer.
    public static void Dispatch(string name, string msg)
    {
        if (name == null || msg == null)
            return;
        if (_instances.TryGetValue(name, out var p))
        {
            p.Deliver(msg);
            return;
        }
        if (!_undelivered.TryGetValue(name, out var q))
            _undelivered[name] = q = new List<string>();
        if (q.Count >= UndeliveredCap)
            q.RemoveAt(0);
        q.Add(msg);
    }

    // Editor Play Stop / ALC reload: instance'lar atilir (bekleyen Task'lar GC'ye kalir), inbox beklemeleri korunur.
    public static void ResetAll()
    {
        _instances.Clear();
        _list.Clear();
    }

    // ---- ayarlar (pak blobu: AssetPackBuilder .plugins anahtari) ----
    // Duzen: i32 magic "DPLG", i32 n, { str name(u16 len+utf8), i32 len, DOBJ bytes }[]
    public const string SettingsPakKey = ".plugins";
    const int SettingsMagic = 0x474C5044; // "DPLG"
    static readonly Dictionary<string, byte[]> _settingsBlob = new();
    internal static AssetDatabase SettingsAssets;

    // Editor: ProjectSettings/Plugins/<Name>.asset'ten yukler (Faz 4 baglar). null = pak blobu / varsayilan.
    public static Func<string, object, bool> SettingsLoader;

    public static void LoadSettingsBlob(byte[] data, AssetDatabase assets)
    {
        SettingsAssets = assets;
        _settingsBlob.Clear();
        if (data == null || data.Length < 8)
            return;
        int p = 0;
        if (I32(data, ref p) != SettingsMagic)
            return;
        int n = I32(data, ref p);
        for (int i = 0; i < n; i++)
        {
            int len = data[p] | (data[p + 1] << 8);
            p += 2;
            string name = Encoding.UTF8.GetString(data, p, len);
            p += len;
            int blen = I32(data, ref p);
            var b = new byte[blen];
            Array.Copy(data, p, b, 0, blen);
            p += blen;
            _settingsBlob[name] = b;
        }
    }

    internal static bool TryReadSettings(string name, object into)
    {
        if (SettingsLoader != null)
            return SettingsLoader(name, into);
        if (!_settingsBlob.TryGetValue(name, out var b))
            return false;
        ObjectBinary.ReadInto(b, into, SettingsAssets);
        return true;
    }

    static int I32(byte[] b, ref int p)
    {
        int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
        p += 4;
        return v;
    }

    // ---- native host (de_plugin.h) ----
    // Pull deseni: de_plugin_peek -> gerekli bayt (name\0msg), managed tamponu ayirir, de_plugin_pop kopyalar.
    // wasm32: pointer'lar byte* (IntPtr 64-bit IR tuzagi, platform-hosts.md).
    public sealed unsafe class NativeHost : IHost
    {
        public static readonly NativeHost Instance = new();
        const string Lib = "digitoyengine_native";

        [DllImport(Lib, EntryPoint = "de_plugin_available")] static extern int de_plugin_available(byte* name);
        [DllImport(Lib, EntryPoint = "de_plugin_send")] static extern int de_plugin_send(byte* name, byte* msg);
        [DllImport(Lib, EntryPoint = "de_plugin_peek")] static extern int de_plugin_peek();
        [DllImport(Lib, EntryPoint = "de_plugin_pop")] static extern int de_plugin_pop(byte* buf, int cap);

        byte[] _buf = new byte[256];

        public bool Available(string plugin)
        {
            var n = Utf8Z(plugin);
            fixed (byte* pn = n)
                return de_plugin_available(pn) != 0;
        }

        public bool Send(string plugin, string msg)
        {
            var n = Utf8Z(plugin);
            var m = Utf8Z(msg);
            fixed (byte* pn = n)
            fixed (byte* pm = m)
                return de_plugin_send(pn, pm) != 0;
        }

        // Siradaki inbox mesaji: "name\0msg" tek tampon. false = bos.
        public bool Next(out string name, out string msg)
        {
            name = null; msg = null;
            int need = de_plugin_peek();
            if (need <= 0)
                return false;
            if (_buf.Length < need)
                _buf = new byte[Math.Max(need, _buf.Length * 2)];
            int got;
            fixed (byte* pb = _buf)
                got = de_plugin_pop(pb, _buf.Length);
            if (got <= 0)
                return false;
            int sep = -1;
            for (int i = 0; i < got; i++)
                if (_buf[i] == 0) { sep = i; break; }
            if (sep < 0)
                return false;
            name = Encoding.UTF8.GetString(_buf, 0, sep);
            msg = Encoding.UTF8.GetString(_buf, sep + 1, got - sep - 1);
            return true;
        }

        static byte[] Utf8Z(string s)
        {
            var u = Encoding.UTF8.GetBytes(s ?? "");
            var b = new byte[u.Length + 1];
            Array.Copy(u, 0, b, 0, u.Length);
            return b;
        }
    }
}

// Ayarli plugin: TSettings [Serializable] saf veri sinifi (ic ice tanimlanabilir). Editor Player Settings > Plugins
// bolumunde ObjectDrawer ile cizilir -> ProjectSettings/Plugins/<Name>.asset; release'te pak .plugins blobundan
// (ObjectBinary katalogsuz, sema tipten). Dosya/kayit yoksa new TSettings().
public abstract class Plugin<TSettings> : Plugin where TSettings : class, new()
{
    TSettings _settings;

    public TSettings Settings
    {
        get
        {
            if (_settings == null)
            {
                var s = new TSettings();
                try { TryReadSettings(Name, s); }
                catch (Exception e) { Debug.LogError("[plugin] " + Name + " ayarlari okunamadi: " + e.Message); }
                _settings = s;
            }
            return _settings;
        }
    }
}

// Ayar alani bos birakilamaz: editor kirmizi gosterir, build (canli plugin icin) hata verir.
[AttributeUsage(AttributeTargets.Field)]
public sealed class RequiredAttribute : Attribute { }

// Plugin'siz proje ayari: [Serializable] sinif Player Settings > Plugins bolumunde gorunur,
// ProjectSettings/Plugins/<TypeName>.asset; build kancalari p.Settings<T>() ile okur.
[AttributeUsage(AttributeTargets.Class)]
public sealed class ProjectSettingAttribute : Attribute { }

#if DE_EDITOR
// Editor Play'de host taklidi (Assets/Plugins/<Ad>/Editor/<Ad>Mock.cs). Protokol host'unkiyle ayni: OnMessage(msg) <- Plugin.Send,
// Send(msg) -> Plugin.OnMessage (frame basinda). Yazar istedigi senaryoyu kurar (1 sn sonra "rewarded", no_fill ...).
// Mock yoksa plugin Available=false -> oyun "unavailable" yolunu test eder; Play olmez.
public abstract class PluginMock<T> : PluginMockBase where T : Plugin
{
    public override string PluginName => typeof(T).Name;
}

public abstract class PluginMockBase
{
    public abstract string PluginName { get; }
    public abstract void OnMessage(string msg);
    protected void Send(string msg) => Plugin.MockHost.Instance.Post(PluginName, msg);
}

public abstract partial class Plugin
{
    public sealed class MockHost : IHost
    {
        public static readonly MockHost Instance = new();
        readonly Dictionary<string, PluginMockBase> _mocks = new();
        readonly Queue<(string, string)> _inbox = new();

        public void Register(PluginMockBase mock) => _mocks[mock.PluginName] = mock;
        public void Clear() { _mocks.Clear(); _inbox.Clear(); }
        public int Count => _mocks.Count;

        public bool Available(string plugin) => _mocks.ContainsKey(plugin);
        public bool Send(string plugin, string msg)
        {
            if (!_mocks.TryGetValue(plugin, out var m)) return false;
            try { m.OnMessage(msg); }
            catch (Exception e) { Debug.LogError("[plugin-mock] " + plugin + ".OnMessage: " + e); }
            return true;
        }
        public void Post(string plugin, string msg) => _inbox.Enqueue((plugin, msg));
        public bool Next(out string plugin, out string msg)
        {
            if (_inbox.Count == 0) { plugin = null; msg = null; return false; }
            (plugin, msg) = _inbox.Dequeue();
            return true;
        }
    }

    // Pak .plugins blobu (AssetPackBuilder): name -> DOBJ (ObjectBinary.Bake(obj, assets), katalogsuz).
    public static byte[] WriteSettingsBlob(IReadOnlyList<(string name, object settings)> items, AssetDatabase assets)
    {
        var w = new SceneBinary.Writer();
        w.I32(SettingsMagic);
        w.I32(items.Count);
        foreach (var (name, obj) in items)
        {
            w.Str(name);
            var b = ObjectBinary.Bake(obj, assets);
            w.I32(b.Length);
            w.Bytes(b);
        }
        return w.ToArray();
    }
}
#endif
