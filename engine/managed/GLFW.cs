using System;
using System.Runtime.InteropServices;

namespace DigitoyEngine;

// GLFW 3.4 P/Invoke binding (editor host, .NET/JIT). YALNIZCA C#'tan cagrilan
// method/struct'lar bind edildi; callback/delegate marshal gerektiren yerler
// (glfwSet*Callback) KAPSAM DISI — girdi polling (getKey/getMouseButton/getCursorPos)
// ile alinir. Pencere/context handle'lari opak IntPtr'dir.
public static class GLFW
{
    // Sokol.Lib ile ayni native lib; exe yanina kopyalanir (csproj).
    const string Lib = "digitoyengine_native";

    // --- Kutuphane yasam dongusu ---

    [DllImport(Lib, EntryPoint = "glfwInit")]
    extern public static int Init();

    [DllImport(Lib, EntryPoint = "glfwTerminate")]
    extern public static void Terminate();

    [DllImport(Lib, EntryPoint = "glfwGetVersion")]
    extern public static void GetVersion(out int major, out int minor, out int rev);

    // --- Pencere ipuclari (glfwCreateWindow oncesi) ---

    [DllImport(Lib, EntryPoint = "glfwDefaultWindowHints")]
    extern public static void DefaultWindowHints();

    [DllImport(Lib, EntryPoint = "glfwWindowHint")]
    extern public static void WindowHint(int hint, int value);

    // --- Pencere olustur / yok et ---

    [DllImport(Lib, EntryPoint = "glfwCreateWindow")]
    extern public static IntPtr CreateWindow(
        int width, int height,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        IntPtr monitor, IntPtr share);

