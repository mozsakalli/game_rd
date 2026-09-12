using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DigitoyEditor;

// Unity [MenuItem("Dosya/Kaydet")] karsiligi: static parametresiz metodu ana
// pencerenin NATIVE menu barina baglar. Yol '/' ile ic ice menu kurar.
// EditorWindow SINIFINA konursa item o pencereyi acar (GetWindow) — panel
// basina ayri "ac" metodu yazmak gerekmez.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class MenuItemAttribute : Attribute
{
    public readonly string Path;
    public readonly int Order;

    public MenuItemAttribute(string path, int order = 0)
    {
        Path = path;
        Order = order;
    }
}

// Native Win32 menu bar: verilen assembly'lerdeki [MenuItem] metodlarini tarar,
// menu agacini kurar, WM_COMMAND'i frame basina Poll ile dagitir.
public static class EditorMenu
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib, EntryPoint = "de_menu_create_bar")]
    static extern IntPtr MenuCreateBar();

    [DllImport(Lib, EntryPoint = "de_menu_add_popup")]
    static extern IntPtr MenuAddPopup(IntPtr parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(Lib, EntryPoint = "de_menu_add_item")]
    static extern void MenuAddItem(IntPtr menu, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, int id);

    [DllImport(Lib, EntryPoint = "de_menu_attach")]
    static extern void MenuAttach(IntPtr glfwWindow, IntPtr bar);

    [DllImport(Lib, EntryPoint = "de_menu_poll")]
    static extern int MenuPoll();

    [DllImport(Lib, EntryPoint = "de_menu_create_popup")]
    static extern IntPtr MenuCreatePopup();

    [DllImport(Lib, EntryPoint = "de_menu_show_context")]
    static extern int MenuShowContext(IntPtr glfwWindow, IntPtr popup);

    [DllImport(Lib, EntryPoint = "de_menu_destroy")]
    static extern void MenuDestroy(IntPtr menu);

    internal static IntPtr MainWindow { get; private set; }

    // Native sag-tik menusu: '/' ile alt menu, "-" ayirici. SENKRON — secilen
    // eylem donmeden calisir. Ornek: ShowContext(("Create/Scene", ...), ("-", null)).
    public static void ShowContext(params (string Path, Action Run)[] items)
    {
        if (MainWindow == IntPtr.Zero)
            return;
        IntPtr popup = MenuCreatePopup();
        var popups = new Dictionary<string, IntPtr>();
        var actions = new List<Action>();
        foreach (var (path, run) in items)
        {
            string[] parts = path.Split('/');
            IntPtr parent = popup;
            string key = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                key = key.Length == 0 ? parts[i] : key + "/" + parts[i];
                if (!popups.TryGetValue(key, out var pop))
                {
                    pop = MenuAddPopup(parent, parts[i]);
                    popups[key] = pop;
                }
                parent = pop;
            }
            if (parts[^1] == "-")
            {
                MenuAddSeparator(parent);
                continue;
            }
            MenuAddItem(parent, parts[^1], actions.Count);
            actions.Add(run);
        }
        int picked = MenuShowContext(MainWindow, popup);
        MenuDestroy(popup);
        if (picked >= 0 && picked < actions.Count)
        {
            try { actions[picked]?.Invoke(); }
            catch (Exception e) { Console.WriteLine($"[menu] context error: {e.Message}"); }
        }
    }

    [DllImport(Lib, EntryPoint = "de_menu_add_separator")]
    static extern void MenuAddSeparator(IntPtr menu);

    struct Item
    {
        public string Path;
        public int Order;
        public Action Run;
    }

    static readonly List<Action> _actions = new();

    // [MenuItem] metodlarini toplar ve menu barini pencereye takar. Oyun kodu
    // editor eklentisi kazandiginda onun assembly'si de buraya eklenecek.
    public static void Build(IntPtr glfwWindow, params Assembly[] assemblies)
    {
        MainWindow = glfwWindow;
        var items = new List<Item>();
        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                // Sinif duzeyi: EditorWindow tureviyse item pencereyi acar.
                var cattr = type.GetCustomAttribute<MenuItemAttribute>();
                if (cattr != null && typeof(EditorWindow).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    var t = type;
                    items.Add(new Item { Path = cattr.Path, Order = cattr.Order, Run = () => EditorWindow.GetWindow(t) });
                }
                foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = m.GetCustomAttribute<MenuItemAttribute>();
                    if (attr == null || m.GetParameters().Length != 0)
                        continue;
                    var mi = m;
                    items.Add(new Item { Path = attr.Path, Order = attr.Order, Run = () => mi.Invoke(null, null) });
                }
            }
        }
        // Kok menu sirasi ilk gorulme sirasidir; ayni menu icinde Order belirler.
        items.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Path, b.Path));

        IntPtr bar = MenuCreateBar();
        var popups = new Dictionary<string, IntPtr>(); // "Dosya" / "Dosya/Alt" -> HMENU
        foreach (var it in items)
        {
            string[] parts = it.Path.Split('/');
            if (parts.Length < 2)
                continue; // kok seviyeye dogrudan item koymuyoruz (Win32 destekler ama Unity gibi degil)
            IntPtr parent = bar;
            string key = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                key = key.Length == 0 ? parts[i] : key + "/" + parts[i];
                if (!popups.TryGetValue(key, out var pop))
                {
                    pop = MenuAddPopup(parent, parts[i]);
                    popups[key] = pop;
                }
                parent = pop;
            }
            var run = it.Run;
            MenuAddItem(parent, parts[^1], _actions.Count);
            _actions.Add(run);
        }
        MenuAttach(glfwWindow, bar);
    }

    // Frame basinda cagrilir: biriken menu komutlarini calistirir.
    public static void Poll()
    {
        int id;
        while ((id = MenuPoll()) >= 0)
        {
            if (id >= _actions.Count)
                continue;
            try
            {
                _actions[id]();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[menu] hata: {e.InnerException?.Message ?? e.Message}");
            }
        }
    }
}
