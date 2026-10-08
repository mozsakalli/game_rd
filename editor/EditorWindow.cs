using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Unity EditorWindow modeli: pencere kendini tanitir (Title + OnGui), editor
// cekirdegi yalnizca havuzu yonetir — hangi pencerenin ne cizdigini bilmez.
// Eklentiler EditorWindow tureterek + [MenuItem] ile menuden acilarak katilir.
public abstract class EditorWindow
{
    public string Title { get; protected set; } = "Window";
    public int PanelId { get; private set; } = -1;

    // Unity "utility window" karsiligi: dock'lanamaz, her zaman kendi native penceresinde acilir
    // (ana pencerenin ortasinda), tab bar'i yoktur, layout'a kaydedilmez. Ctor'da ayarlanir.
    protected bool Utility { get; set; }
    protected int UtilityWidth { get; set; } = 520;   // mantiksal px
    protected int UtilityHeight { get; set; } = 640;

    // Her GUI pass'inde cagrilir (Layout/Repaint/input — Event.Current ayirt eder).
    protected virtual void OnGui() { }

    void DrawPanel(int panelId) => OnGui();

    void Show()
    {
        if (Utility)
            GuiDock.OpenUtility(PanelId, UtilityWidth, UtilityHeight);
        else
            GuiDock.FocusPanel(PanelId); // layout kuruluysa ilk leaf'e dock'lanir
    }

    // Tip basina tek instance (Unity GetWindow varsayilani). Yoksa yaratip dock
    // havuzuna kaydeder, varsa tab'ini one getirir (utility: penceresini acar/one getirir).
    public static T GetWindow<T>() where T : EditorWindow, new()
        => (T)GetWindow(typeof(T));

    // Menu gibi tip'i runtime'da bilen cagiranlar icin (parametresiz ctor sart).
    public static EditorWindow GetWindow(Type type)
    {
        if (_open.TryGetValue(type, out var w))
        {
            w.Show();
            return w;
        }
        var win = (EditorWindow)Activator.CreateInstance(type);
        win.PanelId = GuiDock.RegisterPanel(win.Title, win.DrawPanel, win.Utility);
        _open[type] = win;
        win.Show();
        return win;
    }

    // Ayni tipten YENI pencere acar (coklu viewport senaryosu) — tip-tekil havuza girmez.
    public static T CreateWindow<T>() where T : EditorWindow, new()
    {
        var win = new T();
        win.PanelId = GuiDock.RegisterPanel(win.Title, win.DrawPanel, win.Utility);
        win.Show();
        return win;
    }

    static readonly Dictionary<Type, EditorWindow> _open = new();
}
