using System;
using System.IO;
using System.Text;
using DigitoyEngine;

namespace DigitoyEditor;

// Oyun projesinin IDE/AI dosyalari (proje koku). Her acilista yazilir; icerik ayniysa dokunulmaz.
//  .vscode/mcp.json               -> bu editorun mcp-bridge'ine isaret eder (MAKINEYE OZEL yol; gitignore'lu, acilista yenilenir)
//  .github/copilot-instructions.md -> oyun-tarafi AI kurallari (sablon; yalniz YOKSA yazilir, kullanici duzenler, git'e girer)
//  .gitignore                      -> yalniz yoksa (Library/Build/UserSettings + makineye ozel IDE dosyalari)
static class IdeFiles
{
    public static void Write(Project project)
    {
        WriteIfChanged(Path.Combine(project.Root, ".vscode", "mcp.json"), McpJson());
        WriteIfMissing(Path.Combine(project.Root, ".github", "copilot-instructions.md"), Instructions(project));
        WriteIfMissing(Path.Combine(project.Root, ".gitignore"), GitIgnore);
    }

    // Kurulu: <exe>/tools/mcp-bridge/mcp-bridge.dll; dev: <repo>/tools/mcp-bridge/bin/Debug/net10.0/mcp-bridge.dll (yoksa yazilmaz).
    public static string BridgeDll
    {
        get
        {
            string installed = Path.Combine(AppContext.BaseDirectory, "tools", "mcp-bridge", "mcp-bridge.dll");
            if (File.Exists(installed)) return installed;
            string dev = Path.Combine(App.RepoRoot, "tools", "mcp-bridge", "bin", "Debug", "net10.0", "mcp-bridge.dll");
            return File.Exists(dev) ? dev : null;
        }
    }

