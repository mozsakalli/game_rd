using System;

namespace DigitoyEngine;

public enum LayoutMode { None = 0, Horizontal = 1, Vertical = 2 }
public enum LayoutAlign { Start = 0, Center = 1, End = 2 }

// Eksen basina tasma davranisi (CSS overflow benzeri; Grow = mevcut varsayilan):
// Grow    = icerik kutuyu buyutur (preferred = max(authored, icerik)).
// Visible = kutu authored boyutta kalir, tasan icerik CIZILIR (CSS visible).
// Hidden  = kutu authored boyutta kalir, tasan icerik KIRPILIR (CSS hidden).
// Scroll (Hidden + icerik ofseti) 2. faz.
public enum OverflowMode { Grow = 0, Visible = 1, Hidden = 2 }

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
// STIL (StyleBox): kutu kendi arkaplanini cizebilir — fill (duz/dikey gradient),
// border (kenar basina genislik + duz/dikey gradient), kose basi radius, opsiyonel
// TEXTURE (stretch; slice9* > 0 ise 9-slice grid — atlas uyesiyse atlastan).
// CIZIM = INSTANCED QUAD EMISYONU (SDF parca modeli): kutu, UiPieces atlasindan
// kose hucresi + kenar falloff seridi + ic dolgu quad'lari yayar; border SDF RING
// katmani (USER.z), AA fwidth'ten her olcekte ~1px. Ayri batch sistemi YOK —
// ayni materyali paylasan bitisik quad'lar RenderQueue'da kendiliginden tek
// instanced draw'a merge olur (sprite yoluyla ayni mekanizma).
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
    [SerializeField] bool ignoreLayout;            // grup beni dizmesin (anchor'la yerlesirim)
    [SerializeField] LayoutMode layout;            // cocuk dizme modu
    [SerializeField] float spacing;
    [SerializeField] float padLeft, padTop, padRight, padBottom;
    [SerializeField] LayoutAlign alignChildren = LayoutAlign.Start;
    [SerializeField] bool reverse;
    [SerializeField] OverflowMode overflowX;   // Grow degilse icerik bu ekseni buyutmez
    [SerializeField] OverflowMode overflowY;

    // --- stil (StyleBox) ---
    [SerializeField] Gradient fill = new(Color.Transparent);
    [SerializeField] float borderLeft, borderTop, borderRight, borderBottom;
    [SerializeField] Gradient borderFill = new(Color.Transparent);
    [SerializeField] float radiusTL, radiusTR, radiusBR, radiusBL;

    // --- doku (opsiyonel arkaplan resmi; slice9* > 0 ise 9-slice) ---
    [SerializeField] Sprite sprite;
    [SerializeField] float slice9Left, slice9Top, slice9Right, slice9Bottom; // kaynak doku pikseli

    // --- golge (alpha > 0 ise kutunun altina yumusak kopya cizilir) ---
    [SerializeField] Color shadowColor = Color.Transparent;
    [SerializeField] Vec2 shadowOffset = new(0, 4);
    [SerializeField] float shadowBlur = 8f;   // falloff yari genisligi (dunya px)
    [SerializeField] float shadowGrow;        // kutuyu her yonde genislet (spread)

    // --- cozulmus durum ---
    float _rw, _rh;
    float _placedX, _placedY;   // layout'un son yazdigi lokal pozisyon
    bool _hasPlaced;            // false = pozisyon authored (bagimsiz kutu)
    internal bool _dirty = true;
    internal bool _queued;

    // --- stil geometri durumu ---
    // Agac ici painter sirasi (resolve DFS'inde numaralanir): ayni SortingOrder'da
    // cocuk hep parent USTUNE cizilir (Encode layer = SortingOrder + _paintSeq).
    internal int _paintSeq;

    // --- public API (property'ler kirletir; alanlara dogrudan erisim yok) ---
    public bool FitScreen { get => fitScreen; set { if (fitScreen != value) { fitScreen = value; MarkDirty(); } } }
    public float Width { get => width; set { if (width != value) { width = value; MarkDirty(); } } }
    public float Height { get => height; set { if (height != value) { height = value; MarkDirty(); } } }
    public Vec2 Pivot { get => pivot; set { if (pivot.x != value.x || pivot.y != value.y) { pivot = value; MarkDirty(); } } }
    public Vec2 AnchorMin { get => anchorMin; set { if (anchorMin.x != value.x || anchorMin.y != value.y) { anchorMin = value; MarkDirty(); } } }
    public Vec2 AnchorMax { get => anchorMax; set { if (anchorMax.x != value.x || anchorMax.y != value.y) { anchorMax = value; MarkDirty(); } } }
    public Vec2 AnchoredPos { get => anchoredPos; set { if (anchoredPos.x != value.x || anchoredPos.y != value.y) { anchoredPos = value; MarkDirty(); } } }
    public float Grow { get => grow; set { if (grow != value) { grow = value; MarkDirty(); } } }
    public bool IgnoreLayout { get => ignoreLayout; set { if (ignoreLayout != value) { ignoreLayout = value; MarkDirty(); } } }
    public LayoutMode Layout { get => layout; set { if (layout != value) { layout = value; MarkDirty(); } } }
    public float Spacing { get => spacing; set { if (spacing != value) { spacing = value; MarkDirty(); } } }
    public float PadLeft { get => padLeft; set { if (padLeft != value) { padLeft = value; MarkDirty(); } } }
    public float PadTop { get => padTop; set { if (padTop != value) { padTop = value; MarkDirty(); } } }
    public float PadRight { get => padRight; set { if (padRight != value) { padRight = value; MarkDirty(); } } }
    public float PadBottom { get => padBottom; set { if (padBottom != value) { padBottom = value; MarkDirty(); } } }
    public LayoutAlign AlignChildren { get => alignChildren; set { if (alignChildren != value) { alignChildren = value; MarkDirty(); } } }
    public bool Reverse { get => reverse; set { if (reverse != value) { reverse = value; MarkDirty(); } } }
    public OverflowMode OverflowX { get => overflowX; set { if (overflowX != value) { overflowX = value; MarkDirty(); } } }
    public OverflowMode OverflowY { get => overflowY; set { if (overflowY != value) { overflowY = value; MarkDirty(); } } }
    public OverflowMode Overflow { get => overflowX; set { OverflowX = value; OverflowY = value; } }

    // Stil degisimi layout'u ETKILEMEZ; quad emisyonu her frame stil alanlarindan
    // okur — cache yok, kirletme gerekmez.
    public Gradient Fill { get => fill; set => fill = value; }
    public float BorderLeft { get => borderLeft; set => borderLeft = value; }
    public float BorderTop { get => borderTop; set => borderTop = value; }
    public float BorderRight { get => borderRight; set => borderRight = value; }
    public float BorderBottom { get => borderBottom; set => borderBottom = value; }
    public float BorderWidth { get => borderLeft; set { borderLeft = borderTop = borderRight = borderBottom = value; } }
    public Gradient BorderFill { get => borderFill; set => borderFill = value; }
    public float RadiusTL { get => radiusTL; set => radiusTL = value; }
    public float RadiusTR { get => radiusTR; set => radiusTR = value; }
    public float RadiusBR { get => radiusBR; set => radiusBR = value; }
    public float RadiusBL { get => radiusBL; set => radiusBL = value; }
    public float CornerRadius { get => radiusTL; set { radiusTL = radiusTR = radiusBR = radiusBL = value; } }

    public Sprite Sprite { get => sprite; set => sprite = value; }
    public float Slice9Left { get => slice9Left; set => slice9Left = value; }
    public float Slice9Top { get => slice9Top; set => slice9Top = value; }
    public float Slice9Right { get => slice9Right; set => slice9Right = value; }
    public float Slice9Bottom { get => slice9Bottom; set => slice9Bottom = value; }
    public float Slice9 { get => slice9Left; set { slice9Left = slice9Top = slice9Right = slice9Bottom = value; } }

    public Color ShadowColor { get => shadowColor; set => shadowColor = value; }
    public Vec2 ShadowOffset { get => shadowOffset; set => shadowOffset = value; }
    public float ShadowBlur { get => shadowBlur; set => shadowBlur = value; }
    public float ShadowGrow { get => shadowGrow; set => shadowGrow = value; }

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

    protected internal override void OnEnable() => MarkDirty();
    protected internal override void OnDisable() => ParentBox()?.MarkDirty();

    // Editor Inspector'i alanlara dogrudan yazar; sonrasinda bunu cagirir.
    protected internal override void OnValidate() => MarkDirty();

    // Kirlet + zinciri yukari kirlet (autosize etkisi) + sweep kuyruguna gir.
    // INVARIANT: dirty kutu ⇒ tum ata kutulari dirty (cozum hep zincir tepesinden).
    public void MarkDirty()
    {
        var b = this;
        while (b != null && !b._dirty)
        {
            b._dirty = true;
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

    // fitScreen kaynagi: sahnenin main kamerasinin gorus rect'i (kutu projeksiyon
    // tipini bilmez); kamera yoksa eski davranis (0,0,Screen boyutu).
    bool FitRect(out float x, out float y, out float w, out float h)
    {
        var s = _gameObject?._scene;
        if (s == null) { x = y = w = h = 0; return false; }
        var cam = s.MainCamera;
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
            return;
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
        int seq = 0;
        _paintSeq = seq++;
        ArrangeChildren(ref seq);
    }

    // --- olcum (measure): asagidan yukari, yalniz kutulardan ---

    float PreferredW()
    {
        // Grow disinda icerik kutuyu buyutmez: eksen boyutu salt authored.
        if (overflowX != OverflowMode.Grow)
            return width;
        // Icerik = cocuk kutular + metin (max(authored, icerik) sozlesmesi).
        float aw = MathF.Max(width, TextPreferredW());
        if (layout == LayoutMode.None)
            return aw;
        float pad = padLeft + padRight;
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
                    sum += b.PreferredW(); // grow'lu cocuk olcume 0 girer (payini arrange verir)
            }
            return MathF.Max(aw, sum + pad + spacing * Math.Max(0, n - 1));
        }
        float mx = 0;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null || b.ignoreLayout)
                continue;
            mx = MathF.Max(mx, b.PreferredW());
        }
        return MathF.Max(aw, mx + pad);
    }

    float PreferredH()
    {
        if (overflowY != OverflowMode.Grow)
            return height;
        float ah = MathF.Max(height, TextPreferredH());
        if (layout == LayoutMode.None)
            return ah;
        float pad = padTop + padBottom;
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
                    sum += b.PreferredH();
            }
            return MathF.Max(ah, sum + pad + spacing * Math.Max(0, n - 1));
        }
        float mx = 0;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null || b.ignoreLayout)
                continue;
            mx = MathF.Max(mx, b.PreferredH());
        }
        return MathF.Max(ah, mx + pad);
    }

    // --- yerlestirme (arrange): yukaridan asagi ---

    // Anchor yerlesimi: nokta anchor'da boyut = preferred, stretch'te span*parent + delta.
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
            w = (anchorMax.x - anchorMin.x) * pw + width;
            x = tlx + anchorMin.x * pw + anchoredPos.x + pivot.x * w;
        }

        float h, y;
        if (anchorMin.y == anchorMax.y)
        {
            h = PreferredH();
            y = tly + anchorMin.y * ph + anchoredPos.y;
        }
        else
        {
            h = (anchorMax.y - anchorMin.y) * ph + height;
            y = tly + anchorMin.y * ph + anchoredPos.y + pivot.y * h;
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

    void ArrangeChildren(ref int seq)
    {
        if (layout == LayoutMode.None)
        {
            for (var t = transform.FirstChild; t != null; t = t.NextSibling)
            {
                var b = BoxOf(t);
                if (b == null)
                    continue;
                b._dirty = false;
                b._paintSeq = seq++;
                b.PlaceInParent(this);
                b.ArrangeChildren(ref seq);
            }
            return;
        }

        bool horiz = layout == LayoutMode.Horizontal;

        // 1. gecis: sabit toplam + grow agirlik toplami.
        float fixedSum = 0, growSum = 0;
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
                fixedSum += horiz ? b.PreferredW() : b.PreferredH();
        }

        float availMain = (horiz ? _rw - padLeft - padRight : _rh - padTop - padBottom)
            - spacing * Math.Max(0, count - 1);
        float leftover = MathF.Max(0, availMain - fixedSum);
        float crossAvail = horiz ? _rh - padTop - padBottom : _rw - padLeft - padRight;
        float tlx = -pivot.x * _rw, tly = -pivot.y * _rh;
        float cursor = horiz ? tlx + padLeft : tly + padTop;

        // 2. gecis: yerlestir (reverse: son cocuktan geriye).
        for (var t = reverse ? transform.LastChild : transform.FirstChild;
             t != null;
             t = reverse ? t.PrevSibling : t.NextSibling)
        {
            var b = BoxOf(t);
            if (b == null)
                continue;
            b._dirty = false;
            b._paintSeq = seq++;
            if (b.ignoreLayout)
            {
                b.PlaceInParent(this); // dekor/arkaplan: grup disi, anchor'la yerlesir
                b.ArrangeChildren(ref seq);
                continue;
            }

            float main = b.grow > 0 ? leftover * (b.grow / growSum)
                : (horiz ? b.PreferredW() : b.PreferredH());
            float cross = horiz ? b.PreferredH() : b.PreferredW();
            float crossOff = alignChildren switch
            {
                LayoutAlign.Center => (crossAvail - cross) * 0.5f,
                LayoutAlign.End => crossAvail - cross,
                _ => 0,
            };

            if (horiz)
            {
                b._rw = main;
                b._rh = cross;
                b.SetPos(cursor + b.pivot.x * main, tly + padTop + crossOff + b.pivot.y * cross);
            }
            else
            {
                b._rw = cross;
                b._rh = main;
                b.SetPos(tlx + padLeft + crossOff + b.pivot.x * cross, cursor + b.pivot.y * main);
            }
            cursor += main + spacing;
            b.ArrangeChildren(ref seq);
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

    void ComputeClip(in Mat4 wm)
    {
        _hasClip = false;
        // Kendi uzayimiz eksen-hizali degilse dunya penceresi lokale cevrilemez.
        if (MathF.Abs(wm.m[1]) > 1e-4f || MathF.Abs(wm.m[4]) > 1e-4f
            || wm.m[0] == 0 || wm.m[5] == 0)
            return;
        float wx0 = float.MinValue, wy0 = float.MinValue;
        float wx1 = float.MaxValue, wy1 = float.MaxValue;
        for (var p = ParentBox(); p != null; p = p.ParentBox())
        {
            bool hx = p.overflowX == OverflowMode.Hidden;
            bool hy = p.overflowY == OverflowMode.Hidden;
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

    static Material _styleMat;
    static Texture _white;

    internal static Material StyleMat
    {
        get
        {
            if (_styleMat == null)
            {
                _white = Texture.FromColor(1, 1, Color.White);
                _white.Persistent = true;
                _styleMat = new Material { MainTexture = _white };
            }
            return _styleMat;
        }
    }

    internal static Texture StyleWhite { get { _ = StyleMat; return _white; } }

    bool HasBorder => borderFill.Visible
        && (borderLeft > 0 || borderTop > 0 || borderRight > 0 || borderBottom > 0);

    bool HasShadow => shadowColor.a > 0;

    bool HasStyle
        => sprite != null || fill.Visible || HasBorder || HasShadow;

    // Kutu SDF parcalari font atlasinin sabit bolgesinden orneklenir (kendi fontu,
    // yoksa DefaultFont) — sahnedeki metin + tum kutular ayni texview'i paylasip
    // tek instanced draw'a merge olur. Hic font yoksa lazy fallback atlasi.
    UiAtlas PiecesAtlas(out Material mat)
    {
        var f = font ?? UiPieces.DefaultFont;
        if (f?.Atlas != null)
        {
            mat = f.Material;
            return f.Pieces;
        }
        mat = UiPieces.SharedMaterial;
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
        if (!HasStyle && !HasText)
            return;
        ResolveIfDirty();
        if (_rw <= 0 || _rh <= 0)
            return;

        Mat4 wm = transform._getWorldMatrix(); // kopya: lokal fonksiyonlar ref yakalayamaz
        ComputeClip(in wm);
        int layer = SortingOrder + _paintSeq;  // ayni layer'da parent < cocuk (DFS sirasi)
        float x0 = -pivot.x * _rw, y0 = -pivot.y * _rh;
        float x1 = x0 + _rw, y1 = y0 + _rh;

        if (HasShadow)
            EmitShadow(queue, wm, x0, y0, x1, y1, layer); // ayni layer'da ilk = altta

        if (sprite != null)
        {
            EncodeTextured(queue, in wm, x0, y0, x1, y1, layer);
            if (HasText)
                EncodeText(queue, in wm, x0, y0, layer); // submit sirasi: kutunun ustunde
            return;
        }

        bool hasFill = fill.Visible;
        if (HasBorder)
        {
            // Karsilikli kenarlarin toplami kutuya sigmali (CSS border cakisma kurali).
            float bl = borderLeft, bt = borderTop, br = borderRight, bb = borderBottom;
            float sx = bl + br > _rw ? _rw / (bl + br) : 1f;
            float sy = bt + bb > _rh ? _rh / (bt + bb) : 1f;
            bl *= sx; br *= sx; bt *= sy; bb *= sy;
            EmitLayer(queue, wm, x0, y0, x1, y1,
                radiusTL, radiusTR, radiusBR, radiusBL, bl, bt, br, bb, isBorder: true, layer);
            if (hasFill)
                EmitLayer(queue, wm, x0 + bl, y0 + bt, x1 - br, y1 - bb,
                    MathF.Max(radiusTL - MathF.Max(bl, bt), 0),
                    MathF.Max(radiusTR - MathF.Max(br, bt), 0),
                    MathF.Max(radiusBR - MathF.Max(br, bb), 0),
                    MathF.Max(radiusBL - MathF.Max(bl, bb), 0),
                    0, 0, 0, 0, isBorder: false, layer);
        }
        else if (hasFill)
            EmitLayer(queue, wm, x0, y0, x1, y1,
                radiusTL, radiusTR, radiusBR, radiusBL, 0, 0, 0, 0, isBorder: false, layer);

        if (HasText)
            EncodeText(queue, in wm, x0, y0, layer); // submit sirasi: kutunun ustunde
    }

    const float AaOut = 2f;  // sekil disina tasan AA payi (dunya birimi)
    const float EdgeIn = 2f; // kenar seridinin ic kalinligi (SDF degeri 1'e ulasir)

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
                col, col, user, layer);
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
            EmitQuad(q, mat, wm, x0 + rtl, y0 - b, x1 - rtr, y0 + b, uA, pvIn, uB, pvOut, col, col, eu, layer);
            EmitQuad(q, mat, wm, x0 + rbl, y1 - b, x1 - rbr, y1 + b, uA, pvOut, uB, pvIn, col, col, eu, layer);
        }
        if (y1 - rbl > y0 + rtl)
            EmitQuad(q, mat, wm, x0 - b, y0 + rtl, x0 + b, y1 - rbl, puOut, vA, puIn, vB, col, col, eu, layer);
        if (y1 - rbr > y0 + rtr)
            EmitQuad(q, mat, wm, x1 - b, y0 + rtr, x1 + b, y1 - rbr, puIn, vA, puOut, vB, col, col, eu, layer);

        // Ic dolgu (alpha 1 bolgesi): kenar seritlerinin ici, hucrelerle cakismadan.
        float ix0 = x0 + b, ix1 = x1 - b, iy0 = y0 + b, iy1 = y1 - b;
        if (ix1 <= ix0 || iy1 <= iy0)
            return;
        float sTL = MathF.Max(rtl - b, 0), sTR = MathF.Max(rtr - b, 0);
        float sBR = MathF.Max(rbr - b, 0), sBL = MathF.Max(rbl - b, 0);
        float tT = MathF.Max(sTL, sTR), tB = MathF.Max(sBL, sBR);
        void Solid(float ax0, float ay0, float ax1, float ay1)
        {
            if (ax1 <= ax0 || ay1 <= ay0)
                return;
            EmitQuad(q, mat, wm, ax0, ay0, ax1, ay1, su0, sv0, su1, sv1, col, col, default, layer);
        }
        Solid(ix0 + sTL, iy0, ix1 - sTR, iy0 + tT);
        if (sTL < tT) Solid(ix0, iy0 + sTL, ix0 + sTL, iy0 + tT);
        if (sTR < tT) Solid(ix1 - sTR, iy0 + sTR, ix1, iy0 + tT);
        Solid(ix0, iy0 + tT, ix1, iy1 - tB);
        Solid(ix0 + sBL, iy1 - tB, ix1 - sBR, iy1);
        if (sBL < tB) Solid(ix0, iy1 - tB, ix0 + sBL, iy1 - sBL);
        if (sBR < tB) Solid(ix1 - sBR, iy1 - tB, ix1, iy1 - sBR);
    }

    // Tek katman rounded-rect: 4 SDF kose hucresi + 4 kenar falloff seridi +
    // (dolguysa) ic quad'lar — hepsi disjoint, AA fwidth'ten her olcekte ~1px.
    // isBorder=true: ic dolgu YOK, USER.z ic kenar bandiyla RING cizilir.
    void EmitLayer(RenderQueue q, Mat4 wm, float x0, float y0, float x1, float y1,
        float rtl, float rtr, float rbr, float rbl,
        float ringL, float ringT, float ringR, float ringB, bool isBorder, int layer)
    {
        float w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0)
            return;
        bool ring = isBorder;

        // Radius sigdirma (CSS pair kurali) + parca haritalamasi icin minimum.
        float k = 1f;
        if (rtl + rtr > w) k = MathF.Min(k, w / (rtl + rtr));
        if (rbl + rbr > w) k = MathF.Min(k, w / (rbl + rbr));
        if (rtl + rbl > h) k = MathF.Min(k, h / (rtl + rbl));
        if (rtr + rbr > h) k = MathF.Min(k, h / (rtr + rbr));
        rtl = MathF.Max(rtl * k, 0.75f); rtr = MathF.Max(rtr * k, 0.75f);
        rbr = MathF.Max(rbr * k, 0.75f); rbl = MathF.Max(rbl * k, 0.75f);

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
        Color At(float y) => isBorder ? BorderAt(y) : FillAt(y);

        // Kose hucresi: parca koordinati merkezden (0) kutu kosesine dogru buyur;
        // farLeft/farTop = kutu kosesinin hucre icindeki yonu.
        void Cell(float cx0, float cy0, float cx1, float cy1, float r, bool farLeft, bool farTop, float bC)
        {
            float m = MathF.Min(AaOut, 0.4f * r);
            float pFar = (r + m) * pieceR / r;
            if (farLeft) cx0 -= m; else cx1 += m;
            if (farTop) cy0 -= m; else cy1 += m;
            float uNear = CU(0f), uFar = CU(pFar);
            float vNear = CV(0f), vFar = CV(pFar);
            Vec4 user = ring
                ? new Vec4(0f, 0.5f, MathF.Min(0.5f + bC * pieceR / (r * pieceS), 0.98f), 0f)
                : default;
            EmitQuad(q, mat, wm, cx0, cy0, cx1, cy1,
                farLeft ? uFar : uNear, farTop ? vNear : vFar,
                farLeft ? uNear : uFar, farTop ? vFar : vNear,
                At(cy0), At(cy1), user, layer);
        }
        Cell(x0, y0, x0 + rtl, y0 + rtl, rtl, true, true, MathF.Max(ringL, ringT));
        Cell(x1 - rtr, y0, x1, y0 + rtr, rtr, false, true, MathF.Max(ringR, ringT));
        Cell(x1 - rbr, y1 - rbr, x1, y1, rbr, false, false, MathF.Max(ringR, ringB));
        Cell(x0, y1 - rbl, x0 + rbl, y1, rbl, true, false, MathF.Max(ringL, ringB));

        // Yatay kenar seridi: parca kesiti (sutun x~0.7px, d≈y) dikeyde falloff.
        void HEdge(float xa, float xb, float ye, bool outUp, float b)
        {
            if (xb <= xa)
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
            var user = ring ? new Vec4(0f, 0.5f, c2, 0f) : default;
            EmitQuad(q, mat, wm, xa, yA, xb, yB, uA, v0, uB, v1, At(yA), At(yB), user, layer);
        }
        // Dikey kenar seridi: ayni kesit satirdan (y~0.7px, d≈x) yatay falloff.
        void VEdge(float ya, float yb, float xe, bool outLeft, float b)
        {
            if (yb <= ya)
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
            var user = ring ? new Vec4(0f, 0.5f, c2, 0f) : default;
            EmitQuad(q, mat, wm, xA, ya, xB, yb, u0, vA, u1, vB, At(ya), At(yb), user, layer);
        }
        HEdge(x0 + rtl, x1 - rtr, y0, true, ringT);
        HEdge(x0 + rbl, x1 - rbr, y1, false, ringB);
        VEdge(y0 + rtl, y1 - rbl, x0, true, ringL);
        VEdge(y0 + rtr, y1 - rbr, x1, false, ringR);

        if (ring)
            return;

        // Ic dolgu: kenar seritlerinin ici, kose hucreleriyle cakismadan.
        float ix0 = x0 + EdgeIn, ix1 = x1 - EdgeIn, iy0 = y0 + EdgeIn, iy1 = y1 - EdgeIn;
        if (ix1 <= ix0 || iy1 <= iy0)
            return;
        float sTL = MathF.Max(rtl - EdgeIn, 0), sTR = MathF.Max(rtr - EdgeIn, 0);
        float sBR = MathF.Max(rbr - EdgeIn, 0), sBL = MathF.Max(rbl - EdgeIn, 0);
        float tT = MathF.Max(sTL, sTR), tB = MathF.Max(sBL, sBR);
        void Solid(float ax0, float ay0, float ax1, float ay1)
        {
            if (ax1 <= ax0 || ay1 <= ay0)
                return;
            EmitQuad(q, mat, wm, ax0, ay0, ax1, ay1, su0, sv0, su1, sv1, At(ay0), At(ay1), default, layer);
        }
        Solid(ix0 + sTL, iy0, ix1 - sTR, iy0 + tT);
        if (sTL < tT) Solid(ix0, iy0 + sTL, ix0 + sTL, iy0 + tT);
        if (sTR < tT) Solid(ix1 - sTR, iy0 + sTR, ix1, iy0 + tT);
        Solid(ix0, iy0 + tT, ix1, iy1 - tB);
        Solid(ix0 + sBL, iy1 - tB, ix1 - sBR, iy1);
        if (sBL < tB) Solid(ix0, iy1 - tB, ix0 + sBL, iy1 - sBL);
        if (sBR < tB) Solid(ix1 - sBR, iy1 - tB, ix1, iy1 - sBR);
    }

    // Dokulu kutu: stretch tek quad / slice9 3x3 grid. Radius/border bu modda
    // uygulanmaz (SDF parca ile doku ayni sampler'i paylasamaz — bilinen sinir).
    void EncodeTextured(RenderQueue q, in Mat4 world, float x0, float y0, float x1, float y1, int layer)
    {
        var tex = sprite.Page;
        if (tex == null)
            return;
        float uu0 = 0f, uuS = 1f, vv0 = 0f, vvS = 1f;
        if (sprite.IsRegion)
        {
            float aw = tex.Width, ah = tex.Height;
            uu0 = sprite.X / aw; uuS = sprite.W / aw;
            vv0 = sprite.Y / ah; vvS = sprite.H / ah;
        }
        var mat = StyleMat;
        mat.MainTexture = tex; // TexView DrawMesh aninda yakalanir (paylasilan materyal deseni)
        bool tinted = fill.Visible;
        Color cTop = tinted ? FillAt(y0) : Color.White;
        Color cBot = tinted ? FillAt(y1) : Color.White;

        if (slice9Left <= 0 && slice9Top <= 0 && slice9Right <= 0 && slice9Bottom <= 0)
        {
            EmitQuad(q, mat, world, x0, y0, x1, y1,
                uu0, vv0, uu0 + uuS, vv0 + vvS, cTop, cBot, default, layer);
            return;
        }

        // 9-slice: koseler doku-piksel boyutunda sabit, kenarlar/orta gerilir;
        // dilimler sigmazsa oranla kisilir (border cakisma kuraliyla ayni).
        float sl = slice9Left, st = slice9Top, sr = slice9Right, sb = slice9Bottom;
        float w = x1 - x0, h = y1 - y0;
        float kx = sl + sr > w ? w / (sl + sr) : 1f;
        float ky = st + sb > h ? h / (st + sb) : 1f;
        float tw = Math.Max(1, sprite.LogicalWidth), th = Math.Max(1, sprite.LogicalHeight);
        Span<float> xs = stackalloc float[4] { x0, x0 + sl * kx, x1 - sr * kx, x1 };
        Span<float> ys = stackalloc float[4] { y0, y0 + st * ky, y1 - sb * ky, y1 };
        Span<float> us = stackalloc float[4]
            { 0f, Math.Clamp(sl / tw, 0f, 1f), Math.Clamp(1f - sr / tw, 0f, 1f), 1f };
        Span<float> vs = stackalloc float[4] // v: ust=1, alt=0 (GL yonelimi)
            { 1f, Math.Clamp(1f - st / th, 0f, 1f), Math.Clamp(sb / th, 0f, 1f), 0f };
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                EmitQuad(q, mat, world, xs[c], ys[r], xs[c + 1], ys[r + 1],
                    uu0 + us[c] * uuS, vv0 + vs[r + 1] * vvS,
                    uu0 + us[c + 1] * uuS, vv0 + vs[r] * vvS,
                    tinted ? FillAt(ys[r]) : Color.White,
                    tinted ? FillAt(ys[r + 1]) : Color.White, default, layer);
    }

    // Lokal-uzay dikdortgenini world matrisiyle tek instanced quad'a cevirir.
    // Klip penceresi varsa quad kirpilir: UV lineer remap (SDF parca ornekleme
    // orantili kalir) + dikey gradient renkleri kirpilan banda lerp'lenir.
    void EmitQuad(RenderQueue q, Material mat, in Mat4 world,
        float qx0, float qy0, float qx1, float qy1,
        float u0, float v0, float u1, float v1,
        Color cTop, Color cBottom, in Vec4 user, int layer)
    {
        if (qx1 <= qx0 || qy1 <= qy0)
            return;
        if (_hasClip)
        {
            float nx0 = MathF.Max(qx0, _clX0), ny0 = MathF.Max(qy0, _clY0);
            float nx1 = MathF.Min(qx1, _clX1), ny1 = MathF.Min(qy1, _clY1);
            if (nx1 <= nx0 || ny1 <= ny0)
                return;
            if (nx0 != qx0 || nx1 != qx1)
            {
                float iw = 1f / (qx1 - qx0), du = u1 - u0;
                u1 = u0 + du * ((nx1 - qx0) * iw);
                u0 += du * ((nx0 - qx0) * iw);
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
                Color ct = LerpC(cTop, cBottom, t0);
                cBottom = LerpC(cTop, cBottom, t1);
                cTop = ct;
                qy0 = ny0; qy1 = ny1;
            }
        }
        Mat4 m = world;
        float cx = (qx0 + qx1) * 0.5f, cy = (qy0 + qy1) * 0.5f;
        float sx = qx1 - qx0, sy = qy1 - qy0;
        m.m[12] += m.m[0] * cx + m.m[4] * cy;
        m.m[13] += m.m[1] * cx + m.m[5] * cy;
        m.m[14] += m.m[2] * cx + m.m[6] * cy;
        m.m[0] *= sx; m.m[1] *= sx; m.m[2] *= sx;
        m.m[4] *= sy; m.m[5] *= sy; m.m[6] *= sy;
        // tint koseleri uv uzayinda: ust kenar = uv.y=1 (Mesh.Quad y-down duzeni)
        q.DrawMesh(Mesh.Quad(), mat, in m, cBottom, cBottom, cTop, cTop, in user, u0, v0, u1, v1, layer);
    }

    static Color LerpC(Color a, Color b, float t) => new(
        (byte)(a.r + (b.r - a.r) * t),
        (byte)(a.g + (b.g - a.g) * t),
        (byte)(a.b + (b.b - a.b) * t),
        (byte)(a.a + (b.a - a.a) * t));

    // Dikey gradient KUTU bandina gore (katmandan bagimsiz — parca sinirlari dikissiz).
    Color FillAt(float y)
        => fill.At((y + pivot.y * _rh) / _rh);

    Color BorderAt(float y)
        => borderFill.At((y + pivot.y * _rh) / _rh);
}



// Sahne layout sistemi (ISceneSystem): kirli kutu kuyrugu. Scene layout'u
// TANIMAZ — sweep faz kancasindan kosar. GetSystem<LayoutSystem>() lazy dogar.
internal sealed class LayoutSystem : ISceneSystem
{
    LayoutBox[] _queue = new LayoutBox[32];
    int _count;

    internal void Queue(LayoutBox b)
    {
        if (_count == _queue.Length) Array.Resize(ref _queue, _count * 2);
        _queue[_count++] = b;
    }

    // Pull'un kacirdigi (o frame kimsenin okumadigi) kirli kutulari cozer.
    // FitScreen kutular kuyrukta KALIR (ekran boyutu degisimi izlensin).
    public void AfterUpdate(Scene scene)
    {
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
