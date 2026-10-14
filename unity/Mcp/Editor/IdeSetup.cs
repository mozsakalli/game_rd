using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Digitoy.Mcp
{
    // Projeyi tek adimda VS Code'a baglar: kopruyu Library/DigitoyMcp/bridge'e derler, .vscode/mcp.json'a
    // 'unity-editor' sunucusunu birlestirerek yazar. Unity acilisinda otomatik; menu ile tekrarlanabilir.
    [InitializeOnLoad]
    public static class IdeSetup
    {
        public const string PackageName = "com.digitoygames.mcp";
        public const string ServerName = "unity-editor";

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        public static string BridgeDir => Path.Combine(ProjectRoot, "Library", "DigitoyMcp", "bridge");
        public static string BridgeDll => Path.Combine(BridgeDir, "unity-mcp-bridge.dll");
        public static string McpJsonPath => Path.Combine(ProjectRoot, ".vscode", "mcp.json");

        static IdeSetup()
        {
            // Ilk update'te kos: derleme/IO acilisi geciktirmesin, RpcHost.Port hazir olsun.
            EditorApplication.delayCall += () => Run(false);
        }

        [MenuItem("Digitoy/MCP/Setup VS Code (bridge + .vscode/mcp.json)")]
        static void MenuSetup() => Run(true);

        [MenuItem("Digitoy/MCP/Rebuild bridge")]
        static void MenuRebuild() { BuildBridge(true, true); WriteMcpJson(true); }

        public static string PackageDir()
        {
            string p = Path.GetFullPath("Packages/" + PackageName);
            return Directory.Exists(p) ? p : null;
        }

        public static void Run(bool verbose)
        {
            try
            {
                BuildBridge(false, verbose);
                WriteMcpJson(verbose);
            }
            catch (Exception ex) { Debug.LogWarning("[mcp] setup: " + ex.Message); }
        }

        // Kaynak (Bridge~/*.cs, csproj) dll'den yeniyse ya da dll yoksa derler. PackageCache salt-okunur olabilir: cikti Library'ye.
        public static bool BuildBridge(bool force, bool verbose)
        {
            string pkg = PackageDir();
            if (pkg == null) { Debug.LogWarning("[mcp] paket dizini bulunamadi"); return false; }
            string src = Path.Combine(pkg, "Bridge~");
            if (!Directory.Exists(src)) { Debug.LogWarning("[mcp] Bridge~ yok: " + src); return false; }
            if (!force && File.Exists(BridgeDll))
            {
                var dllTime = File.GetLastWriteTimeUtc(BridgeDll);
                bool stale = false;
                foreach (var f in Directory.GetFiles(src, "*.cs*", SearchOption.TopDirectoryOnly))
                    if (File.GetLastWriteTimeUtc(f) > dllTime) { stale = true; break; }
                if (!stale) { if (verbose) Debug.Log("[mcp] bridge guncel: " + BridgeDll); return true; }
            }
            Directory.CreateDirectory(BridgeDir);
            // obj/ da Library'ye: paket dizini (PackageCache) salt-okunur olabilir.
            string artifacts = Path.Combine(ProjectRoot, "Library", "DigitoyMcp", "artifacts");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{src}\" -c Release -o \"{BridgeDir}\" --artifacts-path \"{artifacts}\" --nologo -v q",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = src,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(120000)) { p.Kill(); Debug.LogWarning("[mcp] bridge derlemesi zaman asimi"); return false; }
                    if (p.ExitCode != 0 || !File.Exists(BridgeDll))
                    {
                        Debug.LogWarning("[mcp] bridge derlenemedi (dotnet SDK 9 kurulu mu?):\n" + output);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[mcp] 'dotnet' calistirilamadi (.NET SDK 9 gerekli): " + ex.Message);
                return false;
            }
            Debug.Log("[mcp] bridge derlendi: " + BridgeDll);
            return true;
        }

        // Mevcut mcp.json korunur; yalniz servers.unity-editor guncellenir. Icerik ayniysa dosyaya dokunulmaz.
        public static void WriteMcpJson(bool verbose)
        {
            JNode root = null;
            if (File.Exists(McpJsonPath))
            {
                try { root = JNode.Parse(File.ReadAllText(McpJsonPath)); }
                catch (Exception ex) { Debug.LogWarning("[mcp] .vscode/mcp.json okunamadi, yeniden yazilacak: " + ex.Message); }
            }
            if (root == null || !root.IsMap) root = JNode.Map();
            var servers = root.Get("servers");
            if (servers == null || !servers.IsMap) { servers = JNode.Map(); root.Set("servers", servers); }

            var server = JNode.Map();
            server.Add("type", "stdio");
            server.Add("command", "dotnet");
            server.Add("args", JNode.Seq().Push(JNode.Str_(BridgeDll.Replace('\\', '/'))));
            server.Add("env", JNode.Map().Add("UNITY_MCP_PORT", RpcHost.Port > 0 ? RpcHost.Port.ToString() : RpcHost.DefaultPort.ToString()));

            var existing = servers.Get(ServerName);
            if (existing != null && JNode.Equal(existing, server))
            {
                if (verbose) Debug.Log("[mcp] .vscode/mcp.json guncel");
                return;
            }
            servers.Set(ServerName, server);
            Directory.CreateDirectory(Path.GetDirectoryName(McpJsonPath));
            File.WriteAllText(McpJsonPath, Pretty(root));
            Debug.Log("[mcp] yazildi: " + McpJsonPath + " (VS Code'u proje kokunde ac; MCP sunucusu '" + ServerName + "')");
        }

        // Okunabilir JSON (JNode.Write kompakt).
        static string Pretty(JNode n)
        {
            var sb = new System.Text.StringBuilder();
            Write(n, sb, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void Write(JNode n, System.Text.StringBuilder sb, int indent)
        {
            string pad = new string(' ', indent * 2), pad2 = new string(' ', (indent + 1) * 2);
            if (n.IsMap)
            {
                if (n.Fields.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                for (int i = 0; i < n.Fields.Count; i++)
                {
                    sb.Append(pad2);
                    JNode.Str_(n.Fields[i].Key).Write(sb);
                    sb.Append(": ");
                    Write(n.Fields[i].Value, sb, indent + 1);
                    if (i < n.Fields.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(pad).Append('}');
            }
            else if (n.IsSeq)
            {
                if (n.Items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < n.Items.Count; i++)
                {
                    sb.Append(pad2);
                    Write(n.Items[i], sb, indent + 1);
                    if (i < n.Items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(pad).Append(']');
            }
            else n.Write(sb);
        }
    }
}
