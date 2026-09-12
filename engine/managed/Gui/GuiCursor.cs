using System;

namespace DigitoyEngine;

public enum GuiCursor : byte
{
    Arrow = 0,
    IBeam,
    ResizeH,   // yatay boyutlandirma (dikey splitter cizgisi)
    ResizeV,   // dikey boyutlandirma (yatay splitter cizgisi)
    Hand,
    ResizeAll,
}

// Unity EditorGUIUtility.AddCursorRect muadili: widget'lar pass sirasinda cursor
// ISTER (genelde Repaint'te hover/hot kontroluyle), GuiHost frame sonunda kazanan
// istegi pencereye uygular. Son istek kazanir — sonra cizilen ustte oldugundan
// dogal z-order onceligi verir. Istek yoksa varsayilan ok.
public static class GuiCursorManager
{
    static readonly IntPtr[] _cursors = new IntPtr[6];
    static GuiCursor _requested;

    public static void Request(GuiCursor cursor) => _requested = cursor;

    internal static void BeginFrame() => _requested = GuiCursor.Arrow;

    internal static void Apply(IntPtr window)
    {
        GLFW.SetCursor(window, Get(_requested));
    }

    static IntPtr Get(GuiCursor c)
    {
        if (c == GuiCursor.Arrow)
            return IntPtr.Zero; // varsayilan ok
        int i = (int)c;
        if (_cursors[i] == IntPtr.Zero)
        {
            int shape = c switch
            {
                GuiCursor.IBeam => GLFWConst.IBEAM_CURSOR,
                GuiCursor.ResizeH => GLFWConst.RESIZE_EW_CURSOR,
                GuiCursor.ResizeV => GLFWConst.RESIZE_NS_CURSOR,
                GuiCursor.Hand => GLFWConst.POINTING_HAND_CURSOR,
                GuiCursor.ResizeAll => GLFWConst.RESIZE_ALL_CURSOR,
                _ => GLFWConst.ARROW_CURSOR,
            };
            _cursors[i] = GLFW.CreateStandardCursor(shape);
        }
        return _cursors[i];
    }
}
