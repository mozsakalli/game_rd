using System;

namespace DigitoyEngine;

public enum LayoutMode { None = 0, Horizontal = 1, Vertical = 2 }
public enum LayoutAlign { Start = 0, Center = 1, End = 2 }
// Cocugun capraz eksende KENDI hizasi (CSS align-self): Auto = parent'in alignChildren'i;
// Stretch = capraz boyut parent'in capraz alanini doldurur (margin dusulur, min/max clamp).
public enum LayoutAlignSelf { Auto = 0, Start = 1, Center = 2, End = 3, Stretch = 4 }
// Ana eksende cocuk GRUBUNUN dagilimi (CSS justify-content). Yalniz grow'lu
// cocuk yokken anlamli: grow varsa kalan alan zaten cocuklara dagitilir.
// SpaceBetween = ilk/son kenara yapisik, kalan esit aralara; SpaceAround = her
// cocugun iki yaninda esit pay (kenarlarda yarim); SpaceEvenly = tum araliklar esit.
public enum LayoutJustify { Start = 0, Center = 1, End = 2, SpaceBetween = 3, SpaceAround = 4, SpaceEvenly = 5 }

// Eksen basina tasma davranisi (CSS overflow benzeri; Grow = mevcut varsayilan):
// Grow    = icerik kutuyu buyutur (preferred = max(authored, icerik)).
// Visible = kutu authored boyutta kalir, tasan icerik CIZILIR (CSS visible).
// Hidden  = kutu authored boyutta kalir, tasan icerik KIRPILIR (CSS hidden).
// Wrap    = flex-wrap: layout ANA ekseninde sigmayan cocuk sonraki satira/kolona
//           gecer; ana eksen authored boyutta kalir, capraz eksen (Grow ise)
//           satir toplamina buyur. Capraz eksende veya layout=None'da anlamsiz
//           (Visible gibi davranir). Kelime-kutusu richtext'in yerlesim temeli.
// Scroll   = Hidden + icerik ofseti (scrollOffset): cocuklar ana/capraz eksende kaydirilir,
//           kutu authored boyutta; jest/atalet ScrollView component'inde. Icerik olcusu
//           (ScrollMax) arrange'da hesaplanir; penceredeki cocuklar dis kalirsa cizilmez/tiklanmaz.
public enum OverflowMode { Grow = 0, Visible = 1, Hidden = 2, Wrap = 3, Scroll = 4 }

// Kutu arkaplan sprite'inin yerlesimi (CSS object-fit / background-repeat):
// Stretch = kutuya gerilir (9-slice yalniz bu modda), Contain = oran korunur, kutuya
// sigacak sekilde kuculur ve ortalanir, Cover = oran korunur, kutuyu doldurur, tasan
// kisim uv'den kirpilir, Tile = mantiksal boyutunda sol-ustten doser (CPU grid,
// TileCap'i asarsa karo buyutulur — shader/doku tekrar modu gerekmez, atlas uyesi olur).
public enum SpriteFit { Stretch = 0, Contain = 1, Cover = 2, Tile = 3 }

// Unity DrivenRectTransformTracker'in sorgu-tabanli muadili: bir component kendi
// GO'sunun transform'unu suruyorsa editor/araclar bunu JENERIK ogrenir — editor
// core somut tipleri (LayoutBox vs) tanimaz, yalniz bu arayuzu bilir.
[Flags]
public enum DrivenTransformProperties { None = 0, Position = 1 }

public interface ITransformDriver
{
    DrivenTransformProperties DrivenProperties { get; }
    // Pozisyon duzenlemesinin yonlendirilecegi serilesen Vec2 alani (orn.
    // "anchoredPos" — Unity'de move tool anchoredPosition yazar); null = kilitli.
    string PositionEditField { get; }
}

public static class TransformDriver
{
    // GO'daki ilk aktif surucunun surdugu transform ozellikleri (yoksa None).
    public static DrivenTransformProperties Driven(GameObject go, out ITransformDriver driver)
    {
        driver = null;
        var c = go?.GetComponentOfType(typeof(ITransformDriver));
        if (c == null || !c._enabled)
            return DrivenTransformProperties.None;
        driver = (ITransformDriver)c;
        return driver.DrivenProperties;
    }
}

// TEK kutu component'i (CSS-box modeli): anchor yerlesimi + flex dizme.
// Kutu agaci = GO hiyerarsisinde DOGRUDAN ebeveyn/cocuk kutular (arada kutusuz GO
// varsa zincir kopar — bilinclì katilik). Layout ciktisi YALNIZ canliya yazilir
// (transform.localPosition + cozulmus _rw/_rh); doc'a asla sizmaz.
// ZAMANLAMA = LAZY PULL (Transform._getWorldMatrix deseni): setter'lar yalniz
// kirletir, RectWidth/RectHeight/WorldCenter okuyan ANINDA cozer — "bir frame
// bekle" hack'i yok. Render oncesi LayoutSystem (ISceneSystem) okunmamis kirlileri toplar.
// Olcum sozlesmesi: preferred = max(authored, icerik) ve icerik YALNIZ cocuk
// kutulardan gelir (senkron/deterministik) — async veri layout'u suremez.
// STIL (StyleBox): kutu kendi arkaplanini cizebilir — fill (duz/dikey-yatay gradient),
// border (kenar basina genislik + duz/dikey-yatay gradient), kose basi radius, opsiyonel
// TEXTURE (stretch; slice9* > 0 ise 9-slice grid — atlas uyesiyse atlastan).
// CIZIM = INSTANCED QUAD EMISYONU (SDF parca modeli): kutu, UiPieces atlasindan
// kose hucresi + kenar falloff seridi + ic dolgu quad'lari yayar; border SDF RING
// katmani (USER.z), AA fwidth'ten her olcekte ~1px. Ayri batch sistemi YOK —
// ayni materyali paylasan bitisik quad'lar RenderQueue'da kendiliginden tek
// instanced draw'a merge olur (sprite yoluyla ayni mekanizma).
[ExecuteInEditMode]
[AddComponentMenu("Layout/Layout Box")]
public sealed unsafe partial class LayoutBox : Renderer, ITransformDriver
{
    // --- serilesen veri (backing alanlar; degisim property'lerden kirletir) ---
    [SerializeField] bool fitScreen;
    [SerializeField] float width, height;          // point-anchor: boyut; stretch: delta
    [SerializeField] Vec2 pivot = new(0.5f, 0.5f);
    [SerializeField] Vec2 anchorMin = new(0.5f, 0.5f);
    [SerializeField] Vec2 anchorMax = new(0.5f, 0.5f);
    [SerializeField] Vec2 anchoredPos;             // pivot'un anchor noktasina gore ofseti
    [SerializeField] float grow;                   // >0: parent grubun kalan alanindan agirlikli pay
    // >0: ana eksende tasma varsa agirlikli kucul (CSS flex-shrink; basis = preferred). Tek tur
    // dagitim (min'e takilan cocugun artigi yeniden dagitilmaz — bilincli yaklasim). grow'lu
    // cocuk shrink'e girmez (zaten leftover'dan pay alir). Wrap'te yok sayilir.
    [SerializeField] float shrink;
    // Dis bosluk (CSS margin; left, top, right, bottom). Flex'te ana eksen toplamina ve capraz
    // hizaya girer; stretch anchor'da kenarlardan dusulur; nokta anchor'da yok sayilir (absolute).
    [SerializeField] Vec4 margin;
    // Boyut sinirlari (0 = yok): olcum ve arrange'daki HER boyut yazimi tek clamp'ten gecer.
    [SerializeField] float minWidth, maxWidth, minHeight, maxHeight;
    [SerializeField] LayoutAlignSelf alignSelf;    // capraz eksende kendi hizam (Auto = parent'inki)
    [SerializeField] bool ignoreLayout;            // grup beni dizmesin (anchor'la yerlesirim)
    [SerializeField] LayoutMode layout;            // cocuk dizme modu
    [SerializeField] float spacing;
    [SerializeField] Vec4 padding; // left, top, right, bottom
    [SerializeField] LayoutAlign alignChildren = LayoutAlign.Start;
    [SerializeField] LayoutJustify justifyChildren = LayoutJustify.Start;
    [SerializeField] bool reverse;
    [SerializeField] OverflowMode overflowX;   // Grow degilse icerik bu ekseni buyutmez
    [SerializeField] OverflowMode overflowY;
    // Scroll ekseninde icerik kaydirmasi (pozitif = icerik yukari/sola kayar; CSS scrollLeft/Top).
    // Doc'taki deger = baslangic; runtime ScrollView yazar. Degisim boyutu etkilemez ->
    // MarkDirty degil, yalniz ArrangeChildren (alt agac FinishChild ile atlanir).
    [SerializeField] Vec2 scrollOffset;

    // --- stil (StyleBox) ---
    [SerializeField] Gradient fill = new(Color.Transparent);
    [SerializeField] Vec4 border; // left, top, right, bottom
    [SerializeField] Gradient borderFill = new(Color.Transparent);
    [SerializeField] Vec4 radius; // top-left, top-right, bottom-right, bottom-left

    // --- doku (opsiyonel arkaplan resmi; slice9* > 0 ise 9-slice) ---
    [SerializeField] Sprite sprite;
    [SerializeField, ShowIf(nameof(sprite))] SpriteFit spriteFit;
    [SerializeField, ShowIf(nameof(sprite))] Vec4 slice9; // left, top, right, bottom; kaynak doku pikseli (yalniz Stretch)

    float padLeft { get => padding.x; set => padding.x = value; }
    float padTop { get => padding.y; set => padding.y = value; }
    float padRight { get => padding.z; set => padding.z = value; }
    float padBottom { get => padding.w; set => padding.w = value; }
    float borderLeft { get => border.x; set => border.x = value; }
    float borderTop { get => border.y; set => border.y = value; }
    float borderRight { get => border.z; set => border.z = value; }
    float borderBottom { get => border.w; set => border.w = value; }
    float radiusTL { get => radius.x; set => radius.x = value; }
    float radiusTR { get => radius.y; set => radius.y = value; }
    float radiusBR { get => radius.z; set => radius.z = value; }
    float radiusBL { get => radius.w; set => radius.w = value; }
    float slice9Left { get => slice9.x; set => slice9.x = value; }
    float slice9Top { get => slice9.y; set => slice9.y = value; }
    float slice9Right { get => slice9.z; set => slice9.z = value; }
    float slice9Bottom { get => slice9.w; set => slice9.w = value; }

    // --- golge (alpha > 0 ise kutunun altina yumusak kopya cizilir) ---
    [SerializeField] Color shadowColor = Color.Transparent;
    [SerializeField] Vec2 shadowOffset = new(0, 4);
    [SerializeField] float shadowBlur = 8f;   // falloff yari genisligi (dunya px)
    [SerializeField] float shadowGrow;        // kutuyu her yonde genislet (spread)

    // --- ic golge (alpha > 0 ise dolgu ustune, border altina kenardan iceri sonen band) ---
    [SerializeField] Color innerShadowColor = Color.Transparent;
    [SerializeField] float innerShadowBlur = 8f;  // bandin derinligi (dunya px)

    // --- isik/gloss (alpha > 0 ise ust-ortaya asili yumusak radyal parlama; EmitRadial, +4 quad) ---
    [SerializeField] Color lightColor = Color.Transparent;
    [SerializeField] float lightWidth = 0.9f;        // kutu genisligi orani
    [SerializeField] float lightHeight = 24f;        // dunya px
    [SerializeField] Vec2 lightOffset = new(0, 2);   // x: merkezden kayma, y: ust kenardan asagi

    // --- cozulmus durum ---
    float _rw, _rh;
    float _placedX, _placedY;   // layout'un son yazdigi lokal pozisyon
    bool _hasPlaced;            // false = pozisyon authored (bagimsiz kutu)
    internal bool _dirty = true;
    internal bool _queued;
    // Son cozumde beni dizen ebeveyn kutu (ResolveTop/FinishChild yazar). Cizim
    // yolu (klip zinciri) her frame GetComponent ile ata aramak yerine bunu izler;
    // reparent/overflow degisimi relayout tetikledigi icin hep guncel.
    LayoutBox _parentBox;

    // --- stil geometri durumu ---
    // Cizim sirasi icin ozel bir sey YOK: Scene.Render hiyerarsi DFS'iyle submit eder,
    // ayni katmanda parent < cocuk kendiliginden; katman = Renderer._effectiveOrder.