    [DllImport(Lib, EntryPoint = "glfwDestroyWindow")]
    extern public static void DestroyWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "glfwWindowShouldClose")]
    extern public static int WindowShouldClose(IntPtr window);

    [DllImport(Lib, EntryPoint = "glfwSetWindowShouldClose")]
    extern public static void SetWindowShouldClose(IntPtr window, int value);

    [DllImport(Lib, EntryPoint = "glfwSetWindowTitle")]
    extern public static void SetWindowTitle(IntPtr window,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(Lib, EntryPoint = "glfwGetWindowSize")]
    extern public static void GetWindowSize(IntPtr window, out int width, out int height);

    [DllImport(Lib, EntryPoint = "glfwSetWindowSize")]
    extern public static void SetWindowSize(IntPtr window, int width, int height);

    [DllImport(Lib, EntryPoint = "glfwGetFramebufferSize")]
    extern public static void GetFramebufferSize(IntPtr window, out int width, out int height);

    [DllImport(Lib, EntryPoint = "glfwGetWindowPos")]
    extern public static void GetWindowPos(IntPtr window, out int xpos, out int ypos);

    [DllImport(Lib, EntryPoint = "glfwSetWindowPos")]
    extern public static void SetWindowPos(IntPtr window, int xpos, int ypos);

    [DllImport(Lib, EntryPoint = "glfwShowWindow")]
    extern public static void ShowWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "glfwHideWindow")]
    extern public static void HideWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "glfwFocusWindow")]
    extern public static void FocusWindow(IntPtr window);

    [DllImport(Lib, EntryPoint = "glfwGetWindowAttrib")]
    extern public static int GetWindowAttrib(IntPtr window, int attrib);

    // --- GL context ---

    [DllImport(Lib, EntryPoint = "glfwMakeContextCurrent")]
    extern public static void MakeContextCurrent(IntPtr window);

    [DllImport(Lib, EntryPoint = "glfwGetCurrentContext")]
    extern public static IntPtr GetCurrentContext();

    [DllImport(Lib, EntryPoint = "glfwSwapBuffers")]
    extern public static void SwapBuffers(IntPtr window);

    [DllImport(Lib, EntryPoint = "glfwSwapInterval")]
    extern public static void SwapInterval(int interval);

    [DllImport(Lib, EntryPoint = "glfwGetProcAddress")]
    extern public static IntPtr GetProcAddress(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string procname);

    [DllImport(Lib, EntryPoint = "glfwExtensionSupported")]
    extern public static int ExtensionSupported(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string extension);

    // --- Olay pompalama / zaman ---

    [DllImport(Lib, EntryPoint = "glfwPollEvents")]
    extern public static void PollEvents();

    [DllImport(Lib, EntryPoint = "glfwWaitEvents")]
    extern public static void WaitEvents();

    [DllImport(Lib, EntryPoint = "glfwWaitEventsTimeout")]
    extern public static void WaitEventsTimeout(double timeout);

    [DllImport(Lib, EntryPoint = "glfwPostEmptyEvent")]
    extern public static void PostEmptyEvent();

    [DllImport(Lib, EntryPoint = "glfwGetTime")]
    extern public static double GetTime();

    [DllImport(Lib, EntryPoint = "glfwSetTime")]
    extern public static void SetTime(double time);

    // --- Girdi (polling; callback yok) ---

    // Callback kayitlari: fonksiyon POINTER'i alir ([UnmanagedCallersOnly] static
    // metod adresi). Eski callback'in pointer'ini dondurur.
    [DllImport(Lib, EntryPoint = "glfwSetMouseButtonCallback")]
    extern public static IntPtr SetMouseButtonCallback(IntPtr window, IntPtr callback);

    [DllImport(Lib, EntryPoint = "glfwSetCursorPosCallback")]
    extern public static IntPtr SetCursorPosCallback(IntPtr window, IntPtr callback);

    [DllImport(Lib, EntryPoint = "glfwSetScrollCallback")]
    extern public static IntPtr SetScrollCallback(IntPtr window, IntPtr callback);

    [DllImport(Lib, EntryPoint = "glfwSetKeyCallback")]
    extern public static IntPtr SetKeyCallback(IntPtr window, IntPtr callback);

    [DllImport(Lib, EntryPoint = "glfwSetCharCallback")]
    extern public static IntPtr SetCharCallback(IntPtr window, IntPtr callback);

    [DllImport(Lib, EntryPoint = "glfwGetKey")]
    extern public static int GetKey(IntPtr window, int key);

    [DllImport(Lib, EntryPoint = "glfwGetMouseButton")]
    extern public static int GetMouseButton(IntPtr window, int button);

    [DllImport(Lib, EntryPoint = "glfwGetCursorPos")]
    extern public static void GetCursorPos(IntPtr window, out double xpos, out double ypos);

    [DllImport(Lib, EntryPoint = "glfwSetCursorPos")]
    extern public static void SetCursorPos(IntPtr window, double xpos, double ypos);

    [DllImport(Lib, EntryPoint = "glfwGetInputMode")]
    extern public static int GetInputMode(IntPtr window, int mode);

    [DllImport(Lib, EntryPoint = "glfwSetInputMode")]
    extern public static void SetInputMode(IntPtr window, int mode, int value);

    [DllImport(Lib, EntryPoint = "glfwGetKeyName")]
    [return: MarshalAs(UnmanagedType.LPUTF8Str)]
    extern public static string GetKeyName(int key, int scancode);

    // --- Pano ---

    [DllImport(Lib, EntryPoint = "glfwSetClipboardString")]
    extern public static void SetClipboardString(IntPtr window,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    // const char* doner (GLFW sahibi, free EDILMEZ); Marshal.PtrToStringUTF8 ile okunur.
    [DllImport(Lib, EntryPoint = "glfwGetClipboardString")]
    extern public static IntPtr GetClipboardStringPtr(IntPtr window);

    // --- Cursor ---

    // shape: GLFWConst.CURSOR_* sekilleri. Donen handle GLFW sahibi (Terminate'e kadar yasar).
    [DllImport(Lib, EntryPoint = "glfwCreateStandardCursor")]
    extern public static IntPtr CreateStandardCursor(int shape);

    // cursor=IntPtr.Zero => varsayilan ok.
    [DllImport(Lib, EntryPoint = "glfwSetCursor")]
    extern public static void SetCursor(IntPtr window, IntPtr cursor);

    // --- Monitor / video modu ---

    [DllImport(Lib, EntryPoint = "glfwGetPrimaryMonitor")]
    extern public static IntPtr GetPrimaryMonitor();

    // GLFWmonitor* dizisi doner (count adet); index'lenip her biri monitor handle'i.
    // Marshal.ReadIntPtr(ptr, i*IntPtr.Size) ile okunur. GLFW sahibi, free EDILMEZ.
    [DllImport(Lib, EntryPoint = "glfwGetMonitors")]
    extern public static IntPtr GetMonitors(out int count);

    // GLFWvidmode* doner; Marshal.PtrToStructure<GLFWvidmode> ile okunur.
    [DllImport(Lib, EntryPoint = "glfwGetVideoMode")]
    extern public static IntPtr GetVideoMode(IntPtr monitor);

    // GLFWvidmode dizisi doner (count adet); ardisik GLFWvidmode struct'lari.
    [DllImport(Lib, EntryPoint = "glfwGetVideoModes")]
    extern public static IntPtr GetVideoModes(IntPtr monitor, out int count);

    [DllImport(Lib, EntryPoint = "glfwGetMonitorPos")]
    extern public static void GetMonitorPos(IntPtr monitor, out int xpos, out int ypos);

    // Kullanilabilir alan (gorev cubugu vb. haric) ekran koordinatlarinda.
    [DllImport(Lib, EntryPoint = "glfwGetMonitorWorkarea")]
    extern public static void GetMonitorWorkarea(
        IntPtr monitor, out int xpos, out int ypos, out int width, out int height);

    // Fiziksel boyut (milimetre).
    [DllImport(Lib, EntryPoint = "glfwGetMonitorPhysicalSize")]
    extern public static void GetMonitorPhysicalSize(
        IntPtr monitor, out int widthMM, out int heightMM);

    // DPI olcegi (ornek: 1.0 = 96 DPI, 2.0 = retina/HiDPI). Editor UI olceklemesi icin.
    [DllImport(Lib, EntryPoint = "glfwGetMonitorContentScale")]
    extern public static void GetMonitorContentScale(
        IntPtr monitor, out float xscale, out float yscale);

    [DllImport(Lib, EntryPoint = "glfwGetMonitorName")]
    [return: MarshalAs(UnmanagedType.LPUTF8Str)]
    extern public static string GetMonitorName(IntPtr monitor);

    // Pencerenin bulundugu monitorun DPI olcegi (pencere basina HiDPI).
    [DllImport(Lib, EntryPoint = "glfwGetWindowContentScale")]
    extern public static void GetWindowContentScale(
        IntPtr window, out float xscale, out float yscale);
}

