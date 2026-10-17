#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: Plugin koprusu (docs/plugins.md) mock host ile — Send/OnMessage, instance oncesi mesaj kuyrugu,
// unsupported (mock yok), await deseni (TCS + Wait.Timeout), ayar blobu round-trip, OnMessage exception izolasyonu.
public static class PluginTests
{
    static int _pass, _fail;

    public sealed class TestPlugin : Plugin<TestPlugin.Config>
    {
        [Serializable] public sealed class Config { [Required] public string appId = ""; public bool testMode = true; public int retries = 3; }
        public readonly List<string> Received = new();
        public bool Throw;
        public new bool Send(string msg) => base.Send(msg);
        protected override void OnMessage(string msg)
        {
            if (Throw) throw new InvalidOperationException("boom");
            Received.Add(msg);
            if (msg.StartsWith("req:") && _pending.Remove(msg.Substring(4), out var tcs)) tcs.TrySetResult(msg);
        }
        readonly Dictionary<string, TaskCompletionSource<string>> _pending = new();
        public Task<string> Request(string id)
        {
            var tcs = new TaskCompletionSource<string>();
            _pending[id] = tcs;
            Send("ask:" + id);
            return tcs.Task;
        }
    }
    public sealed class Orphan : Plugin { public new bool Send(string m) => base.Send(m); }

    sealed class TestMock : PluginMock<TestPlugin>
    {
        public override void OnMessage(string msg)
        {
            if (msg.StartsWith("ask:")) Send("req:" + msg.Substring(4)); // echo yaniti (frame basinda teslim)
            else Send("echo:" + msg);
        }
    }

    public static void Run()
    {
        _pass = _fail = 0;
        var prevHost = Plugin.Host;
        var prevLoader = Plugin.SettingsLoader;
        try
        {
            Plugin.ResetAll();
            Plugin.MockHost.Instance.Clear();
            Plugin.Host = Plugin.MockHost.Instance;
            Plugin.SettingsLoader = null;

            // 1) instance oncesi mesaj: kuyrukta bekler, Get'te teslim
            Plugin.Dispatch("TestPlugin", "early");
            Plugin.MockHost.Instance.Register(new TestMock());
            var p = Plugin.Get<TestPlugin>();
            Check(ReferenceEquals(p, Plugin.Get<TestPlugin>()), "Get tekil instance");
            Check(p.Available, "mock varken Available");
            Check(p.Received.Count == 1 && p.Received[0] == "early", "erken mesaj Get'te teslim");

            // 2) Send -> mock -> Send -> inbox -> PumpInbox -> OnMessage
            Check(p.Send("hello"), "Send true");
            Check(p.Received.Count == 1, "yanit pump'tan once gelmez");
            Plugin.PumpInbox();
            Check(p.Received.Count == 2 && p.Received[1] == "echo:hello", "echo pump ile geldi");

            // 3) await deseni (plugin kendi TCS'i)
            var t = p.Request("7");
            Check(!t.IsCompleted, "Request bekliyor");
            Plugin.PumpInbox();
            Check(t.IsCompleted && t.Result == "req:7", "Request pump ile tamamlandi");

            // 4) OnMessage exception izole
            p.Throw = true;
            p.Send("x"); Plugin.PumpInbox();
            p.Throw = false;
            p.Send("y"); Plugin.PumpInbox();
            Check(p.Received[^1] == "echo:y", "exception sonrasi zincir devam");

            // 5) mock'suz plugin: Available=false, Send false, exception yok
            var o = Plugin.Get<Orphan>();
            Check(!o.Available && !o.Send("z"), "mock yok -> unavailable, Send false");

            // 6) ayarlar: loader yok, blob yok -> varsayilan
            Check(p.Settings.testMode && p.Settings.retries == 3 && p.Settings.appId == "", "ayar varsayilani");
            // blob round-trip (AssetPackBuilder -> GameHost yolu)
            var s = new TestPlugin.Config { appId = "abc-123", testMode = false, retries = 9 };
            var blob = Plugin.WriteSettingsBlob(new List<(string, object)> { ("TestPlugin", s) }, null);
            Plugin.LoadSettingsBlob(blob, null);
            Plugin.ResetAll();
            var p2 = Plugin.Get<TestPlugin>();
            Check(p2.Settings.appId == "abc-123" && !p2.Settings.testMode && p2.Settings.retries == 9, "ayar blobu round-trip");
            Check(PluginSettingsEditor.MissingRequired(new TestPlugin.Config()).Count == 1, "[Required] bos tespiti");
            Check(PluginSettingsEditor.MissingRequired(s).Count == 0, "[Required] dolu");

            // 7) ResetAll sonrasi kuyruk korunur
            Plugin.ResetAll();
            Plugin.Dispatch("TestPlugin", "after-reset");
            var p3 = Plugin.Get<TestPlugin>();
            Check(p3.Received.Count == 1 && p3.Received[0] == "after-reset", "reset sonrasi erken mesaj");

            // 8) native yol dusunulerek: AssetDatabase.IsPluginNativePath
            Check(AssetDatabase.IsPluginNativePath("Plugins/Ads/Android/build.gradle.kts"), "native path android");
            Check(AssetDatabase.IsPluginNativePath("Plugins/Ads/iOS/Ads.m"), "native path ios");
            Check(!AssetDatabase.IsPluginNativePath("Plugins/Ads/Runtime/Ads.cs"), "runtime cs asset yolu");
            Check(!AssetDatabase.IsPluginNativePath("Scenes/Plugins/Android/x.png"), "sahte Plugins klasoru degil");
        }
        catch (Exception e)
        {
            _fail++;
            Console.WriteLine("[plugin] EXCEPTION: " + e);
        }
        finally
        {
            Plugin.ResetAll();
            Plugin.MockHost.Instance.Clear();
            Plugin.LoadSettingsBlob(null, null);
            Plugin.Host = prevHost;
            Plugin.SettingsLoader = prevLoader;
        }
        Console.WriteLine($"[plugin] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[plugin] FAIL: {name}"); }
    }
}
#endif
