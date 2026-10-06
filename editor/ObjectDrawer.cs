using System;
using System.Collections;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Sema-tabanli nesne editoru: [Serializable] bir nesnenin alanlarini (SerializedType
// semasi) IMGUI olarak cizer. Inspector'in asset gorunumu ve ayar pencereleri
// (PlayerSettingsWindow) AYNI ciziciyi kullanir — alan tipi basina tek UI yolu.
// UI durumu (foldout, metin tamponlari) instance'ta yasar; her sahip kendi
// ObjectDrawer'ini tutar ve Bind ile hedef nesneyi baglar.
public sealed class ObjectDrawer
{
    const float RowH = InspectorPanel.RowH;

    // Etiket sutunu genisligi; sahip her frame panel genisliginden turetir.
    public float LabelW = 110f;

    // Alan yolu on eki: renk secici gibi global anahtarlar (path) farkli
    // pencerelerdeki ayni adli alanlarla cakismasin diye sahip basina ayrisir.
    public string KeyPrefix = "";

    public object Target { get; private set; }
    public SerializedType.FieldSchema[] Schema { get; private set; }

    readonly Dictionary<string, bool> _foldouts = new();
    readonly Dictionary<string, SerializedType.FieldSchema> _listMeasureSchemas = new();
    readonly Dictionary<string, char[]> _strBufs = new();
    readonly Dictionary<string, int> _strLens = new();

    // Hedef nesneyi baglar (tip semasi bir kez kurulur). Metin tamponlari sifirlanir,
    // foldout durumu korunur (ayni tipin alanlari ayni yolda kalir).
    public void Bind(object target)
    {
        Target = target;
        Schema = target == null ? null : SerializedType.Build(target.GetType());
        ResetTextBuffers();
    }

    public void Unbind()
    {
        Target = null;
        Schema = null;
        ResetTextBuffers();
    }

    // Tum alanlarin toplam yuksekligi (scroll icerigi icin; Draw ile birebir).
    public float Measure()
    {
        if (Target == null || Schema == null)
            return RowH;
        float height = 0;
        foreach (var field in Schema)
            height += MeasureField(Target, field, KeyPrefix + field.Name);
        return height;
    }

    // Alanlari y'den asagi cizer; herhangi bir deger degistiyse true doner.
    // deferredCommit: drag-drop gibi geciktirilmis yazimlar icin (anlik kaydet).
    public bool Draw(ref float y, float right, Action deferredCommit)
    {
        if (Target == null || Schema == null)
            return false;
        bool changed = false;
        foreach (var field in Schema)
            DrawSchemaField(Target, field, KeyPrefix + field.Name, 0, ref y, right, ref changed, deferredCommit);
        return changed;
    }

    float MeasureField(object owner, SerializedType.FieldSchema field, string path)
    {
        if (!SerializedType.ShowIfVisible(field, owner))
            return 0;
        object value = owner == null ? null : field.Get(owner);
        if (field.Kind == SerializedType.Kind.List)
            _listMeasureSchemas[path] = field;
        return MeasureValue(field.Kind, value, field.Nested, path);
    }

    float MeasureValue(SerializedType.Kind kind, object value,
        SerializedType.FieldSchema[] nested, string path)
    {
        float height = RowH;
        if (!Expanded(path))
            return height;
        if (kind == SerializedType.Kind.Object && value != null && nested != null)
        {
            foreach (var field in nested)
                height += MeasureField(value, field, path + "." + field.Name);
        }
        else if (kind == SerializedType.Kind.List && value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
                height += MeasureListItem(list[i], path + "[" + i + "]");
        }
        return height;
    }

    float MeasureListItem(object value, string path)
    {
        // Liste semasi cizim sirasinda kaydedilir; olcum yalniz acik inline
        // nesneleri ayirt eder, skaler elemanlar tek satirdir.
        if (!_listMeasureSchemas.TryGetValue(path[..path.LastIndexOf('[')], out var field))
            return RowH;
        return MeasureValue(field.ElementKind, value, field.Nested, path);
    }

    void DrawSchemaField(object owner, SerializedType.FieldSchema field, string path,
        int indent, ref float y, float right, ref bool changed, Action deferredCommit)
    {
        if (!SerializedType.ShowIfVisible(field, owner))
            return; // [ShowIf] gizli: olcum de 0 verir (yukseklik tutarli)
        object value = field.Get(owner);
        if (field.Kind == SerializedType.Kind.List)
            _listMeasureSchemas[path] = field;
        bool fieldChanged = false;
        DrawValue(field.Name, field.Kind, field.FieldType, field.ElementType,
            field.Nested, field, value, v => field.Set(owner, v), path,
            indent, ref y, right, 0, ref fieldChanged, deferredCommit);
        if (fieldChanged)
            changed = true;
    }

    internal void DrawValue(string label, SerializedType.Kind kind, Type valueType, Type elementType,
        SerializedType.FieldSchema[] nested, SerializedType.FieldSchema dragSchema,
        object value, Action<object> setValue, string path, int indent,
        ref float y, float right, float rightReserve, ref bool changed, Action deferredCommit)
    {
        if (kind == SerializedType.Kind.List)
        {
            DrawList(label, valueType, elementType, nested, dragSchema, value, setValue,
                path, indent, ref y, right, rightReserve, ref changed, deferredCommit);
            return;
        }
        if (kind == SerializedType.Kind.Object)
        {
            DrawObject(label, valueType, nested, value, setValue, path, indent,
                ref y, right, rightReserve, ref changed, deferredCommit);
            return;
        }

        float labelX = 12 + indent * 12;
        var labelRect = new Rect(labelX, y, Math.Max(20, LabelW - indent * 12), 18);
        float valueX = 12 + LabelW;
        var valueRect = new Rect(valueX, y, Math.Max(20, right - valueX - rightReserve), 18);
        InspectorPanel.DrawFieldLabel(labelRect, label);

        switch (kind)
        {
            case SerializedType.Kind.Float:
                {
                    float current = value is float number ? number : 0f;
                    float next = Gui.DragZone(labelRect, current, 0.02f); // etiket = drag tutamaci
                    next = Gui.DragFloat(valueRect, next, 0.02f);
                    if (next != current) { setValue(next); changed = true; }
                    break;
                }
            case SerializedType.Kind.Int:
                {
                    int current = value is int number ? number : 0;
                    int next = (int)MathF.Round(Gui.DragZone(labelRect, current, 0.05f));
                    next = Gui.DragInt(valueRect, next, 0.05f);
                    if (next != current) { setValue(next); changed = true; }
                    break;
                }
            case SerializedType.Kind.Bool:
                {
                    bool current = value is bool flag && flag;
                    bool next = Gui.Toggle(new Rect(valueRect.x, y, 18, 18), current);
                    if (next != current) { setValue(next); changed = true; }
                    break;
                }
            case SerializedType.Kind.String:
                DrawString(valueRect, value as string ?? "", setValue, path, ref changed);
                break;
            case SerializedType.Kind.Enum:
                {
                    string[] names = Enum.GetNames(valueType);
                    int index = Array.IndexOf(names, value?.ToString() ?? "");
                    int next = Gui.ComboBox(valueRect, index, names);
                    if (next != index && next >= 0)
                    {
                        setValue(Enum.Parse(valueType, names[next]));
                        changed = true;
                    }
                    break;
                }
            case SerializedType.Kind.Vec2:
            case SerializedType.Kind.Vec3:
                {
                    bool three = kind == SerializedType.Kind.Vec3;
                    Vec3 current = three
                        ? (value is Vec3 v3 ? v3 : default)
                        : value is Vec2 v2 ? new Vec3(v2.x, v2.y, 0) : default;
                    Vec3 next = InspectorPanel.Vec3Drags(valueRect, current, three);
                    if (!InspectorPanel.Same(next, current))
                    {
                        setValue(three ? next : (object)new Vec2(next.x, next.y));
                        changed = true;
                    }
                    break;
                }
            case SerializedType.Kind.Vec4:
                {
                    Vec4 current = value is Vec4 v4 ? v4 : default;
                    Vec4 next = InspectorPanel.Vec4Drags(valueRect, current);
                    if (!InspectorPanel.Same(next, current)) { setValue(next); changed = true; }
                    break;
                }
            case SerializedType.Kind.Color:
                {
                    Color current = value is Color color ? color : Color.White;
                    Color next = InspectorPanel.DrawColor(valueRect, current, path);
                    if (!InspectorPanel.SameColor(current, next)) { setValue(next); changed = true; }
                    break;
                }
            case SerializedType.Kind.Asset:
                DrawAssetRef(valueRect, value as IAsset, valueType, setValue, deferredCommit);
                break;
            default:
                if (Event.Current.Type == EventType.Repaint)
                    GuiRenderer.DrawTextIn(valueRect, "(scene reference unavailable)",
                        InspectorPanel.SmallFont, InspectorPanel.LabelDimColor, false, 2);
                break;
        }
        y += RowH;
    }

    void DrawObject(string label, Type valueType, SerializedType.FieldSchema[] nested,
        object value, Action<object> setValue, string path, int indent,
        ref float y, float right, float rightReserve, ref bool changed, Action deferredCommit)
    {
        float labelX = 12 + indent * 12;
        bool expanded = Expanded(path);
        if (Gui.Button(new Rect(labelX, y, 18, 18), expanded ? "\u25be" : "\u25b8"))
            _foldouts[path] = !expanded;
        InspectorPanel.DrawFieldLabel(new Rect(labelX + 22, y, Math.Max(20, LabelW - 22), 18), label);
        if (value == null && Gui.Button(new Rect(12 + LabelW, y,
                Math.Max(20, right - (12 + LabelW) - rightReserve), 18), "Create"))
        {
            value = Activator.CreateInstance(valueType);
            setValue(value);
            changed = true;
        }
        y += RowH;
        if (!expanded || value == null || nested == null)
            return;

        bool nestedChanged = false;
        foreach (var field in nested)
            DrawSchemaField(value, field, path + "." + field.Name,
                indent + 1, ref y, right, ref nestedChanged, () =>
                {
                    setValue(value);
                    deferredCommit?.Invoke();
                });
        if (nestedChanged)
        {
            setValue(value); // boxed struct must be written back to its parent
            changed = true;
        }
    }

    void DrawList(string label, Type listType, Type elementType,
        SerializedType.FieldSchema[] nested, SerializedType.FieldSchema schema,
        object value, Action<object> setValue, string path, int indent,
        ref float y, float right, float rightReserve, ref bool changed, Action deferredCommit)
    {
        float labelX = 12 + indent * 12;
        bool expanded = Expanded(path);
        if (Gui.Button(new Rect(labelX, y, 18, 18), expanded ? "\u25be" : "\u25b8"))
            _foldouts[path] = !expanded;
        int count = value is IList existing ? existing.Count : 0;
        InspectorPanel.DrawFieldLabel(new Rect(labelX + 22, y, Math.Max(20, LabelW - 22), 18),
            InspectorPanel.Nicify(label) + "  [" + count + "]");
        bool add = Gui.Button(new Rect(right - rightReserve - 20, y, 18, 18), "+");
        y += RowH;
        var list = value as IList;
        int removeIndex = -1;
        int moveFrom = -1;
        int moveTo = -1;
        if (expanded && list != null)
        {
            int drawCount = list.Count;
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
                object item = list[index];
                bool itemChanged = false;
                DrawValue("[" + index + "]", schema.ElementKind, elementType, null,
                    nested, schema, item, next => list[index] = next, path + "[" + index + "]",
                    indent + 1, ref y, right, 66, ref itemChanged, () =>
                    {
                        setValue(value);
                        deferredCommit?.Invoke();
                    });
                if (itemChanged)
                {
                    setValue(value);
                    changed = true;
                }
            }
        }
        if (removeIndex >= 0)
            value = RemoveElement(value, listType, elementType, removeIndex);
        else if (moveFrom >= 0)
            Swap((IList)value, moveFrom, moveTo);
        else if (add)
            value = AddElement(value, listType, elementType);
        else
            return;
        setValue(value);
        ResetTextBuffers();
        changed = true;
    }

    void DrawString(in Rect rect, string current, Action<object> setValue,
        string path, ref bool changed)
    {
        if (!_strBufs.TryGetValue(path, out char[] buffer))
        {
            int capacity = 512;
            while (capacity <= current.Length)
                capacity *= 2;
            buffer = new char[capacity];
            int initial = current.Length;
            current.CopyTo(0, buffer, 0, initial);
            _strBufs[path] = buffer;
            _strLens[path] = initial;
        }
        int length = _strLens[path];
        if (length >= buffer.Length - 1)
        {
            Array.Resize(ref buffer, buffer.Length * 2);
            _strBufs[path] = buffer;
        }
        Gui.TextField(rect, ref buffer, ref length);
        _strBufs[path] = buffer;
        _strLens[path] = length;
        if (!InspectorPanel.BufferEquals(current, buffer, length))
        {
            setValue(new string(buffer, 0, length));
            changed = true;
        }
    }

    static void DrawAssetRef(in Rect rect, IAsset asset, Type valueType,
        Action<object> setValue, Action deferredCommit)
    {
        Event ev = Event.Current;
        var body = new Rect(rect.x, rect.y, Math.Max(20, rect.width - 20), rect.height);
        var clear = new Rect(rect.xMax - 18, rect.y, 18, rect.height);
        bool acceptable = DragDrop.Active && DragDrop.Kind == DragDrop.Payload.Asset
            && App.Assets != null && App.Assets.IsAssignable(DragDrop.AssetPath, valueType);
        bool hover = acceptable && body.Contains(ev.MousePosition);
        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(body, hover ? new Color(70, 135, 85, 255)
                : acceptable ? new Color(52, 74, 58, 255) : new Color(45, 48, 58, 255), 0);
            GuiRenderer.DrawTextIn(body, asset?.Name ?? "(none)", InspectorPanel.SmallFont,
                InspectorPanel.RefTextColor, false, 2);
            if (hover)
                DragDrop.RegisterTarget(() =>
                {
                    setValue(SerializedType.Parse(DragDrop.AssetGuid, SerializedType.Kind.Asset,
                        valueType, App.Assets));
                    deferredCommit?.Invoke();
                });
        }
        if (Gui.Button(clear, "x") && asset != null)
        {
            setValue(null);
            deferredCommit?.Invoke();
        }
    }

    bool Expanded(string path)
        => !_foldouts.TryGetValue(path, out bool expanded) || expanded;

    public void ResetTextBuffers()
    {
        _strBufs.Clear();
        _strLens.Clear();
    }

    static object AddElement(object value, Type listType, Type elementType)
    {
        object item = InspectorPanel.DefaultElement(elementType);
        if (listType.IsArray)
        {
            var source = value as Array;
            int count = source?.Length ?? 0;
            var result = Array.CreateInstance(elementType, count + 1);
            if (source != null)
                Array.Copy(source, result, count);
            result.SetValue(item, count);
            return result;
        }
        var list = value as IList ?? (IList)Activator.CreateInstance(listType);
        list.Add(item);
        return list;
    }

    static object RemoveElement(object value, Type listType, Type elementType, int index)
    {
        if (listType.IsArray)
        {
            var source = (Array)value;
            var result = Array.CreateInstance(elementType, source.Length - 1);
            if (index > 0)
                Array.Copy(source, 0, result, 0, index);
            if (index + 1 < source.Length)
                Array.Copy(source, index + 1, result, index, source.Length - index - 1);
            return result;
        }
        var list = (IList)value;
        list.RemoveAt(index);
        return list;
    }

    static void Swap(IList list, int a, int b)
    {
        object temp = list[a];
        list[a] = list[b];
        list[b] = temp;
    }
}