// glfwGetVideoMode'un dondurdugu struct (salt-okunur, PtrToStructure ile).
[StructLayout(LayoutKind.Sequential)]
public struct GLFWvidmode
{
    public int Width;
    public int Height;
    public int RedBits;
    public int GreenBits;
    public int BlueBits;
    public int RefreshRate;
}

// GLFW sabitleri (glfw3.h). Yalnizca yaygin kullanilanlar.
public static class GLFWConst
{
    public const int TRUE = 1;
    public const int FALSE = 0;

    // Girdi aksiyonlari
    public const int RELEASE = 0;
    public const int PRESS = 1;
    public const int REPEAT = 2;

    // Pencere ipuclari
    public const int RESIZABLE = 0x00020003;
    public const int VISIBLE = 0x00020004;
    public const int DECORATED = 0x00020005;
    public const int FOCUSED = 0x00020001;
    public const int FLOATING = 0x00020007;
    public const int MAXIMIZED = 0x00020008;
    public const int FOCUS_ON_SHOW = 0x0002000C;
    public const int MOUSE_PASSTHROUGH = 0x0002000D;
    public const int SCALE_TO_MONITOR = 0x0002200C;
    public const int RED_BITS = 0x00021001;
    public const int GREEN_BITS = 0x00021002;
    public const int BLUE_BITS = 0x00021003;
    public const int ALPHA_BITS = 0x00021004;
    public const int DEPTH_BITS = 0x00021005;
    public const int STENCIL_BITS = 0x00021006;
    public const int SAMPLES = 0x0002100D;
    public const int DOUBLEBUFFER = 0x00021010;
    public const int REFRESH_RATE = 0x0002100F;

    // Context / OpenGL ipuclari
    public const int CLIENT_API = 0x00022001;
    public const int CONTEXT_VERSION_MAJOR = 0x00022002;
    public const int CONTEXT_VERSION_MINOR = 0x00022003;
    public const int OPENGL_FORWARD_COMPAT = 0x00022006;
    public const int OPENGL_DEBUG_CONTEXT = 0x00022007;
    public const int OPENGL_PROFILE = 0x00022008;

    // CLIENT_API degerleri
    public const int NO_API = 0;
    public const int OPENGL_API = 0x00030001;
    public const int OPENGL_ES_API = 0x00030002;

    // OPENGL_PROFILE degerleri
    public const int OPENGL_ANY_PROFILE = 0;
    public const int OPENGL_CORE_PROFILE = 0x00032001;
    public const int OPENGL_COMPAT_PROFILE = 0x00032002;

