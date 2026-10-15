using System;

namespace DigitoyEngine;

// LayoutBox stil katmani: BoxStyle asset referansi + durum + transition.
//   style != null  -> kutunun gorunum alanlari (BoxLook + sprite/fit/slice) stilin ilgili durumundan
//                     KOPYALANIR (surulmus alanlar); Encode'da TEK int kiyasiyla asset degisimi yakalanir.
//   SetState(s)    -> from = su anki gorunum (yarida kesilince mevcut degerden devam), to = varyant;
//                     adim alanlari aninda; BoxLook `transition` sn boyunca LayoutSystem tick'inde lerp.
// Hicbir BoxLook alani layout'u etkilemez -> gecis MarkDirty uretmez, dirty zinciri calismaz.
public sealed partial class LayoutBox
{
    [SerializeField] BoxStyle style;

    public BoxStyle Style
    {
        get => style;
        set
        {
            if (ReferenceEquals(style, value))
                return;
            style = value;
            if (value == null)
                RestoreTransform(); // stil gitti: surulen Transform tabani geri
            ApplyStyle();
        }
    }

    StateSet _states;       // aktif isimli durumlar (hash); bos = Normal
    int _styleVersion = -1;
    BoxStyle _styleApplied; // son uygulanan referans (Inspector dogrudan alan yazarsa SyncStyle yakalar)

    // Transition durumu (yalniz gecisteyken anlamli; LayoutSystem listesinde).
    BoxLook _lookFrom, _lookTo;
    float _lookT, _lookDur;
    Ease _lookEase;
    internal bool _inTransition;
    bool _everEncoded; // ilk Encode'dan sonra true: ondan onceki SetState anidir (giris flash'i yok)

    public StateSet States => _states;
    public int StateCount => _states.Count;
    public bool HasState(int hash) => _states.Contains(hash);
    public bool HasState(string key) => _states.Contains(BoxStyle.Hash(key));
    public bool InTransition => _inTransition;
    public float TransitionProgress => _inTransition ? _lookT : 1f;

    // Durum kumesini TEK duruma indirger (0/bos = Normal). Eski tek-durum kullanimi; Button gibi suruculer.
    public void SetState(int hash)
    {
        var next = new StateSet();
        next.Add(hash);
        ApplyStates(in next);
    }

    public void SetState(string key) { WarnUnknownKey(key); SetState(BoxStyle.Hash(key)); }

    // Coklu durum: ekle/cikar (pressed+selected). Sira stilin varyant listesinden gelir, ekleme sirasindan degil.
    public void AddState(int hash) { var next = _states; if (next.Add(hash)) ApplyStates(in next); }
    public void AddState(string key) { WarnUnknownKey(key); AddState(BoxStyle.Hash(key)); }
    public void RemoveState(int hash) { var next = _states; if (next.Remove(hash)) ApplyStates(in next); }
    public void RemoveState(string key) => RemoveState(BoxStyle.Hash(key));
    public void ClearStates() { var next = new StateSet(); ApplyStates(in next); }

    // Edit modunda yazim hatasi yakalama: stil atanmis ve key varyantlarda yoksa uyari (release'te sifir maliyet:
    // yalniz EditMode sahnede kosar).
    void WarnUnknownKey(string key)
    {
        if (string.IsNullOrEmpty(key) || style == null)
            return;
        var scene = _gameObject?._scene;
        if (scene != null && scene.EditMode && !style.Has(key))
            AssetDatabase.LogWarning?.Invoke($"[style] '{_gameObject.name}': state '{key}' is not defined in the assigned BoxStyle");
    }

    // Durum degisimi: stil yoksa yalniz durum saklanir (gorunum degismez).
    void ApplyStates(in StateSet next)
    {
        if (_states.Equals(in next))
            return;
        _states = next;
        if (style == null)
            return;
        style.Resolve(in _states, out var st);
        ApplyStep(in st);
        float dur = style.transition;
        var scene = _gameObject?._scene;
        // Ilk cizimden once (OnEnable'da baslangic durumu) gecis yok: dogrudan hedef gorunum.
        if (dur <= 0f || scene == null || scene.EditMode || !_everEncoded)
        {
            ApplyLook(in st.look);
            if (_inTransition) { _inTransition = false; }
            return;
        }
        _lookFrom = CaptureLook();
        _lookTo = st.look;
        _lookT = 0f;
        _lookDur = dur;
        _lookEase = style.transitionEase;
        if (!_inTransition)
        {
            _inTransition = true;
            scene.GetSystem<LayoutSystem>().AddTransition(this);
        }
    }

    // Stili bastan uygula (ref degisti / asset yeniden yuklendi / enable / validate): mevcut durumun
    // varyanti aninda, gecis yarida kesilir.
    void ApplyStyle()
    {
        _styleApplied = style;
        if (style == null)
            return;
        _styleVersion = AssetDatabase.ObjectVersion;
        style.Resolve(in _states, out var st);
        ApplyStep(in st);
        ApplyLook(in st.look);
        _inTransition = false; // listede kalsa da tick ilk adimda dusurur
    }

    // Encode basinda: asset degismis ya da referans dogrudan yazilmissa yeniden kopyala (1-2 kiyas).
    void SyncStyle()
    {
        if (!ReferenceEquals(_styleApplied, style) || (style != null && _styleVersion != AssetDatabase.ObjectVersion))
            ApplyStyle();
    }