    // --- public API (property'ler kirletir; alanlara dogrudan erisim yok) ---
    public bool FitScreen { get => fitScreen; set { if (fitScreen != value) { fitScreen = value; MarkDirty(); } } }
    public float Width { get => width; set { if (width != value) { width = value; MarkDirty(); } } }
    public float Height { get => height; set { if (height != value) { height = value; MarkDirty(); } } }
    public Vec2 Pivot { get => pivot; set { if (pivot.x != value.x || pivot.y != value.y) { pivot = value; MarkDirty(); } } }
    public Vec2 AnchorMin { get => anchorMin; set { if (anchorMin.x != value.x || anchorMin.y != value.y) { anchorMin = value; MarkDirty(); } } }
    public Vec2 AnchorMax { get => anchorMax; set { if (anchorMax.x != value.x || anchorMax.y != value.y) { anchorMax = value; MarkDirty(); } } }
    public Vec2 AnchoredPos { get => anchoredPos; set { if (anchoredPos.x != value.x || anchoredPos.y != value.y) { anchoredPos = value; MarkDirty(); } } }
    public float Grow { get => grow; set { if (grow != value) { grow = value; MarkDirty(); } } }
    public float Shrink { get => shrink; set { if (shrink != value) { shrink = value; MarkDirty(); } } }
    public Vec4 Margin { get => margin; set { margin = value; MarkDirty(); } }
    public float MarginLeft { get => margin.x; set { if (margin.x != value) { margin.x = value; MarkDirty(); } } }
    public float MarginTop { get => margin.y; set { if (margin.y != value) { margin.y = value; MarkDirty(); } } }
    public float MarginRight { get => margin.z; set { if (margin.z != value) { margin.z = value; MarkDirty(); } } }
    public float MarginBottom { get => margin.w; set { if (margin.w != value) { margin.w = value; MarkDirty(); } } }
    public float MarginAll { get => margin.x; set { margin = new Vec4(value, value, value, value); MarkDirty(); } }
    public float MinWidth { get => minWidth; set { if (minWidth != value) { minWidth = value; MarkDirty(); } } }
    public float MaxWidth { get => maxWidth; set { if (maxWidth != value) { maxWidth = value; MarkDirty(); } } }
    public float MinHeight { get => minHeight; set { if (minHeight != value) { minHeight = value; MarkDirty(); } } }
    public float MaxHeight { get => maxHeight; set { if (maxHeight != value) { maxHeight = value; MarkDirty(); } } }
    public LayoutAlignSelf AlignSelf { get => alignSelf; set { if (alignSelf != value) { alignSelf = value; MarkDirty(); } } }
    public bool IgnoreLayout { get => ignoreLayout; set { if (ignoreLayout != value) { ignoreLayout = value; MarkDirty(); } } }
    public LayoutMode Layout { get => layout; set { if (layout != value) { layout = value; MarkDirty(); } } }
    public float Spacing { get => spacing; set { if (spacing != value) { spacing = value; MarkDirty(); } } }
    public Vec4 Padding { get => padding; set { padding = value; MarkDirty(); } }
    public float PadLeft { get => padLeft; set { if (padLeft != value) { padLeft = value; MarkDirty(); } } }
    public float PadTop { get => padTop; set { if (padTop != value) { padTop = value; MarkDirty(); } } }
    public float PadRight { get => padRight; set { if (padRight != value) { padRight = value; MarkDirty(); } } }
    public float PadBottom { get => padBottom; set { if (padBottom != value) { padBottom = value; MarkDirty(); } } }
    public LayoutAlign AlignChildren { get => alignChildren; set { if (alignChildren != value) { alignChildren = value; MarkDirty(); } } }
    public LayoutJustify JustifyChildren { get => justifyChildren; set { if (justifyChildren != value) { justifyChildren = value; MarkDirty(); } } }
    public bool Reverse { get => reverse; set { if (reverse != value) { reverse = value; MarkDirty(); } } }
    public OverflowMode OverflowX { get => overflowX; set { if (overflowX != value) { overflowX = value; MarkDirty(); } } }
    public OverflowMode OverflowY { get => overflowY; set { if (overflowY != value) { overflowY = value; MarkDirty(); } } }
    public OverflowMode Overflow { get => overflowX; set { OverflowX = value; OverflowY = value; } }

    // --- scroll ---
    public bool ScrollsX => overflowX == OverflowMode.Scroll;
    public bool ScrollsY => overflowY == OverflowMode.Scroll;
    // Icerik - gorunur alan (>= 0); arrange'da yazilir. Ofsetin dogal araligi [0, ScrollMax].
    public Vec2 ScrollMax { get { ResolveIfDirty(); return _scrollMax; } }
    Vec2 _scrollMax;

    // Clamp'siz yazim (overscroll/yay ScrollView'de). Boyut degismez: kirletmek yerine
    // cocuklar dogrudan yeniden yerlesir; kutu zaten kirliyse bekleyen cozum yeni degeri okur.
    public Vec2 ScrollOffset
    {
        get => scrollOffset;
        set
        {
            if (scrollOffset.x == value.x && scrollOffset.y == value.y)
                return;
            scrollOffset = value;
            if (!_dirty && _gameObject != null && !_destroyed)
                ArrangeChildren();
        }
    }

    // Stil degisimi layout'u ETKILEMEZ; quad emisyonu her frame stil alanlarindan
    // okur — cache yok, kirletme gerekmez.
    public Gradient Fill { get => fill; set => fill = value; }
    public Vec4 Border { get => border; set => border = value; }
    public float BorderLeft { get => borderLeft; set => borderLeft = value; }
    public float BorderTop { get => borderTop; set => borderTop = value; }
    public float BorderRight { get => borderRight; set => borderRight = value; }
    public float BorderBottom { get => borderBottom; set => borderBottom = value; }
    public float BorderWidth { get => borderLeft; set { borderLeft = borderTop = borderRight = borderBottom = value; } }
    public Gradient BorderFill { get => borderFill; set => borderFill = value; }
    public Vec4 Radius { get => radius; set => radius = value; }
    public float RadiusTL { get => radiusTL; set => radiusTL = value; }
    public float RadiusTR { get => radiusTR; set => radiusTR = value; }
    public float RadiusBR { get => radiusBR; set => radiusBR = value; }
    public float RadiusBL { get => radiusBL; set => radiusBL = value; }
    public float CornerRadius { get => radiusTL; set { radiusTL = radiusTR = radiusBR = radiusBL = value; } }

    public Sprite Sprite { get => sprite; set => sprite = value; }
    public SpriteFit SpriteFit { get => spriteFit; set => spriteFit = value; }
    public Vec4 Scale9 { get => slice9; set => slice9 = value; }
    public float Slice9Left { get => slice9Left; set => slice9Left = value; }
    public float Slice9Top { get => slice9Top; set => slice9Top = value; }
    public float Slice9Right { get => slice9Right; set => slice9Right = value; }
    public float Slice9Bottom { get => slice9Bottom; set => slice9Bottom = value; }
    public float Slice9 { get => slice9Left; set { slice9Left = slice9Top = slice9Right = slice9Bottom = value; } }

    public Color ShadowColor { get => shadowColor; set => shadowColor = value; }
    public Vec2 ShadowOffset { get => shadowOffset; set => shadowOffset = value; }
    public float ShadowBlur { get => shadowBlur; set => shadowBlur = value; }
    public float ShadowGrow { get => shadowGrow; set => shadowGrow = value; }

    public Color InnerShadowColor { get => innerShadowColor; set => innerShadowColor = value; }
    public float InnerShadowBlur { get => innerShadowBlur; set => innerShadowBlur = value; }

    public Color LightColor { get => lightColor; set => lightColor = value; }
    public float LightWidth { get => lightWidth; set => lightWidth = value; }
    public float LightHeight { get => lightHeight; set => lightHeight = value; }
    public Vec2 LightOffset { get => lightOffset; set => lightOffset = value; }

    // --- driven transform (Unity paritesi) ---
    // fitScreen veya parent kutu varsa pozisyonu layout yazar → disaridan kilitli.
    public DrivenTransformProperties DrivenProperties
        => fitScreen || ParentBox() != null
            ? DrivenTransformProperties.Position
            : DrivenTransformProperties.None;

    // Grup dizmiyorsa (anchor yerlesimi) pozisyon anchoredPos uzerinden duzenlenir;
    // grup diziyorsa veya fitScreen ise tamamen kilitli.
    public string PositionEditField
    {
        get
        {
            if (fitScreen)
                return null;
            var p = ParentBox();
            if (p == null)
                return null;
            return p.layout == LayoutMode.None || ignoreLayout ? "anchoredPos" : null;
        }
    }

    // Cozulmus rect: okumak COZER (lazy pull) — ilk frame dahil hep guncel.
    public float RectWidth { get { ResolveIfDirty(); return _rw; } }
    public float RectHeight { get { ResolveIfDirty(); return _rh; } }

    // Kutunun dunya-uzayi merkezi (2D; rotation/scale dahil).
    public Vec2 WorldCenter
    {
        get
        {
            ResolveIfDirty();
            float lx = (0.5f - pivot.x) * _rw, ly = (0.5f - pivot.y) * _rh;
            ref var m = ref transform._getWorldMatrix();
            return new Vec2(
                m.m[0] * lx + m.m[4] * ly + m.m[12],
                m.m[1] * lx + m.m[5] * ly + m.m[13]);
        }
    }

