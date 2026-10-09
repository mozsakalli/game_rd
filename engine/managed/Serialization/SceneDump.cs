using System;
using System.Text;

namespace DigitoyEngine;

// Sahne ALAN DOKUMU - test/regresyon araci (docs/registry-removal.md Faz 0). Canli sahneyi
// (GO hiyerarsisi + component'ler + katalog semasindaki her alan) deterministik metne doker.
// Editor (YAML yolu) ve player (pismis pak, AOT) ayni metni uretmeli: serilestirme/katalog
// degisikliklerinin kabul olcutu. Degerler reflection'la okunur (Schema.Get DEGIL: AOT'ta
// deger tipi erisimcileri yok); float'lar bit deseniyle yazilir (bicimlendirme farki olmasin).
public static class SceneDump
{
    public static string Write(Scene scene, TypeCatalog catalog)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < scene.RootCount; i++)
            WriteGo(sb, scene.GetRoot(i), catalog, 0);
        return sb.ToString();
    }

    static void WriteGo(StringBuilder sb, GameObject go, TypeCatalog catalog, int depth)
    {
        string pad = Pad(depth);
        sb.Append(pad).Append("GO ").Append(go.name)
          .Append(" active=").Append(go.activeSelf ? "1" : "0")
          .Append(" layer=").Append(go.layer).Append('\n');
        var t = go.transform;
        sb.Append(pad).Append("  T pos=").Append(V3(t.localPosition))
          .Append(" rot=").Append(V3(t.localEulerAngles))
          .Append(" scale=").Append(V3(t.localScale)).Append('\n');
        int n = go.ComponentCount;
        for (int i = 0; i < n; i++)
        {
            var c = go.ComponentAt(i);
            if (c is Transform || c._destroyed)
                continue;
            var entry = catalog?.Find(c.GetType());
            sb.Append(pad).Append("  C ").Append(c.GetType().Name)
              .Append(" enabled=").Append(c.enabled ? "1" : "0");
            if (entry == null)
            {
                sb.Append(" (katalogda yok)\n");
                continue;
            }
            sb.Append('\n');
            WriteFields(sb, c, entry.Schema, depth + 2);
        }
        for (var ch = t.FirstChild; ch != null; ch = ch.NextSibling)
            if (ch._gameObject != null)
                WriteGo(sb, ch._gameObject, catalog, depth + 1);
    }

    static void WriteFields(StringBuilder sb, object owner, SerializedType.FieldSchema[] schema, int depth)
    {
        if (schema == null)
            return;
        string pad = Pad(depth);
        foreach (var f in schema)
        {
            sb.Append(pad).Append(f.Name).Append(": ");
            object v;
            try { v = ReadField(owner, f); }
            catch (Exception e) { sb.Append("<okunamadi: ").Append(e.GetType().Name).Append(">\n"); continue; }
            WriteValue(sb, v, f, f.Kind, depth);
        }
    }

    static void WriteValue(StringBuilder sb, object v, SerializedType.FieldSchema f, SerializedType.Kind kind, int depth)
    {
        switch (kind)
        {
            case SerializedType.Kind.List:
                {
                    if (v == null) { sb.Append("null\n"); return; }
                    string pad = Pad(depth + 1);
                    if (v is Array)
                    {
                        // AOT dizileri IList/IEnumerable degil; `is T[]` tip testi ve System.Array.Length
                        // transpiler/runtime'da yok (Faz 1: tipli dizi erisimi). Simdilik yalniz varlik.
                        sb.Append("<dizi: Faz 1>\n");
                        return;
                    }
                    if (v is System.Collections.IEnumerable en)
                    {
                        var items = new System.Collections.Generic.List<object>();
                        foreach (var o in en) items.Add(o);
                        sb.Append("[").Append(items.Count).Append("]\n");
                        for (int i = 0; i < items.Count; i++)
                        {
                            sb.Append(pad).Append('-').Append(i).Append(": ");
                            WriteValue(sb, items[i], f, f.ElementKind, depth + 1);
                        }
                        return;
                    }
                    sb.Append("<liste degil: ").Append(v.GetType().Name).Append(">\n");
                    return;
                }
            case SerializedType.Kind.Object:
                if (v == null) { sb.Append("null\n"); return; }
                sb.Append("{\n");
                WriteFields(sb, v, f.Nested, depth + 1);
                sb.Append(Pad(depth)).Append("}\n");
                return;
            default:
                sb.Append(Scalar(v, kind)).Append('\n');
                return;
        }
    }

    static string Scalar(object v, SerializedType.Kind kind)
    {
        if (v == null)
            return "null";
        switch (kind)
        {
            case SerializedType.Kind.Float: return F((float)v);
            case SerializedType.Kind.Int: return ((int)v).ToString();
            case SerializedType.Kind.Bool: return (bool)v ? "true" : "false";
            case SerializedType.Kind.String: return "\"" + (string)v + "\"";
            case SerializedType.Kind.Enum: return "enum#" + v.GetHashCode();
            case SerializedType.Kind.Vec2: { var p = (Vec2)v; return F(p.x) + "," + F(p.y); }
            case SerializedType.Kind.Vec3: return V3((Vec3)v);
            case SerializedType.Kind.Vec4: { var p = (Vec4)v; return F(p.x) + "," + F(p.y) + "," + F(p.z) + "," + F(p.w); }
            case SerializedType.Kind.Color: { var c = (Color)v; return c.r + "," + c.g + "," + c.b + "," + c.a; }
            case SerializedType.Kind.Asset: return v is IAsset a ? "asset:" + a.Name : "asset:<" + v.GetType().Name + ">";
            case SerializedType.Kind.GoRef: return v is GameObject g ? "go:" + Path(g.transform) : "go:<" + v.GetType().Name + ">";
            case SerializedType.Kind.CompRef:
                return v is Component c2 && c2._gameObject != null
                    ? "comp:" + Path(c2.transform) + ":" + c2.GetType().Name
                    : "comp:<" + v.GetType().Name + ">";
            default: return "<" + kind + ">";
        }
    }

    // Alan degeri ada gore (Reflect kabugu; DeclaringType uzerinden: ic ice struct'in kutusunda GetType yok).
    static object ReadField(object owner, SerializedType.FieldSchema f)
    {
        var fi = f.Info ?? Reflect.FindField(f.DeclaringType, f.Name);
        if (fi == null) throw new Exception("alan yok: " + f.Name);
        return fi.GetValue(owner);
    }

    static string Path(Transform t)
    {
        string s = t._gameObject?.name ?? "?";
        for (var p = t.parent; p != null; p = p.parent)
            s = (p._gameObject?.name ?? "?") + "/" + s;
        return s;
    }

    static string V3(Vec3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    static string F(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");
    static string Pad(int depth)
    {
        var sb = new StringBuilder(depth * 2);
        for (int i = 0; i < depth * 2; i++) sb.Append(' ');
        return sb.ToString();
    }
}
