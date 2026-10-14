namespace Digitoy.Mcp
{
    public static partial class Commands
    {
        [RpcCommand("rpc.guide", "START HERE (kodu bilmeyen ajan icin): is akisi, sahne kavramlari, deger bicimleri, play/domain reload davranisi, dogrulama dongusu")]
        static JNode Guide()
        {
            var r = JNode.Map();
            JNode Lines(params string[] lines)
            {
                var s = JNode.Seq();
                foreach (var l in lines) s.Push(JNode.Str_(l));
                return s;
            }
            r.Add("workflow", Lines(
                "1. project.info → scene.tree (kompakt hiyerarsi: id, ad, component tipleri) ya da scene.list/scene.open. Detay: scene.dump (transform'lar), prop.get(go, comp) (alan degerleri). scene.dump(props=true) BUYUK, gerekmedikce cagirma",
                "2. comp.types(filter) → comp.schema(type): alan adlari (temiz alias: 'Color', 'Sprite'), tip, default, enum degerleri, referans bicimi. Tahmin etme, semaya bak",
                "3. TOPLU kur: scene.apply(objects) — GO + components + props + children tek istekte (tek undo, atomik). Kucuk rotus: prop.set/transform.set. Coklu farkli komut: edit.batch",
                "4. view.device(name) → sim.run(frames, screenshot=true) → PNG'ye bak → input.tap(x,y) (screenshot pikseli == koordinat) → render.screenshot",
                "5. Hata donerse yol+sebep verir ve hicbir sey uygulanmaz; duzelt, tekrar gonder. scene.diff = son dump'tan beri ne degisti",
                "6. scene.save YALNIZ kullanici isterse (her sey undo'lu kalir: edit.undo)"));
            r.Add("scene", Lines(
                "GO = id (instanceID, oturum ici kararli) + parent (0=kok) + transform + components. Ayni parent'ta kardes sirasi = UI cizim sirasi (sonraki ustte); scene.reparent(insertBefore) ile siralanir",
                "UI: Canvas (+CanvasScaler, GraphicRaycaster) altinda RectTransform'lu GO'lar (scene.addGo ui=true; apply'da parent UI ise otomatik). Yerlesim: anchorMin/anchorMax/pivot (0..1) + anchoredPos + size. Gorsel: Image (Sprite, Color), Text/TextMeshProUGUI, Button (onClick kodla baglanir)",
                "2D: SpriteRenderer (Sprite, Color, SortingOrder), Collider2D tiklama icin sart. Kamera: Camera (Orthographic, OrthographicSize)",
                "Davranis = C# MonoBehaviour: asset.write('Scripts/X.cs') → code.build → (reload) → comp.add. Tiklama icin IPointerClickHandler ya da Button.onClick; sahne nesnesi icin Collider + IPointer*Handler ya da legacy OnMouseDown",
                "Prefab: prefab.open(path) → sahne komutlari prefab icinde calisir → prefab.close(save). Sahneye ornek: prefab.instantiate ya da apply'da prefab:'Assets/x.prefab'",
                "EventSystem: UI tiklamasi icin sahnede EventSystem GO'su olmali (scene.apply ile EventSystem + StandaloneInputModule/InputSystemUIInputModule ekle)"));
            r.Add("values", Lines(
                "Renk '#RRGGBBAA' ('#RRGGBB' kabul); vektor 'x y' / 'x y z' / 'x y z w' (bosluk); rect 'x y w h'; quaternion euler 'x y z'; bool true|false",
                "Enum TAM AD, buyuk-kucuk harfe duyarli (comp.schema values); flags 'A|B'. LayerMask: sayi ya da 'Default|UI'",
                "Referans: asset 'Assets/UI/btn.png' (alt-asset 'Assets/atlas.png#btn_idle') ya da guid; GO 'goId' ya da 'A/B' hiyerarsi yolu; component 'goId:Image:0' (ordinal = GO'daki kacinci Image). scene.apply/edit.batch icinde '$Ad' = ayni istekteki GO ('$Ad:Image:0')",
                "Alan adi: comp.schema'daki temiz ad ('Color'); ham Unity adi ('m_Color') da gecer. Ic ice yol: 'Tracks[0].Keys[1].Value' (index == eleman sayisi → sona ekle; null → sil / default). Tum liste: [..] degeri",
                "Texture → Sprite otomatik: Sprite alanina png yolu verilirse ayni dosyadaki Sprite alt-asseti secilir"));
            r.Add("play", Lines(
                "sim.run/input.*/render.screenshot play degilse play'i baslatir. Domain reload ACIKSA (varsayilan) play'e giris baglantiyi dusurur ve komut {state:'starting'} doner → ayni komutu TEKRAR cagir (kopru yeniden baglanir)",
                "ONERI: Edit > Project Settings > Editor > Enter Play Mode Options: Reload Domain KAPALI → komutlar tek seferde biter. project.info domainReloadOnPlay gosterir",
                "sim.run deterministik: play DURAKLATILMIS tutulur, her frame dt=1/fps ile adimlanir (Time.captureDeltaTime); sonda duraklatilmis kalir (pause=false serbest birakir). Gercek zamanli davranis istenirse play.pause(false)",
                "Play'de yapilan sahne degisiklikleri Unity kurali geregi play bitince KAYBOLUR; kalici degisiklik icin play.stop sonra duzenle",
                "asset.write(.cs) ve code.build domain reload yapar → baglanti kopar; code.build'i tekrar cagir: status=idle + hatalar"));
            r.Add("coordinates", Lines(
                "input.* koordinati MANTIKSAL nokta, sol-ust orijin, y asagi; uzay = Game View render cozunurlugu (view.get width/height; view.device/view.set belirler)",
                "Screenshot VARSAYILAN KUCULTULMUS (maxSize=1024, jpg): tiklama koordinati = region.x + px * toScreen (sonuctaki carpan; region yoksa 0). Detay gerekirse render.screenshot(region='x y w h') ile bolgeyi tam cozunurlukte al",
                "input.tap donusu: hit (raycast degdi mi), target (hiyerarsi yolu), source (ui|physics2d|physics), handlers (kosan EventSystem handler sayisi) — 0 ise davranis yok demektir",
                "Edit modunda screenshot = kamera render'i (Screen Space Overlay UI gorunmez); UI'yi gormek icin sim.run(screenshot=true)"));
            r.Add("rules", Lines(
                "Sonuctaki 'errors' alani = komut surerken Console'a dusen Error/Exception/Assert (oyun kodunun hatalari, sahne isi degil): kullaniciya ozetle bildir (msg + at), kendin duzeltmeye kalkma; tam liste log.tail(filter='Exception')",
                ".unity/.prefab dosyasini ASLA dogrudan yazma: scene.apply/edit.batch/prop.set — undo'lu, dogrulanir",
                "Sahneye dokunmadan ONCE yanindaki <sahne>.intent.md'yi oku (asset.read); is sonunda APPEND et (asset.write): prompt + yapi + invariantlar + dogrulama",
                "Asset bilgisi (yol/guid/tip, gorsel width/height, sprite'lar) icin asset.list/asset.info — ogrenmek icin kod yazip calistirma",
                "Hata/uyari icin log.tail(filter='Error'); derleme hatasi icin code.build",
                "asset.delete ve scene.save kullanici onayi ister"));
            return r;
        }
    }
}