    // Rect pivot'a gore kayiktir: lokal merkez = pivot ofseti.
    public override bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        ResolveIfDirty();
        center = new Vec2((0.5f - pivot.x) * _rw, (0.5f - pivot.y) * _rh);
        halfSize = new Vec2(_rw * 0.5f, _rh * 0.5f);
        return true;
    }

    // Pick: rect vurusu + ata Hidden/Scroll klibi (kirpilan parca tiklanmaz: cizim = tiklama).
    // Klip yalniz vurusta hesaplanir (down aninda, zincir yuruyusu).
    public override bool HitTest(in Ray ray, out float distance, out Vec3 point)
    {
        if (!base.HitTest(in ray, out distance, out point))
            return false;
        ref var wm = ref transform._getWorldMatrix();
        ComputeClip(in wm);
        if (!_hasClip)
            return true;
        if (!Mat4.Inverse(ref wm, out var inv))
            return true;
        var lp = Mat4.TransformPoint(ref inv, point);
        return lp.x >= _clX0 && lp.x <= _clX1 && lp.y >= _clY0 && lp.y <= _clY1;
    }

    protected internal override void OnEnable()
    {
        HookFonts();
        MarkDirty();
        ApplyStyle();
    }

    protected internal override void OnDisable()
    {
        UnhookFonts();
        ParentBox()?.MarkDirty();
    }

    // Editor Inspector'i alanlara dogrudan yazar; sonrasinda bunu cagirir.
    protected internal override void OnValidate()
    {
        ResolveFont();
        MarkDirty();
        ValidateStyle();
    }

    // Kirlet + zinciri yukari kirlet (autosize etkisi) + sweep kuyruguna gir.
    // INVARIANT: dirty kutu ⇒ tum ata kutulari dirty (cozum hep zincir tepesinden).
    // Olcum cache'i dirty ile birlikte duser: zaten dirty olan atanin cache'i o
    // anda dusmustu ve cozum tamamlanana kadar yeniden dolmaz (cozum senkron).
    public void MarkDirty()
    {
        var b = this;
        while (b != null && !b._dirty)
        {
            b._dirty = true;
            b._prefWValid = b._prefHValid = false;
            b = b.ParentBox();
        }
        QueueSweep();
    }

    internal void QueueSweep()
    {
        if (_queued)
            return;
        var s = _gameObject?._scene;
        if (s == null)
            return;
        _queued = true;
        s.GetSystem<LayoutSystem>().Queue(this);
    }

    // Reparent iki tarafi da kirletir (eski grup yeniden dizilsin; yeni taraf
    // ChildrenChanged ile). Core Transform layout'u tanimaz — jenerik mesajlar.
    protected internal override void OnTransformParentChanged() => MarkDirty();
    protected internal override void OnTransformChildrenChanged() => MarkDirty();

    // Dogrudan ebeveyn GO'daki aktif kutu (katilik kurali: atlama yok).
    LayoutBox ParentBox()
    {
        var go = transform.parent?._gameObject;
        if (go == null || go._destroyed)
            return null;
        var b = go.GetComponent<LayoutBox>();
        return b != null && b._enabled && !b._destroyed ? b : null;
    }

    // fitScreen kaynagi: bu kutunun LAYER'ini cizen kameranin gorus rect'i (kutu
    // projeksiyon tipini bilmez); kamera yoksa eski davranis (0,0,Screen boyutu).
    bool FitRect(out float x, out float y, out float w, out float h)
    {
        var s = _gameObject?._scene;
        if (s == null) { x = y = w = h = 0; return false; }
        var cam = s.CameraForLayer(_gameObject.layer);
        if (cam != null)
            cam.GetWorldRect(s.ScreenWidth, s.ScreenHeight, out x, out y, out w, out h);
        else { x = 0; y = 0; w = s.ScreenWidth; h = s.ScreenHeight; }
        return true;
    }

    // Son uygulanan fit rect orijini (boyut _rw/_rh'de) — degisim tespiti icin.
    float _fitX, _fitY;

    public void ResolveIfDirty()
    {
        // Ekran kutusu kamera rect degisimini cozumde yakalar (resize/pan/zoom
        // frame'inde pull da dogru okur). Maliyet: birkac float kiyasi.
        if (fitScreen && FitRect(out float fx, out float fy, out float fw, out float fh)
            && (_rw != fw || _rh != fh || _fitX != fx || _fitY != fy))
            _dirty = true;
        if (!_dirty)
        {
            // Temiz kutu, kirli atanin altinda olabilir (ata buyuyunce stretch
            // cocuk da degisir): son cozumun ebeveyn zincirinde kirli ata ara.
            // Ucuz pointer yuruyusu (GetComponent yok); temiz kutunun zinciri
            // guncel (reparent kendisini kirletir).
            LayoutBox dirtyTop = null;
            for (var p = _parentBox; p != null && p._enabled && !p._destroyed; p = p._parentBox)
                if (p._dirty)
                    dirtyTop = p;
            dirtyTop?.ResolveTop();
            return;
        }
        var top = this;
        for (var p = ParentBox(); p != null; p = p.ParentBox())
            if (p._dirty)
                top = p;
        top.ResolveTop();
    }

    // Zincir tepesi: kendi rect'ini kur, alt agaci diz. Dirty bayraklari cozumun
    // BASINDA dusurulur (cozum ici okuma yari-guncel deger gorur — reentrancy guard).
    void ResolveTop()
    {
        _dirty = false;
        var parent = ParentBox();
        _parentBox = parent;
        if (fitScreen)
        {
            if (FitRect(out float fx, out float fy, out float fw, out float fh))
            {
                _rw = fw;
                _rh = fh;
                _fitX = fx;
                _fitY = fy;
                // Kamera gorus rect'ini kaplasin: pivot noktasi rect orijini + pivot*boyut.
                SetPos(fx + pivot.x * fw, fy + pivot.y * fh);
            }
        }
        else if (parent != null)
        {
            // Guvenlik dali: invariant geregi normalde tepe kutunun ebeveyni yoktur.
            PlaceInParent(parent);
        }
        else
        {
            // Bagimsiz kutu: boyut preferred, pozisyona DOKUNULMAZ (authored kalir).
            _rw = PreferredW();
            _rh = PreferredH();
            _hasPlaced = false;
        }
        ArrangeChildren();
    }

    // --- olcum (measure): asagidan yukari, yalniz kutulardan ---
    // Cache: alt agac olcumu pahali (metin satir kirma, wrap taramasi) ve ayni kutu
    // bir cozumde atalarinin her seviyesinden tekrar olculur (O(n*derinlik)).
    // MarkDirty zinciri yukari kirletirken cache'i de dusurur -> tutarlilik dirty
    // invariantindan gelir; cozum sirasinda her kutu en fazla 1 kez olculur.
    float _prefW, _prefH;
    bool _prefWValid, _prefHValid;

    float PreferredW()
    {
        if (_prefWValid)
            return _prefW;
        _prefW = ClampW(ComputePreferredW());
        _prefWValid = true;
        return _prefW;
    }

    float PreferredH()
    {
        if (_prefHValid)
            return _prefH;
        _prefH = ClampH(ComputePreferredH());
        _prefHValid = true;
        return _prefH;
    }

    // min/max (0 = yok). Tum boyut yazimlari buradan gecer; default'ta 2 kiyas.
    float ClampW(float w) => maxWidth > 0 && w > maxWidth ? maxWidth : (w < minWidth ? minWidth : w);
    float ClampH(float h) => maxHeight > 0 && h > maxHeight ? maxHeight : (h < minHeight ? minHeight : h);

    // Cocugun ana/capraz eksende layout'a girdigi dis boyut (preferred + margin).
    float OuterW() => PreferredW() + margin.x + margin.z;
    float OuterH() => PreferredH() + margin.y + margin.w;

    float ComputePreferredW()
    {
        // Grow disinda icerik kutuyu buyutmez: eksen boyutu salt authored.
        if (overflowX != OverflowMode.Grow)
            return width;
        // Icerik = cocuk kutular + metin (max(authored, icerik) sozlesmesi).
        float aw = MathF.Max(width, TextPreferredW());
        if (layout == LayoutMode.None)
            return aw;
        float pad = padLeft + padRight;
        // Dikey wrap: kolonlar yana dizilir — genislik = kolon max'lerinin toplami.
        if (layout == LayoutMode.Vertical && overflowY == OverflowMode.Wrap)
            return MathF.Max(aw, WrapContentCross(false) + pad);
        if (layout == LayoutMode.Horizontal)
        {
            float sum = 0;
            int n = 0;
            for (var t = transform.FirstChild; t != null; t = t.NextSibling)
            {
                var b = BoxOf(t);
                if (b == null || b.ignoreLayout)
                    continue;
                n++;
                if (b.grow <= 0)
                    sum += b.OuterW(); // grow'lu cocuk olcume 0 girer (payini arrange verir)
            }
            return MathF.Max(aw, sum + pad + spacing * Math.Max(0, n - 1));
        }
        float mx = 0;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null || b.ignoreLayout)
                continue;
            mx = MathF.Max(mx, b.OuterW());
        }
        return MathF.Max(aw, mx + pad);
    }

    float ComputePreferredH()
    {
        if (overflowY != OverflowMode.Grow)
            return height;
        float ah = MathF.Max(height, TextPreferredH());
        if (layout == LayoutMode.None)
            return ah;
        float pad = padTop + padBottom;
        // Yatay wrap: satirlar alta dizilir — yukseklik = satir max'lerinin toplami.
        if (layout == LayoutMode.Horizontal && overflowX == OverflowMode.Wrap)
            return MathF.Max(ah, WrapContentCross(true) + pad);
        if (layout == LayoutMode.Vertical)
        {
            float sum = 0;
            int n = 0;
            for (var t = transform.FirstChild; t != null; t = t.NextSibling)
            {
                var b = BoxOf(t);
                if (b == null || b.ignoreLayout)
                    continue;
                n++;
                if (b.grow <= 0)
                    sum += b.OuterH();
            }
            return MathF.Max(ah, sum + pad + spacing * Math.Max(0, n - 1));
        }
        float mx = 0;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null || b.ignoreLayout)
                continue;
            mx = MathF.Max(mx, b.OuterH());
        }
        return MathF.Max(ah, mx + pad);
    }

    // FP toleransi (dunya px): olcum/arrange ayni sayilari farkli yoldan uretince
    // tam sigan satir ULP farkiyla sahte kirilmasin (metindeki fitEps'in kutu esi).
    const float WrapEps = 0.01f;

    // Wrap icerik olcumu: capraz eksen = satir max'lerinin toplami + satir arasi
    // spacing. Ana eksen genisligi AUTHORED'dan okunur (stretch/grow'da nihai deger
    // olcum aninda bilinmez — textWrap ile ayni bilinccli yaklasiklik); authored <= 0
    // ise tek satir varsayilir. Kirilim kurali ArrangeWrapped taramasiyla birebir.
    float WrapContentCross(bool horiz)
    {
        float availMain = horiz ? width - padLeft - padRight : height - padTop - padBottom;
        if (availMain <= 0)
            availMain = float.MaxValue;
        float sum = 0, used = 0, lineCross = 0;
        int n = 0, lines = 0;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null || b.ignoreLayout)
                continue;
            float main = horiz ? b.OuterW() : b.OuterH();
            float add = (n > 0 ? spacing : 0) + main;
            if (n > 0 && used + add > availMain + WrapEps)
            {
                sum += (lines++ > 0 ? spacing : 0) + lineCross;
                used = 0;
                n = 0;
                lineCross = 0;
                add = main;
            }
            used += add;
            n++;
            lineCross = MathF.Max(lineCross, horiz ? b.OuterH() : b.OuterW());
        }
        if (n > 0)
            sum += (lines > 0 ? spacing : 0) + lineCross;
        return sum;
    }

    // --- yerlestirme (arrange): yukaridan asagi ---

    // Anchor yerlesimi: nokta anchor'da boyut = preferred, stretch'te span*parent + delta -
    // margin (kenarlardan dusulur; min/max clamp'i kutuyu anchor araliginda sol/ust'e yaslar).
    // transform pozisyonu = pivot noktasinin parent-lokal yeri (rect pivot etrafinda).
    void PlaceInParent(LayoutBox parent)
    {
        float pw = parent._rw, ph = parent._rh;
        float tlx = -parent.pivot.x * pw, tly = -parent.pivot.y * ph;

        float w, x;
        if (anchorMin.x == anchorMax.x)
        {
            w = PreferredW();
            x = tlx + anchorMin.x * pw + anchoredPos.x;
        }
        else
        {
            w = ClampW((anchorMax.x - anchorMin.x) * pw + width - margin.x - margin.z);
            x = tlx + anchorMin.x * pw + anchoredPos.x + margin.x + pivot.x * w;
        }

        float h, y;
        if (anchorMin.y == anchorMax.y)
        {
            h = PreferredH();
            y = tly + anchorMin.y * ph + anchoredPos.y;
        }
        else
        {
            h = ClampH((anchorMax.y - anchorMin.y) * ph + height - margin.y - margin.w);
            y = tly + anchorMin.y * ph + anchoredPos.y + margin.y + pivot.y * h;
        }

        _rw = w;
        _rh = h;
        SetPos(x, y);
    }

    void SetPos(float x, float y)
    {
        _placedX = x;
        _placedY = y;
        _hasPlaced = true;
        var lp = transform.localPosition;
        if (lp.x != x || lp.y != y)
            transform.localPosition = new Vec3(x, y, lp.z);
    }

    // Cozumde cocuga yazilan rect sonrasi: alt agaca yalniz GEREKINCE inilir.
    // Cocuklar ebeveynin BOYUTUNA gore yerlesir (pozisyonuna degil): boyut ayni ve
    // cocuk kirli degilse (invariant: alt agacta kirli varsa cocuk da kirli) alt
    // agac zaten gecerli — tum agac relayout'u yalniz etkilenen dala iner.
    void FinishChild(LayoutBox b, bool wasDirty, float oldW, float oldH)
    {
        b._parentBox = this;
        b._dirty = false;
        if (wasDirty || b._rw != oldW || b._rh != oldH)
            b.ArrangeChildren();
    }

    void ArrangeChildren()
    {
        // Scroll ofseti: cocuklar bu kadar kaydirilir (yalniz Scroll ekseninde).
        float offX = ScrollsX ? scrollOffset.x : 0f, offY = ScrollsY ? scrollOffset.y : 0f;
        ArrangeChildrenCore(offX, offY);
        if (ScrollsX || ScrollsY)
            MeasureScrollContent(offX, offY);
    }

    // Icerik olcusu = katilan cocuklarin sag/alt kenar max'i (margin dahil) + pad; ofset
    // geri eklenir (pozisyonlar kaydirilmis). Yalniz scroll kutusunda, cocuk basina 4 flop.
    void MeasureScrollContent(float offX, float offY)
    {
        float tlx = -pivot.x * _rw, tly = -pivot.y * _rh;
        float right = tlx, bottom = tly;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null || b.ignoreLayout)
                continue;
            right = MathF.Max(right, b._placedX + (1f - b.pivot.x) * b._rw + b.margin.z);
            bottom = MathF.Max(bottom, b._placedY + (1f - b.pivot.y) * b._rh + b.margin.w);
        }
        float contentW = right - tlx + padRight + offX, contentH = bottom - tly + padBottom + offY;
        _scrollMax = new Vec2(ScrollsX ? MathF.Max(0f, contentW - _rw) : 0f,
                              ScrollsY ? MathF.Max(0f, contentH - _rh) : 0f);
    }

    void ArrangeChildrenCore(float offX, float offY)
    {
        if (layout == LayoutMode.None)
        {
            for (var t = transform.FirstChild; t != null; t = t.NextSibling)
            {
                var b = BoxOf(t);
                if (b == null)
                    continue;
                bool wasDirty = b._dirty;
                float ow = b._rw, oh = b._rh;
                b.PlaceInParent(this);
                if (!b.ignoreLayout && (offX != 0f || offY != 0f))
                    b.SetPos(b._placedX - offX, b._placedY - offY); // dekor (ignoreLayout) kaymaz
                FinishChild(b, wasDirty, ow, oh);
            }
            return;
        }

        bool horiz = layout == LayoutMode.Horizontal;

        // Ana eksen Wrap ise satir dizimi ayri yol (grow bu modda yok sayilir).
        if ((horiz ? overflowX : overflowY) == OverflowMode.Wrap)
        {
            ArrangeWrapped(horiz, offX, offY);
            return;
        }

        // 1. gecis: sabit toplam (dis boyut = preferred + margin) + grow/shrink agirlik toplami.
        float fixedSum = 0, growSum = 0, shrinkSum = 0;
        int count = 0;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null || b.ignoreLayout)
                continue;
            count++;
            if (b.grow > 0)
                growSum += b.grow;
            else
            {
                float pref = horiz ? b.PreferredW() : b.PreferredH();
                fixedSum += pref + (horiz ? b.margin.x + b.margin.z : b.margin.y + b.margin.w);
                shrinkSum += b.shrink * pref; // CSS: shrink agirligi = shrink * basis
            }
        }

        float availMain = (horiz ? _rw - padLeft - padRight : _rh - padTop - padBottom)
            - spacing * Math.Max(0, count - 1);
        float leftover = MathF.Max(0, availMain - fixedSum);
        // Tasma + shrink'li cocuk varsa: acik, shrink*pref agirligiyla dagitilir. Tasma yoksa
        // maliyet bir kiyas. Min clamp'e takilan cocuk olursa (nadir) TEK ek tur: takilanlarin
        // sabit kucultmesi dusulur, kalan acik serbest cocuklara k2 ile yeniden dagitilir
        // (CSS freeze dongusunun 2 adimla kesilmis hali; 3+ kademeli takilma dagitilmaz).
        float shrinkK = 0f, shrinkK2 = 0f;
        if (shrinkSum > 0 && fixedSum > availMain)
        {
            float deficit = fixedSum - availMain;
            shrinkK = shrinkK2 = deficit / shrinkSum;
            float frozen = 0, freeW = 0;
            for (var t = transform.FirstChild; t != null; t = t.NextSibling)
            {
                var b = BoxOf(t);
                if (b == null || b.ignoreLayout || b.grow > 0 || b.shrink <= 0)
                    continue;
                float pref = horiz ? b.PreferredW() : b.PreferredH();
                float want = pref - shrinkK * b.shrink * pref;
                float got = horiz ? b.ClampW(want) : b.ClampH(want);
                if (got != want) frozen += pref - got;
                else freeW += b.shrink * pref;
            }
            if (frozen > 0 && freeW > 0)
                shrinkK2 = MathF.Max(0f, (deficit - frozen) / freeW);
        }
        float crossAvail = horiz ? _rh - padTop - padBottom : _rw - padLeft - padRight;
        float tlx = -pivot.x * _rw - offX, tly = -pivot.y * _rh - offY; // scroll: tum cocuklar kayar
        float cursor = horiz ? tlx + padLeft : tly + padTop;
        // grow'lu cocuk kalan alani tuketir; yoksa justify grubu ana eksende dagitir.
        float gap = spacing;
        if (growSum <= 0)
        {
            JustifyOffsets(leftover, count, out float lead, out float extraGap);
            cursor += lead;
            gap += extraGap;
        }

        // 2. gecis: yerlestir (reverse: son cocuktan geriye).
        for (var t = reverse ? transform.LastChild : transform.FirstChild;
             t != null;
             t = reverse ? t.PrevSibling : t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null)
                continue;
            bool wasDirty = b._dirty;
            float ow = b._rw, oh = b._rh;
            if (b.ignoreLayout)
            {
                b.PlaceInParent(this); // dekor/arkaplan: grup disi, anchor'la yerlesir
                FinishChild(b, wasDirty, ow, oh);
                continue;
            }

            // Ana eksen margin'leri (mA = bas, mB = son), capraz margin'ler (mC0 = bas, mC1 = son).
            float mA = horiz ? b.margin.x : b.margin.y, mB = horiz ? b.margin.z : b.margin.w;
            float mC0 = horiz ? b.margin.y : b.margin.x, mC1 = horiz ? b.margin.w : b.margin.z;
            float main;
            if (b.grow > 0)
                main = leftover * (b.grow / growSum) - mA - mB;
            else
            {
                main = horiz ? b.PreferredW() : b.PreferredH();
                if (shrinkK > 0f && b.shrink > 0f)
                {
                    // 1. tur sonucu clamp'e takiliyorsa o degerde donar; takilmayan k2 ile kuculur.
                    float want = main - shrinkK * b.shrink * main;
                    float got = horiz ? b.ClampW(want) : b.ClampH(want);
                    main = got != want ? got : main - shrinkK2 * b.shrink * main;
                }
            }
            main = horiz ? b.ClampW(main) : b.ClampH(main);
            b.CrossPlacement(horiz, crossAvail - mC0 - mC1, alignChildren, out float cross, out float crossOff);

            if (horiz)
            {
                b._rw = main;
                b._rh = cross;
                b.SetPos(cursor + mA + b.pivot.x * main, tly + padTop + mC0 + crossOff + b.pivot.y * cross);
            }
            else
            {
                b._rw = cross;
                b._rh = main;
                b.SetPos(tlx + padLeft + mC0 + crossOff + b.pivot.x * cross, cursor + mA + b.pivot.y * main);
            }
            cursor += mA + main + mB + gap;
            FinishChild(b, wasDirty, ow, oh);
        }
    }

    // Capraz eksen: boyut + hiza ofseti. alignSelf Auto -> parent'in alignChildren'i;
    // Stretch -> capraz alan (margin dusulmus) clamp'li. avail = margin dusulmus capraz alan.
    void CrossPlacement(bool horiz, float avail, LayoutAlign parentAlign, out float cross, out float off)
    {
        if (alignSelf == LayoutAlignSelf.Stretch)
        {
            cross = horiz ? ClampH(avail) : ClampW(avail);
            off = 0;
            return;
        }
        cross = horiz ? PreferredH() : PreferredW();
        var a = alignSelf switch
        {
            LayoutAlignSelf.Start => LayoutAlign.Start,
            LayoutAlignSelf.Center => LayoutAlign.Center,
            LayoutAlignSelf.End => LayoutAlign.End,
            _ => parentAlign,
        };
        off = a switch
        {
            LayoutAlign.Center => (avail - cross) * 0.5f,
            LayoutAlign.End => avail - cross,
            _ => 0,
        };
    }

    // justifyChildren -> (ilk cocuk oncesi bosluk, her araliga eklenecek pay).
    // leftover = ana eksende cocuklar + spacing sonrasi kalan alan (>= 0).
    void JustifyOffsets(float leftover, int count, out float lead, out float extraGap)
    {
        lead = 0; extraGap = 0;
        if (leftover <= 0 || count <= 0)
            return;
        switch (justifyChildren)
        {
            case LayoutJustify.Center: lead = leftover * 0.5f; break;
            case LayoutJustify.End: lead = leftover; break;
            case LayoutJustify.SpaceBetween:
                if (count > 1) extraGap = leftover / (count - 1);
                else lead = leftover * 0.5f; // tek cocuk: CSS gibi ortala
                break;
            case LayoutJustify.SpaceAround:
                extraGap = leftover / count;
                lead = extraGap * 0.5f;
                break;
            case LayoutJustify.SpaceEvenly:
                extraGap = leftover / (count + 1);
                lead = extraGap;
                break;
        }
    }

    // Wrap satir dizimi (greedy; metindeki NextLine'in kutu muadili): ana eksende
    // sigmayan cocuk sonraki satira gecer, satir basina en az 1 uye (sonsuz dongu
    // olmaz). Satir capraz olcusu = uyelerin max'i; alignChildren satir ICINDE
    // hizalar (End ~ alt hizalama, baseline yaklasigi). grow yok sayilir (kelime
    // kutulari icin anlamsiz; satir-ici pay dagitimi gerekirse 2. faz). reverse
    // uyeleri ters siradan paketler. Satir arasi bosluk = spacing (CSS gap analogu).
    void ArrangeWrapped(bool horiz, float offX, float offY)
    {
        float availMain = horiz ? _rw - padLeft - padRight : _rh - padTop - padBottom;
        float tlx = -pivot.x * _rw - offX, tly = -pivot.y * _rh - offY;
        float mainStart = horiz ? tlx + padLeft : tly + padTop;
        float crossCursor = horiz ? tly + padTop : tlx + padLeft;

        var node = reverse ? transform.LastChild : transform.FirstChild;
        while (node != null)
        {
            // 1) satir tarama: uyeler + satir capraz olcusu (olcumle ayni kural).
            float used = 0, lineCross = 0;
            int n = 0;
            var scan = node;
            for (; scan != null; scan = reverse ? scan.PrevSibling : scan.NextSibling)
            {
                var b = BoxOf(scan);
                if (b == null || b.ignoreLayout)
                    continue;
                float main = horiz ? b.OuterW() : b.OuterH();
                float add = (n > 0 ? spacing : 0) + main;
                if (n > 0 && used + add > availMain + WrapEps)
                    break;
                used += add;
                n++;
                lineCross = MathF.Max(lineCross, horiz ? b.OuterH() : b.OuterW());
            }
            // 2) yerlestirme: [node, scan) arasi; satir ici justify (CSS gibi satir bazli).
            JustifyOffsets(MathF.Max(0, availMain - used), n, out float lead, out float extraGap);
            float cursor = mainStart + lead;
            float gap = spacing + extraGap;
            for (var t = node; t != scan; t = reverse ? t.PrevSibling : t.NextSibling)
            {
                var b = BoxOf(t);
                if (b == null)
                    continue;
                bool wasDirty = b._dirty;
                float ow = b._rw, oh = b._rh;
                if (b.ignoreLayout)
                {
                    b.PlaceInParent(this); // dekor/arkaplan: grup disi, anchor'la yerlesir
                    FinishChild(b, wasDirty, ow, oh);
                    continue;
                }
                float mA = horiz ? b.margin.x : b.margin.y, mB = horiz ? b.margin.z : b.margin.w;
                float mC0 = horiz ? b.margin.y : b.margin.x, mC1 = horiz ? b.margin.w : b.margin.z;
                float main = horiz ? b.PreferredW() : b.PreferredH(); // zaten clamp'li
                b.CrossPlacement(horiz, lineCross - mC0 - mC1, alignChildren, out float cross, out float crossOff);
                if (horiz)
                {
                    b._rw = main;
                    b._rh = cross;
                    b.SetPos(cursor + mA + b.pivot.x * main, crossCursor + mC0 + crossOff + b.pivot.y * cross);
                }
                else
                {
                    b._rw = cross;
                    b._rh = main;
                    b.SetPos(crossCursor + mC0 + crossOff + b.pivot.x * cross, cursor + mA + b.pivot.y * main);
                }
                cursor += mA + main + mB + gap;
                FinishChild(b, wasDirty, ow, oh);
            }
            node = scan;
            crossCursor += lineCross + spacing;
        }
    }

    // Layout'a katilan cocuk kutu: aktif GO + enabled kutu (inaktif = layout'tan cikar).
    static LayoutBox BoxOf(Transform t)
    {
        var go = t._gameObject;
        if (go == null || !go.activeInHierarchy)
            return null;
        var b = go.GetComponent<LayoutBox>();
        return b != null && b._enabled && !b._destroyed ? b : null;
    }

    // --- StyleBox cizimi (SDF parca quad-emitter) ---

    // --- overflow klip penceresi (her Encode basinda kurulur) ---
    // Ata kutularin Hidden eksenlerinden gelen kirpma, BU kutunun lokal uzayina
    // cevrilmis pencere. Kirpma GEOMETRIK: EmitQuad quad'i pencereye kirpar, UV +
    // dikey gradient orantili remap edilir — scissor yok, batch kirilmasi yok,
    // her kamerada dogru. SINIR: donmus (rotation'li) kutu/ata eksen-hizali
    // varsayimi bozar — o halkada klip sessizce devre disi kalir.
    bool _hasClip;
    float _clX0, _clY0, _clX1, _clY1;
    // .fx LOCALUV penceresi icin kutunun lokal dikdortgeni (Encode basinda yazilir).
    float _fxX0, _fxY1, _fxInvW, _fxInvH;

    // --- gradient baglami ---
    // EmitLayer/EncodeTextured LINEAR gradient'li katman cizerken kurar: EmitQuad aktifken
    // cagiranin c0/c1'ini YOK SAYAR, 4 kose rengini kutu bandina gore kendisi ornekler
    // (capraz yon = tint0..3 bilinear, ek quad yok) ve ara duraklarda quad'i durak
    // cizgisinde boler (GPU lineer interpolasyonu duragi atlamasin). Golge/isik/radial/
    // metin yollari baglami kullanmaz (kendi renklerini verir).
    bool _gcActive;
    Gradient _gc;
    float _gcX0, _gcY0, _gcInvW, _gcInvH;

    void BeginGradient(in Gradient g)
    {
        _gcActive = g.type == GradientType.Linear;
        _gc = g;
    }

    void EndGradient() => _gcActive = false;

    // Kutu-lokal noktanin gradient eksenindeki konumu (0..1 disina tasabilir, At clamp'ler).
    float GradT(float x, float y)
    {
        float tx = (x - _gcX0) * _gcInvW, ty = (y - _gcY0) * _gcInvH;
        switch (_gc.direction)
        {
            case GradientDirection.Horizontal: return tx;
            case GradientDirection.Diagonal: return (tx + ty) * 0.5f;
            case GradientDirection.DiagonalReverse: return (1f - tx + ty) * 0.5f;
            default: return ty;
        }
    }

    void ComputeClip(in Mat4 wm)
    {
        _hasClip = false;
        // Kendi uzayimiz eksen-hizali degilse dunya penceresi lokale cevrilemez.
        if (MathF.Abs(wm.m[1]) > 1e-4f || MathF.Abs(wm.m[4]) > 1e-4f
            || wm.m[0] == 0 || wm.m[5] == 0)
            return;
        float wx0 = float.MinValue, wy0 = float.MinValue;
        float wx1 = float.MaxValue, wy1 = float.MaxValue;
        // Cozumun yazdigi ebeveyn zinciri: GetComponent yok, yalniz bayrak kiyasi.
        // Devre disi/yok edilmis ata (ParentBox'in dislayacagi) zinciri keser.
        for (var p = _parentBox; p != null && p._enabled && !p._destroyed; p = p._parentBox)
        {
            bool hx = p.overflowX == OverflowMode.Hidden || p.overflowX == OverflowMode.Scroll;
            bool hy = p.overflowY == OverflowMode.Hidden || p.overflowY == OverflowMode.Scroll;
            if (!hx && !hy)
                continue;
            ref var pm = ref p.transform._getWorldMatrix();
            if (MathF.Abs(pm.m[1]) > 1e-4f || MathF.Abs(pm.m[4]) > 1e-4f)
                continue; // donmus ata kirpamaz (bilinen sinir)
            float lx0 = -p.pivot.x * p._rw, ly0 = -p.pivot.y * p._rh;
            float ax = pm.m[0] * lx0 + pm.m[12], bx = pm.m[0] * (lx0 + p._rw) + pm.m[12];
            float ay = pm.m[5] * ly0 + pm.m[13], by = pm.m[5] * (ly0 + p._rh) + pm.m[13];
            if (hx) { wx0 = MathF.Max(wx0, MathF.Min(ax, bx)); wx1 = MathF.Min(wx1, MathF.Max(ax, bx)); }
            if (hy) { wy0 = MathF.Max(wy0, MathF.Min(ay, by)); wy1 = MathF.Min(wy1, MathF.Max(ay, by)); }
            _hasClip = true;
        }
        if (!_hasClip)
            return;
        // Dunya penceresi -> lokal uzay (eksen-hizali: yalniz olcek+tasima;
        // negatif olcekte min/max takasi). Sinirsiz eksen inf'e tasar — zararsiz.
        float inv0 = 1f / wm.m[0], inv5 = 1f / wm.m[5];
        float lxA = (wx0 - wm.m[12]) * inv0, lxB = (wx1 - wm.m[12]) * inv0;
        float lyA = (wy0 - wm.m[13]) * inv5, lyB = (wy1 - wm.m[13]) * inv5;
        _clX0 = MathF.Min(lxA, lxB); _clX1 = MathF.Max(lxA, lxB);
        _clY0 = MathF.Min(lyA, lyB); _clY1 = MathF.Max(lyA, lyB);
    }

    // Kendi Hidden ekseni metni kirpmadan once klip penceresini acar (ata yoksa sinirsiz).
    void EnsureClip()
    {
        if (_hasClip)
            return;
        _hasClip = true;
        _clX0 = _clY0 = float.MinValue;
        _clX1 = _clY1 = float.MaxValue;
    }

    bool HasBorder => borderFill.Visible
        && (borderLeft > 0 || borderTop > 0 || borderRight > 0 || borderBottom > 0);

    bool HasShadow => shadowColor.a > 0;
    bool HasInnerShadow => innerShadowColor.a > 0 && innerShadowBlur > 0;
    bool HasLight => lightColor.a > 0 && lightWidth > 0 && lightHeight > 0;

    bool HasStyle
        => sprite != null || fill.Visible || HasBorder || HasShadow || HasInnerShadow || HasLight;

    // Kutu SDF parcalari icerigin yasadigi SAYFADAN orneklenir: metin varsa fontun
    // sayfasi, ikon varsa sprite'in sayfasi (atlas sayfalari UiPieces tasir), yoksa
    // DefaultFont — kutu + metin + ikon ayni texview'i paylasip tek instanced
    // draw'a merge olur. Hic sayfa yoksa lazy fallback dokusu.
    UiAtlas PiecesAtlas(out Material mat)
    {
        // Metin ya da acik font adi yoksa fontu one alma: ikonlu kutu sprite sayfasiyla tek draw kalsin.
        var f = !string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(font) ? Font : null;
        if (f?.Page != null && f.Page.HasPieces)
        {
            mat = f.Material.ForBlend(BlendMode).ForEffects(Effects);
            return f.Pieces;
        }
        var page = sprite?.Page;
        if (page != null && page.HasPieces)
        {
            var baseMat = UiPieces.TexturedMaterial;
            baseMat.MainTexture = page;
            baseMat.Shader = UiPieces.UiShader;
            mat = baseMat.ForBlend(BlendMode).ForEffects(Effects);
            return new UiAtlas(page, page.PiecesX, page.PiecesY);
        }
        f = UiPieces.DefaultFont;
        if (f?.Page != null && f.Page.HasPieces)
        {
            mat = f.Material.ForBlend(BlendMode).ForEffects(Effects);
            return f.Pieces;
        }
        mat = UiPieces.SharedMaterial.ForBlend(BlendMode).ForEffects(Effects);
        return UiPieces.Fallback;
    }

    // Cizim = instanced quad emisyonu (SDF parca modeli): dolgu katmani + border
    // RING katmani (USER.z ic kenar bandi) — ortusme yok, yari saydam fill+border
    // cift blend olmaz. Ayni materyali paylasan bitisik quad'lar RenderQueue'da
    // tek instanced draw'a merge olur (sprite yoluyla ayni mekanizma).
    internal override void Encode(RenderQueue queue)
    {
        // Suruilen pozisyon dis yazima kapali: layout'un son yazdigi deger her
        // frame geri basilir (Unity driven-property davranisi; runtime dahil).
        if (_hasPlaced)
        {
            var lp = transform.localPosition;
            if (lp.x != _placedX || lp.y != _placedY)
                transform.localPosition = new Vec3(_placedX, _placedY, lp.z);
        }
        SyncStyle(); // HasStyle kontrolunden ONCE: asset yeni dolgu vermis olabilir
        _everEncoded = true;
        if (!HasStyle && !HasText)
            return;
        ResolveIfDirty();
        if (_rw <= 0 || _rh <= 0)
            return;

        Mat4 wm = transform._getWorldMatrix(); // kopya: lokal fonksiyonlar ref yakalayamaz
        ComputeClip(in wm);
        int layer = _effectiveOrder;  // ayni layer'da parent < cocuk (Scene DFS submit sirasi)
        float x0 = -pivot.x * _rw, y0 = -pivot.y * _rh;
        float x1 = x0 + _rw, y1 = y0 + _rh;
        // Kutu duzeyi culling: ata klibinin tamamen disindaysa (golge payi dahil) hicbir
        // parca/glyph dolasilmaz — scroll listesinde gorunmeyen oge bedava. 4 kiyas.
        if (_hasClip)
        {
            float ex = 0f, ey = 0f;
            if (HasShadow)
            {
                ex = shadowBlur + shadowGrow + MathF.Abs(shadowOffset.x);
                ey = shadowBlur + shadowGrow + MathF.Abs(shadowOffset.y);
            }
            if (x1 + ex <= _clX0 || x0 - ex >= _clX1 || y1 + ey <= _clY0 || y0 - ey >= _clY1)
                return;
        }
        _fxX0 = x0; _fxY1 = y1; _fxInvW = 1f / _rw; _fxInvH = 1f / _rh;
        _gcX0 = x0; _gcY0 = y0; _gcInvW = _fxInvW; _gcInvH = _fxInvH;

        if (HasShadow)
            EmitShadow(queue, wm, x0, y0, x1, y1, layer); // ayni layer'da ilk = altta

        if (sprite != null)
        {
            EncodeTextured(queue, in wm, x0, y0, x1, y1, layer);
            if (HasLight)
                EmitLight(queue, wm, x0, y0, x1, layer);
            if (HasText)
                EncodeText(queue, in wm, x0, y0, layer); // submit sirasi: kutunun ustunde
            return;
        }

        bool hasFill = fill.Visible;
        // Ic kutu (border'in ic kenari; border yoksa kutunun kendisi): dolgu + ic golge buraya.
        float ix0 = x0, iy0 = y0, ix1 = x1, iy1 = y1;
        float irTL = radiusTL, irTR = radiusTR, irBR = radiusBR, irBL = radiusBL;
        if (HasBorder)
        {
            // Karsilikli kenarlarin toplami kutuya sigmali (CSS border cakisma kurali).
            float bl = borderLeft, bt = borderTop, br = borderRight, bb = borderBottom;
            float sx = bl + br > _rw ? _rw / (bl + br) : 1f;
            float sy = bt + bb > _rh ? _rh / (bt + bb) : 1f;
            bl *= sx; br *= sx; bt *= sy; bb *= sy;
            EmitLayer(queue, wm, x0, y0, x1, y1,
                radiusTL, radiusTR, radiusBR, radiusBL, bl, bt, br, bb, isBorder: true, layer);
            ix0 += bl; iy0 += bt; ix1 -= br; iy1 -= bb;
            irTL = MathF.Max(radiusTL - MathF.Max(bl, bt), 0);
            irTR = MathF.Max(radiusTR - MathF.Max(br, bt), 0);
            irBR = MathF.Max(radiusBR - MathF.Max(br, bb), 0);
            irBL = MathF.Max(radiusBL - MathF.Max(bl, bb), 0);
        }
        if (hasFill)
        {
            EmitLayer(queue, wm, ix0, iy0, ix1, iy1, irTL, irTR, irBR, irBL, 0, 0, 0, 0, isBorder: false, layer);
            if (fill.type == GradientType.Radial)
                EmitRadial(queue, wm, ix0, iy0, ix1, iy1, fill.color, layer);
        }
        if (HasInnerShadow)
        {
            float b = innerShadowBlur;
            EmitLayer(queue, wm, ix0, iy0, ix1, iy1, irTL, irTR, irBR, irBL, b, b, b, b, isBorder: false, layer, innerBlur: b);
        }
        if (HasLight)
            EmitLight(queue, wm, x0, y0, x1, layer);

        if (HasText)
            EncodeText(queue, in wm, x0, y0, layer); // submit sirasi: kutunun ustunde
    }

    // Gloss: ust kenara asili, yatayda ortali radyal parlama (core UIBoxLight muadili; sprite yok, +4 quad).
    void EmitLight(RenderQueue q, Mat4 wm, float x0, float y0, float x1, int layer)
    {
        float w = MathF.Min(_rw * lightWidth, _rw);
        float h = MathF.Min(lightHeight, _rh);
        float cx = (x0 + x1) * 0.5f + lightOffset.x;
        float top = y0 + lightOffset.y;
        EmitRadial(q, wm, cx - w * 0.5f, top, cx + w * 0.5f, top + h, lightColor, layer);
    }

    const float AaOut = 2f;  // sekil disina tasan AA payi (dunya birimi)
    const float EdgeIn = 2f; // kenar seridinin ic kalinligi (SDF degeri 1'e ulasir)

    // Radyal dolgu overlay'i: taban katman (color2 duz) uzerine merkez rengi,
    // alpha = Shadow parcasinin lineer rampasi (1 - d/128) USER.w ham gecis.
    // Elips closest-side: kenar ortalarina teget, sinirinda 0 -> yuvarlak
    // koseler elips disinda, kirpma gerekmez. 4 aynali ceyrek quad, ayni
    // materyal -> kutunun draw'ina merge olur.
    void EmitRadial(RenderQueue q, Mat4 wm, float x0, float y0, float x1, float y1, Color col, int layer)
    {
        if (x1 <= x0 || y1 <= y0 || col.a == 0)
            return;
        var at = PiecesAtlas(out var mat);
        float texW = at.Tex.Width, texH = at.Tex.Height;
        float pbx = at.X + UiPieces.ShadowX, pby = at.Y + UiPieces.ShadowY;
        float uNear = pbx / texW, uFar = (pbx + UiPieces.ShadowSize) / texW;
        float vNear = pby / texH, vFar = (pby + UiPieces.ShadowSize) / texH;
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        var user = new Vec4(0f, 0f, 0f, 1f);

        void Quarter(float qx0, float qy0, float qx1, float qy1, bool farLeft, bool farTop)
            => EmitQuad(q, mat, wm, qx0, qy0, qx1, qy1,
                farLeft ? uFar : uNear, farTop ? vNear : vFar,
                farLeft ? uNear : uFar, farTop ? vFar : vNear,
                col, col, false, user, layer);
        Quarter(x0, y0, cx, cy, true, true);
        Quarter(cx, y0, x1, cy, false, true);
        Quarter(cx, cy, x1, y1, false, false);
        Quarter(x0, cy, cx, y1, true, false);
    }

    // Golge: kutunun yumusak kenarli kopyasi (SDF parca + USER smoothing — RT/blur
    // pass YOK). Falloff kenar merkezli ±blur; kose yuvarlagi blur'la buyur
    // (gercek gaussian blur iso-konturu davranisi). Partition EmitLayer'la ayni:
    // 4 kose hucresi (genis parca) + 4 kenar seridi (kesit, USER.x=0.5 tam rampa)
    // + ic dolgu. Ayni materyal — kutunun diger katmanlariyla ayni draw'a merge olur.
    void EmitShadow(RenderQueue q, Mat4 wm, float x0, float y0, float x1, float y1, int layer)
    {
        float g = shadowGrow;
        x0 += shadowOffset.x - g; y0 += shadowOffset.y - g;
        x1 += shadowOffset.x + g; y1 += shadowOffset.y + g;
        float w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0)
            return;
        float b = Math.Clamp(shadowBlur, 0.5f, MathF.Min(w, h) * 0.5f);

        // Kose yumusakligi: rs = max(radius+grow, blur); pair-fit clamp.
        float rtl = MathF.Max(radiusTL + g, b), rtr = MathF.Max(radiusTR + g, b);
        float rbr = MathF.Max(radiusBR + g, b), rbl = MathF.Max(radiusBL + g, b);
        float k = 1f;
        if (rtl + rtr > w) k = MathF.Min(k, w / (rtl + rtr));
        if (rbl + rbr > w) k = MathF.Min(k, w / (rbl + rbr));
        if (rtl + rbl > h) k = MathF.Min(k, h / (rtl + rbl));
        if (rtr + rbr > h) k = MathF.Min(k, h / (rtr + rbr));
        rtl *= k; rtr *= k; rbr *= k; rbl *= k;

        var at = PiecesAtlas(out var mat);
        float texW = at.Tex.Width, texH = at.Tex.Height;
        float pbx = at.X + UiPieces.ShadowX, pby = at.Y + UiPieces.ShadowY;
        float WR = UiPieces.ShadowRadius;
        float ebx = at.X + UiPieces.CornerX, eby = at.Y + UiPieces.CornerY;
        float R = UiPieces.CornerRadius, S = UiPieces.CornerSpread;
        float CU(float px) => (pbx + px) / texW;
        float CV(float py) => (pby + py) / texH;
        float EU(float px) => (ebx + px) / texW;
        float EV(float py) => (eby + py) / texH;
        float su0 = (at.X + UiPieces.SolidX + 8) / texW, su1 = (at.X + UiPieces.SolidX + 24) / texW;
        float sv0 = (at.Y + UiPieces.SolidY + 24) / texH, sv1 = (at.Y + UiPieces.SolidY + 8) / texH;
        var col = shadowColor;

        void Cell(float cx0, float cy0, float cx1, float cy1, float r, bool farLeft, bool farTop)
        {
            float pFar = MathF.Min((r + b) * WR / r, 127f);
            if (farLeft) cx0 -= b; else cx1 += b;
            if (farTop) cy0 -= b; else cy1 += b;
            float uNear = CU(0f), uFar = CU(pFar);
            float vNear = CV(0f), vFar = CV(pFar);
            var user = new Vec4(b * WR / (r * UiPieces.ShadowSpread), 0.5f, 0f, 0f);
            EmitQuad(q, mat, wm, cx0, cy0, cx1, cy1,
                farLeft ? uFar : uNear, farTop ? vNear : vFar,
                farLeft ? uNear : uFar, farTop ? vFar : vNear,
                col, col, false, user, layer);
        }
        Cell(x0, y0, x0 + rtl, y0 + rtl, rtl, true, true);
        Cell(x1 - rtr, y0, x1, y0 + rtr, rtr, false, true);
        Cell(x1 - rbr, y1 - rbr, x1, y1, rbr, false, false);
        Cell(x0, y1 - rbl, x0 + rbl, y1, rbl, true, false);

        // Kenar seritleri: keskin parcanin tam rampasi (56..120) ±b'ye gerilir;
        // USER.x=0.5 -> smoothstep(0,1,v) = rampayi dogrudan gecir (gaussian-vari).
        var eu = new Vec4(0.5f, 0f, 0f, 0f);
        float pvOut = EV(R + S * 0.5f), pvIn = EV(R - S * 0.5f);
        float puOut = EU(R + S * 0.5f), puIn = EU(R - S * 0.5f);
        float uA = EU(0.35f), uB = EU(1.0f);
        float vA = EV(0.35f), vB = EV(1.0f);
        if (x1 - rtr > x0 + rtl)
        {
            EmitQuad(q, mat, wm, x0 + rtl, y0 - b, x1 - rtr, y0 + b, uA, pvIn, uB, pvOut, col, col, false, eu, layer);
            EmitQuad(q, mat, wm, x0 + rbl, y1 - b, x1 - rbr, y1 + b, uA, pvOut, uB, pvIn, col, col, false, eu, layer);
        }
        if (y1 - rbl > y0 + rtl)
            EmitQuad(q, mat, wm, x0 - b, y0 + rtl, x0 + b, y1 - rbl, puOut, vA, puIn, vB, col, col, false, eu, layer);
        if (y1 - rbr > y0 + rtr)
            EmitQuad(q, mat, wm, x1 - b, y0 + rtr, x1 + b, y1 - rbr, puIn, vA, puOut, vB, col, col, false, eu, layer);

        // Ic dolgu (alpha 1 bolgesi): kenar seritlerinin ici, hucrelerle cakismadan.
        float ix0 = x0 + b, ix1 = x1 - b, iy0 = y0 + b, iy1 = y1 - b;
        if (ix1 <= ix0 || iy1 <= iy0)
            return;
        Span<float> rects = stackalloc float[InteriorRectsMax * 4];
        int n = SolidInterior(rects, x0 + rtl, y0 + rtl, x1 - rtr, y0 + rtr, x1 - rbr, y1 - rbr, x0 + rbl, y1 - rbl,
            ix0, iy0, ix1, iy1);
        for (int i = 0; i < n; i++)
        {
            float ax0 = rects[i * 4], ay0 = rects[i * 4 + 1], ax1 = rects[i * 4 + 2], ay1 = rects[i * 4 + 3];
            if (ax1 <= ax0 || ay1 <= ay0)
                continue;
            EmitQuad(q, mat, wm, ax0, ay0, ax1, ay1, su0, sv0, su1, sv1, col, col, false, default, layer);
        }
    }

    // Tek katman rounded-rect: 4 SDF kose hucresi + 4 kenar falloff seridi +
    // (dolguysa) ic quad'lar — hepsi disjoint, AA fwidth'ten her olcekte ~1px.
    // isBorder=true: ic dolgu YOK, USER.z ic kenar bandiyla RING cizilir.
    // innerBlur>0: IC GOLGE — ring gibi ama ic band yumusak (USER.x+USER.z), renk
    // innerShadowColor. Band derinligi b radius'u asarsa hucre max(r,b) ile kurulur
    // (gercek blur iso-konturu gibi yuvarlanir; kenar seritleriyle ortusme olmaz); kutunun
    // gercek kosesi hucre merkezli bir KESME dairesiyle yaklasik kirpilir (hata < ~0.7px).
    void EmitLayer(RenderQueue q, Mat4 wm, float x0, float y0, float x1, float y1,
        float rtl, float rtr, float rbr, float rbl,
        float ringL, float ringT, float ringR, float ringB, bool isBorder, int layer, float innerBlur = 0f)
    {
        float w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0)
            return;
        bool inner = innerBlur > 0f;
        bool ring = isBorder || inner;
        if (!inner)
            BeginGradient(isBorder ? borderFill : fill);
        else
            EndGradient();
        // Ic golge: gercek kose radius'lari kesme dairesi icin saklanir, hucre band kadar buyur.
        float ttl = rtl, ttr = rtr, tbr = rbr, tbl = rbl;
        if (inner)
        {
            float b = MathF.Min(innerBlur, MathF.Min(w, h) * 0.5f);
            ringL = ringT = ringR = ringB = b;
            rtl = MathF.Max(rtl, b); rtr = MathF.Max(rtr, b);
            rbr = MathF.Max(rbr, b); rbl = MathF.Max(rbl, b);
        }

        // Radius sigdirma (CSS pair kurali) + parca haritalamasi icin minimum.
        float k = 1f;
        if (rtl + rtr > w) k = MathF.Min(k, w / (rtl + rtr));
        if (rbl + rbr > w) k = MathF.Min(k, w / (rbl + rbr));
        if (rtl + rbl > h) k = MathF.Min(k, h / (rtl + rbl));
        if (rtr + rbr > h) k = MathF.Min(k, h / (rtr + rbr));
        rtl = MathF.Max(rtl * k, 0.75f); rtr = MathF.Max(rtr * k, 0.75f);
        rbr = MathF.Max(rbr * k, 0.75f); rbl = MathF.Max(rbl * k, 0.75f);
        ttl *= k; ttr *= k; tbr *= k; tbl *= k;

        var at = PiecesAtlas(out var mat);
        float texW = at.Tex.Width, texH = at.Tex.Height;
        // Kose parcasi: dolgu = keskin parca; ring = genis bandli parca (ic kenar
        // bandi parca araligina sigsin — border ~radius kalinligina kadar).
        float pbx = at.X + (ring ? UiPieces.ShadowX : UiPieces.CornerX);
        float pby = at.Y + (ring ? UiPieces.ShadowY : UiPieces.CornerY);
        float pieceR = ring ? UiPieces.ShadowRadius : UiPieces.CornerRadius;
        float pieceS = ring ? UiPieces.ShadowSpread : UiPieces.CornerSpread;
        // Kenar seritleri hep keskin parcanin kesitinden orneklenir.
        float ebx = at.X + UiPieces.CornerX, eby = at.Y + UiPieces.CornerY;
        float R = UiPieces.CornerRadius, S = UiPieces.CornerSpread;

        float CU(float px) => (pbx + px) / texW;
        float CV(float py) => (pby + py) / texH;
        float EU(float px) => (ebx + px) / texW;
        float EV(float py) => (eby + py) / texH;
        float su0 = (at.X + UiPieces.SolidX + 8) / texW, su1 = (at.X + UiPieces.SolidX + 24) / texW;
        float sv0 = (at.Y + UiPieces.SolidY + 24) / texH, sv1 = (at.Y + UiPieces.SolidY + 8) / texH;
        Color At(float c) => inner ? innerShadowColor : isBorder ? BorderAt(c) : FillAt(c);
        bool gh = (isBorder ? borderFill : fill).IsHorizontal; // gradient ekseni

        // Kose hucresi: parca koordinati merkezden (0) kutu kosesine dogru buyur;
        // farLeft/farTop = kutu kosesinin hucre icindeki yonu. rTrue = kutunun gercek
        // radius'u (yalniz ic golgede r'den kucuk olabilir).
        void Cell(float cx0, float cy0, float cx1, float cy1, float r, bool farLeft, bool farTop, float bC, float rTrue)
        {
            if (ring && bC <= 0f)
                return; // sifir kalinlikta ring: smoothstep carpimi yine de AA genisliginde iz birakir
            bool cutCorner = inner && rTrue < r;
            // Kesilen kosede hucre kutu sinirinda biter: dis pay kesme dairesinin icinde kalip boyanirdi.
            float m = cutCorner ? 0f : MathF.Min(AaOut, 0.4f * r);
            float pFar = (r + m) * pieceR / r;
            if (farLeft) cx0 -= m; else cx1 += m;
            if (farTop) cy0 -= m; else cy1 += m;
            float uNear = CU(0f), uFar = CU(pFar);
            float vNear = CV(0f), vFar = CV(pFar);
            Vec4 user;
            if (inner)
            {
                // v = 0.5 + t/(2r) (t = kenardan derinlik): band 0.5..0.5+b/(2r) smoothstep'le soner.
                float s = bC / (4f * r);
                float cut = 0.5f;
                if (cutCorner)
                {
                    // Gercek yayin hucre merkezine uzakligi: kenar ucu ile tepe noktasinin ortalamasi.
                    float dEdge = MathF.Sqrt(r * r + (r - rTrue) * (r - rTrue));
                    float dTip = MathF.Sqrt(2f) * r - rTrue * (MathF.Sqrt(2f) - 1f);
                    float rc = 0.5f * (dEdge + dTip);
                    cut = MathF.Max(0.5f + (pieceR - rc * pieceR / r) / pieceS, 0.02f);
                }
                user = new Vec4(s, cut, 0.5f + s, 0f);
            }
            else
                user = ring
                    ? new Vec4(0f, 0.5f, MathF.Min(0.5f + bC * pieceR / (r * pieceS), 0.98f), 0f)
                    : default;
            EmitQuad(q, mat, wm, cx0, cy0, cx1, cy1,
                farLeft ? uFar : uNear, farTop ? vNear : vFar,
                farLeft ? uNear : uFar, farTop ? vFar : vNear,
                At(gh ? cx0 : cy0), At(gh ? cx1 : cy1), gh, user, layer);
        }
        Cell(x0, y0, x0 + rtl, y0 + rtl, rtl, true, true, MathF.Max(ringL, ringT), ttl);
        Cell(x1 - rtr, y0, x1, y0 + rtr, rtr, false, true, MathF.Max(ringR, ringT), ttr);
        Cell(x1 - rbr, y1 - rbr, x1, y1, rbr, false, false, MathF.Max(ringR, ringB), tbr);
        Cell(x0, y1 - rbl, x0 + rbl, y1, rbl, true, false, MathF.Max(ringL, ringB), tbl);

        // Ring USER'i: border keskin ic kenar (z=c2); ic golge 0.5..c2 arasi yumusak band.
        Vec4 RingUser(float c2)
        {
            if (!ring) return default;
            if (!inner) return new Vec4(0f, 0.5f, c2, 0f);
            float s = (c2 - 0.5f) * 0.5f;
            return new Vec4(s, 0.5f, 0.5f + s, 0f);
        }

        // Yatay kenar seridi: parca kesiti (sutun x~0.7px, d≈y) dikeyde falloff.
        void HEdge(float xa, float xb, float ye, bool outUp, float b)
        {
            if (xb <= xa || (ring && b <= 0f))
                return;
            float m = AaOut, inTh, qp, c2 = 0f;
            if (ring) { inTh = b + m; qp = (S * 0.5f) / (m + b); c2 = 0.5f + b * qp / S; }
            else { inTh = EdgeIn; qp = (S * 0.5f) / EdgeIn; }
            float pyOut = MathF.Min(R + m * qp, 127f);
            float pyIn = R - inTh * qp; // dolguda deger tam 1'e iner (ic dolguyla dikissiz)
            float uA = EU(0.35f), uB = EU(1.0f);
            float yA = outUp ? ye - m : ye - inTh;
            float yB = outUp ? ye + inTh : ye + m;
            float vOut = EV(pyOut), vIn = EV(pyIn);
            float v0 = outUp ? vIn : vOut, v1 = outUp ? vOut : vIn;
            var user = RingUser(c2);
            EmitQuad(q, mat, wm, xa, yA, xb, yB, uA, v0, uB, v1, At(gh ? xa : yA), At(gh ? xb : yB), gh, user, layer);
        }
        // Dikey kenar seridi: ayni kesit satirdan (y~0.7px, d≈x) yatay falloff.
        void VEdge(float ya, float yb, float xe, bool outLeft, float b)
        {
            if (yb <= ya || (ring && b <= 0f))
                return;
            float m = AaOut, inTh, qp, c2 = 0f;
            if (ring) { inTh = b + m; qp = (S * 0.5f) / (m + b); c2 = 0.5f + b * qp / S; }
            else { inTh = EdgeIn; qp = (S * 0.5f) / EdgeIn; }
            float pxOut = MathF.Min(R + m * qp, 127f);
            float pxIn = R - inTh * qp;
            float vA = EV(0.35f), vB = EV(1.0f);
            float xA = outLeft ? xe - m : xe - inTh;
            float xB = outLeft ? xe + inTh : xe + m;
            float uOut = EU(pxOut), uIn = EU(pxIn);
            float u0 = outLeft ? uOut : uIn, u1 = outLeft ? uIn : uOut;
            var user = RingUser(c2);
            EmitQuad(q, mat, wm, xA, ya, xB, yb, u0, vA, u1, vB, At(gh ? xA : ya), At(gh ? xB : yb), gh, user, layer);
        }
        HEdge(x0 + rtl, x1 - rtr, y0, true, ringT);
        HEdge(x0 + rbl, x1 - rbr, y1, false, ringB);
        VEdge(y0 + rtl, y1 - rbl, x0, true, ringL);
        VEdge(y0 + rtr, y1 - rbr, x1, false, ringR);

        if (ring)
        {
            EndGradient();
            return;
        }

        // Ic dolgu: kenar seritlerinin ici, kose hucreleriyle cakismadan. Hucre ic
        // kenarlari hucrelerle AYNI ifadeyle (x0 + rtl vb.) uretilir — bit-esit dikis.
        float ix0 = x0 + EdgeIn, ix1 = x1 - EdgeIn, iy0 = y0 + EdgeIn, iy1 = y1 - EdgeIn;
        if (ix1 <= ix0 || iy1 <= iy0)
        {
            EndGradient();
            return;
        }
        Span<float> rects = stackalloc float[InteriorRectsMax * 4];
        int n = SolidInterior(rects, x0 + rtl, y0 + rtl, x1 - rtr, y0 + rtr, x1 - rbr, y1 - rbr, x0 + rbl, y1 - rbl,
            ix0, iy0, ix1, iy1);
        for (int i = 0; i < n; i++)
        {
            float ax0 = rects[i * 4], ay0 = rects[i * 4 + 1], ax1 = rects[i * 4 + 2], ay1 = rects[i * 4 + 3];
            if (ax1 <= ax0 || ay1 <= ay0)
                continue;
            EmitQuad(q, mat, wm, ax0, ay0, ax1, ay1, su0, sv0, su1, sv1, At(gh ? ax0 : ay0), At(gh ? ax1 : ay1), gh, default, layer);
        }
        EndGradient();
    }

    // Kose hucreleri disinda kalan ic alan: ust bant (+kose altlari), orta, alt bant.
    // Kose ic kenarlari cagirandan hazir gelir (hucre quad'lariyla ayni float).
    // Dikdortgenler rects'e (x0,y0,x1,y1 dortluleri, en fazla 7) yazilir, sayi doner —
    // delegate YOK: Action parametresi cagiranin closure'ini her Encode'da heap'e cikariyordu.
    const int InteriorRectsMax = 7;

    static int SolidInterior(Span<float> rects,
        float tlX, float tlY, float trX, float trY, float brX, float brY, float blX, float blY,
        float ix0, float iy0, float ix1, float iy1)
    {
        tlX = MathF.Max(tlX, ix0); tlY = MathF.Max(tlY, iy0);
        trX = MathF.Min(trX, ix1); trY = MathF.Max(trY, iy0);
        brX = MathF.Min(brX, ix1); brY = MathF.Min(brY, iy1);
        blX = MathF.Max(blX, ix0); blY = MathF.Min(blY, iy1);
        float topY = MathF.Max(tlY, trY), botY = MathF.Min(blY, brY);
        int n = 0;
        AddRect(rects, ref n, tlX, iy0, trX, topY);
        if (tlY < topY) AddRect(rects, ref n, ix0, tlY, tlX, topY);
        if (trY < topY) AddRect(rects, ref n, trX, trY, ix1, topY);
        AddRect(rects, ref n, ix0, topY, ix1, botY);
        AddRect(rects, ref n, blX, botY, brX, iy1);
        if (blY > botY) AddRect(rects, ref n, ix0, botY, blX, blY);
        if (brY > botY) AddRect(rects, ref n, brX, botY, ix1, brY);
        return n;
    }

    static void AddRect(Span<float> rects, ref int n, float ax0, float ay0, float ax1, float ay1)
    {
        rects[n * 4] = ax0; rects[n * 4 + 1] = ay0; rects[n * 4 + 2] = ax1; rects[n * 4 + 3] = ay1;
        n++;
    }

    // Dokulu kutu: SpriteFit'e gore Stretch tek quad / slice9 3x3 grid, Contain/Cover
    // tek quad (CPU rect/uv hesabi), Tile CPU grid. UI shader'in dokulu gecis
    // modu (USER.w<0) — sprite atlas sayfasindaysa kutu parcalari/metinle ayni
    // pipeline+doku, tek draw. Radius/border bu modda uygulanmaz (bilinen sinir).
    // fill gorunurse tint olarak carpilir (linear gradient baglamla; Tile'da karo
    // basina degil kutu bandina gore — desen uzerinde surekli gecis).
    const int TileCap = 256; // Tile modunda en fazla quad; asarsa karo buyutulur

    void EncodeTextured(RenderQueue q, in Mat4 world, float x0, float y0, float x1, float y1, int layer)
    {
        var tex = sprite.Page;
        if (tex == null)
            return;
        // Mantiksal sprite uzayi (texel, y asagi, 0..LW x 0..LH) ve icinde pikselleri olan TRIM kutusu.
        // Atlas trim'i seffaf kenarlari sayfaya koymaz: bolge (uu0..) yalniz trim kutusunu kaplar; her
        // yerlesim modu once mantiksal dikdortgeni hedefe esler, sonra EmitSpriteCell trim'le keser.
        var sp = new SpriteSpace
        {
            LW = Math.Max(1, sprite.LogicalWidth), LH = Math.Max(1, sprite.LogicalHeight),
            U0 = 0f, US = 1f, V0 = 0f, VS = 1f,
        };
        if (sprite.IsRegion)
        {
            sp.U0 = sprite.U0; sp.US = sprite.U1 - sprite.U0;
            sp.V0 = sprite.V0; sp.VS = sprite.V1 - sprite.V0;
            sp.TX = sprite.OffX; sp.TY = sprite.OffY; sp.TW = Math.Max(1, sprite.W); sp.TH = Math.Max(1, sprite.H);
        }
        else { sp.TX = 0; sp.TY = 0; sp.TW = sp.LW; sp.TH = sp.LH; }
        var baseMat = UiPieces.TexturedMaterial;
        baseMat.MainTexture = tex; // TexView DrawMesh aninda yakalanir (paylasilan materyal deseni)
        baseMat.Shader = UiPieces.UiShader; // varyantlar ForBlend/ForEffects'te tabandan kopyalanir
        var mat = baseMat.ForBlend(BlendMode).ForEffects(Effects);
        var user = UiPieces.TexturedUser;
        bool tinted = fill.Visible;
        bool gh = fill.IsHorizontal;
        if (tinted) BeginGradient(fill); else EndGradient();
        Color cTop = tinted ? FillAt(gh ? x0 : y0) : Color.White;
        Color cBot = tinted ? FillAt(gh ? x1 : y1) : Color.White;

        if (spriteFit != SpriteFit.Stretch)
        {
            EncodeFitted(q, in world, x0, y0, x1, y1, in sp, mat, in user, cTop, cBot, gh, layer);
            EndGradient();
            return;
        }

        if (slice9Left <= 0 && slice9Top <= 0 && slice9Right <= 0 && slice9Bottom <= 0)
        {
            EmitSpriteCell(q, mat, world, in sp, 0f, 0f, sp.LW, sp.LH, x0, y0, x1, y1, x0, y0, x1, y1, cTop, cBot, gh, user, layer);
            EndGradient();
            return;
        }

        // 9-slice: koseler doku-piksel boyutunda sabit, kenarlar/orta gerilir;
        // dilimler sigmazsa oranla kisilir (border cakisma kuraliyla ayni). Dilim sinirlari MANTIKSAL
        // piksel: trim'lenmis kenar hucreleri EmitSpriteCell'de dogal olarak bos kalir.
        float sl = slice9Left, st = slice9Top, sr = slice9Right, sb = slice9Bottom;
        float w = x1 - x0, h = y1 - y0;
        float kx = sl + sr > w ? w / (sl + sr) : 1f;
        float ky = st + sb > h ? h / (st + sb) : 1f;
        Span<float> xs = stackalloc float[4] { x0, x0 + sl * kx, x1 - sr * kx, x1 };
        Span<float> ys = stackalloc float[4] { y0, y0 + st * ky, y1 - sb * ky, y1 };
        float lx1 = Math.Clamp(sl, 0f, sp.LW), lx2 = Math.Clamp(sp.LW - sr, lx1, sp.LW);
        float ly1 = Math.Clamp(st, 0f, sp.LH), ly2 = Math.Clamp(sp.LH - sb, ly1, sp.LH);
        Span<float> lxs = stackalloc float[4] { 0f, lx1, lx2, sp.LW };
        Span<float> lys = stackalloc float[4] { 0f, ly1, ly2, sp.LH };
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                EmitSpriteCell(q, mat, world, in sp, lxs[c], lys[r], lxs[c + 1], lys[r + 1],
                    xs[c], ys[r], xs[c + 1], ys[r + 1], x0, y0, x1, y1,
                    tinted ? FillAt(gh ? xs[c] : ys[r]) : Color.White,
                    tinted ? FillAt(gh ? xs[c + 1] : ys[r + 1]) : Color.White, gh, user, layer);
        EndGradient();
    }

    // Sprite'in mantiksal uzayi + bolge uv'si + trim kutusu (EncodeTextured basinda bir kez kurulur).
    ref struct SpriteSpace
    {
        public float LW, LH;          // mantiksal boyut (texel)
        public float TX, TY, TW, TH;  // trim kutusu (mantiksal uzayda; pikseller yalniz burada)
        public float U0, US, V0, VS;  // trim kutusunun sayfa uv'si (v: V0+VS <-> ust kenar)
    }

    // Mantiksal dikdortgen [lx0,lx1]x[ly0,ly1] (y asagi) -> hedef [dx0,dx1]x[dy0,dy1] (lokal, y asagi) afin
    // eslemesi. Mantiksal aralik trim kutusuyla ve hedef aralik klip kutusuyla kesilir, uv kirpilmis
    // bolgeden orantiyla hesaplanir. Trimsiz sprite'ta (TX=0, TW=LW) kesme no-op -> eski cikti birebir.
    void EmitSpriteCell(RenderQueue q, Material mat, in Mat4 world, in SpriteSpace sp,
        float lx0, float ly0, float lx1, float ly1, float dx0, float dy0, float dx1, float dy1,
        float cx0, float cy0, float cx1, float cy1, Color c0, Color c1, bool gh, in Vec4 user, int layer)
    {
        if (lx1 <= lx0 || ly1 <= ly0 || dx1 <= dx0 || dy1 <= dy0)
            return;
        float sx = (dx1 - dx0) / (lx1 - lx0), sy = (dy1 - dy0) / (ly1 - ly0);
        // mantiksal kesme: trim kutusu + klip kutusunun mantiksal karsiligi
        float ax0 = MathF.Max(MathF.Max(lx0, sp.TX), lx0 + (cx0 - dx0) / sx);
        float ax1 = MathF.Min(MathF.Min(lx1, sp.TX + sp.TW), lx0 + (cx1 - dx0) / sx);
        float ay0 = MathF.Max(MathF.Max(ly0, sp.TY), ly0 + (cy0 - dy0) / sy);
        float ay1 = MathF.Min(MathF.Min(ly1, sp.TY + sp.TH), ly0 + (cy1 - dy0) / sy);
        if (ax1 <= ax0 || ay1 <= ay0)
            return;
        float qx0 = dx0 + (ax0 - lx0) * sx, qx1 = dx0 + (ax1 - lx0) * sx;
        float qy0 = dy0 + (ay0 - ly0) * sy, qy1 = dy0 + (ay1 - ly0) * sy;
        float u0 = sp.U0 + (ax0 - sp.TX) / sp.TW * sp.US, u1 = sp.U0 + (ax1 - sp.TX) / sp.TW * sp.US;
        float vTop = sp.V0 + (1f - (ay0 - sp.TY) / sp.TH) * sp.VS; // ust kenar (ay0) <-> buyuk v
        float vBot = sp.V0 + (1f - (ay1 - sp.TY) / sp.TH) * sp.VS;
        EmitQuad(q, mat, world, qx0, qy0, qx1, qy1, u0, vBot, u1, vTop, c0, c1, gh, user, layer);
    }

    // Contain/Cover/Tile yerlesimi (9-slice yok). Oran sprite'in MANTIKSAL boyutundan; hedef dikdortgen
    // kutuyla (Cover/Tile) ve trim'le EmitSpriteCell'de kesilir.
    void EncodeFitted(RenderQueue q, in Mat4 world, float x0, float y0, float x1, float y1,
        in SpriteSpace sp, Material mat, in Vec4 user, Color cTop, Color cBot, bool gh, int layer)
    {
        float w = x1 - x0, h = y1 - y0;
        float tw = sp.LW, th = sp.LH;
        switch (spriteFit)
        {
            case SpriteFit.Contain:
            {
                float k = MathF.Min(w / tw, h / th);
                float dw = tw * k, dh = th * k;
                float dx0 = x0 + (w - dw) * 0.5f, dy0 = y0 + (h - dh) * 0.5f;
                EmitSpriteCell(q, mat, world, in sp, 0f, 0f, tw, th, dx0, dy0, dx0 + dw, dy0 + dh, x0, y0, x1, y1, cTop, cBot, gh, user, layer);
                return;
            }
            case SpriteFit.Cover:
            {
                // Kutuyu dolduran olcek; tasan eksen klip ile simetrik kirpilir.
                float k = MathF.Max(w / tw, h / th);
                float dw = tw * k, dh = th * k;
                float dx0 = x0 + (w - dw) * 0.5f, dy0 = y0 + (h - dh) * 0.5f;
                EmitSpriteCell(q, mat, world, in sp, 0f, 0f, tw, th, dx0, dy0, dx0 + dw, dy0 + dh, x0, y0, x1, y1, cTop, cBot, gh, user, layer);
                return;
            }
            default: // Tile
            {
                int nx = (int)MathF.Ceiling(w / tw), ny = (int)MathF.Ceiling(h / th);
                if (nx * ny > TileCap)
                {
                    // Karo buyut: quad sayisi TileCap'e sigsin (desen olcegi degisir, patlama olmaz).
                    float s = MathF.Sqrt((float)(nx * ny) / TileCap);
                    tw *= s; th *= s;
                    nx = (int)MathF.Ceiling(w / tw); ny = (int)MathF.Ceiling(h / th);
                }
                // Karo sol-ustten dizilir; kismi karolar klip ile kesilir (dokunun sol/ust kismi gorunur).
                for (int r = 0; r < ny; r++)
                {
                    float ty0 = y0 + r * th;
                    for (int c = 0; c < nx; c++)
                    {
                        float tx0 = x0 + c * tw;
                        EmitSpriteCell(q, mat, world, in sp, 0f, 0f, sp.LW, sp.LH, tx0, ty0, tx0 + tw, ty0 + th,
                            x0, y0, x1, y1, cTop, cBot, gh, user, layer);
                    }
                }
                return;
            }
        }
    }
    // Lokal-uzay dikdortgenini world matrisiyle tek instanced quad'a cevirir.
    // c0/c1 gradient EKSENI boyunca renkler (gradH=false: ust/alt, true: sol/sag).
    // Gradient baglami aktifse c0/c1/gradH yok sayilir: kose renkleri GradT'den
    // orneklenir, ara duraklarda quad eksen boyunca duraklarda bolunur (capraz yonde
    // ara durak yok — Gradient.MidStops). Klip penceresi varsa quad kirpilir:
    // UV lineer remap (SDF parca ornekleme orantili kalir) + kose renkleri banda lerp'lenir.
    void EmitQuad(RenderQueue q, Material mat, in Mat4 world,
        float qx0, float qy0, float qx1, float qy1,
        float u0, float v0, float u1, float v1,
        Color c0, Color c1, bool gradH, in Vec4 user, int layer)
    {
        if (qx1 <= qx0 || qy1 <= qy0)
            return;
        if (!_gcActive)
        {
            // Kose sirasi: BL, BR, TR, TL (tint0..3, ust kenar uv.y=1).
            if (gradH) EmitQuadCore(q, mat, in world, qx0, qy0, qx1, qy1, u0, v0, u1, v1, c0, c1, c1, c0, in user, layer);
            else EmitQuadCore(q, mat, in world, qx0, qy0, qx1, qy1, u0, v0, u1, v1, c1, c1, c0, c0, in user, layer);
            return;
        }
        int mids = _gc.MidStops; // capraz yonde hep 0 (Gradient.MidStops)
        if (mids == 0)
        {
            EmitGradPart(q, mat, in world, qx0, qy0, qx1, qy1, qx0, qy0, qx1, qy1, u0, v0, u1, v1, in user, layer);
            return;
        }
        // Eksenel: durak cizgileri kutu bandindan lokale (hucrelerle ayni ifade: x0 + t*w).
        bool h = _gc.IsHorizontal;
        float a0 = h ? qx0 : qy0, a1 = h ? qx1 : qy1;
        float org = h ? _gcX0 : _gcY0, span = h ? 1f / _gcInvW : 1f / _gcInvH;
        float cur = a0;
        for (int i = 0; i < mids; i++)
        {
            float s = org + Math.Clamp(_gc.MidPos(i), 0f, 1f) * span;
            if (s <= cur || s >= a1)
                continue;
            if (h) EmitGradPart(q, mat, in world, qx0, qy0, qx1, qy1, cur, qy0, s, qy1, u0, v0, u1, v1, in user, layer);
            else EmitGradPart(q, mat, in world, qx0, qy0, qx1, qy1, qx0, cur, qx1, s, u0, v0, u1, v1, in user, layer);
            cur = s;
        }
        if (h) EmitGradPart(q, mat, in world, qx0, qy0, qx1, qy1, cur, qy0, a1, qy1, u0, v0, u1, v1, in user, layer);
        else EmitGradPart(q, mat, in world, qx0, qy0, qx1, qy1, qx0, cur, qx1, a1, u0, v0, u1, v1, in user, layer);
    }

    // (fx0..fy1) tam quad'in (sx0..sy1) alt dikdortgeni: uv orantili alt pencere,
    // kose renkleri gradient baglamindan. v1 <-> ust, v0 <-> alt.
    void EmitGradPart(RenderQueue q, Material mat, in Mat4 world,
        float fx0, float fy0, float fx1, float fy1,
        float sx0, float sy0, float sx1, float sy1,
        float u0, float v0, float u1, float v1, in Vec4 user, int layer)
    {
        if (sx1 <= sx0 || sy1 <= sy0)
            return;
        if (sx0 != fx0 || sx1 != fx1)
        {
            float iw = 1f / (fx1 - fx0), du = u1 - u0;
            float t0 = (sx0 - fx0) * iw, t1 = (sx1 - fx0) * iw;
            u1 = u0 + du * t1;
            u0 += du * t0;
        }
        if (sy0 != fy0 || sy1 != fy1)
        {
            float ih = 1f / (fy1 - fy0);
            float t0 = (sy0 - fy0) * ih, t1 = (sy1 - fy0) * ih;
            float dv = v0 - v1;
            float nv1 = v1 + dv * t0, nv0 = v1 + dv * t1;
            v1 = nv1; v0 = nv0;
        }
        Color cTL = _gc.AtExtrapolated(GradT(sx0, sy0)), cTR = _gc.AtExtrapolated(GradT(sx1, sy0));
        Color cBR = _gc.AtExtrapolated(GradT(sx1, sy1)), cBL = _gc.AtExtrapolated(GradT(sx0, sy1));
        EmitQuadCore(q, mat, in world, sx0, sy0, sx1, sy1, u0, v0, u1, v1, cBL, cBR, cTR, cTL, in user, layer);
    }

    void EmitQuadCore(RenderQueue q, Material mat, in Mat4 world,
        float qx0, float qy0, float qx1, float qy1,
        float u0, float v0, float u1, float v1,
        Color cBL, Color cBR, Color cTR, Color cTL, in Vec4 user, int layer)
    {
        if (_hasClip)
        {
            float nx0 = MathF.Max(qx0, _clX0), ny0 = MathF.Max(qy0, _clY0);
            float nx1 = MathF.Min(qx1, _clX1), ny1 = MathF.Min(qy1, _clY1);
            if (nx1 <= nx0 || ny1 <= ny0)
                return;
            if (nx0 != qx0 || nx1 != qx1)
            {
                float iw = 1f / (qx1 - qx0), du = u1 - u0;
                float t0 = (nx0 - qx0) * iw, t1 = (nx1 - qx0) * iw;
                u1 = u0 + du * t1;
                u0 += du * t0;
                Color tl = LerpC(cTL, cTR, t0), tr = LerpC(cTL, cTR, t1);
                Color bl = LerpC(cBL, cBR, t0), br = LerpC(cBL, cBR, t1);
                cTL = tl; cTR = tr; cBL = bl; cBR = br;
                qx0 = nx0; qx1 = nx1;
            }
            if (ny0 != qy0 || ny1 != qy1)
            {
                // v1 <-> qy0 (ust), v0 <-> qy1 (alt) — Mesh.Quad y-down uv duzeni.
                float ih = 1f / (qy1 - qy0);
                float t0 = (ny0 - qy0) * ih, t1 = (ny1 - qy0) * ih;
                float dv = v0 - v1;
                float nv1 = v1 + dv * t0, nv0 = v1 + dv * t1;
                v1 = nv1; v0 = nv0;
                Color tl = LerpC(cTL, cBL, t0), bl = LerpC(cTL, cBL, t1);
                Color tr = LerpC(cTR, cBR, t0), br = LerpC(cTR, cBR, t1);
                cTL = tl; cBL = bl; cTR = tr; cBR = br;
                qy0 = ny0; qy1 = ny1;
            }
        }
        Mat4 m = world;
        float cx = (qx0 + qx1) * 0.5f, cy = (qy0 + qy1) * 0.5f;
        float sx = qx1 - qx0, sy = qy1 - qy0;
        if (world.m[1] == 0f && world.m[4] == 0f && world.m[2] == 0f && world.m[6] == 0f)
        {
            // Eksen-hizali: komsu quad'larin ORTAK kenari GPU'da merkez±boyut/2 olarak ayri
            // ayri hesaplanir; bit-esit cikmazsa alt-piksel izgarasinda farkli hucreye dusup
            // 1px boslukbirakir. Koseleri dunya uzayinda 2^-10 izgaraya oturtunca merkez/boyut
            // toplamlari float'ta TAM olur -> ayni lokal kenar iki quad'da ayni biti verir.
            float wx0 = Snap(world.m[0] * qx0 + world.m[12]), wx1 = Snap(world.m[0] * qx1 + world.m[12]);
            float wy0 = Snap(world.m[5] * qy0 + world.m[13]), wy1 = Snap(world.m[5] * qy1 + world.m[13]);
            m.m[0] = wx1 - wx0; m.m[5] = wy1 - wy0;
            m.m[12] = (wx0 + wx1) * 0.5f; m.m[13] = (wy0 + wy1) * 0.5f;
        }
        else
        {
            m.m[12] += m.m[0] * cx + m.m[4] * cy;
            m.m[13] += m.m[1] * cx + m.m[5] * cy;
            m.m[14] += m.m[2] * cx + m.m[6] * cy;
            m.m[0] *= sx; m.m[1] *= sx; m.m[2] *= sx;
            m.m[4] *= sy; m.m[5] *= sy; m.m[6] *= sy;
        }
        // LOCALUV penceresi: parca kutu icinde nerede (uv.y=1 ust kenar; golge
        // parcalari kutu disina tasabilir -> 0..1 disi degerler normal).
        var fxRect = new Vec4((qx0 - _fxX0) * _fxInvW, (_fxY1 - qy1) * _fxInvH, sx * _fxInvW, sy * _fxInvH);
        // tint koseleri uv uzayinda: 0=BL 1=BR 2=TR 3=TL (ust kenar = uv.y=1)
        q.DrawMesh(Mesh.Quad(), mat, in m, cBL, cBR, cTR, cTL, in user, in FxParams, in fxRect, u0, v0, u1, v1, layer);
    }

    const float SnapGrid = 1024f; // 2^10: |deger| < 8192 iken izgara toplamlari tam

    static float Snap(float v) => MathF.Round(v * SnapGrid) * (1f / SnapGrid);

    static Color LerpC(Color a, Color b, float t) => new(
        (byte)(a.r + (b.r - a.r) * t),
        (byte)(a.g + (b.g - a.g) * t),
        (byte)(a.b + (b.b - a.b) * t),
        (byte)(a.a + (b.a - a.a) * t));

    // Gradient KUTU bandina gore, eksen yonune uygun koordinattan (katmandan
    // bagimsiz — parca sinirlari dikissiz). c = dikeyde y, yatayda x.
    Color FillAt(float c)
        => fill.IsHorizontal
            ? fill.At((c + pivot.x * _rw) / _rw)
            : fill.At((c + pivot.y * _rh) / _rh);

    Color BorderAt(float c)
        => borderFill.IsHorizontal
            ? borderFill.At((c + pivot.x * _rw) / _rw)
            : borderFill.At((c + pivot.y * _rh) / _rh);
}



