#if DE_EDITOR
using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// Coklu dil smoke testleri (docs/engine-notes.md LOCALIZATION): tablo serilesme, "<alan>@loc" bagi
// YAML + baked round-trip, dil degisimi (OnValidate yolu), placeholder/Format(args), Instantiate bag
// kopyasi, destroy'da bag temizligi, FormatString kurallari. Izole sahnelerde kosar.
public static class LocalizationTests
{
    static int _pass, _fail;

    [AddComponentMenu("")]
    public sealed class LocTestComp : Component
    {
        public string Title = "";
        public string Nick = "";
        public int Validated;
        protected internal override void OnValidate() => Validated++;
    }

    const string TableKey = "Localization.asset";

    // [Localize] enum: motor attribute'u + prefix arg'i.
    [Localize("error")]
    public enum TestServerError { InsufficientCoins = 1001, RoomFull = 1002 }

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        string prevLang = Localize.Language;
        int baseBindings = Localize.BindingCount;

        // --- Tablo: API + serilesme round-trip ---
        var table = new LocalizationTable { Languages = { "tr" } }; // en (kaynak), tr
        table.Set("menu.play", "en", "Play Now");
        table.Set("menu.play", "tr", "Oyna");
        table.Set("menu.hello", "en", "Hello");           // tr eksik -> fallback
        table.Set("hud.coins", "en", "{0} coins");
        table.Set("hud.coins", "tr", "{0} altın");
        Check(table.Get("menu.play", "tr") == "Oyna" && table.Get("menu.play", null) == "Play Now", "tablo get / kaynak dil");
        Check(table.Get("menu.hello", "tr") == "Hello", "eksik ceviri kaynak dile duser");
        Check(table.Get("yok", "tr") == null, "bilinmeyen key null");
        var node = ObjectSerializer.ToNode(table, null);
        var table2 = new LocalizationTable();
        ObjectSerializer.FromNode(table2, node, null);
        Check(table2.Languages.Count == 2 && table2.Entries.Count == 3 && table2.Get("hud.coins", "tr") == "{0} altın",
            "tablo YAML round-trip (List<string> + List<LocEntry>)");
        Check(table2.Rename("menu.play", "menu.start") && table2.Get("menu.start", "tr") == "Oyna" && table2.Find("menu.play") < 0,
            "rename indeksi tazeler");

        // --- FormatString ---
        Check(Localize.FormatString("{0} coins", new object[] { 12 }) == "12 coins", "format {0}");
        Check(Localize.FormatString("{1}-{0}", new object[] { "a", "b" }) == "b-a", "format sira");
        Check(Localize.FormatString("{{x}} {0:number:0.0}", new object[] { 1.25f }) == "{x} 1.3", "format kacis + number:0.0 yarim yukari");
        Check(Localize.FormatString("{0} {1}", new object[] { "a" }) == "a {1}", "eksik arg yer tutucu kalir");

        Localize.RegisterTable(TableKey, table);
        Localize.Language = null;
        Check(Localize.TableKeyFor("Scenes/Main.scene") == "Localization.asset"
            && Localize.TableKeyFor("Modules/Shop/Scenes/Shop.scene") == "Modules/Shop/Localization.asset"
            && Localize.TableKeyFor(null) == null, "kapsam tablosu anahtari");

        // --- YAML doc: @loc baglari ---
        string yaml = string.Join("\n",
            "scene: loc",
            "objects:",
            "  - id: 1",
            "    parent: 0",
            "    name: A",
            "    components:",
            "      - type: TextSprite",
            "        Text: Play Now",
            "        Text@loc: menu.play",
            "      - type: " + nameof(LocTestComp),
            "        Title: Hello",
            "        Title@loc: menu.hello",
            "        Nick: Guest",
            "  - id: 2",
            "    parent: 0",
            "    name: Coins",
            "    components:",
            "      - type: TextSprite",
            "        Text: '{0} coins'",
            "        Text@loc: hud.coins",
            "  - id: 3",
            "    parent: 0",
            "    name: Orphan",
            "    components:",
            "      - type: TextSprite",
            "        Text: Authored",
            "        Text@loc: no.such.key",
            "");
        var doc = SceneDoc.Parse(yaml, "Scenes/Loc.scene");
        Check(doc.LocTable == TableKey, "doc kapsam tablosu");

