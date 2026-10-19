using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Builtin RPC komutlari: hepsi mevcut EditorScene bogazlarina ince marshal —
// undo/redo, prefab diff, canli patch bedava (Inspector'la ayni yol).
static class RpcCommands
{
    // --- kesif ---

    [RpcCommand("rpc.describe", "Tum komutlari parametre semalariyla listeler")]
    static DocNode Describe() => RpcRegistry.Describe();

    [RpcCommand("rpc.guide", "START HERE (kodu bilmeyen ajan icin): is akisi, sahne kavramlari (UI = LayoutBox), deger bicimleri, davranis icin kod gerektigi, dogrulama dongusu")]
    static DocNode Guide()
    {
        var r = DocNode.Map();
        DocNode Lines(params string[] lines)
        {
            var s = DocNode.Seq();
            foreach (var l in lines) s.Items.Add(DocNode.Scal(l));
            return s;
        }
        // CANLI DURUM: AI baglandiginda hangi oyun/tema/sahnede oldugunu bilsin (kazara yanlis yere yazmasin).
        {
            var st = DocNode.Map();
            st.Add("workingGame", DocNode.Scal(App.Project?.Game ?? ""));
            st.Add("workingTheme", DocNode.Scal(string.IsNullOrEmpty(SceneDoc.ActiveTheme) ? "default" : SceneDoc.ActiveTheme));
            st.Add("openScene", DocNode.Scal(App.EditScene?.Path != null && App.Project != null ? System.IO.Path.GetRelativePath(App.Project.AssetsPath, App.EditScene.Path).Replace('\\', '/') : ""));
            st.Add("note", DocNode.Scal(string.IsNullOrEmpty(SceneDoc.ActiveTheme)
                ? "Tema default: mutasyonlar TABANA yazilir."
                : "Calisilan tema '" + SceneDoc.ActiveTheme + "' (editorde secili): mutasyonlar bu KATMANA gider ve tema kapisi theme:'" + SceneDoc.ActiveTheme + "' ya da theme.set beyani ister. Tabani duzenleyeceksen kullaniciya sor, sonra theme.set('')."));
            r.Add("state", st);
        }
        r.Add("workflow", Lines(
            "1. scene.tree (KOMPAKT hiyerarsi: id, ad, component tipleri; root/depth ile kademeli gez) ya da scene.new/scene.open. Detay: scene.dump(go, props) alt agac/alan degerleri, prop.get(go, comp). Tum sahne scene.dump(props=true) BUYUK, gerekmedikce cagirma",
            "2. comp.types → comp.schema(type): alanlar, default, enum degerleri, format. Tahmin etme, semaya bak",
            "3. TOPLU kur: scene.apply(objects) — GO + components + props + children tek istekte (tek undo, atomik). Kucuk rotus: prop.set/transform.set. PREFAB: prefab.create(id, path) sahnedeki GO'yu prefab yapar (instance'a baglanir); prefab.instantiate(prefab, parent, pos) sahneye instance ekler (edit.batch icinde de); prefab.open/close prefab'in kendisini duzenler (tum instance'lar alir)",
            "4. view.device(name) → sim.run(frames, describe=true) → METIN ozete bak (render.describe: rect/text/click; rect merkezi = input.tap koordinati) → input.tap(x,y) → tekrar describe. Gorsel ton/suphe: screenshot=true ya da render.screenshot (koordinat = region.x + px * toScreen)",
            "5. Hata donerse yol+sebep verir ve hicbir sey uygulanmaz; duzelt, tekrar gonder. scene.diff = son dump'tan beri ne degisti",
            "6. scene.save YALNIZ kullanici isterse (her sey undo'lu kalir). scene.open/scene.new/prefab.open/project.open acik sahne kaydedilmemisse 'UNSAVED:' hatasi doner ve hicbir sey yapmaz: kullaniciya sor → scene.save (kaydet) ya da force=true (at). Korkutmadan once scene.tree'deki dirty alanina bakabilirsin"));
        r.Add("scene", Lines(
            "GO = id + parent + pos/rot/scale + components. Kok parent=0. Ayni parent'ta liste sirasi = cizim sirasi (sonraki ustte); SortingOrder onde gelir",
            "UI primitifi = LayoutBox: kutu (fill/border/radius/shadow) + metin (font,text,textSize,textAlign,textAlignV,textFill) + yerlesim. fitScreen=true → ekrani kaplar (arka plan)",
            "FONT = AD, asset degil: LayoutBox.font / TextSprite.FontName Assets/Fonts.asset'teki bir adi tutar ('' = varsayilan). Tablolar editorce otomatik acilir (Assets/Fonts.asset + her Assets/Modules/<ad>/Fonts.asset; yoksa bos yaratilir). Adlar fonts.list; yeni font fonts.register(path, name?) ile host tablosuna girer; modul tablosu host'u miras alir, ayni ad ezer (modul satirlari asset inspector'inda). Pak'a yalniz tablodaki fontlar girer",
            "RICH TEXT (LayoutBox.text, her zaman acik, TMPro alt kumesi): <b> <i> <u> <color=#RRGGBB[AA]|ad> <size=N|+N|-N|N%> <font=ad> <link=id> <noparse>. <b>=SDF kalinlik, <i>=egik; gercek bold yuzu <font=Bold>. Gecersiz tag oldugu gibi cizilir. Tek metin/tek loc anahtari: 'Accept our <link=privacy><u>Privacy Policy</u></link>'; tiklama = ayni GO'daki pointer component'i OnPointerClick icinde box.LinkAt(e) (id ya da null). Kullanici metnini RichText.Escape(s) ile sar",
            "LayoutBox konum: anchorMin/anchorMax/pivot (0..1, parent'a gore; '0.5 0.5' = ortala) + anchoredPos (mantiksal px ofset) + width/height. layout=Vertical|Horizontal cocuklari dizer (spacing, padding, alignChildren, justifyChildren)",
            "Flex cocuk alanlari (COCUKTA): grow, shrink, margin (l t r b), minWidth/maxWidth/minHeight/maxHeight, alignSelf=Auto|Start|Center|End|Stretch (flex icinde anchor DEGIL alignSelf=Stretch genisletir). Kaydirmali liste: kutuda overflowY=Scroll + ayni GO'ya ScrollView component'i (jest+atalet); scrollOffset animatable",
            "TextSprite = serbest metin, SpriteRenderer = gorsel (Sprite asset), ParticleSystem, MovieClip = doc'ta koreografi (intro/loop), CameraComponent opsiyonel (yoksa piksel-ortho fallback)",
            "'Buton' tipi YOK: gorunum LayoutBox, davranis KOD ister (Component alt sinifi OnPointerClick override; engine.api Component). Kod: asset.write(.cs) + code.build → comp.types yenilenir"));
        r.Add("values", Lines(
            "Renk '#RRGGBBAA' ('#RRGGBB' kabul, alpha ff eklenir); vektor 'x y' / 'x y z' / 'x y z w' (bosluk, virgul yok); bool true|false",
            "Enum TAM AD, buyuk-kucuk harfe duyarli (comp.schema values); asset = guid ya da Assets'e goreli yol (asset.list) — yol guid'e cevrilir",
            "GoRef 'goId'; CompRef 'goId:TypeName:ordinal' (Transform icin 'goId:Transform:0'). scene.apply/edit.batch icinde '$Ad' = ayni istekte tanimlanan GO'nun id'si ('$Ad:Transform:0')",
            "Ic ice yol: 'Tracks[0].Keys[1].Value' (index == eleman sayisi → sona ekle; null → sil/default'a don). Yanlis deger yazim aninda reddedilir"));
        r.Add("movieclip", Lines(
            "comp MovieClip props: Fps, FrameCount, Loop, PlayOnStart, 'Tracks[0].Target':'$Go:Transform:0', 'Tracks[0].Path':'Scale' (anim.props(type) yollar), 'Tracks[0].Keys[0].Value':'0 0 1 0', 'Tracks[0].Keys[0].Ease':'OutBack', 'Tracks[0].Keys[1].Frame':20, 'Tracks[0].Keys[1].Value':'1 1 1 0'"));
        r.Add("coordinates", Lines(
            "input.* MANTIKSAL nokta, sol-ust orijin, y asagi; view.device mantiksal boyutu belirler (view.get)",
            "render.describe = ekranin METIN ozeti (gorunur renderer'lar, rect 'x y w h' mantiksal, text, sprite, fill, click): hizli degerlendirme ve tiklama hedefi icin ONCE bunu kullan; rect merkezi dogrudan input.tap. pixels=N kaba renk izgarasi (ekran bos mu). Goruntu yalniz gorsel kalite/suphe icin",
            "Screenshot VARSAYILAN KUCULTULMUS (maxSize=1024, jpg): tiklama koordinati = region.x + px * toScreen (sonuctaki carpan; region yoksa 0). Detay gerekirse render.screenshot(region='x y w h') ile bolgeyi tam cozunurlukte al; scale=1, maxSize=0, format=png → piksel == koordinat",
            "input.tap donusu: hit (degdi mi), target (hiyerarsi yolu), handlers (kosan handler sayisi) — 0 ise davranis kodu yok demektir"));
        // Ice aktarilabilir bicimler: importer kaydindan CANLI (yeni [AssetImporter] otomatik girer; elle liste yok).
        var fmts = new List<string>();
        foreach (var e in ImportPipeline.Entries)
            fmts.Add(string.Join("/", e.Extensions) + " (" + e.Type.Name + (e.AssetType != null ? " " + e.AssetType : "") + ")");
        r.Add("assets", Lines(
            "ICE AKTARILABILIR bicimler (kayitli importer'lar, kesin kaynak asset.importers): " + string.Join(", ", fmts) + ". Bu listede olan bir uzanti icin 'importer yok' DEME",
            "Ekleme = dosyayi Assets/ altina kopyala (dosya sistemi; asset.write yalniz metin icindir). Editor watcher otomatik import eder; asset.reimport(path) zorlar; sonuc/uyari log.tail",
            "Container asset (psd/psb katmanlari, font glyph'leri): alt oge referansi 'Dosya.psb#id' (id = kalici kimlik, asset.info(path) listeler; 'n:ad' biciminde kararsiz id'den kacin). Referans degerinde guid ya da Assets'e goreli yol; '#id' eki ile alt oge",
            "asset.info(path): tip, guid, gorsel width/height, alt ogeler, atlas uyeligi. asset.list(filter,type): arama. asset.validate: kirik/kararsiz referans taramasi",
            "Import hatasi (desteklenmeyen alt bicim, bozuk dosya) log.tail'de gorunur: kullaniciya bildir, dosyayi kendin donusturmeye kalkma"));
        r.Add("rules", Lines(
            "TEMA KAPISI: her mutasyon (prop.set, scene.apply, edit.batch, comp.*, transform.set, go.setActive, scene.addGo/removeGo/rename/reparent, loc.bind/unbind, ui.dropAsset; rpc.describe 'mutates') calisilan temaya yazar. Tema default degilse istek theme:'<ad>' tasimali YA DA bu baglanti theme.set('<ad>') ile beyan etmis olmali; aksi halde HATA, hicbir sey degismez. Insan toolbar'dan temayi degistirirse beyan duser → yine hata. Ise baslarken rpc.guide 'state'e bak; tabani duzenleyeceksen ve tema acik ise kullaniciya sor, sonra theme.set('')",
            "Sahne/prefab YAML'ini ASLA dogrudan yazma (asset.write ile bile): scene.apply/edit.batch/prop.set kullan — undo'lu, dogrulanir, canli patch",
            "Sahneye dokunmadan ONCE yanindaki <sahne>.intent.md'yi oku (asset.read); is sonunda APPEND et (asset.write): prompt + yapi + invariantlar + dogrulama",
            "Asset bilgisi (yol/guid/tip, gorsel width/height, atlas uyeligi) icin asset.list/asset.info — ogrenmek icin kod yazip calistirma",
            "Animasyon siniri: doc'ta yazilabilen koreografi (intro, giris/cikis, idle loop) = MovieClip; Tween yalniz runtime'da hesaplanan tepkisel hareket ve authored degeri Start'ta ezemez",
            "Motor API'si icin engine.api(type, filter) (public tipler, uyeler, protected override kancalari) — proje disina cikma",
            "Play'de doc degisirse oyun sahnesi bayatlar; sim.run/input.* otomatik yeniden baslatir ('restarted'), screenshot 'stale' doner",
            "GIZLILIK: [Sensitive] alanlar (parola/token/client secret; plugin Config'leri, PlayerSettings.android parolalari) her yanitta '***' gorunur (agac, dosya icerigi, log). Bu degerleri OKUYAMAZSIN ve asla tahmin etme; gercek degeri kullanici editorde girer. '***' degerini geri yazma denemesi reddedilir — hassas alani degistirmen gerekmiyorsa dokunma"));
        r.Add("localization", Lines(
            "TUM oyun metinleri cevrilir. LayoutBox.text / TextSprite.Text'e prop.set ile metin yazinca otomatik baglanir: doc'ta 'text@loc: key' (key baglamdan: sahne.go_adi), kapsam tablosu Assets/Localization.asset (modul: Modules/<ad>/Localization.asset) kendiliginden olusur",
            "Baglanmamasi gereken metin (nick, skor): loc.unbind(go, comp, field) -> prop '' (bilerek kapali). Diger string alanlar icin loc.bind(go, comp, field, key?)",
            "Parametreli metin sablonu tabloda ('{0} coins'); kod key bilmeden comp.Format(args) cagirir. Play'de args gelmeden sablon gorunmez",
            "Ceviri dongusu: loc.languages -> loc.addLanguage(code) (yalniz katalog) -> loc.missing(lang) -> cevir -> loc.set(entries) (tek undo). Dil degistir: loc.setLanguage(lang) (edit + Play aninda). Denetim: loc.unused, loc.usages(key), loc.glyphCheck",
            "Tabloyu asset.write ile YAZMA: loc.set/loc.rename/loc.remove kullan (undo'lu, canli)"));
        r.Add("games", Lines(
            "PROJE = ORTAK Assets + N OYUN (ProjectSettings/Games/<ad>/PlayerSettings.asset + Plugins/<Ad>.asset: her oyunun TAM ayarlari — kimlik, sahneler, startScene, temalar, plugin config). Oyun kodu ortak ve hep derlenir; pak/trim/plugin canliligi o oyunun sahne kumesinden turer. Konvansiyon: oyuna ozel asset Assets/Games/<Ad>/, ortak Assets/Shared/",
            "game.list -> game.set(name): CALISILAN oyun (toolbar combosu) = Play/Build/pak baglami; gecis o oyunun son sahnesini/temasini/cihazini geri getirir ve oyun kodunu taze ALC ile yukler (statikler sifir). Bir oyunu duzenlemek icin gecis GEREKMEZ: Player Settings penceresi oyun sekmeleriyle her oyunu duzenler",
            "game.create(name, copyFrom) yeni oyun (ortak alanlar icin mevcut oyundan kopya); game.remove(name). Build ciktisi Build/<game>/; player.build calisilan oyunu kurar; ayni projede ikinci editor: DigitoyEditor.exe <proje> --game <ad>"));
        r.Add("themes", Lines(
            "TEMA (xmas, kurban...): sahne/prefab DOSYASININ ICINDE isimli override katmani (themes: {xmas: {overrides, removed, added}}); sahne kopyasi/kod dali YOK. Tema listesi OYUNA ait (PlayerSettings.themes; adlar oyunlar arasi ortak sozluk). Build tek temayla piser (PlayerSettings.activeTheme ya da asset.buildPak/player.build theme=...), diger temalar ve asset'leri pak'a girmez. Runtime tema bilmez",
            "Akis: theme.add(name) -> theme.set(name) (calisilan tema = dil gibi: edit + Play; undo yigini temizlenir; bu baglanti icin BEYAN) -> normal prop.set/comp.add/scene.addGo/scene.removeGo: HEPSI o temanin katmanina yazilir, taban dokunulmaz -> scene.save. theme.set('') tabana doner. Beyan etmeden tema acikken mutasyon = hata (tema kapisi); tek istekte theme:'ad' arg'i da gecer",
            "Ortak parca (avatar cercevesi) temalanacaksa katmani EN IC prefab'a koy (prefab stage'de theme.set + duzenle): tum tuketiciler (masa, leaderboard, sahne) otomatik alir. Tema modunda kardes sirasi degistirilemez; theme.check yetim override'lari listeler; theme.setBuild(name) build temasi"));
        r.Add("editorUi", Lines(
            "Editor GUI otomasyonu: ui.panels (panel/tab rect'leri, pencere boyutu) -> ui.screenshot (tum pencere, piksel == koordinat) -> ui.click/ui.drag/ui.scroll/ui.type/ui.key. Jest frame'e yayilir; sonraki komut jest bitince kosar (ui.wait gerekmez, animasyon icin istege bagli)",
            "Menu ve ayri pencereler: ui.menu() yollari listeler, ui.menu('Project/Player Settings') ogeyi calistirir (utility pencere acilir). ui.windows acik utility/detached pencereleri verir; ui.screenshot/click/type/key/drag/scroll window='Player Settings' ile o pencereyi hedefler (koordinat o pencerenin mantiksal px'i).",
            "Asset'i Inspector alanina birakmak icin KOORDINATSIZ yol: ui.fields (secili nesnenin cizili alanlari: yol/tip/rect) -> ui.dropAsset(asset, field). Liste basligi ('a:sources') sona ekler, eleman ('a:sources[0]') atar; asset 'x.psb#id' alt oge olabilir. ui.drag ile panelden panele surukleme KULLANMA (kirilgan).",
            "Combo: ui.click ile ac, acilan listedeki ogeye ui.click (popup ayri pencere ama koordinat ana pencere uzayinda; satir 20px). Metin: alana ui.click, sonra ui.type; '\\n' Enter. Ctrl+Z: ui.key('z','ctrl')",
            "Fare/klavye simulasyonu icin dis araclar (SendInput vb.) kullanma; insan girdisi gorulurse otomasyon kuyrugu atilir"));
        return r;
    }