// Sahne layout sistemi (ISceneSystem): kirli kutu kuyrugu. Scene layout'u
// TANIMAZ — sweep faz kancasindan kosar. GetSystem<LayoutSystem>() lazy dogar.
internal sealed class LayoutSystem : ISceneSystem
{
    LayoutBox[] _queue = new LayoutBox[32];
    int _count;

    // Gecisteki kutular (stil transition): yalniz bunlar tick'lenir; biten/yok edilen duser.
    LayoutBox[] _trans = new LayoutBox[8];
    int _transCount;

    internal void AddTransition(LayoutBox b)
    {
        for (int i = 0; i < _transCount; i++)
            if (ReferenceEquals(_trans[i], b))
                return;
        if (_transCount == _trans.Length) Array.Resize(ref _trans, _transCount * 2);
        _trans[_transCount++] = b;
    }

    void TickTransitions(float dt)
    {
        int w = 0;
        for (int i = 0; i < _transCount; i++)
        {
            var b = _trans[i];
            bool keep = b != null && !b._destroyed && b.TickTransition(dt);
            if (keep)
                _trans[w++] = b;
        }
        for (int i = w; i < _transCount; i++)
            _trans[i] = null;
        _transCount = w;
    }

    internal void Queue(LayoutBox b)
    {
        if (_count == _queue.Length) Array.Resize(ref _queue, _count * 2);
        _queue[_count++] = b;
    }

    // Pull'un kacirdigi (o frame kimsenin okumadigi) kirli kutulari cozer.
    // FitScreen kutular kuyrukta KALIR (ekran boyutu degisimi izlensin).
    public void AfterUpdate(Scene scene)
    {
        if (_transCount > 0)
            TickTransitions(Time.deltaTime); // edit modunda dt=0: gecis ilerlemez
        int n = _count;
        if (n == 0)
            return;
        for (int i = 0; i < n; i++)
        {
            var b = _queue[i];
            _queue[i] = null;
            if (b == null || b._destroyed)
                continue;
            b._queued = false;
            if (b.isActiveAndEnabled)
            {
                b.ResolveIfDirty();
                if (b.FitScreen)
                    b.QueueSweep();
            }
        }
        // Sweep sirasinda eklenenler (re-queue dahil) sona yazildi; basa kaydir.
        int w = 0;
        for (int i = n; i < _count; i++)
        {
            _queue[w++] = _queue[i];
            _queue[i] = null;
        }
        _count = w;
    }

    public void BeginRender(Scene scene, RenderQueue queue) { }

    public void EndRender(Scene scene, RenderQueue queue) { }
}
