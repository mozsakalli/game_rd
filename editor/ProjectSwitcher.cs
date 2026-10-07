using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Kullanici-GENELI editor tercihleri (projeden bagimsiz): son acilan proje + recent listesi.
// Konum: %LOCALAPPDATA%/DigitoyEngine/EditorPrefs.asset (macOS: ~/Library/Application Support/...).
// Proje-bazli ayarlar UserSettings/EditorSettings.asset'te kalir (o dosya "hangi proje" sorusunu cevaplayamaz).
[Serializable]
public class EditorPrefs
{
    public string lastProject = "";
    public List<string> recentProjects = new();

    const int MaxRecent = 10;

    public static string PrefsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitoyEngine", "EditorPrefs.asset");

    public static EditorPrefs Load()
    {
        try
        {
            if (File.Exists(PrefsPath))
                return ObjectSerializer.Load<EditorPrefs>(PrefsPath) ?? new EditorPrefs();
        }
        catch (Exception e) { Console.WriteLine("[prefs] okunamadi: " + e.Message); }
        return new EditorPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
            ObjectSerializer.Save(this, PrefsPath);
        }
        catch (Exception e) { Console.WriteLine("[prefs] yazilamadi: " + e.Message); }
    }

    // Acilan proje listenin basina gelir (tekil, en fazla MaxRecent).
    public void Touch(string projectRoot)
    {
        string full = Path.GetFullPath(projectRoot).TrimEnd('\\', '/');
        lastProject = full;
        recentProjects.RemoveAll(p => string.Equals(Path.GetFullPath(p).TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase));
        recentProjects.Insert(0, full);
        if (recentProjects.Count > MaxRecent)
            recentProjects.RemoveRange(MaxRecent, recentProjects.Count - MaxRecent);
    }
}

// Proje acma/degistirme (Unity modeli): editorun tum durumu (katalog, ALC, panel/dock, watcher, RPC) acilista
// kurulur; canli degistirmek yerine editor YENI PROJE YOLUYLA yeniden baslatilir. Secim: native klasor
// secici (File/Open Project...) ya da recent listesi + repo Projects/* (File/Open Recent...).
public static class ProjectSwitcher
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib, EntryPoint = "de_dialog_pick_folder")]
    static extern unsafe int PickFolder(IntPtr glfwWindow, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, byte* outPath, int cap);

    // Gecerli proje koku: Assets/ ya da ProjectSettings/ klasoru olan dizin (bos klasor de kabul: yeni proje tohumlanir).
    public static bool IsProjectRoot(string dir)
        => Directory.Exists(dir) && (Directory.Exists(Path.Combine(dir, "Assets")) || Directory.Exists(Path.Combine(dir, "ProjectSettings"))
            || Directory.GetFileSystemEntries(dir).Length == 0);

    // Acilista kullanilacak proje: CLI arg > prefs.lastProject (hala varsa) > Projects/Sandbox.
    public static string ResolveStartupProject(string cliArg, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(cliArg))
            return cliArg;
        var prefs = EditorPrefs.Load();
        if (!string.IsNullOrEmpty(prefs.lastProject) && IsProjectRoot(prefs.lastProject))
            return prefs.lastProject;
        return fallback;
    }

    [MenuItem("File/Open Project...", 10)]
    static unsafe void OpenProjectMenu()
    {
        var buf = new byte[4096];
        int ok;
        fixed (byte* p = buf)
            ok = PickFolder(EditorMenu.MainWindow, "Proje klasoru secin (Assets/ iceren)", p, buf.Length);
        if (ok == 0)
            return;
        int n = Array.IndexOf(buf, (byte)0);
        string path = System.Text.Encoding.UTF8.GetString(buf, 0, n < 0 ? buf.Length : n);
        Open(path);
    }

    [MenuItem("File/Open Recent...", 11)]
    static void OpenRecentMenu()
    {
        var prefs = EditorPrefs.Load();
        var items = new List<(string, Action)>();
        string current = App.Project?.Root ?? "";
        foreach (var p in prefs.recentProjects)
        {
            if (!IsProjectRoot(p)) continue;
            string path = p;
            string label = Path.GetFileName(p.TrimEnd('\\', '/')) + (SamePath(p, current) ? "  (acik)" : "") + "   " + p;
            items.Add((label.Replace('/', '\\'), () => Open(path)));
        }
        // Repo Projects/* kardesleri (recent'ta olmayanlar)
        string projectsDir = Path.Combine(App.RepoRoot, "Projects");
        if (Directory.Exists(projectsDir))
        {
            bool sep = false;
            foreach (var d in Directory.GetDirectories(projectsDir))
            {
                if (!IsProjectRoot(d) || prefs.recentProjects.Exists(r => SamePath(r, d))) continue;
                if (!sep) { items.Add(("-", null)); sep = true; }
                string path = d;
                items.Add(("Projects\\" + Path.GetFileName(d), () => Open(path)));
            }
        }
        if (items.Count == 0)
            items.Add(("(recent proje yok)", null));
        EditorMenu.ShowContext(items.ToArray());
    }

    static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // Projeyi yeni editor surecinde ac, bu sureci kapat (cikis yolu layout/ayarlari kaydeder).
    public static void Open(string projectRoot)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        if (!IsProjectRoot(projectRoot))
        {
            EditorLog.Error("[project] proje klasoru degil (Assets/ yok): " + projectRoot);
            return;
        }
        if (App.Project != null && SamePath(projectRoot, App.Project.Root))
        {
            EditorLog.Info("[project] zaten acik: " + projectRoot);
            return;
        }
        if (PlayMode.State != PlayState.Editing)
            PlayMode.Stop();
        var prefs = EditorPrefs.Load();
        prefs.Touch(projectRoot);
        prefs.Save();
        if (!Relaunch(projectRoot))
            return;
        EditorLog.Info("[project] yeni editor basladi: " + projectRoot + " — bu pencere kapaniyor");
        App.RequestClose();
    }

    static bool Relaunch(string projectRoot)
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            string args = "\"" + projectRoot + "\"";
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                args = "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\" " + args; // dotnet Editor.dll <proje>
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            Process.Start(psi);
            return true;
        }
        catch (Exception e)
        {
            EditorLog.Error("[project] editor yeniden baslatilamadi: " + e.Message);
            return false;
        }
    }
}