    // --- gozlem ---

    // Doc hiyerarsisi: parent id → cocuklar (doc sirasi = kardes sirasi). Her cagri tek gecis; buyuk sahnede
    // tam dump yerine kademeli gezinti (scene.tree root/depth) icin.
    static Dictionary<int, List<SceneDoc.GoDoc>> ChildIndex(SceneDoc doc)
    {
        var idx = new Dictionary<int, List<SceneDoc.GoDoc>>();
        foreach (var g in doc.Objects)
        {
            if (!idx.TryGetValue(g.Parent, out var list))
                idx[g.Parent] = list = new List<SceneDoc.GoDoc>();
            list.Add(g);
        }
        return idx;
    }

    // "root": doc id ya da 'A/B/C' yolu (koklerden iner; tek segment ise once kok adi, yoksa tum agacta ad).
    static SceneDoc.GoDoc ResolveDocGo(RpcContext ctx, string key, Dictionary<int, List<SceneDoc.GoDoc>> idx)
    {
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            return ctx.Scene.FindGo(id);
        var segs = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0)
            return null;
        SceneDoc.GoDoc cur = null;
        if (idx.TryGetValue(0, out var roots))
            foreach (var r in roots)
                if (r.Name == segs[0]) { cur = r; break; }
        if (cur == null && segs.Length == 1)
        {
            foreach (var g in ctx.Scene.Doc.Objects)
                if (g.Name == segs[0])
                    return g;
            return null;
        }
        for (int k = 1; cur != null && k < segs.Length; k++)
        {
            SceneDoc.GoDoc next = null;
            if (idx.TryGetValue(cur.Id, out var kids))
                foreach (var c in kids)
                    if (c.Name == segs[k]) { next = c; break; }
            cur = next;
        }
        return cur;
    }

    static SceneDoc.GoDoc RequireDocGo(RpcContext ctx, string key, Dictionary<int, List<SceneDoc.GoDoc>> idx)
        => ResolveDocGo(ctx, key, idx) ?? throw new RpcError($"GO yok: '{key}' (id ya da 'A/B' yolu; scene.tree ile gor)");

    [RpcCommand("scene.tree", "KOMPAKT hiyerarsi (ONCE BUNU CAGIR): satir basina '{id} {girinti}{ad} [inactive] {prefab} (component tipleri)'. root: id ya da 'A/B' yolu (bos = tum sahne); depth: en fazla derinlik (0 = sinirsiz; 1 = yalniz root'un kendisi/kokler); comps=false → tipler gizli. Satirin sonunda '+N' = depth nedeniyle gizlenen alt eleman sayisi")]
    static DocNode SceneTree(RpcContext ctx, string root = "", int depth = 0, bool comps = true)
    {
        var doc = ctx.Scene.Doc;
        var idx = ChildIndex(doc);
        var lines = DocNode.Seq();
        int count = 0;
        int Descendants(SceneDoc.GoDoc g)
        {
            int n = 0;
            if (idx.TryGetValue(g.Id, out var kids))
                foreach (var c in kids) n += 1 + Descendants(c);
            return n;
        }
        void Walk(SceneDoc.GoDoc g, int d)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(g.Id.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(' ', d * 2).Append(g.Name);
            if (!g.Active) sb.Append(" [inactive]");
            if (g.IsPrefabRoot)
                sb.Append(" {").Append(Path.GetFileNameWithoutExtension(ctx.Assets?.ResolvePath(g.PrefabGuid) ?? g.PrefabGuid)).Append('}');
            if (comps && g.Components.Count > 0)
            {
                sb.Append(" (");
                for (int i = 0; i < g.Components.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(g.Components[i].Type);
                    if (!g.Components[i].Enabled) sb.Append("[off]");
                }
                sb.Append(')');
            }
            bool cut = depth > 0 && d + 1 >= depth;
            if (cut)
            {
                int hidden = Descendants(g);
                if (hidden > 0) sb.Append(" +").Append(hidden.ToString(CultureInfo.InvariantCulture));
            }
            lines.Items.Add(DocNode.Scal(sb.ToString()));
            count++;
            if (cut) return;
            if (idx.TryGetValue(g.Id, out var kids))
                foreach (var c in kids) Walk(c, d + 1);
        }
        if (root.Length > 0)
            Walk(RequireDocGo(ctx, root, idx), 0);
        else if (idx.TryGetValue(0, out var roots))
            foreach (var r in roots) Walk(r, 0);
        var res = DocNode.Map();
        res.Add("scene", DocNode.Scal(doc.Name));
        res.Add("lines", lines);
        res.Add("count", DocNode.Scal(count.ToString(CultureInfo.InvariantCulture)));
        res.Add("total", DocNode.Scal(doc.Objects.Count.ToString(CultureInfo.InvariantCulture)));
        return res;
    }

    [RpcCommand("scene.dump", "Acik sahnenin doc dokumu (id/parent/ad/transform/component'ler). go: yalniz bu GO'nun alt agaci (id ya da 'A/B' yolu; bos = tum sahne); props=false → alan degerleri atlanir (yalniz tipler). Tam dump (go bos) diff baseline'ini sifirlar. BUYUK sahnede once scene.tree")]
    static DocNode SceneDump(RpcContext ctx, string go = "", bool props = true)
    {
        var root = DocNode.Map();
        root.Add("name", DocNode.Scal(ctx.Scene.Doc.Name));
        root.Add("path", DocNode.Scal(ctx.Scene.Path ?? ""));
        root.Add("dirty", DocNode.Scal(ctx.Scene.Dirty ? "true" : "false"));
        var objects = DocNode.Seq();
        if (go.Length > 0)
        {
            var idx = ChildIndex(ctx.Scene.Doc);
            void Walk(SceneDoc.GoDoc g)
            {
                objects.Items.Add(DumpGo(g, props));
                if (idx.TryGetValue(g.Id, out var kids))
                    foreach (var c in kids) Walk(c);
            }
            Walk(RequireDocGo(ctx, go, idx));
            root.Add("objects", objects);
            return root;
        }
        _diffBaseline.Clear();
        foreach (var g in ctx.Scene.Doc.Objects)
        {
            var og = DumpGo(g);
            _diffBaseline[g.Id] = og.Clone();
            objects.Items.Add(props ? og : DumpGo(g, false));
        }
        root.Add("objects", objects);
        return root;
    }

    static DocNode DumpGo(SceneDoc.GoDoc g, bool props = true)
    {
        var og = DocNode.Map();
        og.Add("id", DocNode.Scal(g.Id.ToString(CultureInfo.InvariantCulture)));
        og.Add("parent", DocNode.Scal(g.Parent.ToString(CultureInfo.InvariantCulture)));
        og.Add("name", DocNode.Scal(g.Name));
        if (!g.Active)
            og.Add("active", DocNode.Scal("false"));
        og.Add("pos", DocNode.Scal(V3(g.Pos)));
        og.Add("rot", DocNode.Scal(V3(g.Rot)));
        og.Add("scale", DocNode.Scal(V3(g.Scale)));
        if (g.PrefabGuid != null)
            og.Add("prefab", DocNode.Scal(g.PrefabGuid));
        if (g.Components.Count > 0)
        {
            var comps = DocNode.Seq();
            foreach (var cd in g.Components)
            {
                var oc = DocNode.Map();
                oc.Add("type", DocNode.Scal(cd.Type));
                if (!cd.Enabled)
                    oc.Add("enabled", DocNode.Scal("false"));
                if (props && cd.Props.Count > 0)
                {
                    var pm = DocNode.Map();
                    foreach (var kv in cd.Props)
                        pm.Add(kv.Key, kv.Value.Clone());
                    oc.Add("props", pm);
                }
                comps.Items.Add(oc);
            }
            og.Add("components", comps);
        }
        return og;
    }

    // Son dump/diff'ten beri degisenler: baseline id->dump klonu (tam dokum yerine
    // kompakt geri bildirim — AI her komut sonrasi ucuza "ne degisti" gorur).
    static readonly Dictionary<int, DocNode> _diffBaseline = new();

    [RpcCommand("scene.diff", "Son scene.dump/diff'ten beri eklenen/silinen/degisen GO'lar")]
    static DocNode SceneDiff(RpcContext ctx)
    {
        var added = DocNode.Seq();
        var changed = DocNode.Seq();
        var removed = DocNode.Seq();
        var seen = new HashSet<int>();
        foreach (var g in ctx.Scene.Doc.Objects)
        {
            seen.Add(g.Id);
            var now = DumpGo(g);
            if (!_diffBaseline.TryGetValue(g.Id, out var was))
                added.Items.Add(now.Clone());
            else if (!DocNode.Equal(now, was))
                changed.Items.Add(now.Clone());
            _diffBaseline[g.Id] = now;
        }
        var gone = new List<int>();
        foreach (var id in _diffBaseline.Keys)
            if (!seen.Contains(id))
                gone.Add(id);
        gone.Sort();
        foreach (var id in gone)
        {
            removed.Items.Add(DocNode.Scal(id.ToString(CultureInfo.InvariantCulture)));
            _diffBaseline.Remove(id);
        }
        var root = DocNode.Map();
        root.Add("added", added);
        root.Add("changed", changed);
        root.Add("removed", removed);
        return root;
    }

    [RpcCommand("scene.dumpLive", "CANLI sahne dokumu (runtime degerler; which: edit|play). go: yalniz bu GO'nun alt agaci (hiyerarsi yolu 'A/B' ya da tekil ad; bos = tum sahne)")]
    static DocNode DumpLive(RpcContext ctx, string which = "edit", string go = "")
    {
        var scene = which switch
        {
            "edit" => ctx.Scene.LiveScene,
            "play" => PlayMode.PlayScene,
            _ => throw new RpcError("which 'edit' ya da 'play' olmali"),
        };
        if (scene == null)
            throw new RpcError($"canli sahne yok: {which}" + (which == "play" ? " (play.start ile baslat)" : ""));
        var root = DocNode.Map();
        root.Add("name", DocNode.Scal(scene.Name));
        var objects = DocNode.Seq();
        if (go.Length > 0)
        {
            var target = FindLiveByPath(scene, go)
                ?? throw new RpcError($"canli sahnede GO yok: '{go}' (yol 'A/B/C' ya da tekil ad)");
            DumpLiveGo(target, 0, objects);
        }
        else
            for (int i = 0; i < scene.RootCount; i++)
                DumpLiveGo(scene.GetRoot(i), 0, objects);
        root.Add("objects", objects);
        return root;
    }

    // Faz 0 regresyon araci (docs/registry-removal.md): player'daki Build/scene-dump.flag ciktisiyla birebir karsilastirilir.
    [RpcCommand("scene.dumpFields", "Canli sahnenin katalog semasindaki TUM alanlarini metne doker (SceneDump; which: edit|play)")]
    static DocNode DumpFields(RpcContext ctx, string which = "edit")
    {
        var scene = which switch
        {
            "edit" => ctx.Scene.LiveScene,
            "play" => PlayMode.PlayScene,
            _ => throw new RpcError("which 'edit' ya da 'play' olmali"),
        };
        if (scene == null)
            throw new RpcError($"canli sahne yok: {which}");
        var r = DocNode.Map();
        r.Add("text", DocNode.Scal(DigitoyEngine.SceneDump.Write(scene, ctx.Catalog)));
        return r;
    }

    static void DumpLiveGo(GameObject go, int depth, DocNode into)
    {
        var og = DocNode.Map();
        og.Add("name", DocNode.Scal(go.name));
        og.Add("level", DocNode.Scal(depth.ToString(CultureInfo.InvariantCulture))); // hiyerarsi derinligi (kamera depth'i degil)
        if (!go.activeSelf)
            og.Add("active", DocNode.Scal("false"));
        og.Add("pos", DocNode.Scal(V3(go.transform.localPosition)));
        og.Add("rot", DocNode.Scal(V3(go.transform.localEulerAngles)));
        og.Add("scale", DocNode.Scal(V3(go.transform.localScale)));
        var comps = DocNode.Seq();
        for (int i = 0; i < go.ComponentCount; i++)
        {
            var c = go.ComponentAt(i);
            if (c != null && c is not Transform)
                comps.Items.Add(DocNode.Scal(c.GetType().Name));
        }
        if (comps.Items.Count > 0)
            og.Add("components", comps);
        into.Items.Add(og);
        for (var t = go.transform.FirstChild; t != null; t = t.NextSibling)
            if (t.gameObject != null)
                DumpLiveGo(t.gameObject, depth + 1, into);
    }

    [RpcCommand("comp.schema", "Component tipinin serilesen alan semasi (ic ice nesne/liste elemani alanlari 'fields' altinda; ref biciminde 'format')")]
    static DocNode CompSchema(RpcContext ctx, string type)
    {
        var entry = ctx.Catalog.Find(type)
            ?? throw new RpcError($"bilinmeyen component tipi: '{type}'");
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(entry.Name));
        root.Add("fields", SchemaFields(entry.Schema));
        root.Add("hint", DocNode.Scal("prop.set 'prop' ic ice yol alir: Tracks[0].Keys[1].Value (index == eleman sayisi = sona ekle). "
            + "Animatable yol listesi: anim.props(type). 'default' = yazilmazsa gecerli deger. Kavramlar/bicimler: rpc.guide."));
        return root;
    }

    static DocNode SchemaFields(SerializedType.FieldSchema[] schema)
    {
        var fields = DocNode.Seq();
        foreach (var f in schema)
        {
            var fd = DocNode.Map();
            fd.Add("name", DocNode.Scal(f.Name));
            fd.Add("kind", DocNode.Scal(f.Kind.ToString()));
            var leaf = f.Kind;
            if (f.Kind == SerializedType.Kind.List)
            {
                fd.Add("elementKind", DocNode.Scal(f.ElementKind.ToString()));
                leaf = f.ElementKind;
            }
            var leafType = f.Kind == SerializedType.Kind.List ? f.ElementType : f.FieldType;
            if (leaf == SerializedType.Kind.Enum && leafType != null)
                fd.Add("values", DocNode.Scal(string.Join(" ", Enum.GetNames(leafType))));
            string format = f.FontName
                ? "fontName: Assets/Fonts.asset'teki ad (fonts.list); \"\" = varsayilan. Font asset'i dogrudan verilmez (fonts.register ile tabloya ekle)"
                : RefFormat(leaf, leafType);
            if (format != null)
                fd.Add("format", DocNode.Scal(format));
            if (f.Kind != SerializedType.Kind.List && f.Kind != SerializedType.Kind.Object)
            {
                var d = InspectorPanel.CreateDefaultFieldNode(f);
                if (d?.Scalar != null)
                    fd.Add("default", d);
            }
            if (f.Nested != null && f.Nested.Length > 0)
                fd.Add("fields", SchemaFields(f.Nested));
            fields.Items.Add(fd);
        }
        return fields;
    }

    // Ajanin tahmin etmek zorunda kalmamasi icin skaler bicim sozlesmesi (doc kanonik).
    static string RefFormat(SerializedType.Kind kind, Type type) => kind switch
    {
        SerializedType.Kind.CompRef => "\"goId:TypeName:ordinal\" — ayni GO'daki ayni tipten kacinci (0 tabanli); Transform icin \"goId:Transform:0\""
            + (type != null && type != typeof(Component) ? $"; tip: {type.Name}" : ""),
        SerializedType.Kind.GoRef => "\"goId\"",
        SerializedType.Kind.Asset => "asset guid ya da Assets'e goreli yol (asset.list)" + (type != null ? $"; tip: {type.Name}" : ""),
        SerializedType.Kind.Vec2 => "\"x y\"",
        SerializedType.Kind.Vec3 => "\"x y z\"",
        SerializedType.Kind.Vec4 => "\"x y z w\"",
        SerializedType.Kind.Color => "\"#RRGGBBAA\" hex (doc kanonik; baska bicim beyaza duser)",
        SerializedType.Kind.Bool => "true|false",
        _ => null,
    };

    [RpcCommand("anim.props", "Tipin animatable property yollari (Tween path / MovieClip ClipTrack.Path): Transform dahil; kind=Float|Vec3|Color|Trigger... ve kanal sayisi")]
    static DocNode AnimProps(RpcContext ctx, string type)
    {
        Type t = type == "Transform" ? typeof(Transform) : ctx.Catalog.Find(type)?.Type;
        if (t == null)
            throw new RpcError($"bilinmeyen tip: '{type}' (comp.types ya da Transform)");
        var seq = DocNode.Seq();
        void Add(AnimProperty p)
        {
            var m = DocNode.Map();
            m.Add("path", DocNode.Scal(p.Path));
            m.Add("kind", DocNode.Scal(p.Kind.ToString()));
            m.Add("channels", DocNode.Scal(Channels(p.Kind).ToString(CultureInfo.InvariantCulture)));
            if (p.Kind == AnimKind.Enum && p.ValueType != null)
                m.Add("values", DocNode.Scal(string.Join(" ", Enum.GetNames(p.ValueType))));
            if (p.Kind == AnimKind.Ref && p.ValueType != null)
                m.Add("refType", DocNode.Scal(p.ValueType.Name));
            seq.Items.Add(m);
        }
        foreach (var p in AnimRegistry.PropsOf(ctx.Catalog, t))
            Add(p);
        if (t != typeof(Transform))
            Add(AnimRegistry.Enabled);
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(t.Name));
        root.Add("props", seq);
        root.Add("hint", DocNode.Scal("ClipKey.Value = \"x y z w\" (Float/Int/Bool/Enum: x; Vec2: x y; Vec3: x y z; Color: r g b a 0..255); "
            + "Ref turleri ClipKey.Ref; Trigger'da deger yok. ClipTrack.Mask bit: 1=X 2=Y 4=Z 8=W (15=hepsi). Frame = Fps * saniye."));
        return root;
    }

    static int Channels(AnimKind k) => k switch
    {
        AnimKind.Vec2 => 2,
        AnimKind.Vec3 => 3,
        AnimKind.Vec4 or AnimKind.Color => 4,
        AnimKind.Trigger => 0,
        _ => 1,
    };

    [RpcCommand("comp.types", "Katalogdaki tum component tipleri")]
    static DocNode CompTypes(RpcContext ctx)
    {
        var seq = DocNode.Seq();
        var names = new System.Collections.Generic.List<string>();
        foreach (var e in ctx.Catalog.Entries)
            names.Add(e.Name);
        names.Sort(StringComparer.Ordinal);
        foreach (var n in names)
            seq.Items.Add(DocNode.Scal(n));
        var root = DocNode.Map();
        root.Add("types", seq);
        return root;
    }

    // Motor API'si (oyun kodu yazarken): engine assembly'sinin PUBLIC yuzeyi reflection'la.
    // type bos = tip listesi (ad + kind + base); dolu = o tipin uyeleri (C# imzasi). Component
    // alt tiplerinde protected virtual lifecycle/pointer kancalari da listelenir (override edilecekler).
    [RpcCommand("engine.api", "Motor API'si: type bos = public tip listesi (filter ad icerir); type dolu = uyeler (imza + protected override kancalari)")]
    static DocNode EngineApi(string type = "", string filter = "")
    {
        var asm = typeof(GameObject).Assembly;
        var root = DocNode.Map();
        if (type.Length == 0)
        {
            var seq = DocNode.Seq();
            var types = new List<Type>();
            foreach (var t in asm.GetExportedTypes())
                if (!t.IsNested && (filter.Length == 0 || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    types.Add(t);
            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var t in types)
            {
                var m = DocNode.Map();
                m.Add("name", DocNode.Scal(t.Name));
                m.Add("kind", DocNode.Scal(t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : t.IsAbstract && t.IsSealed ? "static" : "class"));
                if (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && t.BaseType != typeof(Enum))
                    m.Add("base", DocNode.Scal(t.BaseType.Name));
                seq.Items.Add(m);
            }
            root.Add("types", seq);
            return root;
        }
        Type target = null;
        foreach (var t in asm.GetExportedTypes())
            if (string.Equals(t.Name, type, StringComparison.Ordinal)) { target = t; break; }
        if (target == null)
            throw new RpcError($"motor tipi yok: '{type}' (engine.api ile listele)");
        root.Add("type", DocNode.Scal(target.Name));
        if (target.BaseType != null && target.BaseType != typeof(object))
            root.Add("base", DocNode.Scal(target.BaseType.Name));
        var members = DocNode.Seq();
        if (target.IsEnum)
        {
            root.Add("values", DocNode.Scal(string.Join(" ", Enum.GetNames(target))));
            return root;
        }
        const System.Reflection.BindingFlags Pub = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
        // Yuru: tip + ata zinciri (engine'de tanimli olanlar), tekrar eden imzalar atlanir.
        var seen = new HashSet<string>();
        for (var t = target; t != null && t.Assembly == asm; t = t.BaseType)
        {
            foreach (var f in t.GetFields(Pub))
                Add(members, seen, $"{(f.IsStatic ? "static " : "")}{Sig(f.FieldType)} {f.Name}", "field");
            foreach (var p in t.GetProperties(Pub))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                string acc = (p.CanRead ? "get;" : "") + (p.CanWrite && p.SetMethod.IsPublic ? " set;" : "");
                Add(members, seen, $"{(p.GetMethod?.IsStatic == true ? "static " : "")}{Sig(p.PropertyType)} {p.Name} {{ {acc} }}", "property");
            }
            foreach (var mi in t.GetMethods(Pub | System.Reflection.BindingFlags.NonPublic))
            {
                if (mi.IsSpecialName) continue;
                bool hook = mi.IsFamily || mi.IsFamilyOrAssembly; // protected (internal) virtual kanca
                if (!mi.IsPublic && !(hook && mi.IsVirtual)) continue;
                if (mi.DeclaringType == typeof(object)) continue;
                string ps = string.Join(", ", Array.ConvertAll(mi.GetParameters(), q => $"{(q.ParameterType.IsByRef ? (q.IsIn ? "in " : q.IsOut ? "out " : "ref ") : "")}{Sig(q.ParameterType.IsByRef ? q.ParameterType.GetElementType() : q.ParameterType)} {q.Name}{(q.HasDefaultValue ? " = " + (q.DefaultValue is null ? "null" : Convert.ToString(q.DefaultValue, CultureInfo.InvariantCulture)) : "")}"));
                string pre = hook ? "protected override " : (mi.IsStatic ? "static " : "") + (mi.IsVirtual && !mi.IsFinal ? "virtual " : "");
                Add(members, seen, $"{pre}{Sig(mi.ReturnType)} {mi.Name}({ps})", hook ? "hook" : (mi.IsStatic && mi.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false) ? "extension" : "method"));
            }
        }
        // Extension metodlar (Tween vb.): bu tipi ilk parametre alan public static'ler.
        foreach (var t in asm.GetExportedTypes())
        {
            if (!(t.IsAbstract && t.IsSealed)) continue;
            foreach (var mi in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (!mi.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)) continue;
                var ps0 = mi.GetParameters();
                if (ps0.Length == 0 || !ps0[0].ParameterType.IsAssignableFrom(target)) continue;
                string ps = string.Join(", ", Array.ConvertAll(ps0[1..], q => $"{Sig(q.ParameterType.IsByRef ? q.ParameterType.GetElementType() : q.ParameterType)} {q.Name}{(q.HasDefaultValue ? " = " + (q.DefaultValue is null ? "null" : Convert.ToString(q.DefaultValue, CultureInfo.InvariantCulture)) : "")}"));
                Add(members, seen, $"{Sig(mi.ReturnType)} {mi.Name}({ps})  // extension ({t.Name})", "extension");
            }
        }
        root.Add("members", members);
        return root;

        static void Add(DocNode seq, HashSet<string> seen, string sig, string kind)
        {
            if (!seen.Add(sig)) return;
            var m = DocNode.Map();
            m.Add("kind", DocNode.Scal(kind));
            m.Add("sig", DocNode.Scal(sig));
            seq.Items.Add(m);
        }

        static string Sig(Type t)
        {
            if (t == typeof(void)) return "void";
            if (t == typeof(float)) return "float";
            if (t == typeof(int)) return "int";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(object)) return "object";
            if (t.IsArray) return Sig(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                string n = t.Name[..t.Name.IndexOf('`')];
                return n + "<" + string.Join(", ", Array.ConvertAll(t.GetGenericArguments(), Sig)) + ">";
            }
            return t.Name;
        }
    }

    // --- yapisal mutasyonlar ---

    [RpcCommand("scene.addGo", "Yeni GameObject ekler, id doner. insertBefore = onune gelecegi kardes id (0 = sona). Paralel cagrilarda varis sirasi belirsizdir: sira onemliyse ardisik cagir veya insertBefore ver", Mutates = true)]
    static DocNode AddGo(RpcContext ctx, int parent = 0, string name = "", int insertBefore = 0)
    {
        if (parent != 0)
            RequireGo(ctx, parent); // parent yoksa erken ve net hata
        if (insertBefore != 0 && RequireGo(ctx, insertBefore).Parent != parent)
            throw new RpcError($"insertBefore={insertBefore} hedef parent'in ({parent}) cocugu degil");
        var g = ctx.Scene.AddGameObject(parent, insertBefore);
        if (name.Length > 0)
            ctx.Scene.SetName(g, name);
        var r = DocNode.Map();
        r.Add("id", DocNode.Scal(g.Id.ToString(CultureInfo.InvariantCulture)));
        r.Add("name", DocNode.Scal(g.Name));
        return r;
    }

    [RpcCommand("scene.removeGo", "GameObject'i (alt agaciyla) siler", Mutates = true)]
    static DocNode RemoveGo(RpcContext ctx, int id)
    {
        ctx.Scene.RemoveGameObject(RequireGo(ctx, id));
        return null;
    }

    [RpcCommand("scene.rename", "GameObject'i yeniden adlandirir", Mutates = true)]
    static DocNode Rename(RpcContext ctx, int id, string name)
    {
        ctx.Scene.SetName(RequireGo(ctx, id), name);
        return null;
    }

    [RpcCommand("scene.reparent", "GameObject'i parent'a tasir (0 = kok) ve/veya kardes sirasini degistirir: insertBefore = onune gelecegi kardes id (0 = sona). Ayni parent + insertBefore = yalniz siralama. Liste sirasi = hiyerarsi = ayni SortingOrder'da cizim sirasi (sonraki ustte)", Mutates = true)]
    static DocNode ReparentGo(RpcContext ctx, int id, int parent, int insertBefore = -1)
    {
        var g = RequireGo(ctx, id);
        if (parent != 0)
            RequireGo(ctx, parent);
        if (insertBefore < 0)
            ctx.Scene.Reparent(g, parent);
        else
        {
            if (insertBefore != 0 && RequireGo(ctx, insertBefore).Parent != parent)
                throw new RpcError($"insertBefore={insertBefore} hedef parent'in ({parent}) cocugu degil");
            ctx.Scene.MoveGameObject(g, parent, insertBefore);
        }
        return null;
    }

    [RpcCommand("comp.add", "Component ekler, index doner", Mutates = true)]
    static DocNode CompAdd(RpcContext ctx, int go, string type)
    {
        var g = RequireGo(ctx, go);
        if (ctx.Catalog.Find(type) == null)
            throw new RpcError($"bilinmeyen component tipi: '{type}' (comp.types ile listele)");
        var cd = ctx.Scene.AddComponent(g, type);
        var r = DocNode.Map();
        r.Add("index", DocNode.Scal(g.Components.IndexOf(cd).ToString(CultureInfo.InvariantCulture)));
        return r;
    }

    [RpcCommand("comp.remove", "Component'i index'iyle kaldirir", Mutates = true)]
    static DocNode CompRemove(RpcContext ctx, int go, int comp)
    {
        var g = RequireGo(ctx, go);
        ctx.Scene.RemoveComponent(g, RequireComp(g, comp));
        return null;
    }

    // --- property mutasyonlari ---

    [RpcCommand("prop.set", "Component alanina deger yazar (kanonik doc formati; null = default'a don). prop ic ice yol alabilir: 'Tracks[0].Keys[1].Value' — liste index'i eleman sayisina esitse SONA EKLER, ic ice yolda null liste elemanini SILER", Mutates = true)]
    static DocNode PropSet(RpcContext ctx, int go, int comp, string prop, DocNode value = null)
    {
        var g = RequireGo(ctx, go);
        var cd = RequireComp(g, comp);
        SetPropPath(ctx, g, cd, prop, value);
        ctx.Scene.History.SealTop(); // her RPC yazimi ayri undo girisi (jest yok)
        return null;
    }

    // prop.set cekirdegi (scene.apply de kullanir): sema dogrulamasi + ic ice yol + SetProp.
    static void SetPropPath(RpcContext ctx, SceneDoc.GoDoc g, SceneDoc.CompDoc cd, string prop, DocNode value)
    {
        // [Sensitive] alana maske degerini geri yazma (yolun son parcasi alan adi)
        if (value?.Scalar != null)
        {
            int dot = prop.LastIndexOf('.');
            string leaf = dot >= 0 ? prop.Substring(dot + 1) : prop;
            int br = leaf.IndexOf('[');
            RpcRedact.GuardInput(br >= 0 ? leaf.Substring(0, br) : leaf, value.Scalar);
        }
        var entry = ctx.Catalog.Find(cd.Type)
            ?? throw new RpcError($"component tipi katalogda yok: '{cd.Type}'");
        int cut = prop.IndexOfAny(new[] { '.', '[' });
        string rootName = cut < 0 ? prop : prop[..cut];
        var f = SerializedType.Find(entry.Schema, rootName)
            ?? throw new RpcError($"'{cd.Type}' tipinde alan yok: '{rootName}' (comp.schema ile listele)");
        var old = EditorScene.FindProp(cd, f);
        DocNode next = value;
        if (cut >= 0)
        {
            // Ic ice yol: kok dugumu klonla, yolu izleyerek yaprağı degistir, kokun tamamini yaz
            // (tek PropOp = tek undo; canli patch kok alani yeniden kurar → OnValidate/Rebuild).
            next = (old ?? InspectorPanel.CreateDefaultFieldNode(f)).Clone();
            SetNested(ctx, next, f.Kind, f.FieldType, f.ElementKind, f.ElementType, f.Nested,
                prop, cut, value, prop);
        }
        else if (value != null)
            Validate(ctx, value, f.Kind, f.FieldType, f.ElementKind, f.ElementType, f.Nested, prop);
        ctx.Scene.SetProp(g, cd, f, old?.Clone(), next);
    }

    // Yazim ani sema dogrulamasi: yanlis enum/sayi/renk/vektor/ref doc'a SESSIZCE girmesin
    // (runtime'da beyaza/sifira/default'a duser, AI hatayi screenshot'ta bile goremez).
    // Kanonik olmayan ama anlasilir bicimler normalize edilir: '#rrggbb' → '#rrggbbff', asset yolu → guid.
    static void Validate(RpcContext ctx, DocNode n, SerializedType.Kind kind, Type type,
        SerializedType.Kind elemKind, Type elemType, SerializedType.FieldSchema[] nested, string path)
    {
        if (n == null || n.IsNull)
            return;
        switch (kind)
        {
            case SerializedType.Kind.List:
                if (n.Scalar == "") return; // bos liste kanonik "" olabilir
                if (!n.IsSeq)
                    throw new RpcError($"'{path}': liste bekleniyor (JSON dizisi), gelen: {Describe(n)}");
                for (int i = 0; i < n.Items.Count; i++)
                    Validate(ctx, n.Items[i], elemKind, elemType, default, null, nested, $"{path}[{i}]");
                return;
            case SerializedType.Kind.Object:
                if (!n.IsMap)
                    throw new RpcError($"'{path}': ic ice nesne bekleniyor (JSON nesnesi; alanlar: {FieldNames(nested)}), gelen: {Describe(n)}");
                foreach (var kv in n.Fields)
                {
                    var f = nested != null ? SerializedType.Find(nested, kv.Key) : null;
                    if (f == null)
                        throw new RpcError($"'{path}.{kv.Key}': '{type?.Name}' tipinde boyle alan yok (alanlar: {FieldNames(nested)})");
                    Validate(ctx, kv.Value, f.Kind, f.FieldType, f.ElementKind, f.ElementType, f.Nested, path + "." + kv.Key);
                }
                return;
        }
        if (n.Scalar == null)
            throw new RpcError($"'{path}': skaler bekleniyor ({kind}), gelen: {Describe(n)}");
        string s = n.Scalar;
        switch (kind)
        {
            case SerializedType.Kind.Float:
                if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw new RpcError($"'{path}': sayi bekleniyor, gelen '{s}'");
                return;
            case SerializedType.Kind.Int:
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new RpcError($"'{path}': tam sayi bekleniyor, gelen '{s}'");
                return;
            case SerializedType.Kind.Bool:
                if (s != "true" && s != "false")
                    throw new RpcError($"'{path}': true|false bekleniyor, gelen '{s}'");
                return;
            case SerializedType.Kind.Enum:
                if (type == null || !type.IsEnum)
                    return;
                var names = Enum.GetNames(type);
                if (Array.IndexOf(names, s) >= 0)
                    return;
                foreach (var nm in names)
                    if (string.Equals(nm, s, StringComparison.OrdinalIgnoreCase))
                        throw new RpcError($"'{path}': enum buyuk-kucuk harfe duyarli: '{s}' degil '{nm}' (degerler: {string.Join(" ", names)})");
                throw new RpcError($"'{path}': '{s}' gecerli bir {type.Name} degil (degerler: {string.Join(" ", names)})");
            case SerializedType.Kind.Vec2: CheckVec(s, 2, path); return;
            case SerializedType.Kind.Vec3: CheckVec(s, 3, path); return;
            case SerializedType.Kind.Vec4: CheckVec(s, 4, path); return;
            case SerializedType.Kind.Color:
                if (s.Length == 7 && s[0] == '#' && IsHex(s, 1, 6))
                {
                    n.Scalar = s + "ff"; // '#rrggbb' → kanonik '#rrggbbaa'
                    return;
                }
                if (!(s.Length == 9 && s[0] == '#' && IsHex(s, 1, 8)))
                    throw new RpcError($"'{path}': renk '#RRGGBBAA' hex olmali (orn. '#ff8800ff'; isim/rgb() yok), gelen '{s}'");
                return;
            case SerializedType.Kind.Asset:
                {
                    if (s.Length == 0)
                        return;
                    // Kanonik biçim: guid / guid#id (yol ya da yol#id girilirse çevrilir; alt öğe index'te var olmalı).
                    string rel = ctx.Assets.ResolvePath(s.Replace('\\', '/'));
                    string owner = SubAssets.Split(rel, out string subId);
                    string guid = ctx.Assets.PathToGuid(owner);
                    if (guid == null)
                        throw new RpcError($"'{path}': asset yok: '{s}' (asset.list ile ara; guid ya da Assets'e goreli yol)");
                    if (subId != null && !SubAssets.TryGetEntry(owner, subId, out _))
                        throw new RpcError($"'{path}': alt oge yok: '{owner}#{subId}' (container icerigi: Project panelinde klasor gibi acin)");
                    n.Scalar = subId == null ? guid : guid + "#" + subId;
                    return;
                }
            case SerializedType.Kind.GoRef:
                if (s.Length == 0 || s == "0")
                    return;
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int goId))
                    throw new RpcError($"'{path}': GO referansi 'goId' (tam sayi) olmali, gelen '{s}'");
                if (ctx.Scene.FindGo(goId) == null)
                    throw new RpcError($"'{path}': GameObject yok: id={goId} (scene.dump)");
                return;
            case SerializedType.Kind.CompRef:
                if (s.Length == 0)
                    return;
                var parts = s.Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cgo)
                    || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ord))
                    throw new RpcError($"'{path}': component referansi 'goId:TypeName:ordinal' olmali (orn. '12:Transform:0'), gelen '{s}'");
                var cg = ctx.Scene.FindGo(cgo)
                    ?? throw new RpcError($"'{path}': GameObject yok: id={cgo} (scene.dump)");
                if (parts[1] == "Transform")
                    return;
                int count = 0;
                foreach (var c in cg.Components)
                    if (c.Type == parts[1]) count++;
                if (count == 0)
                    throw new RpcError($"'{path}': GO {cgo} ('{cg.Name}') uzerinde '{parts[1]}' component'i yok");
                if (ord < 0 || ord >= count)
                    throw new RpcError($"'{path}': GO {cgo} uzerinde {count} adet '{parts[1]}' var, ordinal {ord} aralik disi");
                return;
        }
    }

    static void CheckVec(string s, int n, string path)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool ok = parts.Length == n;
        for (int i = 0; ok && i < n; i++)
            ok = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (!ok)
            throw new RpcError($"'{path}': {n} bilesenli bosluklu vektor bekleniyor (\"{(n == 2 ? "x y" : n == 3 ? "x y z" : "x y z w")}\", virgul yok), gelen '{s}'");
    }

    static bool IsHex(string s, int start, int len)
    {
        for (int i = start; i < start + len; i++)
            if (!Uri.IsHexDigit(s[i]))
                return false;
        return true;
    }

    static string Describe(DocNode n) => n.IsMap ? "nesne" : n.IsSeq ? "liste" : $"'{n.Scalar}'";

    static string FieldNames(SerializedType.FieldSchema[] nested)
        => nested == null ? "-" : string.Join(" ", Array.ConvertAll(nested, f => f.Name));

    // Yol grameri: ('.' ad | '[' index ']')*. Sema ile birlikte yurunur ki eksik liste
    // elemani / ic nesne dogru varsayilanla yaratilsin ve yanlis yol erken hata versin.
    static void SetNested(RpcContext ctx, DocNode node, SerializedType.Kind kind, Type valueType,
        SerializedType.Kind elementKind, Type elementType, SerializedType.FieldSchema[] nested,
        string path, int pos, DocNode value, string fullPath)
    {
        if (path[pos] == '[')
        {
            if (kind != SerializedType.Kind.List)
                throw new RpcError($"'{fullPath}': '{path[..pos]}' liste degil");
            int close = path.IndexOf(']', pos);
            if (close < 0 || !int.TryParse(path.AsSpan(pos + 1, close - pos - 1), out int index))
                throw new RpcError($"'{fullPath}': gecersiz index");
            node.Items ??= new List<DocNode>();
            if (index < 0 || index > node.Items.Count)
                throw new RpcError($"'{fullPath}': index {index} aralik disi (eleman sayisi {node.Items.Count}; sona eklemek icin {node.Items.Count})");
            int after = close + 1;
            if (after >= path.Length)
            {
                // Yaprak = liste elemani: null siler, index==count ekler, aksi degistirir.
                if (value == null) { if (index < node.Items.Count) node.Items.RemoveAt(index); return; }
                Validate(ctx, value, elementKind, elementType, default, null, nested, fullPath);
                if (index == node.Items.Count) node.Items.Add(value); else node.Items[index] = value;
                return;
            }
            if (index == node.Items.Count)
                node.Items.Add(InspectorPanel.CreateDefaultNode(elementKind, elementType, default, null, nested));
            SetNested(ctx, node.Items[index], elementKind, elementType, default, null, nested, path, after, value, fullPath);
            return;
        }
        if (path[pos] != '.')
            throw new RpcError($"'{fullPath}': beklenmeyen karakter '{path[pos]}'");
        if (kind != SerializedType.Kind.Object || nested == null)
            throw new RpcError($"'{fullPath}': '{path[..pos]}' ic ice nesne degil");
        int end = path.IndexOfAny(new[] { '.', '[' }, pos + 1);
        string name = end < 0 ? path[(pos + 1)..] : path[(pos + 1)..end];
        var field = SerializedType.Find(nested, name)
            ?? throw new RpcError($"'{fullPath}': '{valueType?.Name}' tipinde alan yok: '{name}'");
        node.Fields ??= new List<KeyValuePair<string, DocNode>>();
        int slot = -1;
        for (int i = 0; i < node.Fields.Count; i++)
            if (node.Fields[i].Key == field.Name || (field.FormerName != null && node.Fields[i].Key == field.FormerName))
            { slot = i; break; }
        if (end < 0)
        {
            if (value != null)
                Validate(ctx, value, field.Kind, field.FieldType, field.ElementKind, field.ElementType, field.Nested, fullPath);
            var leaf = value ?? InspectorPanel.CreateDefaultFieldNode(field);
            if (slot < 0) node.Fields.Add(new KeyValuePair<string, DocNode>(field.Name, leaf));
            else node.Fields[slot] = new KeyValuePair<string, DocNode>(field.Name, leaf);
            return;
        }
        if (slot < 0)
        {
            node.Fields.Add(new KeyValuePair<string, DocNode>(field.Name, InspectorPanel.CreateDefaultFieldNode(field)));
            slot = node.Fields.Count - 1;
        }
        SetNested(ctx, node.Fields[slot].Value, field.Kind, field.FieldType, field.ElementKind, field.ElementType, field.Nested,
            path, end, value, fullPath);
    }

    [RpcCommand("transform.set", "Pos/rot/scale yazar ('x y z' formati; bos = dokunma)", Mutates = true)]
    static DocNode TransformSet(RpcContext ctx, int go, string pos = "", string rot = "", string scale = "")
    {
        var g = RequireGo(ctx, go);
        ctx.Scene.SetTransform(g,
            pos.Length > 0 ? ParseV3(pos, "pos") : g.Pos,
            rot.Length > 0 ? ParseV3(rot, "rot") : g.Rot,
            scale.Length > 0 ? ParseV3(scale, "scale") : g.Scale);
        ctx.Scene.History.SealTop();
        return null;
    }

    [RpcCommand("select.set", "Editor secimini ayarlar (0 = temizle); paneller bu secime baglanir")]
    static DocNode SelectSet(RpcContext ctx, int id)
    {
        if (id != 0)
            RequireGo(ctx, id);
        Selection.DocId = id;
        return null;
    }

    [RpcCommand("select.asset", "Project panelinde asset secer (Assets'e goreli yol ya da guid; bos = temizle) — Inspector import ayarlari/onizlemeyi gosterir")]
    static DocNode SelectAsset(RpcContext ctx, string path = "")
    {
        if (string.IsNullOrEmpty(path))
        {
            Selection.AssetPath = null;
            return null;
        }
        string rel = App.Assets.ResolvePath(path);
        if (!App.Assets.Exists(rel))
            throw new RpcError("asset yok: " + path);
        Selection.AssetPath = System.IO.Path.Combine(App.Assets.Root, rel);
        return null;
    }

    [RpcCommand("go.setActive", "GameObject aktifligini degistirir", Mutates = true)]
    static DocNode SetActive(RpcContext ctx, int id, bool active)
    {
        ctx.Scene.SetActive(RequireGo(ctx, id), active);
        return null;
    }

    [RpcCommand("comp.setEnabled", "Component enabled durumunu degistirir", Mutates = true)]
    static DocNode SetEnabled(RpcContext ctx, int go, int comp, bool enabled)
    {
        var g = RequireGo(ctx, go);
        ctx.Scene.SetEnabled(g, RequireComp(g, comp), enabled);
        return null;
    }

    // --- toplu mutasyon: N komut = 1 istek (AI tur sayisi), 1 undo adimi, 1 canli reload,
    // ATOMIK (ilk hata → hepsi geri alinir, journal'da iz kalmaz). ---

    // Batch icinde kosabilen komutlar: yalniz senkron doc mutasyonlari (deferred/sahne
    // degistiren/oturum komutlari atomiklik ve yanit modelini bozar).
    static readonly HashSet<string> BatchAllowed = new(StringComparer.Ordinal)
    {
        "scene.addGo", "scene.removeGo", "scene.rename", "scene.reparent", "scene.apply",
        "comp.add", "comp.remove", "comp.setEnabled", "prop.set", "transform.set",
        "go.setActive", "select.set", "prefab.instantiate",
    };

    // Grup govdesi: zaten acik bir batch'in icindeysek (edit.batch → scene.apply) ic ice
    // grup acma, dis grup rollback'i ustlenir.
    static void RunBatch(RpcContext ctx, Action body)
    {
        if (ctx.Scene.History.InGroup)
        {
            body();
            return;
        }
        ctx.Scene.BeginBatch();
        try
        {
            body();
        }
        catch
        {
            ctx.Scene.RollbackBatch();
            throw;
        }
        ctx.Scene.EndBatch();
    }

    [RpcCommand("edit.batch", "Birden cok mutasyon komutunu TEK istekte, TEK undo adiminda, ATOMIK kosar (hata → hepsi geri alinir, hicbiri uygulanmaz). ops: [{cmd, args, as?}] — izinli cmd'ler: scene.addGo/removeGo/rename/reparent/apply, comp.add/remove/setEnabled, prop.set, transform.set, go.setActive, select.set. as='btn' sonucu adlandirir; sonraki args'ta '$btn' → o sonucun id'si (comp.add icin index), '$btn.alan' → sonucun alani, CompRef icin '$btn:Sprite:0'. Donus: her op'un sonucu sirayla", Mutates = true)]
    static DocNode EditBatch(RpcContext ctx, DocNode ops)
    {
        if (ops == null || !ops.IsSeq)
            throw new RpcError("ops bir liste olmali: [{cmd, args, as?}]");
        var refs = new Dictionary<string, DocNode>(StringComparer.Ordinal);
        var results = DocNode.Seq();
        int at = 0;
        string cur = "";
        try
        {
            RunBatch(ctx, () =>
            {
                for (at = 0; at < ops.Items.Count; at++)
                {
                    var op = ops.Items[at];
                    cur = op.IsMap ? op.GetScalar("cmd", null) : null;
                    if (string.IsNullOrEmpty(cur))
                        throw new RpcError("op 'cmd' alani icermiyor");
                    if (!BatchAllowed.Contains(cur))
                        throw new RpcError($"'{cur}' batch icinde kosamaz");
                    var entry = RpcRegistry.Find(cur)
                        ?? throw new RpcError($"bilinmeyen komut: '{cur}'");
                    var args = op.Get("args");
                    if (args != null && !args.IsMap)
                        throw new RpcError("'args' bir nesne olmali");
                    args = args == null ? DocNode.Map() : SubstituteRefs(args.Clone(), s => ResolveBatchRef(ctx, s, refs));
                    var result = RpcRegistry.Invoke(entry, ctx, args) ?? DocNode.Map();
                    string alias = op.GetScalar("as", null);
                    if (!string.IsNullOrEmpty(alias))
                        refs[alias] = result;
                    results.Items.Add(result);
                }
            });
        }
        catch (RpcError ex)
        {
            throw new RpcError($"ops[{at}] {cur}: {ex.Message} — batch geri alindi, hicbir degisiklik uygulanmadi");
        }
        catch (Exception ex)
        {
            throw new RpcError($"ops[{at}] {cur}: {ex.GetType().Name}: {ex.Message} — batch geri alindi, hicbir degisiklik uygulanmadi");
        }
        return results;
    }

    // '$ad' → sonucun id'si (yoksa index'i); '$ad.alan' → sonucun alani; '$ad:Tip:0' → CompRef.
    // Alias yoksa sahnedeki tekil GO adina duser (scene.apply ile ayni sozlesme).
    static string ResolveBatchRef(RpcContext ctx, string s, Dictionary<string, DocNode> refs)
    {
        int end = s.IndexOfAny(new[] { '.', ':' }, 1);
        string name = end < 0 ? s[1..] : s[1..end];
        if (!refs.TryGetValue(name, out var r))
        {
            if (end >= 0 && s[end] == '.')
                throw new RpcError($"'{s}': '{name}' adli onceki sonuc yok (as= ile adlandir)");
            return ResolveGoName(ctx, s, name, end < 0 ? "" : s[end..], null);
        }
        string field = end >= 0 && s[end] == '.' ? s[(end + 1)..] : null;
        string tail = end >= 0 && s[end] == ':' ? s[end..] : "";
        DocNode v = field != null ? r.Get(field) : (r.Get("id") ?? r.Get("index"));
        if (v == null || v.Scalar == null)
            throw new RpcError($"'{s}': '{name}' sonucunda {(field ?? "id/index")} alani yok");
        return v.Scalar + tail;
    }

    // Agactaki '$' ile baslayan skalerleri cozer (yerinde). resolve → yeni skaler.
    static DocNode SubstituteRefs(DocNode n, Func<string, string> resolve)
    {
        if (n == null)
            return null;
        if (n.Scalar != null && n.Scalar.Length > 1 && n.Scalar[0] == '$')
            n.Scalar = resolve(n.Scalar);
        if (n.Items != null)
            foreach (var it in n.Items)
                SubstituteRefs(it, resolve);
        if (n.Fields != null)
            foreach (var kv in n.Fields)
                SubstituteRefs(kv.Value, resolve);
        return n;
    }

    [RpcCommand("scene.apply", "Bildirimsel alt agac: GO'lari component+prop+cocuklariyla TEK istekte kurar/gunceller (tek undo, atomik). objects: [{name, id?, parent?, insertBefore?, active?, layer?, pos, rot, scale, components:[{type, index?, enabled?, props:{Alan: deger, 'Tracks[0].Path': deger}}], children:[...]}]. Prop degeri null = default'a don (ic ice yolda liste elemanini siler). id verilirse MEVCUT GO guncellenir (component: index varsa o, yoksa ayni tipten ilki, o da yoksa eklenir); id yoksa yeni GO. parent/insertBefore ve prop degerlerinde '$Ad' = bu apply'da daha once gecen GO'nun id'si ('$Ad:Sprite:0' CompRef). Donus: girdiyle ayni yapida {name,id,components:[index..],children:[..]}", Mutates = true)]
    static DocNode SceneApply(RpcContext ctx, DocNode objects, int parent = 0)
    {
        if (objects == null)
            throw new RpcError("objects gerekli");
        if (objects.IsMap)
        {
            var one = DocNode.Seq();
            one.Items.Add(objects);
            objects = one;
        }
        if (!objects.IsSeq)
            throw new RpcError("objects bir liste (ya da tek nesne) olmali");
        if (parent != 0)
            RequireGo(ctx, parent);
        var named = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = DocNode.Seq();
        try
        {
            RunBatch(ctx, () =>
            {
                for (int i = 0; i < objects.Items.Count; i++)
                    result.Items.Add(ApplyGo(ctx, objects.Items[i], parent, named, $"objects[{i}]"));
            });
        }
        catch (RpcError ex)
        {
            throw new RpcError(ex.Message + " — apply geri alindi, hicbir degisiklik uygulanmadi");
        }
        return result;
    }

    static DocNode ApplyGo(RpcContext ctx, DocNode spec, int defaultParent, Dictionary<string, int> named, string where)
    {
        if (spec == null || !spec.IsMap)
            throw new RpcError($"{where}: GO tanimi bir nesne olmali");
        try
        {
            string RefStr(string s) => s.StartsWith('$') ? ResolveApplyRef(ctx, s, named) : s;
            int RefInt(string key, int def)
            {
                string s = spec.GetScalar(key, null);
                if (string.IsNullOrEmpty(s))
                    return def;
                s = RefStr(s);
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                    ? v : throw new RpcError($"'{key}' tam sayi degil: '{s}'");
            }

            int parentId = RefInt("parent", defaultParent);
            if (parentId != 0)
                RequireGo(ctx, parentId);
            int insertBefore = RefInt("insertBefore", 0);
            if (insertBefore != 0 && RequireGo(ctx, insertBefore).Parent != parentId)
                throw new RpcError($"insertBefore={insertBefore} hedef parent'in ({parentId}) cocugu degil");
            string name = spec.GetScalar("name", "");

            SceneDoc.GoDoc g;
            int existing = RefInt("id", 0);
            if (existing != 0)
            {
                g = RequireGo(ctx, existing);
                if (spec.Get("parent") != null || spec.Get("insertBefore") != null)
                {
                    if (insertBefore != 0)
                        ctx.Scene.MoveGameObject(g, parentId, insertBefore);
                    else if (g.Parent != parentId)
                        ctx.Scene.Reparent(g, parentId);
                }
            }
            else
            {
                g = ctx.Scene.AddGameObject(parentId, insertBefore);
            }
            if (name.Length > 0)
            {
                ctx.Scene.SetName(g, name);
                named[name] = g.Id; // ayni ad tekrarlanirsa sonuncusu kazanir
            }

            if (spec.Get("active") is { Scalar: not null } act)
                ctx.Scene.SetActive(g, ParseBool(act.Scalar, "active"));
            if (spec.Get("layer") != null)
                ctx.Scene.SetLayer(g, RefInt("layer", g.Layer));
            string pos = spec.GetScalar("pos", ""), rot = spec.GetScalar("rot", ""), scale = spec.GetScalar("scale", "");
            if (pos.Length + rot.Length + scale.Length > 0)
                ctx.Scene.SetTransform(g,
                    pos.Length > 0 ? ParseV3(pos, "pos") : g.Pos,
                    rot.Length > 0 ? ParseV3(rot, "rot") : g.Rot,
                    scale.Length > 0 ? ParseV3(scale, "scale") : g.Scale);

            var r = DocNode.Map();
            r.Add("name", DocNode.Scal(g.Name));
            r.Add("id", DocNode.Scal(g.Id.ToString(CultureInfo.InvariantCulture)));

            if (spec.Get("components") is { } comps)
            {
                if (!comps.IsSeq)
                    throw new RpcError("'components' bir liste olmali");
                var outComps = DocNode.Seq();
                for (int ci = 0; ci < comps.Items.Count; ci++)
                {
                    var cs = comps.Items[ci];
                    if (cs == null || !cs.IsMap)
                        throw new RpcError($"components[{ci}] bir nesne olmali");
                    string type = cs.GetScalar("type", "");
                    SceneDoc.CompDoc cd = null;
                    if (cs.GetScalar("index", null) is { } idxS)
                    {
                        if (!int.TryParse(idxS, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                            throw new RpcError($"components[{ci}].index tam sayi degil: '{idxS}'");
                        cd = RequireComp(g, idx);
                        if (type.Length > 0 && cd.Type != type)
                            throw new RpcError($"components[{ci}]: index {idx} '{cd.Type}' tipinde, '{type}' degil");
                    }
                    else
                    {
                        if (type.Length == 0)
                            throw new RpcError($"components[{ci}]: 'type' (ya da 'index') gerekli");
                        if (ctx.Catalog.Find(type) == null)
                            throw new RpcError($"components[{ci}]: bilinmeyen component tipi '{type}' (comp.types ile listele)");
                        if (existing != 0)
                            foreach (var c in g.Components)
                                if (c.Type == type) { cd = c; break; }
                        cd ??= ctx.Scene.AddComponent(g, type);
                    }
                    if (cs.Get("enabled") is { Scalar: not null } en)
                        ctx.Scene.SetEnabled(g, cd, ParseBool(en.Scalar, "enabled"));
                    if (cs.Get("props") is { } props)
                    {
                        if (!props.IsMap)
                            throw new RpcError($"components[{ci}].props bir nesne olmali");
                        foreach (var kv in props.Fields)
                        {
                            try
                            {
                                SetPropPath(ctx, g, cd, kv.Key, kv.Value.IsNull ? null
                                    : SubstituteRefs(kv.Value.Clone(), s => ResolveApplyRef(ctx, s, named)));
                            }
                            catch (RpcError ex)
                            {
                                throw new RpcError($"components[{ci}] {cd.Type}.{kv.Key}: {ex.Message}");
                            }
                        }
                    }
                    outComps.Items.Add(DocNode.Scal(g.Components.IndexOf(cd).ToString(CultureInfo.InvariantCulture)));
                }
                r.Add("components", outComps);
            }

            if (spec.Get("children") is { } children)
            {
                if (!children.IsSeq)
                    throw new RpcError("'children' bir liste olmali");
                var outChildren = DocNode.Seq();
                for (int k = 0; k < children.Items.Count; k++)
                    outChildren.Items.Add(ApplyGo(ctx, children.Items[k], g.Id, named, $"children[{k}]"));
                r.Add("children", outChildren);
            }
            return r;
        }
        catch (RpcError ex)
        {
            throw new RpcError($"{where}: {ex.Message}");
        }
    }

    // '$Ad' → bu apply'da adlandirilmis GO (yoksa sahnede o adla TEK GO); ':' sonrasi korunur (CompRef).
    static string ResolveApplyRef(RpcContext ctx, string s, Dictionary<string, int> named)
    {
        int colon = s.IndexOf(':', 1);
        string name = colon < 0 ? s[1..] : s[1..colon];
        return ResolveGoName(ctx, s, name, colon < 0 ? "" : s[colon..], named);
    }

    static string ResolveGoName(RpcContext ctx, string s, string name, string tail, Dictionary<string, int> named)
    {
        int id = 0;
        if (named == null || !named.TryGetValue(name, out id))
        {
            int hits = 0;
            foreach (var o in ctx.Scene.Doc.Objects)
                if (o.Name == name) { id = o.Id; hits++; }
            if (hits == 0)
                throw new RpcError($"'{s}': '{name}' adli GO yok (once tanimla/as= ile adlandir ya da sahnede bulunsun)");
            if (hits > 1)
                throw new RpcError($"'{s}': sahnede '{name}' adli {hits} GO var, belirsiz (id kullan)");
        }
        return id.ToString(CultureInfo.InvariantCulture) + tail;
    }

    static bool ParseBool(string s, string label) => s switch
    {
        "true" => true,
        "false" => false,
        _ => throw new RpcError($"'{label}' true/false olmali: '{s}'"),
    };

    // --- oturum ---

    [RpcCommand("edit.undo", "Son islemi geri alir")]
    static DocNode Undo(RpcContext ctx)
    {
        ctx.Scene.DoUndo();
        return null;
    }

    [RpcCommand("edit.redo", "Geri alinan islemi yineler")]
    static DocNode Redo(RpcContext ctx)
    {
        ctx.Scene.DoRedo();
        return null;
    }

    [RpcCommand("scene.save", "Acik sahneyi (prefab stage'deyse prefab'i) diske yazar")]
    static DocNode Save(RpcContext ctx)
    {
        if (ctx.Scene.Path == null)
            throw new RpcError("acik sahne yok");
        ctx.Scene.Save();
        return null;
    }

    // Acik sahneyi/stage'i yikacak komutlar icin kaydedilmemis degisiklik bekcisi. GUI'de popup
    // sorulur; RPC'de popup = kilit. Bu yuzden tek roundtrip'te karar verilsin diye hata metni
    // neyin dirty oldugunu ve iki cikis yolunu (scene.save | force=true) soyler. force=true →
    // degisiklikler bellekte atilir (undo yigini da doc'la gider), komut popup'siz ilerler.
    // stageOnly=true: yalniz prefab stage'in dirty olmasi engeldir (prefab.open: asil sahne
    // stash'te korunur, kaybolmaz).
    static void GuardUnsaved(bool force, bool stageOnly = false)
    {
        var edit = App.EditScene;
        var stash = App.StashedScene;
        bool stageDirty = App.InPrefabStage && edit.Dirty;
        bool sceneDirty = App.InPrefabStage ? (stash?.Dirty ?? false) : edit.Dirty;
        bool blocked = stageOnly ? stageDirty : (stageDirty || sceneDirty);
        if (!blocked)
            return;
        if (force)
        {
            App.DiscardAllChanges();
            return;
        }
        var sb = new StringBuilder("UNSAVED: ");
        if (stageDirty)
            sb.Append("prefab stage '").Append(Path.GetFileName(edit.Path)).Append("' kaydedilmemis");
        if (stageDirty && sceneDirty && !stageOnly)
            sb.Append(" ve ");
        if (sceneDirty && !stageOnly)
            sb.Append("sahne '").Append(stash != null ? Path.GetFileName(stash.Path) : Path.GetFileName(edit.Path) ?? "(untitled)").Append("' kaydedilmemis");
        sb.Append(". Hicbir sey yapilmadi. Secenekler: ");
        if (stageDirty)
            sb.Append("prefab.close(save=true|false) ile stage'i kapat; ");
        if (sceneDirty && !stageOnly)
            sb.Append("scene.save ile kaydet (kullanici onayi olmadan kaydetme); ");
        sb.Append("ya da ayni komutu force=true ile tekrar gonder (degisiklikler ATILIR, geri alinamaz)");
        throw new RpcError(sb.ToString());
    }

    [RpcCommand("scene.new", "Yeni bos sahne yaratir ve acar (yol Assets'e goreli, .scene). Acik sahne kaydedilmemisse UNSAVED hatasi doner: scene.save ya da force=true (degisiklikler atilir)")]
    static DocNode SceneNew(RpcContext ctx, string path, bool force = false)
    {
        string full = GuardAssetPath(path);
        if (File.Exists(full))
            throw new RpcError($"dosya zaten var: {path} (scene.open kullan)");
        GuardUnsaved(force);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        App.OpenScene(full); // dosya yoksa bos doc acilir
        App.EditScene.Save(); // dosyayi yarat
        _diffBaseline.Clear();
        return null;
    }

    [RpcCommand("scene.open", "Sahne acar (yol Assets'e goreli). Acik sahne/prefab stage kaydedilmemisse UNSAVED hatasi doner: scene.save ya da force=true (degisiklikler atilir)")]
    static DocNode SceneOpen(RpcContext ctx, string path, bool force = false)
    {
        string full = GuardAssetPath(path);
        if (!File.Exists(full))
            throw new RpcError($"sahne yok: {path}");
        GuardUnsaved(force);
        App.OpenScene(full);
        _diffBaseline.Clear();
        return null;
    }

    [RpcCommand("prefab.open", "Prefab'i duzenleme icin acar (prefab stage; acik sahne bellekte bekler, kaybolmaz). Yol Assets'e goreli. Baska bir prefab stage'i kaydedilmemisse UNSAVED hatasi doner: prefab.close(save) ya da force=true")]
    static DocNode PrefabOpen(RpcContext ctx, string path, bool force = false)
    {
        string full = GuardAssetPath(path);
        if (!File.Exists(full))
            throw new RpcError($"prefab yok: {path}");
        if (PlayMode.State != PlayState.Editing)
            throw new RpcError("Play acik: once play.stop");
        GuardUnsaved(force, stageOnly: true);
        App.OpenPrefab(full);
        if (!App.InPrefabStage)
            throw new RpcError("prefab stage acilamadi (Console'a bak)");
        _diffBaseline.Clear();
        return null;
    }

    [RpcCommand("prefab.close", "Prefab stage'i kapatir, sahneye doner; save=true degisiklikleri prefab dosyasina yazar, false atar")]
    static DocNode PrefabClose(RpcContext ctx, bool save = true)
    {
        if (!App.InPrefabStage)
            throw new RpcError("prefab stage acik degil");
        if (save)
            App.EditScene.Save();
        else
            App.EditScene.DiscardChanges();
        App.TryExitPrefabStage();
        _diffBaseline.Clear();
        return null;
    }

    [RpcCommand("prefab.create", "Sahnedeki GO'yu (alt agaciyla) Assets'e goreli path'e .prefab olarak yazar ve o GO'yu instance'a BAGLAR (editorde Hierarchy→Project surukleme ile ayni). GO zaten bir prefab instance'inin parcasiysa ret (variant yok). Donus {path, guid}", Mutates = true)]
    static DocNode PrefabCreate(RpcContext ctx, int id, string path)
    {
        var g = RequireGo(ctx, id);
        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) path += ".prefab";
        string full = GuardAssetPath(path);
        if (File.Exists(full))
            throw new RpcError($"dosya zaten var: {path}");
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        if (!ctx.Scene.CreatePrefabAsset(g, full))
            throw new RpcError("prefab olusturulamadi (Console'a bak: instance node'undan prefab yapilamaz)");
        string rel = Path.GetRelativePath(ctx.Assets.Root, full).Replace('\\', '/');
        var r = DocNode.Map();
        r.Add("path", DocNode.Scal(rel));
        r.Add("guid", DocNode.Scal(ctx.Assets.PathToGuid(rel) ?? ""));
        r.Add("id", DocNode.Scal(g.Id.ToString(CultureInfo.InvariantCulture)));
        return r;
    }

    [RpcCommand("prefab.instantiate", "Prefab'i (Assets'e goreli yol ya da guid) sahneye instance olarak ekler (editorde Project→Hierarchy surukleme ile ayni): parent 0 = kok, pos 'x y z' (bos = prefab'in kendi pozu). Instance kok adi benzersizlestirilir; override'lar sonra prop.set ile. Prefab stage'de kendini/iceren prefab'i sokmak ret (dongu). Donus {id, name}", Mutates = true)]
    static DocNode PrefabInstantiate(RpcContext ctx, string prefab, int parent = 0, string pos = "")
    {
        if (parent != 0)
            RequireGo(ctx, parent);
        string rel = ctx.Assets.ResolvePath(prefab);
        if (!rel.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && !ctx.Assets.Exists(rel) && ctx.Assets.Exists(rel + ".prefab")) rel += ".prefab";
        string guid = ctx.Assets.PathToGuid(rel) ?? throw new RpcError($"prefab yok: {prefab} (asset.list type=Prefab)");
        if (!rel.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new RpcError($"prefab degil: {rel}");
        Vec3? at = pos.Length > 0 ? ParseV3(pos, "pos") : null;
        var root = ctx.Scene.InstantiatePrefab(guid, parent, at);
        if (root == null)
            throw new RpcError("instantiate basarisiz (Console'a bak: dongu ya da prefab okunamadi)");
        var r = DocNode.Map();
        r.Add("id", DocNode.Scal(root.Id.ToString(CultureInfo.InvariantCulture)));
        r.Add("name", DocNode.Scal(root.Name));
        return r;
    }

    [RpcCommand("play.start", "Play moduna gecer")]
    static DocNode PlayStart()
    {
        PlayMode.Play();
        return null;
    }

    [RpcCommand("play.stop", "Play'i durdurur (doc'a doner)")]
    static DocNode PlayStop()
    {
        PlayMode.Stop();
        return null;
    }

    [RpcCommand("sim.run", "N frame simule eder, canli dokumle doner. SABIT ADIM: her sim frame'i dt=1/fps (gercek saatten bagimsiz, deterministik). Play degilse baslatir; play'deyken doc DEGISTIYSE oyun sahnesi otomatik yeniden dogar (frame 0'dan; restart=true her zaman). describe=true sonda render.describe (ekranin metin ozeti; UCUZ, once bunu dene); screenshot=true sonda goruntu (kucultulmus jpg; maxSize/format render.screenshot ile ayni, tiklama = px * toScreen); captureEvery=K → her K frame'de goruntu (frames listesi; animasyon inceleme); stop=true sonda durdurur")]
    static DocNode SimRun(RpcContext ctx, int frames = 60, bool stop = false, bool screenshot = false, bool restart = false, int captureEvery = 0, int fps = 60, int maxSize = 1024, string format = "jpg", bool describe = false)
    {
        if (frames < 1 || frames > 100000)
            throw new RpcError("frames 1..100000 olmali");
        if (fps < 1 || fps > 1000)
            throw new RpcError("fps 1..1000 olmali");
        if (captureEvery < 0 || (captureEvery > 0 && frames / captureEvery > 2000))
            throw new RpcError("captureEvery: en fazla 2000 kare yakalanir");
        if (format != "jpg" && format != "png")
            throw new RpcError("format jpg|png olmali");
        bool restarted = false;
        if (PlayMode.State == PlayState.Editing)
            PlayMode.Play();
        else if (restart || PlayMode.PlaySceneStale)
        {
            PlayMode.DoHotReload();
            restarted = true;
        }
        PlayMode.ForcedDt = 1f / fps;
        int start = RpcHost.FrameCount;
        int target = start + frames;
        DocNode captures = captureEvery > 0 ? DocNode.Seq() : null;
        int nextCapture = start + captureEvery;
        RpcHost.Defer(() =>
        {
            if (captures != null && RpcHost.FrameCount >= nextCapture && RpcHost.FrameCount <= target)
            {
                var shot = RenderScreenshot(maxSize: maxSize, format: format);
                shot.Fields.Insert(0, new KeyValuePair<string, DocNode>("frame", DocNode.Scal((RpcHost.FrameCount - start).ToString(CultureInfo.InvariantCulture))));
                captures.Items.Add(shot);
                nextCapture += captureEvery;
            }
            if (RpcHost.FrameCount < target)
                return null; // bekle
            PlayMode.ForcedDt = 0f;
            var dump = DumpLive(ctx, "play");
            if (restarted)
                dump.Add("restarted", DocNode.Scal("true"));
            if (describe)
                dump.Add("describe", RpcDescribe.DescribePlay(ctx));
            if (screenshot)
                dump.Add("screenshot", RenderScreenshot(maxSize: maxSize, format: format));
            if (captures != null)
                dump.Add("frames", captures);
            if (stop)
                PlayMode.Stop();
            return dump;
        });
        return null;
    }

    // --- goruntu + cihaz profili + pointer (AI'in gozu ve eli) ---
    // KOORDINAT SOZLESMESI: her sey MANTIKSAL nokta (Screen.width x height, sol-ust
    // orijin, y asagi). render.screenshot varsayilan KUCULTULMUS jpg yazar (maxSize=1024):
    // tiklama = region.x + px * toScreen; scale=1 & maxSize=0 → goruntu pikseli == koordinat.

    [RpcCommand("view.get", "Game View boyutu/cihaz profili (mantiksal + fiziksel + pixelRatio)")]
    static DocNode ViewGet()
    {
        var r = DocNode.Map();
        r.Add("mode", DocNode.Scal(GameOutput.Forced ? "device" : "panel"));
        if (GameOutput.ForcedName.Length > 0)
            r.Add("device", DocNode.Scal(GameOutput.ForcedName));
        r.Add("width", DocNode.Scal(F(GameOutput.ViewW)));
        r.Add("height", DocNode.Scal(F(GameOutput.ViewH)));
        r.Add("physicalW", DocNode.Scal(GameOutput.PhysicalW.ToString(CultureInfo.InvariantCulture)));
        r.Add("physicalH", DocNode.Scal(GameOutput.PhysicalH.ToString(CultureInfo.InvariantCulture)));
        r.Add("pixelRatio", DocNode.Scal(F(GameOutput.PixelRatio)));
        return r;
    }

    [RpcCommand("view.set", "Game View'a FIZIKSEL cozunurluk + pixelRatio dayatir (mantiksal = w/pixelRatio); width=0 = panel takibine don")]
    static DocNode ViewSet(int width = 0, int height = 0, float pixelRatio = 1f)
    {
        if (width > 0 && (width > 8192 || height <= 0 || height > 8192))
            throw new RpcError("width/height 1..8192 olmali");
        if (pixelRatio <= 0f || pixelRatio > 8f)
            throw new RpcError("pixelRatio 0..8 araliginda olmali");
        GameOutput.SetDevice(width, height, pixelRatio);
        App.SaveSettings();
        return ViewGet();
    }

    [RpcCommand("view.device", "Cihaz preset'i uygular (name: view.devices listesinden). Yon Game View yon tercihinden (Auto = PlayerSettings.orientation, o da Auto ise preset'in dogal yonu); landscape/portrait verilirse tercih olarak kalir")]
    static DocNode ViewDevice(string name, string orientation = "")
    {
        var dv = GameOutput.FindDevice(name) ?? throw new RpcError($"bilinmeyen cihaz: {name} (view.devices)");
        if (orientation.Length > 0)
            GameOutput.Orientation = Enum.TryParse<ScreenOrientation>(orientation, true, out var oe)
                ? oe : throw new RpcError("orientation landscape|portrait|auto olmali");
        bool landscape = GameOutput.ApplyDevice(dv);
        App.SaveSettings();
        var r = ViewGet();
        r.Add("orientation", DocNode.Scal(landscape ? "landscape" : "portrait"));
        return r;
    }

    [RpcCommand("view.devices", "Kullanilabilir cihaz preset'leri")]
    static DocNode ViewDevices()
    {
        var seq = DocNode.Seq();
        foreach (var d in GameOutput.Devices)
        {
            var m = DocNode.Map();
            m.Add("name", DocNode.Scal(d.Name));
            m.Add("physicalW", DocNode.Scal(d.W.ToString(CultureInfo.InvariantCulture)));
            m.Add("physicalH", DocNode.Scal(d.H.ToString(CultureInfo.InvariantCulture)));
            m.Add("pixelRatio", DocNode.Scal(F(d.PixelRatio)));
            m.Add("width", DocNode.Scal(F(d.W / d.PixelRatio)));
            m.Add("height", DocNode.Scal(F(d.H / d.PixelRatio)));
            seq.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("devices", seq);
        return r;
    }

    static int _shotSeq;

    [RpcCommand("render.screenshot", "Game View'in son frame'ini yazar. VARSAYILAN KUCULTULMUS: maxSize=1024 uzun kenar, jpg q80 (hizli yorum). TIKLAMA KOORDINATI = region.x + px * toScreen (sonuctaki carpan; region yoksa 0). scale: mantiksal boyuta carpan (maxSize ile sinirli); maxSize=0 sinirsiz; format jpg|png (scale=1, maxSize=0, png = piksel==koordinat); region 'x y w h' (mantiksal koordinat) → yalniz o bolge, detay icin. path bos = Library/Screenshots/shot_N.jpg|png")]
    static DocNode RenderScreenshot(string path = "", float scale = 1f, int maxSize = 1024, string format = "jpg", int quality = 80, string region = "")
    {
        if (scale <= 0f || scale > 8f)
            throw new RpcError("scale 0..8 araliginda olmali");
        if (format != "jpg" && format != "png")
            throw new RpcError("format jpg|png olmali");
        if (maxSize < 0 || maxSize > 16384)
            throw new RpcError("maxSize 0..16384 olmali");
        if (quality < 1 || quality > 100)
            throw new RpcError("quality 1..100 olmali");
        var req = new GameOutput.ShotRequest { Scale = scale, MaxSize = maxSize, Jpg = format == "jpg", Quality = quality };
        if (region.Length > 0)
        {
            var f = ParseFloats(region, 4, "region");
            if (f[2] <= 0 || f[3] <= 0)
                throw new RpcError("region w/h pozitif olmali");
            req.RegionX = f[0]; req.RegionY = f[1]; req.RegionW = f[2]; req.RegionH = f[3];
        }
        if (path.Length == 0)
            path = Path.Combine("Library", "Screenshots", $"shot_{++_shotSeq:D4}.{format}");
        string full = Path.IsPathRooted(path) ? path : Path.Combine(App.Project.Root, path);
        var res = GameOutput.Screenshot(full, req);
        var r = DocNode.Map();
        r.Add("path", DocNode.Scal(Path.GetFullPath(full)));
        r.Add("w", DocNode.Scal(res.W.ToString(CultureInfo.InvariantCulture)));
        r.Add("h", DocNode.Scal(res.H.ToString(CultureInfo.InvariantCulture)));
        r.Add("screenW", DocNode.Scal(F(GameOutput.ViewW)));
        r.Add("screenH", DocNode.Scal(F(GameOutput.ViewH)));
        if (region.Length > 0)
            r.Add("region", DocNode.Scal(string.Create(CultureInfo.InvariantCulture, $"{res.RegionX:R} {res.RegionY:R} {res.RegionW:R} {res.RegionH:R}")));
        // Tiklama koordinati = region.x + px * toScreen (goruntu kucultulmus/kirpilmis olabilir).
        r.Add("toScreen", DocNode.Scal(F(res.ToScreen)));
        r.Add("format", DocNode.Scal(format));
        r.Add("bytes", DocNode.Scal(res.Bytes.ToString(CultureInfo.InvariantCulture)));
        r.Add("pixelRatio", DocNode.Scal(F(GameOutput.PixelRatio)));
        r.Add("playing", DocNode.Scal(PlayMode.PlayScene != null ? "true" : "false"));
        if (PlayMode.PlaySceneStale)
            r.Add("stale", DocNode.Scal("true")); // doc degisti, oyun sahnesi eski: sim.run ile tazele
        return r;
    }

    static float[] ParseFloats(string s, int count, string label)
    {
        var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != count)
            throw new RpcError($"'{label}' {count} sayi olmali: '{s}'");
        var r = new float[count];
        for (int i = 0; i < count; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]))
                throw new RpcError($"'{label}' sayi degil: '{parts[i]}'");
        return r;
    }

    static Scene RequirePlayScene()
    {
        if (PlayMode.State == PlayState.Editing)
            PlayMode.Play();
        else if (PlayMode.PlaySceneStale)
            PlayMode.DoHotReload(); // input bayat sahneye gitmesin
        return PlayMode.PlayScene ?? throw new RpcError("oyun sahnesi yok (play baslatilamadi)");
    }

    [RpcCommand("anim.goto", "Play sahnesindeki MovieClip'i KESIN bir kareye goturur ve durdurur (deterministik kare yakalama; sim.run gercek saatle ilerler). go: hiyerarsi yolu/adi ('ROOT' ya da 'BG/Panel'), frame kesirli olabilir (12.5), clip = GO'daki kacinci MovieClip.     play=true → o kareden oynatmaya devam. describe=true → bir render sonra render.describe (metin); screenshot=true → goruntu (kucultulmus jpg, toScreen)")]
        static DocNode AnimGoto(RpcContext ctx, string go, float frame, int clip = 0, bool play = false, bool screenshot = false, bool describe = false)
    {
        var s = RequirePlayScene();
        var target = FindLiveByPath(s, go)
            ?? throw new RpcError($"play sahnesinde GO yok: '{go}' (scene.dumpLive(play) ile adlari gor; yol 'A/B/C')");
        MovieClip mc = null;
        int seen = 0;
        for (int i = 0; i < target.ComponentCount; i++)
            if (target.ComponentAt(i) is MovieClip m && seen++ == clip) { mc = m; break; }
        if (mc == null)
            throw new RpcError($"'{go}' uzerinde {clip}. MovieClip yok ({seen} adet)");
        mc.Frame = frame; // durdurur + kesirli ornekler
        if (play)
            mc.Play();
        var r = DocNode.Map();
        r.Add("go", DocNode.Scal(LivePath(target)));
        r.Add("frame", DocNode.Scal(F(mc.CurrentFramePosition)));
        r.Add("totalFrames", DocNode.Scal(mc.TotalFrames.ToString(CultureInfo.InvariantCulture)));
        r.Add("playing", DocNode.Scal(mc.IsPlaying ? "true" : "false"));
        if (!screenshot && !describe)
            return r;
        int at = RpcHost.FrameCount + 1;
        RpcHost.Defer(() =>
        {
            if (RpcHost.FrameCount <= at)
                return null; // ornekleme bir frame ciksin
            if (describe)
                r.Add("describe", RpcDescribe.DescribePlay(ctx));
            if (screenshot)
                r.Add("screenshot", RenderScreenshot());
            return r;
        });
        return null;
    }

    // "A/B/C" yolu ya da tekil ad ile canli GO (play sahnesi): yol koklerden iner; tek segment ise tum agacta ada bakar.
    internal static GameObject FindLiveByPath(Scene s, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        GameObject cur = null;
        for (int i = 0; i < s.RootCount; i++)
            if (s.GetRoot(i).name == segs[0]) { cur = s.GetRoot(i); break; }
        if (cur == null && segs.Length == 1)
        {
            for (int i = 0; i < s.RootCount && cur == null; i++)
                cur = FindByNameDeep(s.GetRoot(i), segs[0]);
            return cur;
        }
        for (int k = 1; cur != null && k < segs.Length; k++)
        {
            GameObject next = null;
            for (var t = cur.transform.FirstChild; t != null; t = t.NextSibling)
                if (t.gameObject?.name == segs[k]) { next = t.gameObject; break; }
            cur = next;
        }
        return cur;
    }

    static GameObject FindByNameDeep(GameObject go, string name)
    {
        if (go.name == name)
            return go;
        for (var t = go.transform.FirstChild; t != null; t = t.NextSibling)
            if (t.gameObject != null && FindByNameDeep(t.gameObject, name) is { } hit)
                return hit;
        return null;
    }

    [RpcCommand("input.pointer", "Oyun sahnesine ham pointer olayi (action: down|move|up; x,y mantiksal). Play degilse baslatir")]
    static DocNode InputPointer(string action, float x, float y)
    {
        var s = RequirePlayScene();
        switch (action)
        {
            case "down": s.Pointer.Down(x, y); break;
            case "move": s.Pointer.Move(x, y); break;
            case "up": s.Pointer.Up(x, y); break;
            default: throw new RpcError("action down|move|up olmali");
        }
        return null;
    }

    [RpcCommand("input.tap", "Tiklama: down, holdFrames frame sonra up; up'tan 1 frame sonra doner (handler'lar kostu)")]
    static DocNode InputTap(float x, float y, int holdFrames = 2)
    {
        var s = RequirePlayScene();
        PlayMode.ForcedDt = 1f / 60f; // jest frame'leri deterministik
        s.Pointer.Down(x, y);
        int upAt = RpcHost.FrameCount + Math.Max(1, holdFrames);
        bool upped = false;
        RpcHost.Defer(() =>
        {
            if (!upped)
            {
                if (RpcHost.FrameCount < upAt)
                    return null;
                PlayMode.PlayScene?.Pointer.Up(x, y);
                upped = true;
                return null;
            }
            if (RpcHost.FrameCount <= upAt)
                return null;
            PlayMode.ForcedDt = 0f;
            return GestureResult(PlayMode.PlayScene, "click");
        });
        return null;
    }

    // Jest sonucu: AI "neye degdim, handler kostu mu" sorusunu bir sonraki turda sormasin.
    static DocNode GestureResult(Scene s, string gesture)
    {
        var r = DocNode.Map();
        r.Add("gesture", DocNode.Scal(gesture));
        var p = s?.Pointer;
        var target = p?.LastDownTarget;
        if (p == null || p.LastDownMissed || target == null)
        {
            r.Add("hit", DocNode.Scal("false"));
            r.Add("note", DocNode.Scal("bu noktada tiklanabilir renderer yok: renderer BlocksRaycast=true olmali ya da GO/atalarinda pointer handler (OnPointerClick vb.) bulunmali; koordinat screenshot pikseliyle birebir"));
            return r;
        }
        r.Add("hit", DocNode.Scal("true"));
        r.Add("target", DocNode.Scal(LivePath(target)));
        r.Add("handlers", DocNode.Scal(p.LastGestureHandlers.ToString(CultureInfo.InvariantCulture)));
        if (p.LastGestureHandlers == 0)
            r.Add("note", DocNode.Scal("hit var ama hicbir handler kosmadi: hedefte/atalarinda OnPointer* override eden component yok (davranis icin kod gerekir: engine.api Component)"));
        return r;
    }

    // Canli GO'nun hiyerarsi yolu ("BG/Panel/CloseBtn") — play sahnesinde doc id yok, yol tek adres.
    internal static string LivePath(GameObject go)
    {
        string path = go.name;
        for (var t = go.transform.parent; t != null; t = t.parent)
            path = t.gameObject.name + "/" + path;
        return path;
    }

    [RpcCommand("input.drag", "Surukleme: (x0,y0) down, frames boyunca lineer move, (x1,y1) up; bitince doner")]
    static DocNode InputDrag(float x0, float y0, float x1, float y1, int frames = 10)
    {
        if (frames < 1 || frames > 10000)
            throw new RpcError("frames 1..10000 olmali");
        var s = RequirePlayScene();
        PlayMode.ForcedDt = 1f / 60f;
        s.Pointer.Down(x0, y0);
        int start = RpcHost.FrameCount;
        bool upped = false;
        RpcHost.Defer(() =>
        {
            var ps = PlayMode.PlayScene;
            if (ps == null)
            {
                PlayMode.ForcedDt = 0f;
                throw new RpcError("oyun sahnesi drag sirasinda kapandi");
            }
            int i = RpcHost.FrameCount - start;
            if (!upped)
            {
                if (i < frames)
                {
                    float t = (i + 1) / (float)frames;
                    ps.Pointer.Move(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
                    return null;
                }
                ps.Pointer.Up(x1, y1);
                upped = true;
                return null;
            }
            PlayMode.ForcedDt = 0f;
            return GestureResult(ps, "drag");
        });
        return null;
    }

    // --- dosya/kod kanali (toptan YAML uretimi + kod yazimi dosyadan; watcher gerisini yapar) ---

    [RpcCommand("asset.write", "Assets altina dosya yazar (yol Assets'e goreli); watcher derleme/reload tetikler")]
    static DocNode AssetWrite(RpcContext ctx, string path, string content)
    {
        string full = GuardAssetPath(path);
        RpcRedact.GuardText(content); // "token: ***" dosyaya girmesin
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, content);
        var r = DocNode.Map();
        r.Add("path", DocNode.Scal(full));
        return r;
    }

    [RpcCommand("asset.read", "Assets altindan dosya okur")]
    static DocNode AssetRead(RpcContext ctx, string path)
    {
        string full = GuardAssetPath(path);
        if (!File.Exists(full))
            throw new RpcError($"dosya yok: {path}");
        var r = DocNode.Map();
        r.Add("content", DocNode.Scal(File.ReadAllText(full)));
        return r;
    }

    [RpcCommand("asset.list", "Projedeki asset'ler (yol/guid/tip/bayt; gorsellerde width/height + atlas uyeligi). filter: yolda gecen metin (buyuk-kucuk harf duyarsiz), type: Sprite|Font|Prefab|AudioClip|PixelEffect|... ya da uzanti (.png)")]
    static DocNode AssetList(RpcContext ctx, string filter = "", string type = "")
    {
        var seq = DocNode.Seq();
        var paths = new List<string>(ctx.Assets.AllAssets.Keys);
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in paths)
        {
            if (filter.Length > 0 && rel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            string typeName = AssetTypeName(rel);
            if (type.Length > 0 && !type.Equals(typeName, StringComparison.OrdinalIgnoreCase)
                && !rel.EndsWith(type, StringComparison.OrdinalIgnoreCase))
                continue;
            seq.Items.Add(AssetEntry(ctx, rel, typeName));
        }
        var r = DocNode.Map();
        r.Add("count", DocNode.Scal(seq.Items.Count.ToString(CultureInfo.InvariantCulture)));
        r.Add("assets", seq);
        return r;
    }

    [RpcCommand("asset.validate", "Asset referanslarini dogrular (sahne/prefab/.asset): kirik guid / alt oge, eski 'yol#id' bicimi, kararsiz id. Sonuc: issues[{file,line,value,problem,severity}]")]
    static DocNode AssetValidate(RpcContext ctx)
    {
        var issues = ReferenceValidator.Validate(ctx.Assets);
        var seq = DocNode.Seq();
        int err = 0;
        foreach (var it in issues)
        {
            var m = DocNode.Map();
            m.Add("file", DocNode.Scal(it.File));
            m.Add("line", DocNode.Scal(it.Line.ToString(CultureInfo.InvariantCulture)));
            m.Add("value", DocNode.Scal(it.Value));
            m.Add("problem", DocNode.Scal(it.Problem));
            m.Add("severity", DocNode.Scal(it.Sev.ToString().ToLowerInvariant()));
            if (it.Sev == ReferenceValidator.Severity.Error) err++;
            seq.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("errors", DocNode.Scal(err.ToString(CultureInfo.InvariantCulture)));
        r.Add("warnings", DocNode.Scal((issues.Count - err).ToString(CultureInfo.InvariantCulture)));
        r.Add("issues", seq);
        return r;
    }

    [RpcCommand("asset.info", "Tek asset'in bilgisi (yol ya da guid): tip/bayt/guid; gorselde width/height (dosya basligindan, yukleme gerekmez) + atlas bolgesi; import ayarlari (varsa)")]
    static DocNode AssetInfo(RpcContext ctx, string path)
    {
        string rel = ctx.Assets.ResolvePath(path).Replace('\\', '/');
        GuardAssetPath(rel);
        if (!ctx.Assets.AllAssets.ContainsKey(rel))
            throw new RpcError($"asset yok: {path} (asset.list ile ara)");
        var r = AssetEntry(ctx, rel, AssetTypeName(rel));
        // Container: alt ogeler (Project panelinde klasor gibi acilanlar); referans anahtari "<yol>#<id>".
        var subIdx = SubAssets.IndexOf(rel);
        if (subIdx.Entries.Count > 0)
        {
            var subs = DocNode.Seq();
            foreach (var e in subIdx.Entries)
            {
                var m = DocNode.Map();
                m.Add("id", DocNode.Scal(e.Id));
                m.Add("name", DocNode.Scal(e.Name));
                m.Add("type", DocNode.Scal(e.Type));
                m.Add("width", DocNode.Scal(e.Width.ToString(CultureInfo.InvariantCulture)));
                m.Add("height", DocNode.Scal(e.Height.ToString(CultureInfo.InvariantCulture)));
                if (!e.Stable) m.Add("stable", DocNode.Scal("false"));
                subs.Items.Add(m);
            }
            r.Add("subAssets", subs);
        }
        var importer = ImportPipeline.ImporterFor(rel);
        if (importer?.SettingsType != null)
        {
            object settings = ImportPipeline.LoadSettings(rel, importer);
            if (settings != null)
                r.Add("importSettings", ObjectSerializer.ToNode(settings, ctx.Assets));
        }
        return r;
    }

    // Uzanti -> tip adi: importer kaydi (AssetType) oncelikli, yoksa AssetDatabase tohumu, yoksa [Asset] .asset tipi.
    static string AssetTypeName(string rel)
        => ImportPipeline.ImporterFor(rel)?.AssetType ?? AssetDatabase.ImportTypeOf(rel)?.Name ?? App.Assets?.AssetClassOf(rel)?.Name ?? "";

    static DocNode AssetEntry(RpcContext ctx, string rel, string typeName)
    {
        var m = DocNode.Map();
        m.Add("path", DocNode.Scal(rel));
        m.Add("guid", DocNode.Scal(ctx.Assets.PathToGuid(rel) ?? ""));
        m.Add("type", DocNode.Scal(typeName));
        string full = ImportPipeline.FullPath(rel);
        if (File.Exists(full))
            m.Add("bytes", DocNode.Scal(new FileInfo(full).Length.ToString(CultureInfo.InvariantCulture)));
        // Gorsel boyutu: once atlas bolgesi (mantiksal OrigW/OrigH), yoksa dosya basligi.
        // Doku yuklemesi tetiklenmez (RPC sahneye yan etki yapmaz).
        if (ctx.Assets.TryGetRegion(rel, out var region))
        {
            m.Add("width", DocNode.Scal(region.OrigW.ToString(CultureInfo.InvariantCulture)));
            m.Add("height", DocNode.Scal(region.OrigH.ToString(CultureInfo.InvariantCulture)));
            m.Add("atlas", DocNode.Scal("true"));
            var trimmed = DocNode.Map();
            trimmed.Add("x", DocNode.Scal(region.OffX.ToString(CultureInfo.InvariantCulture)));
            trimmed.Add("y", DocNode.Scal(region.OffY.ToString(CultureInfo.InvariantCulture)));
            trimmed.Add("w", DocNode.Scal(region.W.ToString(CultureInfo.InvariantCulture)));
            trimmed.Add("h", DocNode.Scal(region.H.ToString(CultureInfo.InvariantCulture)));
            m.Add("trimmed", trimmed);
        }
        else if (AssetDatabase.ImportTypeOf(rel) == typeof(Sprite)
            && (ImageHeader.TryReadSize(full, out int w, out int h)
                || (rel.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    && ImportPipeline.ArtifactPath(rel, "main") is string vec && ImageHeader.TryReadSize(vec, out w, out h))))
        {
            // Vektor kaynak: boyut import ciktisinda (DVEC basligi), kaynak svg'de degil.
            m.Add("width", DocNode.Scal(w.ToString(CultureInfo.InvariantCulture)));
            m.Add("height", DocNode.Scal(h.ToString(CultureInfo.InvariantCulture)));
            m.Add("atlas", DocNode.Scal("false"));
        }
        return m;
    }

    [RpcCommand("asset.importers", "Kayitli asset importer'lari (uzanti/tip/versiyon)")]
    static DocNode AssetImporters()
    {
        var r = DocNode.Map();
        var seq = DocNode.Seq();
        foreach (var e in ImportPipeline.Entries)
        {
            var m = DocNode.Map();
            m.Add("type", DocNode.Scal(e.Type.Name));
            m.Add("extensions", DocNode.Scal(string.Join(" ", e.Extensions)));
            m.Add("version", DocNode.Scal(e.Version.ToString(CultureInfo.InvariantCulture)));
            seq.Items.Add(m);
        }
        r.Add("importers", seq);
        return r;
    }

    [RpcCommand("asset.reimport", "Kaynagi zorla yeniden import eder, artifact listesi doner")]
    static DocNode AssetReimport(RpcContext ctx, string path)
    {
        GuardAssetPath(path); // traversal guard'i (yol Assets'e goreli)
        string rel = path.Replace('\\', '/');
        if (ImportPipeline.ImporterFor(rel) == null)
            throw new RpcError($"bu uzanti icin importer yok: {rel}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string dir = ImportPipeline.EnsureImported(rel, force: true);
        if (dir == null)
            throw new RpcError($"import basarisiz: {rel} (log.tail ile detay)");
        ctx.Assets.InvalidateImported(rel);
        AtlasSystem.RefreshAll(); // bu kaynagi alan atlas gruplari (stamp) yeniden paketlenir
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal("true"));
        r.Add("ms", DocNode.Scal(sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)));
        var arts = DocNode.Seq();
        foreach (var f in Directory.GetFiles(dir))
        {
            string name = Path.GetFileName(f);
            if (name == ".stamp")
                continue;
            var m = DocNode.Map();
            m.Add("name", DocNode.Scal(name));
            m.Add("bytes", DocNode.Scal(new FileInfo(f).Length.ToString(CultureInfo.InvariantCulture)));
            arts.Items.Add(m);
        }
        r.Add("artifacts", arts);
        return r;
    }

    [RpcCommand("project.open", "Baska bir projeyi acar: editor o yolla yeniden baslar, bu surec kapanir (RPC baglantisi kopar). Kaydedilmemis sahne/prefab varsa UNSAVED hatasi doner: scene.save ya da force=true (degisiklikler atilir)")]
    static DocNode ProjectOpen(string path, bool force = false)
    {
        string full = Path.GetFullPath(path);
        if (!ProjectSwitcher.IsProjectRoot(full))
            throw new RpcError($"proje klasoru degil: {path}");
        GuardUnsaved(force);
        ProjectSwitcher.Open(full);
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal("true"));
        r.Add("project", DocNode.Scal(full));
        return r;
    }

    [RpcCommand("project.recent", "Son acilan projeler (kullanici tercihleri) + repo Projects/* adaylari")]
    static DocNode ProjectRecent()
    {
        var prefs = EditorPrefs.Load();
        var r = DocNode.Map();
        r.Add("current", DocNode.Scal(App.Project?.Root ?? ""));
        var seq = DocNode.Seq();
        foreach (var p in prefs.recentProjects)
            seq.Items.Add(DocNode.Scal(p));
        r.Add("recent", seq);
        return r;
    }

    [RpcCommand("module.build", "Acik projeyi dinamik modul (.module.pak) olarak publish eder (Project > Build Module)")]
    static DocNode ModuleBuild()
    {
        bool ok = ModuleBuilder.Build();
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal(ok ? "true" : "false"));
        return r;
    }

    [RpcCommand("asset.buildPak", "Release asset paketini (Build/game.pak) uretir; sonuc log.tail'de. theme: bu temayla pisir (bos = PlayerSettings.activeTheme; 'default' = taban)")]
    static DocNode AssetBuildPak(string theme = null)
    {
        AssetPackBuilder.ThemeOverride = ThemeArg(theme);
        bool ok;
        try { ok = AssetPackBuilder.Build(); }
        finally { AssetPackBuilder.ThemeOverride = null; }
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal(ok ? "true" : "false"));
        return r;
    }

    // RPC tema argumani: null/bos = PlayerSettings.activeTheme (override yok), "default" = taban (""), ad = o tema.
    static string ThemeArg(string theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
            return null;
        theme = theme.Trim();
        if (string.Equals(theme, "default", StringComparison.OrdinalIgnoreCase))
            return "";
        if (!App.Project.Player.themes.Contains(theme))
            throw new RpcError("unknown theme: " + theme + " (theme.list)");
        return theme;
    }

    [RpcCommand("player.build", "game.pak + aotcompiler ile player uretir (target: windows=exe | android=Android Studio projesi | ios=Xcode projesi (Build/ios) | wasm=Build/wasm web cikti); sonuc build bitince doner. theme: pak temasi (bos = PlayerSettings.activeTheme)")]
    static DocNode PlayerBuild(string target = "windows", string theme = null)
    {
        int v0 = PlayerBuilder.Version;
        AssetPackBuilder.ThemeOverride = ThemeArg(theme); // PlayerBuilder pak'i senkron kurar, sonra temizlenir
        bool started;
        try { started = PlayerBuilder.Start(target); }
        finally { AssetPackBuilder.ThemeOverride = null; }
        if (!started)
        {
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal("false"));
            r.Add("error", DocNode.Scal("baslatilamadi; log.tail'e bakin"));
            return r;
        }
        RpcHost.Defer(() =>
        {
            if (PlayerBuilder.Version == v0)
                return null; // aotcompiler suruyor
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal(PlayerBuilder.LastOk ? "true" : "false"));
            r.Add("exe", DocNode.Scal(PlayerBuilder.LastExe));
            var tail = DocNode.Seq();
            var lines = PlayerBuilder.LastOutput.Split('\n');
            for (int i = Math.Max(0, lines.Length - 40); i < lines.Length; i++)
                if (lines[i].Length > 0)
                    tail.Items.Add(DocNode.Scal(lines[i]));
            r.Add("output", tail);
            return r;
        });
        return null;
    }

    [RpcCommand("code.build", "Oyun kodunu derler; sonuc (ok + hatalar) derleme bitince doner")]
    static DocNode CodeBuild()
    {
        int v0 = AssetWatcher.BuildVersion;
        AssetWatcher.RequestBuild();
        RpcHost.Defer(() =>
        {
            if (AssetWatcher.BuildVersion == v0)
                return null; // derleme suruyor
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal(AssetWatcher.LastBuildOk ? "true" : "false"));
            if (!AssetWatcher.LastBuildOk)
            {
                var errs = DocNode.Seq();
                foreach (var line in AssetWatcher.LastBuildOutput.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Contains(": error ") && errs.Items.Count < 30)
                        errs.Items.Add(DocNode.Scal(t));
                }
                r.Add("errors", errs);
            }
            return r;
        });
        return null;
    }

    [RpcCommand("log.tail", "Editor log'unun son N girisi (derleme/import hatalari burada)")]
    static DocNode LogTail(int count = 20)
    {
        var snap = new List<EditorLog.Entry>();
        EditorLog.Snapshot(snap);
        var seq = DocNode.Seq();
        for (int i = Math.Max(0, snap.Count - count); i < snap.Count; i++)
        {
            var e = DocNode.Map();
            e.Add("time", DocNode.Scal(snap[i].TimeText));
            e.Add("sev", DocNode.Scal(snap[i].Sev.ToString()));
            e.Add("msg", DocNode.Scal(snap[i].Message));
            seq.Items.Add(e);
        }
        var r = DocNode.Map();
        r.Add("entries", seq);
        return r;
    }

    // --- yardimcilar ---

    // Yol Assets/ disina kacamaz (path traversal guard'i).
    static string GuardAssetPath(string rel)
    {
        string root = Path.GetFullPath(App.Project.AssetsPath);
        string full = Path.GetFullPath(Path.Combine(root, rel));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new RpcError($"yol Assets disinda: {rel}");
        return full;
    }

    static SceneDoc.GoDoc RequireGo(RpcContext ctx, int id)
        => ctx.Scene.FindGo(id) ?? throw new RpcError($"GameObject yok: id={id}");

    static SceneDoc.CompDoc RequireComp(SceneDoc.GoDoc g, int index)
        => index >= 0 && index < g.Components.Count
            ? g.Components[index]
            : throw new RpcError($"component index gecersiz: {index} (go '{g.Name}' {g.Components.Count} component)");

    static string V3(Vec3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.x:R} {v.y:R} {v.z:R}");

    internal static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static Vec3 ParseV3(string s, string label)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            throw new RpcError($"'{label}' 'x y z' formatinda degil: '{s}'");
        return new Vec3(x, y, z);
    }
}
