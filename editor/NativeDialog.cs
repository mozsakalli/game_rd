using System;
using System.Runtime.InteropServices;

namespace DigitoyEditor;

// Native dosya/klasor diyaloglari (Windows IFileDialog / macOS NSOpenPanel-NSSavePanel), modal + senkron.
// Filtre formati: "Scene|*.scene|All files|*.*" ('|' ile ad/desen ciftleri; desenler ';' ile coklanir).
public static class NativeDialog
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib, EntryPoint = "de_dialog_file")]
    static extern unsafe int DialogFile(IntPtr glfwWindow, int mode,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filters,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string defaultPath,
        byte* outPath, int cap);

    public static string OpenFile(string title, string filters = null, string defaultDir = null)
        => Show(0, title, filters, defaultDir);

    // defaultPath: baslangic klasoru ya da onerilen dosya yolu/adi.
    public static string SaveFile(string title, string filters = null, string defaultPath = null)
        => Show(1, title, filters, defaultPath);

    public static string PickFolder(string title, string defaultDir = null)
        => Show(2, title, null, defaultDir);

    // null = iptal.
    static unsafe string Show(int mode, string title, string filters, string defaultPath)
    {
        var buf = new byte[4096];
        int ok;
        fixed (byte* p = buf)
            ok = DialogFile(EditorMenu.MainWindow, mode, title, filters, defaultPath, p, buf.Length);
        if (ok == 0)
            return null;
        int n = Array.IndexOf(buf, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buf, 0, n < 0 ? buf.Length : n);
    }
}
