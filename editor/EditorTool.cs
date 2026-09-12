using System;
using System.Collections.Generic;
using System.Reflection;
using DigitoyEngine;

namespace DigitoyEditor;

// Unity EditorTool modeli: Move/Rotate/Scale dahil TUM araclar bu siniftan
// turer ve [EditorTool] taramasiyla bulunur — SceneView arac bilmez.
[AttributeUsage(AttributeTargets.Class)]
public sealed class EditorToolAttribute : Attribute
{
    public string Name;
    public int Order = 1000;   // toolbar sirasi (builtin'ler 0-2)
    public int Key;            // GLFW keycode ('W' gibi buyuk harf ASCII); 0 = kisayol yok

    public EditorToolAttribute(string name) => Name = name;
}

public abstract class EditorTool
{
    public string Name { get; internal set; }
    public int Key { get; internal set; }
    internal int Order;

    // Her SceneView pass'inde cagrilir (Layout dahil). Kontrol id'leri KOSULSUZ
    // alinmali (secim yokken de) — pass'ler arasi kontrol sayisi invariant'i.
    public abstract void OnToolGui(SceneViewPanel view, Event ev);
}

public static class EditorTools
{
    static readonly List<EditorTool> _tools = new();
    static EditorTool _active;

    public static IReadOnlyList<EditorTool> All => _tools;

    public static EditorTool Active
    {
        get => _active ??= _tools.Count > 0 ? _tools[0] : null;
        set => _active = value;
    }

    // [EditorTool] tipleri taranir; aktif arac adiyla korunur (kod reload'da
    // eski assembly instance'i tutulmaz).
    public static void Rebuild(params Assembly[] asms)
    {
        string activeName = _active?.Name;
        _tools.Clear();
        _active = null;
        foreach (var asm in asms)
        {
            if (asm == null)
                continue;
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || !typeof(EditorTool).IsAssignableFrom(t))
                    continue;
                var attr = t.GetCustomAttribute<EditorToolAttribute>();
                if (attr == null)
                    continue;
                var tool = (EditorTool)Activator.CreateInstance(t, nonPublic: true);
                tool.Name = attr.Name ?? t.Name;
                tool.Key = attr.Key;
                tool.Order = attr.Order;
                _tools.Add(tool);
            }
        }
        _tools.Sort((a, b) =>
        {
            int c = a.Order.CompareTo(b.Order);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        if (activeName != null)
            _active = _tools.Find(t => t.Name == activeName);
    }
}
