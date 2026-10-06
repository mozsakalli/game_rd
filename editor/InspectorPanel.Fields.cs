using System;
using System.Collections;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

public sealed partial class InspectorPanel
{
    readonly Dictionary<string, bool> _fieldFoldouts = new();

    // Asagidaki yardimcilar ObjectDrawer ile paylasilir (ayni gorsel dil).
    internal static void DrawFieldLabel(in Rect rect, string text)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(rect, Nicify(text), LabelFont, LabelColor, false, 2);
    }

    bool Expanded(string path)
        => !_fieldFoldouts.TryGetValue(path, out bool expanded) || expanded;

    internal static bool BufferEquals(string value, char[] buffer, int length)
    {
        if (value.Length != length)
            return false;
        for (int i = 0; i < length; i++)
            if (value[i] != buffer[i])
                return false;
        return true;
    }

    internal static object DefaultElement(Type type)
    {
        if (type == typeof(string))
            return "";
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return type.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(type) : null;
    }

    static readonly int _colorHash = "Inspector.Color".GetHashCode();

    // Unity renk alani: swatch (ustte rgb, altta alfa seridi) — tiklaninca Color Picker
    // penceresi acilir; picker bu alani (key) duzenledigi surece guncel degeri dondurur.
    internal static Color DrawColor(in Rect rect, Color value, string key)
    {
        int id = GuiUtility.GetControlID(_colorHash, FocusType.Passive);
        Event ev = Event.Current;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                    ev.Use();
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                    if (rect.Contains(ev.MousePosition))
                        ColorPickerWindow.Open(key, value);
                }
                break;
            case EventType.Repaint:
                {
                    GuiRenderer.DrawRect(rect, new Color(20, 21, 26, 255), 0);
                    var body = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 7);
                    GuiRenderer.DrawRect(body, new Color(value.r, value.g, value.b, 255), 1);
                    var abg = new Rect(rect.x + 1, rect.yMax - 5, rect.width - 2, 4);
                    GuiRenderer.DrawRect(abg, Color.Black, 1);
                    GuiRenderer.DrawRect(new Rect(abg.x, abg.y, abg.width * value.a / 255f, 4),
                        Color.White, 2);
                    break;
                }
        }
        if (ColorPickerWindow.TryGet(key, out Color picked))
            return picked;
        return value;
    }

    internal static bool SameColor(Color a, Color b)
        => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    void SaveAssetNow()
    {
        if (_aIsMeta)
        {
            // Importer ayarlari: meta'ya yaz, stamp degisir -> reimport, canli font
            // yerinde tazelenir, bagimli atlas gruplari yeniden paketlenir.
            string rel = RelAssetPath(_aPath);
            if (rel == null)
                return;
            ImportPipeline.SaveSettings(rel, _aObj);
            ImportPipeline.EnsureImported(rel);
            App.Assets.InvalidateImported(rel);
            AtlasSystem.RefreshAll();
            return;
        }
        AssetWatcher.NoteSelfWrite(_aPath);
        ObjectSerializer.Save(_aObj, _aPath, App.Assets);
        if (AtlasSystem.IsAtlasGroupAsset(_aPath))
            AtlasSystem.RefreshAll(); // grup ayari degisti: hemen yeniden paketle
    }

    readonly Dictionary<string, char[]> _docStrBufs = new();
    readonly Dictionary<string, int> _docStrLens = new();
    readonly Dictionary<string, string> _docStrValues = new();

    float MeasureDocField(SerializedType.FieldSchema field, DocNode node, string path)
        => MeasureDocValue(field.Kind, field.ElementKind, field.Nested, node, path);

    float MeasureDocValue(SerializedType.Kind kind, SerializedType.Kind elementKind,
        SerializedType.FieldSchema[] nested, DocNode node, string path)
    {
        float height = RowH;
        if (!Expanded(path))
            return height;
        if (kind == SerializedType.Kind.Object && nested != null)
        {
            foreach (var field in nested)
            {
                if (!DocVisible(field, node))
                    continue;
                height += MeasureDocField(field, FindMapValue(node, field), path + "." + field.Name);
            }
        }
        else if (kind == SerializedType.Kind.List && node?.Items != null)
        {
            for (int i = 0; i < node.Items.Count; i++)
                height += MeasureDocValue(elementKind, default, nested, node.Items[i],
                    path + "[" + i + "]");
        }
        return height;
    }

    void DrawDocRootField(EditorScene scene, SceneDoc.GoDoc go, SceneDoc.CompDoc component,
        SerializedType.FieldSchema field, DocNode current, ref float y, float right)
    {
        int componentIndex = go.Components.IndexOf(component);
        string path = "c:" + go.Id + ":" + componentIndex + ":" + field.Name;
        DocNode edited = (current ?? CreateDefaultFieldNode(field)).Clone();
        bool changed = false;
        Action deferredCommit = () => scene.SetProp(go, component, field, current, edited);
        DrawDocValue(field.Name, field.Kind, field.FieldType, field.ElementKind,
            field.ElementType, field.Nested, edited, path, 0, ref y, right, 0,
            deferredCommit, ref changed);
        if (changed)
            scene.SetProp(go, component, field, current, edited);
    }

    void DrawDocValue(string label, SerializedType.Kind kind, Type valueType,
        SerializedType.Kind elementKind, Type elementType, SerializedType.FieldSchema[] nested,
        DocNode node, string path, int indent, ref float y, float right, float rightReserve,
        Action deferredCommit, ref bool changed)
    {
        if (kind == SerializedType.Kind.List)
        {
            DrawDocList(label, elementKind, elementType, nested, node, path, indent,
                ref y, right, rightReserve, deferredCommit, ref changed);
            return;
        }
        if (kind == SerializedType.Kind.Object)
        {
            DrawDocObject(label, nested, node, path, indent, ref y, right,
                rightReserve, deferredCommit, ref changed);
            return;
        }

        float labelX = 12 + indent * 12;
        var labelRect = new Rect(labelX, y, Math.Max(20, LabelW - indent * 12), 18);
        float valueX = 12 + LabelW;
        var valueRect = new Rect(valueX, y, Math.Max(20, right - valueX - rightReserve), 18);
        DrawFieldLabel(labelRect, label);
        string scalar = node.Scalar ?? "";

        switch (kind)
        {
            case SerializedType.Kind.Float:
                {
                    float current = SafeFloat(scalar);
                    float next = Gui.DragZone(labelRect, current, 0.02f); // etiket = drag tutamaci
                    next = Gui.DragFloat(valueRect, next, 0.02f);
                    if (next != current)
                    {
                        node.Scalar = next.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                        changed = true;
                    }
                    break;
                }
            case SerializedType.Kind.Int:
                {
                    int current = int.TryParse(scalar, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
                    int next = (int)MathF.Round(Gui.DragZone(labelRect, current, 0.05f));
                    next = Gui.DragInt(valueRect, next, 0.05f);
                    if (next != current)
                    {
                        node.Scalar = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        changed = true;
                    }
                    break;
                }
            case SerializedType.Kind.Bool:
                {
                    bool current = scalar == "true";
                    bool next = Gui.Toggle(new Rect(valueRect.x, y, 18, 18), current);
                    if (next != current) { node.Scalar = next ? "true" : "false"; changed = true; }
                    break;
                }
            case SerializedType.Kind.String:
                DrawDocString(valueRect, scalar, node, path, ref changed);
                break;
            case SerializedType.Kind.Enum:
                {
                    string[] names = Enum.GetNames(valueType);
                    int index = Array.IndexOf(names, scalar);
                    int next = Gui.ComboBox(valueRect, index, names);
                    if (next != index && next >= 0)
                    {
                        node.Scalar = names[next];
                        changed = true;
                    }
                    break;
                }
            case SerializedType.Kind.Vec2:
            case SerializedType.Kind.Vec3:
                {
                    bool three = kind == SerializedType.Kind.Vec3;
                    Vec3 current = SerializedType.ParseVec3(scalar.Length > 0 ? scalar : "0 0 0");
                    Vec3 next = Vec3Drags(valueRect, current, three);
                    if (!Same(next, current)) { node.Scalar = V3(next, three); changed = true; }
                    break;
                }
            case SerializedType.Kind.Vec4:
                {
                    Vec4 current = SerializedType.ParseVec4(scalar);
                    Vec4 next = Vec4Drags(valueRect, current);
                    if (!Same(next, current))
                    {
                        node.Scalar = SerializedType.Format(next, kind);
                        changed = true;
                    }
                    break;
                }
            case SerializedType.Kind.Color:
                {
                    Color current = (Color)SerializedType.Parse(scalar, kind, typeof(Color), App.Assets);
                    Color next = DrawColor(valueRect, current, path);
                    if (!SameColor(current, next))
                    {
                        node.Scalar = SerializedType.Format(next, kind);
                        changed = true;
                    }
                    break;
                }
            case SerializedType.Kind.Asset:
            case SerializedType.Kind.GoRef:
            case SerializedType.Kind.CompRef:
                DrawDocReference(valueRect, kind, valueType, node, deferredCommit);
                break;
        }
        y += RowH;
    }

    void DrawDocObject(string label, SerializedType.FieldSchema[] nested, DocNode node,
        string path, int indent, ref float y, float right, float rightReserve,
        Action deferredCommit, ref bool changed)
    {
        float labelX = 12 + indent * 12;
        bool expanded = Expanded(path);
        if (Gui.Button(new Rect(labelX, y, 18, 18), expanded ? "\u25be" : "\u25b8"))
            _fieldFoldouts[path] = !expanded;
        DrawFieldLabel(new Rect(labelX + 22, y, Math.Max(20, LabelW - 22), 18), label);
        y += RowH;
        if (!expanded || nested == null)
            return;
        node.Fields ??= new List<KeyValuePair<string, DocNode>>();
        foreach (var field in nested)
        {
            if (!DocVisible(field, node))
                continue;
            DocNode child = FindMapValue(node, field);
            if (child == null)
            {
                child = CreateDefaultNode(field.Kind, field.FieldType,
                    field.ElementKind, field.ElementType, field.Nested);
                node.Fields.Add(new(field.Name, child));
            }
            DrawDocValue(field.Name, field.Kind, field.FieldType, field.ElementKind,
                field.ElementType, field.Nested, child, path + "." + field.Name,
                indent + 1, ref y, right, 0, deferredCommit, ref changed);
        }
    }

    void DrawDocList(string label, SerializedType.Kind elementKind, Type elementType,
        SerializedType.FieldSchema[] nested, DocNode node, string path, int indent,
        ref float y, float right, float rightReserve, Action deferredCommit, ref bool changed)
    {
        node.Items ??= new List<DocNode>();
        float labelX = 12 + indent * 12;
        bool expanded = Expanded(path);
        if (Gui.Button(new Rect(labelX, y, 18, 18), expanded ? "\u25be" : "\u25b8"))
            _fieldFoldouts[path] = !expanded;
        DrawFieldLabel(new Rect(labelX + 22, y, Math.Max(20, LabelW - 22), 18),
            Nicify(label) + "  [" + node.Items.Count + "]");
        bool add = Gui.Button(new Rect(right - rightReserve - 20, y, 18, 18), "+");
        y += RowH;
        int removeIndex = -1;
        int moveFrom = -1;
        int moveTo = -1;
        if (expanded)
        {
            int drawCount = node.Items.Count;
            for (int i = 0; i < drawCount; i++)
            {
                int index = i;
                float itemY = y;
                if (Gui.Button(new Rect(right - 62, itemY, 18, 18), "\u25b4") && index > 0)
                    (moveFrom, moveTo) = (index, index - 1);
                if (Gui.Button(new Rect(right - 42, itemY, 18, 18), "\u25be") && index + 1 < drawCount)
                    (moveFrom, moveTo) = (index, index + 1);
                if (Gui.Button(new Rect(right - 22, itemY, 18, 18), "x"))
                    removeIndex = index;
                DrawDocValue("[" + index + "]", elementKind, elementType, default, null,
                    nested, node.Items[index], path + "[" + index + "]", indent + 1,
                    ref y, right, 66, deferredCommit, ref changed);
            }
        }
        if (removeIndex >= 0)
            node.Items.RemoveAt(removeIndex);
        else if (moveFrom >= 0)
            (node.Items[moveTo], node.Items[moveFrom]) = (node.Items[moveFrom], node.Items[moveTo]);
        else if (add)
            node.Items.Add(CreateDefaultNode(elementKind, elementType, default, null, nested));
        else
            return;
        ResetDocTextBuffers();
        changed = true;
    }

    void DrawDocString(in Rect rect, string current, DocNode node, string path, ref bool changed)
    {
        if (!_docStrBufs.TryGetValue(path, out char[] buffer)
            || !_docStrValues.TryGetValue(path, out string observed) || observed != current)
        {
            int capacity = 512;
            while (capacity <= current.Length)
                capacity *= 2;
            buffer = new char[capacity];
            int initial = current.Length;
            current.CopyTo(0, buffer, 0, initial);
            _docStrBufs[path] = buffer;
            _docStrLens[path] = initial;
            _docStrValues[path] = current;
        }
        int length = _docStrLens[path];
        if (length >= buffer.Length - 1)
        {
            Array.Resize(ref buffer, buffer.Length * 2);
            _docStrBufs[path] = buffer;
        }
        Gui.TextField(rect, ref buffer, ref length);
        _docStrBufs[path] = buffer;
        _docStrLens[path] = length;
        if (!BufferEquals(current, buffer, length))
        {
            node.Scalar = new string(buffer, 0, length);
            _docStrValues[path] = node.Scalar;
            changed = true;
        }
    }

    void DrawDocReference(in Rect rect, SerializedType.Kind kind, Type valueType,
        DocNode node, Action deferredCommit)
    {
        Event ev = Event.Current;
        var body = new Rect(rect.x, rect.y, Math.Max(20, rect.width - 20), rect.height);
        var clear = new Rect(rect.xMax - 18, rect.y, 18, rect.height);
        bool acceptable = DragDrop.Active && DragDrop.Accepts(kind, valueType);
        bool hover = acceptable && body.Contains(ev.MousePosition);
        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(body, hover ? new Color(70, 135, 85, 255)
                : acceptable ? new Color(52, 74, 58, 255) : new Color(45, 48, 58, 255), 0);
            GuiRenderer.DrawTextIn(body, DocRefDisplay(kind, node.Scalar ?? ""), SmallFont,
                RefTextColor, false, 2);
            if (hover)
                DragDrop.RegisterTarget(() =>
                {
                    node.Scalar = DragDrop.DocValue(kind, valueType);
                    deferredCommit();
                });
        }
        if (Gui.Button(clear, "x") && !string.IsNullOrEmpty(node.Scalar))
        {
            node.Scalar = "";
            deferredCommit();
        }
    }

    static string DocRefDisplay(SerializedType.Kind kind, string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(none)";
        if (kind == SerializedType.Kind.Asset)
            return App.Assets?.ResolvePath(value) ?? value;
        if (kind == SerializedType.Kind.GoRef)
            return int.TryParse(value, out int goId) ? App.EditScene.FindGo(goId)?.Name ?? value : value;
        int colon = value.IndexOf(':');
        if (colon > 0 && int.TryParse(value[..colon], out int componentGoId))
        {
            var target = App.EditScene.FindGo(componentGoId);
            if (target != null)
                return target.Name + " (" + value[(colon + 1)..] + ")";
        }
        return value;
    }

    static DocNode FindMapValue(DocNode map, SerializedType.FieldSchema field)
    {
        if (map?.Fields == null)
            return null;
        foreach (var pair in map.Fields)
            if (pair.Key == field.Name || (field.FormerName != null && pair.Key == field.FormerName))
                return pair.Value;
        return null;
    }

    // [ShowIf] doc modu: kardes prop eksikse default deger uzerinden degerlendirilir.
    static bool DocVisible(SerializedType.FieldSchema f, DocNode map)
    {
        if (f.ShowIf == null)
            return true;
        var sibling = FindMapValue(map, f.ShowIf) ?? CreateDefaultFieldNode(f.ShowIf);
        return SerializedType.ShowIfMatch(f, sibling.Scalar ?? "");
    }

    static bool DocVisible(SerializedType.FieldSchema f, SceneDoc.CompDoc cd)
    {
        if (f.ShowIf == null)
            return true;
        var sibling = FindProp(cd, f.ShowIf) ?? CreateDefaultFieldNode(f.ShowIf);
        return SerializedType.ShowIfMatch(f, sibling.Scalar ?? "");
    }

    static DocNode CreateDefaultNode(SerializedType.Kind kind, Type valueType,
        SerializedType.Kind elementKind, Type elementType, SerializedType.FieldSchema[] nested)
    {
        if (kind == SerializedType.Kind.List)
            return DocNode.Seq();
        if (kind == SerializedType.Kind.Object)
        {
            var map = DocNode.Map();
            object instance = Activator.CreateInstance(valueType);
            if (nested != null)
                foreach (var field in nested)
                    map.Add(field.Name, instance == null
                        ? CreateDefaultNode(field.Kind, field.FieldType, field.ElementKind,
                            field.ElementType, field.Nested)
                        : SerializedType.WriteField(instance, field, _ => "", _ => "", App.Assets));
            return map;
        }
        if (kind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef or SerializedType.Kind.Asset)
            return DocNode.Scal("");
        object value = DefaultElement(valueType);
        return DocNode.Scal(SerializedType.Format(value, kind));
    }

    static DocNode CreateDefaultFieldNode(SerializedType.FieldSchema field)
    {
        try
        {
            object owner = Activator.CreateInstance(field.DeclaringType);
            if (owner != null)
                return SerializedType.WriteField(owner, field, _ => "", _ => "", App.Assets);
        }
        catch
        {
        }
        return CreateDefaultNode(field.Kind, field.FieldType, field.ElementKind,
            field.ElementType, field.Nested);
    }

    void ResetDocTextBuffers()
    {
        _docStrBufs.Clear();
        _docStrLens.Clear();
        _docStrValues.Clear();
    }
}
