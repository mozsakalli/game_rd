using System;
using System.Collections.Generic;
using System.Reflection;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// EDITOR UZANTI NOKTASI (Unity CustomPropertyDrawer modeli): proje/plugin Editor/ kodu (<Ad>.Editor.dll, DigitoyEditor.dll'e
// referans verir) bir [Serializable] tipin BELLI BIR ALANININ cizimini devralir — ObjectDrawer o alani cizmeden once kayda bakar.
// Ornek: AppLovin Config.mediation listesi elle yazilmaz, katalogdan secilir (Assets/Plugins/AppLovin/Editor/AppLovinMediationDrawer.cs).
// Motor bu dosyadan habersiz; attribute Editor kodunda, Runtime Config'e dokunulmaz. Kayit Editor.dll yuklenince (GameCode),
// reload'da temizlenir. Instance (tip, alan) basina TEK: ayni alan iki nesnede (iki oyunun Config'i) ciziliyorsa durum ctx.Path ile ayrilir.
[AttributeUsage(AttributeTargets.Class)]
public sealed class CustomFieldDrawerAttribute : Attribute
{
    public readonly Type OwnerType;
    public readonly string Field;
    public CustomFieldDrawerAttribute(Type ownerType, string field) { OwnerType = ownerType; Field = field; }
}

public sealed class FieldDrawerContext
{
    public object Owner;
    public SerializedType.FieldSchema Field;
    public string Path;      // benzersiz UI anahtari (foldout/metin tamponu icin)
    public float LabelW;
    public int Indent;
    public string ProjectRoot;
    public string LibraryPath; // onbellek icin (git'e girmez)
    public object Value => Field.Get(Owner);
    public void SetValue(object v) => Field.Set(Owner, v);
    public float LabelX => 12 + Indent * 12;
    public float ValueX => 12 + LabelW;
}

public abstract class FieldDrawer
{
    public const float RowH = InspectorPanel.RowH;
    // Toplam yukseklik (Draw ile birebir; scroll icerigi buna gore).
    public abstract float Measure(FieldDrawerContext c);
    // y'den asagi cizer; deger degistiyse true (sahip dosyaya yazar).
    public abstract bool Draw(FieldDrawerContext c, ref float y, float right);
}

// Uzanti yazarlarinin kullandigi gorsel dil (Inspector ile ayni; ic uyeler internal kaldigi icin burada public yuz).
public static class EditorUi
{
    public static float RowH => InspectorPanel.RowH;
    public static float LabelFont => InspectorPanel.LabelFont;
    public static float SmallFont => InspectorPanel.SmallFont;
    public static Color LabelColor => InspectorPanel.LabelColor;
    public static Color DimColor => InspectorPanel.LabelDimColor;
    public static Color WarnColor => ObjectDrawer.HighlightColor;
    public static Color ErrorColor => InspectorPanel.MissingRefColor;
    public static bool Repaint => Event.Current.Type == EventType.Repaint;

    // Alan etiketi (Nicify: camelCase -> "Camel Case").
    public static void Label(in Rect rect, string text) => InspectorPanel.DrawFieldLabel(rect, text);

    public static void Text(in Rect rect, string text, bool small = false, Color? color = null)
    {
        if (Repaint)
            GuiRenderer.DrawTextIn(rect, text ?? "", small ? SmallFont : LabelFont, color ?? LabelColor, false, 2);
    }

    public static void Log(string msg) => EditorLog.Info(msg);
    public static void Warn(string msg) => EditorLog.Warning(msg);
    public static void Error(string msg) => EditorLog.Error(msg);
}

public static class FieldDrawers
{
    static readonly Dictionary<(Type, string), FieldDrawer> _byField = new();

    public static void Clear() => _byField.Clear();

    // Editor.dll'deki [CustomFieldDrawer] siniflari (parametresiz ctor). Hata = Console; digerleri etkilenmez.
    public static int Register(Assembly asm)
    {
        if (asm == null) return 0;
        int n = 0;
        Type[] types;
        try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
        foreach (var t in types)
        {
            if (t == null || t.IsAbstract || !typeof(FieldDrawer).IsAssignableFrom(t)) continue;
            foreach (var a in t.GetCustomAttributes<CustomFieldDrawerAttribute>())
            {
                try
                {
                    _byField[(a.OwnerType, a.Field)] = (FieldDrawer)Activator.CreateInstance(t);
                    n++;
                }
                catch (Exception e) { EditorLog.Error("[drawer] " + t.Name + " kurulamadi: " + e.Message); }
            }
        }
        return n;
    }

    // Sahip tipi (ve tabanlari) + alan adi icin kayitli cizici; yoksa null.
    public static FieldDrawer Find(Type ownerType, string field)
    {
        if (_byField.Count == 0 || ownerType == null) return null;
        for (var t = ownerType; t != null && t != typeof(object); t = t.BaseType)
            if (_byField.TryGetValue((t, field), out var d)) return d;
        return null;
    }
}