        var s = Scene.Create("loc-test");
        s.Catalog = catalog;
        Scene.SetActive(s);
        doc.Spawn(null, catalog, null);
        var play = Find<TextSprite>(s, "A");
        var comp = Find<LocTestComp>(s, "A");
        var coins = Find<TextSprite>(s, "Coins");
        var orphan = Find<TextSprite>(s, "Orphan");
        Check(play != null && play.Text == "Play Now", "spawn kaynak dil");
        Check(comp != null && comp.Title == "Hello" && comp.Nick == "Guest", "bagsiz alan dokunulmaz");
        Check(coins != null && coins.Text == "", "Play: args gelmeden sablon gizli");
        Check(orphan != null && orphan.Text == "[MISSING:no.such.key]", "tabloda olmayan key gorunur hata (authored onbellek yok)");
        Check(Localize.KeyOf(play, "Text") == "menu.play" && Localize.KeyOf(comp, "Nick") == null, "KeyOf");
        Check(Localize.BindingCount == baseBindings + 4, "bag sayisi");

        int validatedBefore = comp.Validated;
        Localize.Language = "tr";
        Check(play.Text == "Oyna", "dil degisimi alani yazar");
        Check(comp.Title == "Hello" && comp.Validated == validatedBefore, "fallback ayni metin: yeniden yazilmaz");
        coins.Format(12);
        Check(coins.Text == "12 altın", "Format(args) bagli sablon");
        Localize.Language = "en";
        Check(coins.Text == "12 coins" && play.Text == "Play Now", "dil degisince son args ile yeniden format");

        // --- Capture: baglar doc'a geri yazilir ---
        string captured = SceneDoc.Capture(s, catalog).ToYaml();
        Check(captured.Contains("Text@loc: menu.play") && captured.Contains("Title@loc: menu.hello") && !captured.Contains("Nick@loc"),
            "capture @loc prop'lari");
        Check(!captured.Contains("Title: Hello") && !captured.Contains("Text: Oyna") && !captured.Contains("Text: Play Now")
            && captured.Contains("Nick: Guest"), "capture: bagli alanin metni serilesmez (tek gercek tablo), bagsiz serilesir");
        // Eski doc'ta kalan onbellek metni (Text + Text@loc birlikte) YAML'a geri yazilmaz; baked'e de girmez.
        string reyaml = doc.ToYaml();
        Check(!reyaml.Contains("Text: Play Now") && reyaml.Contains("Text@loc: menu.play"), "ToYaml eski onbellek metnini dusurur");

        // --- Instantiate: klon bagi tasir ---
        var clone = GameObject.Instantiate(play.gameObject);
        var cloneText = clone.GetComponent<TextSprite>();
        Check(Localize.KeyOf(cloneText, "Text") == "menu.play", "klon bagi");
        Localize.Language = "tr";
        Check(cloneText.Text == "Oyna" && play.Text == "Oyna", "klon dil degisimini alir");
        Localize.Language = null;

        // --- Edit projeksiyonu: sablon gorunur ---
        var se = Scene.Create("loc-test-edit");
        se.Catalog = catalog;
        se.EditMode = true;
        Scene.SetActive(se);
        doc.Spawn(null, catalog, null);
        var coinsEdit = Find<TextSprite>(se, "Coins");
        Check(coinsEdit != null && coinsEdit.Text == "{0} coins", "edit modunda sablon gorunur");

        // --- Baked round-trip ---
        var baked = SceneBinary.Bake(doc, catalog);
        var sb = Scene.Create("loc-test-baked");
        sb.Catalog = catalog;
        Scene.SetActive(sb);
        int before = Localize.BindingCount;
        try
        {
            SceneBinary.Spawn(baked, null, catalog, null);
            var bPlay = Find<TextSprite>(sb, "A");
            var bComp = Find<LocTestComp>(sb, "A");
            var bCoins = Find<TextSprite>(sb, "Coins");
            Check(bPlay != null && bPlay.Text == "Play Now" && bComp.Nick == "Guest", "baked spawn");
            Check(Localize.BindingCount == before + 4, "baked bag sayisi");
            Localize.Language = "tr";
            Check(bPlay.Text == "Oyna" && bComp.Title == "Hello", "baked dil degisimi");
            bCoins.Format(7);
            Check(bCoins.Text == "7 altın", "baked Format");
        }
        catch (Exception e) { Check(false, "baked spawn HATA: " + e.Message); }
        Localize.Language = null;

        // --- Destroy temizligi ---
        Scene.SetActive(prev);
        Scene.Unload(sb);
        Scene.Unload(se);
        Scene.Unload(s);
        Check(Localize.BindingCount == baseBindings, "unload sonrasi bag kalmaz");

