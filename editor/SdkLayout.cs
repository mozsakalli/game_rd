using System;
using System.IO;

namespace DigitoyEditor;

// Editorun yerlesimi (docs/editor-distribution.md Faz 1): KURULU mod (publish paketi: exe yaninda sdk/) ya da
// DEV mod (repo: editor/bin/... altindan kosar, aotcompiler/ kardes klasor). Repo yollarina dokunan herkes buradan gecer.
public static class SdkLayout
{
    static readonly string ExeDir = AppContext.BaseDirectory;

    public static string SdkDir => Path.Combine(ExeDir, "sdk");
    public static bool Installed => File.Exists(Path.Combine(SdkDir, "aotcompiler", "aotcompiler.dll"));

    // aotcompiler'in prebuilt/taze IL'leri (AotCompatCheck referanslari): kurulu -> sdk/aotcompiler; dev -> aotcompiler/obj/aot-il
    public static string AotIlDir => Installed
        ? Path.Combine(SdkDir, "aotcompiler")
        : Path.Combine(App.RepoRoot, "aotcompiler", "obj", "aot-il");

    // aotcompiler calisma dizini: obj/ (native-static, player-c, aot-il) buraya yazilir.
    // Dev: aotcompiler/ (c_runtime goreli yollari, kaynaktan derleme). Kurulu: kullanici dizini (paket salt-okunur olabilir).
    public static string AotWorkDir => Installed
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitoyEngine", "aot-work")
        : Path.Combine(App.RepoRoot, "aotcompiler");

    // aotcompiler komutu (dotnet ile): kurulu -> paketlenmis dll; dev -> dotnet run (gerekirse derler; csproj yalniz dev'de).
    // Donus null = bulunamadi (hata mesaji out).
    public static bool TryAotCommand(string aotArgs, out string file, out string args, out string error)
    {
        file = "dotnet"; args = null; error = null;
        if (Installed)
        {
            string dll = Path.Combine(SdkDir, "aotcompiler", "aotcompiler.dll");
            Directory.CreateDirectory(AotWorkDir);
            args = $"\"{dll}\" {aotArgs}";
            return true;
        }
        string proj = Path.Combine(App.RepoRoot, "aotcompiler", "aotcompiler.csproj");
        if (!File.Exists(proj))
        {
            error = "aotcompiler bulunamadi (ne sdk/aotcompiler/aotcompiler.dll ne de " + proj + ")";
            return false;
        }
        args = $"run --project \"{proj}\" --no-launch-profile -v q -- {aotArgs}";
        return true;
    }
}
