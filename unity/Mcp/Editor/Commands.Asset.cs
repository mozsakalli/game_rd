using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Digitoy.Mcp
{
    // Derleme sonucu domain reload'u asmali: mesajlar Library/DigitoyMcp/compile.json'a yazilir; code.build okur.
    [InitializeOnLoad]
    static class CompileLog
    {
        static readonly List<JNode> _messages = new List<JNode>();
        static bool _any;

        public static string FilePath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "DigitoyMcp", "compile.json");

        static CompileLog()
        {
            CompilationPipeline.compilationStarted += _ => { _messages.Clear(); _any = true; };
            CompilationPipeline.assemblyCompilationFinished += (asm, msgs) =>
            {
                foreach (var m in msgs)
                {
                    if (m.type != CompilerMessageType.Error && m.type != CompilerMessageType.Warning) continue;
                    _messages.Add(JNode.Map()
                        .Add("type", m.type == CompilerMessageType.Error ? "error" : "warning")
                        .Add("assembly", Path.GetFileName(asm))
                        .Add("file", m.file).Add("line", m.line).Add("column", m.column)
                        .Add("message", m.message));
                }
            };
            CompilationPipeline.compilationFinished += _ =>
            {
                if (!_any) return;
                var r = JNode.Map();
                r.Add("finishedAt", DateTime.UtcNow.ToString("o"));
                var seq = JNode.Seq();
                int errors = 0;
                foreach (var m in _messages) { if (m.GetScalar("type", "") == "error") errors++; seq.Push(m); }
                r.Add("errors", errors);
                r.Add("messages", seq);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, r.ToString());
                }
                catch { }
            };
        }

        public static JNode Read()
        {
            try { return File.Exists(FilePath) ? JNode.Parse(File.ReadAllText(FilePath)) : null; }
            catch { return null; }
        }
    }

    // Editor log halka tamponu (log.tail) + komut suresince dusen hatalari toplamak icin monoton sayac.
    [InitializeOnLoad]
    public static class LogBuffer
    {
        const int Cap = 2000;
        static readonly List<JNode> _entries = new List<JNode>();
        static readonly object _lock = new object();
        static long _seq; // toplam kayit sayisi (Clear ile sifirlanmaz)

        static LogBuffer()
        {
            Application.logMessageReceivedThreaded += (msg, stack, type) =>
            {
                var e = JNode.Map().Add("type", type.ToString()).Add("msg", msg.Length > 500 ? msg.Substring(0, 500) + "…" : msg);
                if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                    e.Add("at", stack.Length > 400 ? stack.Substring(0, 400) + "…" : stack);
                lock (_lock)
                {
                    e.Add("seq", JNode.Num(++_seq));
                    _entries.Add(e);
                    if (_entries.Count > Cap) _entries.RemoveRange(0, _entries.Count - Cap);
                }
            };
        }

        public static long Mark() { lock (_lock) return _seq; }

        // mark'tan sonra dusen Error/Exception/Assert kayitlari (en fazla max; toplam ayrica).
        public static JNode ErrorsSince(long mark, int max, out int total)
        {
            var seq = JNode.Seq();
            total = 0;
            lock (_lock)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    if (long.Parse(e.GetScalar("seq", "0")) <= mark) continue;
                    string t = e.GetScalar("type", "");
                    if (t != "Error" && t != "Exception" && t != "Assert") continue;
                    total++;
                    if (seq.Items.Count < max) seq.Push(e.Clone());
                }
            }
            return seq;
        }

        public static JNode Tail(int n, string filter)
        {
            var seq = JNode.Seq();
            lock (_lock)
            {
                int start = Math.Max(0, _entries.Count - n);
                for (int i = start; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    if (filter.Length > 0 && !string.Equals(e.GetScalar("type", ""), filter, StringComparison.OrdinalIgnoreCase)) continue;
                    seq.Push(e.Clone());
                }
            }
            return seq;
        }

        public static void Clear() { lock (_lock) _entries.Clear(); }
    }

    public static partial class Commands
    {
        static string GuardAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new RpcError("path bos");
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) path = "Assets/" + path.TrimStart('/');
            string root = Path.GetDirectoryName(Application.dataPath);
            string full = Path.GetFullPath(Path.Combine(root, path));
            string assets = Path.GetFullPath(Application.dataPath);
            if (!full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new RpcError("yol Assets disina cikiyor");
            return full;
        }

        static string ToAssetPath(string full)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            return full.Substring(root.Length + 1).Replace('\\', '/');
        }

        [RpcCommand("asset.write", "Assets altina dosya yazar (yol Assets'e goreli ya da 'Assets/..'); import tetiklenir (.cs → derleme + domain reload: baglanti kopar, code.build ile sonucu al). .unity/.prefab YAZMA: scene.apply kullan")]
        static JNode AssetWrite(string path, string content)
        {
            string full = GuardAssetPath(path);
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext == ".unity" || ext == ".prefab")
                throw new RpcError("sahne/prefab dosyasi dogrudan yazilmaz: scene.apply / prefab.open kullan");
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            string ap = ToAssetPath(full);
            AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate);
            var r = JNode.Map().Add("path", ap);
            if (ext == ".cs") r.Add("note", "script yazildi: code.build ile derleme sonucunu al (domain reload baglantiyi dusurur)");
            return r;
        }

        [RpcCommand("asset.read", "Assets altindan metin dosyasi okur")]
        static JNode AssetRead(string path)
        {
            string full = GuardAssetPath(path);
            if (!File.Exists(full)) throw new RpcError($"dosya yok: {path}");
            return JNode.Map().Add("path", ToAssetPath(full)).Add("content", File.ReadAllText(full));
        }

        [RpcCommand("asset.list", "Asset'ler: filter (AssetDatabase arama: ad parcasi, 't:Sprite', 'l:etiket'), folder ('Assets/UI'). Gorsellerde width/height")]
        static JNode AssetList(string filter = "", string folder = "", int max = 500)
        {
            var folders = folder.Length > 0 ? new[] { folder } : null;
            var guids = folders != null ? AssetDatabase.FindAssets(filter, folders) : AssetDatabase.FindAssets(filter);
            var seq = JNode.Seq();
            int n = 0;
            foreach (var guid in guids)
            {
                if (n++ >= max) break;
                string p = AssetDatabase.GUIDToAssetPath(guid);
                seq.Push(AssetInfoNode(p, guid, false));
            }
            return JNode.Map().Add("assets", seq).Add("count", seq.Items.Count).Add("total", guids.Length);
        }

        [RpcCommand("asset.info", "Tek asset: yol/guid/tip/alt-asset'ler/import ayarlari (gorsel: boyut, sprite'lar)")]
        static JNode AssetInfo(string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) throw new RpcError($"asset yok: {path}");
            return AssetInfoNode(path, guid, true);
        }

        static JNode AssetInfoNode(string path, string guid, bool deep)
        {
            var m = JNode.Map().Add("path", path).Add("guid", guid);
            var t = AssetDatabase.GetMainAssetTypeAtPath(path);
            m.Add("type", t != null ? t.Name : "Folder");
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
            if (File.Exists(full)) m.Add("bytes", (int)new FileInfo(full).Length);
            var imp = AssetImporter.GetAtPath(path);
            if (imp is TextureImporter ti)
            {
                int w = 0, h = 0;
                try
                {
                    var mi = typeof(TextureImporter).GetMethod("GetSourceTextureWidthAndHeight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (mi != null) { var args = new object[] { 0, 0 }; mi.Invoke(ti, args); w = (int)args[0]; h = (int)args[1]; }
                }
                catch { }
                if (w == 0 && AssetDatabase.LoadAssetAtPath<Texture2D>(path) is Texture2D tex) { w = tex.width; h = tex.height; }
                m.Add("width", w).Add("height", h);
                m.Add("textureType", ti.textureType.ToString());
                m.Add("pixelsPerUnit", ti.spritePixelsPerUnit);
                if (ti.textureType == TextureImporterType.Sprite) m.Add("spriteMode", ti.spriteImportMode.ToString());
            }
            if (deep)
            {
                var subs = JNode.Seq();
                foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    if (o != null) subs.Push(JNode.Map().Add("name", o.name).Add("type", o.GetType().Name).Add("ref", path + "#" + o.name));
                if (subs.Items.Count > 0) m.Add("subAssets", subs);
                if (imp != null) m.Add("importer", imp.GetType().Name);
                var labels = AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(path));
                if (labels.Length > 0) { var ls = JNode.Seq(); foreach (var l in labels) ls.Push(JNode.Str_(l)); m.Add("labels", ls); }
            }
            return m;
        }

        [RpcCommand("asset.reimport", "Asset'i zorla yeniden import eder")]
        static JNode AssetReimport(string path)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) throw new RpcError($"asset yok: {path}");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            return JNode.Map().Add("path", path);
        }

        [RpcCommand("asset.delete", "Asset siler (geri alinamaz; kullanici onayi olmadan cagirma)")]
        static JNode AssetDelete(string path)
        {
            if (!AssetDatabase.DeleteAsset(path)) throw new RpcError($"silinemedi: {path}");
            return JNode.Map().Add("deleted", path);
        }

        [RpcCommand("asset.create", "ScriptableObject asset'i olusturur (type: sinif adi, path: Assets/..asset); props ile alanlar")]
        static JNode AssetCreate(string type, string path, JNode props = null)
        {
            Type t = null;
            foreach (var c in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
                if (c.Name == type || c.FullName == type) { t = c; break; }
            if (t == null) throw new RpcError($"ScriptableObject tipi yok: {type}");
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) path += ".asset";
            var so = ScriptableObject.CreateInstance(t);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            AssetDatabase.CreateAsset(so, path);
            if (props != null && props.IsMap)
            {
                var ser = new SerializedObject(so);
                foreach (var kv in props.Fields)
                {
                    var p = ser.FindProperty(kv.Key) ?? ser.FindProperty("m_" + kv.Key) ?? throw new RpcError($"alan yok: {kv.Key}");
                    PropIO.Assign(p, kv.Value, kv.Key, null);
                }
                ser.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
            return JNode.Map().Add("path", path).Add("guid", AssetDatabase.AssetPathToGUID(path));
        }

        [RpcCommand("code.build", "Derleme: Refresh + script derleme. Derleme domain reload yapar (baglanti kopar) → tekrar cagir: status=idle ve son derlemenin hatalari doner. force=true: degisiklik olmasa da derler")]
        static JNode CodeBuild(bool force = false)
        {
            var r = JNode.Map();
            if (EditorApplication.isCompiling)
            {
                r.Add("status", "compiling");
                r.Add("note", "derleme suruyor; biraz sonra tekrar cagir");
                return r;
            }
            if (force) CompilationPipeline.RequestScriptCompilation();
            else AssetDatabase.Refresh();
            if (EditorApplication.isCompiling || force)
            {
                r.Add("status", "started");
                r.Add("note", "derleme basladi; domain reload baglantiyi dusurur — code.build'i tekrar cagir, hatalar gelir");
                return r;
            }
            r.Add("status", "idle");
            var last = CompileLog.Read();
            if (last != null)
            {
                r.Add("errors", last.Get("errors") ?? JNode.Num(0));
                r.Add("messages", last.Get("messages") ?? JNode.Seq());
                r.Add("finishedAt", last.GetScalar("finishedAt", ""));
            }
            else r.Add("errors", 0).Add("messages", JNode.Seq());
            return r;
        }

        [RpcCommand("log.tail", "Editor/oyun log'unun son N kaydi (filter: Log|Warning|Error|Exception); clear=true sonra temizler")]
        static JNode LogTail(int n = 50, string filter = "", bool clear = false)
        {
            var seq = LogBuffer.Tail(Math.Max(1, Math.Min(n, 2000)), filter);
            if (clear) LogBuffer.Clear();
            return JNode.Map().Add("entries", seq).Add("count", seq.Items.Count);
        }

        [RpcCommand("log.clear", "Log tamponunu temizler")]
        static JNode LogClear() { LogBuffer.Clear(); return JNode.Map().Add("ok", true); }

        [RpcCommand("project.info", "Proje: ad, Unity surumu, yol, aktif sahne, platform, Enter Play Mode ayari")]
        static JNode ProjectInfo()
        {
            var r = JNode.Map();
            r.Add("name", Application.productName);
            r.Add("unity", Application.unityVersion);
            r.Add("root", Path.GetDirectoryName(Application.dataPath));
            r.Add("activeScene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            r.Add("platform", EditorUserBuildSettings.activeBuildTarget.ToString());
            r.Add("domainReloadOnPlay", DomainReloadOnPlay);
            r.Add("port", RpcHost.Port);
            r.Add("bridge", IdeSetup.BridgeDll);
            r.Add("bridgeBuilt", File.Exists(IdeSetup.BridgeDll));
            r.Add("mcpJson", IdeSetup.McpJsonPath);
            return r;
        }
    }
}