        // --- Editor store: slug / key uretimi / oneri (App'siz saf fonksiyonlar) ---
        Check(LocTableStore.Slug("Hemen Oyna!") == "hemen_oyna" && LocTableStore.Slug("PlayButton") == "play_button"
            && LocTableStore.Slug("Şükrü Çığ") == "sukru_cig", "slug + transliterasyon");
        var kt = new LocalizationTable { Languages = { "tr" } };
        Check(LocTableStore.NewKey(kt, "Login", "PlayButton", "text", false, "Play Now") == "login.play_button", "key baglamdan");
        Check(LocTableStore.NewKey(kt, "Login", "Text (3)", "text", false, "Welcome back, hero of the realm") == "login.welcome_back_hero_of",
            "jenerik GO adi -> metin slug'i (4 kelime)");
        Check(LocTableStore.NewKey(kt, "Login", "Card", "title", true, "x") == "login.card.title", "alan eki");
        kt.Set("login.play_button", "en", "Play Now");
        Check(LocTableStore.NewKey(kt, "Login", "PlayButton", "text", false, "Play") == "login.play_button_2", "cakisma _2");
        Check(LocTableStore.FindByText(kt, "en", "Play Now") == "login.play_button" && LocTableStore.FindByText(kt, "tr", "Play Now") == null,
            "ayni metin -> mevcut key");
        kt.Set("common.ok", "en", "OK");
        kt.Set("login.play_again", "tr", "Tekrar Oyna");
        var sug = new List<(string, string)>();
        LocTableStore.Suggest(kt, "en", "play", "login.play_button", sug);
        Check(sug.Count == 1 && sug[0].Item1 == "login.play_again", "oneri: key'de eslesme, mevcut key haric");
        LocTableStore.Suggest(kt, "en", "Tekrar", null, sug);
        Check(sug.Count == 1 && sug[0].Item1 == "login.play_again" && sug[0].Item2.StartsWith("tr:"), "oneri: diger dilde eslesme");

        // --- Formatter (LocFormat): sayi/zaman/tarih, dil ayiraclari, format.* varsayilanlari + tablo ezmesi ---
        object[] A(params object[] a) => a;
        Check(Localize.FormatString("{0:number}", A(1234567), "en", null) == "1,234,567"
            && Localize.FormatString("{0:number}", A(1234567), "tr", null) == "1.234.567", "number N0 dil ayiraci");
        Check(Localize.FormatString("{0:number:N2}", A(1234.5), "en", null) == "1,234.50"
            && Localize.FormatString("{0:number:0.##}", A(1.25f), "en", null) == "1.25"
            && Localize.FormatString("{0:number:0.##}", A(2.0), "en", null) == "2", "number N2 / 0.##");
        Check(Localize.FormatString("{0:number:short}", A(1500), "en", null) == "1.5K"
            && Localize.FormatString("{0:number:short}", A(1500), "tr", null) == "1,5B"
            && Localize.FormatString("{0:number:short:10000}", A(9999), "en", null) == "9,999"
            && Localize.FormatString("{0:number:short}", A(2_500_000_000L), "en", null) == "2.5B", "number short");
        Check(Localize.FormatString("{0:cap}", A("merhaba"), "tr", null) == "Merhaba", "cap");
        Check(Localize.FormatString("{0:time:remaining}", A(90061), "en", null) == "1D 1H 1M"
            && Localize.FormatString("{0:time:remaining:u2}", A(90061), "en", null) == "1D 1H"
            && Localize.FormatString("{0:time:remaining:long}", A(90061), "tr", null) == "1 Gün 1 Saat 1 Dakika"
            && Localize.FormatString("{0:time:remaining:u2:3600}", A(3599), "en", null) == "59:59"
            && Localize.FormatString("{0:time:remaining}", A(45), "en", null) == "45S", "time remaining");
        Check(Localize.FormatString("{0:time:ago}", A(120), "en", null) == "2 minutes ago"
            && Localize.FormatString("{0:time:ago}", A(3600), "tr", null) == "1 saat önce", "time ago");
        Check(Localize.FormatString("{0:time:date:yyyy-MM-dd HH:mm}", A(0), "en", null) == "1970-01-01 00:00"
            && Localize.FormatString("{0:time:date:d MMMM yyyy}", A(1717545600), "tr", null) == "5 Haziran 2024"
            && Localize.FormatString("{0:time:date:dddd}", A(1717545600), "en", null) == "Wednesday"
            && Localize.FormatString("{0:time:date:h:mm tt}", A(1717545600 + 14 * 3600 + 5 * 60), "en", null) == "2:05 PM", "time date desen");
        var prevNow = LocFormat.NowProvider;
        LocFormat.NowProvider = () => 1717545600 + 60;
        var ft = new LocalizationTable();
        Check(Localize.FormatString("{0:time:datelabel:relative}", A(1717545600), "en", null) == "Today"
            && Localize.FormatString("{0:time:datelabel:relative:HH:mm|}", A(1717545600), "en", null) == "Today at 00:00"
            && Localize.FormatString("{0:time:datelabel:relative}", A(1717545600 - 86400), "tr", null) == "Dün"
            && Localize.FormatString("{0:time:datelabel}", A(1717545600 - 86400 * 40), "en", null) == "April 26", "datelabel");
        ft.Set("format.time.today", "en", "Now!");
        Check(Localize.FormatString("{0:time:datelabel:relative}", A(1717545600), "en", ft) == "Now!", "format.* tablo ezmesi");
        LocFormat.NowProvider = prevNow;
        Check(Localize.Get("menu.play") == "Play Now" && Localize.Format("hud.coins", 3) == "3 coins", "global Get/Format (yuklu tablolar)");

