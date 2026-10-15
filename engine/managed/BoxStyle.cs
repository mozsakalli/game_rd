using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Kutu durumlari ISIMLIDIR (BoxStyleState.key, serbest metin: "pressed", "disabled", "turn-active", "vip"...).
// String yalniz authoring'de: BoxStyle.Hash(key) (FNV-1a, ClipHash ile ayni) bir kez hesaplanir, runtime int'le
// calisir. Coklu aktif durum (pressed+selected): StateSet, varyantlar LISTE SIRASIYLA bindirilir (sonraki ezer).
// Button sozlesmeli adlar: BoxStyle.Pressed/Disabled/Hover/Selected. Hover v1'de uretilmez (Pointer bosta Move
// sifir maliyet kurali, mobil oncelik).
public struct StateSet
{
    public const int Max = 4;
    public int A, B, C, D;
    public int Count;

    public bool Contains(int h)
        => Count > 0 && (A == h || (Count > 1 && (B == h || (Count > 2 && (C == h || (Count > 3 && D == h))))));

    public bool Add(int h)
    {
        if (h == 0 || Contains(h)) return false;
        switch (Count)
        {
            case 0: A = h; break;
            case 1: B = h; break;
            case 2: C = h; break;
            case 3: D = h; break;
            default: return false; // dolu: sessiz red (4 es zamanli durum pratik ust sinir)
        }
        Count++;
        return true;
    }

    public bool Remove(int h)
    {
        if (!Contains(h)) return false;
        if (A == h) { A = B; B = C; C = D; }
        else if (B == h) { B = C; C = D; }
        else if (C == h) { C = D; }
        Count--;
        D = 0;
        if (Count <= 2) C = 0;
        if (Count <= 1) B = 0;
        if (Count <= 0) A = 0;
        return true;
    }

    public void Clear() { A = B = C = D = 0; Count = 0; }

    public int Get(int i) => i switch { 0 => A, 1 => B, 2 => C, 3 => D, _ => 0 };

    public bool Equals(in StateSet o)
        => Count == o.Count && A == o.A && B == o.B && C == o.C && D == o.D;
}

// LayoutBox'in LAYOUT'U ETKILEMEYEN gorunum alanlari - tek struct: stil asset'i, durum varyanti
// ve transition snapshot'lari (from/to) ayni tipi kullanir. Lerp'i alloc'suz; hicbir alan
// MarkDirty gerektirmez (kutu setter'lari da kirletmez) -> gecis layout'a sifir maliyet.
[Serializable]
public struct BoxLook
{
    public Gradient fill;
    public Vec4 border;            // left, top, right, bottom
    public Gradient borderFill;
    public Vec4 radius;            // TL, TR, BR, BL
    public Color shadowColor;
    public Vec2 shadowOffset;
    public float shadowBlur, shadowGrow;
    public Color innerShadowColor;
    public float innerShadowBlur;
    public Color lightColor;
    public float lightWidth, lightHeight;
    public Vec2 lightOffset;
    public Gradient textFill;
    public float textOutlineWidth;
    public Gradient textOutline;
    public Color textShadowColor;
    public Vec2 textShadowOffset;
    public float textShadowBlur;
    // Transform katmani: localScale = taban * scale, localEulerAngles.z = taban + rotation (derece).
    // Layout'a girmez (scale layout'a girmez kurali) -> MarkDirty yok. Baska bir surucu (Tween/MovieClip)
    // ayni anda Transform yaziyorsa son yazan kazanir (Button.cs notuyla ayni).
    public Vec2 scale;
    public float rotation;

    // Notr gorunum tabani: scale (1,1) - struct default (0,0) degil; stil yazarken Normal'de scale verilmezse
    // Resolve bunu basar (0 olcek kutuyu yok ederdi).
    public static readonly Vec2 UnitScale = new(1f, 1f);

