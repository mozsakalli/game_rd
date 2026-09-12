using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// Acik proje: Unity proje modeli — Assets/ (tum icerik + kod), ProjectSettings/,
// Library/ (turetilmis cache: typemap, derleme ciktisi; git'e girmez).
public sealed class Project
{
    public string Root;
    public string Name = "Untitled";
    public string StartScene = "Scenes/Main.scene"; // Assets'e goreli

    public string AssetsPath => Path.Combine(Root, "Assets");
    public string LibraryPath => Path.Combine(Root, "Library");
    public string StartScenePath => Path.Combine(AssetsPath, StartScene);

    public static Project Load(string root)
    {
        var p = new Project { Root = Path.GetFullPath(root) };
        var settings = Path.Combine(p.Root, "ProjectSettings", "project.yaml");
        if (File.Exists(settings))
        {
            var doc = Yaml.Parse(File.ReadAllText(settings));
            p.Name = doc.GetScalar("name", p.Name);
            p.StartScene = doc.GetScalar("startScene", p.StartScene);
        }
        Directory.CreateDirectory(p.AssetsPath);
        Directory.CreateDirectory(p.LibraryPath);
        return p;
    }
}