        // --- [Localize] enum: slug/humanize, senkron (ekle/sil/prefix sahipligi), runtime key ---
        Check(Localize.EnumSlug("InsufficientCoins") == "insufficient_coins" && Localize.EnumSlug("HTTPError") == "http_error"
            && Localize.EnumSlug("ROOM_FULL") == "room_full" && Localize.EnumSlug("Error2x") == "error2x", "enum slug");
        Check(LocEnumSync.Humanize("InsufficientCoins") == "Insufficient coins" && LocEnumSync.Humanize("HTTPError") == "HTTP error"
            && LocEnumSync.Humanize("ROOM_FULL") == "Room full", "enum humanize");
        var enums = LocEnumSync.Scan(new[] { typeof(LocalizationTests).Assembly }, null);
        var errInfo = enums.Find(e => e.Type == typeof(TestServerError));
        Check(errInfo != null && errInfo.Prefix == "error" && errInfo.Keys.Contains("error.room_full")
            && enums.Find(e => e.Type == typeof(LocTestCustom.TestState))?.Prefix == "test_state", "enum tarama: prefix (attribute arg / tip adi)");
        table.Set("error.stale", "en", "Old"); // sahipli prefix altinda enum'da olmayan satir -> silinir
        table.Set("errors.keep", "en", "Keep"); // farkli prefix: dokunulmaz
        var syncRes = new LocEnumSync.Result();
        bool changed = LocEnumSync.SyncTable(table, enums, syncRes);
        Check(changed && table.Get("error.room_full", null) == "Room full" && table.Get("error.insufficient_coins", null) == "Insufficient coins"
            && table.Find("error.stale") < 0 && table.Get("errors.keep", null) == "Keep" && table.EnumPrefix("TestServerError") == "error",
            "enum sync: ekle + sahipli sil + Enums kaydi");
        Check(!LocEnumSync.SyncTable(table, enums, new LocEnumSync.Result()), "enum sync idempotent");
        table.Set("error.room_full", "tr", "Oda dolu");
        Check(Localize.EnumKey(TestServerError.RoomFull) == "error.room_full" && Localize.Get(TestServerError.RoomFull) == "Room full", "runtime EnumKey/Get (prefix tablodan)");
        Localize.Language = "tr";
        Check(Localize.Get(TestServerError.RoomFull) == "Oda dolu" && Localize.Get(TestServerError.InsufficientCoins) == "Insufficient coins", "enum Get dil + fallback");
        Localize.Language = null;
        Check(Localize.Get(LocTestCustom.TestState.Idle) == "Idle" && Localize.EnumPrefix("NoSuchEnum") == "no_such_enum", "enum Get: ozel attribute (ad kurali) + kayitsiz prefix tip adindan");
        // Enum kodda kalmadi -> kaydi ve tum satirlari gider.
        enums.RemoveAll(e => e.Type == typeof(TestServerError));
        Check(LocEnumSync.SyncTable(table, enums, new LocEnumSync.Result()) && table.Find("error.room_full") < 0 && table.EnumPrefix("TestServerError") == null
            && table.Get("errors.keep", null) == "Keep", "enum sync: enum silinince satirlar + kayit duser");
        var node3 = ObjectSerializer.ToNode(table, null);
        var table3 = new LocalizationTable();
        ObjectSerializer.FromNode(table3, node3, null);
        Check(table3.EnumPrefix("TestState") == "test_state", "Enums kaydi YAML round-trip");

        Localize.RegisterTable(TableKey, null);
        Localize.Language = prevLang;
        Console.WriteLine($"[localization] {_pass} PASS, {_fail} FAIL");
    }

    static T Find<T>(Scene s, string goName) where T : Component
    {
        for (int i = 0; i < s.RootCount; i++)
        {
            var go = s.GetRoot(i);
            if (go.name == goName && go.GetComponent<T>() is { } c)
                return c;
        }
        return null;
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[localization] FAIL: {name}"); }
    }
}

// Ad kurali: motora referanssiz paylasilan DLL'in kendi LocalizeAttribute'u (Preserve gibi) taninir; prefix yok -> slug(tip adi).
public static class LocTestCustom
{
    [AttributeUsage(AttributeTargets.Enum)]
    sealed class LocalizeAttribute : Attribute { }

    [Localize]
    public enum TestState { Idle, Busy }
}
#endif
