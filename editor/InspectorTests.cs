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
        var drawer = new ObjectDrawer();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var drawDoc = typeof(InspectorPanel).GetMethod("DrawDocValue", flags);
        var drawAsset = typeof(ObjectDrawer).GetMethod("DrawValue", flags);
        var measure = typeof(InspectorPanel).GetMethod("MeasureDocValue", flags);
        float labelW = (float)typeof(InspectorPanel).GetField("LabelW", flags).GetValue(inspector);
        drawer.LabelW = labelW;
        // Vec4Drags yerlesimi: valueX = 12 + LabelW, hucre = (genislik - 12) / 4, adim = hucre + 4.
        float valueX = 12 + labelW;
        float axisStride = (400f - valueX - 12) / 4 + 4;
        var node = DocNode.Scal("1 2 3 4");
        Vec4 assetValue = new(1, 2, 3, 4);
        int previousHot = GuiUtility.HotControl;
        int previousKeyboard = GuiUtility.KeyboardControl;
        var previousEvent = Event.Current;
        try
        {
            var nicify = typeof(InspectorPanel).GetMethod("Nicify",
                BindingFlags.Static | BindingFlags.NonPublic);
            string Nice(string s) => (string)nicify.Invoke(null, new object[] { s });
            Check(Nice("anchorMin") == "Anchor Min", "Nicify camelCase");
            Check(Nice("UIButton") == "UI Button", "Nicify kisaltma");
            Check(Nice("m_Name") == "Name", "Nicify m_ on eki");
            Check(Nice("border_fill") == "Border Fill", "Nicify snake_case");
            Check(Nice("WatchAndEarnButton") == "Watch And Earn Button", "Nicify PascalCase");
            Check(Nice("width") == "Width", "Nicify tek kelime");

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
                    float x = valueX + axisStride * axis + 6;
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
                            drawAsset.Invoke(drawer, args);
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