    // Girdi modlari (glfwSetInputMode)
    public const int CURSOR = 0x00033001;
    public const int STICKY_KEYS = 0x00033002;
    public const int STICKY_MOUSE_BUTTONS = 0x00033003;
    public const int RAW_MOUSE_MOTION = 0x00033005;

    // CURSOR degerleri
    public const int CURSOR_NORMAL = 0x00034001;
    public const int CURSOR_HIDDEN = 0x00034002;
    public const int CURSOR_DISABLED = 0x00034003;

    // Standart cursor sekilleri (glfwCreateStandardCursor)
    public const int ARROW_CURSOR = 0x00036001;
    public const int IBEAM_CURSOR = 0x00036002;
    public const int CROSSHAIR_CURSOR = 0x00036003;
    public const int POINTING_HAND_CURSOR = 0x00036004;
    public const int RESIZE_EW_CURSOR = 0x00036005;
    public const int RESIZE_NS_CURSOR = 0x00036006;
    public const int RESIZE_ALL_CURSOR = 0x00036009;

    // Fare dugmeleri
    public const int MOUSE_BUTTON_LEFT = 0;
    public const int MOUSE_BUTTON_RIGHT = 1;
    public const int MOUSE_BUTTON_MIDDLE = 2;

    // Sik kullanilan tus kodlari
    public const int KEY_SPACE = 32;
    public const int KEY_APOSTROPHE = 39;
    public const int KEY_COMMA = 44;
    public const int KEY_MINUS = 45;
    public const int KEY_PERIOD = 46;
    public const int KEY_SLASH = 47;
    public const int KEY_0 = 48;
    public const int KEY_1 = 49;
    public const int KEY_2 = 50;
    public const int KEY_3 = 51;
    public const int KEY_4 = 52;
    public const int KEY_5 = 53;
    public const int KEY_6 = 54;
    public const int KEY_7 = 55;
    public const int KEY_8 = 56;
    public const int KEY_9 = 57;
    public const int KEY_A = 65;
    public const int KEY_B = 66;
    public const int KEY_C = 67;
    public const int KEY_D = 68;
    public const int KEY_E = 69;
    public const int KEY_F = 70;
    public const int KEY_G = 71;
    public const int KEY_H = 72;
    public const int KEY_I = 73;
    public const int KEY_J = 74;
    public const int KEY_K = 75;
    public const int KEY_L = 76;
    public const int KEY_M = 77;
    public const int KEY_N = 78;
    public const int KEY_O = 79;
    public const int KEY_P = 80;
    public const int KEY_Q = 81;
    public const int KEY_R = 82;
    public const int KEY_S = 83;
    public const int KEY_T = 84;
    public const int KEY_U = 85;
    public const int KEY_V = 86;
    public const int KEY_W = 87;
    public const int KEY_X = 88;
    public const int KEY_Y = 89;
    public const int KEY_Z = 90;
    public const int KEY_ESCAPE = 256;
    public const int KEY_ENTER = 257;
    public const int KEY_TAB = 258;
    public const int KEY_BACKSPACE = 259;
    public const int KEY_INSERT = 260;
    public const int KEY_DELETE = 261;
    public const int KEY_RIGHT = 262;
    public const int KEY_LEFT = 263;
    public const int KEY_DOWN = 264;
    public const int KEY_UP = 265;
    public const int KEY_PAGE_UP = 266;
    public const int KEY_PAGE_DOWN = 267;
    public const int KEY_HOME = 268;
    public const int KEY_END = 269;
    public const int KEY_F1 = 290;
    public const int KEY_F2 = 291;
    public const int KEY_F3 = 292;
    public const int KEY_F4 = 293;
    public const int KEY_F5 = 294;
    public const int KEY_F6 = 295;
    public const int KEY_F7 = 296;
    public const int KEY_F8 = 297;
    public const int KEY_F9 = 298;
    public const int KEY_F10 = 299;
    public const int KEY_F11 = 300;
    public const int KEY_F12 = 301;
    public const int KEY_LEFT_SHIFT = 340;
    public const int KEY_LEFT_CONTROL = 341;
    public const int KEY_LEFT_ALT = 342;
    public const int KEY_LEFT_SUPER = 343;
    public const int KEY_RIGHT_SHIFT = 344;
    public const int KEY_RIGHT_CONTROL = 345;
    public const int KEY_RIGHT_ALT = 346;
}