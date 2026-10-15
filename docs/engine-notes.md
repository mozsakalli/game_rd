# DigitoyEngine R&D (game_rd)

## VEKTÖR ASSET (SVG) — FAZ 1: DVEC KODEK + STANDALONE SPRITE (2026-10-16, editör/pak/player uçtan uca; badge.svg 830 B kaynak → 392 B pak girişi)
- AMAÇ yalnız **asset boyutu**: çözünürlük import'ta sabit (`scale`), DPI ölçeği YOK, runtime vektör çizimi YOK. SVG import'ta
  canvas benzeri **komut akışına** (M/L/C/Z + boya) derlenir, runtime yükleme anında bir kez rasterize eder (ikon: ms düzeyi, worker'da).
- BİÇİM "DVEC" ([de_vector.h](../engine/native/de_vector.h) başındaki şema): header (ver, flags, shapeCount, u16 w/h) + shape (fill/stroke
  boya: solid | linear/radial gradient stop'lu + ters xform; fillRule, join/cap, dash, opacity, paint order) + path'ler. Koordinat
  **i16 sabit nokta 1/16 px** (hedef uzayda; ±2048 px'e sığmazsa flags bit0 → f32). Ölçek import'ta koordinatlara İŞLENİR: stroke
  genişliği/dash ×s, gradient ters xform'un [0..3] ÷s ([4..5] aynı). nanosvg'nin cubic'e açtığı doğru parçaları `L`'ye indirgenir.
- NATIVE: runtime rasterizer `de_vector.h` (nanosvgrast, stb tarzı tek başlık, implementasyon **de_fs.c içinde**) → player/Android/iOS
  kaynak listeleri DEĞİŞMEDİ. `de_fs` decode job'u blob magic'ine bakar: DVEC → `de_vector_rasterize` (RGBA satır-0-altta), değilse stbi.
  Editör-only [de_svg_compile.c](../engine/native/de_svg_compile.c) (nanosvg parser: XML/CSS/transform/`<use>` çözümü) `de_svg_compile(svg,
  scale, maxSize)`; build_editor.cmd/.sh + EditorPublisher listelerine eklendi. `de_vector_draw(dst, x, y)` Faz 2 (atlas sayfası) için hazır.
- MANAGED: [VectorImporter](../editor/VectorImporter.cs) `.svg` → `main` = DVEC (`Standalone`; atlas yutarsa pak'a girmez), ayar
  `VectorImportSettings{scale=1, maxSize=4096}` (.meta `importer:` bloğu). `AssetDatabase._importers[".svg"]=Sprite`; LoadTexture/
  ReloadTexture/pak builder/player **sıfır değişiklik** (PNG yolu). AssetWatcher `.svg` png dalında (reimport + yerinde patch + atlasDirty).
  [ImageHeader](../editor/ImageHeader.cs) DVEC başlığından w/h okur; `asset.info` svg için artifact'e bakar (kaynak svg'de boyut yok).
- nanosvg YAMASI ([nanosvg.h](../engine/native/nanosvg.h) `nsvg__convertGradCoord`): `gradientUnits=objectBoundingBox`'ta birimsiz
  `0..1` değerleri spec'e göre bbox ORANI; özgün kod kullanıcı birimi sayıp gradient'i 1 px'e sıkıştırıyordu (düz renk). Upstream
  güncellenirse yamayı taşı.
- SINIRLAR: nanosvg `<text>/<filter>/<mask>/<clipPath>` desteklemez (sessiz atlanır) → tasarım SVG'leri outline'lı olmalı. Radial
  gradient focal (fx/fy) yok sayılır, spread modları yok. Import stamp'ine native derleyici GİRMEZ: de_svg_compile değişince
  svg'ler elle reimport (dosyaya dokun / importer Version artır).
- TUZAK: `dotnet build editor` native DLL'i `engine/native/build`'den bin'e KOPYALAR; native'i yeniden derleyince managed build'i de
  çalıştırmadan editör eski DLL'i yükler. `player.build` bellekteki PlayerSettings'i kullanır; dosyadan değişiklik için editörü yeniden aç.
- FAZ 2 (yapılmadı): atlas üyeliği — svg bölgeleri aynı sayfada `width×height` yer tutar, sayfa PNG'sinde şeffaf delik, `SpriteTable.Pages[i]`
  vektör listesi, runtime `ComposePage` job (PNG decode + `de_vector_draw`). Faz 3: Inspector önizleme.
- Test varlıkları: `Projects/Sandbox/Assets/Vector/badge.svg` (+ `VectorTest.scene`, 2 SpriteRenderer) — Faz 2 atlas testi için bırakıldı.

## BOXSTYLE v3 — İSİMLİ DURUMLAR (2026-10-15, layout 112 PASS; Sandbox StyledButton + BlueButton.asset göç etti)
- `enum BoxState` ve sabit `hover/pressed/disabled/selected` alanları SİLİNDİ → `BoxStyle { normal, List<BoxStyleState> variants,
  transition, transitionEase }`, `BoxStyleState.key` (serbest metin). Sözleşmeli adlar: `BoxStyle.PressedKey/DisabledKey/HoverKey/
  SelectedKey` + hazır hash'ler `BoxStyle.Pressed/Disabled/Hover/Selected`. `BoxStyle.Hash(key)` = `ClipHash.Of` (FNV-1a).
- RUNTIME: string yok. `StateSet` (4 int + Count; Add/Remove/Contains/Clear, sıkıştırmalı) kutunun aktif durumları. `BoxStyle.
  Resolve(in StateSet, out state)`: normal + eşleşen varyantlar **LİSTE SIRASIYLA** maskeyle bindirilir (sonraki ezer → pressed+
  selected bileşimi stil yazarının sırasıyla tanımlanır). Hash indeksi `_hashes` `ObjectVersion`'da/eleman sayısı değişince bir kez
  kurulur (`EnsureIndex`); alloc yalnız kurulumda. 4 eş zamanlı durum üst sınır (5. sessiz red).
- LAYOUTBOX API: `SetState(int|string)` (tek duruma indirger; 0/"" = Normal), `AddState/RemoveState(int|string)`, `ClearStates`,
  `HasState`, `StateCount`, `States`. `ApplyStates` eski SetState gövdesi (CaptureLook → transition). Edit modunda stilde olmayan
  key → `AssetDatabase.LogWarning` "[style] 'go': state 'x' is not defined" (release'te sıfır maliyet: EditMode kontrolü).
- GÖÇ: FormerlySerializedAs yapı farkı (struct → liste) taşıyamaz; tek asset (BlueButton.asset) elle `variants: [{key: pressed…},
  {key: disabled…}]` biçimine çevrildi. Eski `pressed:`/`disabled:` alanları içeren dosya yüklenirse SESSİZCE yok sayılır (bilinmeyen
  alan) → varyantsız stil. Başka projede eski BoxStyle varsa elle çevir.
- Inspector: `variants` jenerik liste (key metin + [Flags] overrides combosu); tüketicide (Button) key combosu YAPILMADI (sonra).
  MovieClip State track YAPILMADI.
- TESTLER (LayoutTests.StyleNamedStates): string/hash API, liste sırası bindirme (pressed Fill'i ezer, Radius selected'tan kalır),
  RemoveState yeniden çözüm, bilinmeyen key → Normal, StateSet kapasite/sıkıştırma, Add/Remove/Resolve 0 alloc, YAML round-trip.

## TEMA KATMANI — UYGULANDI (2026-10-15, theme 32 PASS + prefab 34 PASS, RPC canlı doğrulandı; karar bölümü aşağıda)
- BİÇİM ([SceneDoc.Theme.cs](../engine/managed/Serialization/SceneDoc.Theme.cs)): dosyada `themes: { xmas: { overrides:[{node,comp,
  prop,value}], removed:"7 9", added:[GoDoc kayıtları] } }` — prefab instance override ŞEMASI/KODU birebir (`ApplyOverrideItems(ovs,
  map)`, map=null → kimlik; `DiffNodeFields` + `DiffComponents` ortak). `node` = doc'un kendi id'si (remap yok; instance içi node'lar
  kalıcı prefabIds ile adreslenir). `added` prefab instance kaydı olabilir. Pasif katmanlar ham DocNode olarak `doc.Themes`'te taşınır,
  aynen geri yazılır. Node override'ına `layer` ve `parent` (yalnız tema üretir) eklendi; `layer` prefab diff'ine de girdi (önce
  sessiz kayboluyordu).
- TEK BOĞAZ `doc.Prepare(catalog, assets, theme)` = `ExpandPrefabs → ApplyTheme → ExpandPrefabs` (added instance'lar). Tüm yükleyiciler:
  `EditorScene.LoadFromText`, `AssetDatabase.MakePrefab` (→ Avatar'ın xmas katmanı Table/leaderboard/sahnede OTOMATİK), `BakeScene`,
  `SceneLoader`, `GameHost` loose. `SceneDoc.ActiveTheme` (static, DE_EDITOR) = yükleyicilerin uygulayacağı tema; `AppliedTheme` bellekte.
  RUNTIME TEMA BİLMEZ: release YAML okumaz, SceneBinary/player/AOT sıfır değişiklik.
- AYARLAR: `PlayerSettings.themes` (serbest ad listesi, taban "default" yazılmaz) + `PlayerSettings.activeTheme` (BUILD teması);
  `EditorSettings.workingTheme` (kullanıcı). Pak: `AssetPackBuilder.Build` → `ThemeOverride ?? activeTheme`; build boyunca
  `ActiveTheme` o tema + prefab cache iki kez düşürülür (çalışılan temayla dolu olabilir), sonunda editörünkine döner. Yalnız çözülmüş
  doc pişer → diğer temaların asset'leri pak'a girmez (test: xmas sprite guid'i taban refs'te yok).
- EDİTÖR (`EditorScene`): tema modunda `Base` (taban projeksiyonu) + `Doc` (taban+katman), ikisi AYNI metinden Prepare (id uzayı ortak).
  `Collapse()` = `Base.Themes[aktif] = Doc.DiffTheme(Base)` + `Base.ToYaml` → Save/reexpand/Apply/stage boğazları DEĞİŞMEDEN çalışır.
  Taban modunda `Base == Doc`. ÖNEMLİ: Base de aynı (temalı) prefab cache'iyle açılır — prefab'ın kendi tema katkısı bu dosyanın
  katmanı değildir; aksi halde Avatar'ın "snow"u sahne katmanına kopyalanırdı (test: diff'te Label yok).
  Toolbar **Working theme** combosu (dil combosunun solunda; liste boşsa gizli): `App.SetWorkingTheme` = `WithPrefabEdit(ActiveTheme=x)`
  (collapse eski temayla → cache düşer → reexpand) + `ResetHistoryKeepDirty` + Selection.Clear. Kayıt istemez (dil gibi).
  Tema modunda `MoveGameObject` (kardeş sırası) tabandaki GO için REDDEDİLİR (temsili yok); `Reparent` serbest (`parent` override).
  Inspector: alan/TRS Base'den farklıysa TURUNCU ikinci şerit (prefab mavisi x=4, tema x=8), GO başında "Theme: xmas (modified|
  theme-only object)" + **Revert to base** (`RevertNodeToBase`: undo'lu normal op'lar). `BaseNodeOf/BaseCompOf` yardımcıları.
- RPC [RpcTheme.cs](../editor/RpcTheme.cs): `theme.list/set/add/remove(purge)/setBuild/check`; `asset.buildPak(theme)`,
  `player.build(target, theme)` ("default" = taban). `rpc.guide` "themes" bölümü (akış + "katmanı en iç prefab'a koy").
- TESTLER [editor/ThemeTests.cs](../editor/ThemeTests.cs): parse/yaz round-trip (pasif katman aynen), ApplyTheme (override name/layer,
  removed alt ağaç, added düz GO + added prefab instance'ın expand + kendi override + Avatar katmanı), bilinmeyen tema = taban,
  nested (Avatar xmas → Table → sahne), bake bayt farkı + asset ref ayrımı, DiffTheme ⇄ Apply simetrisi (prefab tema katkısı sahne
  katmanına yazılmaz; aynı doc diff'i boş), ThemeOrphans, EditorScene tema modu (SetProp/AddGo/RemoveGo tema-only/Move reddi/Reparent
  → Collapse: taban dokunulmadı, katman = eski+yeni, dirty korunur, UndoCount 0), default'a dönüş + pasif katman bayt-eş taşınır.
- **RİSKLER / SONRA DÜZELTİLECEK (kullanıcı: "not al, düzelteceğiz")**:
  1. Tema geçişinde undo yığını temizlenir (katmanlar arası GO kümesi farklı). Alternatif: undo op'ları tema etiketli tutmak.
  2. Tema modunda prefab Overrides paneli (`BuildOverrides` Doc üstünde) temalı alanları da "prefab override" sayar; Apply düğmesi
     tema değerini prefab'a yazar. Doğrusu: tema modunda Apply'ı gizlemek ya da Base'e göre filtrelemek.
  3. `added` GO'lar ApplyTheme'de listenin SONUNA eklenir → çizim sırası (eşit SortingOrder'da liste sırası) temada kardeşlerin sonunda;
     `insertBefore` kaydı yok. Reorder reddi de bundan. Çözüm: added kaydına `before: id`.
  4. Tema modunda instance içindeki prefab çocuğunu silmek `root.PrefabRemoved`'ı Doc'ta işaretler ama katmana yalnız `removed` yazılır;
     projeksiyon doğru, instance kaydı tabanda değişmez (istenen) — ancak Overrides paneli tutarsız gösterebilir (madde 2 ile aynı kök).
  5. `theme.check` tema modunda aktif katmanı son Collapse'tan okur (Doc'taki taze diff değil).
  6. Yetim override (tabanda silinen node) sessizce atlanır (prefab missing-node kuralı); `theme.check` ile görünür, otomatik temizlik yok.
  7. Prefab stage'de tema modu: `SingleRoot` + tema `added` kök altında — test edilmedi (sahne yolu test edildi; aynı kod).
  8. `PlayerSettingsWindow` `themes` listesini jenerik çizer; ad doğrulama (boş/"default") yalnız RPC `theme.add`'de.
  9. Tema katmanı instance × tema matrisi yok (sahnedeki instance'a tema-özel dokunuş = sahnenin katmanı, outermost kuralı) — bilinçli.

## NESTED PREFAB (bileşim) — TAMAMLANDI (2026-10-15, prefab 34 PASS, diğer suite'ler değişmedi)
- İHTİYAÇ: Avatar.prefab (foto + çerçeve) masa/leaderboard/sahne — her yerde; tema (xmas karlı çerçeve) tek yerden.
  "Sahnede yan yana türevler" (variant) DEĞİL; bileşim.
- BULGU: prefab dosyası içinde instance kaydı zaten yaşıyordu (stage = sahne; Save collapse'lı). Kırık tek halka TÜKETİMDİ:
  `MakePrefab` doc'u yalnız Parse ediyordu → Table başka yerde açılırken içindeki Avatar kaydı component'siz boş GO
  (pak, `LoadPrefab().Instantiate()`, sahne — hepsi). İkincil: `InvalidatePrefab(rel)` yalnız o dosya (nested tüketici bayat),
  Apply cache doc'unu `Save()`=collapse'siz yazıyordu (nested içeren prefab'a Apply → iç instance düzleşir), elle yayılım yalnız
  aynı-guid instance'lara, CreatePrefabAsset instance içeren alt ağacı reddediyordu, döngü koruması/prefab testi yoktu.
- MODEL: **cache'teki prefab Doc HEP EXPANDED** ([AssetDatabase.MakePrefab](../engine/managed/AssetDatabase.cs), DE_EDITOR):
  Parse → iç instance kökleri için `LoadPrefab` (özyineleme) → `ExpandPrefabs`. Tüketen sahne/prefab düz node kopyalar
  (`ExpandInstance` DEĞİŞMEDİ) → **outermost override kuralı**: sahnedeki Table instance'ında avatara dokunuş Table instance
  kaydına yazılır; Table stage'inde Avatar kendi instance kaydıyla görünür (Unity default'u). Mevcut diff yetenekleri
  (prop/enabled/addComp/removeComp/name/active/TRS/removed) iç içe için BEDAVA geçerli — yeni diff kodu yok.
- DÖNGÜ: `_prefabLoading` seti; yükleme sırasında aynı key → `LogWarning("[prefab] dongu: A → B → A")` + null → iç kayıt
  missing-prefab gibi delta kalır (çökmez/kaybolmaz). `Prefab.Deps` (geçişli yol seti) expand'de toplanır;
  `PrefabDependsOn(a,b)`; `EditorScene.InstantiatePrefab` stage'de kendini/kendini içereni REDDEDER (dosyaya hiç yazılamaz).
- INVALIDATE: `InvalidateAllPrefabs()` — bağımlılık grafı izlemek yerine tüm cache düşer (YAML küçük, bayat kopya riski sıfır).
  `App.ReexpandAllPrefabs` → `WithPrefabEdit(null)`.
- APPLY YOLU yeniden kuruldu (`EditorScene.PrefabEditScope`, App stash'li sahneyi de kapsar — `App.WithPrefabEdit`):
  (a) sahne(ler) ESKİ cache ile collapse, (b) prefab doc mutasyon + `Save(path, catalog, assets)` COLLAPSE'LI, (c) tüm cache
  düşer, (d) delta'dan reexpand. Yayılım doğrudan VE nested tüketicilere aynı yoldan; uygulanan instance'ın override'ı yeni
  prefab değerine eşit çıktığından diff'ten kendiliğinden düşer (sahte override yok). Elle yayılım kodu SİLİNDİ. Nested
  node'dan Apply = en dış prefab'a nested override satırı (Table.prefab'ta Avatar kaydına). Apply hâlâ undo'suz (eskisi gibi).
- `SceneDoc.Save(path, catalog, assets)` eklendi; cache'ten gelen (expanded) prefab doc'u kaydeden HER yol bunu kullanmalı.
- `CreatePrefabAsset`: alt ağaç instance İÇEREBİLİR (kök instance'a bağlıysa yine ret — variant değil). İç işaretler
  (PrefabGuid/Ids/Removed/LocalId/RootId) yerel uzaya taşınır, dosya collapse'lı yazılır (iç kayıt delta + diff), Layer da
  kopyalanır. `ConnectPrefabOp.Entry` önceki işaretleri saklar → undo birebir geri koyar; connect sonrası iç kökler düz çocuk.
- ID KARARLILIĞI: Table.prefab'taki iç kaydın `prefabIds` kalıcı (Avatar-local → Table-local), sahne haritası Table-local →
  scene. Bilinen sınır: Avatar'a yeni çocuk eklenip Table henüz kaydedilmediyse Table-local id hesaplanır (deterministik,
  kalıcı değil); Table bir kez kaydedilince kalıcılaşır (Unity'nin de bedeli).
- PAK/RUNTIME: sıfır değişiklik (BakeScene zaten expand ediyor; artık iç seviye de dolu). Release baked yolu dokunulmadı.
  Katalog yoksa (`assets.Catalog` null) expand ref remap'siz çalışır; GameHost/editör katalogu önce atar.
- TESTLER [editor/PrefabTests.cs](../editor/PrefabTests.cs) (geçici klasör + gerçek AssetDatabase): iki seviye expand + ref remap
  (Deck→avatar-1 Frame), sıfır sahte override + round-trip bayt-eş, nested (Table) + sahne override katmanları, removed iki
  seviyede, addComp/removeComp nested node'da, bake/spawn paritesi + ref çözümü, yayılım (Avatar değişti → nested override ve
  sahne override'ı duruyor, yeni çocuk iki instance'a), döngü uyarısı + Deps/PrefabDependsOn, Apply (Table.prefab nested satır,
  collapse'lı, override düştü, ikinci Table instance'ına yayıldı), CreatePrefabAsset instance içeren alt ağaç + undo/redo +
  expanded doğrulama, stage döngü reddi.
- TEMA için zemin hazır: Avatar.prefab'ın xmas katmanı (çerçeve sprite) `LoadPrefab`'da doc düzeyinde çözülünce masa/leaderboard/
  sahne otomatik karlı olur (aşağıdaki TEMA kararı). Sıradaki iş tema katmanı.

## TEMA (bayram/sezon skin) — KARAR (2026-10-15, tartışma sonucu; uygulama üstteki bölümde)
- SORU: Noel/Kurban gibi temalar için mevcut ekranları her seferinde sahneden mi düzenleyeceğiz? HAYIR. Sahne kopyası
  (drift, loc/prefab bağı çoğalması) ve kodda `if (xmas)` (asset adı koda sızar) REDDEDİLDİ.
- REDDEDİLEN 2. YOL — asset remap (`[Asset] Theme {Source→Replacement}` + AssetDatabase çözümünde dolaylama): kesişen kaygıyı
  iyi çözer ama tüm asset pipeline'ını keser, karışık (kullanıcı). Yapısal fark (ek GO) yapamaz.
- REDDEDİLEN 3. YOL — Unity prefab variant (ayrı dosya `Menu_Xmas.prefab`): "ne farklı" sorusunu çözer, "hangisi etkin"
  sorusunu çözmez (referans veren sahne değişmek zorunda); yalnız prefab, sahne yok; N tema × M dosya patlaması.
- KARAR: TEMA = DOC'TA İSİMLİ OVERRIDE KATMANI, loc gibi first-class, build-time projeksiyon.
  * Tema listesi serbest metin (loc `Languages[]` gibi): `ProjectSettings.themes: [default, xmas, kurban, ...]`; ilk = taban.
  * Doc biçimi (prefab VE sahne — her SceneDoc): GO'nun yanında
    `themes: { xmas: { overrides:[{node,comp,prop,value}], removed:"7", added:[GoDoc...] } }` — mevcut prefab instance
    override şemasıyla BİREBİR; uygulama/diff kodu `ExpandInstance`/`DiffComponents` yolunun yeniden kullanımı
    (`ApplyThemeLayer(doc, theme)`). Taban doc değişmez; katman yalnız delta.
  * EDİTÖR: toolbar "Working theme" combosu (Working language ile aynı yer ve anlam; ayrı "preview theme" YOK). `default`
    seçiliyken normal edit; başka tema seçiliyken HER edit tema katmanına diff'lenir (taban'a yazılmaz): prop → overrides,
    GO ekle → added, GO sil → removed. Inspector'da tema rengi şerit + "Revert to base". Prefab stage'de aynı. Play da
    working theme ile koşar.
  * RUNTIME'DA TEMA YOK. Motor (DigitoyEngine.dll) temadan habersiz; `Theme.Current`/Version/SceneBinary alanı/player
    seçimi YOK. Proje HER ZAMAN TEK AKTİF TEMAYLA derlenir: `PlayerSettings.activeTheme` (ya da `player_build(theme)`);
    pak = `ApplyTheme(doc, aktif) → BakeScene` düz doc. Diğer katmanlar ve asset'leri pak'a GİRMEZ (bağımlılık toplayıcı
    yalnız çözülmüş doc'u gezer). Amaç sürdürülebilirlik, dinamik indirme/DLC DEĞİL (kullanıcı). Canlı tema geçişi
    gereksinim değil; editörde combo değişimi = `ReexpandFrom` (reload).
  * Sınırlar (v1): instance × tema matrisi YOK (tema katmanı prefab'ın/sahnenin kendisinde; instance'a tema-özel dokunuş
    gerekiyorsa sahnenin kendi katmanı override eder). Tema tek eksen, nesting/kombinasyon yok. BoxStyle `[Asset]` olduğu
    için stilin kendisi temalanmaz → doc'ta `style` ref'i override edilir; ortak buton tekrarı nested prefab borcuna bağlı.
  * Sonraki doğrulamalar: `theme.check` (taban'da silinen GO'ya yetim override), `theme.assets(name)` (tema asset raporu),
    `theme.remove(name)`. Yetim override davranışı = prefab'ın mevcut "missing node" kuralı.
- UNITY VARIANT İLE FARK: variant = ayrı dosya/GUID, referans veren seçer, yalnız prefab, yapısal türetme zinciri
  (yeniden kullanım: aynı sahnede yan yana türevler). Tema = aynı dosyada katman, proje-geneli tek anahtar, her doc,
  kesişen kaygı, bir anda TEK etkin (projeksiyon). En yakın akraba Android `res/values-xx/` qualifier / Xcode appearance.
  "Sahnede yan yana türevler" (RedButton/BlueButton) ihtiyacı temayla ÇÖZÜLMEZ → nested prefab / variant ayrı tartışma
  (bileşim YAPILDI — üstteki NESTED PREFAB bölümü; variant/türetme hâlâ yok, bilinçli).

## RPC: render.describe — EKRANIN METIN OZETI (2026-10-15)
- `render.describe(which=play, root, types, minSize=2, clickable, pixels=0)` ([editor/RpcDescribe.cs](../editor/RpcDescribe.cs)):
  gorunur renderer'lar cizim sirasinda; her biri `path, type, rect "x y w h" (mantiksal, ekrana kirpilmis; partial=true tasma),
  order, alpha(<1), text, sprite, fill (duz #rrggbbaa ya da Linear:#a→#b), tint, click`. `rect` merkezi dogrudan `input.tap`
  (dogrulandi: FitPerspective kamerada GUEST/TERMS merkezine tap → ayni target). `pixels=N` → N sutun x orana gore satir
  ortalama renk izgarasi (`GameOutput.PixelGrid`, mevcut ReadImage). `sim.run(describe=true)` / `anim.goto(describe=true)`.
- Guide: ONCE describe (ucuz, kesin), screenshot yalniz gorsel ton/suphe icin.
- MOTORA SIFIR DOKUNUS (kullanici sarti: runtime'a sizmasin): projeksiyon editorde, kameranin public `ScreenPointToRay(0,0)`
  isinindan turetilir (yon (0,0,1) = ortho → dunya xy; aksi halde Origin = goz, goz→p dogrusunun z=0 kesiti) + `GetWorldRect`
  ile dogrusal haritalama. Renderer bbox = `GetLocalSelectionBounds` 4 kose × world matrix. Kamera secimi, alpha/enabled
  filtresi ve `HasHandlerInChain` PointerInput.Pick'in editor kopyasi (InternalsVisibleTo). Klip (Hidden/Scroll) ile kirpilan
  cocuk rect'te tam gorunur — v2 adayi (`clipped`). ParticleSystem bbox kaba.

## RPC: KUCUK/JPEG SCREENSHOT + scene.tree (2026-10-15; Unity MCP paketinden geri port)
- `render.screenshot(path, scale, maxSize=1024, format=jpg, quality=80, region)`: VARSAYILAN kucultulmus jpg (AI gorsel token
  maliyeti piksel sayisiyla olceklenir). Sonucta `toScreen` (= region.w / w) ve `region`: TIKLAMA = region.x + px*toScreen.
  Eski birebir davranis: `scale=1, maxSize=0, format=png`. Encoder: jpg = native `de_image_encode` (stb, zaten vardi),
  png = `PngWriter`. `GameOutput.Screenshot(path, ShotRequest)` bolgeyi mantiksal koordinatta kirpar (RT fiziksel → px/vw).
  `sim.run(maxSize, format)` ve `anim.goto` ayni yoldan gecer; MCP koprusu png|jpg + `frames[]` listesini image content yapar.
  `ui.screenshot` (editor penceresi) PNG kaldi — bilerek, UI otomasyonunda piksel==koordinat.
- `scene.tree(root, depth, comps)`: satir basina `{id} {girinti}{ad} [inactive] {prefab} (Tipler)`, depth kesince `+N` gizli
  eleman; `root` id ya da 'A/B' yolu. `scene.dump(go, props)` alt agac + prop'suz dump (yalniz TAM dump diff baseline'ini
  sifirlar); `scene.dumpLive(which, go)` alt agac. Guide adim 1 artik scene.tree. Doc'ta parent→children indeksi her cagrida
  tek gecis (`ChildIndex`) — doc'a kalici indeks eklenmedi.
- Kopru dll'i VS Code MCP sureci acikken kopyalanamaz (MSB3027): MCP sunucusunu yeniden baslat, sonra build.

## OTURUM KAPANIS — WEB-KAFASI UI PAKETI EKSIKLERI (2026-10-15; cizim + flex + scroll + boxstyle v1/v2 bitti, layout 102 PASS)
- DURUM: editor KAPALI. Diskte KAYDEDILMEMIS test sahneleri `Sandbox/Assets/Scenes/{DrawTest,FlexTest,ScrollTest,StyleTest}.scene`
  (bos dosya + .meta; icerikleri %TEMP%\{drawtest,flextest,scrolltest,styletest}_apply.json — scene.apply ile yeniden kurulur).
  Kalici ornekler: `Styles/BlueButton.asset` (maskeli), `Scripts/StyledButton.cs`. `Localization.asset`'e scene.apply'in ekledigi
  `scroll_test.item*` anahtarlari duruyor (istenmiyorsa geri al). Git'te commit YOK — tum degisiklikler working tree'de.
- EKSIK / SONRAKI ADIMLAR (oncelik sirasi onerisi):
  1. Press-shift (basinca 2px kayma): pozisyon layout'un surdugu alan -> BoxLook'a `offset` + Encode'da quad'lara render ofseti
     (transform'a degil); hit-test ofseti de gormeli.
  2. Hover: Pointer'a enter/exit (`OnPointerEnter/Exit`), bosta Move'da hit-test maliyeti — masaustu/editor icin opt-in bayrak
     (`PointerInput.TrackHover`), mobilde kapali. BoxState.Hover slotu hazir.
  3. Kutu-olmayan cocuk renderer (SpriteRenderer/TextSprite) Hidden/Scroll altinda kirpilmiyor ve culling'siz: klip penceresini
     `Scene.WalkRenderers`'ta alpha gibi indir, `Renderer.Encode` basinda rect kiyasi.
  4. Scrollbar: opsiyonel cocuk kutu (`IgnoreLayout`) + ofset orani; ScrollView'e `scrollbar` CompRef. Yatay+dikey birlikte test
     edilmedi (kod iki ekseni bagimsiz destekliyor).
  5. Grow + maxWidth'te artik diger grow'lulara dagitilmaz (tek tur) — shrink'teki 2. tur deseni kopyalanabilir.
  6. Capraz gradient'te ara durak yok (eksen-hizali quad'la bolunemez) — istenirse shader'da (USER yok; mod-paketleme/uniform kurali).
  7. Radial gradient 2 durak; keyfi aci gradient yok; ClipRound kapsam disi (mask benzeri genel efekt yolu ile birlikte).
  8. BoxStyle: varyant `Sprite` grubu test edilmedi (kod var); "Copy from Normal" dugmesi yok (maske mirasi yeterli); stil varken
     kutunun surulen alanlari Inspector'da salt-okunur DEGIL (Unity gibi; jenerik "driven fields" mekanizmasi yok).
  9. Transition sirasinda MovieClip/Tween ayni kutunun BoxLook alanini yazarsa son yazan kazanir; Transform katmani da ayni.
  10. Klip asset'i (`@keyframes` karsiligi: MovieClip'i [Asset] olarak referansla uygulamak; hedef yolu goreli cozum).
  11. Editor/RPC: native combo popup'i (modal) acikken RPC yanit vermiyor -> popup acikken de istek islensin ya da `ui.key esc` oncelikli.
      `edit.undo` sim restart'ini da geri alabiliyor (ust uste apply'da Root birikti). `rpc.ps1`'e trailing-newline korumasi
      (`.Trim()`) eklenebilir.
  12. Serializer `byte` desteklemiyor (int/float) — Gradient.stops int kaldi; istenirse Kind.Int'e byte/short eslemesi.
  13. AOT/player tarafinda yeni tipler (BoxStyle, ScrollView, BoxLook struct icinde Sprite ref) pak/DOBJ ile HIC test edilmedi —
      `AssetPackBuilder` + player calistirmasi bir sonraki oturumda ilk is olmali.
  14. BOXSTYLE v3 — YAPILDI 2026-10-15 (ustteki bolum): sabit `BoxState` enum yerine ISIMLI durumlar — `BoxStyleState.key` (string) +
      `List<BoxStyleState> variants`; oyun durumlari ("turn-active", "selected-wrong", "vip") ve COKLU aktif durum (pressed+selected,
      liste sirasiyla bindirme). String yalniz authoring'de: yuklemede/ObjectVersion'da key'ler bir kez hash'lenir (ClipHash deseni),
      runtime `SetState/AddState/RemoveState(int hash)` + hash hesaplayan string overload (alloc yok), kutuda kucuk sabit hash dizisi.
      Resolve yine durum degisiminde bir kez. Button sozlesmeli adlar ("pressed","disabled"). Yazim hatasi: edit modunda log uyarisi +
      Inspector'da atanan stilin key'leri combo. MovieClip Trigger/State track'inden tetiklenebilir.
- OLCUM OZETI (regresyon referansi): EncodeBench 300 kutu 2-stop ~3.2 ms / 4-stop ~4.7 ms / capraz ~3.3 ms, 0 B; ArrangeBench 300 cocuk
  0.07 ms, 0 B; tum testler 0 alloc sartli.

## BOXSTYLE v2 — OVERRIDE MASKESI + TRANSFORM KATMANI (scale/rotation) + Inspector [Flags] (2026-10-15, layout 102 PASS, editor basili screenshot)
- MASKE: `BoxStyleState.enabled` -> `BoxLookFields overrides` ([Flags]: Fill, Border, BorderFill, Radius, Shadow, InnerShadow, Light,
  TextFill, TextOutline, TextShadow, Transform, Sprite — GRUP bazli, alan degil). Varyant yalniz maskeli gruplari Normal'in ustune
  bindirir (CSS `:active { background }` hissi); None = varyant yok. `BoxStyle.Resolve(state, out BoxStyleState)` durum degisiminde bir
  kez (~12 bit kiyasi, alloc yok); `For(ref)` kalkti. Normal'de maske yok sayilir (tam temel).
- TRANSFORM KATMANI: `BoxLook.scale` (Vec2) + `rotation` (derece z), lerp'li. `LayoutBox.ApplyTransformLook`: stil Transform'a ILK
  dokundugunda taban (`_baseScale/_baseRot`) okunur; `localScale = taban*scale`, `eulerZ = taban+rotation`; notr (1,1/0 ya da (0,0)=
  belirtilmemis) ve hic dokunulmamissa Transform'a YAZIM YOK (stil kullanmayan kutuya sifir maliyet). Stil kalkinca (`Style=null`)
  `RestoreTransform`. `CaptureLook` surulen orani geri hesaplar (yarida kesme dogru). scale (0,0) -> (1,1) (Resolve + Apply).
  Scale layout'a girmez kurali geregi MarkDirty yok. Baska surucu (Tween/MovieClip) ayni anda Transform yazarsa son yazan kazanir.
  Pozisyon ofseti (basinca 2px kayma) ALINMADI: pozisyon layout'un surdugu alan (Encode geri basar) -> render ofseti gerekir, 2. adim.
- INSPECTOR [Flags] (editor/ObjectDrawer.DrawFlags, jenerik): [Flags] enum -> ComboBox; 0. eleman OZET ("Fill +3" / None / All),
  digerleri "[x] Ad" / "[  ] Ad", secim biti CEVIRIR (Enum.ToObject). Sifir/bilesik degerler listelenmez. Serializer flags'i zaten
  "A, B" metniyle yaziyor/okuyor (Enum.TryParse). Ozet dogrulandi; popup native pencere oldugundan ui.screenshot'ta gorunmez.
- "COPY FROM NORMAL" DUGMESI YAPILMADI: maske mirasi sorunu cozuyor; tip-ozel Inspector kodu olurdu (kullaniciya anlatildi).
- EDITOR DOGRULAMA: BlueButton.asset maskeli: pressed `overrides: Fill, BorderFill, Shadow, Transform` + `scale 0.92`, `rotation -3`;
  disabled `Fill, BorderFill, TextFill, Light, Shadow`. Shop basiliyken: kucuk+egik, koyu dolgu, beyaz border, light/radius Normal'den
  miras; birakinca birebir geri. Locked: gri, light yok.
- TESTLER: StyleMaskAndTransform — Normal notr -> Transform'a dokunulmadi; maske: Fill ezildi, Border/Shadow miras; Transform grubu
  taban x 0.9 / +5 derece; Normal'e donus taban; stil kaldirilinca taban geri. Onceki StyleApplyAndTransition maske modeline tasindi.
- TUZAK: editor combo popup'i (native, modal) acikken RPC yanit vermiyor — ui.click ile combo acarsan once kapat (esc gonderilemez cunku
  RPC de bekliyor); editoru kapatmak gerekti. Ayri bakilacak (RPC'nin popup acikken de islenmesi).

## BOXSTYLE ASSET + DURUM + TRANSITION (2026-10-15, layout 97 PASS, editor: 3 buton Normal/Pressed/Disabled + asset canli guncelleme)
- VERI (engine/managed/BoxStyle.cs): `BoxLook` struct = LayoutBox'in LAYOUT'U ETKILEMEYEN gorunum alanlari (fill, border+borderFill,
  radius, shadow*, innerShadow*, light*, textFill/outline/shadow) + `Lerp` (Gradient: renk/durak lerp, type/direction/stops %50'de
  adim). `BoxStyleState { enabled, look, sprite, spriteFit, slice9 }` (sprite alanlari ADIM). `[Asset][CreateAssetMenu("UI/Box Style")]
  BoxStyle { normal, hover, pressed, disabled, selected, transition sn, transitionEase }`; `For(state)` ref doner (etkin degilse Normal).
  Varyant = TAM KOPYA (alan bazli override maskesi v2). `BoxState { Normal, Hover, Pressed, Disabled, Selected }`.
- LAYOUTBOX (LayoutBox.Style.cs partial): `[SerializeField] BoxStyle style` + `Style` property; stil varken kutunun kendi gorunum alanlari
  SURULUR (ustune kopyalanir; Unity Selectable gibi — doc'a kopya degerler yazilir, zararsiz). `SetState(s)`: adim alanlari aninda;
  BoxLook `from = CaptureLook()` (yarida kesilince mevcut degerden) -> `to`, `LayoutSystem.AddTransition`; ilk Encode'dan once
  (`_everEncoded` false: OnEnable'daki baslangic durumu) ya da EditMode/dur<=0 -> ani (giris flash'i yok). Tick `LayoutSystem.
  AfterUpdate -> TickTransition(dt)` yalniz listedeki kutular; dt=0 edit modunda ilerlemez. Hicbir BoxLook alani MarkDirty uretmez ->
  gecis layout'a sifir maliyet; alloc 0 (testte sart).
- ASSET DEGISIM BILDIRIMI (jenerik): `AssetDatabase.ObjectVersion` (static) — `ReloadObject` basarisinda ++, editor `SaveAssetNow` ->
  `NoteObjectChanged()` ++ (self-write maskesi watcher reload'unu engelledigi icin). Kutu Encode'da `SyncStyle`: referans degisti
  (`_styleApplied`) ya da versiyon degisti -> `ApplyStyle` (1-2 kiyas/frame). OnValidate -> `ValidateStyle` yalniz gecis yokken (animasyon
  bildirimi OnAnimatedPropertiesApplied -> OnValidate gecisi oldurmesin).
- HOVER v1'de URETILMEZ (Pointer bosta Move sifir maliyet; mobil oncelik) — slot hazir. Durum kaynagi OYUN KODU: Sandbox
  `Scripts/StyledButton.cs` ornegi (Down->Pressed, Up/DragStart->Normal, interactable=false->Disabled); yuzbirextra Button.cs DOKUNULMADI.
- SERIALIZER: ic ice struct (BoxStyleState.look.fill) + struct icinde Sprite referansi sorunsuz; YAML elle yazildi (asset.write),
  `comp.schema` style alanini `Asset tip: BoxStyle` gosterdi; `code.build` ile StyledButton katalogda.
- EDITOR DOGRULAMA: Sandbox `Scenes/StyleTest.scene` (KAYDEDILMEDI; JSON %TEMP%\styletest_apply.json; asset `Styles/BlueButton.asset`
  + `Scripts/StyledButton.cs` DISKTE kaldi): Play/Shop Normal (gradient+border+light+golge+metin golgesi), Shop basiliyken (input.tap
  holdFrames=90 + es zamanli render.screenshot, Start-Job) Pressed (koyu, beyaz border, radius 20), birakinca Normal; Locked Disabled gri.
  Asset'i diskte degistirince (mavi->yesil) sahneye dokunmadan kutular canli guncellendi (piksel 167/232/188), geri alindi.
- TESTLER: StyleApplyAndTransition — kopya, versiyon bump -> yeniden kopya (bump'siz kopya yok), t=0.5 ara deger, yarida kesme mevcut
  degerden, bitis + listeden dusme, etkin olmayan varyant -> Normal, stilsiz SetState gorunumu degistirmez, tick+sync 0 B.
- SONRAKI ADAYLAR: varyant override maskesi / "Copy from Normal" editor dugmesi; hover (enter/exit, masaustu); varyantta `scale`
  (Button.cs olcek tween'inin stile tasinmasi); klip asset'i; scrollbar.

## SCROLLVIEW — Overflow=Scroll + ScrollView component'i, kutu culling, klipli hit-test (2026-10-15, layout 87 PASS, editor uctan uca)
- MODEL: `OverflowMode.Scroll` = Hidden (geometrik klip, batch kirmaz) + `scrollOffset` (Vec2, serilesir = baslangic; animatable ->
  Tween/MovieClip scrollTo bedava). Arrange: `ArrangeChildren` -> `ArrangeChildrenCore(offX, offY)` (flex/wrap tlx/tly kaydirilir; None'da
  PlaceInParent sonrasi SetPos; ignoreLayout dekor KAYMAZ) + `MeasureScrollContent` (yalniz scroll kutusunda: cocuk sag/alt kenar max'i +
  pad + ofset -> `ScrollMax` = max(0, icerik - gorunur)). Uniform/GPU kaydirma REDDEDILDI: klip zaten batch kirmiyor, pointer/klip/MovieClip
  CPU pozisyon okuyor; layout zaten SetPos yaziyor (ofset = 1 toplama).
- OFSET DEGISIMI MARKDIRTY DEGIL: dirty zinciri tepeye kadar kirletip tum agaci cozerdi. `ScrollOffset` setter boyut degismedigi icin
  dogrudan `ArrangeChildren()` (alt agac FinishChild ile atlanir; kutu zaten kirliyse bekleyen cozum yeni degeri okur).
- SCROLLVIEW (engine/managed/ScrollView.cs, `[AddComponentMenu("Layout/Scroll View")]`): LayoutBox pointer handler'i TASIMAZ — handler
  bayragi tip-bazli, her kutu pick adayi olup altindakilerin tiklamasini calardi. Jest ayri component'te: Down hizi sifirlar + baslangic
  ofseti; OnDrag `e.worldTotal` -> kutu lokal (Mat4.Inverse) -> ofset = baslangic - hareket, sinir disinda `OverscrollDrag` direnci; hiz
  frame farki/dt yumusatilmis (0.4/0.6), parmak durunca (`_dragMoved` bayragi — frameCount yalniz UpdateAll'da artar, pointer/Update
  sirasi belirsiz) hiz yarilanir; Up -> Update'te atalet (`Friction` ussel sonum, sinira carpinca durur, `MinVelocity` altinda biter)
  ya da sinir disinda yay (`SpringSpeed`). Alloc yok; hareket yokken Update 1-2 kiyas. Cocuk Button'lar Down alir, DragStart'ta iptal
  eder (mevcut desen), Click yalniz suruklenmediyse — ayrim Pointer esiginden bedava.
- KUTU CULLING (tum Hidden/Scroll altinda): Encode'da ComputeClip sonrasi kutu rect (+golge payi) ∩ pencere bos -> return (4 kiyas) —
  parca/glyph dolasilmaz; uzun listede yuk gorunur oge kadar.
- HITTEST KLIP: `LayoutBox.HitTest` override = base rect vurusu + ata klip penceresi (ComputeClip vurusta, lokal nokta testi) — kirpilan
  cocuk tiklanmaz (cizim = tiklama). Kutu-olmayan cocuk renderer'lar (SpriteRenderer) hala kirpilmaz/culling'siz (bilinen sinir; liste
  ogeleri kutu + kutu sprite'i + kutu metni ile kurulur).
- POINTER FIX: `ComputeWorldTotal` kamera yoksa 0 donuyordu (Pick piksel-ortho fallback kullanirken) -> kamerasiz sahnede drag toplami
  hep sifirdi. Simdi ayni fallback isini.
- TESTLER: ScrollSmoke (authored boyut + ScrollMax, ofset kaydirma, kirletmedi, pencere ici/disi quad, klip ici/disi HitTest),
  ScrollViewGesture (Pointer.Down/Move/Up ile drag ofseti, kumulatif, atalet devam/sonum sinir icinde, overscroll negatif + yay 0'a).
- EDITOR UCTAN UCA: Sandbox `Scenes/ScrollTest.scene` (KAYDEDILMEDI; JSON %TEMP%\scrolltest_apply.json): 14 oge (alignSelf=Stretch —
  flex icinde stretch ANCHOR degil alignSelf genisletir, rpc.guide'a yazildi), input.drag 240px -> ~690px kayma (atalet), kutu
  disina tap miss / icine tap Item10 hit. TUZAK: `edit.undo` sim restart'i da geri alabiliyor -> ust uste apply'da Root birikti;
  temizlik `scene.removeGo(id)`.
- SONRAKI ADAYLAR: scrollbar (opsiyonel cocuk kutu + ofset orani), kutu-olmayan cocuk klip/culling (pencereyi WalkRenderers'ta alpha gibi
  indirmek), BoxStyle asset + durum/transition, klip asset'i.

## LAYOUTBOX FLEX TAMAMLAMA — margin / min-max / alignSelf / shrink (2026-10-15, layout 74 PASS, arrange bench 0.07 ms/300 cocuk, 0 B)
- YENI ALANLAR (cocuk kutuda, hepsi SerializeField -> Inspector/animatable/RPC otomatik): `margin` Vec4 (L,T,R,B), `minWidth/maxWidth/
  minHeight/maxHeight` (0 = yok), `alignSelf` (LayoutAlignSelf: Auto|Start|Center|End|Stretch), `shrink` float. Property'ler Grow deseni
  (degisim -> MarkDirty). Yeni gecis YOK: hepsi mevcut olcum/arrange donglerine toplama/clamp olarak girdi.
- OLCUM: `PreferredW/H` cache'i artik `ClampW/H` ile clamp'li (tum boyut yazimlari tek noktadan). Cocuk katkisi `OuterW/H` = preferred +
  margin (flex toplami, None-eksen max'i, WrapContentCross kirilimi — arrange ile AYNI ifade, tutarlilik korunur).
- ARRANGE (flex): 1. geciste `fixedSum += pref + margin`, `shrinkSum += shrink*pref` (CSS agirligi) bedava toplanir. Ana eksen:
  grow -> `leftover*pay - margin`; aksi pref; sonra clamp. Capraz: `CrossPlacement` — Stretch = capraz alan - margin, clamp'li; Auto =
  parent alignChildren. Pozisyon `cursor + mA`, cursor `+= mA + main + mB + gap`.
- SHRINK: yalniz `shrinkSum>0 && fixedSum>availMain` iken (tasma yoksa 1 kiyas). k1 = acik/shrinkSum; min/max'a TAKILAN cocuk varsa TEK
  ek tur (frozen kucultme dusulur, kalan acik serbest cocuklara k2) — CSS freeze dongusunun 2 adimla kesilmis hali (3+ kademeli takilma
  dagitilmaz; bilincli). grow'lu cocuk shrink'e girmez; Wrap'ta shrink yok (CSS'te de satir ici ikincil).
- PLACEINPARENT (anchor/None/ignoreLayout): stretch anchor'da (`anchorMin != anchorMax`) margin kenarlardan dusulur + clamp (clamp kutuyu
  bas kenara yaslar); nokta anchor'da margin yok sayilir (CSS absolute benzeri — margin flex kavrami).
- GROW + max: grow'lu cocuk max'a takilinca artik DIGER grow'lulara dagitilmaz (tek tur; CSS dagitir). Gerekirse shrink'teki gibi 2. tur.
- TESTLER: FlexMarginMinMax (autosize margin dahil, konum, max/min olcum+arrange, grow+max, stretch anchor margin), FlexShrink (orantili,
  shrink=0 sabit, min clamp + artik yeniden dagitim, tasma yokken etkisiz), FlexAlignSelf (Auto/End/Stretch margin/Stretch maxHeight),
  FlexWrapMargin (dis boyutla kirilim, satir caprazi, 2. satir konumu), ArrangeBench (300 cocuk karisik, 0 B sarti). NOT: testte
  `transform.localPosition` okumak PULL DEGIL — once Rect*/ResolveIfDirty.
- GORSEL: Sandbox `Scenes/FlexTest.scene` (KAYDEDILMEDI, undo'lu; JSON %TEMP%\flextest_apply.json): 4 satir — genis (tasma yok), dar
  (shrink: A 140, B min 150 donuk, D 2x, hepsi padding'e sigar), alignSelf+margin, grow+max+margin. Dogrulandi.
- RPC ISTEMCI TUZAGI (onceki girisin "zincir takiliyor" notunun sebebi): `Get-Content -Raw` sondaki CRLF'yi tasir -> rpc.ps1 WriteLine ile
  sunucuya ikinci BOS satir gider, yanit okumasi takilir (editor istegi uygulamis olur). Dosyadan JSON gonderirken `.Trim()`.

## LAYOUTBOX CIZIM PAKETI — 4 duraklı/çapraz gradient + SpriteFit (2026-10-15, layout 55 PASS, bench 0 B alloc, DrawTest screenshot)
- BAGLAM: RmlUi (Downloads/rmlui-master, 6.3) incelendi: RCSS/transition/flexbox/decorator yetenekleri "web kafasi" uretkenligi veriyor
  ama DOM + property sozlugu + element basina geometri + batch'siz; dusuk uc mobil hedefi yok. KARAR: gommek YOK; yetenekler tek tek,
  sicak yol (Encode -> EmitQuad -> tek instanced draw) degismeden eklenir. Adim adim: 1) cizim (bu giris) 2) sonraki karar kullanicida
  (adaylar: BoxStyle asset + durum/transition, flex tamamlama margin/min-max/alignSelf/shrink, ScrollView, asset MovieClip).
  ClipRound (yuvarlak kose cocuk kirpma) KAPSAM DISI birakildi — ileride mask benzeri daha genel efekt yolu.
- KANAL KURALI (yeni): `Instance.user[4]` DOLU (x smoothing, y kenar merkezi/isaret, z ic bant, w mod). Kanal isteyen ozellik sirasiyla:
  mod-paketleme (Progress deseni) -> grup-basi opcode/uniform (scissor granulerligi) -> EN SON instance genisletme (144->160 B = 55k
  sprite'ta %11 bant). Bu paket 0 kanal, 0 shader degisikligi kullandi.
- GRADIENT (Gradient.cs YENIDEN, uyumluluk kaygisi yok — kullanici karari): `GradientDirection` += Diagonal (TL->BR), DiagonalReverse
  (TR->BL). Duraklar `stops` (int 2..4; 0 = eski yaml = 2), `color3/stop3`, `color4/stop4`; `At(t)` parcali-lineer, `AtExtrapolated(t)`
  0..1 disinda komsu segmentin dogrusunu uzatir (kanal clamp'li). `MidStops` capraz yonde HEP 0: durak cizgileri capraz olur, eksen-hizali
  quad'larla bolunemez (2x2 yaklasim denendi: parca sinirinda 12/255 dikis -> reddedildi). Serializer byte desteklemiyor (int/float) ->
  stops int.
- LAYOUTBOX gradient BAGLAMI: `BeginGradient/EndGradient` (EmitLayer dolgu/border, EncodeTextured tint) aktifken `EmitQuad` cagiranin
  c0/c1/gradH'ini YOK SAYAR: `GradT(x,y)` ile 4 kose rengini kutu bandindan ornekler (capraz = tint0..3 bilinear, ek quad yok), eksenel ara
  duraklarda quad'i durak cizgisinde boler (`EmitGradPart`, en fazla 3 parca, yalniz duraktan gecen quad). `EmitQuadCore` 4 kose renkli;
  klip remap'i bilinear (eski c0/c1 ozel hali). Golge/isik/radial/ic golge/metin baglam kullanmaz (kendi renkleri).
- DIKIS DUZELTMESI (onceden de vardi, 4/255): kose hucresi/kenar seridi AA payi kadar kutu DISINA tasar, `At` clamp'ledigi icin hucre
  icindeki egim bozuluyordu -> `AtExtrapolated`. Olcum: H3 kutusu x=798 sutunu 38/34 -> 35/35.
- SPRITEFIT (`spriteFit`, ShowIf sprite): Stretch (mevcut; 9-slice yalniz burada) | Contain (oran korunur, ortalanir) | Cover (oran korunur,
  uv simetrik kirpilir) | Tile (mantiksal boyutta sol-ustten CPU grid; kismi karo uv kirpmali; `TileCap=256` asilirsa karo buyutulur).
  Tile icin fragment'e atlas rect gerekirdi (vertex sablonunda `a_uvrect` fragment'e gecmiyor) -> shader altyapisina dokunmamak icin CPU
  grid secildi. Tint (fill) karo basina degil kutu bandina gore (desen ustunde surekli gecis).
- ALLOC FIX (onceden vardi): `SolidInterior(Action solid, ...)` delegate aliyordu -> EmitLayer/EmitShadow closure'u her Encode'da heap'e
  (400 B/kutu). Simdi `Span<float> rects` (stackalloc 7x4) + `AddRect(ref n)`; Span local function'da yakalanamaz (CS9108) -> statik helper.
- BENCH (editor/LayoutTests EncodeBench, 300 kutu radius+border, 10 tekrar): 2-stop 5700 quad 3.3 ms, 4-stop 7500 quad 4.8 ms, capraz 5700
  quad 3.5 ms; hepsi 0 B alloc (PASS/FAIL sarti). Draw call: ayni materyal -> tek instanced draw korunur (quad sayisi degisir, batch degil).
- TESTLER: GradientStopsSmoke (At 3/4 durak, stops=0 geriye uyum, capraz ek quad yok, capraz ara durak yok sayilir, dikey 3 durak bolme
  sinirli artis), SpriteFitSmoke (Contain/Cover tek quad + slice9 yok sayilir, Tile 4x3=12, cap). Gorsel: Sandbox `Scenes/DrawTest.scene`
  (KAYDEDILMEDI — undo'lu; apply JSON %TEMP%\drawtest_apply.json): 4 durak dikey + border, 3 durak yatay, capraz, regresyon kutusu
  (border gradient+shadow+light+inner shadow), radial, Stretch/Contain/Cover/Tile/Tile+tint. Login sahnesi aciliyor, stil bozulmadi.
- RPC NOTU: rpc.ps1 ile 3-4 komutluk zincir bazen takiliyor (editor sahneyi uygulamis, yanit okunmuyor); tek tek cagri sorunsuz. Ayri bakilacak.

## INTERP (vmint) — canon dunyasinda yeniden ayaga kaldirma; SIMDILIK KAPALI, TEKRAR ELE ALINACAK (2026-10-15, 3 deneme)
- DURUM: `AOT_MODULES=1` selftest interp yolu 0/347 (yuklenmiyordu: `host'ta tip yok: __Canon`) -> **98/347 PASS** (yukleniyor, kosuyor).
  AOT yolu etkilenmedi (125/347, generated.c birebir). Eski 84 case'ten interp'te 29 FAIL (8,10,30,32,44,46,47,53,55-58,61-72,81,83,84).
- MODEL: canon HOST'UN IC MESELESI; modul yalniz EXACT ozellesmis kod tasir. `ModuleWriter.W.Exact(c)`: paylasimli exact kabuk
  (List<string>.Add, govdesiz) -> klon (`Code.ExactContext=true`), `CilFrontend.TranslateInstance` MARKER'SIZ exact baglamla yeniden
  cevirir, `Resolver.ResolveModuleClone` (ResolveCodeBody + AssignVirtualSlots(code) + `Lower.LowerModuleClone`: yalniz iface LdType).
  Canon govde/canon sahipli ornek (`IsCanonCode`) modulde YOK: `Method()` ownersiz placeholder (`Ad#canon`, host'ta bulunmaz -> stub);
  `__dict` vtable sentinel'i de placeholder (slot hizasi). `Type()`: `__Canon` -> object; canon-form tip -> object (`canon->object` sayaci,
  yalniz placeholder imzalari). `IsLocalType`: canon-form asla yerel; `IsLocalCode`: IsCanonCode asla yerel. Write/VisitType ctx enumerasyonlari
  snapshot (klon cevirisi ctx'ye ornek ekler).
- vmint: host'ta olmayan extern (Span ctor intrinsic'i, host'un trim'ledigi uye, zayif sembol 0) artik YUKLEME HATASI DEGIL -> yerel stub
  (`stubReason`, cagrilirsa NotImplementedException; AOT stub sozlesmesi). ModuleWriter `DictSlotKind` int yazar; vmint yeni OP enum'lari.
- KALAN (27x `canon sozluk op'u` OLUMCUL + 1x `typeof: descriptor'suz tip`): bazi gomulu (EmbeddedMethods, 8 439) govdelerde hala LdDictSlot/
  *Rt op'u var -> kaynak: provided sablonun somutlamasi olarak YEREL yazilan ama govdesi canon'dan gelen kodlar (IsSharedExact degil,
  IsCanonCode degil ama Operations canon govdesiyle paylasilan?) ya da struct generic (KVP<string,int> = KVP<__Canon,int> paylasimli)
  uyeleri. TESHIS ADIMI: dmod yazilirken op.Type in {LdDictSlot,...Rt} goren metotlari raporla (`report.ReachableStubs` benzeri liste),
  kaynagi (Exact klon mu, yerel mi) bul. Ardindan: ya o kodlar da `Exact()` yoluna girer ya da vmint'e modul sozlugu (dmod'da slot tablosu,
  yuklemede Demarker esdegeri, frame'de __dict, this->vtable sentinel) eklenir — ikincisi "modulde canon paylasimi" karari (gerekli degil).
- BILINEN SINIRLAR: modulde GVM (generic sanal metot) cagrisi placeholder -> NotImplemented; `T[]` IEnumerable<T> (AOT'de de yok).
- Intrinsic'ler (Span/MemoryExtensions/Buffer/FieldInfo.RefAt): frontend ZATEN genel IR op'larina aciyor (TrySpanCall/TrySpanNewobj:
  AddrLocal/LoadInd/SpanOps.Copy/NewArray...) — C'ye ozgu degil; interp icin ek lowering gerekmedi. Sorun yalniz host'ta karsiligi olmayan
  `extern` bildirimlerinin yukleme hatasi vermesiydi (yukarida stub'a cevrildi).

## CANON LOWERING — sozluk karari IR'da acik op, emitter/interp canon bilmez (2026-10-15, P0-P4 bitti; selftest 125/347 sabit)
- NEDEN: canon sozluk karari Resolver.RequestSlots'ta verilip `Op.DictSlot/DictSlotFn/DictSlotIface` YAN-ALANINDA saklaniyor, CTranspiler
  12 handler + 4 helper'da (`TypeAddr/StaticRef/TypeTestOp/UnboxValueOp/DictArgFor`) "sozlukten mi sabitten mi" diye yeniden yorumluyordu
  (~60 nokta). Yeni T-bagimli op gelince (A4) emitter'da eksik kaliyordu. Tartisilan alternatifler (sanal ozellestirme, ham tip param,
  thin wrapper otomasyonu, canon'u atmak) REDDEDILDI: hepsi ya paylasimi daraltir ya runtime tablo ekler ya da zaten canon'a donusur.
  KARAR: .NET/IL2CPP modeli kalir (ref paylasimi + derleme-zamani slot tablosu), yalniz IR'da acik hale gelir.
- MODEL (`aotcompiler/source/Lower.cs` = "bu op exact tip ister" kararinin TEK sahibi): yeni OpType'lar (Code.cs sonunda; vmint OP_ enum ayni
  sira) — gizli operandlar `LdType`(statik descriptor) `LdDictSlot`(Slot=indeks, Value=DictSlotKind) `LdDictSelf` `LdDictConst`(exact callee
  sozluk sembolu) `LdFnPtr`; tuketiciler `NewRt NewArrayRt BoxRt UnboxRt UnboxOrDefaultRt IsTypeRt AsTypeRt CastRt TypeOfRt GetStaticRt
  SetStaticRt AddrStaticRt ExIsRt ExBindRt DelegateNewRt` (tip descriptor'u YIGINDAN, Primitive.TypeHandle/DictHandle/FnPtrHandle yigin tipleri).
  SOZLESME: gizli operandlar tuketicinin HEMEN ONCESINDE (bitisik), tepededir: Rt op [.., degerler, T]; DelegateNewRt [recv?, dt, fn];
  Call/CallVirtual [.., args, ifaceT?, dict?] (callee ABI NeedsDictParam -> dict; iface cagrisinda itable anahtari HER ZAMAN operand: statik LdType
  / canon LdDictSlot -> emitter tek yol). Trim/vmint bitisiklige dayanir (`Lower.HiddenSlot`). Iki asama: (1) Resolver sabit noktasinda canon
  govdeler (`LowerCanonCode`: slot talebi + yeniden yazim; talep yeni ornek dogurabilir), (2) sabit nokta sonrasi `LowerRest` (LdDictConst/LdType/
  LdFnPtr). Ekleme sonrasi `Linker.LinkLabels`. Ref kutusu / struct `new` icin slot YINE istenir ve operand gider (Trim exact hedefi bu op'tan
  gorur; emitter pop edip yok sayar) — eski davranisla birebir. NullableWrap: slot istenir, operand yok (eskisi gibi).
- EMITTER: handler'lar `HiddenType(op, Rt)` ile T'yi pop eder; yardimcilar "T verildi mi" ile calisir (`TypeTestT/UnboxValueT/StaticRefT`).
  `CVal.IsConst` (gizli operand sabit, SpillStack atlar) — regex degil: `X_s_dict` kullanici alani `*_dict` kalibina yakalanmisti (P1 deneme 1).
  `Op.DictSlot*` alanlari ve `Resolver.RequestSlots` SILINDI. `CTranspiler.Canon.cs` (yeni partial) = BAGLAMA katmani: FnPtrSym/StubSym/
  TypeDictSym/MethodDictSym/DictSymOf, Dict()/DictPrologue (this->vtable[sentinel]), EmitStub/RequestStub/VSlotRefOf, EmitDict/
  ExportTypeSymStrict, VSlotRef sentinel, CanonFieldCType/FieldCast'ler/CanonInst (paylasilan yerlesim), `VirtualCallTakesDict` (exact GVM slotu
  stub, canon slot sozluk alir — Lower ile ayni kural). KAPI: CTranspiler.cs op handler araliginda `Canon.|IsCanon|IsSharedExact|Canonical|
  DictSlot|DictSentinel` = 0 (yalniz `case OpType.LdDictSlot` etiketi).
- DOGRULAMA: `tools/gen_diff.py <taban.c> <yeni.c> [--canon-only|--exact-only]` — generated.c fonksiyon bazli normalize diff (temp adlari/
  etiketler sirali yeniden adlandirilir, STEP/SETLINE atilir). Taban `obj/lower_base/generated.c`. SONUC: 29 212 fonksiyon (3 488 canon)
  BAYT BAYT AYNI; selftest 125/347; Sandbox lean 2506 KB / full 4926 KB (degismedi); Sandbox.exe calisti (60 fps, Login sahnesi).
- vmint (P4): OP_ enum senkron; `LdType`/`LdFnPtr` ham isaretci push; iface CallVirtual tip operandini duser (dispatch kendi kaydindan);
  sozluk op'lari (`LdDictSlot/LdDictSelf/LdDictConst/*Rt`) `vm_fail("canon sozluk op'u: modul canon destegi henuz yok")`. ModuleWriter
  DictSlotKind'i int yazar. Interp yolu ONCEDEN kirik (`host'ta tip yok: __Canon`) — INTERP ISI (ayri): modul sozlukleri (dmod'da slot
  tablolari, yuklemede Demarker esdegeri, frame'de __dict, this->vtable sentinel) bu op'larin ustune kurulur.
- SIRADA (P5, ayri onay): cift yazilan semantikler Lower'a — checked aritmetik (conv.ovf), DivCheck, conv doyurma, constrained->box+callvirt,
  Nullable box — vmint `vm_conv/OP_Div` kopyalari silinir.

## GC REFMAP — struct icinde ref TEK KAYNAK + barrier sozlesmesi + A4 koku (2026-10-15, selftest 125/347 sabit, regresyon yok)
- NEDEN: "struct icinde ref" bilgisi 4 yerde ayri hesaplaniyordu (CTranspiler.CollectGcRefPaths C yol string'i; vmint
  vm_host_struct_refs reflection members[]'dan -> trim'e bagli sessiz GC riski; uretilen trace_*/trace_inline_* fn'leri; canon
  EmitCanonTrace/IsSharedExact ozel dallari). Barrier karari emitter'da `CVal.Owner` sahip sezgisiyle veriliyordu; oysa
  gc_write_barrier(obj,val) obj'yi yok sayiyordu. Kullanici karari: yama degil kok, NativeAOT GCDesc / IL2CPP gc_desc modeli.
- DEGISMEZLER (R1-R6): R1 tip basina TEK RefMap (`aotcompiler/source/RefMap.cs`, alan zinciri, onbellekli) -> descriptor'da
  `Type.refmap` = `X_refmap[] = { n, offsetof(struct X, a.b.c)... }` (class: nesne basindan, struct: payload'dan; ofseti C hesaplar).
  R2 TUM GC tuketicileri tablodan: `gc_trace_object` (trace fn + refmap), `digitoyengine_trace_array` (eleman refmap), kutu
  (flags STRUCT -> header atlanir), statik kokler `GcStaticRoot{addr,map}[]` (`gc_add_static_roots`), vmint host struct `e->refmap`.
  URETILEN TIPLERDE TRACE FN YOK (trace yalniz runtime ic tipleri: dizi/string/closure/modul yerel). R3 barrier GC'nin sozlesmesi:
  `DIGITOYENGINE_BARRIER_REF(slot*)` / `BARRIER_MAP(slot*, refmap)` inline `if (unlikely(gc_marking))`; sahip hicbir zaman
  derleyicide sezilmez (CVal.Owner SILINDI); kusakli/card-mark GC gelirse yalniz runtime degisir. R4 hedef kurali: local/arg struct'a
  yazim barrier'siz; alan/eleman/statik/pointer(StoreInd) hedef barrier'li. R5 canon yerlesim = exact yerlesim -> tek tablo
  (`RefMapSym -> StructTag(canon)`), canon trace ozel dali yok. R6 tek canlilik kapisi (A4 asagida).
- A4 KOKU (`Items<(int,string)>` -> `ValueTuple<int,__Canon>` generated.c derlenmiyordu): `Code.IsCanonCode/NeedsDictParam/
  HasMethodDict` metot argumaninda yalniz UST DUZEY `__Canon` ariyordu (`Contains(Primitive.Canon)`); struct<ref> argumanli generic
  METOT canon kod sayilmiyor -> sozluk yok, canon tipler `&X_type` ile adresleniyordu. Tip tarafi (IsCanonForm) zaten ozyinelemeli
  `Canon.ContainsCanon` kullaniyordu; metot tarafi ayni kapiya baglandi. Test97 CanonStructArg: Items<(int,string)>/Items<KV<string>>
  box/typeof/is/Equals .NET ile birebir. Par94 `TupItems` -> `Items(tup)` geri acildi.
- TEST (Cases/Test97.cs, CaseCount 97, Par96 Disabled): (a) [gcbench] 100x full GC, 100k Node + struct dizileri -> stderr;
  (b) TriColor: GERCEK beyaz->siyah barrier sinavi (dongu MARK'tayken taranmamis nesnenin tek ref'i statik slot / statik struct /
  MARK'ta dogan siyah nesnenin alani / struct dizisi elemani / struct kopyasi / canon List / Dictionary<int,struct>'a tasinir, sonra
  ayni boy tahsisle slot geri kullanilir). BULGU: eski Demo51 ("genc -> yasli") tri-color ARTIMLI GC'de barrier GEREKTIRMEZ (yeni nesne
  siyah dogar) -> AOT_NOBARRIER=1'de bile geciyordu, hicbir seyi sinamiyordu. Test97 NOBARRIER'de FAIL (kapi calisiyor).
- OLCUM: selftest 125/347 (eski 84: yalniz 10,47; AYNI), AOT_CANON=0 ayni. generated.c: 870 refmap tablosu, 0 trace fn, 8983
  BARRIER_REF + 738 BARRIER_MAP, 0 gc_write_barrier. Boyut release: Sandbox lean 2495 -> 2506 KB (+11), full 4904 -> 4926 (+22);
  wasm olculmedi (taban 1319). gcbench (-O1 selftest.exe, TickCount 15.6 ms cozunurluk, ardisik olmayan olcum): taban min 203 /
  medyan 219 ms; sonra min 218 / medyan 250 ms (ara olcumde min 187 de gorulduk -> gurultu sinyalden buyuk). A/B YAPILAMADI:
  HEAD tabani kurulamiyor (Par86-95 + corelib duzeltmeleri commit'lenmemis, Program.cs onlara bagli). SIRADA: commit sonrasi
  `git worktree` ile taban, iki ikili donusumlu 10'ar kosu; >%5 ise `gc_trace_object` tek birlesik dal (struct kutu ofseti tabloya
  onceden eklenir; trace/refmap tek test) — tek kaynak bozulmaz. Sicak yol kazanci kesin: ref yazimi out-of-line cagri -> inline bayrak.
- vmint: `vm_host_struct_refs` SILINDI, VmT.refs/nrefs -> `refmap` (ayni format, `vm_refs_pack`), host ata refmap'i yerel class'a
  eklenir, barrier makrolari. Derleniyor; UCTAN UCA DOGRULANAMADI: AOT_MODULES=1 interp yolu ONCEDEN kirik (`host'ta tip yok: __Canon`;
  AOT_CANON=0'da `Span<int>$ctor` yok). Yan duzeltme: `IsEmittableArray` InternedArrays Context'ler arasi statik -> baska ctx'in eleman
  tipi icin dizi descriptor'u emit etmiyor (interp_host.c derlenir hale geldi).
- YAN BULGU (dokunulmadi): `T[]` `IEnumerable<T>` olarak gecince InvalidCast (dizi descriptor'unda itable yok, DIGITOYENGINE_itable).
- TARTISMA (karar bekliyor): canon'un emitter'a yayilmis 58 dali da ayni hastalik -> IL seviyesinde LOWERING gecisi (`LdDict`,
  runtime-tipli Box/IsInst/NewArray op'lari); CTranspiler ve vmint aptal kalir, cift yazilan semantikler (checked aritmetik, conv,
  constrained) tek yere iner. RefMap bu isin zemini.

## AOT PARITE SUITE (2026-10-15, selftest case 86-95, ~3.500 kontrol; SADECE TEST + RAPOR, duzeltme yok)
- YONTEM: tests/dotnet-selftest/Cases/Par*.cs + Par.cs kiti. Her kontrol `case*1000+k<TAB>deger` satiri basar; harness (ParseResults)
  bunlari ayri case sayar -> .NET baseline ile AOT degeri yan yana. Beklenen deger HARDCODE YOK, olcut .NET. Her kontrol kendi
  lambda'sinda (`Q(() => ...)`, tests/wrap_parj.py ile otomatik sarildi) -> eksik API yalniz o kontrolu dusurur (AOT stub metot bazli).
  Kosu: `AOT_DEBUG=1 aotcompiler.exe`. Eski 84 case: regresyon yok (82 PASS). Par96 (async/iterator) YAZILDI ama .NET'te adim 11'de
  (Task.Yield tek thread'de bitmez) takiliyor -> CaseCount=95, kapali. 97-105 yazilmadi.
- SONUC: 346 satir -> 114 PASS; fail adimlardaki 3.493 kontrol: 1.186 esit, 1.733 STUB (desteklenmeyen yuzey), 574 GERCEK FARK.
- A) SURECI/DERLEMEYI OLDURENLER (testte bayrakla atlatildi, Par.Killers / Arm / yorum):
  1. `int.MinValue / -1`, `% -1` -> C idiv trap, exit 0xC0000095 (.NET: OverflowException). 2. cctor'lar program basinda EAGER kosar;
  cctor exception'i TypeInitializationException'a sarilmadan ABORT (exit 134). 3. `Exception.Source/HelpLink/Data/GetBaseException`
  override -> "override edilecek virtual yok", AOT derlemesi tumden durur. 4. `IEnumerable<(int,string)>` -> emitter
  `ValueTuple<int,__Canon>` tipini emit etmiyor, generated.c DERLENMEZ. 5. `(Span<int>)int[]` -> `Span_1_Int__op_Implicit` LINK hatasi.
- B) DOGRULANMIS SEMANTIK FARKLAR (deger): IL `not` -> `!` (86018: ~x hepsi 0); `div.un` isaretli bolme (86012); `long & 0xFFFFFFFFL`
  -> -8 (86017); `(int)0xFFFFFFFFu == -1` False (86026); float->int NaN/inf/aralik disi: .NET 0/doyurma vs AOT INT_MIN/0/garbage
  (87010/14/18/19, 88026 BitConverter); `-0.0` ToString "-0" vs .NET "0" (88004/88017); sonsuz sembolu "∞" vs "Infinity" (20+ kontrol);
  Math.Round(x, digits) ve MidpointRounding farklari (88010: 2.5->3); Max/Min NaN yayilimi yok (88017: NaN->1); FORMAT: D3/X8/F2/E/N/P/C
  bicimleri ve hizalamayla birlikte format yok sayiliyor, B (binary) yok, custom "0.00"/"#,##0"/"00-00" yok, enum {0:D}/{0:X} ad
  donuyor, Flags ToString "R, X" yerine sayi (89002-89019, 90022/23); string: ToUpper/ToLower yalniz ASCII (91010), CompareTo/Compare
  isaret buyuklugu (-97 vs -1, 91013), interning farklari (91016/91020), ReadOnlySpan<char> ToString tip adi donuyor (91003/91019/91025);
  EnumEqualityComparer yok, enum hash negatif (93013); ilkel dizi cast'leri (int[]<->uint[], enum[]<->int[]) CLR'de gecerli AOT'de
  InvalidCast (94014); exception MESAJLARI jenerik (InvalidCast/KeyNotFound/ArgumentOutOfRange parametre adi yok, 95010); finally'de
  yakalanan exception orijinali eziyor (95005.5); ArrayTypeMismatch firlamiyor (95010.23); IList sabit boyut -> InvalidCast.
  Exception TIPI farklari (118): ArgumentOutOfRange->IndexOutOfRange (26), ArgumentNull->NullReference (22), ArgumentNull/Argument->
  InvalidCast (9), Argument/Format/Overflow->IOException (10!), OutOfMemory/NotSupported -> deger donuyor.
- C) DESTEKLENMEYEN YUZEY (stub, obj/selftest-c/diag.txt): conv.ovf.* TUM varyantlar (71; checked kullanan metot stub), exception
  FILTER `when` (37), Int/Byte/Long/Bool/SByte.Parse, Convert.To*, Array.IndexOf/Sort/LastIndexOf/Clone/GetValue/CreateInstance,
  CultureInfo, StringBuilder ctor/Append/Insert overload'lari, Decimal, AggregateException/SystemException/ArgumentOutOfRange tipleri,
  Enum.Parse/IsDefined/CompareTo, cok boyutlu dizi, Index/Range, Queue/Stack/LinkedList enumerator'lari, Math.Abs/Round/DivRem/Clamp
  overload'lari, Buffer.BlockCopy, IComparable/ICloneable/IConvertible, Exception.Data, ArgumentException.ParamName, Delegate.Method,
  String.Empty alani, String.Join overload'lari, ArraySegment, SortedDictionary. 131x "FieldDefinitionHandle not present": desteklenmeyen
  tipli closure alani atlaninca sonraki ldfld frontend'i dusuruyor (kaskad).
- DUZELTME FAZI A (2026-10-15, 1 deneme, AOT_DEBUG=1, suite 114/346 sabit, eski 82 PASS korundu, cokme/link hatasi yok):
  * A1 YAPILDI: runtime kind 7 `DIGITOYENGINE_EX_OVERFLOW` + `OverflowException : ArithmeticException` (DivideByZero da ArithmeticException'a
    bagli; .NET hiyerarsisi). Emitter `DivCheck(a,b)`: isaretli int/long'da `DIGITOYENGINE_SDIVCHECK_AT(a,d,MIN,ln)` -> d==0 DivideByZero,
    d==-1&&a==MIN Overflow (vmrt.h). vmint OP_Div/OP_Mod ayni. 86006/86007 PASS, Par.Killers=true.
  * A3 YAPILDI: corelib Exception'a virtual `Source/HelpLink/Data/GetBaseException` (alanlar innerException'dan SONRA eklendi; corelib.c
    DigitoyEngineException layout kopyasi onek olarak gecerli). Override artik derleniyor. Kalan B: Data varsayilani null (.NET bos
    ListDictionary), Dictionary<K,V> non-generic IDictionary'yi uygulamiyor, ic ice tip adi `Demo95.App95/X` (.NET `+`).
  * A5 YAPILDI: CilFrontend op_Implicit: `(Span<T>)null` (ldnull, Object tipli) -> bos span intrinsic'i (extern'e normal cagri link
    hatasi veriyordu).
  * A2 YAPILMADI (tasarim): cctor eager+abort; lazy init tum statik erisimlere kapi ister -> ayri karar.
  * A4 YAPILMADI -> SONRA BAKILACAK (KARAR 2026-10-15): `Items<(int,string)>` -> canon `ValueTuple<int,__Canon>` struct tipi generated.c'de
    bildirilmiyor. Tetikleyici: generic METOT + "ref alanli generic struct" argumani (generic TIP orneklemesi ve corelib ici
    KeyValuePair<int,__Canon> yolu CALISIYOR). Build-time clang hatasi (sessiz runtime hatasi degil). Kullanici notu: "struct icindeki
    ref" ozel durumu yamayla kurtarilmis gorunuyor; canon modelinin bu kismi IL2CPP (generic sharing: value-type arg -> tam ozellesme,
    ref -> paylasim; struct icinde ref olunca TAM ozellesme) ve NativeAOT (__Canon yalniz ref tipler; struct<ref> her zaman exact
    orneklenir, canon'a girmez) ile karsilastirilarak yeniden ele alinacak. Testte tuple generic IEnumerable'dan gecirilmeden
    (TupItems) yazili; acilinca generated.c derlenmez.
  * Par.HugeAlloc=false: int.MaxValue boyutlu tahsisler (OOM abort riski) ayri bayrakta.
- DUZELTME FAZI B (2026-10-15, 2 deneme + 1 regresyon kosusu, AOT_DEBUG=1; gercek fark 574 -> 317, eski 82 PASS korundu, Sandbox AOT
  exe derlendi ve calisti):
  * KOK NEDEN: IL yiginda int32/uint32 ayrimi yok; Roslyn int<->uint icin conv emit etmez, uint32'ye sigan long sabitlerini
    `ldc.i4 bits; conv.u8` olarak yazar. Frontend kaynak-tip tahminiyle Int+UInt'i LONG'a terfi ediyordu -> `(uint)-8/2u` isaretli,
    `(int)0xFFFFFFFFu == -1` False, `0xFFFFFFFFL` maskesi -1, `3037000499L*3037000499L` yanlis.
  * Primitive.PromoteNumeric + vmint vm_promote: Int/UInt karisimi -> 32-bit (Int), Long degil. Isaret bilgisi OPCODE'dan: frontend
    `div.un/rem.un` -> UnBin (iki operand uint/ulong'a Conv), `shr.un` -> UnShr (sol operand unsigned), `conv.u8` 32-bit kaynakta once
    uint (sifir-genisletme), `conv.r.un` once unsigned.
  * CTranspiler: IL `not` = BITWISE `~` (eski `!`; vmint OP_Not de), shift sayaci `& 31/63` (C UB -> .NET maskesi), kayan->tamsayi
    `DIGITOYENGINE_f2i/f2u/f2l/f2ul` DOYURMA (NaN->0, inf->Min/Max; kucuk tipler int32 uzerinden; vmint vm_conv ayni), NaN sabitleri
    .NET bit deseniyle (`DIGITOYENGINE_nan_d/f` 0xFFF8.../0xFFC00000; clang 0.0/0.0'i pozitif NaN'a katliyordu).
  * corelib.c: double.ToString sonsuz "Infinity"/"-Infinity" (eski "∞"); Math.Max/Min (double/float) NaN yayar, Max(-0,+0)=+0,
    Min=-0 (fmax/fmin NaN'i yok sayiyordu); MathF.Round rintf (banker's; roundf half-away idi). Math.cs: Sign(NaN) ArithmeticException,
    Clamp(min>max) ArgumentException (C# govde; C Sign silindi).
- DUZELTME FAZI C-1 (2026-10-15, 3 deneme, AOT_DEBUG=1; gercek fark 317 -> 231, EXCEPTION farki 197 -> 39; eski 82 PASS, Sandbox AOT
  exe calisti):
  * RUNTIME KIND'LAR 8..13 (vmrt.h `DIGITOYENGINE_EX_ARGOOR/ARGNULL/ARG/FORMAT/NOTSUP/INVOP`, diziler 8->16 `DIGITOYENGINE_EX_KINDS`,
    WellKnown.ExceptionKinds, Context[16]) + `DIGITOYENGINE_throw_kind(kind, msg)`: corelib C govdeleri artik dogru .NET tipiyle
    firlatir. corelib.c: Substring -> ArgumentOutOfRange (eski IndexOutOfRange); IndexOf/Replace/StartsWith/EndsWith/Contains(null) ->
    ArgumentNull (eski NullReference); Convert.ToInt32(str,base): gecersiz taban Argument, rakam yok Format, tasma Overflow, bos string
    ArgumentOutOfRange, '-' taban!=10 Argument, null -> 0, "0x" oneki (eski hepsi IOException/NRE); String.Replace/Split -> Argument/
    ArgumentOutOfRange.
  * corelib Exception.cs: `ArgumentOutOfRangeException` (ParamName/ActualValue), `ArgumentException.ParamName` + (msg,paramName) ctor,
    `ArgumentNullException(paramName,msg)`, parametresiz `InvalidOperationException`, mesajli IOException/NotSupported/NotImplemented;
    `Exception.Data` varsayilani bos `Dictionary<object,object>` (.NET gibi null degil).
  * Collections.cs: List indexer/RemoveAt/Insert -> ArgumentOutOfRange; AddRange/Find/FindIndex(null) -> ArgumentNull; List ve HashSet
    `version` + enumerator MoveNext InvalidOperationException (.NET: Add/Insert/RemoveAt/Clear; HashSet yalniz basarili Add);
    List.Remove/IndexOf null-guvenli Equals; Dictionary null anahtar -> ArgumentNull, duplicate Add -> ArgumentException("...Key: k");
    HashSet null eleman IZINLI (hash 0; .NET gibi); non-generic IList.Add/Insert/indexer yanlis tip -> Argument, null -> ArgumentNull;
    Dictionary<K,V> artik `System.Collections.IDictionary` uygular (DictionaryEntryEnumerator; Exception.Data ve eski API'ler icin).
  * String.cs: Concat/Join(null) ArgumentNull, Join(start,count) / PadLeft/PadRight(negatif) / IndexOf(char,start[,count]) sinirlari
    ArgumentOutOfRange. Text.cs: GetString/GetBytes null ArgumentNull, GetString(index,count) ArgumentOutOfRange.
  * KALAN (not): Enum.ToObject/IsDefined yanlis tip -> Argument (AOT InvalidCast); `(int)(object)E.A` / `(E)(object)1` unbox (runtime
    alias denkligi var, emitter yolu InvalidCast veriyor -> ayri inceleme); string -> IEnumerable<char>/ICloneable/IComparable cast'leri;
    ilkel dizi cast'leri int[]<->uint[], enum[]<->int[] (CLR gecerli); array IList.Add NotSupported yerine 1 donuyor; dev tahsis OOM;
    `new int[(long)int.MaxValue+1]` Overflow yerine -1 (SESSIZ); AsSpan(start>len) ve List.BinarySearch(index,count) sinir kontrolu yok;
    exception MESAJ paritesi (InvalidCast "Unable to cast X to Y", KeyNotFound "'k'", ArgumentOutOfRange "Actual value was", ToString
    "Type: msg ---> inner" bicimi; ic ice tip adi `/` vs `+`).
  * FIKIR (kullanici, 2026-10-15): editor hot-reload derlemesine PARALEL "CoreLib dogrulama derlemesi" (NoStdLib + Digitoy.CoreLib.dll,
    yalniz tanilar) + CIL frontend tanilari (checked/filter/cok boyutlu dizi) -> AOT uyumsuzlugu edit zamaninda; C sinifini (stub yuzeyi)
    tumden onler. A/B sinifini (davranis farki) onlemez; onun araci bu parite suite'i. AYRI IS.
  * FAZ B KALAN (bu faz disi): `checked`/conv.ovf desteklenmiyor (C sinifi); runtime `0.0/0.0` NaN isaret biti (clang -O1 inline katlama);
    format motoru (D3/X8/F2/E/N/P/C, hizalama+format, custom bicimler, Flags ToString), string ToUpper non-ASCII, exception mesaj/tip
    paritesi (ArgumentOutOfRange->IndexOutOfRange, ArgumentNull->NullReference, ->IOException).
- SIRADA (karar kullanicinin): duzeltme A->B->C; Par96 Task.Yield/Wait kontrollerini cikar; 97-105 (LINQ, generics/variance, delegate,
  struct/boxing, pattern/record, unsafe/span, init sirasi, DateTime/Random, comparer/uri); AOT_MODULES=1 interp kosusu (ikinci faz).

## .NET 10 UPGRADE (2026-10-15, tum projeler net10.0; SDK 10.0.203, runtime 10.0.7)
- KARAR: 16 csproj `net9.0 -> net10.0` (UnityCheck netstandard2.1 kaldi, Unity icin). Hardcode cikti yollari da guncellendi:
  editor/GameCode.cs (uretilen csproj TFM), editor/IdeFiles.cs, aotcompiler/source/Program.cs, tools/MsdfPort/Program.cs,
  aotcompiler/tests/*.ps1, 3x .vscode/mcp.json, oyun csproj HintPath'leri, copilot-instructions.md, editor-distribution.md.
- Roslyn paketi 4.12.0/4.14.0 DEGISMEDI (net10 ile calisiyor; C# 14 istenirse ayri is). global.json yok.
- DOGRULAMA: tum projeler build OK; editor Sandbox acilis testleri hepsi PASS (lifecycle 23, movieclip 65, serialization 38, ...);
  RPC `code.build` OK (oyun kodu TPA'dan net10 referanslariyla in-process derleniyor). AOT CIL selftest 82/84 — 2 FAIL
  (Test10 InlineArray4 string.Format, Test47 StringSplitOptions) ESKI BILINEN (bkz. "selftest 68/71" notu), net9 binary'si de
  ayni tipleri referansliyor; .NET 10 regresyonu yok.
- DAGITIM ETKISI: editor kullanicilarinda artik .NET 10 Runtime gerekir (docs/editor-distribution.md).
- TEMIZLIK (ayni gun, kullanici karari): editor/PixelEffectTests.cs SILINDI (kasitli bozuk shader acilis log'una sokol
  GL_SHADER_COMPILATION_FAILED gurultusu basiyordu; fx compose zaten ProgressTests'te dolayli kapsanir). editor/AudioTests.cs
  SILINDI (Run cagrisi 2026-10-11'de kaldirilmisti, dosya olu kalmisti). Kokteki eski stdout dokumleri out_*.txt (9 dosya) git'ten
  cikarildi. Acilis testleri 15 grup, hepsi PASS.

## WASM: FILESYSTEM=0, pak fetch -> heap (2026-10-14, Sandbox tarayicida dogrulandi; game.js 116 -> 65 KB)
- KARAR: `--preload-file`/MEMFS/`game.data` KALDIRILDI. `-sFILESYSTEM=0` (+`-sEXPORTED_FUNCTIONS=_main,_malloc`); `game.pak` Build/wasm'a
  AYNEN kopyalanir. `host_wasm.c`: main loop once kurulur (g_running=0 -> bos frame), `de_js_fetch_pak("game.pak")` (EM_JS fetch ->
  arrayBuffer -> _malloc + HEAPU8.set) -> `de_wasm_pak_ready(ptr,len)` (KEEPALIVE) -> `de_fs_mount_memory("/data/Build/game.pak")`
  + `de_app_init("/data",...)`. GameHost yolu degismedi.
- `de_fs.c` web backend: `de_fs_mount_memory/de_fs_mem_range`; handle `mem` tasir (f = sentinel), read = sinir denetimli memcpy,
  yol-tabanli okuma (handle<0) web'de yok. `audio_shim.c` wasm `de_music_open`: fopen yerine `de_fs_mem_range` -> Blob (ek tampon yok).
- Pak heap'te TEK kopya (MEMFS modelinde JS tarafinda + okuma kopyasi vardi). `printf/stderr` FILESYSTEM=0 ile calisir (minimal fd_write);
  `sokol_shim` crash_native.log fopen NULL doner (guard var). Prefs zaten localStorage.
- SIRADA (kullanici: sonraki konu): tek HTML (`-sSINGLE_FILE` + pak base64 `Module.pakBase64` -> `de_wasm_pak_ready`; EM_JS'te 3 satir);
  `--closure 1` (-20..30 KB js), MINIMAL_RUNTIME.

## CANON GENERICS + TRIM SIZINTILARI (2026-10-14, selftest 82/84; Sandbox/yuzbirextra lean+full boot OK)
- CANON (.NET __Canon modeli, yalniz REF-TIP argumanlar; `aotcompiler/source/Canon.cs`): exact `List<string>` kabuk (descriptor/vtable/
  itable/statics/dict) + govdesiz Code; kod `List<__Canon>`'da. Kimlik: `Canonicalize` (ref -> __Canon, generic struct ozyinelemeli);
  canon govde IL'den MARKER'li baglamla cevrilir (`__Canon#0`, `#1.0` yol isaretcili; `Apply/Instantiate` marker'i __Canon'a indirger)
  -> `op.TypeArguments/OpenRef` acik bicim = sozluk slot anahtari (`Demarker` exact'e cozer). Pozisyonel/unify REDDEDILDI
  (`Dict<KVP<string,int>,KVP<Foo,int>>`). Sozluk: derleme zamani sabit `X_dict[]` (TypeHandle/TypeDict/MethodDict/FnPtr), talep
  Resolver sabit noktasinda (`ProcessDictionaries`), derinlik korkulugu 10. Erisim: class instance metodu `this->gc.type->vtable[K]`
  (Hierarchy dict sentinel slotu; tureyen miras alir), static/struct/canon generic metot gizli ilk param `__dict`. Fn-pointer
  gereken her yer (MethodInfo.fn, delegate, struct vtable/itable, GVM slotu) `de_inst_*` instantiating stub -> thunk/vmint/Invoke
  DEGISMEDI. C struct canon'un (`StructTag`), exact alan erisiminde acik cast; `Type.statics/.dict` (append-only); exact cctor
  `canon_cctor(exact_dict)`; `DIGITOYENGINE_is_any` (runtime hedefli is/as/cast); canon kod yalniz trace kaydi. Kapi `AOT_CANON=0`.
- OLCUM Windows release: Sandbox lean 3 278 -> 2 448 KB, full 5 754 -> 4 674; yuzbirextra lean (Common.dll ile) 3 535 -> 2 321 KB.
- TRIM SIZINTILARI (Common.dll "sadece durmasi" +500 KB wasm): (1) `IsSubtype` TANIM esitligi -> exact (canon esdegerligi dahil;
  `Dictionary<Char,Char>` ~ `Dictionary<int,X>` sayiyordu); (2) descriptor trim: `IsEmittableModel` -> `IsTypeLive`, `MarkType`
  imza/yerel/alan/canon/generic_def/sozluk + runtime kok tipleri (exception kind, NotImplementedException, reflection wrapper,
  System.Array); (3) "tum System.* ornekli" -> yalniz generic olmayan tanimlar; (4) "tum struct'lar ornekli" -> yalniz CANLI
  struct'lar (sabit nokta); (5) sozluk kenari op-bazli, yalniz canli exact (`DrainDictOps`). `--trim-report`: "canli kod sahibe gore
  + kok yolu", "kodsuz canli tip neden zinciri".
- CIdent: kaynak ad `u_` oneki (rezerve liste YOK; emscripten `#define unix 1` tuzagi). KALAN: Test64 matrisi, `--no-canon` CLI,
  ModuleWriter gomulu-generic kontrolu, perf A/B, 895 kodsuz canli tip (AssetDatabase.LoadTexture zinciri).

## REGEX FAZ 1: corelib'de minimal .NET-uyumlu motor (2026-10-14, selftest 82/84 = eski 2 bilinen; Demo85 21/21 .NET paritesi)
- KARAR (kullanıcı): POSIX/PCRE/compile-time yok; kendimiz, **çok minimal**, tek dosya `aotcompiler/c_runtime/corelib/Regex.cs`
  (eski 2-pattern'lik Prefix/Suffix stub'u SİLİNDİ; engine'de çağıran yoktu). Kapsam = Common + core.upm **runtime** kullanımı:
  `^[\p{L}\d _-]+$` (NickValidator) ve `\{(\d+)(?::([^}]+))?\}` + `Replace(MatchEvaluator)` (LocalizationFormatter).
- Alt küme: literal/kaçış (`\{ \} \( \) \. \\ \n \t \r \uXXXX`), `.`, `[..] [^..] a-z`, `\d\D\w\W\s\S \p{L} \p{N}`, `( ) (?: ) |`,
  `* + ? {m} {m,} {m,n}` + tembel, `^ $` (.NET: son `\n` öncesi). Options: None/Compiled(yok sayılır)/IgnoreCase/Singleline/
  CultureInvariant; diğeri ve dışı yapılar ctor'da `ArgumentException("regex: desteklenmeyen yapi '..' @konum")`.
  Yüzey: `Regex(.., opt) IsMatch/Match(+startat)/Replace(string|evaluator)` + statikleri (8'lik pattern cache) + `Escape`;
  `Capture←Group←Match` (gerçek .NET'e karşı derlenmiş IL `Capture.get_Value` çağırır — hiyerarşi şart), `GroupCollection`,
  `MatchEvaluator`, `RegexOptions`. `$1/$$` yerine koyma var.
- İç yapı: parser → `Node[]` (KChar/KAny/KSet/KBol/KEol/KGroup/KRepeat, kind<0 = grup kapanışı); **tek çekirdek**
  `MatchSeq(seq,i,pos,Cont)` delegate'siz continuation zinciri (`Cont{seq,idx}` ya da tekrar adımı `rep,count,iterStart`);
  boş tekrar guard'ı; 1M adım sınırı → `InvalidOperationException`. Tüm API `Scan` üstünden.
- SINIR: `\p{L}`/`\w` corelib `Char.IsLetter` = Latin aralıkları (Kiril/Arap nick → eşleşmez; Char tabloları ayrı iş). FAZ 2:
  lookaround, isimli grup, backreference, `\b`, Multiline, `Matches`, `Split`.
- Parite testi: tests Cases/Test64.cs (Demo85) — baseline gerçek .NET; **desteklenmeyen yapının hata vermesi bu harness'ta
  test edilemez** (.NET'te geçerli). yuzbir: `NickValidator.cctor` stub kapısından çıktı; `System.IO.InvalidDataException`
  corelib'e eklendi (IO.cs) → **yuzbir full build Common.dll ile ilk kez geçti ve boot OK** (eager cctor'lar: Inflater
  DEFLATE tabloları, NickValidator regex). Kalan Common stub'ları (diag.txt) erişilmez; Katman A listesi duruyor.

## UNITY MCP PORTU — unity/Mcp (2026-10-14, yuzbirextra.unity'de doğrulandı)
- `com.digitoygames.mcp` UPM paketi (yalnız Editor/): editör RPC'sinin Unity 6 portu; motor koduna/`tools/mcp-bridge`'e
  dokunulmadı. Ayrı köprü `Bridge~/` (.NET 9, env `UNITY_MCP_PORT`, default 5158, domain reload için 20×500ms reconnect).
  Sözleşme aynı: `{id,cmd,args}` → `{id,ok,result|error}`, `rpc.describe`/`rpc.guide` şeması birebir (köprü bunlardan üretir).
- GO id = `instanceID`; prop yazımı `SerializedProperty` (alan adı temiz alias `m_Color`→`Color`; `A[0].B` → `Array.data`);
  referans biçimleri: asset yolu/`#sub`/guid, `goId`, `goId:Type:ord`, `A/B` yolu. Undo: batch/apply = `IncrementCurrentGroup`
  + `CollapseUndoOperations`, hata → `RevertAllDownToGroup` (atomik doğrulandı).
- TUZAK: `EditorApplication.Step()` update içinden frame üretmiyor → adımlama "çöz → N frame akıt → yeniden duraklat"
  (`Time.captureDeltaTime=1/fps` deterministik; ±1 frame overshoot, `simulated` döner).
- TUZAK: Unity arka plandayken (play+paused) editör döngüsü uyur, `EditorApplication.update` durur → okuyucu thread her
  istekte ve Pump deferred iş varken ana pencereye `PostMessage(WM_NULL)` atar (Windows); arka planda çalışması doğrulandı.
- Play'e giriş domain reload yaparsa komut `{state:"starting"}` döner, ajan tekrar çağırır; Enter Play Mode Options
  "Reload Domain kapalı" ile tek seferde biter. Screenshot play'de end-of-frame `ReadPixels` (overlay UI dahil), edit'te kamera render.
- MovieClip/Tween/DocNode/pak gibi motor-özel şeyler bilinçli olarak yok.
## REFLECTION: alan erişimi üretilmiş fonksiyonsuz (offset+tag), property = MethodInfo (2026-10-14, selftest 81/83 = eski 2 bilinen)
- ÖLÇÜM (yuzbir full): her field/property için `digitoyengine_refget_*/refset_*` çifti üretiliyordu → **9 959 fonksiyon**, 1 334 tipte,
  ~3.7 MB C (en çok LayoutBox 348, SG 316, GLFWConst 256, HandSorter/<>c lambda cache 211). %85'i `offset+tag` ile aynı bellek işi;
  `DigitoyEngineMember` zaten `tag/offset/size/addr/valueType` taşıyordu (modül "tek meta"). hxcpp incelemesi (`StorageInfo{type,offset}`)
  aynı modeli doğruladı; hxcpp'nin `__Field` string-switch + metot başına closure'ı bizden ağır — feyz alınan tek şey "fonksiyon üretme".
- KARAR: `DigitoyEngineMember.get/set` fn ptr'ları KALDIRILDI → `getter/setter` (`const MethodInfo*`, yalnız property). Alan: runtime
  `digitoyengine_member_get/set` → `*_generic` (tag switch; eskiden yalnız modül tipleri içindi, şimdi tek yol). Property:
  `digitoyengine_invoke_items(getter/setter, target, &value, n)` = `MethodInfo.Invoke` çekirdeği (sanal getter vtable/itable'dan,
  struct self payload). `CTranspiler.EmitReflectionMembers` yalnız tablo yazar (`AccessorRef` → `MethodRef`; kaydı yoksa 0).
  `ReflectGetValue/ReflectSetValue/PropertyCall/ReflectSelf` SİLİNDİ. Kazanç yan etkisi: ref alan yazımında `gc_write_barrier` artık
  tek yerde ve doğru (eski refset barrier çağırmıyordu).
- TRIM: Props facet'li tipin get_/set_ Code'ları `TrimResult.Accessors` → `MethodRecord` Full (thunk) — aksi halde lean'de
  PropertyInfo.GetValue "imza sekli host'ta yok" derdi. Kurallar korundu: static readonly SetValue yasak (`isStatic && isInitOnly`),
  fixed dizi (tag `p`, size≠8) unsupported, descriptor'suz struct alanı unsupported.
- SONUÇ: Sandbox lean release 3 362 816 → 3 278 336 B (−84 KB; lean'de az üye var, full'de etki çok daha büyük). Lean+full boot OK
  (AOT_DEBUG=1), gen'de `refget_` 0. yuzbir full build Common.dll stub kapısında (ayrı iş) durduğundan orada ölçülmedi.

## LOCALIZATION: bağlı alanın metni SERİLEŞMEZ — tek gerçek tablo (2026-10-14, 50 PASS, RPC e2e + pak/player OK)
- KARAR (kullanıcı): `text@loc` dolu olan alanın `text` prop'u doc'a/baked'e YAZILMAZ; yüklemede tablo yazar. Önceki "son dil
  önbelleği" ikinci gerçek kaynaktı (LocEditOp iki yeri yazıyordu). Şimdi: `SceneDoc.CaptureGo` bağlı alanı atlar, `CompToNode`
  eski doc'tan kalan metni düşürür (`IsLocBoundField`), `SceneBinary.WriteFields` aynı (`HasLocBinding`). `Localize.Binding.Authored`
  SİLİNDİ: satır yoksa alan `[MISSING:key]` (görünür hata, sessiz eski metin yok).
- Editör: `EditorScene.SetProp` bağlı string alanda doc'a yazmaz → tablonun çalışılan-dil hücresine (`LocEditOp` yalnız hücre; RPC
  `prop.set` ile inspector aynı yol). `SetLocKey/ApplyLocKeyOp(.., text)`: bağlanınca `text` prop'u doc'tan düşer, kapatınca
  (`""`) çalışılan-dil hücresi prop'a geri yazılır (metin kaybolmaz); `LocKeyOp.OldText/NewText` undo/redo simetrik. Doğrulandı
  RPC ile: bind → props `{Text@loc}` yalnız; prop.set → tablo; unbind → `{Text@loc:"", Text:"Probe Edited"}`; undo → geri.
- Açık karar (önceki mesaj): key'in inspector'da görünürlüğü (a/b/c) kullanıcıdan bekleniyor.

## LOCALIZATION F3: RPC loc.*/ui.*, formatter portu, placeholder önizleme, açılış dili (2026-10-14, 48 PASS, RPC uçtan uca)
- RPC `loc.*` (`editor/RpcLoc.cs`): `languages/setLanguage/addLanguage/removeLanguage` (katalog dışı red), `list(filter,missing)`,
  `set(entries)` toplu tek undo (`[{key,lang,value}]` ya da `[{key,values:{..}}]`, olmayan key satır açar), `remove/rename`,
  `key/bind/unbind(go,comp,field)`, `usages/unused(+dangling)/missing/glyphCheck`. Denetimler `editor/LocAudit.cs`: dosya düzeyi
  tarama (`@loc:` satırları + C# `Localize.Get|Format("key")` sabitleri; kapsam = `TableKeyFor(rel)` eşleşen dosyalar), glyph = tablo
  codepoint'leri ⊆ projedeki her .ttf/.otf Font.Glyphs (kontrol/`{}` hariç). Pak build `LocWarnings`: eksik çeviri sayısı + glyph
  boşlukları uyarı (durdurmaz). `rpc.guide` "localization" + "editorUi" bölümleri.
- EDİTÖR GUI RPC `ui.*` (`editor/UiAutomation.cs`, `RpcUi.cs`; kullanıcı: fare simülasyonu yok): sentetik girdi `GuiInput.Inject`
  ile GLFW callback'iyle AYNI kuyruğa; adımlar frame'e yayılır (down/up ayrı pass). `Busy` iken RPC pompası bekler → `ui.click`
  sonrası komut jest bitince koşar; `Engaged` (son girdi sentetik) klavye odağı varken de pompayı açık tutar (insan tuş/tık görülünce
  kuyruk atılır, `GuiInput.HumanInputSeen`). Koordinat = ana pencere mantıksal px == `ui.screenshot` pikseli. Açık `GuiPopup`
  (combo listesi) içine düşen tık popup penceresine yönlenir (`GuiPopup.TryMapFromHost`, ekran px). `ui.panels` =
  `GuiDock.ListPanels` (tab + içerik rect'i; detached/utility hariç). `ui.screenshot`: `de_sokol_read_framebuffer` (fb 0, sg_commit
  sonrası/swap öncesi `UiAutomation.CaptureIfRequested`), `RpcHost.Defer` ile yanıt. `ui.type` TextInput, `ui.key(name,mods)`.
  TUZAK: popup ayrı OS penceresi → `ui.screenshot`'ta görünmez; satır yüksekliği `GuiPopup.ItemH`=20.
- FORMATTER (`engine/managed/Localization/LocFormat.cs`, Unity LocalizationFormatter portu; Regex/CultureInfo/`ToString("D2")`
  YOK → AOT corelib): `{n:cap}`, `{n:number[:N2|F0|0.##|short[:eşik]]}` dil ayıracı tablosu (en `1,234.5`; tr/de/es/it/pt/nl/id/vi
  `1.234,5`; fr/ru/pl `1 234,5`), kısaltma tr `B/M/Mr` diğer `K/M/B`; `{n:time:remaining[:long][:uN][:eşik][:birim]}`, `ago`,
  `date:<desen>`, `localtime:<desen>` (`LocFormat.UtcOffsetSeconds`, varsayılan 0 — host set eder), `datelabel[:relative][:saat|
  tarih]`. Desen jetonları yyyy yy MMMM MMM MM M dddd ddd dd d HH H hh h mm m ss s tt 'literal'; ay/gün adları en/tr/de/fr/es
  gömülü. `FormatDefaults` (format.*, 5 dil) tablo satırıyla ezilir. `Localize.FormatString(template,args,lang,table)`;
  `LocFormat.NowProvider`. Takvim corelib `DateTime` (Year/Month/Day) üzerinden; haftanın günü unix günden.
- `Localize.Get(key)/Format(key,args)` TABLOSUZ: `Table()` ile yüklenen tablolar sırayla (host önce, modüller) — `Loc.g.cs` codegen
  DÜŞÜRÜLDÜ (Registry.g.cs yok; key güvenliği `loc.unused/usages`). Yoksa `[MISSING:key]` (görünür).
- INSPECTOR: bağlı metinde `{n}` varsa altında `{0}`,`{1}`… arg kutuları + `preview` satırı (çalışılan dil, `LocFormat`); ölçüm
  `PlaceholderCount` ile aynı. Doğrulandı: `{0:number} coins left, {1:time:remaining:u2}` + 12500/5400 → "12,500 coins left, 1H 30M".
- AÇILIŞ DİLİ: `Localize.InitLanguage()` (GameHost.BootWith): PlayerPrefs `Localize.Lang` > `DeviceLanguage()` (shim
  `de_sys_language`: Win `GetUserDefaultLocaleName`, mac `CFLocaleCopyPreferredLanguages`; yalnız DE_DESKTOP, mobil kabuk sonra)
  > kaynak dil. Oyun içi seçim `Localize.SetUserLanguage(lang)` prefs'e yazar; editör Working language prefs'e DOKUNMAZ.
- AOT DERSİ (publish kırıldı, düzeltildi): corelib yüzeyi dar — `IFormattable`, `decimal`, `AsSpan` parse, `new string(char[])`,
  `Array.IndexOf<T>` YOK; kodda kaldırıldı. KÜÇÜK EKLER corelib'e girdi (kullanıcı kararı): `Char` statikleri (IsDigit/IsLetter/
  IsUpper/IsLower/IsWhiteSpace/ToUpperInvariant/ToLowerInvariant — Latin-1/Ext-A, Numeric.cs; WellKnownTypes'taki boş struct
  kaldırıldı), `Double.IsNaN/IsInfinity`, `string.IndexOf(char,start,count)`, `PadLeft/PadRight`, `Join(sep,arr,start,count)`,
  `StringBuilder.Append(string,start,count)`. Doğrulama: `aotcompiler sdk-il` 5 hedef + RPC `player.build windows` link OK +
  Sandbox.exe açıldı (`[host] dil: en`). Sayı yuvarlama yarım-yukarı (`(long)(x*scale+0.5)`), Math.Round banker's değil.
- SONRAKİ: mobil kabuk dil + TZ (`UtcOffsetSeconds`), modül prefab'ının host sahnesinde kullanımı (kapsam = sahne sınırı),
  `loc.usages` kod taramasında dinamik key'ler görünmez (string sabit kuralı).

## LOCALIZATION F2: editör — "loc" toggle, otomatik bağ, Working language, tablo penceresi (2026-10-14, 38 PASS, RPC+görsel doğrulandı)
- `editor/LocTableStore.cs`: editörün tablo kapısı. Kapsam `App.EditScene.LocTableKey` (prefab stage dahil); `Get(scope, create)`
  tabloyu **ilk bağda kendisi yaratır** (`Assets/Localization.asset` | `Assets/Modules/<ad>/Localization.asset`, diller host
  tablosundan ya da `["en"]`; `ScanMetas` guid). Yazımlar AssetDatabase'in CANLI instance'ına → dirty → `Tick` (1 sn boşta, hot
  control yokken) / `Flush` (sahne Save, Play, çıkış, pencere Save). Her `Touch` → `Localize.RefreshAll` (projeksiyon + Play anında).
- DİL KATALOĞU SABİT (`LanguageCatalog`, ISO 639-1, 18 dil): serbest kod girişi YOK — pencerede işaretli kutular (tik = sütun ekle,
  kaldır = sütun + hücreler silinir; kaynak dil kilitli). `AddLanguage` katalog dışını reddeder (RPC/kod yolu da).
- KEY ÜRETİMİ `NewKey`: `slug(sahne).slug(go)[.alan]`; GO adı jenerikse (`GameObject/Text/Label/LayoutBox/TextSprite/Box/Button/
  Title`, "(n)" eki atılır) metnin ilk 4 kelimesi; çakışma `_2`; `Slug` translitere (ç/ğ/ı/ö/ş/ü, de/fr/es), camelCase→`_`. Aynı
  metin (çalışılan dilde) zaten varsa `FindByText` → mevcut key'e bağlanır. `Suggest`: çalışılan dilde değer/key substring, yoksa
  diğer diller (etiket `tr: ...`), en fazla 5.
- OTOMATİK BAĞ `EditorScene.SetProp` boğazında (inspector + RPC `prop.set` aynı yol): `LocTableStore.IsDefaultLocField`
  (`LayoutBox.text`, `TextSprite.Text`) + prop yok + metin dolu → `AutoBindLoc` BEKLETİR (`TickLoc`: klavye odağı yok ya da 1 sn)
  → `BindLocNew`. Neden bekletme: inspector harf harf yazar, ilk harften `login.p` gibi key doğmasın. Dolu literal düzenlenirken
  bağ açılmaz (kullanıcı toggle'la açar). Üç durum: prop yok = varsayılan, `""` = bilerek kapalı (nick), key = bağlı
  (`LocKeyOf`). WriteLocBindings boş key'i binary'ye yazmaz.
- UNDO (`editor/LocUndoOps.cs`): `LocKeyOp` (prop + bu işlemle açılan satır; undo satırı siler), `LocEditOp` (doc önbellek metni +
  tablo hücresi TEK adım, yazarken coalesce), `LocCellOp`, `LocRenameOp` (tablo + açık doc `RenameLocKey`; başka sahne/prefab
  dosyaları `RenameInOtherScenes` metin düzeyi, undo'suz, loglanır), `LocRowOp`. `BindLocNew/RebindLocExisting` grup içinde
  çağrılırsa (RPC batch) dış gruba katılır (`History.InGroup`).
- INSPECTOR (`InspectorPanel.Loc.cs`): kök düzeydeki her string alanın sağında `loc` düğmesi (alt şerit mavi = bağlı). Bağlıyken
  metin kutusu TABLONUN çalışılan-dil hücresini gösterir/yazar (doc prop'u önbellek olarak birlikte), bu dilde boşsa sol kırmızı
  şerit; altında `key` satırı (ayrı tampon, `Rename` ile uygulanır) + yazarken öneri düğmeleri (seçim → `RebindLocExisting`: eski
  satır kullanılmıyor ve ≤1 dilde doluysa düşer). DİĞER DİL HÜCRELERİ INSPECTOR'DA YOK (kullanıcı: kafa karıştırıyor) — pencerede.
  Ölçüm `LocExtraRows` (1 + öneri sayısı) cizimle aynı durumdan. Toggle/Rename/öneri tıklaması frame'i keser (mevcut desen).
- TOOLBAR sağ üst `Working language` combosu (host tablosunun dilleri) → `Localize.Language` (edit projeksiyonu + Play) +
  `EditorSettings.workingLanguage`. Boş = kaynak dil. `LocTableStore.EffectiveLanguage(scope)` inspector'ın yazdığı hücre.
- `Window/Localization` (`LocalizationWindow.cs`): kapsam combosu (Host/modüller), filtre (key + tüm hücreler), dil kutuları, yeni
  key satırı, satırlar: key | uses (açık doc) | dil hücreleri (boş = MISSING) | x. Hücre yazımı `LocCellOp`.
- `Yaml.Parse` UTF-8 BOM'u atar (dış editörle yazılan .asset "bilinmeyen tip" veriyordu).
- Doğrulama: RPC `comp.add LayoutBox` + `prop.set text "Play Now"` → doc `text@loc: login.play_button`, `Assets/Localization.asset`
  oluştu (en=Play Now); TextSprite "Title"/"Hello World" → `login.hello_world`; dış düzenlemeyle `tr` eklenince in-place reload;
  kullanıcı combo/inspector/pencere/undo akışını elden doğruladı. Sandbox Login sahnesi KAYDEDİLMEDİ; `Assets/Localization.asset`
  diskte (2 satır, en+tr).
- SIRADA F3: RPC `loc.*` (set lang, missing, set key/lang/value toplu — AI çeviri, usages/unused proje taraması, glyphCheck) +
  editör GUI RPC'si (combo/düğme tetikleme, panel screenshot — kullanıcı isteği: fare simülasyonu yok), formatter portu
  (`number:short`, `time`, `cap`, `FormatDefaults`), `Loc.g.cs` codegen, inspector placeholder arg önizleme, cihaz dili.

## LOCALIZATION F1: `<alan>@loc` bağı + LocalizationTable + Localize runtime (2026-10-13, [localization] 30 PASS, pak+player OK)
- KARAR (uzun tartışma, kullanıcı): oyunlarda TÜM metinler çevrilir; multilanguage "zahmet" olmaktan çıkmalı. REDDEDİLENLER:
  `LocString` alan tipi (component kodu değişir), `[Localize]` attribute (nick kutusu gibi instance istisnası yok), değer öneki
  `"@key"` (pratik değil), sahne-yerel tablo (her sahneye "Play Now" tekrar), CSV/export (kendi serializer + tablo penceresi + AI
  çeviri), elle `.loc.asset` seçimi (konvansiyon). Modüller host ile tablo PAYLAŞMAZ.
- MODEL: component alanı DÜZ `string` kalır, kod hiçbir şey bilmez (`box.Text = nick` serbest). Localize kararı INSTANCE'a ait ve
  doc'ta alanın yanında prop olarak durur: `text: Play Now` + `text@loc: menu.play`. Alanın doc'taki metni son çalışılan dilin
  ÖNBELLEĞİ; gerçek tabloda. Prop-yanı yaklaşım prefab expand/override diff/collapse/klonla BEDAVA uyumlu (hepsi prop adıyla
  çalışır; `SerializedType.Find` null → schema yolları sessiz atlar, YAML `@` sorunsuz).
- TABLO: `[Asset] LocalizationTable {Languages[] ("en" ilk = KAYNAK/FALLBACK), Entries[{Key, Values[]}]}` (engine/managed/
  Localization/). Kapsam başına TEK dosya, konvansiyon: host `Assets/Localization.asset`, modül `Assets/Modules/<ad>/
  Localization.asset` — `Localize.TableKeyFor(sahneAnahtarı)`. `SceneDoc.LocTable` yalnız bellekte (konumdan türer; YAML'a yazılmaz);
  `SceneDoc.Parse(text, assetKey)` doldurur (EditorScene.Load/ReexpandFrom, AssetPackBuilder.BakeScene, AssetDatabase.MakePrefab,
  SceneLoader, GameHost loose). Tembel indeks (Entries referansı/sayısı değişince yeniden — ReloadObject/pencere uyumlu).
- RUNTIME: `Localize.Bind(comp, field, table, key)` spawn'da alanlar yazıldıktan SONRA (authored = alan metni); `Binding` component
  başına bağlı liste (`Component._loc`) + global liste (swap-remove; `Scene.DestroyComponentNow/DestroyGameObjectNow` → `Unbind`,
  ALC reload'da tip tutulmaz). `Localize.Language` set → `Version++` + tüm bağlar yeniden yazılır (`FieldSchema.Set` + `OnValidate`;
  aynı metinse dokunmaz). `GameObject.Instantiate` → `CopyBindings` (args dahil). Frame maliyeti SIFIR.
- PARAMETRE: şablon tabloda (`{0} coins`); kod key bilmeden `box.Format(args)` / `textSprite.Format(args)` → bağ `Args` saklar, dil
  değişince yeniden formatlar. Play'de args gelmeden şablon GİZLİ (""), `Scene.EditMode` projeksiyonunda görünür. Bağsız `Format` =
  mevcut metin şablon (tek seferlik). `Localize.FormatString`: `{n}`/`{n:fmt}`/`{{ }}`, Regex/CultureInfo YOK (AOT); eksik arg yer
  tutucu kalır. Zengin biçimler (number:short, time, cap — Unity LocalizationFormatter portu) F3.
- BINARY: SceneBinary **v3**: nObj'den önce `i32 locTableStr`, comp gövdesinde alanlardan sonra `u16 nLoc {u16 fieldIdx, i32 keyStr}`;
  `WriteFields` `@loc` prop'larını sessiz atlar (DOBJ'de anlamsız). TrimRoots yalnız başlık okur → etkilenmez. Pak: BakeScene tablo
  dosyası VARSA sahnenin bağımlılığı (kök gerekmez; Spawn'dan önce hazır). Tablo yoksa bağlar tablosuz kurulur → authored metin kalır.
- Testler editor/LocalizationTests.cs: tablo API + YAML round-trip, FormatString, @loc YAML spawn, dil değişimi, fallback,
  Format(args), Capture `@loc` geri yazımı, Instantiate, edit-mode şablon, baked round-trip, unload sonrası bağ sayısı.
- SIRADA F2 (editör): Inspector her string alanı yanında 🌐 toggle (LayoutBox.text/TextSprite.Text varsayılan AÇIK), açıkken yazılan
  metin → tablo satırı (key bağlamdan: `sahne.go_adı[.alan]`, translitere, çakışma `_2`; jenerik GO adı → metin slug'ı), canlı öneri
  listesi (çalışılan dilde, sonra diğer diller; mevcut key seç → aynı satır), key ✎ rename (bağları günceller), diğer dil kutuları,
  placeholder arg önizleme; toolbar Working language combo (edit modunda canlı, Play'de geçerli); tablo dosyasını editör ilk bağda
  kendi oluşturur; LocalizationTable penceresi (kapsam combosu). F3: RPC `loc.*` (AI çeviri), formatter portu, `Loc.g.cs`,
  glyph/missing doğrulama, cihaz dili.
- TUZAK: modül prefab'ı host sahnesine konursa bağ HOST tablosunda aranır (kapsam = sahne) → key yoksa authored kalır; bilinçli sınır.

## PROGRESS ROUNDRECT (perimetre halkası) — TAMAMLANDI (2026-10-13, screenshot doğrulandı)
- `ProgressShape.RoundRect`: yuvarlak köşeli kutunun ÇEVRESİ boyunca dolan halka (sıra sayacı rozeti). Alanlar yeni değil:
  `CornerRadius` (Bar+RoundRect), `Thickness/StartAngle/Clockwise` (Radial+RoundRect). Koordinat = perimetre YAY-UZUNLUĞU
  turu (0 = tepe ortası, saat yönü; StartAngle/360 = tur ofseti) → köşe/kenar hızı eşit. Thickness 0 = dolu kutu (yarım
  dolum = en yakın kenara göre dikey/yatay bölünme). Ghost/edge/segment/border/fx aynen çalışır (USER sözleşmesi Radial ile aynı).
- `RoundRectFragment`: `pr_box` SDF (dış) + `dOut + t` (iç, eş kalınlık → iç köşe r−t) + `pr_perim(p,hs,r)`: 8 parça
  (4 kenar + 4 çeyrek yay) simetriyle: q=|p|−(a,b); köşe bölgesi (q.x>0&&q.y>0): aynı-işaret çeyreklerde ATAN2(q.x,q.y),
  zıt'ta ATAN2(q.y,q.x), taban = a + (zıt ? qc+2b : 0) + (p.x<0 ? H : 0); kenar: q.y>q.x yatay. AA: fa = px/perimetre (analitik).
- **USER.z PAKETLEME:** `±(1 + (köşe_q + kalınlıkOranı·0.999)/1000)`, köşe oranı 3 hane kuantize (0..999). Shader
  `f = fract(|z|)·1000 + 0.0005; cq = floor(f); t = (f−cq−0.0005)/0.999`. TUZAK: float gürültüsü t≈6e-5 → "hayalet ince halka"
  (Thickness 0 görünmez oluyordu) → `t < 0.002 ⇒ 0` ölü bölge. highp float varsayımı (mevcut FWIDTH shader'ları gibi).
- `[ShowIf]` artık ÇOKLU DEĞER alır (`params object[]`, herhangi biri eşleşir): `FieldSchema.ShowIfScalars string[]`.
- Testler: ProgressTests 14 PASS (roundrect shader + katman sayısı/padding kapanması).

## PREFAB STAGE (prefab edit modu) — TAMAMLANDI (2026-10-13, RPC ile uçtan uca doğrulandı)
- MODEL (Unity Prefab Stage): `.prefab` zaten tek köklü SceneDoc → geçici `EditorScene{SingleRoot=true}` olarak açılır; asıl
  sahne `App._stashedScene`'de BELLEKTE bekler (dirty/undo/LiveScene korunur; ExternallyDriven → UpdateAll atlar, render yok).
  `EditorApp._editScene` artık mutable. `lastScene` değişmez. Pencere başlığı/Ctrl+S/undo/Inspector/Scene View bedava çalışır.
- API (EditorApp): `OpenPrefab(abs)` (Play açıksa reddeder), `TryExitPrefabStage(then)` (dirty → `SaveChangesPopup`
  Save/Discard/Cancel; odak kaybı/Esc = Cancel; callback popup kapanırken koşar), `ExitPrefabStageNow`, `InPrefabStage`,
  `PrefabStagePath`, `ReexpandAllPrefabs(rel)`. `OpenScene` stage'deyken önce çıkış ister (ertelenmiş açılış). `ReloadScene`
  dokunulmadı (stage'de prefab'ı yeniden yükler = AssetWatcher .prefab-kendisi yolu). Kod reload stash'li sahneyi de yıkar/kurar.
- EditorScene.SingleRoot: `AddGameObject(0)` kök altına düşer; kök silinemez; köke reparent/move reddedilir (Warning).
  `Collapse()`/`ReexpandFrom(yaml)`/`DiscardChanges()` eklendi; `ReexpandPrefabs = ReexpandFrom(Collapse())`.
- **SIRA KURALI (gizli bug düzeltildi):** prefab değişince önce ESKİ cache ile `Collapse` (diff eski prefab'a göre), SONRA
  `InvalidatePrefab`, SONRA expand. Eski AssetWatcher yolu invalidate→Reexpand sırasıyla çalışıyordu → değişen alanlar
  instance'larda SAHTE OVERRIDE oluyordu (yayılım kırılırdı). Artık `App.ReexpandAllPrefabs(rel)` tek boğaz (stash dahil).
  Bu yüzden Save stage'de cache düşürmez; çıkışta düşer.
- Giriş noktaları: Project paneli `.prefab` çift tık; Inspector prefab başlığında "Open"; Hierarchy üstünde stage şeridi
  (`< Back` + `Prefab: ad[*]`, toolbar 22px aşağı kayar). RPC: `prefab.open(path)`, `prefab.close(save=true)`.
  Play stage'de reddedilir (`PlayMode.Play` guard; ileride izole prefab play düşünülebilir).
- DOĞRULAMA: Main.scene → orb.prefab stage → Height 110→150 + child ekle → close(save) → orb(1)/(2) Height 150 + child
  (yeni localId yeni sceneId aldı); ters yön 150→110 + child sil → instance'lar 110'a döndü (override DEĞİL); kök silme reddedildi.
- KARAR (modül tartışması, aynı gün): modül = oyun projesinde `Assets/Modules/<ad>/` KLASÖRÜ (bundled = klasör, provided =
  projenin geri kalanı; `Modules/` host pak'ta strip'ten korunur). Paket yöneticisi / çok köklü AssetDatabase / tek proje-çok
  oyun REDDEDİLDİ; oyunlar arası paylaşım = klasörü `.meta`'larıyla kopyala. Modül giriş noktası ileride prefab olabilir.
  Editör reload hızı: tek assembly kalır (sınır analizi ileride), ayrı derleme yalnız publish'te.

## GAME VIEW ÜST BARI: boyut combo + yön + Fit/1:1, kalıcı (2026-10-13, görsel + RPC doğrulandı)
- SAHA: RPC `view.device` cihaz profilini EditorSettings'e kalıcı yazıyordu, editörde geri alacak UI yoktu → Game View "sabit
  boyda kalıyor, fit'ten çıkıyor" hissi. Unity Game View barı paritesi eklendi.
- GameViewPanel (BarH=24): `Gui.ComboBox` [Free Aspect | GameOutput.Devices adları] (RPC view.set özel boyut → -1 "(none)");
  yön butonu Auto/Portrait/Landscape döngü (Free Aspect'te soluk ama ÇİZİLİR — kontrol sayısı sabit); Fit/1:1 butonu (1:1 =
  mantıksal birebir, `GuiClip.Push(vis)` ile kırpılır — Push orijini taşır, dst vis-lokal verilir); sağda `WxH @Nx` etiketi.
  İçerik/pointer alanı `vis = full - bar`.
- GameOutput: `Orientation` (ScreenOrientation; Auto = PlayerSettings.orientation, o da Auto = preset doğal yönü) + `OneToOne`
  + `ApplyDevice(in Device) → landscape` (swap mantığı RPC'den buraya taşındı; combo + RPC aynı boğaz). RPC `view.device
  orientation=` artık TERCİH olarak kalır (eski tek-seferlik davranış değişti). EditorSettings: gameViewOrientation/gameViewOneToOne
  (cihaz alanları zaten vardı); EditorApp yükleme/SaveSettings.
- Doğrulandı: combo Free Aspect → view.get mode=panel; yön Portrait → 390x844; UserSettings/EditorSettings.asset alanlar yazıldı.

## ADD COMPONENT POPUP: [AddComponentMenu] + EditorPopup tabanı (2026-10-13, testler yeşil, görsel OK)
- Engine `[AddComponentMenu("Rendering/Sprite Renderer")]` (Attributes.cs, Unity paritesi, Inherited=false): yol = grup/etiket;
  isaretsiz → **"Scripts"** grubu (oyun kodu), `("")` = menüden gizli. Builtin'ler etiketlendi (Rendering/Layout/Camera/Audio/
  Animation/Effects); test/demo component'leri (LifecycleTests Recorder..., AnimTarget, Handler, SerTestComp, StressOrb, Spinner) gizli.
  Editör tip-özel hiçbir şey bilmez — grup/sıra attribute'tan.
- editor/EditorPopup.cs (abstract, instance): imleç yanında dekorasyonsuz floating native pencere (ColorPicker/GuiPopup deseni),
  workarea clamp, focus kaybı/Esc/ShouldClose = Close, tek aktif popup. EditorApp kancaları: `UpdateActive` (ColorPicker.UpdateWindow
  yanı), `EncodeActive`, `PresentActive`. Alt sınıf `OnGui(ev)` + `OnClosing` (kendi Hot/KeyboardControl'ünü bırakır — global state).
- editor/AddComponentPopup.cs (260×380): tepede filtre TextField (açılışta odak; Enter/Up/Down TextField'da tüketilmediği için
  popup gezinmesi burada), filtre boşsa grup başlıkları + alfabetik öğeler ("Scripts" sonda), doluysa düz eşleşen liste (etiket + tip
  adı substring). Tık/Enter → `EditorScene.AddComponent` (undo bedava) + kapan. Katalog listesi her açılışta kurulur (ALC reload güvenli).
- Inspector inline tip listesi + `_addingComp` SİLİNDİ; buton `AddComponentPopup.Show(g.Id)`.
- KALAN: ColorPickerWindow/GuiPopup'ı EditorPopup tabanına taşımak (ayrı iş); Project paneli Create menüsü için aynı popup.

## KAMERA PROJEKSİYONU = KARDEŞ COMPONENT (genişletilebilir) + SceneView Persp (2026-10-13, testler yeşil, yuzbirextra görsel OK)
- SAHA: yuzbirextra `CameraScaler` (oyun kodu, Update'te perspektif kamerayı referans çözünürlük mesafesine koyuyordu) → editör
  Update koşturmadığından edit/oyun arasında uçurum; SceneView çerçevesi `(0,0,ViewW,ViewH)` sabitti (PixelPerfect varsayımı).
- KARAR: `CameraProjection` enum + orthoSize/fov/near/far **CameraComponent'ten SİLİNDİ**. Projeksiyon = kamera GO'suna takılan
  `abstract CameraProjection : Component` ([ExecuteInEditMode]; engine/managed/CameraProjection.cs). Sözleşme: `GetWorldRect(camPos,
  viewW,viewH)` (z=0 kesiti) + `Apply(Camera,camPos,viewW,viewH)` + virtual `ScreenPointToRay`. Builtin: `PixelPerfectProjection`,
  `OrthographicProjection{orthoSize}`, `PerspectiveProjection{fov,nearClip,farClip; protected virtual Distance(camPos,aspect)}`,
  `FitPerspectiveProjection : Perspective {referenceResolution}` (cover: transform z YOK SAYILIR). Oyun kodu türeterek yeni tip ekler
  (component zaten polimorfik; [SerializeReference] gerekmedi). Projeksiyon yoksa CameraComponent PixelPerfect fallback.
- KAYIT (frame'de GetComponent YOK): `CameraProjection.OnEnable/OnDisable → CameraComponent.Attach/Detach`; CameraComponent.OnEnable
  bir kez GetComponent (sıra bağımsız). İkinci projeksiyon → Console uyarı, ilki kalır. `CameraComponent.Projection` okunur.
- Editör: SceneView çerçeve + Fit → `MainCamera.GetWorldRect` (kamera yoksa eski (0,0,View)). Toolbar **Persp** toggle: EditorCam'a
  `MainCamera.ApplyTo(panelW,panelH)` basılır (arka plan dahil) — salt önizleme, pan/zoom/gizmo/seçim/çerçeve kapalı (OnGui erken
  çıkar; mod değişimi yalnız buton tıkında = IMGUI id kuralı korunur). Editör projeksiyon TİPİNİ bilmez.
- Migrasyon: yuzbirextra LoginScene RPC edit.batch ile (CameraScaler remove + FitPerspectiveProjection fov 30 / 1440 960; undo'lu,
  KAYDEDİLMEDİ). CameraScaler.cs silindi. Eski `projection:/fov:` doc prop'ları sessiz düşer (prop.set şema dışı alanı silemez —
  Save'de temizlenir mi bak). **Sandbox Login.scene** hâlâ eski `projection: Perspective` taşıyor → `PerspectiveProjection` eklenmeli
  (PixelPerfect'e düşer). PointerTests persp dilimi component'li (24 PASS).
- KALAN: projeksiyon-özel editör çizimi (frustum gizmo) için genel [DrawGizmo] dilimi; Persp modunda etkileşim yok (bilinçli).

## AOT KİMLİK = YAPISAL; isim yalnız emisyon (CoreCLR/IL2CPP modeli; 2026-10-13, selftest 80/82 (2 eski), yuzbirextra+Common.dll exe OK)
- Belirti: `class EncodedSerializer : Serializer<Encoded> { override ShouldEncode(Encoded) }` → "override edilecek virtual yok":
  vtable slotu MANGLED İSİMLE eşleniyordu; generic örnek klonları şablon adını (`ShouldEncode_T`) taşırken türeyen somut sınıf
  `ShouldEncode_M_Encoded` üretiyordu. Matristeki `Derived<T>:Base<T>` yalnız iki tarafın da `T` harfini kullandığı için geçiyordu —
  imzasında T taşıyan sanal üye hiç test edilmemişti. Aynı kök: `Apply`/`PointerOf` intern'siz → `SameType` ad karşılaştırıyordu;
  `Context.Codes` string anahtarlı; `FindCode` 3 mekanizma (TryGetCode / MangleCandidates kartezyen / FindGenericMethod).
- KARAR (tek seferde, geçişsiz): .NET `MethodTableBuilder`/IL2CPP kuralı — kimlik yapısal, mangled ad yalnız C sembolü/trace çıktısı.
  * Tip: `Context.Definitions` yalnız CLR tam ad → TANIM (metadata zaten isimle gelir; alias burada). Generic uygulama
    `ctx.Apply(def,args)` yapısal intern (ApplyKey); kapalı uygulama somutlanınca AYNI nesne yerinde örneğe döner
    (`InstantiatedFrom` set, `GenericTemplate` null) → önceki referanslar geçerli kalır. `PointerOf/FixedArrayOf` dizi gibi intern.
    Tip eşitliği her yerde `==`. `Primitive.Definition` = `GenericTemplate ?? InstantiatedFrom ?? this` ("(x.GenericTemplate ?? x)"
    kalıbının tek doğru hali — somut örnekte eski kalıp kendisini döndürüp yanlış sonuç verirdi).
  * Code: `SimpleName` (metadata adı) kaynak; `Name` TÜRETİLMİŞ get-only (simple + param tip adları; klon = şablon adı
    [+`<margs>`] → `Ov<int>.Foo(T)/Foo(int)` sembolleri `Foo_T`/`Foo_Int` ayrı kalır; corelib byref `out_/ref_` sözleşmesi
    `ByrefNamedMangle` + `Argument.IsOutParam` flag'inden — corelib.c sembolleri ve host/modül Fnv64 hash'leri DEĞİŞMEDİ).
    Kayıt anahtarı `CodeKey(owner, RootTemplate, methodTypeArgs)`; `ctx.MembersOf(owner)` indeksi; `ctx.FindMember(owner,
    simple, paramCount)` yalnız ctor/cctor/finalize/get_X gibi metadata adı sorguları için. Property `Getter/Setter` Code referansı
    frontend'de bağlanır (`ReflectionAccessor(model,"set_X_Type")` string araması silindi).
  * `Signature.cs` tek yer: `Matches` (ad + arity + param tipleri `==`, generic metot parametreleri konumsal; DÖNÜŞ TİPİ HARİÇ —
    .NET override kuralı, covariant return serbest), `MatchesCall`/`Unify` (MemberRef çözümü; generic metotta arity + unify yeter,
    T yalnız dönüşte olabilir: `Array.Empty<T>()`). `Hierarchy`: override → parent vtable'da imza eşleşen İLK slot; explicit iface
    impl'leri slot eşlemesine girmez; newslot her zaman yeni slot (`Ov<int>.Foo(T)/Foo(int)` çakışması .NET'te de geçerli — eski
    "virtual slot zaten var" hatası kaldırıldı). GENERIC SANAL METOT: şablon slot açmaz; her (metot, margs) örneklemesi ayrı slot,
    türeyen override'ın aynı margs örneği Hierarchy'de üretilir (IL2CPP generic virtual method table karşılığı) → Resolver sabit
    noktası Hierarchy/Concretize yeni kod kaydettiyse tekrar döner. CallVirtual slot = `VTable.IndexOf(op.Code)`.
  * Silinen miras (MiniCs dönemi, hiçbir yerden çağrılmıyordu): ACode/APrimitive/OpAsm/IteratorLowering/TextIO, `Code/Primitive
    Encode/Decode`, `Unresolved` placeholder çözümü (Resolver.Register/ResolvePrimitive/ResolveCode).
- TESTLER: `Cases/Test63.cs` (case 74–83): somut argümanlı taban (`IntSer : Ser<int>`), farklı harf `D<U>:B<U>`, 3 seviye
  `Base<T>→Mid<T>→Leaf:Mid<string>` + `base.`, `override Mix(ref T, out T)`, generic sınıfta generic metot override `Pair<U>(T,U)`,
  `Foo(T)/Foo(int)` çakışması, `IConv<int>` explicit/generic/miras impl, `virtual T Value` + reflection SetValue, `abstract T Make()`,
  comparer/Format (83). Sandbox windows exe çalışır, wasm lean linkler, editor build OK. Yan bulgu düzeltildi: sanal trampolin
  (`de_tramp_v_*`) `DeSlot a[]`/`r` yerelleri C# param adı `a`/`b` ile çakışıyordu → CArgNames'e rezerve.
- CORELIB EKLERİ (Digitoy.Common.dll'in istedikleri): `IEqualityComparer<T>`, `EqualityComparer<T>.Default` (örnekleme başına
  singleton, object.Equals/GetHashCode varsayılanı), `StringComparer : IEqualityComparer<string>`, `Dictionary/HashSet(comparer)`
  ctor'ları (`comparer == null` yolu eski sıfır-alloc davranışı birebir; `HashOf/Eq` yardımcıları), `IFormatProvider`,
  `string.Format(object…)/(IFormatProvider, string, object[])` (`{i[,align][:fmt]}`; align uygulanır, fmt/provider yok sayılır —
  Roslyn anonim tip/record ToString'i bunu üretir), `FormatException/ArgumentException/ArgumentNullException`, `Math.Round(double)`
  (C `rint` = ToEven), `Span<T>.Fill/Clear` saf C# (indexer intrinsic'i üzerinden; extern'di ama frontend lowering'i yoktu → link hatası).
  Emisyon düzeltmesi: tek-dosya `TranspileProgram` ileri `struct X;` bildirimlerini paylaşımlı header'dan SONRA basıyordu → String
  prototipi `struct System_IFormatProvider*` görünce tag prototip kapsamında kalıp "incompatible pointer types" veriyordu; sıra
  düzeltildi (çok-dosya yolu zaten doğruydu). yuzbirextra + Digitoy.Common.dll → `yuzbirextra.exe` (9045 KB) linkliyor.
- TUZAK (bu oturumda yaşandı): PowerShell 5 `Get-Content`/`Set-Content -Encoding UTF8` BOM'suz UTF-8 dosyayı ANSI okuyup BOM'lu
  yazar → "—" gibi karakterler `â€”` olur, BOM eklenir. Toplu metin değişiminde `[IO.File]::ReadAllBytes` + `UTF8Encoding(false)`
  kullan; sonrasında `git diff` ile non-ASCII satırları kontrol et. Ayrıca çok satırlı `.Replace()` CRLF dosyada sessizce tutmaz.
- STUB KAPISI KÖKLERİ (2026-10-13): kapı yalnız 6 host girişinden yürüyordu; cctor'lar op'la çağrılmadığı (EmitInit doğrudan) için stub
  cctor (`M.CodecTypes.cctor` → ConcurrentDictionary yok) build'de görünmeyip runtime'da patlıyordu. Şimdi kapı trim planından SONRA
  koşar, kökler = host girişleri + `CTranspiler.InitRoots` (EmitInit ile aynı kaynak: canlı eager cctor'lar + finalizer'lar +
  exception-kind ctor'ları). Yol raporunda `<cctor: EmitInit eager>`. Lean'de ölü cctor hata değil.
- CORELIB: `System.Collections.Concurrent.ConcurrentDictionary<K,V>` (Concurrent.cs) — TEK THREAD sarmalayıcı (kilit yok), yüzey .NET
  ile birebir. DERS: corelib imzaları DÖNÜŞ TİPİ DAHİL .NET'le aynı olmalı — kullanıcı DLL'i gerçek .NET'e karşı derlenir; `GetEnumerator`
  struct dönünce IL arayüz bekleyip kutuladı → InvalidCast (case 84 yakaladı). Frontend'de ad-remap/intrinsic YAPILMADI (kimlik = tip).
  Regex: corelib'deki `Regex` gerçek motor değil (statik prefix/suffix) → `new Regex(p, RegexOptions)` (M.NickValidator.cctor) kapıda
  kırmızı — bilinçli; çözüm Common'da regex'siz doğrulama ya da ayrı planla sınırlı motor.
- TRIM: LEAN'DE CCTOR KÖKÜ = DOKUNULAN TİP (2026-10-13). Eskiden TÜM cctor'lar kök + EmitInit hepsini eager çağırıyordu → referanssız
  plugin (Digitoy.Common) lean'de bile canlı kalıyor, cctor'ları koşuyordu. Şimdi [TrimAnalysis.Reach](../aotcompiler/source/Trim/TrimAnalysis.cs)
  `Touch(tip)`: üyesi çağrılan / statik alanına erişilen / `New`'lenen / reflection facet'li (Ctors|Methods|Props|Fields) tipin cctor'u
  canlı; EmitInit zaten yalnız canlı cctor'ları basar (IsEmittableCode→IsCodeLive), StaticDeps sırası aynen. `full` (modül host)
  eager kalır: dmod kodu host'un her statiğine dokunabilir. Sonuç: yuzbirextra lean 9045→4065 KB, `M.*` 1772 kod ölü, 0 canlı
  (yalnız 2 attribute tipi reflection facet'i — CollectAutoRoots her kullanılan attribute tipini kök sayar, konservatif); Sandbox
  lean 3019 KB çalışır, yuzbirextra lean Login sahnesine kadar çalışır. Lazy cctor (.NET beforefieldinit, A seçeneği) bilinçli
  ertelendi: her statik erişime dal + descriptor'a ensure gerekir; ihtiyaç doğarsa plan engine-notes'ta (bu girdi).

## AOT: DİZİ = BİRİNCİ SINIF TİP (kanonik T[] descriptor) + Activator struct (2026-10-12, selftest 70/72 (2 eski), yuzbir AOT full+lean boot OK)
- SORUN: AOT alan metasında dizi alanlarının tipi `0` idi (`ReflectionTypeSym` Array → "0") → `FieldInfo.FieldType == null`
  → `SerializedType.TryKind(null)` NRE (Font.Glyphs ilk dizi alanlı pak tipi). Kök sebep: dizi IR'de ayrı Primitive ama
  kanonik değil (`ArrayOf` her çağrıda yeni), descriptor tembel (typeof(T[]) ya da runtime sentez), tahsis tipi ayrı
  (`vmarray_ref/val_type` + ref taşıyan struct için üretilen `arr_X_type`). .NET NativeAOT/IL2CPP modeli: T[] = eleman+rank'tan
  türeyen kanonik tip, kimlik = tahsis.
- ÇÖZÜM: `Primitive.ArrayOf` INTERN ((elem,rank) → tek Primitive, `InternedArrays`, DisplayName "int[]"). CTranspiler ön geçişi
  elemanı descriptor'lu+canlı her kanonik diziye descriptor üretir (`RegisterArray`; typeof(T[]) hedefi zorunlu; jagged derinlik
  sırasıyla). Descriptor = kimlik + TAHSİS tipi (`gc.type == typeof(T[])`): `{trace=digitoyengine_trace_array, finalize_vmarray,
  atomic (ref'siz değer elemanda 1), base System.Array, name, flags ARRAY, elem_type, rank, hash}`. `NewArray` tahsisi bu
  descriptor'ı kullanır; `ReflectionTypeSym/ExportTypeSym/GenericArgSym` dizi için `&T_arr_type`.
- TEK TRACE (kullanıcı önerisi): `digitoyengine_trace_array` eleman türüne bakar — ref → shade; struct → `elem_type->trace_inline`;
  atomik → yok. Struct descriptor'ına `trace_inline(void* payload)` eklendi (Type APPEND-ONLY alan); kutu trace'i onu sarar.
  Tip başına `arr_X_type`/`EmitStructArrayType`/`array_of` SİLİNDİ. Sentez descriptor (`digitoyengine_array_type`) da tahsis
  edilebilir (trace/finalize dolu) → `Array.CreateInstance` kanonik tipi kullanır. `vmarray_ref/val_type` yalnız corelib.c ham
  `vmarray_new` + interp yedeği; `GetType` ARRAY bayraklı tipi doğrudan döner.
- Corelib: `Activator.CreateInstance(Type)` ad aramasıyla değil handle ile (`CreateInstanceOf`); struct/primitive → kutulanmış
  default(T) (`List<Page>.Add(object)` null'dan NRE veriyordu — serializer `NewElement` struct liste elemanı).
- Trim: `ResolveComponentRoots` pak'taki `[Serializable]` DOBJ tipleri (Font, SpriteTable, oyun [Asset]) `Inline` facet'le kök sayar
  (yalnız Component'ti). Dizi descriptor'ı facet'li tiplerin alan/kod dizileri için; ekstra kırpma gevşemesi yok.
- Dizi arayüz haritası (`IList<T>`/`IEnumerable<T>` cast) HENÜZ YOK — kanonik tip altyapısı hazır, ayrı iş.

## PAK GÖRSELLER KODLANMIŞ (PNG/JPG), DTEX/DPIX SİLİNDİ, R8 SDF YOLU KALKTI (2026-10-12, 15 grup PASS, pak/player OK)
- SEBEP: pak her dokuyu ham RGBA(+filtre)+zlib yazıyordu; 57 KB jpg → 518 KB. Artık görseller KAYNAK KODLAMASIYLA
  girer; runtime stb_image native worker'da async çözer (zaten vardı). `DtexFormat`, `PixelBlob`, `de_try_dtex`,
  `MakePageTexture` SİLİNDİ. Kullanıcı kararı: ham doku yolu yok, "ha atlas ha png — fark yok".
- `ImageImporter` (.png/.jpg/.jpeg, v1): `format Auto|Png|Jpg` (Auto = bayt-eşit kopya), `jpgQuality`. main Standalone
  (atlas yutmuşsa pak'a girmez → builder guid stub'ı). Editörde `LoadTexture` artifact DOSYASINDAN decode eder
  (`AssetDatabase.ArtifactPathResolver` → `ImportPipeline.ResolveArtifactPath`). Watcher png değişince önce reimport.
- Atlas sayfası / font sheet = düz RGBA8 PNG, `LoadTexture(owner#pageN, w, h)` ile diğer png'lerle AYNI async yoldan
  (`Texture.CreatePending(expectedW,H)`: UV'ler pikseller inmeden doğru). Preload `LoadNodeAsync` bayt İÇERİĞİNE bakar
  (PNG/JPG magic → doku). Sayfalar flip'li yazılır: stbi yükleme flip'i bellek düzenini birebir geri getirir →
  SpriteTable koordinatları değişmez. `AtlasFormat`/`Channels` kalktı; sayfa hep RGBA8.
- R8 SDF font modu SİLİNDİ (kullanıcı: MSDF kadar net değil): `FontSdfMode` yok, FontImporter yalnız MTSDF (v12).
  `UiPieces`: TEK shader (`Fragment`, median/alpha), `ShaderFor/UiShaderMsdf/SdfFragment` silindi, fallback dokusu RGBA.
  GUI (editör) R8 raster/SDF fontu ayrı yol, dokunulmadı.
- Native: `de_fs_decode` kanalı korur (gray PNG → R8, `len==w*h` ile anlaşılır; `Texture._AttachPixels(..., ch)`).
  `de_image_write.c` (stb_image_write, editör-only) `de_image_encode` yalnız JPG için; PNG = `editor/PngWriter.cs`
  (adaptif filtre + .NET ZLibStream SmallestSize — stb'nin deflate'i LZ77 yapmıyor, sayfa 970 KB çıkıyordu → 714 KB).
  Screenshot `WriteRgb` de aynı sınıfa taşındı.
- SONUÇ (Sandbox): 1119 → 1035 KB. Görseller 650 KB → 290 KB ✔; atlas sayfası 435 → 714 KB ✘ — içerik değişti: iki font
  MSDF (3 farklı kanal, R8-kopyalı eski sayfadan az sıkışır). Küçültme: `sdfSize` / charset / maxSize. Yuzbir yeniden
  build edilmedi (beklenti ~1.58 MB → ~600 KB).
- AOT: `Buffer.BlockCopy` corelib'de YOK → release yolunda `Array.Copy` (SceneBinary Reader.Blob). `tools/pakls.ps1`: pak
  içerik dökümü (boyuta göre).

## ATLASGROUP.sources: ISpriteSource + ARAYÜZ ALANI = POLİMORFİK ASSET REFERANSI (2026-10-12, objtest 34 PASS, Sandbox atlas 643 bölge/11 üye)
- SORUN (kullanıcı): AtlasGroup hâlâ `List<Font> fonts` istiyordu; importer ise tipi bilmeden yalnız `sprites` tablosunu
  okuyordu (Spine vb. için jenerik). Font artık IAsset değil `[Asset]` saf veri → `List<IAsset>` Font'u kabul etmezdi.
- KARAR: `ISpriteSource` marker arayüzü (engine Sprite.cs) = "importer'ı `sprites` SpriteTable + sayfa piksel artifact'i
  yayar" sözleşmesi; Font uygular. `AtlasGroup.sources : List<ISpriteSource>` (`[FormerlySerializedAs("fonts")]`;
  ReadGroup "sources" ?? "fonts"). Sandbox/yuzbirextra .asset'leri `sources:`a çevrildi.
- GENEL KURAL: `AssetDatabase.IsAssetType` artık HER arayüz tipini Kind.Asset sayar (IAsset özel-case kalktı);
  `LoadAsset(key, arayüz)` somut tipi yoldan (`ImportTypeOf ?? AssetClassOf`) çözer, arayüzü taşımıyorsa null (bozuk
  referans sessiz boş). Drag-drop `IsAssignable` zaten `fieldType.IsAssignableFrom(concrete)`.
- Tuzak kapatıldı: `AssetClassOf` editörde diskte olmayan kaynak (bellek/test) için yalnız DOBJ deniyordu; YAML baytından
  da `type:` okur. ObjectBinaryTests `IObjTestTag`/`Tagged` listesi: şema Kind.Asset, YAML+DOBJ çözüm, uyumsuz → null.

## MSDF C# PORTU (tools/MsdfPort) — Unity importer için native'siz MTSDF (2026-10-12, 4 font × 95..548 glyph BAYT-AYNI)
- AMAÇ: LayoutBox'ın "metin + kutu aynı atlas/tek draw" modelini Unity'ye taşırken (`UIBox`) font sheet'ini Unity editöründe
  native DLL'siz üretmek. Kullanıcı kararı: engine native'e DOKUNMA; msdfgen + stb_truetype'ın gereken kısmını C#'a port et,
  mevcut `digitoyengine_native.dll` yalnız REFERANS olarak P/Invoke ile okunur.
- Kaynak: engine/native/msdfgen (1.12) C++'ı satır satır — kullanıcının eski Haxe portu (daha eski msdfgen) KULLANILMADI:
  `normalize` deconverge (convergent-curve-ordering + DECONVERGE_OVERSHOOT), `edgeColoringSimple` (initColor/seed, renk
  konturlar arası taşınır), selector EdgeCache relevance kısa devresi, `pixelFloatToByte = ~int(255.5-255x)` farklı.
- Dosyalar: `Msdf/Core.cs` (Vector2/Arith/SignedDistance/EquationSolver/Projection/DistanceMapping), `Msdf/EdgeSegments.cs`,
  `Msdf/Shape.cs` (Contour/Shape/ConvergentCurveOrdering/EdgeColoring), `Msdf/Selectors.cs` (PerpendicularBase,
  MultiAndTrue, OverlappingContourCombiner, ShapeDistanceFinder, Generator), `Msdf/ErrorCorrection.cs` (EDGE_PRIORITY +
  CHECK_DISTANCE_AT_EDGE tam), `TrueType.cs` (stbtt alt kümesi: cmap 0/4/6/12/13, glyf shape + bileşik, bbox, hmtx/hhea,
  kern fmt0 + GPOS PairPos 1/2; CFF/OTF YOK), `MsdfFontBaker.cs` (= de_msdf_font_glyph/de_sdf_font_metrics/kern_table
  sözleşmesi), `NativeReference.cs`, `Program.cs` (bayt-diff).
- PARİTE: `MsdfPort <font.ttf> [--size N] [--charset ...] [--dll ...] [--dump dir] [--no-ref]`. NotoSans-Black, arial,
  Digitoy-Bold (64), Digitoy-Primary (48, Latin Ext-B dahil 548 glyph): tüm glyph bitmap'leri, out5, metrikler ve kern
  tabloları BAYT-AYNI. Hız: port ≈ native × 1.3–1.5 (yeterli, editör-only).
- Bayt-eşitlik için dikkat edilenler: tüm double math sırası C++ ile aynı (FMA yok: clang baseline x64 + RyuJIT); float texel
  tipleri EC'de korundu (`mix<float,double>` → float cast, `fabsf`); stbtt kontur başlangıç/kapatma mantığı birebir (edge
  coloring başlangıç kenarına bağlı); bileşik glyph dönüşümü float→short kırpma; generator/EC serpantin x yönü.
- SIRADA: Unity `.msdf` ScriptedImporter bu kodu (Editor asm) kullanır; UiPieces bandı + sheet paketleme + DigitoyFont SO.
- UNITY PAKETİ `unity/Box` (com.digitoygames.uibox, 2026-10-12, ADIM 1 = editör-only importer): port kaynağı ARTIK
  PAKETTE (`Editor/Msdf/*.cs`, `Editor/Font/TrueType.cs|MsdfFontBaker.cs`), tools/MsdfPort bunları `<Compile Link>` ile
  derler (tek kaynak; parite hâlâ 95/95). Namespace `Digitoy.Box` / `Digitoy.Box.Editor` (core.upm `Digitoy.UI.UIBox` ile
  çakışmasın), asmdef `Digitoy.Box.Runtime` + `Digitoy.Box.Editor` (Editor-only). Blok namespace + C# 9 (Unity 2021.3
  uyumu; 2022.3 UnityEngine/UnityEditor.dll'e karşı LangVersion 9 derleme kontrolü geçti).
  * Runtime: `UIFont` (SO: metrikler, Glyph[] {codepoint, advance, xOff, yOff, w, h, inkL, inkR, x, y}, short[] kern,
    sheet Texture2D, piecesX/Y; rect'ler SHEET px SATIR 0 ÜSTTE, `UvRect` çevirir; `SdfSpread=21`), `UIPieces`
    (sabitler + `BakeRegion(Color32[])` 4 kanal eşit → median==a).
  * Editor: `UIFontImporter` `[ScriptedImporter(1,"msdf")]` — `.msdf` = uzantısı değişmiş TTF (Unity .ttf'yi bırakmaz);
    ayarlar charset/sdfSize; glyph MTSDF bake → `SheetPacker` (shelf, pow2 ≤4096, glyph pad 1, pieces margin 4+pad 2) →
    RGBA32 linear/uncompressed/no-mip/bilinear/clamp sub-asset Texture2D (non-readable) + UIFont main. `UIFontMenu`:
    Assets▸Create▸Digitoy▸"UIBox Font (.msdf) from TTF". CFF/OTF import hatası verir (TTF gerekli).
  * Projeye ekleme: manifest `"com.digitoygames.uibox": "file:<yol>/game_rd/unity/Box"`.
  * SIRADA (adım 2): uGUI SDF shader (USER=uv1) + minimal Graphic ile "Ag"+köşe parçası tek draw kanıtı; sonra UIBox layout.
- ADIM 2 YAPILDI (2026-10-12, Unity 6 LoginScene'de DOĞRULANDI: radius 40 kutu + kontur/gölgeli MTSDF metin TEK draw):
  * Yaşanan tuzaklar: (1) importer'da `Texture2D.Apply(false, makeNoLongerReadable:true)` → serileşen sheet BOŞ
    (readable kalmalı); (2) Graphic alt sınıfı `[RequireComponent(typeof(CanvasRenderer))]` ister (Image gibi) yoksa
    sessizce çizmez; (3) asmdef `UnityEngine.UI` referansı şart; (4) TextArea `\r` koyar → fallback '?' (kontrol
    karakterleri atlanır, `IsSkipped`). Varsayılan textColor siyah (fill beyaz üstünde okunsun).
  * `Runtime/Shaders/UIBoxSdf.shader` "Digitoy/UIBox SDF" = UI/Default iskeleti (stencil/RectMask2D/alphaclip) + engine
    `MsdfFragment` birebir; USER = TEXCOORD1 (float4). `fwidth(d)` dallanma DIŞINDA hesaplanır. Canvas
    `additionalShaderChannels |= TexCoord1` → `UIBoxMaterials.EnsureCanvasChannels` OnEnable/CanvasHierarchyChanged'de.
  * `UIBoxMaterials`: sheet başına tek paylaşımlı materyal (dictionary), font'suz kutular için 512×128 fallback pieces
    dokusu (`UIPieces.BakeRegion`), `Editor/UIBoxBuild.cs` shader'ı Always Included'a ekler (Shader.Find build'de bulsun).
  * `UIBox.cs` (stil alanları: radius4/border4/borderColor/padding4/shadow*/innerShadow*; `Graphic.color` = fill, a=0 →
    yok) + `UIBox.Draw.cs` (EmitQuad/EmitLayer/EmitShadow/SolidInterior — engine LayoutBox portu; kutu uzayı y-DOWN,
    EmitQuad Unity'ye çevirir; UV `CV = 1 - row/H`; üst kenar v1/alt kenar v0 sözleşmesi korunur) + `UIBox.Text.cs`
    (LayoutBox.Text portu: NextLine/FitCount/BuildTextQuads/EmitRun, wrap/ellipsis/hiza/weight/spacing/outline/shadow;
    `TextPreferredWidth/Height` adım 3 ölçümü için hazır). Çizim sırası: gölge → border ring → fill → iç gölge → metin.
  * `mainTexture`/`defaultMaterial` override → font sheet (yoksa fallback). Gradient/sprite(9-slice)/radial henüz YOK
    (`FillAt/BorderAt` kancaları düz renk döner).
  * Derleme kontrolü aracı repoya alındı: tools/UnityCheck/UnityCheck.csproj (2022.3 modül dll'leri + okeyextra
    Library'den UnityEngine.UI.dll, LangVersion 9) — `dotnet build` ile Unity açmadan sözdizimi/API doğrulaması.
  * SIRADA (adım 3): layout yarısı — RectTransform sürme (DrivenRectTransformTracker), flex/anchor/grow/wrap/justify,
    lazy pull, Canvas.willRenderCanvases sweep; UIBox.Layout.cs olarak aynı sınıfa.
- ADIM 3 KODLANDI (2026-10-12, Unity doğrulaması KULLANICIDA): `UIBox.Layout.cs` — LayoutBox yerleşim portu.
  * Kaynak = component alanları (`width/height` authored, `grow`, `ignoreLayout`, `layout`, `spacing`, `alignChildren`,
    `justifyChildren`, `reverse`, `overflowX/Y`); RectTransform PROJEKSİYON: `sizeDelta` HEP sürülür (nokta anchor →
    preferred, stretch → authored delta); grup dizen çocukta anchor (0,1) + anchoredPosition da sürülür
    (`DrivenRectTransformTracker` — struct, readonly alan OLMAZ). Pivot/anchor/pozisyon dizilmeyen kutuda Unity'nin.
  * Çocuk konumu parent rect sol-üstüne göre y-down: `anchoredPosition = (x + pivot.x*w, -(y + pivot.y*h))`.
  * Lazy pull `RectWidth/RectHeight`; sweep `Canvas.preWillRenderCanvases` (CanvasUpdateRegistry'den ÖNCE koşsun diye
    willRender değil). `OnRectTransformDimensionsChange` → MarkDirty, kendi yazımlarımız `s_writing` ile yutulur.
  * Hidden → aynı GO'ya `RectMask2D` (kullanıcı kararı; kendi metni kırpılmaz, çocuklar kırpılır). `ILayoutElement`
    dışa preferred bildirir (uGUI LayoutGroup içine konabilir). fitScreen YOK (Canvas kökü stretch anchor).
  * Inspector [Header] gruplandı. Testler henüz yok (engine LayoutTests'in 12 senaryosu EditMode testine port edilecek).
- IMPORTER MODELİ DEĞİŞTİ → `UIBoxAtlas` (2026-10-12, kullanıcı kararı: "1 ttf = 1 msdf" yerine PROJE-TEK atlas ayarı):
  `.msdf` importer + menü SİLİNDİ. `Editor/Font/UIBoxAtlasImporter.cs` `[ScriptedImporter(1,"uiboxatlas")]` — dosya boş
  işaretçi, ayarlar importer'da: `fonts[] {Font ttf, name, sdfSize, charset}`, `maxPageSize`. Çıktı: `UIBoxAtlas` SO (ana)
  + `PageN` Texture2D'ler + `UIFont` sub-asset'leri (`UIFont.page` eklendi). Paketleme: font = BÖLÜNMEZ birim (kutu+metin
  tek doku); büyükten küçüğe, mevcut sayfalara `TryAddToPage` (mevcut+yeni birlikte yeniden paketlenir), sığmazsa yeni
  sayfa; her sayfada UiPieces bandı. `DependsOnSourceAsset(ttf)` → TTF değişince otomatik rebake. Menü:
  Assets▸Create▸Digitoy▸"UIBox Atlas". Görseller (sprite) aynı atlasa SONRAKİ adımda eklenecek.
- MSDF KONTUR ÇENTİK TEŞHİSİ (2026-10-12): Unity'de metin konturu çentikli görünüyordu; kullanıcı importer'dan şüphelendi.
  Teşhis: port A kanalı/kontur PNG dökümü (tools/MsdfPort `--png`) pürüzsüz; UIBox `Text Debug` (Alpha/Median ham rampa)
  Unity'de A + USER transportunu doğruladı. Gerçek sebep: MSDF median'ın MENZİL DIŞI artifact'ları (köşe uzantılarının
  kesişimi, harf dışında median>0.5 "X" bantları) — fill pass'i median ile konturun ÜSTÜNE çizildiğinden konturu ısırıyor.
  Engine'de de aynı veri; arial/21px'te fark edilmemiş. ÇÖZÜM (UIBoxSdf.shader): |a-0.5| > 0.206 (~2.5 SDF px) ise
  median yerine gerçek SDF (.a) kullan — köşe keskinliği korunur, uzak artifact'lar yok olur. UiPieces bölgeleri 4 kanal
  eşit → etkilenmez. NOT: aynı düzeltme engine `UiPieces.MsdfFragment`'a da uygulanmalı (managed shader string; sonra).
  Teşhis alanı `textDebug` UIBox'ta kaldı (Off varsayılan).
- RADIAL + LIGHT (2026-10-13): `UIFillType.Radial` (gradient 0 = merkez, 1 = kenar; tek lineer rampa, ara duraklar yok;
  yalnız `fill`'de anlamlı, diğer katmanlar kenar rengini kullanır) → taban katman kenar rengiyle düz + `EmitRadial`
  overlay (engine portu: Shadow parçası ham rampa USER.w>0, 4 çeyrek quad, closest-side elips). `UIBoxLight {enabled,
  color, width, height, offsetX/Y}` = aynı EmitRadial ile üst-orta prosedürel gloss (16 vertex, doku/draw yok); sprite
  override atlas sprite fazında. Çizim: fill(+radial) → iç gölge → light → metin. Dikey gradient t=0 ALT (kullanıcı).
- ENGINE LayoutBox LIGHT (2026-10-13, layout testleri 40/40 PASS, görsel kullanıcıda): Unity'deki Light'ın engine muadili.
  Alanlar `lightColor` (a>0 = açık), `lightWidth` (oran, 0.9), `lightHeight` (px, 24), `lightOffset` (Vec2 0,2); stil
  setter'ları düz assignment. `EmitRadial` artık renk parametresi alır (radyal fill `fill.color` geçer); `EmitLight` =
  üst-orta dikdörtgene EmitRadial → +4 instanced quad, aynı draw. Düz VE dokulu (sprite) yolda çizilir, metinden önce.
  Test: StyleMeshSmoke "light +4 quad".
- GRADIENT → `UIFill` TİPİ (2026-10-12, kullanıcı: "fillGradient gibi değil, adam gibi tip"): `UIFill {type Solid|Gradient,
  color, UnityEngine.Gradient, horizontal}` + `Evaluate(t)`, `Visible`, `InnerKeyTimes`. Alanlar: `fill`, `borderFill`,
  `textFill`, `textOutline` (hepsi UIFill; eski borderColor/textColor/UIBoxGradient SİLİNDİ). `Graphic.color` YOK SAYILIR
  (Inspector'da `UIBoxEditor` gizler: m_Color/m_Material/m_OnCullStateChanged) — kullanıcı "tek color prop ne iş"
  dedi + alfa 0'a çekilince her şey kayboluyordu. Tint = runtime-only `UIBox.Tint` (serileşmez; durum/fade koddan).
  Band: kutu katmanları kutu rect'i (dikey t=0 üst), metin BLOK bandı (dikey blok yüksekliği, yatay availW; engine
  TextGradAt). İç dolgu quad'ları key zamanlarında bölünür (`EmitSolidSplit`). `Editor/UIFillDrawer` PropertyDrawer:
  Solid → tek satır (tip+renk), Gradient → gradient + yön popup. Radyal YOK.

## [Asset] VERİ SINIFI + ObjectBinary (DOBJ) — Faz A (2026-10-12, 15 test grubu PASS, editör Play + pak/player birebir)
- KARAR (uzun tartışma, kullanıcı): çekirdek ÜÇ primitif bilir — Texture, Audio, serileşen C# sınıfı (+referansları). Geri
  kalan her şey (Settings, LevelDef, Spine, Font, Atlas) `[Asset]` sınıf = dosya olarak yaşar, alanlarda REFERANS (Kind.Asset,
  GUID), pak'a referanslandığı için girer. Düz `[Serializable]` = GÖMÜLÜ (Kind.Object). Unity ScriptableObject/Serializable
  ayrımı miras yerine attribute ile; `[CreateAssetMenu]` `[Asset]`'i ima eder. Ayrı "Settings" sınıfı/penceresi/yol kuralı YOK:
  `[Serializable, CreateAssetMenu] ServerSettings` → Create → .asset → component alanına sürükle (Sandbox SettingsProbe örneği).
- Faz A yapıldı: `AssetAttribute`; `Reflect.IsAssetClass/AssetTypes`; `Kind.Blob` (yalnız byte[]: binary ham, YAML base64 —
  int[]/float[] List kaldı, inspector semantiği bozulmasın); `ObjectBinary` (DOBJ: string tablosu + tip adı + SceneBinary'nin
  PAYLAŞILAN alan tablosu/alan kodlayıcısı; Go/CompRef yazılmaz); `TypeCatalog.RegisterAsset` (Entry.IsAsset/CreateObject,
  `Entries` yalnız component, `AssetEntries` ayrı); PakRoots DOBJ tip adlarını trim kökü sayar (ilk 8 bayt zlib peek, dokular
  şişirilmez); `AssetDatabase.LoadObject/ReloadObject/KeyOf/AssetClassOf/Catalog` (anahtar başına TEK instance, okumadan ÖNCE
  cache'e girer → A→B→A döngüsü güvenli; editörde .asset kaynağı YAML dosyası, importer artifact'i değil); `ImportContext.
  AddArtifact(name, object)` → DOBJ; AssetPackBuilder importer'sız .asset'i YAML→DOBJ pişirir, referansları graf kenarı.
- Editör: Inspector .asset'i AssetDatabase'in CANLI instance'ında düzenler (Play'deki component aynı nesneyi görür);
  AssetWatcher .asset değişince `ReloadObject` yerinde (referans kırılmaz); katalog rebuild → nesne cache düşer.
- Tuzak: `ObjectSerializer.ToNode` Kind.Asset'i `AssetDatabase.KeyOf(v, assets)` ile yazar — [Asset] nesnenin anahtarı
  yüklendiği AssetDatabase'te kayıtlı; başka db'den/elle `new` edilmiş nesne "" yazar (referans kaybolur).
- SIRADA: AOT lean build ile [Asset] trim doğrulaması ve scene-dump paritesi henüz koşulmadı. `_importers` (uzantı→tip)
  sözlüğü duruyor: yükleme yolu değil, editör drag-drop/IsAssignable tohumu — Spine gibi yeni tür importer kaydıyla girer.

## FAZ D: ÇEKİRDEK ÜÇ PRİMİTİF (2026-10-12, 15 grup PASS, editör + pak/player uyarısız)
- `LoadAsset` = `[Asset]` sınıf → `LoadObject`; Sprite/Texture; AudioClip. Font/PixelEffect özel dalları SİLİNDİ.
- PixelEffect `[Asset] { string Body }` saf veri (Name/Version/BodyText yok); FxImporter v2 DOBJ üretir; FxCompose cache
  govde string REFERANSIYLA bayatlar (reload yeni string → yeniden derleme; frame'de yalnız referans kıyası).
- `InvalidateImported`: klip özel; gerisi `#sprites` tablosu + `ReloadObject` (font, fx, oyun sınıfları tek yol).

## FAZ B+C: TEK SPRITE TABLOSU + FONT SAF VERİ (2026-10-12, 15 grup PASS, editör Login screenshot + pak/player uyarısız)
- `AtlasData`(DATL) + `RegionManifest`(DRGN) SİLİNDİ → tek `[Asset] SpriteTable {Channels, SinglePage, Pages[{Artifact,W,H,
  PiecesX/Y}], Regions[{Name,Page,X,Y,W,H,Off,Orig,Kind,Pad}]}` (DOBJ ile pişer — kendi altyapımız). Atlas `main`, üretici
  (font/spine) `sprites` (Standalone: sheet'le birlikte yutulunca düşer). AtlasImporter üye tablolarını `sprites`'tan okur.
- Runtime `AssetDatabase.LoadSpriteTable(owner, tableKey)` jenerik (`LoadAtlas` sarmal). ÖNCELİK: yabancı tablo (atlas) öz
  tabloyu (fontun sheet'i) ezer, öz yabancıyı ezmez → yükleme sırasından bağımsız. Tablo düşünce bölge diğer yüklü tablodan
  yeniden çözülür (FindRegion), yoksa png kaynağına döner. `LoadSprite("x#alt")` öz tabloyu tembel yükler (`EnsureOwnTable`);
  alt bölgenin kendi dosyası yok → bulunamazsa sayfasız Sprite (tablo gelince bağlanır).
- TUZAK (crash dersi): `LoadSpriteTable` tabloyu `_tables`'a bölge atamadan ÖNCE kaydetmeli; aksi halde atama → BindFont →
  EnsureOwnTable → tekrar yükleme → sonsuz özyineleme → IMAGE_POOL_EXHAUSTED.
- `Sprite.U0/V0/U1/V1` + `BindVersion` bind anında (kullanıcı önerisi): SpriteRenderer/ParticleSystem/LayoutBox/metin frame'de
  bölme yapmaz. Pak builder: tablo artifact'i → sayfa artifact'lerine kenar (aktivasyon senkron okur); `#`'li referans
  sahibine indirgenir.
- Font ARTIK `[Asset]` SAF VERİ: `{Ascent,Descent,LineHeight,KernScale,SdfSize, Glyph[] Glyphs, byte[] Kern(Blob)}`,
  `Glyph{Codepoint,Advance,XOff,YOff,W,H,InkL,InkR, Sprite Sprite}` — glyph sprite anahtarı "x.ttf#U+0041". Font doku bilmez:
  `Page/BindVersion/Pieces` ilk pikselli glyph'in Sprite'ından; codepoint tabloları + kern short[] ilk kullanımda türetilir
  (`[NonSerialized]`, Glyphs referansı değişince yeniden). SİLİNDİ: DFNT, WriteArtifact/FromArtifact/ReloadFrom, BindSheet/
  BeginAtlasBind/SetGlyphPosition/Unbind, AtlasX/Y, SheetWidth/Height, `AssetDatabase._fonts/BindFont`. `LoadFont` =
  `LoadObject(key, typeof(Font))` + DefaultFont. Reimport: `InvalidateImported` önce `#sprites` tablosunu, sonra nesneyi yerinde.
- FontImporter v10: main = Font (DOBJ, glyph Sprite'ları `Sprite.FromTexture(null, key)` yalnız anahtar yazılır), sheet DPIX,
  sprites SpriteTable. AtlasImporter v3.

## LAYOUTBOX IC GOLGE (innerShadowColor/Blur) — ek draw/doku yok (2026-10-11)
- Unity UIBox karsilastirmasindan: iç gölge, border ring yolunun (EmitLayer) yumusak-ic-bandli hali. Dolgu USTUNE, border
  ALTINA (border'in ic kutusu), 8 quad (4 hucre + 4 serit), ic dolgu yok; ayni UiPieces materyali -> kutunun draw'ina merge.
- SHADER (UiPieces._fragTail): `USER.x>0 && USER.z>0` kombinasyonu artik "dis kenar fwidth KESKIN, ic band smoothing'li"
  demek (onceden iki kenar da yumusakti; hicbir yayici kullanmiyordu). Golge x>0/z=0, border x=0/z>0 degismedi.
- b > radius durumu: hucre max(r,b) ile kurulur (gercek blur iso-konturu gibi yuvarlanir, seritlerle ortusme/çift blend
  olmaz); kutunun gercek kosesi hucre merkezli KESME dairesiyle (USER.y=c) yaklasik kirpilir — yay uc noktasi ile tepe
  uzakliginin ortalamasi, hata < ~0.7px; bu hucrede dis AA payi (m) 0 (pay kesme dairesinin icinde kalip boyanirdi).
  b <= r ise border ring'iyle birebir ayni geometri (kesin).
- YOK (bilincli): offset'li inset golge (CSS inset dx/dy) — kaydirilmis ring'in kutuya kirpilmasi gerekir; istenirse 2. faz.
- RPC TUZAGI: comp.schema Color format'i yanlis "r g b a 0..255" diyordu; doc kanonik "#RRGGBBAA" (baska bicim ParseColor'da
  BEYAZA duser, hata vermez). Format metni duzeltildi. Test betigi: tools/innershadow_test.ps1 (Sandbox Scenes/InnerShadowTest).

## CORELIB EKSIGI = CORELIB'DE KAPANIR (Delegate.Combine / IsMacCatalyst, 2026-10-11)
- yuzbirextra Button.cs `event` (+=/-=) ve PlatformFilter `IsMacCatalyst` AOT'de patliyordu (CS0656 / CS0117); scriptler yeni
  eklenmisti, editorde (.NET) calistigi icin fark edilmemis. Cozum TAMAMEN corelib'de: `Delegate.Combine/Remove` extern
  (roslyn/WellKnownTypes.cs) -> corelib.c govdesi vmrt `digitoyengine_delegate_combine/remove`; `OperatingSystem.IsMacCatalyst`
  extern -> TARGET_OS_MACCATALYST. Derleyiciye dokunulmadi; selftest 46 (delegate +=/-=) da bununla yesile dondu.
- KURAL: corelib eksigi once C# yuzeyi (+ gerekirse extern/corelib.c). CilFrontend/CTranspiler'a yalniz IL sekline bagli,
  C#'ta yazilamayan sey girer (ldtoken, Span intrinsic, async remap). IR'de hazir ama kullanilmayan op (DelegateCombine/Remove,
  Code.cs) frontend'e dokunma gerekcesi degildir — o op'lar artik olu; bir sonraki temizlikte silinebilir.

## CORELIB DATETIME/TIMESPAN minimal (2026-10-11)
- c_runtime/corelib/DateTime.cs: tek alan tick (100ns, 0001-01-01), proleptik Gregoryen (BCL GetDatePart algoritmasi).
  Yuzey: ctor(ticks|y,m,d[,h,mi,s]), UtcNow/Now, Ticks/Year..Millisecond/Date/TimeOfDay, Add*/Subtract, karsilastirma
  operatorleri, IsLeapYear, MinValue/UnixEpoch. TimeSpan: Ticks/Total*/From*/+ -/karsilastirma, TicksPer* sabitleri.
- YOK (bilincli): Kind/saat dilimi (Now == UtcNow), Parse/ToString/format, kultur, DayOfWeek, IComparable. Gerekirse ekle.
- Native: corelib.c `System_DateTime_GetUtcNowTicks` (win GetSystemTimeAsFileTime+1601 ofseti; posix CLOCK_REALTIME).
- Test: selftest case 73 (Cases/Test62.cs) .NET ile birebir. Selftest .NET 9'a karsi derlenir: .NET 9'un yeni
  int overload'lari (TimeSpan.FromHours(int)) corelib'de yok; test double yazar. Oyun kodu CoreLib'e karsi derlendigi icin sorun degil.

## LAYOUTBOX QUAD DIKISLERI: alt-piksel bosluk (2026-10-11)
- BELIRTI: LayoutBox parcali quad'lari (SDF kose/kenar/ic dolgu, golge, slice9) arasinda konuma/olcege bagli 1px
  cizgiler (yuvarlak noktada arti seklinde, kutu kenarlarinda). NEDEN: ortak kenar iki quad'da GPU'da ayri hesaplanir
  (merkez ± boyut/2); float bit-esit cikmazsa rasterizer alt-piksel izgarasinda farkli hucreye duser -> bosluk/ustuste.
  Ayrica ic dolgu kenarlari hucrelerden farkli ifadeyle uretiliyordu ((x0+2)+(r-2) vs x0+r).
- COZUM (LayoutBox.EmitQuad): eksen-hizali dunya matrisinde koseler dunya uzayinda 2^-10 izgaraya oturtulur (Snap);
  merkez/boyut bu koselerden turer -> GPU'daki merkez±boyut/2 toplami float'ta TAM, iki quad ayni biti verir.
  Donuslu matriste eski yol. Ic dolgu partisyonu `SolidInterior`: kose ic kenarlari hucrelerle AYNI ifadeyle gelir.
- KURAL: parcali renderer'da komsu quad'in ortak kenarini AYNI float ifadeyle uret; "a+b-b" turevleri dikis acar.
- BORDER SIFIR KENAR: ring shader'i `smoothstep(c)*(1-smoothstep(USER.z))`; kalinlik 0'da USER.z==c olsa da carpim
  t(1-t) AA genisliginde (tepe 0.25 alfa) iz birakir -> `border 0 0 0 2` ust/yanlarda ince cizgi. EmitLayer'da ring
  serit/hucresi kalinlik <= 0 ise HIC emit edilmez (Cell/HEdge/VEdge).

## GENERICS: SOMUT BAGLAMLA INDIRGEME (.NET/IL2CPP modeli) — S1+S2+S3 BITTI (2026-10-11)
- NEDEN: her generic isinde (koleksiyon, canon, ref/struct) ayni mayin: sablon govdesi T ACIKKEN IR'a cevriliyor, klonda op op
  "yama" (GenericCast->Unbox/CastClass, IsValueType, Default...) + emisyonda 43 ayri IsStruct/IsEnum/IsNumericVal kombinasyonu.
  Eksik kombinasyon ancak kullanimda patliyordu. Kullanici karari: "evrensel, her zaman dogru" yapi, case case degil.
- S1 (CilFrontend/GenericInstantiator/Resolver): SABLON GOVDESI HICBIR ZAMAN CEVRILMEZ/KLONLANMAZ. Ornek kabugu (imza)
  GenericInstantiator kurar; govdesi `CilFrontend.TranslateInstance` ile AYNI IL'den SOMUT GenCtx'le (TypeParams =
  Owner.TypeArguments, MethodParams = Code.MethodTypeArguments) yeniden cevrilir -> her IL opcode (box/unbox.any/constrained/
  isinst/sizeof/ldtoken) tam tiple tek anlamli. `Code.IlSource` (loader+handle) sablon ve orneklerde ortak; `RootTemplate`
  zinciri. Loader hazir degilse (LoadMembers suruyor) kuyruk bekler (`LoaderReady`). Resolver.ResolveAll SABIT NOKTA: cozum
  sirasinda dogan ornek kodlari da islenir. CloneCodeBody/GenericCast yamasi/BodyPending/MarkBodyReady SILINDI.
  TUZAK: frontend New+Call(ctor) op'lari AYNI TypeArguments listesini paylasir (temizleme yok).
- S2 (TypeShape.cs): tip sekli TEK kapi: `Shape {Void,Scalar(enum dahil),Struct,Class,Interface,Delegate,Array,FixedArray,
  Pointer,GenericParam}` + IsValueLike/IsGcRef/IsStructModel/IsClassModel/IsScalar. CTranspiler.CType/IsNumericVal ve 26 ad hoc
  ifade buna yonlendirildi (davranis birebir: full+lean "0 yazildi"). Frontend unbox.any/isinst de TypeShape (dizi/delegate/
  iface'i deger tipi saniyordu). Kutulama tek kapi: `BoxValue/UnboxValue/BoxedTypeOf` (enum/skaler/struct/Nullable).
- S3 (tests/dotnet-selftest/Cases/Matrix.cs + Test61.cs = case 61..72): `Probe<T>` tek govde x T = {int, long, enum, struct,
  ref-struct, string, class, iface, delegate, int[], int?} + Nullable<T> ozel yolu; op listesi: box/unbox/is/cast, default/typeof,
  T[] + Array, static alan, generic base + sanal + iface, ref/out, delegate indirect + capture, generic metot icinde generic
  metot, constrained ToString/Equals, List<T>/Dictionary<int,T>/foreach, iterator, async Task<T>. .NET baseline ile birebir.
  Yeni op kategorisi -> Matrix.cs'e eklenir. Fark ayiklama: `H.trace=true` -> stderr [h] adimlari, .NET vs AOT diff.
- MATRISIN YAKALADIGI gercek eksikler (hepsi duzeltildi): (1) generic TABAN SINIF (TypeSpec base) frontend'de hic cozulmuyordu ->
  Object'e dusuyordu; (2) generic sinifin generic metodu MemberRef ile cagrilinca `!!0` baglam disi; (3) struct'ta override
  edilmemis Equals/GetHashCode/ToString constrained cagrisi patliyordu -> .NET gibi KUTULA+callvirt; kutulu struct descriptor'u
  artik vtable tasir (override thunk'u ya da `digitoyengine_valuetype_eq/hash` payload varsayilani); (4) delegate ve dizi
  descriptor'larinin vtable'i yoktu (`((object)d).Equals` NullRef) -> `digitoyengine_delegate_vtable` (hedef+metot esitligi),
  dizilerde Object vtable'i; (5) `Array.ToString()` null -> "System.Int32[]"; (6) Nullable<T>: kutusu T kutusudur (HasValue=false
  -> null), unbox T kutusundan sarar, `is T?` T'yi test eder; Nullable.Equals/GetHashCode/ToString override'lari.
- SONUC: selftest 68/71 (yalniz 3 eski bilinen: InlineArray, Delegate.Combine, StringSplitOptions); matris 12/12. Sandbox
  windows full 5 417 KB / lean 2 737 KB calisiyor; wasm lean 1 366 KB (tarayicida dogrulandi). Editor build OK.
- corelib/Random.cs (kullanici): seed `Environment.TickCount` (DateTime.Now/Ticks corelib.c'de govdesiz, link kirardi).
- SIRADA (KARAR 2026-10-11: CANON, collections degil — ".NET standardi, evrensel"): ref-argumanli generic'ler tek kod.
  Plan (her fazda selftest 68/71 + matris 12/12 + Sandbox exe/wasm yesil): (1) kanonik anahtar (class/iface/delegate/dizi/
  string -> __Canon), exact tip kimligi (descriptor/vtable/ad/generic_args) AYRI, govdeler canon ornege bagli, C struct tek;
  T-bagimli op kullanmayan metotlar paylasilir, digerleri bu fazda ozel kalir. (2) RGCTX: instance -> this->type->generic_args,
  static/struct/generic metot -> gizli `const Type* __rgctx`; typeof/new T[]/is-as/default/sizeof dict'ten; runtime
  `digitoyengine_instantiate(def,args)` tablosu. (3) Statikler ornekleme basina (`Type.statics` blogu). (4) Meta: MethodInfo
  canon basina, trace adi `List<T>.Add`. (5) Olcum + `--no-canon` kapisi. Matrise ek hucre: Probe<D> (iki ref orneklemesi
  AYNI sembole gider mi). HENUZ BASLANMADI. Hierarchy'de struct vtable/itable eksigi de duruyor (yukarida).

## TRIM CERCEVESI (ILLink benzeri) — ADIM 1+2+3 BITTI: lean = meta + kod trim (2026-10-11)
- Karar: trim aotcompiler GENELINDE tek cerceve (`aotcompiler/source/Trim/`): kok kume + analiz -> `TrimResult`; emisyon
  (CTranspiler/Meta) kod/meta/thunk/trampoline/tip-adi kararlarini YALNIZ `CTranspiler.TrimPlan`'a sorar, profile dogrudan bakmaz.
  Host opsiyonelligi bu cercevenin bir profili (lean), ayri mekanizma degil.
- Parcalar: `TrimProfile` (full = bugunku cikti, lean = moduleHost kapali + meta yalniz koklere; `--trim full|lean`),
  `TrimRoots` (Entry = GameHost Init/Event/Frame/Pause/Resume/Shutdown; Reflection tip->MemberKinds; ComponentNames pak'tan),
  `PakRoots` (game.pak DPK3 index + zlib -> DSCN v2 tip tablosu + alias adlari; AYRI kok dosyasi YOK, pak tek gercek),
  `TrimAnalysis.Run` (otomatik kokler: [Preserve], typeof(T)->Name, attribute tipleri, enum->Name; pak adlari kisa ad esleme
  (Component turevi); facet yayilimi: base zinciri, Fields -> alan tipleri (struct/[Serializable] -> Inline, enum/ref -> Name,
  List<T>/T[] eleman); kod erisilebilirligi RTA-benzeri: ReachableStubs kurali + tum cctor'lar + facet'li tiplerin ctor/metotlari),
  `TrimResult` (IsCodeLive/Reflection/MethodRecord None|Trace|Full/NeedsThunk/NeedsVTramp/NeedsDTramp/NeedsTypeName).
- Emisyon baglantilari: IsEmittableCode && IsCodeLive; HasMethodRecord && MethodRecord!=None; sekil thunk + MethodInfo.thunk
  yalniz NeedsThunk; HasVirtualTramp/HasDelegateTramp Needs*; ReflectionFields/ReflectionProperties facet'e gore (members tablosu,
  refget/refset, havuz adlari); TypeNameRef NeedsTypeName. Attribute tablolari HENUZ facet'e bagli degil (adim 2).
- `--trim-report` -> `<proje>/Library/aot/<hedef>/trim-report.md` (kokler, dead kod listesi, facet nedenleri).
- DOGRULAMA: Sandbox windows full profil: TrimPlan=analiz vs TrimPlan=Everything -> 0 dosya yeniden yazildi, exe 5 464 KB ayni.
  Rapor (Sandbox): 11 pak component, 86 facet tipi, 1 446 canli / 7 622 somut kod (rapor amacli; TrimCode kapali).
- SIRA (adim 2 = lean): vmint derlenmez (DE_NO_MODULES stub'lari), Module.cs #if; MethodInfo trace oneki (ilk alanlar) + FULL
  bayragi, runtime okuyucular (vmrt.c/corelib.c GetMethods/GetFields/Activator) kok disi -> bos; attribute tablolari facet'e;
  Reflect.ComponentTypes (asm.GetTypes) yalniz facet'li component'ler. Sonra olcum + docs. Adim 3: TrimCode (B) ayri onayla.
- ADIM 2 BITTI (lean profil): `MethodTrace` = MethodInfo'nun ON EKI (name/file/trypc/nparams/ret_tag/flags/nattrs/declaringType/
  param_types; vmrt.h'de MethodInfo bu alanlarla BASLAR). Kok disi metot `{cn}_mtrace[]`/`digitoyengine_mtrace_misc[]`'e yalniz
  bu kayitla girer, frame'e `(const MethodInfo*)` cast'iyla konur; Type.methods'a GIRMEZ (reflection/Invoke/find_method gormez).
  Frame okuyuculari (trace/crash/trypc) zaten yalniz on eki okur. Lean'de thunk/vtramp/dtramp yok; `vmint_stub.c` vmint.c yerine
  derlenir (Module.cs P/Invoke'lari baglanir, vmint_load "disabled" doner; Android CMake `${DE_VMINT}`); attribute tablolari
  Attrs facet'ine bagli; `Type.flags` unsigned short + `DIGITOYENGINE_TYPE_NOREFLECT=256` (facet'siz tip; Assembly.GetTypes
  atlar -> katalog yalniz kok component'leri gorur: Sandbox 16 -> 13). TIP ADLARI HER ZAMAN yazilir (trace "List<?>" olmasin).
  PlayerSettings.moduleHost (varsayilan false = lean) -> PlayerBuilder `--trim`.
- Lean'de bulunan kok eksikleri (first-chance dokumuyle): (1) `new T()` -> Activator.CreateInstance<T> -> typeof(T)+
  CreateInstance(Type): TypeOf'u izleyen Activator cagrisi hedefe Ctors verir (GetSystem<LayoutSystem> null donuyordu);
  (2) List<T> alanlari: serializer `NewList` = Activator(List<T>) -> List<T> tipine Ctors.
- OLCUM: Windows exe full 5 408 KB -> lean 3 483 KB; wasm 3 865 -> 2 402 KB (CODE 2 622 -> 1 725 KB, DATA 1 196 -> 713 KB).
  Selftest 56/59 (degismedi; selftest Everything profiliyle). Sandbox lean calisiyor (sahne yuklendi).
- DEBUG FAZ A: `AOT_DEBUG=1` build'de `DIGITOYENGINE_throw` FIRST-CHANCE dokum basar (yakalanacak olsa da): firlatma anindaki
  frame'ler + `[locals]` + `[steps]`. Runtime turu (null/cast/index) her zaman; kullanici exception'lari `DE_FIRSTCHANCE=1`.
  Task icinde yutulup GetResult'ta yeniden firlayan hatalarin GERCEK yeri boyle bulundu (lldb'siz). Faz B (breakpoint/stdin
  komutlari) bekliyor. Bilinen kucuk kusur: bazi generic klon frame'lerinde dosya adi yok ("TrySetResult(System.Int32):107:13").
- ADIM 3 BITTI (TrimCode, lean profilinde ACIK; `meta` profili = yalniz meta trim, karsilastirma icin): erisilemeyen kod emit
  edilmez (`IsEmittableCode && TrimPlan.IsCodeLive`). Analiz = RTA: kokler host girisleri + tum cctor/finalize + exception
  kind ctor'lari + Object/String vtable + facet ctor/metotlari; sanal/iface cagri SLOT tabanli (Owner.VTable slotu -> ornekli alt
  tiplerin VTable[slot] / ITables[iface][slot]; ad tabanli eski kural da birlesir) ve yalniz ORNEKLENMIS tiplerde cozulur
  (New/Box/ctor cagrisi, Ctors facet'i, exception kind, tum struct'lar, tum System.* [C runtime gc_alloc eder], Object/String).
  Olu vtable/itable girisi `0` yazilir (VSlotRef). Sandbox: 3 444 canli / 7 622 somut, 968 ornekli tip; olunun %78'i
  System.Collections generic orneklemeleri -> sira C (canon generics).
- OLCUM (adim 3): Windows exe 3 483 -> 2 951 KB; wasm 2 402 -> 1 960 KB (CODE 1 362 KB, DATA 613 KB). Toplam: 5 580 -> 1 960.
  Selftest 56/59. Sandbox lean calisiyor (sahne yuklendi; oyun ici etkilesim gorsel dogrulama bekliyor).
- E TOOLCHAIN BITTI (wasm release varsayilani, Program.BuildWasm): -Oz derleme+link, -sMALLOC=emmalloc, -sSUPPORT_LONGJMP=wasm
  (derleme VE link'te). Olcumler (lean): -O2 1 960 | -Oz 1 546 | -Oz+LTO+emmalloc 1 594 (LTO BUYUTTU, kullanilmaz) |
  -Oz+emmalloc 1 539 | -Oz+emmalloc+longjmp=wasm **1 467 KB** (CODE 818, DATA 637). Tarayicida dogrulandi (Login sahnesi,
  particle/layout/text calisiyor). Deney kapilari: AOT_OPT / AOT_LTO=1 / AOT_CFLAGS_EXTRA / AOT_LINK_EXTRA.
- NEREDE NE VAR (lean, obj bazli: tools/wasm_sizes.ps1): CODE = gen DigitoyEngine ~%45 (LayoutBox 132 KB@-O2, ParticleSystem 38,
  Font 29, Scene 26, MovieClip 22, SceneDump 22), System.Collections orneklemeleri ~%30, native shim ~%15 (sokol_shim 158,
  de_fs 100), c_runtime ~%5. Lean'de SURPRIZ canlilar: GameHost.NetProbe state machine 30 KB, Module.LoadAsync 18 KB,
  SceneDump 22 KB. DATA 637 = string havuzu 222 KB (2 230 VmString: 152 KB UTF-16 + 70 KB baslik; en buyukler 1 763/1 292/1 246
  karakter = shader/uzun literal) + 14 KB UTF-8 metot adi + ~400 KB descriptor (Type/vtable/itable/MethodTrace/mp_; System.
  Collections 268 KB obj-DATA) + sokol 73 + audio 33.
- 1 MB HEDEFI ICIN SIRA: C canon generics (CODE -200..250, DATA -150..200) -> string havuzu (uzun literal UTF-8/lazy, baslik
  kucult: -80..120) -> surpriz canlilari kes (NetProbe/Module/SceneDump: -70) -> sokol/de_fs olu yollar (-50..80).
- ADIM 4 BITTI ([DevOnly]): `DigitoyEngine.DevOnlyAttribute` (metot). Lean'de isaretli metot GOVDESIZ emit edilir (void -> bos,
  Task -> Task.get_CompletedTask, diger -> default; Task<T> build hatasi) ve govdesi gezilmez -> cagirdiklari kirpilir
  (TrimResult.Stubbed / IsStubbed, CTranspiler.EmitFunction). GameHost dev kancalari `DevArmSceneDump` (durum `_dumpArmed`
  alanla) ve `DevAfterBoot` (autoload modul + net probe) altina alindi -> SceneDump/Module.LoadAsync/Net.cs/NetProbe kirpildi.
  wasm 1 467 -> **1 354 KB** (CODE 733, DATA 610); canli kod 3 444 -> 2 646. Tarayicida dogrulandi. NOT: scene-dump testi ve
  autoload modul FULL profil ister (tools/scene-dump-test.ps1 uyarir; PlayerSettings.moduleHost=true ya da --trim full).

## BOYUT (PLAYABLE/WASM) — OLCUM + PLAN + A1 BITTI (2026-10-11)
- Hedef: playable (tek HTML, base64 x1.33; 2-5 MB ag limiti) -> Sandbox wasm <= ~1.5 MB ham. Baslangic 5 580 KB (-O2) + 1 114 KB data.
- OLCUM (llvm-objdump -h game.wasm): CODE 2 622 KB / DATA 2 437 KB. DATA'nin yarisi string havuzu (10 563 VmString, 1 420 KB:
  metot gosterim adlari "Owner<Args>.Metot(params)" her ornekleme icin ayri UTF-16), kalani descriptor tablolari (MethodInfo ~20k
  kayit, members, vtable). 2 754 `T_System_*` generic ornekleme vs 578 engine tipi (her Task<T> -> List<Action<Task<T>>> vb.).
- KOK NEDEN: "tek meta" her tipin her uyesini descriptor'a yazar -> hicbir sey elenemez (IR'de de linkerde de).
- KULLANICI KARARLARI: symbols.map / harici sembol dosyasi YOK (trace ikilide, satir:kolon korunur). Modul host (vmint + tam
  meta) OPSIYONEL olacak (PlayerSettings, varsayilan kapali); reflection yalniz kok kume: pak'taki sahne/prefab component
  tipleri + alanlarinin gecisli tipleri + AddComponent<T>/typeof(T) ornekleme kokleri + [Preserve] + [Animatable].
- A1 BITTI (metot adlari kompakt): `MethodInfo.name/file` artik `const char*` UTF-8 (`_cs_<hash>` havuzu, gen_strings.c;
  VmString basligi yok); name = YALIN ad ("Add", ".ctor") tum orneklemelerce paylasilir; trace "Sahip.Ad(ParamTipleri)
  (dosya:satir:kolon)" sahibi `declaringType`'tan (generic ornekleme -> `generic_def->name`, "List<T>.Add"), parametre adlari
  `param_types`'tan -> .NET bicimi korunur (Test17 "Demo17.App17.Boom()"). `MethodBase.Name` parse etmez; `GetMethod(name)`
  `vmstring_eq_utf8`. vmint tag'i char*. SONUC: wasm 5 111 -> 3 940 KB (DATA 2 437 -> 1 273 KB; VmString 10 563 -> 3 715 adet,
  238 KB; 1 602 UTF-8 ad). Windows exe 7 441 -> 5 547 KB. Selftest 56/59, Sandbox exe OK. Performans etkisi yok (ad yalniz
  soguk yollarda okunur).
- Onceki kucuk kazanc: Task<T>.typedContinuations (kullanilmayan ContinueWith(Action<Task<T>>)) silindi: -470 KB.
- SIRA: A2 tip adlari (generic orneklemelerde Type.name yazma, generic_def+args'tan lazy kur; non-generic UTF-8) ->
  E toolchain (-Oz, -flto, emmalloc, closure, SUPPORT_LONGJMP=wasm, FILESYSTEM=0+fetch pak) -> host opsiyonel + kok kume
  (kok disi metot yalniz 12 B trace oneki: MethodInfo'nun ilk 3 alani) -> C referans-tipli generic paylasimi (__Canon modeli:
  kod ortak, Type kimligi ayri, generic dictionary) -> B IR DCE. Her adimda olcum; esik tutmazsa B plani: playable icin ayri
  ince TS/Haxe runtime (motor/editor C# kalir).
- Dersler: .NET/NativeAOT modeli = runtime-zorunlu descriptor (vtable/GC) ile reflection satirlari AYRI; adlar UTF-8 tekil
  yigin + offset; lazy materyalizasyon. IL2CPP global-metadata.dat boyut ornegi DEGIL (stripping disinda her seyi tutar).

## NETWORK FAZ 1: WINHTTP + Net.cs BITTI (2026-10-11)
- engine/native/de_net.{h,c} (Windows WinHTTP async HTTP+WS; diger platformlar faz 2-4 stub) + engine/managed/Net.cs
  (`Http.Get/Post/SendAsync(req, ct)`, `WebSocket.ConnectAsync/Send/ReceiveAsync/OnMessage/CloseAsync/OnClose`). Yalniz
  NativeAsync.Await/Listen kullanir, kendi pump'i yok. editor/NetTests.cs 18 PASS (localhost HttpListener + WS echo).
- TUZAKLAR: (1) Editor DLL'i `dotnet build editor` kopyalar; yalniz build_editor.cmd kosturup editor acmak BAYAT DLL calistirir.
  (2) WinHttpWebSocketClose bekleyen Receive varken hata -> Shutdown + CLOSE frame'i alma ile gelir. (3)
  WinHttpWebSocketQueryCloseStatus DWORD doner (0 = basari). (4) ws:// semasi WinHttpCrackUrl'de yok -> http(s)'e esle.
  (5) AOT frontend: struct'ta `??=` + this spill hatali C (`WsMessage` class yapildi) — frontend hatasi ACIK, ayri is.
  (6) WebSocket.Finish: `_closeTask.TrySetResult` EN SON (await devami inline kosar, OnClose'u gormeli).
- Detay/sira: docs/network.md "Durum". Sirada: Faz 2 wasm (EM_JS fetch/WebSocket), Faz 3 Android OkHttp, Faz 4 Apple.
- AOT calisma zamani (Windows) DOGRULANDI: GameHost dev kancasi PlayerPrefs `net.probe`=taban URL -> `[net-probe]` GET/POST/WS
  loglari; sunucu = editor `DE_NET_SERVER_PORT=18500` (NetTests sunucusu CORS'lu acik kalir). Faz 2 wasm backend'i (EM_JS
  fetch + WebSocket, `_de_net_done` geri cagri) YAZILDI ve emcc ile derlendi; tarayici testi kullanici istegiyle ERTELENDI
  (nasil: docs/network.md).

## CANCELLATIONTOKEN (CORELIB) + ASYNC IPTAL ALTYAPISI + FRONTEND ERTELENMIS KLON (2026-10-11)
- KARAR: HttpClient taklidi YOK (yuzey dev, iki implementasyon ayrisir) ama CancellationToken corelib'e GIRDI: kucuk,
  senkron, async deyiminin parcasi (Task gibi). `c_runtime/corelib/Cancellation.cs`: CTS (Cancel: callback'ler SENKRON ve
  .NET gibi LIFO; Dispose), CancellationToken (None/CanBeCanceled/IsCancellationRequested/ThrowIf/Register — iptal edilmise
  Register hemen kosar), CancellationTokenRegistration (Dispose/Unregister), OperationCanceledException, TaskCanceledException.
  Corelib'de OLMAYAN: CancelAfter (saat yok -> `Wait.Timeout`), CreateLinkedTokenSource. Ana-thread modeli, kilit yok.
- Task.cs: `canceled` (IsCanceled true / IsFaulted false, fault=TaskCanceledException), IsCompletedSuccessfully, CompletedTask,
  FromResult/FromException/FromCanceled, WhenAll(Task[])/WhenAny(Task[]) (acik yardimci siniflar; generic icinde closure yok),
  TCS TrySetCanceled(ct)/SetResult/SetException/SetCanceled. Selftest Test60 .NET baseline ile birebir (56/59; 3 eski stub).
- FRONTEND HATASI BULUNDU/DUZELTILDI: GenericInstantiator, sablon govdesi henuz LoadBodies'te cevrilmemisken klon isterse
  BOS Operations kopyalayip BodyCloned damgaliyordu (Task.WhenAll -> WhenAllState -> TCS<int> -> Task<int>: Task<T>..ctor
  bossa base ctor + continuations init yok -> NRE). Cozum: `Code.BodyPending` (LoadMembers'ta), `CloneCodeBody` bekletir
  (`PendingClones`), `LoadBodies` her govde sonrasi `GenericInstantiator.MarkBodyReady` -> bekleyenler dolar; ertelenmis
  klonda Locals/LocalNames sablondan tamamlanir. Simdiye dek tetiklenmemis gizli bomba idi (corelib non-generic sinifi,
  corelib generic'ini kendi gövde sırasından once orneklemiyordu).
- Test tuzagi: .NET 9'da `Task.WhenAll(a,b)` params ReadOnlySpan (InlineArray) / `WhenAny(a,b)` generic overload'a baglanir;
  selftest'te ACIK dizi ver. `Task.ContinueWith` ENGINE KODUNDA YASAK: .NET'te thread pool'a gider (ana-thread ihlali); `await` kullan.
- Engine: `NativeAsync.Await(job, ct, onCancel)` (iptal: kayit duser, onCancel=native iptal, Task iptal; gec native tamamlanma
  sahipsiz->Free), `Listen(job, cb, ct, onCancel)`, `Frame.Next(ct)`, `Wait.Seconds(s[, ct])` (Pump(dt), olceksiz gercek zaman;
  AssetDatabase.Tick(budget, dt)), `Wait.Timeout(s)` -> CTS. Kayitlar tamamlanmada Dispose edilir (uzun omurlu CTS'de sizinti yok).
- `Component.destroyCancellationToken` (Unity paritesi): tembel CTS, Scene.DestroyComponentNow/DestroyGameObjectNow OnDestroy
  sonrasi Cancel; yok edilmis component'te iptal durumlu token doner. editor/AsyncQueueTests 28 PASS; Sandbox.exe AOT OK.

## NETWORK (HTTP + WEBSOCKET) + TEK ASYNC TAMAMLANMA KUYRUGU — PLAN (2026-10-11)
- Tam plan: docs/network.md. Kullanici karari: alt sistem basina Pump YOK ("Net.Pump, AsyncJobs.Pump, yarin baska
  Pump" reddedildi). Tek genel mekanizma: native `de_async` MPSC tamamlanma kuyrugu (de_app olay kuyrugu deseni) +
  managed `NativeAsync.Await(job)/Listen(job)/Pump()`; frame'de TEK Pump (GameHost.Frame + editor PlayMode).
- Task.cs'teki "POD DigitoyEngineAsyncCompletion + main-thread drain" yorumu tasarlanmis ama HIC yazilmamisti; de_fs
  kendi job tablosunu + `de_job_poll(w,h)` (texture'a ozel imza) yapmisti. Simdi gercekten yaziliyor.
- Kuyruk POD `(job, code, data, len, a, b)`; anlam bilmez (HTTP a=status, decode a=w b=h). Tek-sefer (Await→Task)
  ve cok-sefer olay (Listen: WS mesaji, indirme ilerlemesi) ayni kuyruktan. Planlanan `Platform.OnMessage` de bu kuyruk.
- Ag backend'leri YALNIZ platformun hazir altyapisi, TLS/proxy/cookie OS'un: Windows WinHTTP async (+WS), Apple
  NSURLSession (+NSURLSessionWebSocketTask), Android OkHttp (framework'te WS yok; HTTP de OkHttp = tek yol),
  wasm EM_JS fetch + WebSocket. Editor ve .NET dev host AYNI digitoyengine_native.dll (WinHTTP) → .NET HttpClient YOK.
- Sira: Faz 0 de_async + NativeAsync → de_fs'i uzerine tasi (AsyncJobs API korunur, Sandbox/testler birebir) →
  Faz 1 de_net Windows + Net.cs + editor testi (localhost HttpListener + WS echo) → wasm → Android → Apple → (ops.)
  download-to-file + remote Module.LoadAsync.
- Kapsam disi: cert pinning, HTTP server, UDP. JSON corelib'de YOK — ayri is.
- **FAZ 0 BITTI (ayni gun)**: de_async.{h,c} + NativeAsync.cs; de_fs job tablosu/de_job_poll KALKTI (malloc'lu FIFO →
  de_async_complete); Sokol.AssetPoll/FreeJob silindi; editor bloklayan bekleme NativeAsync.TryTake. Tuzak: hata kodu negatif
  → EventBit maskesi yanlis pozitif; IsEvent yalniz Code>=0. editor/AsyncQueueTests 12 PASS; editor/AOT exe/wasm dogrulandi.
  Detay docs/network.md "Durum".

## RENDERGROUP — HIYERARSIK ALFA (2026-10-10)
- engine/managed/RenderGroup.cs: `Alpha` (0..1) + `IgnoreParentAlpha`. Siradan component (katalogdan Inspector/
  Tween/MovieClip/`comp.add` bedava). Fade-in/out = kok GO'ya tek track; cocuklara dokunulmaz.
- Mimari KARAR: GO'ya alan YOK, sistem/dirty YOK. Scene.WalkRenderers (SortingOrder cozen DFS) GO component'lerinde
  RenderGroup'u bulur, `parentAlpha * Alpha` (IgnoreParent: yalniz Alpha) hesaplar, `Renderer._effectiveAlpha` yazar,
  Encode'dan once `RenderQueue.GroupAlpha` basar; DrawMesh 8.8 sabit noktayla 4 kose alfasini carpar (scale 256 =
  dalsiz gecis). Renderer tipleri grubu BILMEZ. Reddedilenler: GO.Alpha (container'a nitelik), AlphaSystem (5 kanca +
  hiyerarsi-sürüm ilkeli; walk zaten her frame kosuyor, kazanc yok — flattened render list gunu gelirse birlikte).
- Cikis premultiplied (wrapper rgb*a) oldugundan yalniz alfa carpmak Additive/Screen/Multiply'da da dogru solar.
- Alfa<=0: Encode atlanir, Pointer.Pick gormez (BlocksRaycast dahil; cizim = tiklama). Disabled grup etkisiz.
- Bilinen sinir: ortusen kardesler fade'de birbirinin icinden gorunur (CanvasGroup ile ayni); RT-flatten opt-in, ileride.
- editor/RenderGroupTests.cs (12 PASS): ic ice carpim, ignore, disabled, alfa 0 encode/pointer, Tween("Alpha"), 100 frame zero alloc.

## PLAYERPREFS — KALICI ANAHTAR/DEGER (2026-10-10)
- engine/managed/PlayerPrefs.cs: Set/Get Int|Float|Bool|String, HasKey, DeleteKey, DeleteAll, Save, SetRoot (host).
  Degerler STRING (invariant kultur); tip uyusmazligi varsayilana duser. Buyuk/sik veri icin degil.
- Depo platformun KENDISI (kullanici karari: mobilde native dosya YOK), de_fs.c "PREFS" bolumu, de_fs.h'ta API:
  iOS CFPreferences (NSUserDefaults'un C yuzeyi, ObjC gerekmez; TargetConditionals TARGET_OS_IPHONE),
  Android SharedPreferences ("digitoy_prefs"; JNI → platforms/android/java/.../Prefs.kt, host JNI_OnLoad
  de_android_prefs_bind, GameActivity Prefs.init(context); set=apply, save=commit),
  Web localStorage (anahtar oneki "de:<location.pathname>:" — ayni origin'deki oyunlar carpismaz),
  Windows/macOS/Linux <root>/prefs.dat (bellek haritasi, tembel yukleme, "k\tv\n" \t\n\\ kacisli, Save = .tmp + rename).
- Kok: AOT de_app_init writableRoot → de_prefs_set_root (managed ABI degismedi); .NET dev host ve editor <proje>/Library
  (ayni dosya: editorde kaydedilen ilerleme dev host'ta gorunur; Library git'e girmez). SetRoot bellegi dusurur
  (kaydedilmemis degisiklik GIDER) — test de bunu kullanir.
- Otomatik Save: GameHost.Pause (arka plan) + Shutdown + editor PlayMode.Stop. Kritik anlarda oyun kodu Save() cagirir.
- editor/PlayerPrefsTests.cs (15 PASS): gecici kokte round-trip (unicode/kacis/bos string), DeleteKey/All, Save+reload.
- Derleme: Windows DLL, NDK arm64 (de_fs + host_android), emcc de_fs OK; Apple SDK yok (CF dali derlenmedi).
- Kalan: Web icin IndexedDB'ye gecis gerekmez (localStorage ~5 MB yeter); macOS release'te NSUserDefaults tercih
  edilirse DE_PREFS_CF kosulunu __APPLE__'a genisletmek yeter (editor icin proje-yerel dosya bilerek korundu).

## SES: DESKTOP PAUSE + RELEASE DECODER'SIZ (2026-10-10)
- Durum: ses altyapisi TAM (Audio.cs + audio_shim.c: SFX mixer, Media Foundation muzik, importer decode); 2026-09-15
  notundaki "player'da hic yok" maddesi BAYAT.
- Desktop "arka plan" = ICONIFIED (odak kaybi DEGIL; masaustu oyunu odak disinda surer). PlayerApp PollIconified
  (poll, callback yok) ve host_desktop.c glfwSetWindowIconifyCallback → GameHost.Pause/Resume = de_app_pause/resume
  (SFX cihazi kapanir, muzik duraklar, dt durur) — mobil host'larla ayni yol. GLFWConst.ICONIFIED eklendi.
- DE_NO_DECODERS tum RELEASE native yollarina: build_native_static.cmd, aotcompiler BuildNativeStaticLib (define'lar
  .toolchain damgasina girdi; bayrak degisince lib yenilenir), wasm CcBuild, android CMake. Editor DLL yollari
  (build_editor.cmd/.sh, EditorPublisher) decoder'li kalir (importer). audio_shim.o 373 KB → 115 KB.
- AYNI GUN DEVAM — AudioSource + testler + onizleme + platform muzik:
  * engine/managed/AudioSource.cs: Clip/PlayOnAwake/Loop/Volume, tek voice (Unity: kaynak basina tek ses), Start'ta
    yalniz Simulating sahnede calar ([ExecuteInEditMode] YOK → editorde sessiz), OnDisable/OnDestroy durdurur,
    OnValidate Volume clamp + canli voice'a iner (animasyon/inspector ayni kanca). Muzik (Stream) component'te DEGIL:
    Audio.PlayMusic — Stream asset'i Clip alanina atanirsa AssetDatabase null doner (uyari log).
  * editor/AudioTests.cs (acilis, 33 PASS): DPCM blob/klip/voice, suspend, AudioSource yasam dongusu (sim + edit),
    WAV→DPCM decode. Zamanlama adimlari yalniz Audio.IsDeviceOpen ise (ses kartsiz CI atlar).
  * Inspector onizleme GENEL kanca: AssetImporter.PreviewHeight/DrawPreview/HidePreview (tip-ozel hardcode yok);
    InspectorPanel secim degisince HidePreview. AudioImporter: bilgi satiri (Effect: sure/Hz/kanal/KB; Stream: KB),
    Play/Stop + ilerleme (Effect: duvar saati — mixer voice konumu vermez; Stream: MusicPosition/Duration) + seek
    slider (daima cizilir: kontrol sayisi sabit). RPC select.asset(path|guid) eklendi (AI dongusu Inspector'a ulasir).
  * de_music_* artik 4 platform: Windows MF; Web EM_JS HTMLAudioElement (pak araligi C'de okunup Blob; autoplay
    reddi → ilk jestte otomatik yeniden deneme); Android JNI → platforms/android/java/.../Music.kt (MediaPlayer;
    "asset:" → AssetManager.openFd+offset, dosya → FileInputStream fd, URL; host_android.c JNI_OnLoad sinifi APP
    loader'dan cozup de_android_music_bind'e verir — GL thread FindClass bulamaz; GameActivity Music.assets kurar);
    Apple AVAudioPlayer (dosya/aralik, NSData) + AVPlayer (URL). Apple dali `__OBJC__` ister: build_editor.sh ve
    EditorPublisher audio_shim.c'yi -x objective-c -fobjc-arc derler, AVFoundation+CoreMedia linkler; duz C'de
    #warning + stub. Dogrulama: Windows/NDK arm64/emcc derlendi; Apple SDK yok (derlenmedi, elde edilince kontrol).
- Kalan: Android/iOS/Web cihazda muzik dinleme testi; ses grubu/bus (UI/SFX/Music ayri master) yok; pitch/pan yok;
  AudioSource icin PlayOneShot (ek voice) yok.

## [ExecuteInEditMode] — OYUN MANTIGI EDIT SAHNESINDE UYUR (2026-10-10)
- SAHA: PlatformFilter(platforms=IOS).Awake → gameObject.SetActive(false) EDIT sahnesinde de kostu → APPLE butonu editorde
  kayboldu (doc temizdi; kullanici "doc'a siziyor / inspector eziyor" sandi). Kok: Awake/OnEnable/Start/OnDisable/OnDestroy
  edit projeksiyonunda da calisiyordu (Update zaten simulate'e bagli); Simulating spawn aninda henuz true'ydu (Update'te set).
- COZUM (Unity modeli): engine ExecuteInEditModeAttribute (Inherited) → LifecycleFlags.EditMode (ComputeFlags IsDefined);
  Scene.EditMode (spawn'dan ONCE ayarlanir; EditorScene.Instantiate fresh.EditMode=true). Scene.Runs(c, hook): EditMode'da
  isaretsiz component'in Awake/OnEnable/Start/OnDisable/OnDestroy + transform mesajlari KESILIR. Renderer/update-slot
  kayitlari gate'ten bagimsiz (cizim surer). Isaretli motor component'leri: LayoutBox, CameraComponent, MovieClip,
  ParticleSystem (Start/OnEnable zaten Simulating'e bakar). IEditorPreview/PreviewSession etkilenmez (acik cagri).
- KURAL: oyun component'i edit modunda GORUNMEK istiyorsa [ExecuteInEditMode] + icinde Simulating kontrolu; aksi halde
  hicbir sey yazmasin — editor dogrudan doc'u gosterir. Test: LifecycleTests.EditModeGatesGameLogic. Dogrulama:
  LoginScene edit screenshot VEYA+APPLE gorunur, play (Windows) gizli.
- Inspector "onceki component yeni seciliyi eziyor" suphesi: CancelNumericEdit + KeyboardControl=0 secim degisiminde var,
  tamponlar "c:goId:compIdx:alan" anahtarli — somut yol bulunamadi; buyuk ihtimalle ustteki belirti. Repro gelirse bak.

## MOVIECLIP RPC ILE YAZILABILIR: anim.props + ic ice prop.set + comp.schema format (2026-10-10)
- SAHA: "MovieClip ile LoginScene intro" → ajan 33 adim dondu: ClipTrack.Target (CompRef) bicimini, Transform yolunu
  ("localScale"?), liste elemanina nasil yazilacagini TAHMIN etti. Kok neden: kesif araci yoktu + prop.set yalniz kok alan.
- FIX (editor/RpcCommands.cs):
  * anim.props(type): AnimRegistry.PropsOf → path/kind/channels (+enum values, refType), Transform dahil, Enabled dahil;
    hint'te ClipKey.Value "x y z w" sozlesmesi + Mask bitleri + Frame=Fps*sn.
  * comp.schema: ic ice nesne/liste elemani alanlari 'fields' altinda ozyineli; ref/vektor/renk skalerleri icin 'format'
    (CompRef "goId:TypeName:ordinal" — Transform "goId:Transform:0"; Asset guid|yol; Vec "x y z"; Color "r g b a").
  * prop.set 'prop' IC ICE YOL: Tracks[0].Keys[1].Value — sema ile yurunur (yanlis ad/index erken hata), eksik ara
    dugumler default'la yaratilir (InspectorPanel.CreateDefaultFieldNode internal yapildi), index==count → sona ekle,
    yaprakta null → liste elemani sil. Kok alanin tamami tek PropOp (tek undo; canli patch alani yeniden kurar →
    MovieClip.OnValidate Rebuild).
  * RpcHost.BindArg: JSON null → C# null (onceden Scal("") oluyordu → prop.set "null = default'a don" hic calismiyordu).
- DOGRULAMA (dev editor DIGITOY_RPC_PORT=5199, yayinlanmis editor 5157'de acikken): Scale 0→1 OutBack clip RPC ile kuruldu,
  sim.run + scene.dumpLive: 0.08 → 0.85 → 1.046 (asim) → 1.03; key sil/undo/kok null test edildi. Test sahnesi silindi.
- Talimat: MCP Instructions + copilot-instructions'a "Tween vs MovieClip siniri" + RPC tarifi eklendi.

## ANIMASYON YAZIMI → OnAnimatedPropertiesApplied (2026-10-10)
- SORUN: AnimRegistry serilesen ALANA yazar (FieldSetter; property setter yok, Unity gibi) → LayoutBox.anchoredPos/width
  tween/MovieClip ile yazilinca MarkDirty calismiyordu. Inspector ayni durumu EditorScene.WriteProp sonrasi OnValidate ile
  cozuyordu; animasyon yolu kancayi cagirmiyordu. Motion.cs "property uzerinden yaz" workaround'u bu yuzdendi.
- COZUM (Unity OnDidApplyAnimationProperties modeli, generic, alloc'suz):
  * AnimProperty.Apply(c, v) = Set + scene.NoteAnimated(c) (Trigger haric). Tween.Write ve MovieClip.SampleTrack HEP Apply.
  * Scene: _animTouched dizisi + Component._animTouched bayragi (frame basina component basina TEK kayit). FlushAnimated
    Scene.Update'te Tweens.Tick/LateUpdate'ten SONRA, sistem sweep'inden (LayoutSystem) ONCE; edit modunda da kosar
    (ClipPanel SampleFrame yazimlari da bildirilir).
  * Component.OnAnimatedPropertiesApplied() virtual, VARSAYILAN => OnValidate(). TEK SOZLESME: component "alanlarim
    disaridan yazildi, turetilmis durumu tazele" mantigini yalniz OnValidate'e yazar; inspector/undo/animasyon hepsi
    oradan gecer. Yalniz OnValidate'i pahali olan tip ayrica override eder (MovieClip: Rebuild alloc → no-op; nested
    clip'in Frame'i her frame yazilir).
  * KURAL: setter'da yan etki yok; turetilmis durum lazy (dirty + okumada coz) ve OnValidate bayragi kaldirir.
  * MALIYET: LifecycleFlags.Validate (OnValidate ya da OnAnimatedPropertiesApplied override edilmis) yoksa Apply bildirimi
    tamamen atlar — Transform/SpriteRenderer tween'inde ek is SIFIR; LayoutBox'ta yazim basina bool + frame'de bir sanal cagri.
- Test: MovieClipTests.AnimatedWriteNotifies (Tween 'width' → RectWidth; clip GotoAndStop; edit modunda SampleFrame).
  ZeroAlloc hala 0 B. Talimat tuzagi guncellendi ("property kullan" artik gereksiz).

## INSPECTOR "SET NATIVE SIZE" + asset.list/asset.info RPC (2026-10-10)
- RPC asset.list(filter,type) / asset.info(path|guid): yol/guid/tip/bayt + gorsellerde width/height + atlas uyeligi
  (trimmed bolge) + importSettings. Neden: ajan sprite boyutunu ogrenmek icin console kodu yazip calistiriyordu.
  Boyut kaynagi: once AssetDatabase.TryGetRegion (atlas OrigW/OrigH, yuklu), yoksa editor/ImageHeader.cs PNG IHDR /
  JPEG SOF basligi (saf FileStream, platform bagimsiz, decode/GPU yok; 21 proje gorselinde System.Drawing ile birebir
  dogrulandi). RPC doku yuklemesi TETIKLEMEZ (sahneye yan etki yok). MCP bridge Instructions + repo talimatina eklendi.
- Inspector: Istek: sprite boyutunu surekli elle aramak zorunda kalmamak. Tip-ozel kod YOK — sema konvansiyonu:
  Sprite tipli Asset alaninin component'inde float Width/Height kardesleri varsa (OrdinalIgnoreCase; LayoutBox
  width/height backing alanlari da eslesir) sprite satirinin altina "Set Native Size" butonu + "W × H" ipucu gelir.
  InspectorPanel.NativeSizeTargets (olcum ve cizim ayni kosul) + DrawSetNativeSize. SpriteRenderer ve LayoutBox
  otomatik kapsanir; yeni bir component ayni alan adlarini kullanirsa kendiliginden kazanir.
- Yazim doc uzerinden es.SetProp (iki ayri PropOp → iki undo adimi; grup op yok, kabul edildi). Sprite
  App.Assets.LoadAsset(guid, typeof(Sprite)) ile cozulur; async yukleme bitmeden LogicalWidth 0 ise tiklama etkisiz.

## AGENT SAHA DERSLERI 1 (2026-10-10, publish editor + yuzbirextra ile gercek kullanim)
- Ders A: prop_set 'value' MCP semasinda "object" ilan edilmisti → istemci skaler 72'yi reddetti → agent YAML'i elle
  duzenledi. FIX: kopru node tipi [string,number,boolean,object,array,null]; talimatlara "YAML'i ASLA dogrudan duzenleme,
  arac calismazsa atlatma bildir" kurali (sablon + yuzbirextra + repo).
- Ders B: agent motor API'sini bilmedigi icin proje disina (engine kaynagi) cikmaya calisti; publish kullanicisinda kaynak
  yok. FIX: RPC engine.api(type, filter) — public tip listesi / uyeler (C# imza) + protected override KANCALARI (hook) +
  o tipe uygulanan extension metodlar (Tween*). Talimat: "motor kaynagi projede yok, engine_api kullan" + sik API ozeti.
- Ders C: "sari tasi butonlarin altina al" → agent scene.reparent ile ayni parent'ta SIRA degistiremedi (parametre yoktu),
  SortingOrder'a sapti, Instant HR her yazimda sahneyi frame 0'a aldigi icin MovieClip'in farkli anini gorup "10 tasi
  kayboldu" sandi. FIX: scene.reparent(id,parent,insertBefore=-1) → MoveGameObject (0=sona; ayni parent=yalniz sira);
  PlayMode.SpawnedDocVersion/PlaySceneStale; sim.run bayat play'i yeniden dogurur (yanit "restarted"; restart=true zorlar),
  input.* RequirePlayScene ayni; render.screenshot "stale":true. Talimat: cizim sirasi kurali + "ayni frame sayisinda
  kiyasla". NOT: HR=Instant projede stale olusmaz (editor zaten reload eder) — bayrak OnSave/Off icin.
- Ders D: agent LoginScene'de scene.addGo'yu PARALEL cagirdi → kardes sirasi karisti (RpcHost kuyrugu ana thread'de
  sirali kosar ama paralel istemci cagrilarinin TCP'ye VARIS sirasi belirsiz). FIX: scene.addGo(parent,name,insertBefore=0)
  → EditorScene.AddGameObject(parent, insertBeforeId) tek AddGoOp ile dogru index'e Insert; talimat: yapisal araclari
  paralel cagirma, sira onemliyse ardisik ya da insertBefore.
- IdeFiles.Instructions sablonu $@"" → raw string $""" (icerikte cift tirnak serbest; iki kez derleme kirdi).
- ACIK EKSIK: LayoutBox rich text yok (inline bold/altcizgi/link) — "Hizmet Kosullari" tipi metin icin markup + span tiklama
  ayri is adayi.

## NOTLAR REPOYA TASINDI + PROJE IDE/AI DOSYALARI (2026-10-10)
- Bu dosya eski /memories/repo notunun aynisi (baska makinede git ile devam). Kurallar: .github/copilot-instructions.md.
- editor/IdeFiles.cs — proje acilisinda (WriteIdeProject yaninda): .vscode/mcp.json (bridge dll yolu: kurulu <exe>/tools/
  mcp-bridge, dev repo bin/Debug; MAKINEYE OZEL, icerik-kiyasli yenilenir, gitignore'lu), .github/copilot-instructions.md
  (oyun-tarafi sablon, YALNIZ yoksa — kullanici duzenler, git'e girer), .gitignore (yoksa: Library/Build/UserSettings +
  *.Game.csproj + .vscode/mcp.json). Repo .gitignore'a Projects/*/*.Game.csproj + .vscode/mcp.json eklendi;
  yuzbirextra.Game.csproj git'ten cikarildi (--cached).
- EditorPublisher Managed adimi tools/mcp-bridge'i publish eder → paket tools/mcp-bridge/mcp-bridge.dll (hash'e dahil).
- NOT: Assets altindaki .intent.md'ye de .meta uretiliyor (ScanMetas her dosyaya) — zararsiz, istenirse .md haric tutulur.

## AI GOZ+EL RPC (2026-10-10, uctan uca dogrulandi: iphone-14 390x844 screenshot dogru yonelimli)
- KARAR (tartisma): "gunler bazinda oyun" = AI kapali devre; eksik 1) goz/el RPC 2) MCP sarmalayici
  3) oyun-tipi kit 4) kurallar saf C#+test 5) spec→intent akisi. Madde 1 YAPILDI.
- KOORDINAT SOZLESMESI: her sey MANTIKSAL nokta (Screen.width/height, sol-ust, y asagi);
  render.screenshot default scale=1 = mantiksal boyut → goruntu pikseli == input koordinati.
- GameOutput: CIHAZ PROFILI modu SetDevice(physW,physH,pixelRatio,name) — BeginFrame'de panelden
  bagimsiz uygulanir (Game sekmesi gizliyken de), ViewW/H=phys/ratio, RT=phys; PixelRatio prop
  (Forced? cihaz : Gui.Scale) → EditorApp Screen.pixelRatio buradan. Devices[] preset tablosu
  (iphone-se/14/14-pro-max, pixel-7, galaxy-s23, ipad, ipad-pro-12, desktop-1080p).
  EditorSettings.deviceName/W/H/PixelRatio kalici. view.set/device yaniti SONRAKI frame'de gecerli.
- Readback: shim de_sokol_read_image(img,w,h,rgba) YALNIZ GLCORE (glfwGetProcAddress ile FBO+
  glReadPixels, sonra sg_reset_state_cache); Metal/GLES stub 0 doner. DllImport EDITOR'de
  (GameOutput.cs, Sokol.cs'e eklenmedi — player AOT link'i kirilmasin). Texture.ImageId internal.
  GL satir 0 = alt → Screenshot satir cevirir; kutu filtreli kucultme; PngWriter (editor, RGB, zlib).
- RPC: view.get/set/device/devices, render.screenshot(path,scale), input.pointer(action,x,y),
  input.tap(x,y,holdFrames) (down → N frame → up → +1 frame doner), input.drag(x0,y0,x1,y1,frames).
  input.* play degilse baslatir. Screenshot default: <proje>/Library/Screenshots/shot_NNNN.png.
  sim.run(frames, stop, screenshot=true) → dump + "screenshot" alt nesnesi.
- ORIENTATION (2026-10-10): PlayerSettings.orientation string → enum ScreenOrientation{Landscape,Portrait,Auto}
  (Player Settings penceresinde sema'dan otomatik combo). Yaml'lar "Landscape" olarak migre edildi
  (enum parse case-sensitive!). Android/aotcompiler tuketicileri ToLowerInvariant ile eski "landscape"
  metnini alir. view.device(name, orientation="") yonu PlayerSettings'ten alir (Auto=preset dogal
  yonu), swap = preset yonu != istenen; yanitta "orientation". KURAL: sim/screenshot proje yonune sadik.
- MOTION PRESET PROOF (2026-10-10, calisti): yuzbirextra Assets/Common/Scripts/Motion.cs (oyun kodu, engine'e
  TASINMADI) — MotionPreset enum (PopIn/SlideUp/Down/Left/Right/DropIn/Shake/Pulse), kendi zamanini surer,
  LayoutBox.AnchoredPos PROPERTY uzerinden (field tween dirty bypass tuzagi). LoginScene'e RPC ile baglandi
  (BUTTONS SlideUp, butonlar PopIn stagger, POLICY SlideUp) + frame screenshot'lari dogruladi. SAHNE KAYDEDILMEDI
  (editor acik, undo'lu). LoginScene.intent.md yazildi. Tartisma sonucu: gorsel iletisim = goster (referans/
  screenshot) + elestir (varyant+not) + isimli sozluk (motion preset/token) + diff'ten zevk ogrenme.
- MCP KOPRUSU (madde 2, YAPILDI, stdio ile dogrulandi): tools/mcp-bridge (net9 exe, bagimliliksiz
  System.Text.Json JSON-RPC). rpc.describe → tools/list (ad "scene.addGo"→"scene_addGo"; int→integer,
  float→number, bool→boolean, node→object; default'suz=required). tools/call → TCP RPC; sonucta
  path *.png (kok veya "screenshot" cocugu) → image content (AI goruntuyu dogrudan gorur). RPC hatasi
  → isError. Editor kapaliysa tools/list bos. .vscode/mcp.json "digitoy-editor" kaydi (dll yolu —
  once dotnet build tools/mcp-bridge). Editor yeniden baslayinca kopru otomatik yeniden baglanir.

## PLAYER SCRIPT KAPSAMI FIX (2026-10-09): aotcompiler GameAotDll Assets/Scripts yerine Assets/**/*.cs
  (Editor/ haric) tarar — editor GameCode ile AYNI kapsam. Belirti: player "[baked] bilinmeyen component
  tipi atlandi: X" (X Assets/Common/... altindaydi). KURAL: kaynak tarama kapsami editor=player.
- Screen.pixelRatio eklendi (fiziksel/mantiksal; GameHost.Frame scale, EditorApp uiScale). Android density,
  wasm devicePixelRatio, desktop GLFW content scale olarak zaten host'tan geliyor.
- yuzbirextra CameraScaler: referenceResolution FIZIKSEL px (pixelRatio'ya bolunur), persp z =
  max(refH, refW/aspect)/(2 tan(fov/2)); kok kutu fitScreen kamera rect'ini izler.

## IDE CSPROJ (2026-10-09): GameCode.WriteIdeProject(project) proje kokune <Ad>.Game.csproj yazar
  (EditorApp acilista, CompileAndLoad oncesi). Unity Assembly-CSharp modeli: YALNIZ IntelliSense,
  editor derlemesi in-process Roslyn (csproj'u kullanmaz). Glob Assets/**/*.cs (Editor/ haric),
  Sdk.props/targets explicit import ile bin/obj Library/IdeBuild/'e. Icerik ayniysa yazmaz. dotnet build ile dogrulandi.

## NATIVE FILE DIALOG + NEW PROJECT (2026-10-09, build yesil, kullanici test edecek)
- KOK NEDEN ("Open Project not responding"): .NET ana thread MTA → SHBrowseForFolder/IFileDialog
  STA ister, MTA'da takiliyordu. FIX: de_dialog_file (sokol_shim.c Win32) diyalogu AYRI STA
  thread'de kosar, ana thread MsgWaitForMultipleObjects+PeekMessage ile pompalar (pencere canli).
- API: de_dialog_file(win, mode 0=ac|1=kaydet|2=klasor, title, filters "ad|*.ext;*.e2|...",
  defaultPath, out, cap). Win32 = IFileOpen/SaveDialog (COBJMACROS, GUID'ler elle tanimli);
  macOS = NSOpenPanel/NSSavePanel (allowedFileTypes). de_dialog_pick_folder = mode 2 sarmali.
- C#: editor/NativeDialog.cs OpenFile/SaveFile/PickFolder (null=iptal). ProjectSwitcher:
  File/New Project... (order 9; SaveFile diyalogu klasor adi girisi; Seed = Project.Load +
  productName + scenes + Assets/Scenes/Main.scene bos SceneDoc + UserSettings/EditorSettings
  lastScene) → Open → relaunch. Open Project artik NativeDialog.PickFolder.
- Menu id'leri kaydi: New Project=22, Open Project=23, Open Recent=25 (sendmenu).

## AOT TRACE MALIYETI A/B (2026-10-09, UYGULANDI: MODE4 = satir store'u yalniz firlatma dali/cagri oncesi)
- UYGULANAN (vmrt.h + CTranspiler.cs): DIGITOYENGINE_SETLINE yalniz Call/CallVirtual/CallIndirect/New*/Throw/Cast/Unbox
  oncesi (EmitLine; jump hedefinde dedup sifirlanir). Sicak check'ler *_AT(…, line): BOUNDS_AT/DIVCHECK_AT/NULLCHECK_AT
  satiri unlikely dalinda SETLINE ile yazar. NULLCHECK_AT HER platformda EXPLICIT (MMU/VEH emniyet agi) ->
  NRE satiri sembol dosyasiz kesin (Test60.cs:46:13 dogrulandi). PUSH sonrasi compiler barrier eklendi
  (TBAA ilk null deref'i frame store ustune tasiyor, VEH sp=0 goruyordu -> 'native fault' raporu).
- Sonuc bench: trace 321ms vs NOTRACE 326ms (ESIT; onceden 486 vs 297). #line/PDB/DbgHelp yolu DENENDI ve
  SILINDI (kullanici: harici sembol dosyasi istemiyor; DbgHelp ic ice inline'da ic satiri kaybediyordu).
- Bench: 4096 int-struct dizi guncelleme + 256 sanal cagri x3000 iter, x10 (gecici Test60, silindi).
  Ayni generated.c, clang -O2: trace 486ms | yalniz LINE kaldirilmis 379ms | NOTRACE 297ms (startup ~30ms).
  → DIGITOYENGINE_LINE ~%23, PUSH/POP shadow stack ~%18; ikisi birden ~1.7x. Bounds/null check olculebilir degil
  (native NULLCHECK zaten bos, BOUNDS tek unsigned cmp).
- TUZAK: NOTRACE ile tam selftest CRASH eder — VEH null→NRE cevirisi DIGITOYENGINE_sp>0 ister (sp=0 → native fault
  + 2sn crash dump). NOTRACE release'e alinacaksa VEH handler'in sp kosulu kaldirilmali.
- Hizli A/B yontemi: obj/selftest-c/generated.c'de `Program_Main();` satirini hedef fonksiyonla regex-replace edip
  emsdk clang ile (-O2 / -DDIGITOYENGINE_NOTRACE) ayri exe derle; pipeline tekrar kosmaz.
- Diger adaylar (olculmedi): rank-1 dizi fast path (ArrayIndexSetup rank/dims load'lari), inline gc_write_barrier.
- LINE ALTERNATIFLERI mikro-bench (obj/selftest-c/linebench.c, -DMODE=n, clang -O2, ayni is yuku): izleme yok 214ms |
  mevcut stack[sp-1].line 530 | tek global int pc 307 | 16-bit pc 465 (DAHA YAVAS, kullanma) | yerel register pc +
  global'e yalniz throw dalinda/cagri oncesi yazim 222 (≈bedava). Sonuc: global pc mevcutun yarisi; asil maliyet
  store'un alias etkisi (a->len/data yeniden yuklenir) — yerel pc bunu sifirlar ama native null-fault (VEH) satiri
  bayat olur (son cagri/publish noktasi). PUSH/POP tum modlarda (0 haric) acik.

## PARTICLE SYSTEM (2026-10-05, 19 test PASS + zero-alloc asserti, kullanici gorsel bekliyor)
- engine/managed/ParticleSystem.cs: `ParticleSystem : Renderer` [Previewable], tek component +
  STRUCT moduller (CopyTo deger kopyalar; class modul klonla paylasilirdi): emission/shape/
  velocity/noise/rotation/textureSheet + colorOverLifetime (ColorGradient class, 64 Color LUT,
  key imza kiyasiyla rebake) + sizeOverLifetime (LifeCurve struct: Constant|Ease(from,to)|Curve
  + randomize; Curve key imzasi degisince Version++). FloatRange/Vec3Range (min..max).
  Parcacik AoS Particle[] (maxParticles kapasite, swap-remove), xorshift32 RNG (randomSeed).
- Step sirasi: ONCE Advance(dt) mevcutlar, SONRA emisyon (Spawn frame-ici dogum yasini kendi
  uygular — ters sira cift ilerletme bug'i yasandi). Burst (t0,t1] penceresi, loop sarmasinda
  ResetBursts + FireBursts(-eps,t1). rateOverDistance emitter dunya konumu farkindan.
- Yon kanonu: sekil "ileri" = -Y (ekran yukari). Cone 2D/3D (emit3D), Circle/Sphere radyal,
  randomDirection harman. Rotasyon xyz euler Transform kuraliyla ayni (RotScaleCS); rot.x=y=0
  hizli yol. Stretched: kolon-0 = hiz yonu (atan2'siz), sx = size*lengthScale + |v|*speedScale.
  Local: MulAffine(world, model) (tam Multiply'in yarisi). World: dogumda TransformPoint/Vector.
- Cizim: Mesh.Quad instanced DrawMesh/parcacik, Shared materyal (ForBlend/ForEffects) → tek draw.
  KARELER = `List<Sprite> sprites` (textureSheet grid KALDIRILDI, kullanici istegi): her kare
  ayri Sprite (atlas bolgesi olabilir). Encode basinda ResolveFrames → _frames FrameUv[] (uv +
  trim olcek/ofset + sayfa + aspect; yalniz uzunluk buyuyunce alloc). Farkli sayfa → per-particle
  mat.MainTexture mutate (DrawMesh aninda yakalanir; ayni sayfa merge). `frames` FrameModule
  {mode Lifetime|Fps|RandomFrame, cycles, fps, startFrame}. aspect=0 → sprite dogal orani.
  Trim ofseti: kolonlar boyutu tasidigi icin normalize (fox/foy) ile — size carpma (yasandi).
  Sub-emitter: subEmitterOnDeath.Emit(n, worldPos).
- Editor preview: engine `IEditorPreview.PreviewStep(dt)` + PreviewSession.Step(dt)
  (EditorApp editLive.Update sonrasi) — Update edit modunda kosmadigi icin preview boyle akar.
- editor/ParticleTests.cs (rate/omur, burst, loop+stopAction, world+distance, draw=count +
  0 B alloc, sub-emitter). YAPILMADI: kamera-yuzlu billboard (Encode kamera bilmez), trail,
  yas sirali cizim (swap-remove sira bozar), collision, Inspector icin kompakt Range editoru.

## RADIAL GRADIENT (2026-10-02, build yesil, kullanici gorsel bekliyor)
- GradientType.Radial: color=merkez, color2=kenar. At() Radial'de color2 doner (border/text/
  textured tuketiciler duz dis renge duser). Visible: type!=Solid && color2.a>0.
- LayoutBox.EmitRadial: taban EmitLayer(color2 duz) + 4 aynali ceyrek quad, Shadow parcasi
  (1-d/128 lineer rampa). (a) smoothstep profili FAZLA YUMUSAK bulundu -> (b) USER.w>0 ham
  rampa bayragi SdfFragment'a eklendi (alpha=d*color.a), user=(0,0,0,1). USER.w artik DOLU.
- Yapilmadi (tartisildi): radialScale/merkez ofseti, textured kutuya overlay, tam disk parcasi.

## PROJECT PANEL DIS-DOSYA REFRESH (2026-09-21, build yesil, kullanici gorsel bekliyor)
- BUG: editor acikken Assets/'e disaridan kopyalanan dosya (font vb.) Project panelinde
  gorunmuyordu — ProjectPanel _rows cache'i yalniz kendi _dirty'sinde kuruluyordu.
- FIX: AssetWatcher.FileListVersion (dosya SETI degisince ++ — ApplyAssetChange'de
  ScanMetas kosulan yeni/silinen/rename dalinda); ProjectPanel OnGui _seenListVersion
  kiyasiyla _dirty=true. AssetChangeVersion (Inspector cache) dokunulmadi.

## SERIALIZER FieldInfo ARINDIRMA — TAMAMLANDI (2026-09-15, dilim d; 13 registrytest + tum acilis testleri PASS)
- KARAR (tartisma, kullanici): editor ZENGIN (reflection serbest), release YALIN (custom AOT
  derleyebilecek kadar) — release sozlesmesi: Create + alan YAZMA (deserialize) + CopyTo/ref-remap
  + GetFloat/SetFloat. Serialize YONU (WriteField/Capture) release'te gereksiz (simdilik durur,
  ileride #if DE_EDITOR adayi). Binary DocNode + build-time prefab expand = AYRI dilim (olc, sonra).
- FieldSchema CEKIRDEK uyeleri eklendi: FieldType, DeclaringType, Get/Set (boxed Func/Action),
  NewList/NewArray/NewElement (kurucu delegeler). Serializer/clone IC YOLLARI YALNIZ bunlari
  kullanir: ReadField/ReadElement/WriteField/ShowIfVisible (SerializedType.cs) +
  GameObject.RemapRefs — Info.GetValue/SetValue/Activator cagrilari kalkti.
- Build (reflective yol) delegeleri FieldInfo'dan doldurur (f.Get = fi.GetValue vb.);
  CatalogWriter duz lambda doker: public → ((D)o).f, private/readonly → UnsafeAccessor thunk,
  STRUCT sahibi Set → Unsafe.Unbox<D>(o).f (box icine yazar; thunk struct'ta ref parametre).
  Kurucular: NewArray = n => new T[n], NewList = () => new List<T>(), NewElement = new T() /
  (object)default(T). Info = GetField emisyonu + B const SILINDI → Registry.g.cs %100
  reflection'siz (kalan tek istisna: private ctor Activator fallback + RegisterReflective fallback).
- CanEmit sikilasti: her serilesen alanin FieldType'i erisilebilir olmali (Set cast'i tip adlandirir);
  degilse tip RegisterReflective fallback'ine duser.
- Editor tuketicileri delegelere gecti: InspectorPanel.Fields (Get/Set/FieldType/DeclaringType),
  DragDrop, BuiltinTools, RpcCommands. Info alani DURUYOR ama tuketicisi yalniz Build-zamani
  (EnsureFloatAccessors expression compile, CatalogWriter emisyonu); uretilmis semada Info=null.
- RegistryTests: Info kiyasi yerine FieldType/DeclaringType + Get/Set dolulugu + kurucu delege
  paritesi; smoke'a boxed Set/Get asserti (13 PASS).
- KALAN (bu koldan): Parse icindeki Enum.TryParse(Type,...) hala tip-tabanli (AOT gununde enum
  parse tablosu uretilebilir); WriteField'i DE_EDITOR'a alma; binary DocNode.

## ★ KALDIĞIMIZ YER (2026-09-15 sonu — RELEASE ALTYAPISI oturumu, devam edilecek)
- OTURUM OZETI: release derleme altyapisinin 4 dilimi bitti (detaylar ilgili bolumlerde):
  1) CATALOG CODEGEN — reflection katalogu → CatalogWriter → Roslyn in-memory → CANLI katalog
     uretilmis kod (editor=player yakinsamasi). Her acilis/reload'da diff-assert.
  2) RELEASE YESIL — engine -c Release temiz (SYSLIB0050 pragma'landi).
  3) DIGITOYPLAYER — player/ exe, pak + loose iki modda 10sn crash'siz boot etti.
  4) SERIALIZER FieldInfo ARINDIRMA — Registry.g.cs %100 reflection'siz, ic yollar boxed
     Get/Set delegelerinden (en ustteki bolum). 13 registrytest + tum acilis testleri PASS.
- MIMARI KARAR (kalici): editor ZENGIN (reflection serbest, kaynak = SerializedType.Build),
  release YALIN (custom AOT derleyebilecek kadar): Create + deserialize (alan yazma) +
  CopyTo/ref-remap + GetFloat/SetFloat. Serialize YONU release'te kullanilmiyor.
- BEKLEYEN KULLANICI DOGRULAMASI: player'i gorsel ac (player\bin\Debug\net9.0\DigitoyPlayer.exe,
  arg'siz Sandbox) — sahne/sprite/font/tiklama? Editor de bir tur gorsel kontrol (Inspector
  alan duzenleme ozellikle: asset inspector + doc modu artik Get/Set delege yolundan).
- RELEASE TODO (mutabik oncelik sirasi):
  a) Pak export adimi: "Build Player" menusu — Build Asset Pack → player klasorune kopyala
     (Registry.g.cs + Digitoy.Registry.dll + Game.dll + game.pak + player exe tek klasore).
  b) game.pak TAZELE (mevcut pak Sprite rename ONCESI olabilir — bozuk icerik gorulurse once bu).
  c) Atlas release loader (player'da AtlasBuilder yok; ATLS artifact: sayfa + guid→rect manifesti —
     su an sprite'lar kaynak texture'dan cizer, batch kaybi).
  d) WriteField/Capture/ShowIfVisible govdelerini #if DE_EDITOR'a alma (player binary'sinden
     serialize yonu tamamen duser) — kucuk is, istenirse hemen.
  e) Binary DocNode + build-time prefab expand (pak'ta YAML yerine binary doc; OLC sonra karar —
     sahne YAML'lari kucuk, parse maliyeti kanitlanmadan yapma).
  f) Custom AOT kalintilari: Parse'taki Enum.TryParse(Type,...) → uretilmis enum parse tablosu;
     private ctor Activator fallback'i; RegisterReflective fallback'li tipler (private nested/generic)
     AOT'de calismaz — o gun geldiginde envanter cikar.
  g) Ses altyapisi (player'da hic yok).
  h) Yan kapi: oyun kodunu da Roslyn in-memory derleme (hot reload ~1.5s→~200ms).
- TUZAK NOTLARI: player katalog icin ONCE EDITORLE ACILMIS proje ister (Digitoy.Registry.dll +
  Game.dll Library/Build'de; yoksa acik hata firlatir). Registry game ALC'sinde yasar, reload'da
  birlikte olur. Editor-assembly Component'leri (test Recorder'lari vb.) registry'ye YAZILMAZ,
  reflection'la eklenir. UnsafeAccessor thunk'lari private/readonly alan; IVT internal tip icin.
  Uretilmis semada Info=null — Info'ya dokunan yeni kod YAZMA (Get/Set/FieldType/DeclaringType kullan);
  Info yalniz Build-zamani araci (EnsureFloatAccessors, CatalogWriter).

## RELEASE DILIM 2+3: RELEASE YESIL + DIGITOYPLAYER (2026-09-15, iki modda boot dogrulandi, kullanici GORSEL bekliyor)
- Release build ZATEN yesildi (DE_EDITOR disiplini); tek SYSLIB0050 uyarisi ObjectSerializer.IsAssetType
  pragma'siyla susturuldu.
- player/DigitoyPlayer.csproj + PlayerApp.cs (unsafe, ~200 satir): GUI'siz runtime host.
  arg[0]=proje koku (default Projects/Sandbox). project.yaml'dan name+startScene (Yaml.Parse).
  Katalog: Library/Build/Digitoy.Registry.dll Assembly.LoadFrom + Registry.RegisterAll —
  reflection TARAMASI yok (editor RebuildCatalog artik PE'yi diske de yazar; once editorle ac sarti).
  Game.dll varsa once LoadFrom (registry referansi adla ayni baglamdan cozulur).
- Asset kaynagi: Build/game.pak varsa PakSource (guid tablosu pak'tan); yoksa loose Assets/ +
  ArtifactResolver fallback = Library/Artifacts/<PathToGuid(key)>/main (dev konforu, import kosmaz).
- Dongu: PollEvents → assets.Tick → Scene.UpdateAll(dt,lw,lh,simulate:true) → Pointer besleme
  (GetCursorPos/GetMouseButton polling, /mouseScale, Down/Move/Up edge; STANDALONE POINTER BORCU KAPANDI)
  → scene.DriveCameras(pool,lw,lh) (kamera yoksa fallback'i KENDISI kurar, >=1 doner; Target=null=
  swapchain) → cam.Encode(cb,fbw,fbh) → Submit/Commit/SwapBuffers. GL/Metal ayni csproj deseni.
- Dogrulama: pak modu + loose modu 10sn crash'siz kostu ([player] log satirlari). GORSEL kullanicida.
  DIKKAT: mevcut game.pak ESKI olabilir (Sprite rename oncesi) — icerik bozuksa Build Asset Pack ile
  tazele veya pak'i sil (loose fallback).
- KALAN: pak build'i player export adimina baglama ("player'a kopyala"), atlas release loader'i
  (player'da AtlasBuilder yok — sprite'lar kaynak texture'dan cizer), ses. (Serializer FieldInfo
  arindirma YAPILDI — en ustteki bolum.)

## RELEASE ALTYAPISI DILIM 1: CATALOG CODEGEN (2026-09-15, 11 test PASS, canli calisiyor)
- KARAR (kullanici onerisi, Roslyn source-generator IPTAL): reflection'la kurulan CANLI
  TypeCatalog C# koduna SERILESTIRILIR (editor/CatalogWriter.cs) — kural motoru tek
  (SerializedType.Build/ComputeFlags/importer kayitlari), yazici sadece SONUCU doker.
  Roslyn generator'in semantik ciftlenmesi (IsAssetType runtime importer kaydi, [Serializable]
  pseudo-attr, ctor erisim...) bilerek reddedildi.
- Akis (EditorApp.RebuildCatalog): refCat=FromAssemblies → CatalogWriter.Write(engine+game
  tipleri; editor-asm tipleri YAZILMAZ, reflection'la eklenir) → Library/Build/Registry.g.cs
  (player export ayni dosyayi diske derler) → RegistryCompiler.Compile (Roslyn in-memory,
  Microsoft.CodeAnalysis.CSharp 4.12.0 editor paketi; MSBuild subprocess yok, ~50-150ms)
  → GameCode.LoadRegistry (game ALC'ye LoadFromStream; reload'da birlikte olur) →
  DigitoyEngine.Generated.Registry.RegisterAll(cat) → RegistryTests.CatalogsEqual DIFF-ASSERT
  → CANLI katalog = uretilmis olan (App.RegistryActive). Hata/fark → reflection'a fallback + log.
- Uretilen kod: Create=new X() (private ctor→Activator fallback), CopyTo=duz atamalar,
  Flags=(LifecycleFlags)N sabit, Schema=FieldSchema[] literal (Info=GetField ile doldurulur —
  serializer ic yollari hala FieldInfo, tam reflection-free sonraki is), GetFloat/SetFloat
  onceden dolu lambda'lar. Private/[SerializeField]/readonly alanlar [UnsafeAccessor] ref-thunk
  (visibility bypass, AOT-uyumlu; custom AOT gununde intrinsic implement gerekir).
- Erisilemeyen tip (private nested/generic) → cat.RegisterReflective(Type.GetType(...)) fallback
  satiri (TypeCatalog.RegisterReflective = FromAssemblies govdesinden refactor edildi, public).
- IVT: engine csproj InternalsVisibleTo("Digitoy.Registry"); GameCode uretilen csproj'a
  RegistryVisibility.g.cs enjekte eder (internal oyun tipleri gorunsun). Registry asm adi
  CatalogWriter.AssemblyName="Digitoy.Registry". GameCode.GameDllPath (registry ref'i icin).
- TypeCatalog.Aliases public (writer alias'lari koda gecirir). RegistryTests.Run acilis
  testi (11 assert: aktiflik, Create/CopyTo/floats/thunk/ShowIf). Yan kapi (yapilmadi):
  ayni Roslyn altyapisiyla oyun kodunun kendisi de in-memory derlenebilir (hot reload ~200ms).
- KALAN RELEASE DILIMLERI (mutabik sira): 2) Release build yesile cek, 3) DigitoyPlayer exe
  (Windows minimal: GLFW+sokol init+PakSource+startScene+Pointer besleme), 4) pak boot +
  "player'a kopyala", 5) serializer ic yollarinin FieldInfo'dan arindirilmasi (custom AOT gunu).

## PROSEDUREL UCGEN GLYPH (2026-09-14, build yeşil, kullanıcı görsel bekliyor)
- Sorun: ▾/▸ fontta yok → .notdef karesi (combo/inspector header), hiyerarşi ">"/"v" harfti.
- GuiFont.BakeTriangle: U+25B2..25C4 alt kümesi (▲▴▼▾▶▸►◀◂◄) raster yolunda PROSEDUREL
  pişer (4x4 supersample, taban=physPx*0.5, yükseklik=0.6*taban, y0 cap ortasına hizalı) —
  aynı dinamik atlas, draw call artmaz. GetRasterGlyph native çağrıdan önce dener.
- Kullanım standardı: katla/aç = ▾/▸ (hiyerarşi + inspector header + tüm foldout butonları,
  eski "-"/"+" değişti); liste taşı = ▴/▾ (eski "^"/"v"). TimelinePanel legacy dokunulmadı.

## INSPECTOR COLLAPSABLE COMPONENT HEADER (2026-09-14, build yeşil, kullanıcı görsel bekliyor)
- Unity tarzı: ▾/▸ ok + enabled checkbox(x=24) + tip adı(x=46) + koyu şerit + sağda x.
- Başlık tıklaması katlar: HeaderClick (çizimsiz kontrol, ButtonCore deseni) — toggle/x
  önce çağrıldığı için MouseDown'ı onlar yutar. Durum _collapsed HashSet<long>,
  key=(goId<<32)|compIndex, default açık. MeasureHeight katlanmışta RowH+2 sayar
  (ölçüm=çizim koşul eşitliği kuralı). Ok glyph'i raster on-demand codepoint.

## UI SORT KEY FIX (2026-09-14, kullanıcı doğruladı): SceneView gizmo/toolbar/çerçeve
kayboldu — RenderQueue pip<<40 tie-break'i Ui modunda layer bitleriyle (layer~1000,
<<32 → bit 35-41) ÇAKIŞIYORDU; pixel-effect işi pip numaralarını büyütünce RT quad'ı
(One/Zero) overlay'lerin üstüne sıralandı. Fix: Ui de Transparent gibi pip/texView
key'e GİRMEZ (aynı layer = submit sırası, radix stable merge korunur). Transparent
için aynı fix 09-12'de yapılmıştı; Ui'da unutulmuştu.

## FLICKER KURALI (2026-09-14): GuiRenderer.Draw* YALNIZ Repaint'te çağrılır —
guard'sız çağrı her pass'te (Layout + her input event + Repaint) yeniden emit eder,
pass sayısı event'lerle değişince metin kalınlığı oynar = flicker. Inspector component
başlığında yaşandı (InspectorPanel.cs ~112, tek kaçak site, fix: Repaint guard).

## PIXEL EFFECT ZİNCİRİ (DİLİM 2) — TAMAMLANDI (2026-09-14, 18+14+19+14+9 test PASS, kullanıcı görsel bekliyor)
- .fx asset modeli: gövde `VEC4 fx(VEC4 c, VEC2 uv)` — c DUZ renk (kanon), uv LOKAL 0..1.
  Renderer.Effects (List<PixelEffect>, sıra=uygulama) TÜM renderer'larda (sprite/kutu/metin).
  Inspector liste editörü (eleman ekle + asset drag-drop) jenerik altyapıdan bedava.
- Compose (Shader.ComposeEffects): core fs_main→fs_core rename + fx→fx<i> + üretilmiş fs_main
  zinciri tek shader'da uygular (ek pass yok). MSL yolu: fs_core/fx<i> makrolarıyla tex/smp
  threading (tanım makro-rewrite, çağrı _impl'e explicit — makro çağrıda tetiklenmez kuralı).
- localUv varying (v_uvLocal=a_uv, atlas-remap'siz): YALNIZ composed shader'lar öder
  ({LOCAL_IN/OUT/ASSIGN} template placeholder'ları + _fragLocalPrefix #define LOCALUV).
- FxCompose (PixelEffect.cs): İÇERİK-anahtarlı cache — (core ref + efekt ref dizisi + Version
  dizisi); farklı liste NESNELERİ aynı içerikte AYNI composed shader'ı paylaşır (batch korunur,
  çift derleme yok). Frame yolu: ref+int kıyaslı lineer tarama, alloc/string SIFIR.
  Version değişince (reload) girdi yerinde rebuild. Null elemanlar atlanır.
- Material.ForEffects: composed-Shader-ref anahtarlı varyant listesi (MainTexture her seçimde
  tabandan eşitlenir). Zincir emisyonu: base.ForBlend(BlendMode).ForEffects(Effects) 5 sitede.
- FALLBACK: Shader.TryCreateEffect — sokol FAILED shader'ı pipeline'a BULAŞMADAN yakalar
  (destroy + null + hata metni); zincir derlenmezse TAMAMI devre dışı, EFEKTSİZ CORE ile çizer
  + PixelEffect.LogError (editörde EditorLog.Error → Console paneli). Yarım zincir yok.
- ★ SOKOL TUZAĞI (yaşandı, saatler): sg_resource_state enum'una yeni sürümde UNSEALED girdi —
  VALID artık 2 değil! Shim'den enum SIZDIRMA: de_sokol_shader_valid boolean export'u.
- Hata detayı: sokol GL compile info log'u ERROR'dan sonra INFO seviyesinde gelir —
  shim de_sokol_log ERROR'u yakalar + pending bayrağıyla İZLEYEN INFO'yu append eder;
  de_sokol_last_error() → C# (satır numarası + GL hatası Console'a düşer; DSL gövdesi
  wrapper prefix'i yüzünden ~18 satır kayık).
- .fx importer (editor/FxImporter.cs): metin passthrough artifact (pak yolu bedava).
  AssetDatabase: _importers[.fx]=PixelEffect, LoadPixelEffect (senkron, instance kalıcı),
  InvalidateImported .fx'i AYNI instance'ta yerinde tazeler (Version++) → watcher jenerik
  dalıyla canlı hot-reload (renderer'lar anında iyileşir, sahne reload'suz).
- Örnekler: Projects/Sandbox/Assets/Effects/Grayscale.fx + Vignette.fx.
- editor/PixelEffectTests.cs (açılışta, 9 assert): trivial/localUv probe, compose valid,
  içerik-anahtar paylaşımı, USER'li core (UiShader) compose, bozuk fx→core+log, reload
  iyileşmesi, 2'li zincir. Kasıtlı bozuk test LogError'u geçici yutar.
- KALAN/ertelenen (mutabık): TIME makrosu, parametre kanalı (USER per-instance /
  materyal uniform), shine, LayoutBox USER çakışması nedeniyle kutu efekt PARAMETRESİ yok
  (efektin kendisi çalışıyor), composed shader eski sokol handle sızıntısı (editör-only).

## BLEND KANONU + BLENDMODE (2026-09-14, build + 18+14+19+14 test PASS, kullanıcı görsel A/B bekliyor)
- KARAR ZİNCİRİ (uzun tartışma): shader asset + shine ERTELENDİ/ATILDI → yerine PIXEL EFFECT
  ZİNCİRİ modeli seçildi (core fs_main dokunulmaz, fx(c, uv) son rengi işler, composer string
  cerrahisiyle tek shader'a birleştirir, varyant cache (core, zincir) anahtarlı). Efekt gövdesi
  .fx DOSYASI olacak (kodda kayıt istenmedi; importer trivial). fx imzası fx(c, uv) — uv LOKAL
  0..1 (v_uvLocal varying eklenecek, atlas-uzayı uv tuzak). DİLİM 2 HENÜZ YAPILMADI.
- DİLİM 1 YAPILDI — BLEND KANONU: fs_main DUZ (straight) renk döner; premultiply TEK yerde,
  wrapper suffix'te (GL _fragSuffix + 2 MSL suffix: frag = vec4(c.rgb*c.a, c.a)).
  Düz-döndürülen fragment'lar: UiPieces.SdfFragment, GuiRenderer _sdfFragment/_rasterFragment
  (el yapımı rgb*outA çarpımları silindi). Material default: SrcBlend=One, Dst=OneMinusSrcAlpha
  (eski SrcAlpha yoluyla matematiksel birebir: SrcAlpha·rgb ≡ One·(rgb·a) — görsel regresyon yok).
  Font.Material/GuiRenderer font matları/UiPieces.SharedMaterial/texBlendMaterial explicit
  faktörleri kaldırıldı (default=kanon). Blit One/Zero yolları dahili, dokunulmadı.
- Gerekçe: motor Unity canvas/sprite ikiliğini yaşıyordu (sprite düz+SrcAlpha, SDF premul+One,
  dokulu kutu düz — aynı LayoutBox'ta iki sözleşme). Premul dahili format = RT kompozisyonu
  matematiksel doğru (ileriki blur/RT UI efektleri için şart).
- BLENDMODE: RenderEnums.BlendMode {Normal, Additive, Multiply=DstColor/OneMinusSrcAlpha,
  Screen=One/OneMinusSrcColor} + Renderer.BlendMode public alanı (serialize+Inspector combo
  bedava; sprite/kutu/metin AYNI alan). Material.ForBlend(mode): Normal=this; diğerleri lazy
  _blendVariants[3] klonu — MainTexture/Shader HER seçimde tabandan eşitlenir (paylaşılan
  materyal mutate deseniyle uyumlu). Uygulama siteleri: SpriteRenderer.Encode,
  LayoutBox.PiecesAtlas + EncodeTextured (StyleMat), LayoutBox.Text EncodeText, TextSprite.Encode.
  Farklı blend = farklı pipeline = ayrı draw (doğal); aynı blend merge korunur.
- Premul yan hediyesi (yapılmadı, kapı açık): premul faktörler sabitken shader çıkışı a=0 yazmak
  additive'e denk — ileride per-instance blend pipeline-switch'siz mümkün.
- DİLİM 2 PLANI (mutabık): .fx importer (metin artifact) + composer (fs_main→fs_core rename +
  fx zinciri + üretilmiş fs_main) + varyant cache + Renderer.effects alanı + bozuk efekt →
  log + EFEKTSİZ CORE'a düşüş (default'a değil; shim'e de_sokol_query_shader_state gerekir) +
  Grayscale/Vignette örnekleri. Watcher: .fx değişince gövdeyi içeren varyantlar cache'ten düşer.

## LAYER + ÇOKLU KAMERA + POINTER PIPELINE (2026-09-14, build + 18+14+19+14 test PASS, kullanıcı görsel bekliyor)
- TASARIM MUTABAKATI (uzun tartışma, kullanıcı onayı): motor 2D'ye FORCE ETMEZ — 2D özellikler 3D
  dünyada yaşayan konforlar. 8 layer, layer GO'da, çocuklara miras YOK. Kamera pratikte 2-3
  (World persp + UI pixelPerfect). 3D yol haritası (yapılmadı): depth pipeline → MeshRenderer →
  glTF importer → unlit shader → editör 3D viewport → skinning.
- LAYER: GameObject.layer (int 0-7). GoDoc.Layer + yaml `layer:` (0 ise yazılmaz) + Capture/Spawn/
  Parse + prefab ExpandInstance + Instantiate klonu kopyalar. Inspector: name altında ComboBox
  (_layerNames: Default..UI), EditorScene.SetLayer + LayerOp (undo, ActiveOp deseni).
- ÇOKLU KAMERA: CameraComponent.cullingMask (int, -1=hepsi) + clearFlags (CameraClearFlags
  SolidColor|DepthOnly|DontClear; ApplyTo Clear* bayraklarına basar). Scene.Render(queue, layerMask=-1).
  Scene.DriveCameras(pool List<Camera>, viewW, viewH) → aktif CameraComponent'ler depth artan sırayla
  havuz kameralarına ApplyTo + Render(mask); kamera yoksa pool[0] piksel-ortho fallback; kullanılmayan
  havuz kuyrukları Begin() ile boşaltılır (bayat draw canlanmasın). Scene.CameraForLayer(layer):
  min-depth maske eşleşen (LayoutBox FitRect artık bunu kullanır — UI kutusu UI kamerasına oturur).
  EditorApp: sceneCam SİLİNDİ → gameCams havuzu; hepsi GameOutput RT'ye, frame debugger aralığı hepsi.
- POINTER PIPELINE (engine/managed/Pointer.cs): Ray{Origin,Dir,At} + RayHit{Renderer,Distance,Point,
  +REZERVE Normal/TriIndex/Uv (MeshRenderer günü dolar, API kırılmaz)}. CameraComponent.ScreenPointToRay
  (ortho: eksene paralel, persp: kameradan geçen — tüketici projeksiyon bilmez).
  Renderer.HitTest(in Ray, out dist, out point) virtual: default lokal-bbox z=0 düzlem kesişimi
  (GetLocalSelectionBounds'tan) → HER renderer sıfır kodla tıklanabilir. Renderer.BlocksRaycast bool.
- JEST MAKİNESİ (PointerInput, Scene.Pointer lazy): Down/Move/Up (oyun çıktısı mantıksal px).
  Handler seti: OnPointerDown / OnDragStart (eşik 6px ilk aşımda 1 kez) / OnDrag / OnPointerUp
  (HEP gelir, e.dragged ayırt eder — OnDragEnd BİLEREK YOK) / OnPointerClick (eşik aşılmadıysa).
  LifecycleFlags: PointerDown=512..Drag=8192 + AnyPointer; ComputeFlags OverridesPtr(PointerEvent
  paramlı lookup). BUBBLE: hit GO'dan parent zincirine, e.Use() keser, e.target asıl hit (hangi taş).
  Aday filtresi = handler'lı GO zinciri + BlocksRaycast (tarama YALNIZ down'da). Kameralar ters depth
  (üstteki kamera bloklar); kamera içi: min mesafe, eşitse büyük SortingOrder/geç slot.
  PointerEvent TEK instance (Gui.Event modeli): screenPos/screenTotal/worldTotal(TEMBEL: drag düzlemi
  = down hit noktasından geçen kameraya dik z-düzlemi; ray-düzlem kesişimi) + TotalInParentOf(t)
  (parent world inverse → localPosition'a eklenebilir delta; kümülatif desen: _start + total).
  Jest bitince ClearRefs (GO köklenmez — ALC reload güvenli). 'e' SAKLANMAZ kuralı.
- EDITOR BESLEME: GameViewPanel artık Play'de mouse'u letterbox ters-map ile PlayScene.Pointer'a
  iletir (HotControl capture ile panel dışı up garantili). Edit modunda İLETMEZ (bilinçli).
- Vec3'e operatörler (+,-,*s, Length, Distance, Normalize); Mat4.TransformPoint/TransformVector.
- Testler: editor/PointerTests.cs (14 assert: click DUC, drag DSGU+worldTotal, bubble e.target=çocuk,
  cullingMask maskeleme, SortingOrder üstte, perspektif ray pick+drag totalleri). EditorApp Run'a eklendi.
- KALAN: standalone player'da Pointer beslemesi (player yok henüz); hover bilinçli yok; pinch ileride;
  prefab child layer override'ı diff'e girmiyor (bilinen sınır).

## KAMERA UNITY-UYUMLU ALT KÜME: PROJEKSİYON ENUM + PERSPEKTİF (2026-09-14, build + 18+14+19 test PASS, kullanıcı görsel bekliyor)
- CameraComponent.pixelPerfect bool → `CameraProjection projection` enum {PixelPerfect(default,
  eski davranış birebir), Orthographic(Unity: orthoSize=YARI yükseklik, pos=merkez — ESKİ tam-yükseklik
  semantiği DEĞİŞTİ), Perspective(fov dikey derece, pos=kamera konumu)}. [ShowIf] alanlar:
  orthoSize(300)/fov(60)/nearClip(1)/farClip(20000 — dünya birimi piksel, Unity'den geniş).
- Mat4: Perspective(fovyRad,aspect,near,far) + ViewYDown(px,py,pz) (translate(-pos)+y/z eksen çevirisi;
  y-aşağı dünya → GL y-yukarı eye). Mat4 unsafe fixed buffer — dışarıdan m[] yazılamaz, matris
  kurulumları hep Mat4 static'lerine.
- Camera: _hasOrtho→_hasRect+_persp; SetPerspective(fovDeg,aspect,near,far,px,py,pz) no-op dedup'lı.
  Rect (_oL..) = perspektifte z=0 düzlemindeki frustum KESİTİ → GetWorldRect/ScreenToWorld değişmeden
  perspektifte de çalışır (tüketici projeksiyon tipini bilmez — layout/picking etkilenmedi, LayoutBox
  FitRect sıfır dokunuş). ScreenToWorld perspektifte yalnız z=0 düzleminde birebir doğru.
- ApplyTo: Perspective → cam.SetPerspective; diğerleri GetWorldRect+SetOrtho. Kamera görmek için
  pz < 0 şart (Unity z=-10 gibi; piksel dünyada tipik -500..-1500; d=orthoSize eşdeğeri: h=2·|z|·tan(fov/2)).
- Login.scene: pixelPerfect kaldı → `projection: PixelPerfect` yazıldı. Eski sahnelerdeki pixelPerfect
  alanı serializer'ca sessiz düşer; default enum=PixelPerfect olduğundan eski default sahneler kırılmaz.
- Editor SceneView/blur/GUI kameraları SetPixelOrtho ile aynen (etkilenmedi).
- KALAN: perspektifte picking henüz yok (zaten game-view picking yok); z≠0 düzlem kesiti gerekirse
  GetWorldRect'e düzlem mesafesi parametresi eklenir.

## RENDER EMIT CACHE TARTIŞMASI — ERTELENDİ (2026-09-14, kullanıcı kararı)
- Karar: gerçek mobil cihazda release build alınıp SORUN YAŞANIRSA dönülecek; şimdilik kod değişikliği YOK.
- Kararlaştırılan plan (dönülürse): önce HUD'a encode/flush ms ayrımı ekle → ÖLÇ.
  Dilim 1 = frame-level skip: Scene.RenderVersion (transform/stil/tween/kamera RectVersion/resize
  değişmediyse encode+sort+upload atla, önceki command stream re-submit — Frame Debugger
  capture/replay altyapısı hazır). Dilim 2 = per-renderer emit cache (Starling render cache modeli;
  önce text, sonra kutu, klip zinciri en son "cache'lenemez → taze hesap" fallback'li).
  Dilim 3 (kalıcı instance buffer + dirty range) ölçüm göstermeden YAPILMAZ.
- Bilinen tuzaklar (envanter çıkarıldı): Tween FieldFloat + Inspector ApplyField alanlara DOĞRUDAN
  yazar (property dirty bypass) — boğaz FieldSchema accessor'ları (tek yerden sarmalanır);
  Sprite.BindRegion/atlas repack yerinde mutate + DefaultFont değişimi + async texture boyutu →
  asset version alanları gerekir; ComputeClip şu an propagasyonsuz-doğru, cache'i en riskli parça.
  Sigorta: DE_EDITOR paranoid mod (N frame'de bir taze emit ile kıyas assert'ü).
- Ayrıca A maddesi (bedava, dirty'siz): UiPieces CU/CV/EU/EV atlas UV sabitlerini UiAtlas'a bir kez
  pişir — bu da yapılmadı, dönülünce ilk aday.

## GUI COMBOBOX (2026-09-14, build + 18+14+19 test PASS, kullanıcı görsel bekliyor)
- engine/managed/Gui/GuiCombo.cs: Gui.ComboBox(rect, selectedIndex, string[] items) →
  yeni indeks döner. MODAL model: popup açıkken HotControl tutulur (tüm mouse combo'ya
  akar — GetTypeForControl filtresi; Overrides cache-freeze kuralıyla da uyumlu). Dış
  tık = kapat+yut. Aç jesti down+up; seçim MouseDown'da. Wheel scroll (ItemH*2,
  scrollview işaret uyumu), açılışta seçili öğe ortalanır. ekrana sığmazsa yukarı
  açılır/ yatay clamp (ComboPopupRect). Çizim: GuiClip.PushScreen + Push(globalPopup),
  layer 64-67 (widget'lar 0-4) → clip dışına taşar, her şeyin üstünde. Ok '▾' (raster
  yol codepoint on-demand). Güvenlik: açıkken hot başkasına geçtiyse popup kapanmış sayılır.
- GuiClip: PushScreen() (ekran köküne dön, overlay çizimi) + ScreenRect property.
- Tıkla-döngü editörler combo'ya çevrildi: InspectorPanel.Fields 2 enum sitesi
  (asset + doc; Enum.GetNames per-pass — Type-keyed statik cache BİLEREK yok, ALC
  reload kökler), ClipPanel ease + channel (_easeNames/_channelNames; EaseName/
  ChannelName helper'ları silindi), EditorApp toolbar HR modu (_hrNames).
- TimelinePanel (legacy demo) BİLEREK dokunulmadı.

## GRADIENT DIRECTION (2026-09-14, build + 18+14+19 test PASS, kullanıcı görsel bekliyor)
- Gradient'e direction (GradientDirection Vertical|Horizontal, [ShowIf type=Linear]) +
  IsHorizontal helper. Ctor (start,end,dir=Vertical) — eski (top,bottom) çağrılar uyumlu.
- LayoutBox.EmitQuad imzası: cTop/cBottom → c0/c1 + bool gradH (eksen başı/sonu renkleri).
  Klip lerp'i eksene göre (x-band yatayda, y-band dikeyde); DrawMesh tint:
  yatay (c0,c1,c1,c0), dikey (c1,c1,c0,c0) — köşe 0=BL 1=BR 2=TR 3=TL.
- FillAt/BorderAt(c): eksene göre x veya y normalize (pivot ofsetli _rw/_rh).
  EmitLayer lokal gh bayrağı; Cell/HEdge/VEdge/Solid At(gh?x:y) örnekler.
  EncodeTextured (stretch+9slice) da gh'li. Gölge/shadow hep düz renk (false).
- LayoutBox.Text: TextGradAt(g,c) eksenli — yatay normalize _tbAvailW, dikey _tbBlockH.
- TextSprite: GradAt(g,c,half) jenerik (halfW/halfH); Emit horiz bayraklı.

## KUTU QUAD DİKİŞ FİX (2026-09-14, build yeşil, kullanıcı görsel bekliyor)
- BUG: LayoutBox kutu/gölge parçaları (köşe hücreleri) parça koordinatı 0'dan örnekler
  (CU(0)/CV(0)) = parça bölgesinin TAM texel sınırı → bilinear komşu pad'in SIFIRLARINI
  %50 karıştırır → köşe hücrelerinin İÇ kenarlarında yarı-alfa çizgi ("köşeler ortaya
  değmiyor" dikişleri). DFNT atlasında bölge y=1800/ShadowX=136 olduğundan görünür;
  fallback atlasında clamp-to-edge kısmen maskeler.
- FIX: UiPieces.BakeDisc APRON=4 px — disk formülünün doğal devamı pad'e de pişer
  (x+0.5 simetrisi: texel -1 = texel 0 → sınırda bilinear birebir doğru). Corner apron
  131'de biter, Shadow 132'de başlar (çakışmaz); üst apron 8px glyph pad'ine düşer.
  FontImporter Version=4 (otomatik reimport). Şeritlerin EU(0.35) örneklemesi de düzeldi.

## ORTHO NEAR/FAR FIX (2026-09-13): SetOrtho default near/far ±16384 oldu (eski -1..1).
Dunya birimi=piksel; X/Y rotasyonlu quad z'de yuzlerce birim uzuyor, dar aralik near/far
duzlemlerinde KIRPIYORDU ("rotation donmek yerine image kirpiyor" bug'i — LayoutBox+SpriteRenderer).
Depth test cogunlukla kapali (Material bazli) → genis aralik zararsiz.

## KAMERA SAHNE VATANDAŞI (2026-09-13, build + 18+14+19 test PASS, kullanıcı görsel bekliyor)
- KARAR (tartışma): Camera render nesnesi (pass sahibi) AYNEN kalır; sahneye giren şey onu
  TARIF eden ince CameraComponent. LayoutBox/picking projeksiyon tipine ASLA koşullanmaz —
  tek sözleşme "kameranın dünya görüş rect'i". Performans ilkesi: per-frame maliyet = birkaç
  int/float kıyası (dirty/no-op dedup), tarama/lookup yok.
- Camera.cs: SetOrtho(l,r,top,bottom,near,far) — AYNI parametrelerle no-op (statik kamera
  maliyeti bu kıyas); SetPixelOrtho ona delege. RectVersion (int, ++). GetWorldRect(out x,y,w,h)
  (perspektif gelince frustum kesiti verecek — çağıran bilmez). ScreenToWorld(sx,sy,viewW,viewH).
  ViewProj alanı/Encode yolu değişmedi; dışarıdan doğrudan ViewProj yazan rect alamaz (custom).
- engine/managed/CameraComponent.cs: depth (en düşük = main), background,
  pixelPerfect(default true; pos = görüşün SOL-ÜST köşesi, 1 birim=1px; pos 0,0 = eski davranış
  birebir) | pixelPerfect=false: orthoSize=dünya YÜKSEKLİĞİ [ShowIf], pos=MERKEZ, genişlik
  aspect'ten. GetWorldRect(viewW,viewH,out...) TEK matematik noktası; ApplyTo(cam,viewW,viewH)
  = SetOrtho + BackgroundColor. OnEnable/OnDisable → Scene.Register/UnregisterCamera.
- Scene: _cameras List + MainCamera (min depth lineer, kamera sayısı az). Renderer 'is' deseni
  değil, component kendini kaydeder.
- EditorApp: sceneCam projeksiyonu artık gameScene.MainCamera.ApplyTo'dan (WYSIWYG — edit
  modunda da sahne kamerası); kamera yoksa fallback SetPixelOrtho (eski davranış). Render'dan
  HEMEN ÖNCE basılır (fitScreen encode'da okur). SceneViewPanel.ApplyEditorCam → SetOrtho.
- LayoutBox fitScreen → FitRect(): s.MainCamera.GetWorldRect(ScreenW/H) ?? (0,0,Screen).
  _fitX/_fitY son rect orijini; ResolveIfDirty rect (x,y,w,h) değişince dirty (kamera pan/zoom
  da yakalanır, eski sadece boyut kıyaslıyordu). ResolveTop: SetPos(fx + pivot*fw, ...).
- KALAN (kararlı plan): picking dilimi (kameralar ters depth, Renderer.HitTest virtual,
  tık anı maliyeti), perspektif (planeDistance kök kutuya), standalone player'da Camera'yı
  component'ten kurma, Camera.Order hâlâ editörün (depth yalnız main seçimi).

## LAYOUTBOX OVERFLOW (2026-09-13, testler 18+14+19 PASS, kullanıcı görsel bekliyor)
- OverflowMode {Grow=0(default, eski davranış: preferred=max(authored,içerik)),
  Visible(authored boyut, taşan çizilir), Hidden(authored boyut, taşan KIRPILIR)}.
  Alanlar overflowX/overflowY (+Overflow ikisini set eder). Scroll = 2. faz (Hidden+içerik ofseti).
- Measure: PreferredW/H başında overflow != Grow → düz authored width/height döner
  (metin+çocuk içerik ekseni büyütmez — "text yazınca büyüyor" şikâyeti çözüldü).
- KIRPMA GEOMETRİK (scissor DEĞİL): ComputeClip her Encode başında ata ParentBox
  zincirindeki Hidden eksen rect'lerini dünya AABB olarak kesiştirir, kendi lokal
  uzayına çevirir (_hasClip,_clX0..Y1). EmitQuad instance metodu oldu: quad klip
  penceresine kırpılır, UV lineer remap (SDF parça örnekleme orantılı doğru) +
  dikey gradient cTop/cBottom LerpC ile banda lerp. Batch kırılmaz, her kamerada doğru.
  UV/y eşlemesi: v1↔qy0(üst), v0↔qy1(alt) — Mesh.Quad y-down düzeni.
- Kendi Hidden ekseni METNİ kırpar: EncodeText başında EnsureClip + kendi rect
  kesişimi (Encode'un son emisyonu — mutasyon güvenli). Kutunun kendi fill/border'ı
  kendi Hidden'ından etkilenmez (CSS: kendi arkaplanı kırpılmaz, içerik kırpılır).
- SINIRLAR (bilinçli): rotation'lı kutu/ata → klip o halkada sessiz devre dışı
  (eksen-hizalı varsayım); SpriteRenderer/TextSprite torunları KIRPILMAZ (yalnız
  LayoutBox quad'ları — faz 2); klip border-box'ta (padding-box değil).
- LayoutTests.OverflowSmoke: grow/visible boyut + kısmi kırpma + tamamen dışarıda
  çocuk 0 quad (draw sayısıyla).

## SPRITE PRIMİTİFİ: SpriteTable SİLİNDİ (2026-09-13, testler 18+14+14 PASS, kullanıcı sprite sürükleyip ÇİZDİĞİNİ doğruladı)
- KARAR (uzun tartışma): motor genelinde TEK bölge primitifi `Sprite : IAsset` (engine/managed/Sprite.cs;
  AtlasGroup da oraya taşındı, SpriteTable.cs git rm). Eski motorun Texture2D modeli: renderer Texture
  değil Sprite referanslar; atlas üyeliği DETAY — builder aynı instance'ı BindRegion ile YERİNDE
  günceller (canlı referans kırılmaz, frame'de string lookup YOK; eski SpriteTable.Resolve her frame
  OrdinalIgnoreCase dict lookup yapıyordu).
- Sprite: Name/Page(Texture)/X,Y,W,H (W==0 = tüm-sayfa modu; async yüklemede boyut Page'den) /
  OffX,OffY/OrigW,OrigH/Rotated(rezerve) + IsRegion/LogicalWidth/Height + FromTexture/BindRegion/BindFull.
- AssetDatabase: _sprites cache + LoadSprite(key) (=FromTexture(LoadTexture)); _importers png/jpg/jpeg
  → typeof(Sprite) (Texture DEĞİL artık); IsAssetType Texture'ı özel-case tutar (RT alanları Kind.Asset
  kalsın); LoadAsset Sprite dalı. AssetPackBuilder ImportTypeOf==Sprite kontrolü.
- SpriteRenderer.Texture → Sprite (alan adı!); LayoutBox texture → sprite ([SerializeField], prop Sprite).
  Sahne/prefab yaml'ları "Texture:"→"Sprite:", "texture:"→"sprite:" GÜNCELLENDİ (Sandbox 5 dosya;
  guid'ler aynı — Sprite aynı png key'inden yüklenir).
- AtlasBuilder: Built.Members artık List<Sprite>; Pack → assets.LoadSprite(m).BindRegion(atlas,...);
  Teardown → BindFull(kaynak texture). SpriteTable.Register/Clear kalktı.
- KARARLAŞTIRILAN GELECEK (henüz yapılmadı): Font.Glyph → {Sprite, Advance, XOff, YOff} (glyph başına
  Sprite; eski motorda production'da kanıtlı) → atlas MERGE jenerik olur (packer Sprite listesi alır,
  yerinde rebind). Merge engelleri: R8 SDF vs RGBA sayfa formatı (çözüm adayı rgb=beyaz+a=SDF) + shader
  birleşimi. Release atlas loader (ATLS artifact: sayfa + guid→rect manifesti) de yapılmadı.
- Dependency kuralı: sahne HİÇBİR ZAMAN atlas sayfasını referanslamaz, hep kaynak guid; sayfa türetilmiş
  artifact. Runtime lazy-load: LoadSprite → Page async (CreatePending placeholder mekanizması aynen).
- EDİTÖR SIZMASI TEMİZLENDİ (aynı gün): AtlasGroup EDITÖR'e taşındı (AtlasBuilder.cs, ns DigitoyEditor);
  Sprite.BindRegion/BindFull internal (editör InternalsVisibleTo ile erişir; oyun kodu sprite mutasyonuna
  kapalı; ileriki release atlas loader motor-içi olduğundan internal yeter). EditorApp AssetTypes
  taraması artık editör assembly'sini de kapsar (Create menüsünde Atlas Group görünsün diye).

## GRADIENT STRUCT (2026-09-13, build + açılış testleri 18+14+14 PASS, kullanıcı görsel bekliyor)
## [ShowIf] KOŞULLU INSPECTOR ALANI (aynı gün, testler yeşil, kullanıcı görsel bekliyor)
- GENERIC mekanizma (Odin ShowIf modeli): [ShowIf("kardeşAlan", değer=null)] field attribute'u
  (engine Attributes.cs). değer verilirse KANONİK SKALER eşitliği (enum adı/bool/sayı —
  SerializedType.Format uzayı); null ise truthy (bool true / asset-goref-compref dolu /
  sayı != 0 / string dolu).
- FieldSchema.ShowIf (çözülmüş kardeş şeması) + ShowIfScalar; Build sonunda ResolveShowIf
  (ileri referans serbest, çözümsüz kural sessiz düşer = hep görünür).
- Değerlendirme: SerializedType.ShowIfVisible(f, owner) canlı mod; ShowIfMatch(f, scalar)
  doc modu. Editör InspectorPanel: DocVisible(f, DocNode map | CompDoc) — kardeş prop
  eksikse CreateDefaultFieldNode ile default üzerinden.
- KRİTİK: ÖLÇÜM ve ÇİZİM aynı koşulla atlamalı (yükseklik tutarlılığı). 4 site çifti:
  MeasureAssetField+DrawAssetSchemaField (canlı, root+nested tek boğaz),
  MeasureDocValue Object döngüsü + DrawDocObject, root comp measure+draw (InspectorPanel.cs).
- Kullanım: Gradient.color2 [ShowIf(type, Linear)]; LayoutBox.Text tüm text* alanları
  [ShowIf(nameof(font))] (font atanmadan metin alanları görünmez).

- engine/managed/Gradient.cs: [Serializable] struct Gradient {GradientType type(Solid|Linear),
  Color color(üst), color2(alt)} + Visible (a>0 kontrolü) + At(t) (Solid=color, Linear=clamp'li lerp)
  + implicit Color→Gradient. Standart serializer'a NESTED OBJECT olarak girer (Kind.Object,
  IsInlineObject kuralı) — serializer/Inspector'a SIFIR dokunuş, Inspector foldout'la düzenler.
- 6 site dönüştü (color+color2+bool üçlüleri SİLİNDİ):
  * LayoutBox: fillColor/fillColor2/gradient → fill (Fill prop); borderColor/2/borderGradient →
    borderFill (BorderFill prop). Grad static helper silindi (Gradient.At'e taşındı).
  * LayoutBox.Text: textColor(2)/textGradient → textFill (TextFill); textOutlineColor(2)/
    textOutlineGradient → textOutline (TextOutline). TextGradAt(in Gradient, y) oldu.
  * TextSprite: Color/Color2/Gradient → Fill; OutlineColor(2)/OutlineGradient → Outline.
    GradAt(in Gradient, y, halfH) oldu.
- KIRILMA (kullanıcı onayladı): eski .scene dosyalarındaki fillColor/borderColor/... alanları
  sessizce düşer (Login.scene, New Scene.scene) — kullanıcı editörden yeniden ayarlayacak.

## COLOR PICKER (Unity modeli) — KODLANDI (2026-09-13, build yeşil, kullanıcı görsel bekliyor)
- GuiRenderer.DrawRectGradient(rect, TL,TR,BR,BL): 4-köşe tint quad (instance tint0..3;
  Mesh.Quad uv: ekran-üst=uv.y1 → tint0=BL,1=BR,2=TR,3=TL). Kısmi klipte köşe renkleri
  Bilerp ile orantılı lerp'lenir (gradyan sabit kalır).
- editor/ColorPickerWindow.cs: STATIK popup — EditorWindow/dock DEĞİL. Ghost pencere deseni:
  NativeWindow.Open (DECORATED=0, FLOATING=1, RESIZABLE=0; hint'ler elle geri alınır),
  imleç ekran konumu+12'de açılır (GetWindowPos+GetCursorPos main), 240x246 mantıksal.
  Focus kaybı = Close (GetWindowAttrib FOCUSED; açılışta 15 frame tolerans). Tekrar Open =
  reposition+FocusWindow. EditorApp loop kancaları: UpdateWindow (GuiDock.UpdateWindows
  sonrası), Encode (EncodeWindows sonrası), Present (PresentWindows sonrası).
  Statik baglam _owner(key)+HSV(_h/_s/_v)+_a
  (HSV tutulur → s/v=0'da hue kaybolmaz, Unity davranışı). Open(key,initial);
  TryGet(key,out c) alan sahibi her frame çeker → commit/undo mevcut SetProp/asset-save yolundan.
  UI: SV karesi = DİNAMİK DOKU (bilinear quad köşegen dikiş yapıyordu — GPU üçgen
  interpolasyonu; Unity gibi hue değişince CPU'da 128² RGBA pişirilir,
  Texture.CreateDynamicRgba/UpdateRgba EKLENDİ, EnsureSvTexture GUI pass'inden önce
  frame'de bir kez; GL satır 0 = ekran ALTI → value=y/(h-1)) +
  dikey hue şeridi (6 segment, yatayda sabit=düzlemsel → gradyan quad sorunsuz) +
  dikey alfa şeridi + RGBA DragInt + hex TextField
  (odak yokken bufferdan renk yazılır; 6/8 hane parse, '#' opsiyonel) + önizleme.
- InspectorPanel.Fields.DrawColor(rect,value,KEY): 4-DragInt KALDIRILDI → Unity swatch
  (üst rgb opak, altta siyah üstüne beyaz alfa-oran şeridi); tık → Open. Key = path
  (doc: "c:goId:compIdx:alan...", asset: "a:..." — benzersiz). İki çağrı sitesi de path geçer.

## TAB NAVIGASYONU (Gui) — KODLANDI (2026-09-13, build yeşil, kullanıcı görsel bekliyor)
- GuiUtility: MoveFocus(fromId, backward) + ConsumeTabFocus(id). FocusType.Keyboard
  kontroller GetControlID'de pass sirasiyla izlenir (first/last/before/after-from).
  Istek pass ortasinda gelir → _tabFresh ile BIR TAM pass izlenir, cozum sonraki
  BeginPass'te KeyboardControl'e yazilir (basa/sona sarma dahil). Shift+Tab = geri.
- TextField: KeyDown Tab → MoveFocus + Use; tab ile odak alinca select-all.
- DragCore: tab odagi gelince dogrudan yazma modu (_numEditId, tumu secili);
  NumEditEvents Tab commit'ten once MoveFocus cagirir → alanlar arasi Tab akisi.

## NUMERIC FIELD UX (Unity paritesi) — KODLANDI (2026-09-13, build yeşil, kullanıcı görsel bekliyor)
- Gui.DragCore edit oturumu artik TAM TextField: GuiTextField cekirdegi (TextEditState,
  HandleKey, DrawTextField, IndexFromX, Insert/DeleteSelection) aynen paylasilir →
  caret + secim + Ctrl+A/C/X/V + ok/Home/End calisir. _numFresh hack SILINDI; tik-ile-gir
  = select-all (Anchor=0, Caret=len). Enter/Tab commit, Esc iptal, disari tik commit.
  Edit event'leri NumEditEvents(rect,id,...) ayri fonksiyonda. _numBuf readonly'den
  cikti (HandleKey ref char[] istiyor). Sayi metni artik SOL hizali (centerX kalkti,
  TextField ile tutarli). TextInput filtresi: 0-9 - , (float'ta . e E +).
- Gui.DragZone(rect, value, speed, min, max): GORUNMEZ drag tutamaci (cizim yok,
  ResizeH cursor + yatay drag = deger). Unity label-drag modeli icin.
- Editor: InspectorPanel.Vec3Drags → AxisDrag (hucre basina 12px X/Y/Z etiketi =
  DragZone tutamaci, kalan alan DragFloat). Float/Int alanlarinda field ETIKETI de
  DragZone (DrawAssetValue + DrawDocValue iki site). Alan drag'i da duruyor (3px esik).

## LAYOUTBOX.TEXT (kutu vatandasi metin) — KODLANDI (2026-09-13, testler 18+14+14 PASS, kullanıcı görsel bekliyor)
- LayoutBox partial oldu; metin isi engine/managed/LayoutBox.Text.cs'de (kullanıcı istegi: uzun kod ayri dosya).
- Alanlar: font/text/textSize/textAlign(TextAlign)/textAlignV(LayoutAlign)/textWrap(default true)/
  textEllipsis(default true) + TextSprite stil paritesi: textColor(2)+textGradient,
  textOutlineWidth/Color(2)/Gradient, textShadowColor/Offset/Blur. Yerlesimi etkileyenler MarkDirty,
  stil alanlari duz assignment (her frame instance'tan okunur).
- Olcum: TextPreferredW (wrap+authored width varsa 0 → metin kutuyu genisletmez; yoksa en genis satir),
  TextPreferredH (satir sayisi*lineH; wrap genisligi authored width'ten — grow/stretch'te yaklasiklik,
  bilinccli). PreferredW/H: aw=max(width, TextPreferredW()) tum dallarda (icerik sozlesmesi).
- NextLine: greedy word wrap (SDF-px uzayi; bosluktan kirilir, bosluk yutulur; tek kelime sigmazsa
  karakter kirilimi, satirda min 1 glyph). Olcum + kurulum ayni fonksiyonu kullanir.
- Ellipsis: blok availH'ye sigmayan satirlar kirpilir, son gorunen satir + availW asan satirlar
  FitCount(clipW - "..." genisligi) ile kesilip "..." eklenir.
- Cache: TextQuad[] icerik sol-ust orijinli; font/text/size/align/wrap/ellipsis/availW/availH
  degisince rebuild. cap = len + 3*lines + 1 ("..." payi).
- Cizim: Encode sonunda EncodeText (kutu quad'larindan SONRA = ayni layer ustte; textured dalda da).
  Encode gate: !HasStyle && !HasText → cik. Golge→kontur→dolgu sirasi, USER kanallari TextSprite
  ile birebir (TextSdfSpread=21). Gradient blok bandina gore (TextGradAt). textAlignV blok ofseti.
- Font.Material ayni UiShader → kutu parcalari + metin ayni font atlasindaysa tek draw merge.
- BATCH FIX 2: UiPieces.DefaultFont (ilk yuklenen font otomatik; reimport'ta ayni ad guncellenir,
  Font.FromArtifact sonunda set). PiecesAtlas: kendi fontu ?? DefaultFont ?? Fallback → font'suz
  kutular da ayni font atlasindan cizer, sahnedeki TUM kutu+metin tek instanced draw.
  (Onceden hep Fallback'ti → text batch kiriyordu; sonra font'suz kutular kiriyordu.)

## TEXTSPRITE → INSTANCED GLYPH + OUTLINE/SHADOW/GRADIENT (2026-09-13, testler 18+14+14 PASS, kullanıcı görsel bekliyor)
- Mesh cache SİLİNDİ (batch kırıyordu): glyph'ler LayoutBox EmitQuad deseniyle Mesh.Quad
  instanced draw (GlyphQuad[] yerleşim cache'i — yalnız font/text/size/align değişince rebuild;
  stil alanları her frame instance'tan okunur, rebuild YOK). Aynı atlas+UiShader → kutu+metin merge.
- Yeni alanlar: Color2+Gradient (dikey dolgu), OutlineWidth/OutlineColor/OutlineColor2/
  OutlineGradient (kontur da lineer gradient destekler), ShadowColor/ShadowOffset/ShadowBlur.
- SDF matematiği: SdfSpread=21 (shim bake sabiti). Kontur = USER.y kenar merkezi
  0.5 - width/scale/21 (clamp 0.05). Gölge = USER.x smoothing blur*0.5/scale/21, silueti
  kontur kenarından (cOut). Painter: gölge → kontur → dolgu (aynı layer submit sırası).
- Gradient tint'i quad üst/alt kenarında GradAt ile örneklenir (blok y'sinden t) —
  glyph içi interpolasyon vertex stage bilinear mix'ten (üst kenar uv.y=1 Mesh.Quad y-down).

## GÖLGE (SDF, RT'siz) — TAMAMLANDI (2026-09-12, testler yeşil, kullanıcı görsel bekliyor)
- LayoutBox yeni alanlar: shadowColor (a>0=açık), shadowOffset (Vec2, default 0,4),
  shadowBlur (falloff yarı genişliği, default 8), shadowGrow (spread). HasStyle'a dahil.
- EmitShadow: Encode'da İLK katman (aynı layer, submit sırası=altta). Partition
  EmitLayer'la aynı (4 köşe + 4 şerit + iç dolgu). Köşe: geniş parça (ShadowRadius
  64/Spread 128 — 80/160'tan DEĞİŞTİ, (r+b) haritalaması 128px'e sığsın; ring c2
  formülü oransal, etkilenmedi), rs=max(radius+grow, blur), USER=(b/(2rs), 0.5).
  Şeritler: keskin parça tam rampası (56..120) ±b'ye gerilir, USER.x=0.5 →
  smoothstep(0,1,ramp) gaussian-vari falloff; köşe/şerit profilleri birebir aynı
  (±b smoothstep) → dikişsiz. blur clamp: min(w,h)/2 (şerit örtüşmesi olmaz).
- FontImporter Version=3 (parça sabiti değişti → otomatik reimport).
- Test gölgeleri RPC ile Login'e atandı (undo'lanabilir): FacebookLogin/GuestLogin
  #00000080 blur10, Logo #0008246e blur18 offset 0 8. Kullanıcı bakacak; beğenirse kaydeder.

## INSTANCE GENİŞLEMESİ: 4 KÖŞE TINT + USER VEC4 (2026-09-12, build+testler yeşil, kullanıcı görsel/stres testi bekliyor)
- KARAR ZİNCİRİ (uzun tartışma): SDF ubershader → vazgeçildi → eski motorun baked-corner
  9-slice modeli (kullanıcı Canvas.cs örneği: 64px AA çeyrek disk + 7 quad + tek doku batch)
  → katmanlı border numarası (dış dolu quad + iç inset quad = her kalınlık kesin, ring
  dokusu gereksiz) → utility parçaları SDF tipinde DFNT atlasında SABİT bölgeye (tüm
  fontlarda aynı UV → kutu+metin tek draw; pişirme ENGINE'de, importer gömer, runtime
  fallback) → gölge SDF'e binemez (fwidth hep ~1px keskin tutar) → çözüm: instance USER
  kanalı (smoothing per-instance) → StyleBatch TAMAMEN GEREKSİZLEŞİYOR: her şey
  (kutu parçaları/gölge/glyph) Mesh.Quad instanced draw; RenderQueue merge zaten tek
  drawcall yapar; gradient = 4 köşe tint (eski DrawMultiColorQuad modeli).
- YAPILDI (dilim 1, tamamen managed — shim'e dokunulmadı, stride C#'tan geliyor):
  * Instance: model[16]+uvRect[4]+tint0..3(4xColor)+user[4] = 112B (eski 84). tint
    köşeleri UV uzayında: 0=(0,0) 1=(1,0) 2=(1,1) 3=(0,1). Vertex stage bilinear mix.
  * Shader.cs: _vertexTemplate {USER_IN/OUT/ASSIGN} yer tutucuları — fragment gövdesi
    "USER" içeriyorsa a_user attribute'lu varyant + _fragUserPrefix (in v_user + #define
    USER). İÇERMİYORSA a_user hiç girmez (GL derleyicisi budayınca sokol
    GL_VERTEX_ATTRIBUTE_NOT_FOUND WARN spam'i basıyordu — yaşanan ders). Shader.UsesUser.
  * PipelineCache: attr8..11 tint0..3 UBYTE4N @80/84/88/92; attr12 user FLOAT4 @96
    YALNIZ UsesUser shader'da. Toplam 13 attr (GL sınırı 16).
  * RenderQueue.DrawMesh: eski imza → 4 aynı tint + default user'a delege; yeni overload
    (tint0..3, in Vec4 user). Buffer reuse olduğundan user alanları her yazımda explicit.
  * Vec4.cs eklendi (minimal x,y,z,w).
- Maliyet bilinçli: +28B/instance herkese (55k stres ~4.6→6.2MB/frame upload) — tek layout
  sadeliği için kabul. Stres ölçümü (HUD ms + B/f) kullanıcıda.
- KALAN DİLİMLER (kararlaştırılmış plan): 2) DFNT v2 sabit utility bölgesi (SDF köşe
  parçası; Font.Bake pişirir, runtime fallback atlası aynı fonksiyonla) 3) LayoutBox.Encode
  → quad emitter (gölge=USER.x blur, border=katmanlı, gradient=tint0..3; StyleBatch +
  tessellation + LayoutSystem Begin/EndRender kancaları + Scene._frameStamp gate SİLİNİR)
  4) TextSprite → instanced glyph (mesh cache silinir) 5) Frame Debugger "layer başına
  1 draw" + görsel A/B. Kabul edilen kayıplar: per-edge farklı border RENGİ (köşede keskin
  geçiş), köşegen-düzlemsel-olmayan 4-renk kombinasyonunda diyagonal dikiş (2 üçgen),
  yarı saydam fill+border'da köşe blend sızması.

## LAYOUTBOX QUAD-EMITTER: TESSELLATION + STYLEBATCH SİLİNDİ (2026-09-12, testler 18+14+14 PASS, kullanıcı GÖRSEL A/B bekliyor — Login sahnesi)
- LayoutBox.Encode artık INSTANCED QUAD EMİSYONU: kutu = UiPieces SDF parçalarından
  4 köşe hücresi + 4 kenar falloff şeridi + iç dolgu quad'ları (≤15/katman; disjoint).
  RenderQueue'nun mevcut merge'ü (aynı pip+mesh+texview run) tek instanced draw yapar —
  ayrı batch sistemi YOK. LayoutStyleBatch + RebuildGeometry/BuildPath/BuildScale9/
  Ring/BandAt/FillTint/UvAt + _styleVerts/_meshDirty alanları SİLİNDİ; stil setter'ları
  düz assignment (cache yok, her frame stil alanlarından okunur).
- BORDER = SDF RING katmanı: shader USER.z = iç kenar bandı (alpha = smoothstep(dış)
  × 1-smoothstep(iç)) → dolgu katmanıyla örtüşme YOK (yarı saydam fill+border çift
  blend sorunu da çözüldü). Ring köşeleri geniş-bantlı parçadan (Shadow piece R=80
  spread=160: b≤r), kenar şeritleri keskin parça kesitinden (Q'=32/(m+b), c2=0.5+bQ'/64).
- UiPieces.SdfFragment = ortak UI shader: USER.x smoothing (0=fwidth), USER.y kenar
  merkezi, USER.z iç band. UiShader/SharedMaterial (fallback atlas); Font.Material
  AYNI shader → kutu+metin aynı pipeline (texture farklıysa draw ayrılır; tek draw
  LayoutBox.Text font atlası kullanınca gelecek).
- Şerit matematiği: dolgu pyIn=56 (değer tam 1, iç dolguyla dikişsiz), pyOut=
  min(88+m*32/b,127); köşe hücresi [x0-m..x0+r]², m=min(2,0.4r), pFar=(r+m)*pieceR/r.
  Min radius 0.75 (keskin köşe de SDF AA alır). Radius CSS pair-fit clamp.
- Painter sırası: layer = SortingOrder + _paintSeq (batch-öncesi davranışa dönüş;
  parent<çocuk ✓; kutu-vs-sprite interleave = submit sırası — SortingOrder aralık bırak).
  Scene._frameStamp layout tarafından artık KULLANILMIYOR (alan duruyor).
- Dokulu kutu: stretch 1 quad / scale9 3x3 (StyleMat paylaşılan default-shader mat).
  KABUL EDİLEN KAYIPLAR: texture+radius kırpma (doku ile SDF parça aynı sampler'ı
  paylaşamaz), per-edge farklı border RENGİ, eliptik iç köşe (inner r = r-max(inset)
  dairesel), scale9 4x4→3x3 (davranış aynı).
- RenderQueue.Count internal (testler draw sayısı doğrular; assert 15→14).
- SIRADA: gölge alanları (shadowColor/Offset/Blur — USER.x + Shadow piece, altyapı
  hazır), TextSprite→instanced glyph (LayoutBox.Text ile birlikte anlamlı), LayoutBox.Text.

## ASSET IMPORTER + PREBAKED SDF FONT + TEXTSPRITE (2026-09-12, RPC uçtan uca doğrulandı, kullanıcı GÖRSEL testi bekliyor)
- ScriptedImporter modeli (editor/ImportPipeline.cs): [AssetImporter(".ttf", Version=n)] attr +
  abstract AssetImporter.Import(ImportContext) + ImportContext.AddArtifact(name,bytes)/Fail(msg).
  Rebuild assembly taramalı (RebuildCatalog'dan; oyun assembly'si kendi importer'ını ekler).
  Uzantı çakışması = HATA + ikisi de devre dışı (sessiz öncelik yok). Kaynak Assets/'te,
  artifact Library/Artifacts/<guid>/<ad>; ".stamp" = SHA256(kaynak)+":v"+Version hash kapısı
  (eşleşirse import atlanır — doğrulandı: LoadFont ikinci import koşturmadı). Klasör sil-yaz.
  "main" artifact = kaynak anahtarıyla yüklenen birincil çıktı (konvansiyon).
- Engine tarafı: IAsset{Name} interface (GpuResource.Name field→property, GpuResource:IAsset).
  Kind.Texture → Kind.Asset GENELLEŞTİ (SerializedType/DragDrop/Inspector ~10 site):
  TryKind → AssetDatabase.IsAssetType(t) (= importer kayıt defterindeki IAsset tipleri;
  Prefab IAsset değil → davranış değişmedi). Parse → assets.LoadAsset(key,type) dispatch
  (Texture/Font). Inspector DrawAssetTexture → DrawAssetRef (IAsset.Name gösterir).
- AssetDatabase: ArtifactResolver delegesi (editörde ImportPipeline.ResolveMainArtifact;
  release'te null → pak zaten artifact taşır), LoadFont (senkron, cache'li; başarısız yükleme
  cache'lenmez), InvalidateImported(rel). _importers'a .ttf/.otf→Font eklendi.
- Font (engine/managed/Font.cs): DFNT artifact ('DFND magic 0x544E4644, v1) = metrikler +
  glyph 7float*224 + kern int16*224² + R8 atlas 2048². Font.Bake(ttf) EDITOR-time
  (Sokol.FontBake, GPU'suz) — FontImporter bunu çağırır; FromArtifact runtime kurar
  (Texture.FromAlpha, Persistent — evict tarifsiz). Font.Material lazy: SDF shader
  (GuiRenderer fragment'ının kopyası) SortMode.Transparent, premultiplied.
  Kullanıcı API'si olarak Font.Load YOK — serialized field referansı tek yol.
- TextSprite (engine): Renderer, Font/Text/Size/Color/Align(Left|Center|Right) alanları.
  Mesh cache'li (font/text/size/align değişince rebuild; Mesh.Create immutable + Destroy
  ertelemeli), blok transform.position'da ORTALANIR, y-down, V takası GUI SDF yoluyla aynı.
  GetLocalSelectionBounds ölçümden. Renk instance tint'ten (vertex beyaz).
- Watcher: switch default'una jenerik dal — ImporterFor(ext)!=null → EnsureImported(force)+
  InvalidateImported (canlı referanslar eski Font'ta kalır, sahne reload'da tazelenir). Doğrulandı.
- Pak: importer'lı kaynak → pak'a ARTIFACT girer (ttf girmez). Test: 14 asset, DFNT 4.3MB →
  pak 516KB (%8.4), verify bayt-eşit OK.
- RPC: asset.importers (liste), asset.reimport(path, force+artifact listesi+ms). Doğrulandı.
- Menü id hesabı (sendmenu): (Order, Path ordinal) sort → actions index; Build Asset Pack=7.
- KALAN borçlar: importer settings .meta'da (charset/sdf param; şu an shim sabitleri 224 glyph
  Latin-1 — Türkçe ş/ı/ğ '?' düşer, genişletme importer setting işi), UIText (LayoutBox kutu
  vatandaşı metin), import worker-thread'e alma (şu an ana thread ~90ms), Texture/DTEX+Atlas'ın
  importer modeline migrasyonu, artifact'ların Resources.Collect entegrasyonu.
- TEST ARTIĞI: Login.scene'de kaydedilmemiş "Baslik" GO (TextSprite "Merhaba Dunya", 657,120)
  — kullanıcı görsel doğrulama için bırakıldı, KAYDETME kararı kullanıcının.

## RPC HOST (AI komut kanali) — TAMAMLANDI (2026-09-12, uctan uca dogrulandi)
- Amac: AI-driven akis — editor calisirken TCP localhost:5157 (env DIGITOY_RPC_PORT)
  satir-bazli JSON RPC. Istek {"id","cmd","args"}, yanit {"id","ok","result"|"error"}.
- editor/RpcHost.cs: [RpcCommand(name,help)] attribute (static metod; ilk param RpcContext
  opsiyonel, kalanlar JSON args'tan ADA GORE baglanir: int/float/bool/string/DocNode,
  default degerli=opsiyonel). RpcRegistry.Rebuild(asms) — RebuildCatalog'da editor+game
  assembly taranir (oyun kendi komutunu ekleyebilir, ALC reload guvenli). Describe =
  self-schema (AI oturum basinda ceker; ileride MCP tool listesine ayni sema).
  RpcError = temiz mesajli beklenen hata. RpcJson: DocNode<->JSON (skaler yazimda dar
  tip tespiti long/bool/double; JSON number -> Scal(rawtext) kanonik format).
- Threading: accept+read thread'leri yalniz ConcurrentQueue'ya yazar; EditorApp loop'ta
  HotControl==0 && KeyboardControl==0 iken RpcHost.Pump(ctx) ana thread'de kosar
  (AssetWatcher deseni; jest ortasinda drain YOK — IMGUI id-kaymasi kurali).
- editor/RpcCommands.cs builtin'ler: rpc.describe, scene.dump (tam doc dokumu),
  comp.schema/comp.types, scene.addGo/removeGo/rename/reparent, comp.add/remove,
  prop.set (SetProp bogazi — undo bedava; her RPC yazimi SealTop'lu ayri undo girisi),
  transform.set ("x y z"), go.setActive, comp.setEnabled, edit.undo/redo, scene.save,
  play.start/stop. Hepsi mevcut EditorScene bogazlarina ince marshal.
- tools/rpc.ps1: test istemcisi (stdin satirlari -> yanitlar). DIKKAT: here-string TEK
  oge olarak pipe olur (tek yanit okur) — satir dizisi @('...','...') ile gonder.
- Dogrulandi: describe, addGo, comp.add(SpriteRenderer), prop.set Width=96 doc'a yazildi,
  transform.set + edit.undo geri aldi, hata yollari (yanlis id/alan) temiz mesajli.
- TUR 2 — TAMAMLANDI (ayni gun, uctan uca dogrulandi): DEFERRED YANIT altyapisi
  (RpcHost.Defer(poll) — handler isaret birakir, Pump her frame poll'lar, DocNode donunce
  yanit yazilir; _pending listesi; FrameCount pump sayaci). AssetWatcher'a BuildVersion/
  LastBuildOk/LastBuildOutput sinyali (Apply'de set). Yeni komutlar:
  * scene.dump artik diff BASELINE'ini sifirlar (id->DumpGo klonu); scene.diff =
    added/changed/removed (DocNode.Equal kiyasi, baseline guncellenir).
  * scene.dumpLive(which=edit|play) — CANLI sahne runtime dokumu (RootCount/GetRoot +
    FirstChild/NextSibling internal gezinti; layout-cozulmus pozisyonlar gorunur).
  * sim.run(frames,stop) — play degilse baslatir, N pump-frame sonra play dumpLive'iyla
    doner, stop=true sonda Stop.
  * asset.write/asset.read (Assets'e goreli, path traversal guard'i GuardAssetPath) —
    toptan YAML/kod dosya kanali; watcher derleme/reload'u kendi yapar.
  * code.build — RequestBuild + BuildVersion degisince ok/errors (": error " satirlari).
  * log.tail(count) — EditorLog.Snapshot son N giris.
- AI dongusu dogrulandi: asset.write bozuk .cs -> code.build ok:false + satir/kolon hatalar
  -> duzeltilmis kod -> ok:true -> comp.types'ta yeni tip gorundu.
- ILK PROMPT-DRIVEN SAHNE (Login.scene) — TAMAMLANDI, RPC'den uctan uca: scene.new/scene.open
  komutlari eklendi (App.OpenScene; dosya yoksa bos doc + Save yaratir). Akis: comp.schema
  LayoutBox (37 alan, halusinasyonsuz) -> addGo x7 -> comp.add + prop.set'lerle stil ->
  ApplePlatformOnly.cs (asset.write+code.build) -> comp.add -> sim.run dogrulama -> save.
  Sahne: fitScreen gradient BG (layout None) + anchor'li Logo(ust-orta)/ButtonRow(merkez,
  Horizontal autosize — buton sayisindan bagimsiz ortalanir; inaktif cocuk dizilimden cikar,
  2 buton ±82 dogrulandi)/Copyright(alt-orta). Dersler: id'leri addGo yanitlarindan al
  (tahmin riskli), comp.schema her yeni tip icin ilk adim.
- KARAR (kullanici): edit projeksiyonunda Awake KOSMAYA DEVAM (WYSIWYG) — platform gate
  SceneView'da da gizler. Scene.IsPlaying bayragi onerildi ama ertelendi ("sorun yasarsak
  bakariz"). Sorun cikarsa: Scene.IsPlaying alani + PlayMode set + script'te
  if(!scene.IsPlaying) return deseni.
- ★ KONVANSIYON — NIYET GUNLUGU (kullanici onayladi): her AI sahne isinin sonunda
  <sahne>.intent.md (sahnenin yanina, asset.write ile) APPEND-ONLY yazilir: tarihli giris =
  prompt + yapi ozeti + INVARIANTLAR (YAML'da gorunmeyen kurallar: "buton sayisindan
  bagimsiz ortali" gibi) + sim.run dogrulama sonuclari. Sahneye sonraki AI mudahalesi
  ONCE intent dosyasini okur (invariant bozmamak icin). Ilk ornek:
  Projects/Sandbox/Assets/Scenes/Login.intent.md. Eski girisler degistirilmez, bayatlarsa
  yeni girisle duzeltilir. Altyapi yok — saf konvansiyon.
- SIRADA (AI-driven yol haritasi): scene.diff, code.write/code.build (deferred yanit —
  AssetWatcher sonucuna baglanir), sim.run(frames), render.screenshot, MCP sarmalayici.

## DRIVEN TRANSFORM (Unity DrivenRectTransformTracker paritesi) (2026-09-12, testler 18+14+15 PASS, kullanıcı görsel testi bekliyor)
- Engine (LayoutBox.cs, core'a dokunulmadı): [Flags] DrivenTransformProperties{Position} +
  ITransformDriver{DrivenProperties, PositionEditField} + static TransformDriver.Driven(go,out drv)
  (GetComponentOfType(interface) ile jenerik sorgu). LayoutBox implemente eder:
  fitScreen|parentBox → Position driven; PositionEditField = parent layout==None||ignoreLayout
  iken "anchoredPos" (move oraya yönlenir), grup diziyorsa/fitScreen'de null (kilit).
- Runtime enforcement: SetPos _placedX/Y+_hasPlaced kaydeder (fitScreen dalı da SetPos kullanır;
  bağımsız kutu dalı _hasPlaced=false); Encode başında localPosition != placed → geri basılır
  (dış yazım her frame ezilir, z korunur).
- Editör (LayoutBox'ı TANIMAZ, yalnız arayüz): EditorScene.SetTransform guard'ı — live driven
  Position ise pos=g.Pos (paste/diğer araçlar dahil tek boğaz). MoveTool Entry listesi:
  normal → SetTransform; proxy → CompDoc(ilk aynı-tip)+schema field bulup es.SetProp("x y" R
  invariant, PropOp coalesce, start değeri basıştan kümülatif); PositionEditField null → seçimde
  dursa da taşınmaz. InspectorPanel: posDriven ise Vec3RowDisabled (soluk salt-okunur, CANLI
  localPosition gösterir — doc Pos bayat), rot/scale serbest.
- Kapsam bilinçli dar: yalnız Position; w/h (grow/stretch'te fiilen kullanılmayan authored
  preferred) kilitlenmedi — alan-düzeyi driven gösterimi ileriye borç.

## SCENEVIEW GIZMO EXTENSIBILITY: HANDLES + EDITORTOOL (2026-09-12, testler 18+14+15 PASS, kullanıcı görsel testi bekliyor)
- Unity modeli üçlüsü kuruldu (1+2. dilim): editor/Handles.cs + EditorTool.cs + BuiltinTools.cs.
- HandleUtility: BeginLayout(mouseInside) her Layout pass başında (SceneViewPanel çağırır;
  imleç panel dışındaysa AddControl no-op — komşu panel tık çalma guard'ı). AddControl(id,dist)
  nearest'ı STRICT < ile günceller → eşitlikte İLK kayıt kazanır (kayıt sırası = öncelik,
  eski "merkez > X > Y" hardcode'unun yerini aldı). PickDistance=5. Mesafe helper'ları:
  DistanceToRect (içeride 0), DistanceToCircleEdge(center,r,band) — rotate band=4 (+5 pick = eski ±9).
  WorldDeltaToParentLocal buraya taşındı (unsafe).
- Handles.Drag(id,dist,ev) → Act{None,Pressed,Dragged,Released}: GetControlID+AddControl+HotControl
  kalıbı tek çağrı; StartMouse (basış konumu, kümülatif drag referansı), IsHot/IsHover.
- EditorTool: abstract OnToolGui(SceneViewPanel,Event); [EditorTool("Ad", Order=n, Key='W')]
  (Key=GLFW keycode=büyük harf ASCII). EditorTools registry: Rebuild(asms) taramayla kurar
  (EditorApp.RebuildCatalog → editor + game assembly; aktif araç ADIYLA korunur — reload güvenli).
- BuiltinTools.cs: GizmoDraw (renkler/kollar/merkez), AxisTool (Move+Scale ortak omurga: 3 id
  koşulsuz, freeRect/xRect/yRect mesafeleri, hover/cursor/Draw), MoveTool (multi-select taşıma),
  ScaleTool, RotateTool (tek id: merkez-rect ∪ halka mesafesi min'i).
- SceneViewPanel sadeleşti: toolbar EditorTools.All'dan döngüyle (genişlik=14+8*len), kısayol
  döngüsü tool.Key'den (HotControl==0 guard'ı — drag ortasında araç değişimi id kaydırır),
  DoGizmoAndSelect = BeginLayout + Active.OnToolGui + select id + braket çizimi (araçtan bağımsız,
  panel çizer). PanelToWorld/WorldToPanel/Zoom PUBLIC (araç API'si). Tool enum/ApplyDrag/
  HandleGizmo/HitAxis/DrawGizmo SİLİNDİ.
- KURAL taşındı: araç içinde de GetControlID'ler koşulsuz (seçim yokken de) alınır.
- KALAN dilimler (3+4): OnSceneGUI custom editor kancası, [DrawGizmo]/OnDrawGizmos pasif çizim.
- SEÇİM BOUNDS VIRTUAL'LAŞTI (aynı gün): Renderer.GetLocalSelectionBounds(out center, out halfSize)
  virtual (false=seçilemez). SpriteRenderer: (0,0)+(W/2,H/2). LayoutBox: pivot ofsetli merkez
  ((0.5-pivot)*rw/rh) + rect yarısı (ResolveIfDirty'li) → LayoutBox artık tıkla-seç/braket/FocusOn
  kapsamında. Editörde SpriteRenderer hardcode'u KALMADI: HitTest/BoundsExtent/DrawSelectionBrackets
  GetComponent<Renderer>()+bounds kullanır; braket kol yönleri bbox merkezine göre (transform pos değil).
  SINIR: GO'da birden çok Renderer varsa İLKİ baz alınır. Gizmo merkezi hâlâ transform.position
  (Unity paritesi); pivot'lu kutuda gizmo köşede görünebilir — bilinçli.

## ISCENESYSTEM: CORE'DAN LAYOUT SÖKÜMÜ (2026-09-12, testler 18+14+15 PASS)
- Scene artık LayoutBox'ı TANIMAZ: _layoutQueue/QueueLayout/SweepLayout/_styleBatch SİLİNDİ.
- engine/managed/SceneSystem.cs: public ISceneSystem {AfterUpdate, BeginRender, EndRender}.
  Scene: _systems dizisi + GetSystem<T>() (lazy, lineer tarama, kayıt sırası=çağrı sırası;
  instance-bazlı = ALC reload güvenli, statik event bilinçli YOK). _frameStamp Scene'de kaldı.
- LayoutSystem (LayoutBox.cs sonunda, internal): kirli kutu kuyruğu + StyleBatch sahibi.
  AfterUpdate=eski SweepLayout; BeginRender/EndRender=StyleBatch Begin/Flush.
  LayoutBox.QueueSweep/Encode → scene.GetSystem<LayoutSystem>().
- Transform mesajları jenerik oldu: LifecycleFlags ushort'a genişledi (ParentChanged=128,
  ChildrenChanged=256); Component.OnTransformParentChanged/OnTransformChildrenChanged virtual.
  Transform.SetParent → GO.NotifyTransformParentChanged (taşınan) + NotifyTransformChildrenChanged
  (eski+yeni parent). LayoutBox.OnReparent statik çağrısı SİLİNDİ (override'larla MarkDirty).
- Bilinçli hardcoded kalanlar: Scene.Tweens (faz sırası kritik core özellik),
  Renderer 'is' kontrolü (Renderer core), Resources.Tick.

## LAYOUTBOX TEXTURE + SCALE9 (2026-09-12, kullanıcı görsel testi bekliyor)
- LayoutBox'a [SerializeField] texture + slice9Left/Top/Right/Bottom (doku pikseli; Slice9
  property 4'ünü set eder). Texture setter ReferenceEquals kıyası, yalnız _meshDirty.
- Stretch mod (slice=0): mevcut tessellation'a per-vertex UV eklendi (UvAt: rect 0..1
  normalize + atlas frame remap, clamp'li — feather bleed yok). Fill rengi yoksa tint=White
  (FillTint), varsa gradient dahil tint'ler. Border textured kutuda dokuyu TINT'ler
  (beyaz texel yok — bilinçli sınır). Radius texture'ı kırpar (fan UV'li).
- Scale9 (herhangi slice>0): 4x4 grid/9 quad, radius/border/AA yok; dilimler sığmazsa
  oranla kısılır. v: üst=1 alt=0 (GL/stbi yönelimi, SpriteRenderer paritesi).
- Atlas paritesi: SpriteTable.Resolve(texture.Name) → atlastan çizer, UV frame'e remap.
  Atlas re-pack cache'i bayatlatır (yalnız stil/boyut değişince tazelenir — bilinen borç).
- Async doku: Encode'da texture.Width/Height != _meshTexW/H → rebuild (placeholder 1x1
  → gerçek boyut gelince UV düzelir).
- LayoutStyleBatch: grup artık (layer, _batchTex) ile bölünür; Flush StyleMat.MainTexture'ı
  grup başına mutate eder (TexView DrawMesh anında yakalanır). DrawMesh uvRect artık 0,0,1,1
  (beyaz grupta vert uv (0,0) → 1x1 beyaz, davranış aynı).
- ★ SORT KEY FIX (aynı gün, kullanıcı repro: sprite ekleyince kutu sırası flip):
  Transparent key'inde ARTIK state tie-break yok — MakeSortKey Transparent = yalnız
  bucket|layer; pip<<40 + texView OR'u yalnız Transparent-dışı modlarda (pip<<40 layer
  bit 40-47 ile ÇAKIŞIYORDU — ayrı bug'dı). Aynı layer = SUBMIT sırası (radix stable;
  bitişik aynı-state run'lar yine merge → 55k stres tek batch kalır). Sonuç: aynı
  layer'da sprite'lar önce (renderer listesi), kutu stilleri sonra (batch en son flush);
  farklı dokulu kutu grupları painter sırasında. Eski "mesh-slot belirsizliği" borcu kapandı.
- Testler: LayoutTests.Scale9Smoke eklendi (grid/uv/stretch-dönüş). 18+14+15 PASS.

## STYLEBOX BATCH + PER-EDGE/GRADIENT BORDER (2026-09-12, kullanıcı görsel testi bekliyor)
- Kutu başına Mesh KALDIRILDI (batch kırıyordu: RenderQueue batch key'i mesh slot içerir →
  N kutu = N drawcall). Yeni model: LayoutBox lokal tessellation'ı CPU'da cache'ler
  (_styleVerts/_styleIdx, yalnız stil/boyut değişince rebuild), sahne-sahipli
  LayoutStyleBatch (LayoutBox.cs sonunda) her frame world'e taşıyıp SortingOrder başına
  TEK dinamik mesh'e yazar → tipik UI = 1 drawcall. Encode = batch.Add; Scene.Render
  _styleBatch.Begin/Flush sarar. Grup içi painter sırası = (SortingOrder,_paintSeq) sort +
  yazım sırası; 65k vertex üstü aynı layer'da mesh böler (radix stable → sıra korunur).
- GPU güncelleme frame'de BİR KEZ: Scene._frameStamp (Update'te ++) gate'i — ikinci kamera
  (SceneView+Game) yalnız re-encode eder (sokol update-once kuralı). Buffer büyümesi:
  eski Mesh.Destroy() (deferred release) + yeni CreateDynamic (pow2).
- Mesh.CreateDynamic(vcap,icap) + UpdateDynamic(ptr'ler) eklendi (UsageDynamic, Persistent,
  CPU tarifsiz → evict edilemez). de_sokol_update_buffer shim'de zaten vardı.
- BORDER GENİŞLEDİ: borderWidth → borderLeft/Top/Right/Bottom (BorderWidth property 4'ünü
  set eder, get=left) + borderColor2/borderGradient (dikey, fill gradient'le aynı model).
  BuildPath kenar-başına inset alır; eşit olmayan inset'te köşe ELİPTİK (CSS modeli:
  rx=r-insetX, ry=r-insetY); segment topolojisi hâlâ orijinal radius'tan sabit (ringler
  eşleşir). Kenar/iç ring rengi VERTEX BAŞINA: BandAt(edge,inner)>0.5 birim ise border,
  değilse fill → genişliği 0 kenar border rengi almaz. Karşılıklı kenar toplamı kutuya
  sığmazsa oranla kısılır. ESKİ 'borderWidth' serileşmiş alan okunmaz (sessiz düşer).
- Layer artık SortingOrder (paintSeq eklenmez — sıra batch içi vertex sırasında). Frozen
  frame debugger'da dinamik buffer içeriği güncellenmeye devam eder (kozmetik, editor-only).
- Testler 18+14+12 PASS. LayoutTests.StyleMeshSmoke _styleVerts doğrular oldu.

## GPU DESTROY ERTELEME (2026-09-12, kullanıcı testi bekliyor)
- BUG: edit → RefreshLive/LayoutBox rebuild GUI SIRASINDA (frame ortası) Mesh.Destroy →
  buffer'lar hemen ölüyor ama bu frame'in kuyruğu (Scene.Render GUI'den ÖNCE dolar) onları
  encode ediyor → VALIDATE_ABND_VBUF/IBUF_ALIVE PANIC. Frozen'da ayrıca capture replay'i
  ölü handle bind ediyordu (Collect guard'ı DOĞRUDAN Destroy'u kapsamıyordu).
- FIX (GpuResource.cs): GpuResource.Destroy artık ReleaseGpu ÇAĞIRMAZ — Unregister +
  Resources.DeferRelease listesine; FlushReleases sonraki frame başında Resources.Tick'te
  (önceki stream çalıştıktan sonra) koşar, RenderDebug.Frozen iken BEKLETİLİR (unfreeze
  sonrası ilk Tick'te boşalır). _resSlot<0 = çift Destroy guard'ı. Türev Destroy'lar CPU
  tarifini hemen bırakır (sorun değil; unregistered olduğundan Collect da dokunamaz).
- KURAL netleşti: frame ortasında GPU release YASAK — tüm yok etme Tick'e akar.

## FRAME DEBUGGER: YALNIZ OYUN CAPTURE (2026-09-12, kullanıcı testi bekliyor)
- EditorApp: sceneCam (oyun, Order=0) EncodeAll'dan ÇIKARILDI, cb.Begin sonrası elle encode;
  öncesi/sonrası cb.Length → RenderDebug.NoteGameRange(start,end,sceneCam.Target).
- RenderDebug.AfterSubmit yalnız oyun dilimini kopyalar; CapturedW/H fbo==0'dan gelmez
  (oyun pass'i offscreen) → NoteGameRange'deki RT boyutundan. PatchReplayTargets: kopya
  stream'de BeginPassOffscreen colorView==oyun RT view'ı → ReplayTarget view'larına KALICI
  patch (offset: opcode+22/+26). Native değişiklik YOK (fbo==0 override artık fiilen kullanılmıyor).
- Sonuç: capture/draw listesi/önizleme yalnız oyun draw'ları; editör UI girmiyor.
- REPLAY "SCREENSHOT" MODELİ (aynı gün): ReplayIfFrozen artık HER FRAME replay ETMEZ —
  yalnız yeni capture veya MaxDraws değişince bir kez koşar (_replayedMaxDraws cache);
  RT arada sabit görüntü. Kullanıcı sorusu netleşti: live crash'in sebebi Live butonu değil
  frame-ortası destroy'du (GPU DESTROY ERTELEME fix'i); Live butonu KALDIRILMADI.
  Frozen guard'ları (resize/destroy erteleme) hâlâ şart — satır tıklanınca re-replay var.

## SPLITTER CRASH — KÖK NEDEN BULUNDU + FIX (2026-09-12, kullanıcı testi bekliyor)
- Repro: Frame Debugger capture (Frozen) → splitter drag → Scene/Game panel resize →
  RT destroy+recreate → frozen capture'daki stream ESKİ RT view'larını referanslıyor →
  sokol validation: VALIDATE_BEGINPASS_COLOR/DEPTHSTENCILATTACHMENTVIEW_ALIVE +
  VALIDATE_ABND_VIEW_ALIVE → PANIC abort. (Resources.Collect frozen guard'ıyla aynı sınıf.)
- FIX: RenderDebug.Frozen iken RT resize ERTELENIR — GameOutput.RequestSize erken çıkar,
  SceneViewPanel repaint resize koşuluna !RenderDebug.Frozen. Unfreeze sonrası ilk repaint
  boyut farkını görüp normal resize eder. Frozen'da panel büyütülürse görüntü geçici stretch.
- KURAL: Frozen'ken HİÇBİR GPU kaynağı destroy edilmemeli (capture ham handle replay eder).
- YAPILDI (önceki turlar): sokol logger (de_sokol_log → stderr + crash_native.log; logger'sız
  sokol validation hatası SESSİZCE abort ediyordu) + EditorApp UnhandledException → crash.log.
- tools/splitter_stress.ps1: splitter'ları ileri-geri sürükleyen otomasyon (repro aracı).

## UI MİMARİ KARARI: LayoutBox (2026-09-12 — DİLİM 1+2 KODLANDI, testler 12/12 PASS)
- ★ ÇALIŞMA KURALI (kullanıcı talimatı): GÖRSEL DOĞRULAMALARI KULLANICI YAPAR — screenshot
  otomasyonu/editörü açıp bakma turu YAPMA; build + açılış testleri yeşilse kullanıcıya bırak.
- DİLİM 2 YAPILDI (StyleBox çizimi, LayoutBox.cs içinde): LayoutBox artık Renderer —
  fillColor/fillColor2+gradient(dikey)/borderWidth+borderColor/radiusTL-TR-BR-BL (+CornerRadius
  hepsini set eder). Fragment DSL'de custom uniform OLMADIĞI için SDF değil TESSELLATION
  (ImGui yolu): dolgu fan + border ring + 1px AA feather şeridi (Feather=1 dünya birimi);
  gradient per-vertex renk (lineer → üçgenlemeden bağımsız birebir doğru). BuildPath(inset)
  sabit segment topolojisi → ringler nokta-nokta eşleşir (inset'te köşe merkezi sabit kalır,
  r-inset<0 → köşe noktası). Mesh yalnız stil/boyut değişince rebuild (alloc kabul; Mesh registry
  slot'ları reuse edilmiyor — sık rebuild'de liste büyür, bilinen ufak borç). Encode: HasStyle
  değilse çık; ResolveIfDirty; StyleMat (paylaşılan beyaz 1x1 + default shader) + SortingOrder.
  Stil setter'ları yalnız _meshDirty (layout kirletmez); OnValidate ikisini de kirletir.
- EDITÖR FONT BOYUTU: EditorSettings.uiFontSize (default 16, clamp 10-24) → Gui.FontSize açılışta.
  GuiDock tab başlığı = Gui.FontSize-1, Inspector caption = Gui.FontSize-3 (sabit 13/11 kaldırıldı).
- DRAW ORDER FIX: Transparent sort key aynı layer'da MESH SLOT'una düşer; her kutunun kendi
  mesh'i olduğundan parent çocuğu örtüyordu. Çözüm: resolve DFS'inde _paintSeq numaralanır,
  Encode layer = SortingOrder + _paintSeq (parent < çocuk garantisi). SINIR: ayrı kutu ağaçları
  arası ve kutu-vs-SpriteRenderer aynı layer'da hâlâ mesh-slot belirsizliği — sprite'ı kutunun
  üstünde göstermek için SortingOrder ver (FitMode dilimi gelince kökten çözülecek).
- GEÇİCİ: editor/UiStyleDemo.cs + EditorApp'te "UiStyleDemo.Spawn(); // TEMP" — görsel demo
  (gradient panel + 3 grow kart + alt bar; ekran görüntüsüyle doğrulandı: radius/AA/gradient/
  border doğru). Kullanıcı işi bitince demo çağrısı + dosya SÖKÜLECEK.
- DİLİM 1 YAPILDI (engine/managed/LayoutBox.cs): LayoutBox component — fitScreen/w/h/pivot/
  anchorMin-Max/anchoredPos/grow/ignoreLayout/layout(None|H|V)/spacing/pad4/alignChildren/reverse.
  Backing alanlar [SerializeField] private + dirty'leyen public property'ler (alan adları lowercase
  Inspector'da görünür). Lazy pull: RectWidth/RectHeight/WorldCenter ResolveIfDirty (zincir tepesinden
  measure→arrange; dirty ⇒ tüm atalar dirty invariant'ı). Scene: _layoutQueue + SweepLayout
  (Update sonunda, simulate'ten BAĞIMSIZ — edit modunda da; fitScreen kutular kuyrukta kalır =
  resize pull'suz da yakalanır). Transform: LastChild/PrevSibling accessor + SetParent →
  LayoutBox.OnReparent (eski+yeni taraf kirlenir). Component.OnValidate virtual eklendi
  (editör ApplyField sonrası çağırır → MarkDirty). TypeCatalog.CopyTo artık public + [SerializeField]
  nonpublic alanları kopyalar (clone kapsamı = serileşme kuralı; LayoutBox backing'leri için şart).
  editor/LayoutTests.cs açılışta koşar (5 senaryo 10 assert: ekran kökü+anchor Update'siz pull,
  H-flex grow payları, autosize+değişiklik-sonrası pull, ignoreLayout+stretch arkaplan, resize sweep).
- DİLİM 1'DE YOK (sonraki dilimler): StyleBox çizimi (SDF rounded-rect shader), SpriteRenderer
  FitMode, UIText/TextSprite, Overflow/scissor, measure cache (PreferredW/H her resolve'da yeniden
  hesaplanıyor — kutu ağaçları küçükken sorun değil), Inspector koşullu alan gösterimi, Wrap/Grid,
  AlignSelf, UseChildScale, min/max/margin clamp'leri.
- SORUN: Unity Canvas/RectTransform ayrı-evren modeli UI+oyun elemanı karışımını kabusa çevirir.
  Hedef: ağırlıkla 2D ama 3D (perspektif kam, 3D model) rahat karışabilmeli — layout EKRAN uzayına
  değil hep PARENT'IN LOKAL DÜZLEMİNE kurulur (world-space'te perspektif rotation bedava çalışır).
- KARAR — TEK ŞİŞMAN COMPONENT `LayoutBox` (CSS-box modeli; HGroup/LayoutElement/Fitter saçılması YOK):
  * Kendi yerleşimi: w/h, pivot, anchorMin/Max (parent KUTUYA göre; 9'lu Snap = editör preset'i),
    margin, min/max. Rect'siz parent = sıfır boyutlu nokta (anchor dejenere, anchoredPos=localPos).
  * Çocuk dizme: Layout=None|Horizontal|Vertical (Grid/Wrap v2), Spacing, Padding(4), AlignChildren,
    Reverse, UseChildScale(default KAPALI — scale layout'a girmez, eski Flash motoru gibi isteyene bayrak).
  * Çocuk-başına flex ÇOCUĞUN kutusunda: Grow (ağırlıklı pay; eski StretchZero'nun genellemesi),
    AlignSelf, IgnoreLayout (arkaplan/dekor muafiyeti).
  * STİL (StyleBox — "CSS kadar" DEĞİL, kapalı set): fill düz/linear gradient, bg image
    stretch/9-slice/tile, border, corner radius (köşe başına) → tek quad + rounded-rect SDF shader,
    normal RenderQueue+sortingOrder (ayrı render yolu YOK). Shadow/blur/filter v1'e GİRMEZ (blur=RT yolu).
  * Overflow=Visible|Hidden (scissor opcode hazır); ileride ScrollView = Hidden + içerik ofseti.
- Transform SADE KALIR (pos/rot/scale) — "w/h/pivot Transform'a" fikri tartışıldı ve REVİZE EDİLDİ:
  oyun nesneleri (55k stres) layout vergisi ödemez, kutu çözümü GO transform.pos'a yazılır.
- RENDERER KURALI: layout renderer'ı ASLA boyutlandırmaz/ölçmez; renderer kutuyu OKUR — FitMode
  (None|Stretch/Fit/Cover, aynı GO'daki kutuya). İçerik görseli=renderer, dekoratif görsel=kutu stili.
- ASYNC KURALI (eski motorun acısı): layout girdisi ya AUTHORED ya senkron ölçülebilir; async runtime
  verisi layout'u ASLA süremez. Resim boyutu/aspect editörde asset atanınca doc'a BAKE edilir;
  runtime'da inen görsel authored kutuya fit olur (web'in layout-shift dersi). "Resim yüklenince
  re-layout" mekanizması gereksizleşir.
- TEXT: iki component, TEK çekirdek (font/ölçüm/satır kırma/quad üretimi ortak — GuiFont raster+SDF
  ikiliği şablon): UIText = kutu vatandaşı (wrap/align/ellipsis/overflow, preferred size bildirir —
  measured-size sözleşmesinin TEK istisnası, font yerli=senkron; raster piksel-snap) |
  TextSprite = dünya vatandaşı (serbest transform, SDF, layout'suz).
- ÇALIŞMA MODELİ: dirty-tabanlı (setter → en yakın layout'lu ataya kabarcık; autosize yukarı da
  kirletir), TEK layout fazı Update/Tween SONRASI render encode ÖNCESİ, kirli alt-ağaçta measure
  (aşağıdan yukarı, cache'li preferredW/H) → arrange (yukarıdan aşağı), AYNI frame'de biter
  (Unity ForceRebuild gecikmesi yasak). Zero-GC: FirstChild/NextSibling gezinti, sahne-sahipli reuse
  dirty listesi (TweenPool deseni), text ölçüm cache'i.
- DOC/UNDO: layout çıktısı YALNIZ CANLIYA yazılır, doc'a asla (doc-kaynak modeli sızmayı imkânsız
  kılar — eski motorda XML kaynak olduğu için aynı numara çalışıyordu). Driven alanlar Inspector'da
  soluk+kilitli gösterilmeli. Ekran kökü = kameraya fit kök kutu (ekran özel bir kavram değil).
- ZAMANLAMA KARARI (okey/ıstaka senaryosu — "bir frame bekle" hack'lerini öldürür): LAZY PULL —
  Transform._getWorldMatrix deseninin aynısı. Setter yalnız kirletir; box.Rect/WorldRect OKUYAN
  çözer (ResolveIfDirty: en üst kirli ataya çık, oradan measure→arrange senkron). Render öncesi
  faz sadece süpürge (okunmamış kirlileri çözer). Reentrancy guard şart (resolving bayrağı).
  Opsiyonel OnRectChanged callback'i (resolve anında, gerçek değişimde) — konfor, zorunluluk değil.
- NOT: Wrap LAZIM OLACAK (kullanıcı isteği) — v1'de yok ama tasarımda kapı açık bırakılsın.
- AÇIK KALAN: kutu ağacı eşleşmesi — katı "doğrudan çocuk" ile başla (arada kutusuz GO atlamak
  sürpriz üretir), ihtiyaç kanıtlanırsa gevşet. Inspector'da moda göre koşullu alan gösterimi +
  foldout gruplama gerekecek (25+ alan tek listede olmaz — editör işi).
- Eski Flash motorundan (Digiplay UISprite/HGroup) taşınan dersler: natural=max(authored,content)
  measure kavramı, dirty invalidation, Hidden≠Visible (Hidden layout'tan çıkarır+parent invalidate),
  flex-grow (StretchZero), min/max clamp + MaxWidthPercent. Ölen: Snap/Stretch enum'ları (anchor'a
  genellendi), string-parse Margin setter'ları, HasBackground ilk-çocuk numarası (stil + IgnoreLayout),
  kalıtım modeli (UISprite:Sprite2D → component).

## ASSET SOURCE + PAK (RELEASE YOLU) — TAMAMLANDI (2026-09-12, uçtan uca doğrulandı)
- KARAR: editör=loose dosya+.meta (esnek), release=tek game.pak (tarama/meta/decode-yol yok).
- FORMAT DPK2 (0x324B5044) — GİRİŞ-BAZLI ZLIB (download size kararı): index girişleri
  key, guid, offset, storedLen, rawLen. rawLen==storedLen → stored (png zaten sıkışık,
  kazanç yoksa ham). Yazma .NET ZLibStream(SmallestSize), native çözme stb_image'ın GÖMÜLÜ
  stbi_zlib_decode_buffer'ı — iki uçta da sıfır yeni bağımlılık. Giriş-bazlı olduğundan
  rastgele erişim (fd+offset, Android AAsset noCompress) bozulmaz.
- engine/managed/AssetSource.cs: AssetSource abstract {StartLoad, ReadBytes, Exists, FillGuidTable}
  + LooseFileSource(root) + PakSource(pakPath) + PakWriter (Write → (rawTotal, pakSize) döner).
  Guid tablosu pak index'inde GÖMÜLÜ → release'te ScanMetas hiç koşmaz. PakSource.ReadBytes
  zlib'li girişi managed ZLibStream ile açar (prefab/scene metinleri).
- AssetDatabase: _source alanı; ctor(string root)=LooseFileSource (editör aynen), ctor(AssetSource)
  =release (FillGuidTable ctor'da). LoadTexture/ReloadTexture → _source.StartLoad(key);
  LoadPrefab → _source.ReadBytes. ScanMetas _root==null ise no-op.
- Native (sokol_shim.c): de_asset_load_range(path, offset, length, rawLength) — worker
  aralığı okur, rawLength!=length ise önce stbi_zlib_decode_buffer sonra stbi_load_from_memory.
  de_asset_load = range(0,0,0). Sokol.AssetLoadRange(path,off,len,rawLen) binding.
- editor/AssetPackBuilder.cs: [MenuItem("Project/Build Asset Pack",1)] → Assets/ tarar
  (.meta/.cs/Editor/ atlanır), deterministik sıra, <proje>/Build/game.pak + VERIFY:
  index sayısı + tüm girişler bayt-eşit (zlib round-trip dahil) + ilk texture pak'tan
  DTEX+inflate yolundan native yükleme.
- DTEX (decode'suz texture) — TAMAMLANDI: build texture'ları native worker'la BİR KEZ çözer
  (DecodeToDtex: Sokol.AssetLoad+poll+Marshal.Copy), DtexFormat.Build (engine AssetSource.cs:
  [magic 'DTEX' 0x58455444][format 0=RGBA8][w][h][pikseller], satır 0 altta) pak'a yazar;
  runtime'da native de_try_dtex (sokol_shim.c, de_decode_pixels: önce DTEX, değilse stbi)
  memcpy+upload yapar — png decode maliyeti SIFIR. malloc'lu buffer stbi_image_free(=free) uyumlu.
  ASTC/BCn ileride sadece yeni format enum + upload yolu. Decode başarısızsa ham dosya kopyalanır
  (runtime stbi fallback). PakWriter imzası (Key,Guid,byte[] Data) oldu (içeriği çağıran hazırlar).
- DTEX format 1 (PNG-tarzı satır filtreli) — TAMAMLANDI: DtexFormat.BuildFiltered satır başına
  None/Sub/Up/Average/Paeth min-sum heuristiği (libpng), [1 filtre baytı + stride] satırlar;
  native de_try_dtex fmt==1 → lineer defilter (de_paeth). Builder BuildFiltered kullanır.
  VERIFY güçlendirildi: pak(DTEX+inflate+defilter) pikselleri kaynağın doğrudan stbi decode'uyla
  BİREBİR karşılaştırılır (AwaitPixels helper — job poll + Marshal.Copy).
- Test: 569KB → 426.2KB (%21.6 açık boyuta göre; filtre -%25 kazandırdı), pikseller birebir OK.
- ★ TRADE-OFF güncel: filtreli DTEX pak'ı hâlâ PNG'li pak'tan büyük (102KB→426KB bu test
  içeriğinde; .NET deflate libpng zlib-9'dan zayıf + test atlası gürültülü). Gerçek art'ta
  oran daralır. Kalan adaylar (yapılmadı): asset bazında dtex opt-out, daha iyi deflater, ASTC.
- Android planı (konuşuldu): .pak aapt noCompress'e; AAssetManager_open+openFileDescriptor
  → fd+baseOffset+length, range okumaya base eklenir (API birebir uyar). Play indirme boyutunu
  zaten kendi sıkıştırır; giriş-bazlı zlib asıl ileride BCn/ham piksel formatında kazandırır.
- KALAN release borçları: strip/Consumes (yalnız kullanılan asset'ler), pre-decoded/BCn texture,
  scene/prefab binary, source generator (TypeCatalog AOT), standalone player exe (henüz yok),
  çoklu pak (CompositeSource, Play Asset Delivery eşlemesi).

## PREFAB SERTLEŞTİRME TURU (2026-09-12 — kullanıcı testi bekliyor)
- Analiz bulguları (açık borçlar): nested prefab yok ve expand'de PrefabGuid klonlanmadığından
  SESSİZCE düşer; comp eşleme Tip:occurrence kırılgan (sıra değişirse override kayar); Apply
  kapsamı dar (addComp/removeComp/çocuk apply yok, Apply undo'suz); PrefabIds budanmıyor;
  prefab edit modu / unpack / duplicate yok; FindById O(n) → büyük sahnede kuadratik.
- FIX 1: WritePrefabRecord — prefab kayıpken BuildOverrides null dönünce artık
  root.PendingOverrides AYNEN geri yazılır (eskiden missing prefab + Save = override kaybı);
  PrefabIds null guard'ı (NRE).
- FIX 2: Reparent/MoveGameObject g.IsPrefabChild ise reddedip EditorLog.Warning basar
  (parent+kardeş sırası diff'e girmiyor, taşıma Save'de sessizce kaybolurdu — Unity da yasaklar).
- FIX 3 (prefab YARATMA): hiyerarşiden GO'yu Proje paneline sürükle → görünen klasöre
  <ad>.prefab + CONNECT. EditorScene.CreatePrefabAsset: CollectSubtree → sceneId→localId
  (kök=1) → klon + RemapCompRefs(clearUnmapped:true — alt-ağaç dışı ref'ler prefab dosyasında
  BOŞALIR, instance'ta kalır = override olur) → NoteSelfWrite+Save+ScanMetas+PathToGuid →
  ConnectPrefabOp (undo işaretleri söker, dosya kalır; canlı etkilenmez, RefreshLive yok).
  RemapValue/RemapRefScalar/RemapCompRefs opsiyonel clearUnmapped parametresi aldı
  (RemapCompRefs internal oldu). ProjectPanel Repaint'te SceneObject payload'ına RegisterTarget.
- Source generator (TypeCatalog release/AOT) AYRI iş olarak duruyor; prefab O(n) taramalarını
  çözmez (o iş id→GoDoc indeksi ister, yapılmadı).
- Açılış testleri 18+14 PASS. Otomatik prefab round-trip testi HÂLÂ YOK (borç).

## DOCK TAB REORDER (2026-09-12)
- BUG: tab drag-drop hep bar sonuna ekleniyordu (MoveNow append-only) + aynı leaf'e bırakma
  "aktif tab yap" dalına düşüyordu → sıralama imkânsızdı.
- FIX (GuiDock.cs): MoveNow(panelId, targetLeaf, insertIndex=-1) — araya ekleme, aynı-leaf
  taşımada kaldırma sonrası indeks kayması düzeltilir (srcIndex<idx → idx--). _pendingMoveIndex
  eklendi. MouseUp'ta target>=0 ise (aynı leaf DAHİL) TabInsertIndexAt(mouse.x'ten yuvarlanmış
  boşluk indeksi 0..TabCount) ile pending move. Repaint: bar üstünde sürüklerken sarı dikey
  ekleme çizgisi (idx*(TabW+2)). Salt tık (drag'siz) hâlâ sadece aktif tab yapar.
- FIX 2 (aynı gün): aynı-leaf reorder RemoveFromTree'den geçince TEK tab'lı leaf boşalıp
  CollapseLeaf ile ölüyordu (Used=false) ve tab ölü node'a ekleniyordu → zone + panel
  kayboluyor, "boşalan zone'a tab eklenemiyor" olarak görülüyordu. Çözüm: MoveNow'da
  aynı-leaf taşıma YERİNDE dizi rotasyonu (removal yok); farklı-leaf yolunda RemoveFromTree
  sonrası hedef Used/TypeLeaf yeniden doğrulanır.
- FIX 3 (detach ekran-dışı bug'ı): detach konumu logical*Gui.Scale ile çevriliyordu ama
  EditorApp Gui.Scale = uiScale*2 (supersample çarpanı) atıyor → pencere sanal masaüstü
  DIŞINA yaratılıyordu (taskbar'da var, tıklanamaz, preview beyaz). Çözüm: GuiDock detach'te
  GERÇEK GetWindowContentScale(MainWindow) kullanır. KURAL: Gui.Scale konum/piksel çevirisi
  için güvenilmez (render çarpanı içerebilir); ekran koordinatına çevirirken hep content scale al.
- ÇOKLU DOCK HOST (Unity modeli, aynı gün — kullanıcı testi bekliyor): detached native pencere
  artık tek-panel değil, DOCK HOST. Panel.Detached/Window/HomeLeaf SİLİNDİ → DockWin{Used,Win,Root}
  havuzu (MaxWins=16); her pencere ORTAK node havuzundan kendi leaf'ini barındırır, DrawLeaf/
  ResolveAndDraw aynen pencerede koşar (tab bar bedava). _ctxWin (-1=ana) çizim bağlamı,
  _dragWin drag'in başladığı pencere (ghost+highlight yalnız orada — diğerlerinin mouse'u bayat).
  CROSS-WINDOW DROP: MouseUp'ta önce kendi ağacı FindLeafAt, yoksa ToScreen(GlobalMouse) →
  FindLeafAtScreen (diğer DockWin'ler + ana; GetWindowPos+ContentScale ile pencere-lokal'e çevirip
  FindLeafAt — rect'ler son turdan geçerli), hiçbiri değilse yeni pencereye detach. DetachNow yeni
  DockWin + Leaf(panel) yaratır. Pencere temizliği ApplyPending'de: Win==null (X'le kapandı) →
  tablar ana ilk leaf'e, boş root (TabCount==0, son tab taşındı) → pencere destroy + slot boşalır.
  RemoveFromTree/FocusPanel/MoveNow FindLeafWithPanelAll (ana + tüm pencere ağaçları) kullanır.
  UpdateWindows/Encode/Present _wins üzerinden. SINIR: pencere ağaçları SaveLayout'a girmez
  (yüklemede panelleri ilk leaf'e döner); pencere içi split yok (tek leaf); HotControl hâlâ
  pencereler arası global (bilinen borç).
- NATIVE DRAG GHOST (aynı gün): canvas-içi ghost KALDIRILDI → imleci takip eden gerçek
  dekorasyonsuz native pencere (UpdateDragGhost, UpdateWindows sonunda). Hints: DECORATED=0,
  FLOATING=1, FOCUSED/FOCUS_ON_SHOW=0, MOUSE_PASSTHROUGH=1 (GLFWConst'a FLOATING/FOCUS_ON_SHOW/
  MOUSE_PASSTHROUGH eklendi). DİKKAT: DefaultWindowHints ÇAĞIRMA — GL context hint'lerini
  sıfırlar (sonraki shared-context pencereler bozulur); set edilen hint'ler elle geri alınır.
  Konum: GetWindowPos(origin)+GetCursorPos(origin) (ikisi de fiziksel px; capture sayesinde
  pencere dışında da akar). İçerik: tab rengi + başlık (kendi Gui.Frame'i); Encode/Present
  ghost'u da işler. Drag bitince Destroy.
- DROP-TO-SPLIT (aynı gün): "boşalan zone'a tab konamıyor" şikâyetinin gerçek çözümü — collapse
  olan zone geri YARATILAMIYORDU. SplitDirAt(leaf,p): içerik kenar bandı %25 → yön (0=sol 1=sağ
  2=üst 3=alt), orta/tab bar → -1 (tab olarak). SplitNow: RemoveFromTree SONRASI parent okunur
  (collapse kardeş bağlarını değiştirir) + hedef yeniden doğrulanır; yeni Leaf + Split(0.5)
  parent'ta hedefin yerine geçer; parent<0 ise _root veya _wins[i].Root güncellenir. Tek-tab'lı
  leaf kendi içine split = no-op. _pendingSplit* ApplyPending'de. Repaint: kenarda yarım-alan
  mavi önizleme, ortada soluk (tab olarak) — pencere içi split artık VAR (eski 'tek leaf' sınırı kalktı).
- CROSS-WINDOW DROP GÖSTERGESİ: hedef pencerede indicator çıkmıyordu — vurgu _dragWin==_ctxWin
  gate'liydi ve diğer pencerelerin event mouse'u bayat (capture origin'de). Çözüm: DragMouseLocal()
  — GetCursorPos(origin)+GetWindowPos(origin) → ekran → FromScreen(_ctxWin) CANLI konum; Repaint
  vurguları bunu kullanır, gate kaldırıldı. KURAL: drag'de başka pencerede konum gerekiyorsa
  event mouse'u değil ekran-uzayı hesabı kullan.
- PENCERE KAPANIŞ DAVRANIŞI (kullanıcı kararı): X ile kapanan native dock penceresinin panelleri
  ana ağaca REDOCK EDİLMEZ — dock'suz kalırlar (Window menüsü → FocusPanel leaf<0 → ilk leaf'e
  dock'lar). ApplyPending temizliği: Win==null → rn.TabCount=0 → node+slot boşalır.
- LAYOUT v2: SaveLayout artık pencereleri de yazar — "v2 <ana ağaç> [W x y w h <ağaç>]*"
  (x/y=GetWindowPos, w/h fiziksel px). LoadLayout v1(salt ana)+v2 okur; pencere kaydı için
  NativeWindow.Open (LoadLayout NativeWindow.Initialize SONRASI çağrılır — güvenli), açılamazsa
  FreeSubtree ile ağaç düşer, panelleri 'kayıp panel' toplayıcısı ilk leaf'e alır. Yardımcılar:
  ParseInt, FirstPanelIn (pencere başlığı için), FreeSubtree.

## CONTEXT COMPILER MVP (2026-09-12)
- A/B testi (tween zincir bug'ı): compile paketi (5,958 tok) TEK BAŞINA teşhis+fix'i taşıdı (0 ek dosya okuma); ek okumalar yalnız test yazımı içindi. Gerçek fix: TweenPool.Kill artık Then() zincirini uyandırır (ölü hedef + Cancel yolları dahil), Next temizlenir; LifecycleTests.TweenChainSurvivesKill 18/18 PASS.
- Maliyet dersi: paket vs seçici-okuyan agent farkı girdi hacminde mütevazı (~%25-40); asıl kazanç KEŞİF TURU sayısında (her tur tüm konuşmayı yeniden gönderir) + naive tam-dosya yapıştırmaya karşı ~30x.

- `tools/context-compiler`: .NET 9 CLI + Roslyn syntax parser. `compile <task> --file ... --budget N --out ...`; `.context/state.json` ile fact/decision/constraint/hypothesis ayrımı, git status sıkıştırması, görev/aktif dosya/değişiklik/referans puanlaması ve yaklaşık token bütçeli Markdown üretir.
- Doğrulama: `SceneDoc.ApplyOverrides` aktif dosyasız bulundu; state + compact git dahil çıktı 3000 bütçede 2974 tahmini token. Tool ve `editor` build temiz. Sonraki katman: semantic symbol/call graph + diagnostics/tests + ölçüm seti.

- Semantic katman tamam: sentetik Roslyn compilation ile 1402 source symbol, 3168 call edge; gerçek caller/callee/dependency ilişkileri output ve scoring'e bağlı. Sentetik compilation diagnostics güvenilmez olduğu için yalnız syntax diagnostics otomatik; gerçek build/test logu `--diagnostics` ile alınır. `verify.ps1` 5 koşul PASS. Ölçüm: 91 C# dosyası ~184301 raw token → 4970 context token (%97.3 azalma).

## STANDARTLAR (2026-09-11 sonrası — kalıcı kararlar)
- OTOMATİK İTERASYON FAZ 1 ÇALIŞIYOR (test edildi): AssetWatcher (FileSystemWatcher Assets/,
  debounce 0.35s, ConcurrentQueue→ana thread Tick) .cs değişince GameCode.CompileOnly'yi
  Task.Run'da koşar (worker-safe: dosya IO+dotnet build subprocess; ALC/sahne YOK), sonuç ana
  thread'de App.ApplyCompiledGame (Stop→UnloadLive→LoadCompiled→RebuildCatalog→Instantiate;
  play'deyse otomatik re-Play). Hata → EditorLog.Error satır satır + Console paneli açılır,
  ESKİ assembly yaşar. Toolbar'da non-modal durum ("Compiling...", kırmızı "Compile failed").
  EditorLog (lock'lu ring 500) + ConsolePanel (Window/Console). Menü Project/Compile+Load Code
  → AssetWatcher.RequestBuild. FAZ 2 DE ÇALIŞIYOR (test edildi): .png/.jpg → AssetDatabase.
  ReloadTexture (yerinde piksel patch, _AttachPixels zaten replace destekli); .prefab →
  InvalidatePrefab + EditScene.ReexpandPrefabs (doc→yaml collapse→parse→expand; prefabIds kalıcı
  olduğundan id/seçim korunur); .scene (açık sahne) → Dirty ise BELLEK KAZANIR + warning, değilse
  ReloadScene; .asset → AssetChangeVersion++ (Inspector cache düşürür). Self-write maskesi:
  AssetWatcher.NoteSelfWrite(path) 2sn yankı yutar (EditorScene.Save, prefab Apply x2, Inspector
  asset save x2, ProjectPanel create x2 çağırır). EditorScene.Dirty = History.Version != _savedVersion.
  KALAN FAZLAR: 3=hash cache+dependency graph+ScriptedImporter,
  4=native progress dialog (de_progress_*)+paralel import+iptal.
- UI DİLİ İNGİLİZCE (menüler File/Edit/Project/Run/Window; kod yorumları Türkçe kalır).
- ATLAS (GÜNCELLENDİ 2026-09-13: SpriteTable SİLİNDİ — bkz. en üstteki SPRITE PRİMİTİFİ bölümü.
  Parite kuralı duruyor: atlas üyesi HER modda atlastan çizilir; artık mekanizma Sprite.BindRegion):
- Uzantılar YAML'sız: .scene / .prefab / .asset (içerik yaml; .vscode/settings.json association var).
- ObjectSerializer (engine/Serialization): [Serializable](System) + new() + Component-değil sınıf =
  asset. Save/Load/LoadInto/ToNode/FromNode; GoRef/CompRef atlanır. Dosya: type: + fields:.
  CREATE MENÜSÜ OPT-IN: [CreateAssetMenu(MenuName="Config/X", FileName="X")] şart (Unity paritesi);
  [Serializable] tek başına menüye girmez (ObjectSerializer.IsCreatable).
  Editör ayar standardı da bu (EditorSettings → UserSettings/EditorSettings.asset: lastScene,
  hotReload). TEK İSTİSNA: dock layout (UserSettings/Layout.txt, özyinelemeli — kompakt özel format;
  SerializedType.Build özyinelemeli tipte sonsuz döngüye girer, dikkat).
- Menü: [MenuItem] metod VEYA EditorWindow SINIFINA (class-attr → GetWindow(type)). Native sağ-tık:
  EditorMenu.ShowContext(("Create/Scene", act), ("-", null)...) — senkron TrackPopupMenu (shim:
  de_menu_create_popup/show_context/destroy). Project paneli Create menüsü App.AssetTypes'tan
  (engine+game assembly [Serializable] taraması, RebuildCatalog'da tazelenir) otomatik.
- Layout diske: GuiDock.SaveLayout/LoadLayout (panel Title bazlı; çıkışta kaydet, açılışta yükle,
  bozuksa default ağaç). Panel Title değişirse eski layout çözülmez → default'a düşer.
- Açılışta sahne AÇILMAZ; EditorSettings.lastScene varsa o açılır. App.OpenScene tek boğaz.
- Inspector .asset düzenler (DrawAssetInspector: şemadan, her değişiklik dosyaya, v1 undo'suz).
- Editör 1600x900; Project panelinde seçili asset mavi vurgu; hiyerarşi çift tık → SceneView.FocusOn.

## OTURUM 2026-09-11 (devam): stabilite + reparent — KULLANICI DOĞRULADI
- IMGUI ID-KAYMASI dersi (hayalet override bug'ı): Inspector'da Overrides panel satırları
  Transform'dan ÖNCE çizilir; drag sırasında satır sayısı değişirse sıralı control-ID'ler kayar,
  basılı drag başka DragFloat'a akar (pos/rot hayalet override, "editlenemez alan", revert dağılması).
  ÇÖZÜM: InspectorPanel.Overrides() GuiUtility.HotControl != 0 iken cache'i DONDURUR.
  GENEL KURAL: hot control varken kontrol sayısını değiştiren HİÇBİR layout değişikliği yapma.
- Reparent world-korumalı (Unity gibi): EditorScene.WorldTrs/LocalTrs (doc zinciri, 2D compose:
  pos=pp+Rz(prot)*(ps⊙lp), rot toplanır, scale bileşen çarpımı; non-uniform+rot yaklaşık).
  HEM MoveGameObject (aynı parent'ta reorder'da dokunmaz) HEM Reparent (hiyerarşi "Into" yolu —
  DİKKAT: iki ayrı fonksiyon!) dönüştürür; MoveGoOp/ReparentOp Old/New TRS taşır (undo tam).
- Scene view'a prefab drop imleç dünya konumuna instantiate ✓ (zaten çalışıyordu).

## OVERRIDES PANELİ (Unity modeli) + INT/REVERT FIX — TAMAMLANDI (2026-09-11, doğrulandı)
- Per-field R/A butonları KALDIRILDI (kullanıcı istemedi); Inspector tepesinde "Prefab: <ad> | Overrides (N) +/-"
  başlığı: aç → satır başına etiket + Revert + Apply (canApply ise). InspectorPanel.DrawPrefabHeader/TryDescribe;
  Overrides() frame-cache'li BuildOverrides (SceneDoc.Prefab.cs public). Alanlarda yalnız mavi şerit kaldı.
  Başlıktaki yol yalnız DOSYA ADI (uzun yol butonla çakışıyordu — DrawTextIn kırpmaz).
- CRASH FIX: prefab prop'u serileştirmemişse (default) RemappedPrefabValue null → revert "" yazıp
  int.Parse("") patlıyordu. Çözüm: SetProp/WriteProp value=null → prop node SİLİNİR + RefreshLive
  (gerçek default'a dönüş); PropOp null Old/NewValue taşıyabilir (undo/redo çalışır ✓ doğrulandı).
  Ayrıca SerializedType.Parse savunmalı: Float/Int TryParse→0, Enum TryParse→0. 
- Gui.DragInt eklendi (DragCore format parametreli; DragFloat "0.00", DragInt "0"). Int alanlar artık
  tam sayı gösterir/düzenler. ✓ SortingOrder drag→5, revert→0, undo→5 doğrulandı, crash yok.
- Pencere 1600x900 açılıyor (EditorApp CreateWindow); dock oranları ölçekleniyor.
- DİKKAT: Camera.cs bir ara yanlışlıkla editor/'a taşınmıştı (muhtemelen VS Code'da kazara sürükleme)
  → engine derlenmez. Doğru yeri engine/managed/Camera.cs.

## PREFAB AŞAMA 3: OVERRIDE GÖSTERGESİ + REVERT/APPLY — TAMAMLANDI (2026-09-11, doğrulandı)
- Inspector: her component için prefab kaynağı çözülür (TryGetPrefabSource + CompAddressOf +
  FindCompByAddress). Alan override'lıysa (RemappedPrefabValue ile DocNode.Equal kıyası):
  MAVİ ŞERİT (sol) + değer kutusu daraltılıp R(evert)/A(pply) butonları (yalnız scalar-ish alanlar;
  List/Object yalnız şerit).
- EditorScene.RevertField: prefab değerine SetProp (normal undo'lu) ✓ doğrulandı (136→110, şerit gitti).
- EditorScene.ApplyFieldToPrefab: sahne değeri TERS remap'le (InvertMap) prefab uzayına → prefab doc'una
  yaz → prefab DOSYASI kaydedilir (LoadPrefab cache'i aynı nesne) → diğer instance'lara YAYILIM:
  alanın eski prefab değerinde kalanlara (override'sızlara) yeni değer (RemapAcross: rootRec→prefab→r
  uzayı), override'ı olan korunur → RefreshLive. v1 UNDO'SUZ (prefab dosyası değişiyor; not).
  ✓ doğrulandı: A → orb.prefab.yaml Width: 136.37 + revert'lenmiş instance'a yayıldı (şeritsiz 136.37).
- SceneDoc.Prefab.cs public editör yardımcıları: PrefabRootOf, TryGetPrefabSource, CompAddressOf,
  FindCompByAddress, RemappedPrefabValue, InvertMap, RemapScalar.
- Kozmetik borç: DragFloat değer metni daraltılmış kutuda R butonuyla hafif çakışıyor.
- KALAN: prefab çocuğu silme→PrefabRemoved; transform override göstergesi (child node'lar; şu an yalnız
  comp props); comp ekleme/silme diff'i; prefab edit modu; nested v2; Apply undo.

## PREFAB AŞAMA 1+2 — TAMAMLANDI (2026-09-11, round-trip doğrulandı)
- MODEL: "diskte delta, bellekte expanded". Editör/motor prefab BİLMEZ — bellekte sıradan GoDoc'lar;
  tüm mevcut editör özellikleri (inspector/gizmo/undo/çoklu seçim) prefab içinde bedava çalışır.
- Format (scene.yaml instance kaydı): id/parent/name/pos/rot/scale (kök instance'a ait) + prefab: guid
  + prefabIds: "localId:sceneId ..." (KALICI eşleme — GoRef/seçim/undo kararlı) + removed: "3 5"
  + overrides: [{node(localId), comp("Tip:idx"), prop, value(DocNode)}].
- engine/managed/Serialization/SceneDoc.Prefab.cs (SceneDoc partial'ı):
  * ExpandPrefabs/ExpandInstance: prefab doc klonlanır (DocNode.Clone), id eşlenir (yeni prefab
    çocuğu yeni sceneId alıp map'e girer = otomatik yayılma), prefab-içi Go/CompRef'ler sahne
    id'lerine REMAP (şema üzerinden, list elemanları dahil), kök kaydıyla birleşir (kök transform/ad
    instance'ındır), override'lar uygulanır, removed (ata zinciriyle) atlanır. Missing prefab → delta korunur.
  * WritePrefabRecord/DiffComponents (collapse): expand-node'lar diske yazılmaz; her alan prefab'ın
    REMAP EDİLMİŞ klonuyla karşılaştırılır (DocNode.Equal), farklıysa override satırı. Kök transform
    diff'e girmez. Comp eşleme Tip+occurrence.
- DocNode.Clone + DocNode.Equal (Yaml.cs) eklendi.
- GoDoc yeni alanlar: PrefabGuid/PrefabLocalId/PrefabRootId/PrefabIds/PrefabRemoved/PendingOverrides;
  IsPrefabRoot/IsPrefabChild. ToYaml(catalog, assets) = collapse'lı; ToYaml() eski davranış (prefab kaydı).
- EditorScene: Load→ExpandPrefabs; Save→ToYaml(catalog,assets); InstantiatePrefab(guid,parent)
  (UniqueName + PrefabAddOp undo: Remove/ReaddPrefabInstance). UniqueName "ad (n)" AddGameObject'te de.
- AssetDatabase.ImportTypeOf bileşik uzantı destekli (".prefab.yaml"→Prefab); hiyerarşi Repaint'te
  prefab-asset drop hedefi olarak kendini kaydeder (RegisterTarget → InstantiatePrefab).
- DOĞRULANDI: Proje'den hiyerarşiye sürükle→instance (orb (1)/(2) benzersiz ad); Width drag → Save →
  yaml'da delta+overrides (node/comp/prop/value); Reload → expand + override korunmuş (112.93), 
  texture/renk prefab'dan.
- KALAN (Aşama 3+): Inspector override GÖSTERGESİ (mavi şerit) + Revert/Apply; prefab çocuğu silme →
  PrefabRemoved entegrasyonu (şu an RemoveGameObject prefab-farkında değil); comp ekleme/silme diff'i
  (v1 kapsam dışı); prefab edit modu; nested prefab v2. Scene view'a prefab drop (hiyerarşi var).

## RENAME + ASSET BROWSER + GENERIC DRAG-DROP — TAMAMLANDI (2026-09-11, uçtan uca doğrulandı)
- GO RENAME: Inspector başlığı textfield (buffer _nameForId ile seçim değişiminde tazelenir,
  yazdıkça EditorScene.SetName → NameOp coalesce'li undo, doc+canlı go.name). Hiyerarşiye anında yansır ✓.
- GENERIC DRAG-DROP (kullanıcı ilkesi: HİÇBİR ŞEY tip-özel/hardcoded olmayacak):
  * DragDrop (editor/DragDrop.cs): payload tip-bağımsız (Asset{guid,path} | SceneObject{docId}).
    KABUL kararı tamamen ŞEMADAN: Accepts(FieldSchema) — asset: Kind+AssetDatabase.IsAssignable
    (uzantı→tip importer haritası, RegisterImporter ile genişler); GoRef: her GO; CompRef: hedef
    GO'da alan tipinden component var mı (GetComponentOfType). DocValue(f) doc temsilini üretir
    (SceneDoc ref formatlarıyla birebir: guid / "id" / "id:Tip:idx").
  * KAYIT-TABANLI DROP: hover'daki kabul eden alan Repaint'te DragDrop.RegisterTarget(apply) ile
    kendini kaydeder; EditorApp frame sonunda mouse bırakıldıysa uygular (EndFrame). Panel çizim
    sırası / event tüketimi (hot-filter MouseUp) sorunlarını KÖKTEN çözer. Hayalet: DrawGhost DrawUi'de.
  * Inspector'da Texture/GoRef/CompRef TEK generic ref-editör case'i (kutu + RefDisplay okunur metin +
    hover yeşil vurgu). Yeni asset türü = sadece RegisterImporter; yeni ref kind = Accepts/DocValue'da eşleme.
- EditorScene.WriteProp: Go/CompRef yazımında RefreshLive (canlıya patch localId haritası ister);
  undo/redo aynı boğazdan → tutarlı.
- ProjectPanel ("Proje", alt şerit): AssetDatabase.AllAssets (path→guid) üzerinden klasör gezinme
  (^ üst, Yenile=ScanMetas), satır cache dirty-tabanlı, dosya sürükle → BeginAsset.
- Hiyerarşi sürüklemesi artık DragDrop.BeginSceneObject da yapar (reparent + alan bağlama aynı jest);
  reparent yalnız bırakma hiyerarşinin görünür alanındaysa.
- Engine ekleri: GameObject.GetComponentOfType(Type); AssetDatabase.AllAssets/IsAssignable/RegisterImporter/ImportTypeOf.
- DOĞRULANDI: rename→hiyerarşi ✓; hiyerarşiden GO → Spinner.Target(CompRef) "orbx (SpriteRenderer:0)" ✓;
  Proje panelinden orb.png → SpriteRenderer.Texture "Textures/orb.png" ✓.
- SIRADA: PREFAB + PREFAB OVERRIDE (asset browser + DnD temeli hazır; prefab satırı sahneye/hiyerarşiye
  sürüklenerek instantiate + doc'ta prefab referansı & override listesi tasarımı gerekiyor).

## ÇOKLU SEÇİM + DRAG-DROP REPARENT + SIRALAMA — TAMAMLANDI (2026-09-11, doğrulandı)
- Selection: List<int> tabanlı; DocId = BİRİNCİL (son seçilen; get/set eski tek-seçim kodlarıyla uyumlu).
  Toggle/Add/Remove/Clear/Contains/Ids/Count. Ctrl+tık = toggle (hiyerarşi + scene view hit-test).
- HierarchyPanel: satır rect'leri her pass _rows listesine (alloc'suz reuse); panel-düzeyi tek drag
  kontrol id'si (HotControl claim); 5px eşik sonrası sürükleme; hayalet etiket "ad (+N)".
  Seçiliyken mousedown seçimi BOZMAZ (çoklu sürükleme başlasın), sürükleme olmadan bırakınca tek seçime düşer (Unity).
  Sil butonu çoklu siler. Çoklu sürüklemede parent zinciri seçili olan atlanır (alt-ağaç bütün taşınır).
- DROP BÖLGELERİ: satır üst %25 = ÖNÜNE sırala, alt %25 = ARKASINA, orta = ÇOCUĞU yap (yeşil dolgu /
  sarı çizgi feedback). Boş alan = köke (sona). Kendi alt-ağacına bırakma engelli (IsSameOrDescendant).
- EditorScene.MoveGameObject(g, newParent, insertBeforeId): parent + LİSTE SIRASI tek op (MoveGoOp,
  Sealed, index'ler eleman-çıkarılmış liste üzerinden; ApplyMoveOp undo/redo). Liste sırası = çizim +
  hiyerarşi sırası olduğundan kardeş sıralaması bu. Reparent (Into) eski op'la.
- SceneViewPanel: Ctrl+tık toggle; Move gizmosu TÜM seçimi taşır (_multiStart: MouseDown'da tüm
  seçilmişlerin start pozları; her obje kendi parent uzayında delta alır); braketler tüm seçim için
  (birincil parlak turuncu, ikincil soluk); Rotate/Scale yalnız birincil (v1).
- Doğrulama: kullanıcı reparent+çoklu seçimi elle doğruladı; sıralama otomasyonla: satır 1 → 3. satır
  arkasına drag → highlight 3. konuma ([A B C]→[B C A] ✓), Undo → 1. konuma geri ✓.
- tools/sendctrlclick.ps1 eklendi (keybd_event CTRL + click, DPI-aware).

## ROTATE + SCALE GİZMOLARI — TAMAMLANDI (2026-09-11)
- SceneViewPanel araç modları: Tool {Move, Rotate, Scale} — panel toolbar butonları (Tasi/Dondur/Olcek,
  aktif *'lı) + Unity W/E/R kısayolları (KeyboardControl==0 iken).
- Kontrol düzeni değişmedi: 3 gizmo id + 1 select id her pass koşulsuz; Rotate'te X/Y rect'leri default(0)
  (hiç isabet almaz), halka isabeti merkez kolunda özel RingHit (R=48, band ±9, panel-lokal MANTIKSAL uzay).
- ApplyDrag tek fonksiyon, hepsi kümülatif (start değerleri MouseDown'da: pos+rot+scale+merkez):
  * Move: eskisi (dünya delta → parent-lokal).
  * Rotate: atan2 açı farkı (derece), Shift = 15° snap; 2D'de tüm dönüşler z → parent'lı objede de
    lokal delta = ekran deltası. Doğrulandı: drag ile rotation -21.9° değişti.
  * Scale: X kolu = x*(1+dx/100), Y kolu = y*(1+dy/100), merkez = uniform (1+dx-dy), min 0.01 clamp.
- Çizim: Rotate = 36 noktalı mavi halka + mevcut açıyı gösteren sarı elmas işaretçi + merkez nokta;
  Scale = Move gövdesi ama KARE uçlar (Unity scale tutamacı); Move = elmas ok başları. Hepsi
  gölge-konturlu, hover/aktif renk durumları ortak (AxisColor). Cursor: Rotate=Hand, Scale/Move eksen=ResizeH/V, merkez=ResizeAll.
- Görsel doğrulama: Move gizmosu ✓, Rotate halkası+işaretçi ✓ (zoom'lu), rotate drag ✓.
  Scale drag'i test ortamına kullanıcı müdahil olduğu için otomasyonla bitirilemedi ama ekranda
  scale değerlerinin değiştiği görüldü (yol çalışıyor); kullanıcı elle doğrulayacak.
- Sandbox doc'unda test artıkları olabilir (scale 8.2/3.2 gibi) — kullanıcı undo/düzeltme yapabilir.

## ANINDA HR + PROFESYONEL GİZMO — TAMAMLANDI (2026-09-11, uçtan uca doğrulandı)
- Anında hot reload DOĞRULANDI: HR=Instant + Play + doc değişikliği (+GO ve Undo) → jest bitiminde
  "[play] hot reload" logu; Undo da tetikler (UndoStack.Version undo/redo'da da artar) ✓.
  Calistir/Hot Reload Modu menü öğesi eklendi (döngü + log; otomasyonda koordinatsız test için).
- Gizmo görsel yenileme (SceneViewPanel): eksen kolları gölge-konturlu 2px çizgi + DrawDiamond ok
  başları (X kırmızı sağa, Y yeşil aşağı — y-down dünya); merkez = koyu çerçeveli yarı saydam kare;
  seçim vurgusu = OBB köşelerinde L-braketler (Unity bounds stili, kutu merkezine bakar);
  durum renkleri: aktif sürükleme=sarı (255,222,60), hover=+50 parlak. _hotAxis/_hoverAxis alanları.
- Native cursor: hover/drag'de GuiCursorManager.Request — serbest=ResizeAll, X=ResizeH, Y=ResizeV
  (splitter'la aynı desen; GuiCursor enum'unda hazırdı).
- Gizmo isabet alanları görselden geniş (kol+ok başı kapsanır: AxisLen+12 x 12).
- OTOMASYON DERSİ: koordinat tabanlı tıklar tab-drag'i yanlışlıkla tetikleyip LAYOUT BOZABİLİR
  (dock state persist edilmediği için restart düzeltir). Kritik akış testlerinde MENÜ yolunu tercih et
  (sendmenu koordinatsız, deterministik); tık testlerini tek tek + screenshot arası yap.

## GERİ SADELEŞME: Scene|Game + HOT RELOAD SEÇENEĞİ — TAMAMLANDI (2026-09-11, doğrulandı)
- KULLANICI KARARI: Viewport birleşimi + Düzenle örtüsü ÇOK KARIŞIK bulundu → iptal.
  ViewportWindow.cs + örtü/blur (GameOutput'taki BlurHCam/OverlayCam/EncodeOverlay) SİLİNDİ.
  (Blur deseni gerekirse git geçmişinde/notlarda; GuiRenderer.DrawTextureBlended KALDI.)
- Kalan mimari (bu İYİ ve kalıcı):
  * AYRIK SAHNELER: EditorScene.LiveScene = doc projeksiyonu, ExternallyDriven=true, HİÇ simüle
    olmaz; EditorApp her frame elle editLive.Update(dt, simulate:false) (lifecycle-only).
    PlayMode.PlayScene = Play'de doc'tan doğan bağımsız oyun sahnesi (Scene.Active olur);
    Stop = sadece imha + Scene.SetActive(LiveScene) — RESTORE GEREKMEZ (edit hiç kirlenmez).
  * SceneViewPanel: durağan doc + pan/zoom/Fit/1:1 + tıkla-seç + taşıma gizmosu + oyun çerçevesi.
  * GameViewPanel: GameOutput RT letterbox (salt görüntü). GameOutput sade (blur yok).
  * Play → Game tab otomatik öne (GetWindow=FocusPanel), Stop → Scene tab öne. Doğrulandı.
  * EditorScene.Instantiate aktif sahneyi ÇALMAZ (play'de arka planda projeksiyon yenilenir);
    UnloadLive() kod-reload için (ALC boşalmadan tüm instance'lar ölmeli). ReloadCode önce PlayMode.Stop.
- HOT RELOAD SEÇENEĞİ: PlayMode.HotReload = Off|OnSave|Instant (toolbar solda "HR: ..." döngü butonu,
  default OnSave). DoHotReload() = play sahnesi doc'tan yeniden doğar (akış kesilmez; play değilse no-op).
  * OnSave: Dosya/Sahneyi Kaydet → Save + DoHotReload.
  * Instant: UndoStack.Version (Push/Undo/Redo'da ++) + jest bitişinde (HotControl==0) versiyon
    değiştiyse DoHotReload — drag ORTASINDA her frame reload olmaz, bırakınca bir kez.
- Doğrulanan akış: edit donuk → Play (Game tab, akış) → Stop (Scene tab, donuk doc + kontur).
- DERS: iki büyük UX iterasyonu (birleşik viewport, örtü modu) yazılıp geri alındı — kullanıcı
  sadeliği seçti. Büyük UX değişikliklerinde önce küçük dikey dilim göstermek daha ucuz olurdu.

## SEÇİM + TAŞIMA GİZMOSU — TAMAMLANDI (2026-09-11, etkileşimli doğrulandı)
- ViewportWindow (editör kam): DoGizmoAndSelect — pan'den ÖNCE çağrılır (tıklama önceliği).
  Kontrol id'leri KOŞULSUZ alınır (seçim yokken de) — pass'ler arası kontrol sayısı sabit kuralı.
- Tıkla-seç: sol MouseDown → PanelToWorld → HitTest: doc sırasıyla tüm GoDoc'lar, es.Live(id),
  SpriteRenderer OBB testi (world matrix 2x2 inverse ile lokale çevir, |l|<=W/2) — doc sırası ~
  çizim sırası, SONUNCU kazanır (üstteki). Boş tık: Selection=0, event kullanılmaz → pan devralır.
  Doğrulandı: tık → hiyerarşi highlight + Inspector doldu + gizmo çizildi.
- Gizmo: merkez sarı kare (serbest) + kırmızı X + yeşil Y kolları (dünya-hizalı). Drag kümülatif
  (başlangıç pos + toplam delta — frame kaybı birikmez), dünya deltası parent-lokale çevrilir
  (WorldDeltaToParentLocal, parent world 2x2 inverse), EditorScene.SetTransform (doc+canlı+undo
  coalesced). Seçim çerçevesi: OBB 4 köşesi turuncu işaret. Layer 3 (RT 1'de). Drag doğrulandı.
- ViewportWindow artık `unsafe` (Mat4.m fixed buffer erişimi).
- NOT/BORÇ: Play sırasında gizmo drag DOC'A yazar (kalıcı) — Unity'de play değişiklikleri runtime-only.
  İleride play'de canlı-only moda alınmalı.
- TEST VERİSİ TUZAĞI: Projects/Sandbox Main.scene.yaml'da ESKİ oturumlardan birikmiş Spinner rot'ları
  (binlerce derece) ve runtime pozisyon kümeleri var — "sim-sürekli-koşan" eski mimaride Kaydet
  yapılmıştı. Görsel regresyon karşılaştırmalarında doc'un GERÇEK içeriğini baz al; şüphede yaml'ı oku.
  (Temiz test istenirse doc'taki rot'ları sıfırlayıp pozisyonları düzgün dağıtmak iyi olur.)
- UI otomasyonu ortak makinede: pencere boyutu/odak kullanıcı tarafından değişebilir — uzun test
  serilerinde her adımda screenshot.ps1 ile pencereyi yeniden konumla; tutarsızlıkta ortamı sorgula.

## VIEWPORT BİRLEŞİMİ + PLAY/PAUSE/STOP — TAMAMLANDI (2026-09-11, etkileşimli doğrulandı)
- KARAR: Scene/Game ayrı panel DEĞİL; tek ViewportWindow (Unreal modeli) — üstte kamera seçici
  (Editör|Oyun). Editör kam: pan/zoom/kontur/(ileride gizmo), tam yetki. Oyun kam: paylaşılan
  GameOutput RT'si letterbox'la, düzenleme yok. Pencere/Yeni Viewport ile ÇOKLU instance
  (EditorWindow.CreateWindow<T> eklendi, tip-tekil GetWindow'dan ayrı) → biri Editör biri Oyun
  bırakılarak Unity Scene|Game yan yana akışı geri gelir. İkinci viewport bağımsız kamera ✓.
- editor/GameOutput.cs: statik paylaşılan oyun RT'si; frame'in İLK oyun-kameralı viewport'u boyutu
  belirler (RequestSize, _sizedThisFrame). ViewW/H = oyunun Screen.width/height.
- editor/ViewportWindow.cs: All registry (statik liste), EditorCam (Camera field), kendi RT'si
  (BeginFrame emekli-RT ölümü), EditorCamVisible. EditorApp kamera listesini her frame kurar:
  sceneCam(0) + görünür editör-viewport kamları + mainCam(9).
- editor/PlayMode.cs: Editing/Playing/Paused. Play = EditScene.Instantiate (bellekteki doc'tan,
  kaydedilmemiş dahil), Stop = yine Instantiate (doc restore, play mutasyonları kaybolur = Unity),
  Pause toggle. Tek viewport varsa Play'de otomatik Oyun kamerasına geçer, Stop'ta döner.
- ENGINE: Scene.Update/UpdateAll(dt, simulate=true) — simulate=false: SceneTime/deltaTime donar,
  Update/Tween/LateUpdate atlanır, AMA Start/destroy/compaction koşar (editör oplarına lazım).
  Edit modu artık Unity gibi donuk; Play'de akar. Görsel doğrulandı (orb'lar edit'te sabit blob).
- Toolbar: DrawUi'de dock'tan önce, DockSpace y=ToolbarH(26)'dan başlar. Play*/Pause*/Stop.
- ★ BÜYÜK TUZAK ÇÖZÜLDÜ: tools/sendclick|sendwheel|senddrag DPI-UNAWARE idi → %150 monitörde
  SetCursorPos/mouse_event koordinatları 1.5x sanallaşıp yanlış yere tıklıyordu (menü/GLFW MASUM,
  saatler kaybettirdi). Hepsine SetProcessDPIAware eklendi. KURAL: input simülasyon aracı yazarken
  HER ZAMAN DPI-aware yap. GLFW ham imleç = mantıksal*scale gibi görünür ama GuiHost /scale yapar → doğru.
- Kozmetik borç: dar leaf'te tab bar taşması (komşu leaf'in tab barına biniyor — clip yok);
  menü çubuğu sırası ordinal (Calistir Dosya Duzen...) — üst menü sırası kontrolü yok.
- Menü id'leri sort'a göre kayar: sendmenu -Id N kullanırken sıralamayı hesaba kat (order,path ordinal).

## SCENE VIEW — TAMAMLANDI (2026-09-11, etkileşimli doğrulandı)
- editor/RenderViewWindow.cs: RT'li view tabanı (EditorWindow) — RT fiziksel boyut izler,
  emekli RT BeginFrame'de ölür, VisibleLastFrame (gizliyken sahne o view için encode edilmez),
  OnGui sealed (RT+DrawTexture), alt sınıf OnViewGui(vis) override eder. GameViewPanel artık 10 satır.
- editor/SceneViewPanel.cs: editör kamerası — _center+_zoom → Mat4.Ortho(cx±halfW, cy±halfH) (y aşağı).
  Pan: her tuş drag (HotControl; seçim aracı gelince sol ayrışacak). Zoom: wheel, İMLEÇ MERKEZLİ
  (before/after PanelToWorld farkı center'a eklenir). Toolbar 1:1 + Fit (oyun çerçevesine odak).
  DrawGameFrame: oyun ekranı (0,0,gameW,gameH) mavi kontur — layerOffset=2 (RT dokusu 1'de, altında kalmasın!).
- EditorApp: sceneViewCam (Order 1, oyun 0, GUI 2); _sceneView.VisibleLastFrame ise Scene.Active.Render
  ikinci kez sceneViewCam.Queue'ya (retained katman sayesinde aynı frame iki kamera ✓). Target'lar encode öncesi.
- Dock: orta leaf = {Scene, Game} tab ikilisi (Unity gibi). Pencere/Scene menüsü eklendi.
- KONTROL SIRASI DERSİ: pan kontrolü toolbar butonlarından ÖNCE GetControlID alırsa tüm tıkları yutar —
  butonlar önce çizilir/işlenir, pan sonra (tıklama önceliği = kontrol sırası).
- OTOMASYON ARAÇLARI (tools/): sendmenu.ps1 (WM_COMMAND), sendwheel.ps1 (WM_MOUSEWHEEL, imleç konumlu),
  sendclick.ps1 (SetCursorPos+mouse_event), senddrag.ps1 (sol tuş adım adım drag). HEPSİ pencere (0,0)'dayken
  çalışır → ÖNCE screenshot.ps1 çağır (pencereyi taşır), SONRA input gönder. Koordinat: fiziksel ekran px;
  client offset ≈ (9, ~91: başlık+menü %150'de). Bu setle GUI etkileşimleri otonom test edilebiliyor.
- Fit/wheel-zoom/pan/çerçeve görsel olarak doğrulandı (screenshot döngüsü).

## EDITORWINDOW + NATIVE MENÜ — TAMAMLANDI (2026-09-11, uçtan uca doğrulandı)
- Unity modeli: editor/EditorWindow.cs — abstract EditorWindow {Title, PanelId, OnGui()},
  GetWindow<T>() tip başına tek instance havuzu; yeni pencere GuiDock.RegisterPanel + FocusPanel
  (dock'suzsa ilk leaf'e pending move ile dock'lanır). Editör çekirdeği pencere içeriği BİLMEZ.
- GuiDock.FocusPanel(panelId) eklendi (pending üzerinden: aktif tab yap / ilk leaf'e taşı;
  detached ise no-op). MoveNow dock'suz paneli de ekleyebiliyor (RemoveFromTree -1 dönse de).
- Paneller EditorWindow'a çevrildi: HierarchyPanel/InspectorPanel/ClipPanel/TimelinePanel/
  RenderDebugPanel/GameViewPanel + App.ControlsWindow (nested; App private statiklerine erişir).
  Draw(int panelId) → protected override void OnGui(); Title ctor'da.
- NATIVE MENÜ (Win32): sokol_shim.c sonunda de_menu_create_bar/add_popup/add_item(id+1000)/
  add_separator/attach(glfwWindow,bar)/poll. GLFW wndproc SetWindowLongPtrW ile subclass,
  WM_COMMAND (HIWORD=0) ring buffer'a; extern glfwGetWin32Window (aynı DLL, statik link).
  Menü modal loop'u render'ı bloklar (bilinen GLFW+menü kısıtı, kabul edildi).
- [MenuItem("Dosya/Sahneyi Kaydet", order)] attribute (editor/EditorMenu.cs): static parametresiz
  metotlar assembly taramasıyla bulunur, '/' yolu iç içe popup kurar, EditorMenu.Build(window, asms)
  + loop başında EditorMenu.Poll() dispatch. editor/BuiltinMenus.cs: Dosya/Duzen/Proje/Pencere.
  Oyun editör-assembly'si gelince Build'e eklenecek.
- TEST: tools/sendmenu.ps1 (PostMessage WM_COMMAND 1000+id) ile menü komutu programatik tetiklendi,
  Sahneyi Kaydet dosya mtime'ını değiştirdi ✓. Menü barı görsel olarak doğrulandı ✓.
- DİKKAT: sceneCam kurulumunda _gameView henüz null (GetWindow sonra) → Target frame döngüsünde bağlanır.

## GERÇEK EDİTÖR ADIM 1 — TAMAMLANDI (2026-09-11, görsel doğrulandı)
- EditorApp demo tamamen temizlendi: blur zinciri, Liste/Ekstra panelleri, Isim textfield,
  Model2D, MakeBlurShader SİLİNDİ. Kontroller panelinde yalnız gerçek editör butonları kaldı.
- editor/GameViewPanel.cs: oyun kendi RT'sine (sceneCam.Target), panel RT'yi DrawTexture ile
  gösterir. RT panelin FİZİKSEL boyutunu izler (HiDPI'da tam çözünürlük). Resize deseni:
  eski RT _retired'a alınır, BeginFrame() (frame başı) yok eder — frame ortası Destroy YASAK;
  sceneCam.Target ataması encode'dan HEMEN ÖNCE yapılır (GUI sırasında resize olabilir).
- Scene.UpdateAll(dt, gameView.ViewW, ViewH): oyunun Screen'i artık Game View boyutu.
- Dock TAM PENCERE: root Split(0.2){sol Leaf(Hiyerarsi,Kontroller), Split(0.74){
  Split(dikey 0.68){Leaf(Game), Leaf(Clip,Timeline,FrameDbg)}, Leaf(Inspector)}}.
  GuiDock derin ağacı destekliyor; GuiDock.DebugDump() (internal) teşhis için eklendi.
- İKİ SAHTE ALARM (ders!):
  1) "Sprite'lar bozuk" → pipeline değil, Projects/Sandbox/Assets/Textures/orb.png DOSYASI
     bozuktu (üzerine timer/renk barı görüntüsü yazılmış). view_image ile asset'e BAKMAK
     kök nedeni buldu. tools/make_orb.ps1 ile yeniden üretildi (meta/GUID korundu).
     Motor baştan beri doğru render ediyormuş; eski blur pass'i bozuk dokuyu gizliyordu.
  2) "Inspector/alt şerit kayıp" → layout doğruydu; tools/screenshot.ps1 DPI-aware değildi
     (%150 monitör → pencerenin 2/3'ü büyütülmüş yakalanıyordu). SetProcessDPIAware eklendi.
- tools/screenshot.ps1: DPI-aware + pencereyi (0,0)'a taşır + topmost yap-çık (önde kesin).
  tools/zoom.ps1: -Src -CropX -CropY -CropW -CropH -Zoom -Out (nearest büyütme).
  DİKKAT: dış PowerShell komut satırında $ yutulur — script'leri Set-Content here-string ile yaz.
- Font raster yolu %150 HiDPI'da da cam gibi doğrulandı (PhysPx logical*1.5 → tam piksel).
- Sonraki adımlar: Scene View (editör kamerası ile ikinci render), Inspector boşlukları
  (Color/String editörleri, asset picker), oyun input'unun Game panele hapsi, Play/Pause.

## FONT: UI raster yolu ÇÖZÜLDÜ (2026-09-11, görsel doğrulandı — cam gibi)
- Sorun: 32px SDF glyph 11-13px'e minify = çamur (mip yok, fwidth smoothstep geniş).
- Çözüm (fontstash modeli): UI metni GERÇEK fiziksel piksel boyutunda rasterize + dinamik atlas.
  * native sokol_shim.c: de_font_glyph (stbtt tek codepoint bitmap + metrics), de_font_vmetrics.
  * GuiFont: _ttf saklanır; RGlyph cache Dictionary<long(physPx<<32|cp)>; 1024² R8 dinamik atlas
    (Texture.CreateDynamicAlpha + UpdateAlpha), shelf packer, dolunca reset; FlushRasterAtlas()
    frame'de BİR KEZ (sokol dinamik image kuralı) — EditorApp/App.Editor loop'ta GuiDock.UpdateWindows
    sonrası cb.Begin öncesi çağrılır. PhysPx(logical)=round(logical*Gui.Scale). RasterVMetrics cache.
  * GuiRenderer.DrawText: raster yol — snap GLOBAL fiziksel uzayda (GuiClip.Unclip(default) ofseti ile),
    tamsayı penX + round(advance) ilerleme; _rasterFragment düz alpha shader (SDF smoothstep YOK),
    premultiplied One/OneMinusSrc. SDF shader/FontMaterial DURUYOR (dünya/ölçekli metin için).
  * TextSize + GuiTextField.IndexFromX aynı rounded raster advencelarla (çizimle birebir; kerning UI'da yok, ImGui gibi).
  * Codepoint bazlı on-demand → Türkçe karakterler bedava geldi (eski 32..255 sınırı raster yolda yok).
- Otonom görsel test döngüsü: tools/screenshot.ps1 <out.png> (DigitoyEditor penceresini yakalar) +
  tools/zoom.ps1 -Src -CropX -CropY -CropW -CropH -Zoom -Out (nearest büyütme) → view_image ile incele.
- App.Editor.cs demo ÇÜRÜMÜŞTÜ (TimelinePanel/RenderDebugPanel editöre taşınıp silinmiş; API drift)
  → tüm dosya #if DE_DEMO_APP guard'ında (derlenmiyor, referans olarak duruyor).
- TUZAK: editörde açık stale buffer'lar grep/read'de görünür ama diskte olmayabilir (TimelinePanel.cs öyleydi) — şüphede Test-Path.

## Yapı
- engine/managed: C# .NET 9 renderer beyni (DigitoyEngine.csproj, entry: App.Editor.cs Main)
- engine/native: sokol_shim.c (FFI + RenderExecute decoder), engine.c = SADECE REFERANS (eski arge, projede kullanılmıyor)

## Build & çalıştırma
- Native: engine\native> .\build_editor.cmd → build\digitoyengine_native.dll (clang, GLFW statik)
- Managed: engine\managed> dotnet run (csproj DLL'i exe yanına kopyalar)
- GLFW.cs ve Sokol.cs Lib = "digitoyengine_native" (eski langtest yolu düzeltildi)

## Render zinciri (ÇALIŞIYOR, doğrulandı 2026-09-10)
- Frame: queue.Begin → DrawMesh → cb.Begin → cb.BeginPass → queue.Flush(cb, ref viewProj) → cb.EndPass → cb.Submit → Sokol.Commit → SwapBuffers
- CommandBuffer.Submit() → de_sokol_render_execute (sokol_shim.c sonunda): opcode decoder, RenderCmd enum ile birebir; memcpy ile hizasız okuma
- Frame instance buffer decoder'a ait (stream vbuf, min 256KB, 2x büyür); SetBindings instanceBuffer=0 sentinel → bu buffer + son UploadInstances blok tabanı
- sg_append_buffer pass İÇİNDE serbest (sokol dok. doğrulandı); kısıt: aynı buffer'a aynı frame update+append karışmaz
- GL 4.1 core context hint'leri şart (sokol GLCORE); Mat4.Ortho eklendi, piksel koordinat sol-üst orijin
- SortMode Unity gibi MATERIAL'da (Material.SortMode, default Transparent — default blend alpha). DrawMesh'te sadece layer (=sortingOrder). Enum top-level RenderEnums.cs'de; sort key'in 60. bitlerinde kova → kovalar sırayla çizilir
- Camera.cs: pass'in SAHİBİ (Unity Camera gibi). Order (=Camera.depth), Target (null=swapchain / Texture=RT), Clear*, ViewProj, kamera-başına RenderQueue (cullingMask yerine explicit kuyruk). Camera.EncodeAll(list, cb, fbw, fbh). SetPixelOrtho helper
- Offscreen pass: opcode 11 BeginPassOffscreen (colorView+depthView), shim de_sokol_begin_pass_offscreen. Texture.CreateRenderTarget(w,h,depth) → color image usage=attachment + textureView + colorAttachmentView (+depth). RT önce yaz sonra başka pass'te doku oku = serbest (aynı pass'te yasak)
- Sampler'lar RenderQueue'da static lazy (LinearSampler/NearestSampler internal static)
- RT demo doğrulandı: rtCam(Order=-1)→256x256 RT, mainCam ortada RT'li quad
- Blur dialog senaryosu DOĞRULANDI (4 pass): sahne→yarım çözünürlük RT → H-blur→rtPing → V-blur→rtScene (ping-pong) → ekrana fullscreen + net dialog. Blur = 5-tap separable gaussian, texel ofsetleri shader string'ine bake (fragment DSL'de uniform yok henüz). Blit materyalleri One/Zero blend + SortMode.Opaque
- DİKKAT: PipelineCache pipeline'ları default depth formatıyla kurar → RT'ler depth'li yaratılmalı (depth=false pass'te format uyuşmazlığı olur)
- Eski demo çalışırken build MSB3026 verir (exe kilitli) → Stop-Process ile kapat

## Eksik / sonraki adımlar
- PLAN: Render debug altyapısı (KARARLAŞTIRILDI, sonra dönülecek):
  1. de_sokol_render_execute'a maxDraws limiti (frame debugger "ilk N draw" replay)
  2. Son frame akışını sakla + C# stream walker: istatistik (draw/instance/pipeline switch/upload bytes), disassembly listesi, batch-break sebebi (Flush zaten biliyor)
  3. "Varsayılan hedef" dolaylaması: Target=null → editörde game-view RT, standalone'da swapchain (Unity Game View modeli)
  4. Scene View = GameObject retained katmanıyla birlikte (immediate kuyruk başka açıdan re-render edemez)
  5. Sonra: pass-başına GPU timer (GL query shim), RenderDoc tetikleyici. Draw-başına GPU süresi TBDR mobilde yanıltıcı — pass granülaritesi esas
- GUI ÇEKİRDEĞİ YAZILDI ve ÇALIŞIYOR (engine/managed/Gui/): Rect, Event (tek instance mutate, GetTypeForControl hot/kbd filtresi), GuiUtility (sayaç+hint ID, GuiState<T> open-addressing struct havuzu), GuiClip (offset+physical scissor, mouse lokal uzaya çevrilir), GuiLayout/GuiLayoutUtility (two-pass CalcW→SetH→CalcH→SetV, margin çakıştırma, struct havuz + int-index ağaç, replay cursor, BeginArea ayrı köklü ağaçlar), GuiStyle/GuiSkin (8-state, koyu tema), GuiRenderer (Unclip+clip kesişimi→RenderQueue.DrawMesh SortMode.Ui; radix sort STABLE → aynı layer'da submit sırası korunur), Gui (Button/Toggle/Box/HSlider/VScrollbar/BeginScrollView/Window drag'li), GuiHost (GLFW polling→Layout→input→Repaint).
  * ÖĞRENİLEN BUG: çocuk ExpandWidth stretch'i Width(240) option'lı gruba sızıyordu → FixedWOpt/FixedHOpt bayrağı stretch propagasyonunu keser (Unity fixedWidth davranışı).
  * Kontrol sırası korunumu: ScrollView bar görünmese de dummy GetControlID tüketir.
  * Scroll wheel YOK (GLFW polling'de yok, callback gerekir). Text render YOK (sonraki faz, SDF).
- NATIVE MULTIWINDOW EKLENDİ (eski engine_host_glfw.c deseninin C# portu — kullanıcı dosyayı old_engine_readonly'ye ekledi, desen oradan):
  * SOKOL TEK CONTEXT'TE KALIR (ana pencere). İkincil pencere = glfwCreateWindow(share=main).
  * Ana context'te paylaşılan renderbuffer'lı FBO (mainFb) → sokol pass'i Camera.Framebuffer=mainFb ile oraya çizer.
  * Pencere kendi context'inde AYNI renderbuffer'ı saran 2. FBO (winFb) → present'ta blit→default fb→swap→ana context'e dön→sg_reset_state_cache. (FBO container context-lokal, renderbuffer paylaşılır; MakeContextCurrent WGL'de implicit flush.)
  * C# dosyaları: NativeWindow.cs (GLExt: glfwGetProcAddress ile GL FBO/blit fn ptr'ları — delegate* unmanaged; NativeWindow: Open/UpdateSize(resize=rb yeniden kur)/Present/Destroy; her pencere kendi Camera+GuiHost taşır). Camera.Framebuffer alanı eklendi (Target=null iken ham GL fbo).
  * Frame akışı: ana kameralar + panel kameraları TEK cb.Submit + Commit; sonra her panel Present (blit).
  * Event'ler pencere-lokal: her NativeWindow kendi GuiHost'u (polling o pencerenin handle'ından).
  * NOT: GuiUtility.HotControl global (pencereler arası paylaşılıyor, eski C de öyleydi); GuiState id'leri pencereye göre salt'lanmıyor — docking'de pushId gerekecek.
  * Demo: ana pencerede "panel aç" butonu → detached native pencere, içinde kendi GUI'si; iki pencere aynı state'i sürebiliyor.
  * SONRAKİ: docking v2 (tab'ı başka leaf'e drag-drop, split-by-drop), scroll wheel (callback'li host), text (SDF).
- DOCKING v1 EKLENDİ (Gui/GuiDock.cs): sanal Gui.Window kullanılmıyor artık.
  * Model: Node struct havuzu (Leaf=tab listesi [ayrı düz int[] _tabs, fixed buffer erişim derdi olmasın diye] / SplitH / SplitV ratio'lu), RegisterPanel(title, PanelFunc), Leaf/Split/SetRoot builder.
  * KRİTİK KURAL: ağaç mutasyonları (detach/redock/aktif tab) PENDING'e yazılır, SONRAKİ frame'in Layout pass'i başında ApplyPending ile uygulanır — pass'ler arası kontrol sırası invariant'ı korunur. Splitter ratio anında değişebilir (kontrol sayısını etkilemez).
  * Tab drag → dock alanı DIŞINA bırak = detach (NativeWindow.Open, ekran pozisyonu GetWindowPos+globalMouse); pencere X = redock (HomeLeaf'e, yoksa ilk leaf'e). Boş leaf collapse: parent split yerine kardeş geçer.
  * Panel içerik func'ı hem dock leaf'inde hem detached pencerede AYNI (BeginArea lokal uzay). Detached çizim: cached static GuiFunc + _currentDetached index (closure alloc yok).
  * App akışı: guiHost.Frame(ana) → GuiDock.UpdateWindows() → encode ana kameralar + GuiDock.EncodeWindows(cb) → Submit+Commit → ana swap → GuiDock.PresentWindows().
  * Demo: sol 320px dock şeridi, SplitV(Kontroller+Ekstra / Liste), tab renkleri panel-hash (text yok).
  * v1.5 düzeltmeleri: splitter 8px + tutamaç çizgisi + 3px isabet toleransı (5px görünmüyordu/tıklanamıyordu); drag sırasında drop vurguları (tüm tab bar'lar soluk mavi, hover hedef parlak); REDOCK-BY-DRAG: tab başka leaf'in üzerine bırakılınca MoveNow ile taşınır (FindLeafAt), detach sadece dock alanı DIŞI; RemoveFromTree ortak helper (detach+move).
- CALLBACK'Lİ INPUT HOST (Gui/GuiInput.cs): GLFW.Set*Callback binding'leri (fn ptr IntPtr), [UnmanagedCallersOnly(Cdecl)] static callback'ler → pencere-başına event KUYRUĞU (9 slot × 32 struct, zero-alloc). Move/Drag ardışıksa coalesce; down/up/scroll/key asla ezilmez (eski C dersinin genellemesi). Double-click 0.35s+5px. GuiHost polling diff yerine kuyruğu drain eder: Layout → kuyruk sırayla → Repaint. NativeWindow.Open/Destroy → GuiInput.Attach/Detach; ana pencere App'te Attach.
  * Scroll wheel ÇALIŞIYOR: BeginScrollView imleç üstündeyken delta.y*40 + clamp + Use. Klavye/char event'leri kuyruğa giriyor (textfield hazırlığı; tüketen widget henüz yok).
- TEXT RENDER (SDF) EKLENDİ:
  * Native: de_font_bake (sokol_shim.c, STB_TRUETYPE_IMPLEMENTATION + engine/native/stb_truetype.h — kullanıcı kopyaladı). imgui_font.c'nin kanıtlanmış parametreleri: atlas 2048² tek kanal, hücre 128, SDF taban 32px, spread 21, pad 6, 224 glyph (cp 32..255), tam kerning int16 tablosu. Tek çağrı: caller-allocated buffer'lar (atlas, glyph 224×9 float, kern 224×224 short, metrics 5 float).
  * C#: GuiFont (Load/FromMemory, TextSize kerning'li, GlyphIndex kapsam dışı→'?'), Texture.FromAlpha (R8 image — shader .r okur, GL R8'de .a=1!). GuiRenderer: DrawClipped (UV-orantılı kırpma — glyph panel kenarında doğru kesilir), SDF shader (smoothstep+FWIDTH, premultiplied One/OneMinusSrcAlpha), DrawText/DrawTextIn (floor+0.5 piksel snap, atlas UV ±0.5px inset).
  * Widget'lar: Gui.Font/FontSize statik, Label/LayoutLabel, Button(text)/LayoutButton(text) (genişlik=text+padding), GuiDock tab başlıkları (13px).
  * SINIR: charset Latin-1 (cp 32-255) — Türkçe ş/ı/ğ YOK ('?' düşer); genişletme sonra. Font: segoeui.ttf → arial.ttf fallback, yoksa text çizilmez UI çalışır.
  * BUG ÇÖZÜLDÜ: text dikey ters çıkıyordu — quad UV düzeni GL RT'lere göre (satır 0 altta), CPU atlası satır 0 üstte; eski engine -height ile çiziyordu → DrawText'te V takası ile düzeltildi. AYNI SORUN gelecekte dosyadan sprite yüklemede çıkacak (Texture'a yönelim bayrağı gerekecek).
- HiDPI EKLENDİ: GLFW SCALE_TO_MONITOR hint; UI/oyun MANTIKSAL (point) uzayda, viewport fiziksel — kamera ortho'su logical (fb/scale) kurulur, SDF text ölçekte keskin. GuiHost.Frame(scale) mouse'u fiziksel→logical böler (scroll delta tik, ölçeklenmez). Pencere-başına GetWindowContentScale (monitörler farklı olabilir; GuiDock.UpdateWindows kendi scale'ini alır). Gui.Scale statik (detach ekran konumu logical→fiziksel çevirisi için). Camera.SetPixelOrtho artık float.
- TEXTFIELD + KEYBOARD FOCUS EKLENDİ (Gui/GuiTextField.cs, Gui partial oldu):
  * Metin ÇAĞIRANIN char[] buffer'ında (ref length, zero-alloc core); TextEditState (Caret/Anchor/ScrollX) GuiState havuzunda.
  * Tam davranış: tık=caret (IndexFromX glyph-ortası eşiği), sürükle=seçim, çift-tık=kelime, üç-tık=tümü, ok/Home/End (+Shift seçim, +Ctrl kelime), Backspace/Delete seçim-duyarlı (+Ctrl kelime sil), Ctrl+A/C/X/V (GLFW clipboard: SetClipboardString LPUTF8Str / GetClipboardStringPtr→PtrToStringUTF8), yatay auto-scroll (caret görünür), caret blink (GetTime%1<0.55), GuiClip.Push ile alan kırpma.
  * Focus: MouseDown içi → KeyboardControl=id; dışı → focus bırak. KeyDown/TextInput GetTypeForControl ile sadece focus'luya. Skin.TextField stili (Focused parlak).
  * Paste tek satır: kontrol karakterleri atlanır. Copy/Paste alloc yapar (yalnız o operasyonda, kabul).
  * UNDO YOK (v2 adayı).
- TIMELINE PANELİ (Gui/TimelinePanel.cs): Unity MovieClipTimelineWindow'un GUI portu — dock tab'ı olarak. Ruler scrub, zoom, snap, actions şeridi, track lane'leri (elmas keyframe), çift-tık ekleme, GRUP sürükleme (snap anchor'a, grup ≥0 clamp), rubber-band çoklu seçim (actions+lanes kapsar), header'lar (aktif toggle+isim TextField+up/down/sil), h/v scrollbar, resizable inspector (DragFloat time, ease cycle, çoklu shift/dup/del), Delete/Ctrl+D, Play/Loop. Yapısal mutasyonlar pending→Layout. Kapsam dışı: SerializedObject/undo, sahne preview, sağ-tık menü.
  * Yeni widget'lar: GuiRenderer.DrawDiamond (45° quad, kaba cull — rect-kırpılmaz), Gui.HorizontalScrollbar, Gui.DragFloat (TryFormat zero-alloc).
  * KRİTİK DÜZELTMELER (2 crash dersi):
    1. GuiHost artık UNITY MODELİ: HER event pass'inden önce ayrı Layout pass'i (frame başında tek Layout yetmez — input pass'i state değiştirince sonraki pass'in cache'i bayatlar).
    2. GuiLayoutUtility replay taşması artık NON-FATAL: pass ortasında yapı değişirse (MouseDown seçimi değiştirdi → aynı pass'in inspector'ı farklı dal) fazla kontroller DUMMY rect alır (_overrun sayacı, BeginArea/BeginGroup/GetRect/EndGroup/EndArea hepsi korumalı) — Unity kDummyRect davranışı. Sonraki pass taze Layout ile düzelir.
- NATIVE CURSOR DESTEĞİ (Gui/GuiCursor.cs): Unity AddCursorRect muadili istek modeli — widget Repaint'te hover/hot iken GuiCursorManager.Request(GuiCursor.X); GuiHost frame sonunda Apply(window) (glfwSetCursor; lazy glfwCreateStandardCursor cache). Son istek kazanır (sonra çizilen üstte = doğal z-önceliği). Bağlı: TextField→IBeam, dock splitter→ResizeH/V (yöne göre), timeline splitter→ResizeH, DragFloat→ResizeH. Pencere-başına uygulanır (her GuiHost.Frame kendi window'una).
- FRAME DEBUGGER TAMAMLANDI ve DOĞRULANDI (release maliyeti SIFIR):
  * DE_EDITOR define: csproj Debug config'de DefineConstants; RenderDebug.cs + RenderDebugPanel.cs tamamen #if DE_EDITOR, App entegrasyonu #if bloklarında. Release'e tek satır girmez.
  * Native: de_render_execute_core(maxDraws, overrideColorView/DepthView) — maxDraws>=0 ise N'inci draw'dan sonrası atlanır (pass/state işlenir, hedefler doğru); fbo==0 swapchain pass'leri override attachment'lara yönlenir (replay RT'ye). Normal yol maliyeti: draw başına 1 karşılaştırma. de_sokol_render_execute_dbg export'u.
  * RenderDebug: CaptureRequested → AfterSubmit'te akış unmanaged kopya + Walk (DrawEntry[]: pass/pipeline/vbo/tex/sampler/elems/inst + BreakReason: First/NewPass/Pipeline/Texture/Mesh/Sampler/InstanceRange; istatistik: draw/inst/pass/pipe/uniform+instance bytes). Frozen'ken ReplayIfFrozen Submit'ten ÖNCE koşar (replay pass'leri önce → aynı frame GUI RT'yi okur). ReplayTarget captured boyutta RT.
  * Panel: Capture/Canli, "ilk N draw" slider, tıklanabilir draw listesi (satır tık → MaxDraws=i+1), RT aspect-fit önizleme. GuiRenderer.DrawTexture eklendi (tek mutate materyal — TexView DrawMesh anında yakalanır, güvenli). CA2014: stackalloc döngü DIŞINA (ders).
  * Doğrulandı: 6 draw, sebepler doğru, önizlemede frozen frame (GUI dahil — özyinelemeli görüntü normal).
  * RECAPTURE CRASH FIX: Frozen'ken tekrar Capture → yakalanan stream ReplayTarget önizlemesini doku olarak içeriyordu; replay aynı RT'ye render ederken onu sample'lıyordu (+ boyut değişiminde destroy edilen stale view) → sokol validation crash. Fix: Capture butonu CaptureRequested=true + Unfreeze() — aynı frame'in Repaint'i önizleme çizmez, temiz canlı frame yakalanır.
- GAMEOBJECT/COMPONENT KATMANI EKLENDİ (GameObject.cs, Component.cs, Scene.cs, SpriteRenderer.cs, Time.cs):
  * Lifecycle Unity ile BİREBİR: Awake (GO ilk aktifleşince, enabled'dan bağımsız) → OnEnable → Start (ilk Update'ten önce) → Update → LateUpdate → OnDisable → OnDestroy. Destroy frame SONUNDA (FlushDestroyed). SetActive/enabled/SetParent aktiflik zinciri Unity semantiği.
  * HIZLI: new GameObject = 2 alloc (GO+Transform). Tip lifecycle bayrakları Component.TypeFlags<T> STATİK GENERIC cache (reflection tip başına 1 kez, AddComponent'te lookup yok). Boş sanal çağrı yok — yalnız override edenler dispatch listelerinde. Listeler düz Component[]; disable = O(1) null-delik (iterasyon güvenli), frame sonu kompaksiyon. protected internal virtual metodlar (Scene aynı assembly'den direkt çağırır).
  * Scene INSTANCE (kullanıcı statik olmasını istemedi — modülerlik): SceneManager statikleri Scene.Active (lazy "Main"), Create(name) additive, SetActive, UpdateAll, Unload (kökler anında OnDisable/OnDestroy). GO doğduğu sahneye kaydolur (go.scene, _rootIndex'li kök listesi — hiyerarşi paneli hazırlığı). Reparent kök listesi + aktifliği günceller (Transform.SetParent → go.OnTransformParentChanged hook).
  * Transform artık : Component (Unity gibi); ctor scale=(1,1,1) düzeltildi (default 0 sprite'ları görünmez yapıyordu); FirstChild/NextSibling internal gezinti.
  * Renderer tabanı + SpriteRenderer (unsafe): Width/Height dünya matrisi basis kolonlarına bake (ek mat mul yok); Material null → paylaşılan mutate materyal (TexView DrawMesh anında yakalanır). Scene.Render(queue) aktif renderer'ları encode eder.
  * Time.time/deltaTime/frameCount (Time.Tick App döngüsünde).
  * Demo: halka artık 8 GO + SpriteRenderer + Spinner.Update (App.Editor.cs). Doğrulandı çalışıyor.
  * Destroy sırası inceliği: DestroyGameObjectNow (mark+OnDisable/OnDestroy alt ağaç) ÖNCE, SetParent(null) SONRA (destroyed guard'ı aktivasyonu keser). Component.Destroy(Transform) reddedilir.
  * EKSİK: MoveGameObjectToScene, GetComponentInParent, component Update sırası script-execution-order.
- GPU KAYNAK YAŞAM DÖNGÜSÜ (GpuResource.cs) — Unity Object modeli, IDisposable/finalizer YOK:
  * ~Mesh/~Texture finalizer'ları KALDIRILDI (GC thread'inden sokol çağrısı bug'dı).
  * GpuResource tabanı: kayıt defteri (Resources, slot'lu swap-remove), LastUsedFrame damgası, KeepAlive(), Persistent bayrağı, CanEvict/ReleaseGpu abstract, Destroy virtual (GPU+kayıt; türev CPU tarifini bırakır).
  * Damgalama TEK BOĞAZDA bedava: RenderQueue.DrawMesh → mesh._Sync() + texture._Sync() (Stamp içeride). markResources tarzı sanal metod/traversal YOK — unutulamaz.
  * Agresif sweep YOK (kullanıcı istemedi — yavaş cihazda oyun içi evict/reload hıçkırığı): Resources.Collect(maxAge=3) yalnız Scene.Unload'dan 3 frame SONRA (ScheduleCollect+Tick, UpdateAll sonunda) veya elle. DE_EDITOR: RenderDebug.Frozen iken Collect çalışmaz (frozen capture eski handle replay eder).
  * Evict = yalnız GPU tarafı; tarif kalır → sonraki _Sync geri yükler. Texture: piksel kopyalılar evictable (RT/FromAlpha değil; RT Persistent). Mesh: vertex/index dizileri artık TUTULUYOR (CanEvict=true), UploadBuffers/Vbuf==0 kontrolü; Mesh.Quad Persistent.
  * Asset yükleme (dosya→CPU→GPU, async, Texture.Load key cache) AYRI FAZ, sonra.
- SERİALİZASYON MİMARİSİ KARARLAŞTIRILDI (uzun tartışma, kullanıcıyla mutabık):
  * Kullanıcının eski Flash motoru XAML/QML tarzıydı: deklaratif XML sahne + bind: ifadeleri + action: script'leri + isimli animasyonlar + include/template (=prefab). Güçlü: tek kaynak, hot-reload, tasarımcı dostu. Zayıf: string içinde program (refactor kırar), görsel editör eklenemez. Örnek sahne görüldü (lobby.xml).
  * TEMEL: Unity SerializedObject modeli — property modeli (tip başına BİR KEZ reflection, TypeFlags deseni) TEK veri modeli: sahne kaydetme + Inspector + Undo + (ileride) prefab diff hepsi bunun üstüne. Alan (field) serialize edilir, dar kurallar (Unity gibi).
  * FORMAT: YAML (kullanıcı seçti — XML git conflict sorunu). İnsan yazabilir kalacak.
  * PLAY/STOP YOK — canlı editör: SceneDoc (bellek içi authored model) = KAYNAK; canlı sahne = projeksiyon. Düzenleme: doc + canlı nesneye ucuz patch (anında görsel, init koşmaz). CTRL+S = COMMIT: YAML yaz + sahne doc'tan FULL RELOAD (init'ler taze koşar — kullanıcının eski motoru gibi). Kod değişikliği de aynı kapı (assembly güncellenince reload). Runtime asla dosyaya sızamaz (kaydedilen hep doc).
  * DİSİPLİN KURALI: oyun durumu (para, kullanıcı vs.) SAHNE DIŞINDA düz C# serviste yaşar; sahne binding'le bağlanır (eski motorun gizli önkoşulu). Runtime state snapshot/restore YOK (bataklık).
  * FAZLAR: 1) property modeli + SceneDoc + instantiate + YAML + save-reload döngüsü. 2) Editör panelleri (Inspector/Hierarchy) doc üstünde + jest-içi patch. 3) Binding/expression motoru (yüklemede bir kez derlenir, delegate) + action'lar (tek çağrı=data, mantık=C#). Prefab BİLE BİLE ertelendi (property modeli oturmadan girilmez).
  * Layout component'leri (Snap/Stretch/HGroup — eski motor formatının yarısı) sahne katmanına gelecek; authored vs layout-üretimi değer ayrımı property modelinde tasarlanacak (henüz karara bağlanmadı).
- SERİALİZASYON FAZ 1 TAMAMLANDI ve DOĞRULANDI (Serialization/SerializedType.cs + SceneDoc.cs):
  * SerializedType: tip başına BİR KEZ reflection → FieldSchema[] (yalnız PUBLIC instance alanlar; Float/Int/Bool/String/Enum/Vec2/Vec3/Color; Texture/Material atlanır — asset sistemi bekliyor). Kanonik string formatlar: float "R" invariant, Vec3 "x y z", Color "#rrggbbaa". ComponentTypes: ad→Type (tüm assembly'ler bir kez taranır; private nested tipler dahil).
  * SceneDoc: GoDoc{Id,Parent(0=kök),Name,Active,Pos/Rot/Scale} + CompDoc{Type,Props(string kv)}. Capture(scene) canlı→doc; InstantiateInto(scene) doc→canlı. UNITY SIRASI: component'ler AddComponentRaw ile aktivasyonsuz eklenir, alanlar yazılır, SONRA ActivateComponentsNow (Awake taze değerleri görür).
  * YAML el yazımı dar altküme (bağımlılık yok): 2'şer boşluk, "- " öğe, '#'/':'/tırnak içeren skalarlar tek tırnaklı. Parser satır+girinti tabanlı (0/2/4/6/8).
  * GameObject.AddComponentRaw(Type) (Activator, nonPublic) + ActivateComponentsNow; Component.GetFlags(Type) dictionary cache (generic TypeFlags<T> hızlı yol duruyor).
  * SpriteRenderer: Texture null → paylaşılan 1x1 White fallback (asset yokken deserialize edilen sahne görünür).
  * App: scene.yaml varsa yükle, yoksa demo kur + bootstrap kaydet. ScenePath = BaseDirectory/../../../scene.yaml (engine/managed/scene.yaml). Kontroller panelinde Kaydet (canlı→yaml, EZER) + Yeniden Yukle (yaml→taze sahne: Scene.Create+SetActive+InstantiateInto+Unload(eski)).
  * Test: elle yaml düzenleme + Yeniden Yukle çalıştı (kullanıcı onayı). NOT: değişiklik görmek için Kaydet'e basmadan yaml düzenle.
  * FAZ 2 (sırada): dosya izleyici (yaml değişince otomatik reload = gerçek Ctrl+S akışı), Hierarchy/Inspector panelleri (doc üstünde düzenleme + jest-içi canlı patch), Undo (property diff).
- TYPECATALOG + INSTANTIATE (mimari karar: statik tip cache YASAK, AOT hazırlığı):
  * Kullanıcı itirazı: Component'teki static Dictionary<Type,flags> hem statik-yasak prensibine aykırı hem reload'u bozar (motor assembly'si oyun Type'larını köklerse AssemblyLoadContext boşalmaz) + kendi AOT derleyicisinde reflection zayıf.
  * Çözüm: Serialization/TypeCatalog.cs INSTANCE — Entry{Type,Name,Create(Func<Component>),CopyTo(Action<C,C>),Flags,Schema}. Editörde FromReflection(); release/AOT'de source generator AYNI Register çağrılarını düz kod üretecek. Statik cache'ler silindi: Component.GetFlags dict, ComponentTypes sınıfı, SerializedType._cache (Get→Build, cache'siz; sonucu katalog sahiplenir).
  * TypeFlags<T> generic static BİLEREK kaldı: generic instantiation T'nin ALC'siyle çöpe gider — reload güvenli hızlı yol.
  * Scene.Catalog alanı (statik değil): InstantiateInto atar, App bootstrap'ta atar; clone katalogu go.scene.Catalog'dan bulur.
  * GameObject.Instantiate(original,parent=null): CloneRecursive (transform TRS + AddComponentRaw(entry) + entry.CopyTo + _enabled kopyası + inactive dallar SetActive(false) pre-aktivasyon) → ActivateTree EN SONDA (Awake kopya değerleri görür — deserialize ile aynı sıra). CopyTo TÜM public alanları shallow kopyalar (schema değil — Texture/Material referansları da geçer, Unity semantiği). İç-referans remap ERTELENDİ (nesne referansı serileşince localId ile).
  * Demo: Kontroller panelinde "Klonla" butonu (ilk root'u klonlar, Spinner.Index değiştirir).
  * DERS: 'dotnet run --no-build' bazen anlık EXIT=0 çıkıyor (sahte); exe'yi doğrudan çalıştırınca sorunsuz. Test için Start-Process ile exe başlat.
- ASSET SİSTEMİ + ASYNC IO TAMAMLANDI ve DOĞRULANDI:
  * Native (sokol_shim.c): STB_IMAGE_IMPLEMENTATION (stb_image.h zaten native'de vardı) + Win32 worker thread iş kuyruğu — de_asset_load(path)→jobId, de_asset_poll(job,&px,&w,&h) 0/1/-1, de_asset_free_job. Dosya okuma+decode TAMAMEN C'de worker'da (managed tek thread = custom AOT sözleşmesi). stbi_set_flip_vertically_on_load(1) → satır 0 altta, GL yönelimi (text V-flip sorunu dosya asset'lerinde baştan çözüldü). InterlockedExchange full fence; slot havuzu 64 job.
  * WASM KARARI: API poll tabanlı olduğu için backend serbest — WASM'de thread varsayılmaz, emscripten_fetch/JS async tamamlar + main thread bütçeli decode (veya createImageBitmap). WASM threads (COOP/COEP) opsiyonel hızlandırma, bağımlılık değil.
  * AssetDatabase.cs (instance): key=köke göreli yol, cache; LoadTexture handle'ı ANINDA döner (Texture.CreatePending = 1x1 beyaz placeholder), Tick(budget=2) frame başına bütçeli bağlar (spike yok). LoadOp = Unity AsyncOperation muadili (poll, IsDone/Failed, aynı asset bekleyenler Next zinciri). App: _assets.Tick() her frame.
  * Texture._AttachPixels: ReleaseGpu (placeholder yıkılır) + piksel kopyası + _dirty → sonraki _Sync gerçek boyutta kurar. Evict/reload ile AYNI durum makinesi (NotResident→CPU→GPU birleşti).
  * SerializedType Kind.Texture: Format=texture.Name (asset key), Parse=assets.LoadTexture(key). Parse imzası AssetDatabase aldı; InstantiateInto(scene,catalog,assets). yaml'da "Texture: orb.png" doğrulandı.
  * Demo: assets/orb.png (ffmpeg testsrc2 128x128; ffmpeg: C:\ffmpeg-8.1-essentials_build\bin). Eski yaml → scene.yaml.bak.
  * Sokol.cs'e using System eklendi (IntPtr). AssetsPath = proje kökü/assets.
  * SONRAKI ADIMLAR (async üstüne): sahne geçişinde LoadOp grubu bekleme, time-sliced instantiate, GUID/.meta, mesh/prefab asset'leri.
- NESNE REFERANSI SERİLEŞTİRME + CLONE REMAP TAMAMLANDI (core sertleştirme; binding/expression/layout BİLİNÇLİ ertelendi — kullanıcı: "core mükemmel olsun, onlar yapıtaşı üstü"):
  * SerializedType: Kind.GoRef (GameObject alanı) + Kind.CompRef (Component türevi alan). Format/Parse bu kind'lar için SceneDoc'ta özel işlenir (bağlam ister).
  * SceneDoc.Capture İKİ GEÇİŞ: önce AssignIds (GO→localId haritası — ileri referanslar çözülsün), sonra CaptureGo. Değer formatı: GoRef="goId", CompRef="goId:TipAdi:aynıTiptenKaçıncı". Sahne dışı referans → boş (kaydedilemez).
  * InstantiateInto: ref alanları deferredRefs listesine, TÜM nesneler doğduktan sonra ResolveRef ile çözülür (ileri referans serbest), aktivasyon en sonda (Awake çözülmüş referansları görür).
  * GameObject.Instantiate: goMap+compMap (transform dahil) kurulur; RemapRefs klon İÇİNE işaret eden Go/CompRef alanlarını klon kopyalarına çevirir, dış referanslar aynen (Unity semantiği).
  * Demo: Spinner.Target (public SpriteRenderer) — yaml'da "1:SpriteRenderer:0" doğrulandı; Awake _sr = Target ?? GetComponent.
  * DOĞRULANDI (2026-09-11): fallback geçici kaldırılarak test edildi — reload sonrası tüm orb'lar canlı (ref çözüldü), klonlar bağımsız dönüp renkleniyor (remap doğru). Fallback geri kondu. Doğrulama tekniği: fallback maskeleyeceği için test sırasında kaldır.
- CORE SERTLEŞTİRME ÜÇLÜSÜ (stres + lifecycle testleri + prefab):
  * STRES: App'te StressOrb component (Seed'den deterministik yörünge, alloc'suz) + "Stres +5000" butonu + zero-alloc perf HUD (sağ üst, DrawUi/DrawPerfHud: frame ms EMA x100, GC.GetAllocatedBytesForCurrentThread delta B/f, RootCount). TickPerfStats loop başında.
  * LIFECYCLE TESTLERİ: LifecycleTests.cs (#if DE_EDITOR, açılışta Run(catalog)) — izole Scene.Create("lifecycle-test"), s.Update() elle sürülür, sonuç konsola. Recorder component (A/E/S/U/D/X harf logu, log PRIVATE StringBuilder — CopyTo public alan kopyaladığı için clone log paylaşmaz). 6 senaryo 15 assert: Awake'te AddComponent (AE anında), self-destroy aynı frame DX, Start ömür boyu tek (SetActive döngüsü), OnEnable'da self-disable (Update hiç koşmaz), inaktif parent'a reparent D / parent aktifleşince E / parent destroy alt ağaç DX, Update içinde Instantiate (klon AE anında, U sonraki frame). SONUÇ: 15 PASS 0 FAIL (2026-09-11). Exe stdout: Start-Process -RedirectStandardOutput ile okundu.
  * STRES SONUCU (2026-09-11, kullanıcı ekranı): 55.008 GO'da 0 B/f (SIFIR ALLOC), 24.16ms/frame Debug build + blur açıkken (~0.44µs/sprite: Update+transform+encode+sort+upload dahil). 60fps sınırı ~35-40k bağımsız sprite (Debug). Hot path alloc'suz DOĞRULANDI.
  * PREFAB (runtime şablon; override/variant BİLEREK yok): Prefab.cs {Key, Doc(SceneDoc), Assets} + Instantiate(parent) → Doc.Spawn. SceneDoc.CaptureSubtree(root,catalog) kök parent=0; SceneDoc.Spawn(parent,catalog,assets) → ilk kök GO döner (InstantiateInto artık Spawn'a delege — sahne yükleme ve prefab spawn AYNI yol: ref çözümü + geç aktivasyon). AssetDatabase.LoadPrefab(key) cache'li senkron parse. Demo: "Prefab Kaydet"/"Prefab Spawn" butonları (assets/orb.prefab.yaml).
  * PREFAB DOĞRULANDI (2026-09-11): Prefab Kaydet + tekrarlı Prefab Spawn çalışıyor — her spawn bağımsız orb.
  * UI FIX: Kontroller paneli içeriği taşınca scrollview'e sarıldı (GuiClip.VisibleRect + Gui.BeginScrollView(0,0,vis,500) + EndScrollView; layout rect'leri clip offset'iyle kayar, mouse lokal — layout+scrollview karışımı sorunsuz çalıştı).
- SERİLEŞTİRME TAM SÖZLEŞME TAMAMLANDI (2026-09-11, 12/12 test PASS):
  * Attributes.cs: SerializeFieldAttribute + FormerlySerializedAsAttribute (DigitoyEngine ns). [NonSerialized]=System. [SerializeReference] BİLİNÇLİ YOK (tüketicisi binding/action; tip-etiketli node+rid ister, ihtiyaçta).
  * Yaml.cs: DocNode (Scalar/Items/Fields, sıra korunur) + genel yazıcı/parser (2'şer girinti, "- " öğe, seq-of-map ilk alan inline "- id: 1" biçimi — eski format birebir korunur; tırnaklı skalar ':' içerebilir, tokenize'da quote-başlangıç anahtarsız sayılır; ParseSeq map öğesini satırı indent+2 normal satıra çevirip ParseMap'e devreder).
  * SerializedType yeniden: Kind + List + Object. FieldSchema{Name,FormerName,Info,Kind,ElementKind,ElementType,Nested}. Build: base-önce tip zinciri (DeclaredOnly — base'deki private [SerializeField] görünsün), kural: (public && !NotSerialized flag) || [SerializeField]. Koleksiyon: T[]/List<T> (iç içe liste yok). IsInlineObject: [Serializable] flag + parametresiz ctor + motor tipi değil; derinlik ≤7. STRUCT içinde Go/CompRef Build'de ATLANIR (boxed kopya vs deferred çözüm). WriteField/ReadField: DocNode ağacı; ref'ler RefSink delegesiyle deferred (liste elemanı/iç nesne dahil closure set — SetAt array/IList). Object: mevcut değer ?? Activator; struct boxed doldurulup geri yazılır. Find: Name sonra FormerName.
  * SceneDoc: CompDoc.Props artık List<KV<string,DocNode>>; ToYaml/Parse DocNode köküyle; Spawn'da Sink → deferred (kind,val,set) → ResolveRef. Format diskte değişmedi.
  * SYSLIB0050: IsNotSerialized/IsSerializable obsolete → FieldAttributes.NotSerialized/TypeAttributes.Serializable flag'leri + lokal pragma (yanlış pozitif — BinaryFormatter uyarısı, biz kendi serializer'ımızda okuyoruz).
  * SerializationTests.cs (#if DE_EDITOR, açılışta): 12 assert — public/[NonSerialized]/[SerializeField]/[FormerlySerializedAs] (yaml'da ad değiştirip), float[], List<Vec3>, List<SpriteRenderer> (ref listede!), iç içe nesne+listesi, enum. Hepsi PASS, build uyarısız.
- EDİTÖR/PROJE YAPISI (A+B+C ADIMLARI TAMAMLANDI 2026-09-11; testler 15+12 PASS, gamecode yüklendi):
  * A) AYRIM: engine/managed = DigitoyEngine.csproj LIBRARY (InternalsVisibleTo DigitoyEditor; native dll None+CopyToOutput transitif taşınır). editor/ = DigitoyEditor.csproj EXE (DE_EDITOR her config'de): EditorApp.cs (eski App.Editor), Project.cs, GameCode.cs, TimelinePanel, RenderDebugPanel, LifecycleTests, SerializationTests — namespace DigitoyEditor + using DigitoyEngine.
  * B) PROJE MODELİ (Unity adlandırma): Projects/Sandbox/{ProjectSettings/project.yaml (name+startScene), Assets/{Scenes/Main.scene.yaml, Textures/orb.png, Scripts/, Editor/}, Library/}. Project.Load(root); editör CLI arg ?? DefaultProjectPath (repo/Projects/Sandbox). Texture key artık "Textures/orb.png". Bootstrap demo SİLİNDİ (sahne yoksa boş).
  * C) OYUN KODU: GameCode.cs — Assets/**/*.cs taranır (yolu /Editor/ içerenler HARİÇ — custom editor assembly'si sonraki faz), Library/Build/Game.csproj üretilir (Reference HintPath=editör yanındaki DigitoyEngine.dll, EnableDefaultCompileItems=false), dotnet build -v q, collectible ALC LoadFromStream (dosya kilitlenmez). Spinner/StressOrb → Projects/Sandbox/Assets/Scripts/*.cs taşındı; DIŞ ASSEMBLY'DE lifecycle override'ları 'protected override' YAZILIR (protected internal cross-assembly kuralı). Engine'e Screen.cs (width/height, internal set — script'lerin editör statiklerine bağımlılığı kesildi).
  * TypeCatalog.FromAssemblies(params Assembly[]) eklendi; editör RebuildCatalog = engine+editor+game assembly'leri (AppDomain taraması DEĞİL — unload edilmiş eski assembly ad çakışması yapar). FromReflection artık FromAssemblies'e delege.
  * EditorApp katalog-tabanlı: SpawnStress _catalog.Find("StressOrb") + AddComponentRaw + schema SetValue + ActivateComponentsNow (editör oyun tiplerini İSİMLE bilir). "Kodu Derle+Yukle" butonu = ReloadCode: RenderDebug.Unfreeze → sahne yık (ALC boşalsın) → CompileAndLoad → RebuildCatalog → sahne doc'tan reload. _speed/Reset/Hız demo kalıntıları silindi (Spinner.Speed alan oldu).
  * D SIRADA: GUID/.meta + script kimliği (typemap: guid→class listesi kaynak taraması, tek-class rename otomatik migrasyon, [MovedFrom] attr). NOT: Unity class rename YAKALAMAZ (dosya GUID'i korur, class rename = missing script) — biz daha zekisini hedefliyoruz. Editor/ klasörü derlemesi (custom editor assembly) de sırada.
  * D) GUID/.META + SCRIPT KİMLİĞİ TAMAMLANDI (2026-09-11):
    - AssetDatabase.ScanMetas(createMissing): her asset dosyasına <ad>.meta (guid: hex32); sahipsiz meta silinir; guid↔path indeks. ResolvePath(keyOrGuid) — LoadTexture/LoadPrefab guid VEYA yol kabul eder (elle yaml yazımı serbest). Editör açılışta + ReloadCode'da tarar.
    - Serileştirme: SerializedType.WriteField artık AssetDatabase alır; Kind.Texture GUID yazar (PathToGuid ?? yol). SceneDoc.Capture/CaptureSubtree(scene,catalog,assets=null) imzaları.
    - Typemap (Library/typemap.yaml): GameCode.UpdateTypemap — script guid → dosyadaki Component class'ları (regex ∩ assembly tipleri). RENAME TESPİTİ: aynı guid'de tek class kayıp + tek yeni → renames kaydı (zincir sıkıştırmalı) + konsol logu. Unity bunu YAPMAZ (class rename = missing script).
    - TypeCatalog._aliases + RegisterAlias + Find alias zinciri (max 8 hop); [MovedFrom("Eski")] class attribute'u FromAssemblies'te otomatik alias. Editör RebuildCatalog typemap rename'lerini alias olarak basar.
    - Doğrulandı (kullanıcı): Kaydet→Texture GUID yazıldı + reload çözdü; class rename Spinner→OrbSpinner "[gamecode] rename yakalandi" ile otomatik migre edildi (sahnede eski ad, alias çözdü). NOT: sahnede artık class OrbSpinner (kullanıcının test rename'i kalıcı olabilir — kontrol et).
    - Bilinen geçiş dönemi davranışı: "Kaydet" canlı sahneyi yakalar (pos/rot/Color animasyon anındaki değerlerle yazıldı) — editör panelleri gelince kaynak doc olacak, runtime sızmayacak.
    - EditorApp hâlâ demo içeriyor (blur sahnesi, Isim textfield vs) — kullanıcı bilinçli erteledi ("zamanı gelince temizleriz").
- HIERARCHY + INSPECTOR PANELLERİ, DOC-KAYNAKLI DÜZENLEME (2026-09-11, KULLANICI HENÜZ DOĞRULAMADI — kaldığımız yer):
  * editor/Selection.cs: Selection.DocId (int, SceneDoc localId; 0=yok).
  * editor/EditorScene.cs: DOC = KAYNAK modeli — Doc(SceneDoc) + Path + _live(docId→GO) + Load(path,catalog,assets)/Instantiate(taze Scene.Create+Spawn+eski Unload)/Save(doc'u yazar). Patch'ler: ApplyTransform/ApplyActive/ApplyEnabled/ApplyField (FindLiveComponent: aynı tipten kaçıncı CompDoc → canlı eş; ReadField NoopRef sink — ref alanları v1 düzenlenmez).
  * SceneDoc değişiklikleri: Spawn(parent,catalog,assets,liveMap=null) docId→GO haritası doldurur; CompDoc.Enabled eklendi (Unity m_Enabled — capture c._enabled, spawn'da geri, yaml "enabled: false" yalnız false iken, parse'da type/enabled prop sayılmaz).
  * HierarchyPanel: doc ağacı (GoDoc.Parent), satır tık=DocId seçimi, ok=collapse (HashSet<int>), inaktif soluk. Runtime-doğan nesneler GÖRÜNMEZ (authored görünüm — bilinçli).
  * InspectorPanel: doc props (DocNode scalar) gösterir/düzenler — Float/Int drag, Bool toggle, Enum döngü (GetNames yalnız tıkta), Vec2/Vec3 drag; Texture GUID→ResolvePath ile salt-okunur yol gösterimi; ref/List/Object salt-okunur özet. Commit(): node.Scalar güncelle (yoksa Props.Add) + es.ApplyField — TEK COMMIT NOKTASI (undo diff buraya bağlanacak). Transform: GoDoc.Pos/Rot/Scale typed + ApplyTransform. GO active + comp enabled toggle'ları doc+canlı.
  * EditorApp: _editScene (EditorScene), EditScene/Catalog/Assets internal accessor'ları. Kaydet=_editScene.Save (runtime SIZAMAZ), Yeniden Yukle=Load(dosyadan), ReloadCode=Instantiate(bellekteki doc'tan — kaydedilmemiş düzenleme kod reload'da korunur). ReloadScene silindi. Panel dock: sol leaf {Hiyerarsi,Kontroller,Ekstra,Liste}, sağ {Inspector,Timeline,Frame Dbg}.
  * DİKKAT (bu turda yaşandı): InspectorPanel tam-dosya değişiminde eski sınıf gövdesi artık kaldı, ikinci replace ile temizlendi — tam yeniden yazımda dosya sonunu doğrula.
  * SIRADAKİ ADIMLAR (öncelik sırası):
    1. KULLANICI DOĞRULAMASI: Inspector'da Position sabit mi (authored), Speed sürükleyince canlı hızlanıyor mu, Kaydet'te yaml temiz authored değer mi.
    2. Inspector eksikleri: Color alan editörü (doc modunda salt-okunur kaldı — "#hex" parse + 4 kanal drag), String editörü (TextField buffer), asset seçici (Texture alanına tıkla → proje asset listesi), GO name TextField.
    3. Hierarchy yapısal işlemler: GO ekle/sil/reparent (doc'a + canlıya; NextId üretimi — doc max id+1), component ekle/kaldır (katalog listesinden).
    4. Undo/Redo: Commit noktalarından property diff kaydı (doc üstünde).
    5. Editör yerleşimi: dock şu an alt şerit — tam pencere dockspace (sol hiyerarşi / orta game view RT / sağ inspector). Game view = kamera Target=null→editörde RT dolaylaması (eski plan maddesi).
    6. Assets/Editor derlemesi (custom editor assembly) + custom inspector kayıt API'si.
    7. EditorApp demo temizliği (blur sahnesi, Isim textfield, Liste/Ekstra panelleri — kullanıcı 'zamanı gelince temizleriz' dedi).
    8. Scene View (retained katman hazır) + gizmo/seçim.
- PREVIEW MİMARİSİ KARARLAŞTIRILDI (2026-09-11, tartışma bitti — implementasyon SIRADA):
  * Problem: Unity'de preview yazmak = elle CaptureState/RestoreState/CollectTargets (kullanıcının Unity MovieClip kodu örnek — track'ler sahnedeki BAŞKA nesneleri sürer, dokunulan küme dinamik). Amaç: component yazarı HİÇBİR ŞEY yazmadan, doc'a sızmasız preview'ı core sağlasın.
  * ÇÖZÜM — PreviewSession (in-place, reload-restore): Begin() = preview modu; custom editor/panel CANLI nesneleri serbestçe mutate eder (SampleAt/scrub/action) — kayıt/snapshot YOK çünkü DOC ZATEN EVRENSEL SNAPSHOT. End() = bellekteki doc'tan reload → tüm izler ölür (restore = motorun zaten kullandığı yol).
  * Kanal ayrımı otomatik: Commit→doc (+canlı patch) düzenlemeleri = YAŞAR (preview içinde keyframe düzenle → sonraki SampleAt yeni değeri örnekler — sıcak iterasyon, durdurmadan düzenle); preview sample izleri = canlıda kalır, End'de ÖLÜR. Kaydet preview sırasında bile güvenli.
  * Session bitişi ÖRTÜK: başka GO/component seçilince biter (Unity Animation Window modeli) + açık stop. Oyun saati DURMAZ (kullanıcı kararı: çakışma runtime'da da var — WYSIWYG; pause opsiyonu ileride, session'ın parçası değil).
  * [Previewable] attribute: işaretli component'e Inspector'da jenerik preview kontrolü; custom editor'ler PreviewSession.Begin/End'i kendileri tetikler (tek yükümlülük Begin/End paranteziRestore). Attribute'suz component'te preview UI yok.
  * SAHNE SAATİ hâlâ gerekli altyapı (preview'dan bağımsız kabul edildi): Scene kendi time/deltaTime akümülatörü; Scene.Update(dt) girişinde Time statiklerine SET (global sadece pencere). Screen de aynı ilke (sahne-kapsamlı ambient: Time, Screen; ileride Random/Input). İzole sahne preview'ı (v2 backend — ana sahnenin runtime durumunu bozmadan ayrı Scene+RT panel) sonraya.
  * İMPLEMENTASYON SIRASI (devam edilince): 1) Scene saati (+Screen sahne-kapsamı), 2) [Previewable] attribute (engine), 3) editörde PreviewSession (Begin/End + seçim-değişince-bitir + reload), 4) Inspector'da jenerik preview kontrolü, 5) örnek: MovieClip benzeri basit bir component + timeline panel entegrasyonu (mevcut TimelinePanel'in sahne component'ine bağlanması).
- UNDO/REDO + MUTASYON BOĞAZI TAMAMLANDI (2026-09-11, kullanıcı testi bekliyor):
  * Sıra kararı: undo+yapısal işlemler PreviewSession'dan ÖNCE (ortak zemin: doc-mutasyon API'si). Kalan sıra: yapısal işlemler (GO/comp ekle-sil-reparent, aynı boğaza) → sahne saati → [Previewable]+PreviewSession → MovieClip örneği+timeline bağlama.
  * editor/UndoStack.cs: UndoOp (Sealed + TryCoalesce) + UndoStack (Push coalesce'li, SealTop, Undo/Redo iki stack). Op'lar: PropOp{GoId,CompIndex,Prop,Old/NewValue string — kanonik doc formatı, diff bedava}, TransformOp{6xVec3}, ActiveOp/EnabledOp (ctor'da Sealed=true — tek atımlık). Coalesce: aynı hedefe mühürsüz ardışık giriş birleşir (ilk Old korunur, son New güncellenir).
  * EditorScene: History(UndoStack) + SetProp(g,cd,f,oldValue,newValue)/SetTransform/SetActive/SetEnabled — kayıt BOĞAZIN İÇİNDE (RecordObject unutma bug'ı yapısal imkânsız); WriteProp (doc node yaz/ekle + ApplyField); ApplyPropOp/ApplyTransformOp/ApplyActiveOp/ApplyEnabledOp (docId+compIndex adresli — reload'a dayanıklı); FindProp EditorScene'e taşındı (static). DoUndo/DoRedo.
  * InspectorPanel: Commit silindi → es.SetProp(old,new — old panel'in gösterdiği formatlanmış değer, prop yoksa default string; undo'da node remove edilmez, eski gösterilen değer yazılır). Transform/active/enabled da Set* boğazından.
  * EditorApp.DrawUi: Ctrl+Z/Y (KeyboardControl==0 iken — textfield odağı korunur) + HotControl==0 iken History.SealTop() (jest mühürleme) + Kontroller'de Undo/Redo butonları.
  * DERS: kullanıcının formatter'ı editor dosyalarını yeniden biçimlendiriyor (case blokları girintili süslü) — eski metinle replace başarısız olur, önce mevcut hali oku.
- YAPISAL İŞLEMLER TAMAMLANDI (2026-09-11, kullanıcı testi bekliyor):
  * Strateji: yapısal mutasyon = doc + FULL RELOAD (RefreshLive=Instantiate; kısmi canlı-cerrahi bug sınıfı yok; runtime fazı sıfırlanır — beklenen).
  * SceneDoc.Spawn İKİ GEÇİŞ oldu: önce tüm GO'lar doğar, sonra parent/transform/comp — parent çözümü liste sırasından bağımsız (reparent edilmiş doc'ta çocuk önce gelebilir).
  * EditorScene: AddGameObject(parentId)→NextId(max+1), RemoveGameObject (CollectSubtree ile alt ağaç+indeksler), Reparent (döngü guard'lı IsDescendantOf), AddComponent(g,typeName — props boş=default), RemoveComponent. Hepsi History.Push'lu.
  * UndoStack yapısal op'lar: AddGoOp/RemoveGoOp (artan indeksle geri ekleme)/ReparentOp/AddCompOp/RemoveCompOp — hepsi Sealed=true, Apply=doc mutasyonu+RefreshLive.
  * UI: HierarchyPanel araç çubuğu (+GO/+Child/Sil, toolbar 26px); InspectorPanel: component başlığında "x" (kaldır, koleksiyon değişince break — sonraki pass taze), en altta "Add Component" açılır tip listesi (TypeCatalog.Entries eklendi — _byName.Values). MeasureHeight güncellemesi.
  * Reparent UI YOK henüz (API hazır; hiyerarşide drag-drop sonraya).
- SAHNE SAATİ + [PREVIEWABLE] + PREVIEWSESSION TAMAMLANDI (2026-09-11, kullanıcı "ok sayalım sonra bakarız"):
  * Scene: SceneTime/ScreenWidth/Height/ExternallyDriven alanları; Update(dt) girişinde Time.time/deltaTime + Screen.width/height PENCERE statiklerine basılır (ambient=sahneye ait ilkesi); UpdateAll(dt,screenW,screenH) frameCount++ ve ExternallyDriven sahneleri atlar. Time.Tick SİLİNDİ (internal set'li pencereler). App: dt hesabı (_lastUpdateT) + UpdateAll(dt,lw,lh).
  * PreviewableAttribute (engine Attributes.cs) → TypeCatalog.Entry.Previewable (IsDefined). Sandbox Spinner [Previewable] işaretli.
  * editor/PreviewSession.cs: statik — Begin(docId,compIndex)/End(=RefreshLive, izler ölür)/Restart/Tick(selectedDocId — seçim değişince örtülü End). EditorApp.DrawUi'de PreviewSession.Tick. InspectorPanel: Previewable entry'de Preview/Stop + Restart butonları.
  * LifecycleTests s.Update() → s.Update(0f) (PowerShell toplu değişim — mekanik tekdüze değişim istisnası).
  * SIRADA: MovieClip (ENGINE'e — Flash mirası core özellik) + kompakt ClipPanel (timeline bağlama): CommitComponent boğazı (canlı component→doc tam serileştirme + CompPropsOp undo), scrub/play PreviewSession içinde SampleAt (commit'siz — izler ölür), key düzenleme commit'li. Mevcut TimelinePanel'in tam rebind'i sonra.
- MOVIECLIP + CLIPPANEL + EASING + TWEEN TAMAMLANDI (2026-09-11, tween stres kullanıcı testi bekliyor):
  * MovieClip (ENGINE core — Flash mirası): AnimChannel(PosX/Y/Rot/ScaleX/Y/Alpha), MovieKey{Time,Value,Ease}, MovieTrack{Channel,Active,Keys}, [Previewable] MovieClip{Tracks,Loop,PlayOnStart,Speed} — SampleAt SAF (scrub bedava), Update sadece zaman ilerletir. Preview için component kodu SIFIR satır.
  * ClipPanel (editör, kompakt timeline): Play/Stop/+Track/+Key/değer-ease-Del toolbar, cetvel scrub (PreviewSession otomatik Begin + clip.Stop — zaman devralınır), elmas sürükleme (Resort ile sıra korunur, MouseUp'ta commit), kanal ayrımı: scrub/play=canlı SampleAt (Stop'ta ölür), key düzenleme=CommitComponent→doc (kalıcı, undo'lu).
  * EditorScene.CommitComponent(g,cd): canlı component'in TÜM alanlarını doc'a serileştirir (zengin editör boğazı; ids=_live ters harita ile ref kodlama) + CompPropsOp (Old/New props listesi, coalesce'li — drag spam'lemez). SceneDoc.GoRefValue/CompRefValue internal yapıldı.
  * EASING CORE (Easing.cs): Ease enum 31 Penner + Easing.Evaluate = LUT (31x257 float ~32KB açılışta) ~2-3ns HER EĞRİ AYNI MALİYET; EvaluateExact analitik referans/pişirici. Curve ([Serializable] Hermite CurveKey listesi): Evaluate kendi LUT'una lazy pişer (Version++ ile invalidate — editör arttırır), EvaluateExact hermite. MovieClip yerel Ease enum+fonksiyonu SİLİNDİ → global (yaml ease adları isimle uyumlu). ClipPanel ease cycle 31'e, adlar Enum.GetNames cache.
  * TWEEN CORE (Tween.cs): sahneye ait TweenPool (Scene.Tweens; Tick Update-LateUpdate arası) — struct Slot dizisi + free list + GENERATION (Kill'de Gen++ → bayat handle no-op). TweenHandle{Pool,Index,Gen} readonly struct; fluent Ease/WithCurve/Delay/From/Loops(-1=sonsuz,yoyo)/Then(zincir: Waiting slot uyanır)/OnDone/Cancel/IsActive/Value. Kanallar: PosX/PosY/Rot/ScaleX/Y/Uniform/Alpha/ColorRgb (switch, delegesiz) + FieldFloat (schema erişimcisi) + Value (hedefsiz, poll). Ölü hedef: Target._destroyed → sessiz Kill (GO destroy tüm comp'ları işaretler — Transform dahil). From ilk tick'te mevcut değerden (HasFrom).
  * "SERİLEŞEN SAYISAL ALAN = OTOMATIK TWEENABLE" kararı: [Tweenable] attr YOK. FieldSchema'ya GetFloat/SetFloat (Func/Action) — EnsureFloatAccessors expression-compile (alan başına 1 kez, çağrı ~2-3ns, boxing yok; Int convert'li); release'te source-gen aynı slotları dolduracak. ERİŞİM DÜZLEMİ notu: aynı erişimciler ileride serializer/Inspector/binding/MovieClip genelleşmesi için (5 tüketici).
  * Delege overhead tartışması: ~2-3ns/çağrı, ease+lerp yanında gürültü; gerekirse delegate*/UnsafeAccessor/custom-AOT direct-write yükseltmesi (slot aynı). Kullanıcı ikna.
  * Editör: "Tween Stres 6k" butonu (2000 GO grid + 3'er sonsuz tween). HUD ile 0 B/f doğrulanacak.
  * TWEEN STRES DOĞRULANDI (kullanıcı ekranı): 8.008 GO = ~24.000 canlı tween, 0 B/f, 16.83ms (60fps vsync sınırı, blur açık, Debug build). Zero-alloc + hız hedefi tutturuldu.
- ★ KALDIĞIMIZ YER + YOL HARİTASI (2026-09-11 sonu — kullanıcı başka konuya geçti):
  * SON DURUM: Animasyon omurgası tamam ve doğrulanmış (Easing LUT + Curve + Tween 24k@60fps@0alloc + MovieClip/ClipPanel + PreviewSession). Editör: Hierarchy/Inspector doc-kaynaklı + undo/redo + yapısal işlemler + Kaydet/Yeniden Yukle/Kodu Derle+Yukle. Proje modeli: Projects/Sandbox, GUID/meta, typemap rename yakalama, oyun kodu ALC.
  * BEKLEYEN DOĞRULAMA: MovieClip/ClipPanel kullanıcı akış testi (Clip sekmesi: +Track/+Key/scrub/Play/Stop/undo) — henüz kullanıcı elle denemedi.
  * YOL HARİTASI (öncelik sırasız, hepsi açık):
    1. Editör yerleşimi: tam pencere dockspace (sol hiyerarşi / orta GAME VIEW RT / sağ inspector) — kamera Target=null→editörde RT dolaylaması (eski plan maddesi).
    2. Inspector eksikleri: Color alan editörü (doc modunda salt-okunur), String editörü (TextField), asset seçici (Texture alanı), GO name düzenleme, reparent drag-drop (API hazır).
    3. SOURCE GENERATOR (release/AOT): TypeCatalog Register + Create/CopyTo/Flags/Schema + FieldSchema Get/SetFloat erişimcileri — mimari hazır, mekanik iş.
    4. Particle sistemi (easing zemini hazır — kullanıcı bahsetti, tasarım konuşulmadı).
    5. Assets/Editor derlemesi (custom editor assembly) + custom inspector/panel kayıt API'si.
    6. EditorApp demo temizliği (blur sahnesi, Isim textfield, Liste/Ekstra panelleri).
    7. Scene View + gizmo/seçim; sahne geçişi + LoadOp grubu; MoveGameObjectToScene; izole preview sahnesi (v2 backend); [SerializeReference]; prefab override/variant; WASM asset backend; Türkçe charset (SDF font Latin-1); TextField undo; Texture yönelim bayrağı (dosya sprite'ları stbi flip ile çözüldü ama RT/atlas karışıklığı notu duruyor).
    8. TimelinePanel'in (zengin demo) ClipPanel yerine MovieClip'e tam rebind'i.
  * Çalıştırma düzeni: editör = dotnet build editor + editor\bin\Debug\net9.0\DigitoyEditor.exe (Start-Process ile); native değişince engine\native\build_editor.cmd önce.
- SIRADA: GameObject/Component katmanı (retained sahne, Sprite, Scene View) — kalan tek büyük kol. Ayrıca ufak adaylar: pass-başına GPU timer, RenderDoc tetikleyici, capture'da GUI pass'lerini hariç tutma seçeneği.
  İNCELEME SONUCU (old_engine_readonly):
  * imgui.c (~1700sat) = Unity IMGUI'nin C kopyası: Layout→input→Repaint çok-pass, widget seti dolgun (textfield caret'li, scrollview, window, DOCKING ağacı), statik havuzlar zero-alloc, open-addressing control-state tablosu. ZAYIF: ID sadece hash(hint) (Unity'nin sıralı sayacı yok), layout tek-geçiş (gerçek two-pass değil).
  * imgui_font.c: stb_truetype SDF atlas 2048², kerning tablosu, smoothstep AA — text fazı için hazır şablon.
  * Multi-window: engine_host_render_panel(panelId,...) her native pencere kendi swapchain'inde bağımsız 3-pass IMGUI turu; DRAG down/up'ı ezmez (event kuyruğu inceliği). engine_window.c sadece Win32 stub, gerçeği GLFW host köprüsünde.
  * Unity referans dosyaları (Modules/IMGUI): LayoutGroup.cs (CalcWidth→SetHorizontal→CalcHeight→SetVertical + margin çakıştırma + scroll reflow — EN KRİTİK), GUILayoutUtility.cs (DoGetRect kaydet/replay, LayoutCache), GUI.cs, GUIUtility.cs (GetControlID sayaç+hint, hot/keyboard), GUIStyle/GUISkin (8-state), GUIContent.Temp (statik mutate, alloc'suz), GUIClip (mouse'u lokal uzaya çevirir, scroll=clip offset).
  * C# plan: omurga Unity'den (çok-pass+ID sayaç+two-pass layout+GUIClip+8-state style), gövde C'den (struct havuzları+int index, boxing'siz LayoutOption, docking, pencere-başına tur). Span API'ler, params[] yasak, Dictionary<int,object> yerine tip-başına struct havuzu. Çizim draw list'siz doğrudan Camera.Queue.DrawMesh + SetScissor opcode.
- GameObject/Component iskelet, Sprite.sprite<T>() boş
- Input kullanımı yok, asset loading senkron, build.sh boş

## IMGUI referans incelemesi (2026-09-10, rapor kullanıcıya verildi)
- Eski C imgui (old_engine_readonly/imgui.c ~1690 satır): Unity modelinin sade C kopyası.
  Layout+Repaint+input event'leri ile OnGUI tekrar koşulur; Layout'ta entry/group kaydı,
  diğer event'lerde replayEntry/replayGroup cursor'la cache okunur. Tüm state statik sabit
  dizilerde (2048 control state open-addressing hash, 4096 layout entry, 256 group) → zero-alloc.
  hot/keyboardControl, skin (8'li style state), origin stack'li BeginGroup+scissor, dock tree var.
  Layout çözümü tek geçiş expand dağıtımı (Unity'nin two-pass min/max'ından basit).
- imgui_font.c: stb_truetype SDF atlas (2048², 128px hücre, 224 glyph ASCII+Latin1), kerning
  tablosu, smoothstep SDF shader, quad-per-glyph engine_draw_mesh.
- Multi-window: engine_window.c saf Win32 HWND (GL yok, stub); gerçek detached panel GLFW host
  köprüsünde (engine_host_glfw.c bu repoda YOK) — pencere başına swapchain, engine_host_render_panel
  pencere-lokal event ile Layout→event→Repaint üç pass koşar.
- Unity IMGUI kritik dosyalar: LayoutGroup.cs (CalcWidth/SetHorizontal/CalcHeight/SetVertical
  two-pass + margin çakıştırma + GUIScrollGroup reflow — en önemli), GUILayoutUtility.cs (LayoutCache,
  DoGetRect), GUI.cs (DoControl state machine), GUIStyle.cs, GUIContent.Temp (3 statik mutate —
  zero-alloc), GUIStateObjects.cs (id→state), GUIClip push/pop. ID sırası korunumu invariant:
  koşullu kontroller bile GetControlID çağırmalı (ScrollView 6 dummy id).
