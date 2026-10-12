# DigitoyEngine R&D — çalışma kuralları

Kendi 2D-ağırlıklı oyun motoru + editörü (C# .NET 9 + sokol/GLFW native shim, custom AOT). Kalıcı karar
günlüğü ve tuzaklar: [docs/engine-notes.md](../docs/engine-notes.md) (kronolojik, en yeni üstte) —
yeni işe başlamadan ilgili bölümü oku; oturum sonunda yeni kararları oraya ekle.

## ÇALIŞMA DİSİPLİNİ (KESİN KURAL)
- **Kod yazmadan ÖNCE her zaman plan sun ve onay bekle.** Küçük/büyük fark etmez: ne değişecek, hangi dosyalar, ne
  silinecek, risk ne — kısa madde listesi; kullanıcı "başla/onay" demeden tek satır kod yazma. Onaylanan planın
  dışına çıkan yeni bir karar gerekirse dur, tekrar sor. (Kullanıcı kararı, 2026-10-12.)

## Yapı
- `engine/managed` DigitoyEngine.dll (motor), `engine/native` sokol_shim.c (FFI), `editor/` DigitoyEditor.exe,
  `player/` GUI'siz runtime, `aotcompiler/` C AOT, `Projects/<oyun>/` Unity benzeri proje (Assets/, ProjectSettings/, Library/).
- Build: native `engine\native\build_editor.cmd`; managed `dotnet build editor`; çalıştır
  `editor\bin\Debug\net9.0\DigitoyEditor.exe [projeKökü]` (Start-Process ile; açıkken build DLL kilidi verir → Stop-Process).
- Açılış testleri (Lifecycle/Serialization/Layout/Pointer/...) editör start'ında koşar, stdout'a PASS/FAIL yazar.

## Mimari değişmezler
- Doc = kaynak (SceneDoc YAML), canlı sahne projeksiyon; runtime asla dosyaya sızmaz. Oyun durumu sahne dışında düz C#.
- Editör zengin (reflection serbest), release yalın (katalog codegen; Registry.g.cs reflection'sız).
- Hot path sıfır alloc. Statik tip cache yasak (ALC reload). Frame ortasında GPU destroy yasak (Resources.Tick'e akar).
- IMGUI: hot control varken kontrol sayısını değiştirme; GuiRenderer.Draw* yalnız Repaint'te.
- UI metinleri İngilizce, kod yorumları Türkçe. Yeni asset türü = importer kaydı; hiçbir şey tip-özel hardcode edilmez (şema sürer).
- Veri = `[Asset]` (veya `[CreateAssetMenu]`) `[Serializable]` sınıf → `.asset` dosyası, alanlarda REFERANS (GUID), pak'ta
  ObjectBinary (DOBJ). Düz `[Serializable]` gömülüdür. Ayarlar/level/tanımlar için ayrı "Settings" API'si YOK: component
  alanına referans verilir (docs/engine-notes.md "[Asset] VERİ SINIFI").

## AI çalışma döngüsü (RPC / MCP)
- Editör açıkken TCP `localhost:5157` satır-bazlı JSON RPC (`tools/rpc.ps1`; here-string değil `@('...','...')` dizisi
  gönder) veya MCP `digitoy-editor` (`.vscode/mcp.json`; önce `dotnet build tools/mcp-bridge`). `rpc.describe` = şema,
  `rpc.guide` = kavramlar/biçimler — MCP köprüsü bunu initialize'da `instructions` olarak otomatik gömer (prompt'ta
  "önce describe çağır" yazmak gerekmez); kural metninin tek sahibi editördeki `rpc.guide`.
- Tipik akış: `view.device(name)` → `sim.run(frames, screenshot=true)` → görüntüyü incele → `input.tap/drag` →
  `render.screenshot`. Yeni component tipi için önce `comp.schema`. Kod yazımı `asset.write` + `code.build`.
  Asset bilgisi (yol/guid/tip, görsel width/height, atlas üyeliği) için `asset.list(filter,type)` / `asset.info(path)` —
  boyut öğrenmek için kod yazıp çalıştırma.
- ANİMASYON SINIRI: hedefi doc'ta yazılabilen koreografi (sahne intro, giriş/çıkış, idle loop) = **MovieClip**
  (doc'ta, scrub/undo'lu, frame 0 authored); Tween yalnız runtime'da hesaplanan tepkisel hareket. Tween'in Start'ta
  authored değeri ezmesi yasak. MovieClip tarifi: `comp.add MovieClip` → `anim.props(type)` yol listesi (Transform:
  Position/Rotation/Scale/Active) → `prop.set` iç içe yol: `Tracks[0].Target="goId:Transform:0"`, `Tracks[0].Path="Scale"`,
  `Tracks[0].Keys[0].Value="0 0 1 0"`, `Tracks[0].Keys[0].Ease="OutBack"`, `Tracks[0].Keys[1].Frame=30`,
  `Tracks[0].Keys[1].Value="1 1 1 0"` (index == eleman sayısı → sona ekle; value null → sil). `comp.schema` iç içe
  alanları ve ref biçimlerini (`format`) verir.
- KOORDİNAT SÖZLEŞMESİ: input ve screenshot (scale=1) MANTIKSAL nokta, sol-üst orijin, y aşağı;
  screenshot pikseli == tıklama koordinatı. Yön `PlayerSettings.orientation`'dan; sim/screenshot ona sadık.
- Her sahne işinin sonunda `<sahne>.intent.md` (sahnenin yanında, APPEND-ONLY): prompt + yapı + INVARIANTLAR +
  doğrulama. Sahneye dokunmadan ÖNCE oku. Sahne KAYDETME kararı kullanıcınındır (undo'lu bırak, söyle).
- Sahne/prefab YAML'ını ASLA doğrudan düzenleme; TOPLU iş için `scene.apply(objects)` (bildirimsel ağaç: GO + components +
  props + children, `$Ad` ile aynı çağrıdaki GO'ya referans, `id` verilirse mevcut GO güncellenir) ve `edit.batch(ops)`
  (`{cmd,args,as}` listesi, `$as` referansı) — tek istek, tek undo, atomik (hata → hiçbiri uygulanmaz). Tek tek `prop.set`/
  `transform.set`/`comp.add`/`scene.addGo` yalnız küçük rötuş için. Hepsi undo'lu, canlı patch. `prop.set value` skaler
  alır (72, "OutBack", "0 10 0"). Araç çalışmazsa atlatma, bildir.
- Çizim sırası: SortingOrder, eşitse liste sırası (sonraki üstte); sıralama `scene.reparent(..., insertBefore)`. Play'de doc
  değişirse oyun sahnesi BAYAT olur; `sim.run`/`input.*` bunu algılayıp yeniden başlatır (`restarted`), screenshot `stale` döner.
- Görsel doğrulama: screenshot ile kendin kontrol et; nihai his/zevk kullanıcıda. Kullanıcı "görsel bakacağım" dediyse
  screenshot turu yapma. Tasarım iletişimi isimli sözlükle (motion preset, token) — ease/piksel konuşma.

## Tuzaklar (sık yaşananlar)
- Enum parse case-sensitive (yaml `Landscape`). Türetilmiş durumu olan component (LayoutBox gibi) tazelemeyi
  `OnValidate`'te yapar; Tween/MovieClip alan yazımı frame sonunda otomatik `OnAnimatedPropertiesApplied → OnValidate`
  çağırır — setter'a yan etki koyma, animasyon için özel kaçış yazma.
- Oyun component'lerinin Awake/OnEnable/Start'ı EDİT sahnesinde koşmaz (Unity gibi); editör doc'u gösterir. Edit
  modunda da çalışması gereken görsel/yerleşim component'i `[ExecuteInEditMode]` alır ve `scene.Simulating`'e bakar.
- `dotnet run --no-build` sahte EXIT 0 verebilir; exe'yi doğrudan çalıştır. Input simülasyon araçları DPI-aware olmalı.
- sokol enum'larını shim'den sızdırma (sürümde kayar); boolean export et. Frozen frame debugger'da RT resize/destroy yasak.
