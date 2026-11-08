using System.Text.Json.Nodes;
using XmlToScene;

// xml2scene <layout/x.xml> [--parent N] [--dry] [--verbose] [--force-prefabs]
//           [--src <eskiAssets>] [--project <Projects/Oyun>] [--port 5157]
// Eski Digiplay XML sahnesini acik editordeki sahneye (RPC) GO+LayoutBox agaci olarak uygular.
// include / T:ui:* sablonlari once prefab'a cevrilir (bagimlilik sirasiyla).

string? xml = null; int parent = 0, port = 5157; bool dry = false, verbose = false, force = false, camera = true;
float designW = 1440, designH = 960; // eski Stage2D.Setup(1440,960)
string src = @"C:\Work\digitoygames\digiplay-net\TekelOkey\Assets";
string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Projects", "TekelOkey"));
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--parent": parent = int.Parse(args[++i]); break;
        case "--port": port = int.Parse(args[++i]); break;
        case "--dry": dry = true; break;
        case "--verbose": verbose = true; break;
        case "--force-prefabs": force = true; break;
        case "--no-camera": camera = false; break;
        case "--design": designW = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); designH = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--src": src = args[++i]; break;
        case "--project": project = args[++i]; break;
        default: xml = args[i]; break;
    }
}
if (xml == null)
{
    Console.Error.WriteLine("kullanim: xml2scene <layout/x.xml> [--parent N] [--dry] [--verbose] [--force-prefabs] [--design W H] [--no-camera] [--src dir] [--project dir]");
    return 2;
}
string projAssets = Path.Combine(project, "Assets");
if (!Directory.Exists(projAssets)) { Console.Error.WriteLine("proje Assets yok: " + projAssets); return 2; }

var index = new AssetIndex(projAssets);
Console.WriteLine($"texture index: {index.Count} gorsel");
try
{
    using var rpc = new Rpc(port, dry) { Verbose = verbose };
    var types = new HashSet<string>(StringComparer.Ordinal);
    if (!dry)
        foreach (var t in rpc.Call("comp.types", new JsonObject())["types"]!.AsArray()) types.Add(t!.ToString());
    var conv = new Converter(src, projAssets, index, rpc, types) { ForcePrefabs = force, DesignW = designW, DesignH = designH, EnsureCamera = camera };
    conv.ConvertScene(xml.Replace('\\', '/'), parent);
    Console.WriteLine("tamam");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("HATA: " + ex.Message);
    if (verbose) Console.Error.WriteLine(ex);
    return 1;
}