    static string McpJson()
    {
        string dll = BridgeDll;
        if (dll == null)
            return null; // kopru yok: dosya uretme (eski icerik dokunulmaz)
        string port = Environment.GetEnvironmentVariable("DIGITOY_RPC_PORT") is { Length: > 0 } p ? p : RpcHost.DefaultPort.ToString();
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"servers\": {");
        sb.AppendLine("    \"digitoy-editor\": {");
        sb.AppendLine("      \"type\": \"stdio\",");
        sb.AppendLine("      \"command\": \"dotnet\",");
        sb.AppendLine($"      \"args\": [\"{dll.Replace('\\', '/')}\"],");
        sb.AppendLine($"      \"env\": {{ \"DIGITOY_RPC_PORT\": \"{port}\" }}");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string Instructions(Project project) => $"""
# {project.Name} — AI çalışma kuralları (DigitoyEngine projesi)

Bu klasör bir DigitoyEditor oyun projesidir. Editör açıkken MCP sunucusu `digitoy-editor` (.vscode/mcp.json) üzerinden
sahne/kod/sim araçları kullanılır; `rpc_describe` tüm araçların şemasını verir.

## Döngü
- `view_device(name)` → `sim_run(frames, screenshot=true)` → görüntüyü incele → `input_tap/input_drag` → `render_screenshot`.
- Yeni component tipi kullanmadan ÖNCE `comp_schema`. Kod: `asset_write` + `code_build` (hatalar satır/kolonla döner).
- MOTOR API'Sİ: motor kaynağı bu projede YOKTUR ve proje dışına çıkma. `engine_api` kullan: `engine_api()` tip listesi
  (filter ile daralt), `engine_api(type="Component")` üyeler + override kancaları, `engine_api(type="Transform")`
  tween extension'ları dahil. Sık kullanılanlar: Component (Awake/Start/Update + OnPointerClick/Down/Up/DragStart/Drag(PointerEvent e),
  GetComponent<T>, gameObject, transform, enabled), PointerEvent (target, screenPos, screenTotal, dragged, Use()),
  LayoutBox (kutu/metin/stil; Text/Font/TextSize/Fill/... property'leri), Tween extension'ları (tr.TweenMove/Scale/Rotation,
  c.Tween("path", to, dur).Ease(Ease.OutBack)), Easing, MovieClip, Scene.Pointer. Lifecycle override'ları `protected override`.
- ZAMANLI GÖRSEL DEĞİŞİM (basınca küçül, fade, kay, pop): kendi `Update` sayacı/ease döngüsü YAZMA; `Tween*` kullan
  (sıfır alloc, destroy-güvenli, yarıda kesilince mevcut değerden devam). `TweenHandle` alanda tut, yeni tween'den önce
  `.Cancel()`, `OnDisable`'da iptal + taban değeri geri yaz. Elle döngü yalnız tek ease'e sığmayan parametrik eğriler
  (sönümlü shake vb.) için. Aynı kanala (örn. localScale) iki component yazmasın; feedback'i tek sahipte topla.
- KOORDİNAT: input ve screenshot (scale=1) MANTIKSAL nokta, sol-üst orijin, y aşağı; screenshot pikseli == tıklama koordinatı.
  Yön `ProjectSettings/PlayerSettings.asset` orientation'dan; sim ona sadık.

## Konvansiyonlar
- Sahne = kaynak (YAML); runtime durumu sahneye sızmaz, oyun durumu düz C# serviste yaşar.
- SAHNE/PREFAB YAML'INI ASLA DOĞRUDAN DÜZENLEME (asset_write dahil). Sahne değişikliği yalnız editör araçlarıyla:
  `prop_set` (value skaler olabilir: 72, "OutBack", "0 10 0", "#ff0000ff"), `transform_set`, `comp_add/remove`,
  `scene_addGo/removeGo/reparent`. Böylece undo'lu kalır, canlı sahneye anında yansır, kaydetme kararı kullanıcıda olur.
  Bir araç çalışmazsa dosyayı düzenleyerek atlatma; sorunu bildir.
- ÇİZİM SIRASI (2D): önce Renderer.SortingOrder (büyük üstte), eşitse hiyerarşi/liste sırası (sonraki üstte). "X'i Y'nin
  arkasına al" = `scene_reparent(id, parent, insertBefore=Y)` ile Y'nin önüne taşı (aynı parent'ta yalnız sıralama)
  ya da SortingOrder düşür. Pozisyon değiştirmez. "Altına/üstüne" belirsizse (ekranda mi, katmanda mı) kullanıcıya sor.
  Yapısal araçları (`scene_addGo/reparent/removeGo`) PARALEL çağırma: varış sırası belirsizdir, kardeş sırası karışır.
  Sıra önemliyse ardışık çağır ya da `scene_addGo(..., insertBefore=Y)` ile yeri açıkça ver.
- PLAY TAZELİĞİ: play sırasında yapılan doc değişiklikleri oyun sahnesine OTOMATİK yansımaz; `sim_run` bayat sahneyi
  algılayıp frame 0'dan yeniden başlatır (yanıtta `restarted`), `input_*` de öyle. Zamana bağlı şeyleri (MovieClip, Motion)
  kıyaslarken aynı frame sayısında screenshot al; `render_screenshot` yanıtında `stale: true` görürsen `sim_run` çağır.
- Her sahne işinin sonunda `<sahne>.intent.md` (sahnenin yanında, APPEND-ONLY): prompt + yapı + INVARIANTLAR + doğrulama.
  Sahneye dokunmadan ÖNCE oku. Sahne KAYDETME kararı kullanıcınındır (undo'lu bırak, söyle).
- Görsel doğrulamayı screenshot ile kendin yap; nihai his kullanıcıda. Tasarım iletişimi isimli sözlükle
  (motion preset, token) — ease/piksel konuşma.
- Kod yorumları Türkçe, UI metinleri oyunun diline göre. Scriptler `Assets/**/*.cs` (Editor/ hariç), lifecycle override'ları `protected override`.

""";

    const string GitIgnore = @"# DigitoyEditor: turetilmis cache + build ciktilari + kisisel ayarlar
Library/
Build/
UserSettings/
# makineye ozel, editor her acilista yeniden uretir
*.Game.csproj
.vscode/mcp.json
";

    static void WriteIfChanged(string path, string text)
    {
        if (text == null) return;
        if (File.Exists(path) && File.ReadAllText(path) == text) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    static void WriteIfMissing(string path, string text)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }
}