    // OnValidate (Inspector alan yazimi / animasyon bildirimi): gecis surerken stili yeniden
    // basma (animasyon bildirimi gecisi oldururdu); referans degisimi SyncStyle'da yakalanir.
    void ValidateStyle()
    {
        if (!_inTransition)
            ApplyStyle();
    }

    // LayoutSystem tick'i: true = devam, false = bitti (listeden duser).
    internal bool TickTransition(float dt)
    {
        if (!_inTransition || style == null)
            return false;
        _lookT = MathF.Min(1f, _lookT + (_lookDur > 0f ? dt / _lookDur : 1f));
        float e = Easing.Evaluate(_lookEase, _lookT);
        if (_lookT >= 1f)
        {
            ApplyLook(in _lookTo);
            _inTransition = false;
            return false;
        }
        var l = BoxLook.Lerp(in _lookFrom, in _lookTo, e);
        ApplyLook(in l);
        return true;
    }

    void ApplyStep(in BoxStyleState st)
    {
        sprite = st.sprite;
        spriteFit = st.spriteFit;
        slice9 = st.slice9;
    }

    public BoxLook CaptureLook()
    {
        BoxLook l;
        l.fill = fill;
        l.border = border;
        l.borderFill = borderFill;
        l.radius = radius;
        l.shadowColor = shadowColor;
        l.shadowOffset = shadowOffset;
        l.shadowBlur = shadowBlur;
        l.shadowGrow = shadowGrow;
        l.innerShadowColor = innerShadowColor;
        l.innerShadowBlur = innerShadowBlur;
        l.lightColor = lightColor;
        l.lightWidth = lightWidth;
        l.lightHeight = lightHeight;
        l.lightOffset = lightOffset;
        l.textFill = textFill;
        l.textOutlineWidth = textOutlineWidth;
        l.textOutline = textOutline;
        l.textShadowColor = textShadowColor;
        l.textShadowOffset = textShadowOffset;
        l.textShadowBlur = textShadowBlur;
        // Transform: taban yakalanmissa surulen oran, degilse notr (tabandan sapma yok).
        if (_transformDriven)
        {
            var s = transform.localScale;
            l.scale = new Vec2(_baseScale.x != 0 ? s.x / _baseScale.x : 1f, _baseScale.y != 0 ? s.y / _baseScale.y : 1f);
            l.rotation = transform.localEulerAngles.z - _baseRot;
        }
        else
        {
            l.scale = BoxLook.UnitScale;
            l.rotation = 0f;
        }
        return l;
    }

    // --- Transform katmani (scale/rotation) ---
    // Taban = stil Transform'a ILK dokundugunda okunan authored deger; stil kalkinca geri yazilir.
    // Notr (1,1 / 0) ve hic dokunulmamissa Transform'a yazim yok: stil kullanmayan kutuya sifir maliyet.
    bool _transformDriven;
    Vec3 _baseScale;
    float _baseRot;

    void ApplyTransformLook(Vec2 scale, float rotation)
    {
        bool neutral = (scale.x == 1f && scale.y == 1f || scale.x == 0f && scale.y == 0f) && rotation == 0f; // (0,0) = belirtilmemis
        if (!_transformDriven)
        {
            if (neutral)
                return;
            _transformDriven = true;
            _baseScale = transform.localScale;
            _baseRot = transform.localEulerAngles.z;
        }
        if (scale.x == 0f && scale.y == 0f)
            scale = BoxLook.UnitScale;
        var ns = new Vec3(_baseScale.x * scale.x, _baseScale.y * scale.y, _baseScale.z);
        var cs = transform.localScale;
        if (cs.x != ns.x || cs.y != ns.y)
            transform.localScale = ns;
        var e = transform.localEulerAngles;
        float nr = _baseRot + rotation;
        if (e.z != nr)
            transform.localEulerAngles = new Vec3(e.x, e.y, nr);
    }

    void RestoreTransform()
    {
        if (!_transformDriven)
            return;
        _transformDriven = false;
        transform.localScale = _baseScale;
        var e = transform.localEulerAngles;
        transform.localEulerAngles = new Vec3(e.x, e.y, _baseRot);
    }

    // Dogrudan alan yazimi: setter'lar da kirletmez ama burada property dolasimi gereksiz.
    public void ApplyLook(in BoxLook l)
    {
        fill = l.fill;
        border = l.border;
        borderFill = l.borderFill;
        radius = l.radius;
        shadowColor = l.shadowColor;
        shadowOffset = l.shadowOffset;
        shadowBlur = l.shadowBlur;
        shadowGrow = l.shadowGrow;
        innerShadowColor = l.innerShadowColor;
        innerShadowBlur = l.innerShadowBlur;
        lightColor = l.lightColor;
        lightWidth = l.lightWidth;
        lightHeight = l.lightHeight;
        lightOffset = l.lightOffset;
        textFill = l.textFill;
        textOutlineWidth = l.textOutlineWidth;
        textOutline = l.textOutline;
        textShadowColor = l.textShadowColor;
        textShadowOffset = l.textShadowOffset;
        textShadowBlur = l.textShadowBlur;
        ApplyTransformLook(l.scale, l.rotation);
    }
}
