using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Acik proje: Unity proje modeli — Assets/ (tum icerik + kod), ProjectSettings/,
// Library/ (turetilmis cache: typemap, derleme ciktisi; git'e girmez).
// Yayin kimligi/ilk sahne PlayerSettings'te (ProjectSettings/PlayerSettings.asset);
// eski project.yaml yalniz ilk acilista tohum olarak okunur ve .asset'e tasinir.
public sealed class Project
{
    public string Root;
    public PlayerSettings Player = new();

    public string Name => Player.productName;
    public string StartScene => Player.startScene; // Assets'e goreli

    public string AssetsPath => Path.Combine(Root, "Assets");
    public string LibraryPath => Path.Combine(Root, "Library");
    public string ProjectSettingsPath => Path.Combine(Root, "ProjectSettings");
    public string PlayerSettingsPath => Path.Combine(ProjectSettingsPath, "PlayerSettings.asset");
    public string StartScenePath => Path.Combine(AssetsPath, StartScene);

    public static Project Load(string root)
    {
        var p = new Project { Root = Path.GetFullPath(root) };
        if (File.Exists(p.PlayerSettingsPath))
        {
            try { p.Player = ObjectSerializer.Load<PlayerSettings>(p.PlayerSettingsPath); }
            catch (System.Exception e) { EditorLog.Error("[project] PlayerSettings okunamadi: " + e.Message); }
        }
        else
        {
            var legacy = Path.Combine(p.ProjectSettingsPath, "project.yaml");
            if (File.Exists(legacy))
            {
                var doc = Yaml.Parse(File.ReadAllText(legacy));
                p.Player.productName = doc.GetScalar("name", p.Player.productName);
                p.Player.startScene = doc.GetScalar("startScene", p.Player.startScene);
            }
            p.SavePlayerSettings(); // standart dosya ilk acilista olusur
        }
        Directory.CreateDirectory(p.AssetsPath);
        Directory.CreateDirectory(p.LibraryPath);
        return p;
    }

    public void SavePlayerSettings()
    {
        Directory.CreateDirectory(ProjectSettingsPath);
        ObjectSerializer.Save(Player, PlayerSettingsPath);
    }
}