    public static BoxLook Lerp(in BoxLook a, in BoxLook b, float t)
    {
        BoxLook r;
        r.fill = LerpG(a.fill, b.fill, t);
        r.border = LerpV4(a.border, b.border, t);
        r.borderFill = LerpG(a.borderFill, b.borderFill, t);
        r.radius = LerpV4(a.radius, b.radius, t);
        r.shadowColor = LerpC(a.shadowColor, b.shadowColor, t);
        r.shadowOffset = LerpV2(a.shadowOffset, b.shadowOffset, t);
        r.shadowBlur = L(a.shadowBlur, b.shadowBlur, t);
        r.shadowGrow = L(a.shadowGrow, b.shadowGrow, t);
        r.innerShadowColor = LerpC(a.innerShadowColor, b.innerShadowColor, t);
        r.innerShadowBlur = L(a.innerShadowBlur, b.innerShadowBlur, t);
        r.lightColor = LerpC(a.lightColor, b.lightColor, t);
        r.lightWidth = L(a.lightWidth, b.lightWidth, t);
        r.lightHeight = L(a.lightHeight, b.lightHeight, t);
        r.lightOffset = LerpV2(a.lightOffset, b.lightOffset, t);
        r.textFill = LerpG(a.textFill, b.textFill, t);
        r.textOutlineWidth = L(a.textOutlineWidth, b.textOutlineWidth, t);
        r.textOutline = LerpG(a.textOutline, b.textOutline, t);
        r.textShadowColor = LerpC(a.textShadowColor, b.textShadowColor, t);
        r.textShadowOffset = LerpV2(a.textShadowOffset, b.textShadowOffset, t);
        r.textShadowBlur = L(a.textShadowBlur, b.textShadowBlur, t);
        r.scale = LerpV2(a.scale, b.scale, t);
        r.rotation = L(a.rotation, b.rotation, t);
        return r;
    }

    static float L(float a, float b, float t) => a + (b - a) * t;
    static Vec2 LerpV2(Vec2 a, Vec2 b, float t) => new(L(a.x, b.x, t), L(a.y, b.y, t));
    static Vec4 LerpV4(Vec4 a, Vec4 b, float t) => new(L(a.x, b.x, t), L(a.y, b.y, t), L(a.z, b.z, t), L(a.w, b.w, t));

    static Color LerpC(Color a, Color b, float t) => new(
        Ch(a.r + (b.r - a.r) * t), Ch(a.g + (b.g - a.g) * t), Ch(a.b + (b.b - a.b) * t), Ch(a.a + (b.a - a.a) * t));

    static byte Ch(float v) => (byte)Math.Clamp((int)(v + 0.5f), 0, 255);

    // Gradient: renkler/duraklar lerp; type/direction/stops adim (CSS discrete: %50'de hedefe gecer).
    static Gradient LerpG(in Gradient a, in Gradient b, float t)
    {
        var step = t < 0.5f ? a : b;
        Gradient r = step;
        r.color = LerpC(a.color, b.color, t);
        r.color2 = LerpC(a.color2, b.color2, t);
        r.color3 = LerpC(a.color3, b.color3, t);
        r.color4 = LerpC(a.color4, b.color4, t);
        r.stop3 = L(a.stop3, b.stop3, t);
        r.stop4 = L(a.stop4, b.stop4, t);
        return r;
    }
}

// Varyantin Normal uzerine BINDIRDIGI alan gruplari (CSS ":active { background: ... }" hissi: yalniz
// yazdigin degisir, gerisi Normal'den miras). None = varyant yok (Normal). Normal'de yok sayilir.
[Flags]
public enum BoxLookFields
{
    None = 0,
    Fill = 1 << 0,
    Border = 1 << 1,        // border genislikleri
    BorderFill = 1 << 2,
    Radius = 1 << 3,
    Shadow = 1 << 4,        // shadowColor/Offset/Blur/Grow
    InnerShadow = 1 << 5,   // innerShadowColor/Blur
    Light = 1 << 6,         // lightColor/Width/Height/Offset
    TextFill = 1 << 7,
    TextOutline = 1 << 8,   // textOutlineWidth + textOutline
    TextShadow = 1 << 9,    // textShadowColor/Offset/Blur
    Transform = 1 << 10,    // scale + rotation
    Sprite = 1 << 11,       // sprite + spriteFit + slice9 (adim)
}

// Bir durumun gorunumu: isimli key + lerp'lenen BoxLook + adim alanlari (sprite/fit/slice). Varyantta `overrides`
// maskesi hangi gruplarin tabani ezdigini soyler; Normal (BoxStyle.normal) tam set, key'i yok.
[Serializable]
public struct BoxStyleState
{
    public string key;
    public BoxLookFields overrides;
    public BoxLook look;
    public Sprite sprite;
    public SpriteFit spriteFit;
    public Vec4 slice9;
}

// Paylasilan kutu stili ([Asset] -> .asset dosyasi, LayoutBox.style referansi). Bir kez yaz, N kutu
// kullansin; asset degisince tum kutular canli yenilenir (AssetDatabase.ObjectVersion). Durum
// degisiminde `transition` sn boyunca `transitionEase` ile BoxLook lerp'i (LayoutSystem tick'i).
// Kutunun kendi gorunum alanlari stil varken SURULUR (uzerine kopyalanir; Unity Selectable gibi).
// v3: sabit enum yerine `variants` listesi (isimli durumlar). Key hash indeksi ObjectVersion'da bir kez kurulur.
[Serializable]
[CreateAssetMenu(MenuName = "UI/Box Style", FileName = "BoxStyle")]
public sealed class BoxStyle
{
    // Sozlesmeli adlar (Button vb. surucular bunlari kullanir; stil yazari ayni key'i verir).
    public const string PressedKey = "pressed", DisabledKey = "disabled", HoverKey = "hover", SelectedKey = "selected";
    public static readonly int Pressed = Hash(PressedKey), Disabled = Hash(DisabledKey), Hover = Hash(HoverKey), Selected = Hash(SelectedKey);

