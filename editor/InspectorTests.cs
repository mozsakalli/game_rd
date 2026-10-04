#if DE_EDITOR
using System;
using System.Reflection;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

public static class InspectorTests
{
    public static void Run()
    {
        int passed = 0, failed = 0;
        void Check(bool condition, string name)
        {
            if (condition) passed++;
            else { failed++; Console.WriteLine("[inspector] FAIL: " + name); }
        }

        var inspector = new InspectorPanel();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var drawDoc = typeof(InspectorPanel).GetMethod("DrawDocValue", flags);
        var drawAsset = typeof(InspectorPanel).GetMethod("DrawAssetValue", flags);
        var measure = typeof(InspectorPanel).GetMethod("MeasureDocValue", flags);
        var node = DocNode.Scal("1 2 3 4");
        Vec4 assetValue = new(1, 2, 3, 4);
        int previousHot = GuiUtility.HotControl;
        int previousKeyboard = GuiUtility.KeyboardControl;
        var previousEvent = Event.Current;
        try
        {
            Check((float)measure.Invoke(inspector, new object[]
            {
                SerializedType.Kind.Vec4, default(SerializedType.Kind), null, node, "vec4-test",
            }) == 22f, "Vec4 doc tek satir yuksekligi");
            var ev = new Event();
            var screen = new Rect(0, 0, 500, 500);
            for (int target = 0; target < 2; target++)
            {
                for (int axis = 0; axis < 4; axis++)
                {
                    GuiUtility.HotControl = GuiUtility.KeyboardControl = 0;
                    float x = 104 + 75 * axis + 6;
                    foreach (var type in new[] { EventType.Layout, EventType.MouseDown,
                        EventType.MouseDrag, EventType.MouseUp })
                    {
                        ev.Set(type, new Vec2(x + (type == EventType.MouseDrag ? 40 : 0), 9),
                            default, 0, 1, 0, '\0', EventModifiers.None);
                        GuiUtility.BeginPass(ev, screen);
                        object[] args;
                        if (target == 0)
                        {
                            args = new object[] { "quad", SerializedType.Kind.Vec4, typeof(Vec4),
                                default(SerializedType.Kind), null, null, node, "vec4-doc", 0,
                                0f, 400f, 0f, (Action)(() => { }), false };
                            drawDoc.Invoke(inspector, args);
                            Check((float)args[9] == 22f, "doc Vec4 input pass tek satir");
                        }
                        else
                        {
                            args = new object[] { "quad", SerializedType.Kind.Vec4, typeof(Vec4),
                                null, null, null, assetValue, (Action<object>)(v => assetValue = (Vec4)v),
                                "vec4-asset", 0, 0f, 400f, 0f, false, (Action)(() => { }) };
                            drawAsset.Invoke(inspector, args);
                            Check((float)args[10] == 22f, "asset Vec4 input pass tek satir");
                        }
                    }
                    Vec4 value = target == 0 ? SerializedType.ParseVec4(node.Scalar) : assetValue;
                    Check(value.x == 3
                        && value.y == 2 + (axis >= 1 ? 2 : 0)
                        && value.z == 3 + (axis >= 2 ? 2 : 0)
                        && value.w == 4 + (axis >= 3 ? 2 : 0),
                        (target == 0 ? "doc" : "asset") + " Vec4 eksen " + axis + " bagimsiz edit");
                }
            }
        }
        finally
        {
            Gui.CancelNumericEdit();
            GuiUtility.HotControl = previousHot;
            GuiUtility.KeyboardControl = previousKeyboard;
            Event.Current = previousEvent;
        }
        Console.WriteLine($"[inspector] {passed} PASS, {failed} FAIL");
    }
}
#endif
