using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Editor icinden release player uretimi (Unity File > Build karsiligi). Zincir:
//   1) oyun kodu temiz mi (suren/hatali derleme varsa iptal; Registry.g.cs katalogla yazilir)
//   2) game.pak (AssetPackBuilder: Scenes In Build'den erisilen asset'ler) — ana thread, GPU verify
//   3) aotcompiler 'player <proje>' — ARKA PLAN sureci: DigitoyPlayer Release (registry +
//      scriptler gomulu, DE_AOT) -> CIL -> IR -> C -> clang + statik native. Cikti
//      <proje>/Build/<ProjeAdi>.exe, game.pak'in yanina.
// Sureç ciktisi satir satir Console'a akar; durum toolbar'da (AssetWatcher.Status deseni).
// Ayni anda tek build; derleyici calisma dizini aotcompiler/ (c_runtime goreli yollari).
public static class PlayerBuilder
{
    public static string Status { get; private set; } = "";
    public static bool Failed { get; private set; }
    public static bool IsRunning => _task != null && !_task.IsCompleted;

    // RPC/otomasyon sinyali: her biten build'de artar; son sonuc + cikti kuyrugu.
    public static int Version { get; private set; }
    public static bool LastOk { get; private set; }
    public static string LastExe { get; private set; } = "";
    public static string LastOutput { get; private set; } = "";

    static Task _task;

    [MenuItem("Project/Build Player", 2)]
    static void BuildMenu() => Start();

    // Donus: zincir baslatildi (arka plan surecinin sonucu Version/LastOk ile izlenir).
    // false = on kosul/pak hatasi, loglandi.
    public static bool Start()
    {
        if (IsRunning)
        {
            EditorLog.Warning("[player-build] zaten calisiyor");
            return false;
        }
        var project = App.Project;
        if (project == null)
            return Fail("acik proje yok");
        if (AssetWatcher.Status.Length > 0)
            return Fail("oyun kodu derlemesi suruyor; bitince tekrar deneyin");
        if (AssetWatcher.Failed)
            return Fail("oyun kodu derlenemiyor; once hatalari giderin (Console)");
        string registry = Path.Combine(project.LibraryPath, "Build", "Registry.g.cs");
        if (!File.Exists(registry))
            return Fail("Registry.g.cs yok (katalog kurulmadi): " + registry);
        string aotProj = Path.Combine(App.RepoRoot, "aotcompiler", "aotcompiler.csproj");
        if (!File.Exists(aotProj))
            return Fail("aotcompiler bulunamadi: " + aotProj);

        Failed = false;
        Status = "Building pak...";
        EditorLog.Info("[player-build] 1/2 asset pack");
        if (!AssetPackBuilder.Build())
            return Fail("game.pak uretilemedi");

        string projName = Path.GetFileName(project.Root.TrimEnd('\\', '/'));
        string exe = Path.Combine(project.Root, "Build", projName + ".exe");
        Status = "Building player...";
        EditorLog.Info($"[player-build] 2/2 aotcompiler player {project.Root}");
        _task = Task.Run(() => RunAot(aotProj, project.Root, exe));
        return true;
    }

    static bool Fail(string why)
    {
        Failed = true;
        Status = "Player build failed";
        EditorLog.Error("[player-build] " + why);
        return false;
    }

    // Arka plan: dotnet run (aotcompiler gerekirse derlenir) -> satirlar Console'a.
    static void RunAot(string aotProj, string projectRoot, string exe)
    {
        var sw = Stopwatch.StartNew();
        var tail = new List<string>();
        int exit = -1;
        try
        {
            var psi = new ProcessStartInfo("dotnet",
                $"run --project \"{aotProj}\" --no-launch-profile -v q -- player \"{projectRoot}\"")
            {
                WorkingDirectory = Path.GetDirectoryName(aotProj), // RepoRoot = ".." varsayimi
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            void OnLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                lock (tail)
                {
                    tail.Add(line);
                    if (tail.Count > 200)
                        tail.RemoveAt(0);
                }
                if (line.Contains("[HATA]") || line.Contains("error") || line.Contains("basarisiz"))
                    EditorLog.Error("[aot] " + line);
                else
                    EditorLog.Info("[aot] " + line);
            }
            proc.OutputDataReceived += (_, e) => OnLine(e.Data);
            proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            exit = proc.ExitCode;
        }
        catch (Exception e)
        {
            EditorLog.Error("[player-build] aotcompiler baslatilamadi: " + e.Message);
        }

        bool ok = exit == 0 && File.Exists(exe);
        lock (tail)
            LastOutput = string.Join("\n", tail);
        LastOk = ok;
        LastExe = ok ? exe : "";
        Failed = !ok;
        Status = ok ? "" : "Player build failed";
        if (ok)
            EditorLog.Info($"[player-build] OK -> {exe} ({new FileInfo(exe).Length / 1024} KB, {sw.Elapsed.TotalSeconds:F0} s)");
        else
            EditorLog.Error($"[player-build] FAIL (exit {exit}, {sw.Elapsed.TotalSeconds:F0} s) — ayrintilar yukarida [aot] satirlarinda");
        Version++;
    }
}