    public BoxStyleState normal;   // overrides yok sayilir: her zaman tam temel
    public List<BoxStyleState> variants = new();
    public float transition = 0.15f;      // sn; 0 = ani
    public Ease transitionEase = Ease.OutCubic;

    // Key -> int (FNV-1a, ClipHash ile ayni); bos/null = 0 (hicbir varyantla eslesmez).
    public static int Hash(string key) => ClipHash.Of(key);

    int[] _hashes;          // variants[i].key hash'i (indeks ObjectVersion'a bagli; alloc yalniz yeniden kurulumda)
    int _hashVersion = -1;

    void EnsureIndex()
    {
        int n = variants.Count;
        if (_hashes != null && _hashes.Length == n && _hashVersion == AssetDatabase.ObjectVersion)
            return;
        if (_hashes == null || _hashes.Length != n)
            _hashes = new int[n];
        for (int i = 0; i < n; i++)
            _hashes[i] = Hash(variants[i].key);
        _hashVersion = AssetDatabase.ObjectVersion;
    }

    public int IndexOf(int hash)
    {
        EnsureIndex();
        for (int i = 0; i < _hashes.Length; i++)
            if (_hashes[i] == hash)
                return i;
        return -1;
    }

    public bool Has(string key) => IndexOf(Hash(key)) >= 0;

    // Durum kumesinin cozulmus gorunumu: Normal + eslesen varyantlarin maskeli gruplari LISTE SIRASIYLA
    // (sonraki oncekini ezer; pressed+selected gibi bilesimler boyle tanimlanir). Yalniz durum degisiminde
    // (ve stil yeniden uygulandiginda) kosar; alloc yok. scale (0,0) -> (1,1).
    public void Resolve(in StateSet states, out BoxStyleState r)
    {
        r = normal;
        r.key = null;
        if (states.Count > 0)
        {
            EnsureIndex();
            for (int i = 0; i < _hashes.Length; i++)
                if (states.Contains(_hashes[i]))
                    Overlay(ref r, variants[i]);
        }
        if (r.look.scale.x == 0f && r.look.scale.y == 0f)
            r.look.scale = BoxLook.UnitScale;
    }

    // Tek durum kisayolu (testler/basit suruculer).
    public void Resolve(int state, out BoxStyleState r)
    {
        var s = new StateSet();
        s.Add(state);
        Resolve(in s, out r);
    }

    static void Overlay(ref BoxStyleState r, in BoxStyleState v)
    {
        var m = v.overrides;
        if (m == BoxLookFields.None)
            return;
        ref var L = ref r.look;
        ref readonly var V = ref v.look;
        if ((m & BoxLookFields.Fill) != 0) L.fill = V.fill;
        if ((m & BoxLookFields.Border) != 0) L.border = V.border;
        if ((m & BoxLookFields.BorderFill) != 0) L.borderFill = V.borderFill;
        if ((m & BoxLookFields.Radius) != 0) L.radius = V.radius;
        if ((m & BoxLookFields.Shadow) != 0)
        { L.shadowColor = V.shadowColor; L.shadowOffset = V.shadowOffset; L.shadowBlur = V.shadowBlur; L.shadowGrow = V.shadowGrow; }
        if ((m & BoxLookFields.InnerShadow) != 0)
        { L.innerShadowColor = V.innerShadowColor; L.innerShadowBlur = V.innerShadowBlur; }
        if ((m & BoxLookFields.Light) != 0)
        { L.lightColor = V.lightColor; L.lightWidth = V.lightWidth; L.lightHeight = V.lightHeight; L.lightOffset = V.lightOffset; }
        if ((m & BoxLookFields.TextFill) != 0) L.textFill = V.textFill;
        if ((m & BoxLookFields.TextOutline) != 0) { L.textOutlineWidth = V.textOutlineWidth; L.textOutline = V.textOutline; }
        if ((m & BoxLookFields.TextShadow) != 0)
        { L.textShadowColor = V.textShadowColor; L.textShadowOffset = V.textShadowOffset; L.textShadowBlur = V.textShadowBlur; }
        if ((m & BoxLookFields.Transform) != 0) { L.scale = V.scale; L.rotation = V.rotation; }
        if ((m & BoxLookFields.Sprite) != 0) { r.sprite = v.sprite; r.spriteFit = v.spriteFit; r.slice9 = v.slice9; }
    }
}
