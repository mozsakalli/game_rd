using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Digitoy.Mcp
{
    // SerializedProperty uzerinden alan oku/yaz/sema. Alan adlari TEMIZ aliastir ("m_Color" -> "Color");
    // cozumleme hem temiz hem ham adi kabul eder. Ic ice yol: 'Tracks[0].Keys[1].Value'
    // (Unity'nin 'Array.data[i]' bicimine cevrilir). Deger bicimleri rpc.guide "values" ile ayni.
    public static class PropIO
    {
        const int MaxDepth = 4;
        const int MaxArrayDump = 64;

        public static string Alias(string raw)
        {
            if (raw.StartsWith("m_", StringComparison.Ordinal) && raw.Length > 2)
                return raw.Substring(2);
            return raw;
        }

        // --- dokum ---

        public static JNode DumpComponent(Component c)
        {
            var map = JNode.Map();
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                map.Add(Alias(it.name), Dump(it, 1));
            }
            return map;
        }

        public static JNode Dump(SerializedProperty p, int depth)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return JNode.Num(p.longValue);
                case SerializedPropertyType.Boolean: return JNode.Bool(p.boolValue);
                case SerializedPropertyType.Float: return JNode.Num(p.doubleValue);
                case SerializedPropertyType.String: return JNode.Str_(p.stringValue);
                case SerializedPropertyType.Color: return JNode.Str_(Commands.Col(p.colorValue));
                case SerializedPropertyType.LayerMask: return JNode.Num(p.intValue);
                case SerializedPropertyType.Enum:
                    {
                        int idx = p.enumValueIndex;
                        var names = p.enumNames;
                        if (idx >= 0 && idx < names.Length) return JNode.Str_(names[idx]);
                        return JNode.Num(p.intValue); // flags / disi deger
                    }
                case SerializedPropertyType.Vector2: return JNode.Str_(Commands.V2(p.vector2Value));
                case SerializedPropertyType.Vector3: return JNode.Str_(Commands.V3(p.vector3Value));
                case SerializedPropertyType.Vector4: return JNode.Str_(Commands.V4(p.vector4Value));
                case SerializedPropertyType.Vector2Int: { var v = p.vector2IntValue; return JNode.Str_(v.x + " " + v.y); }
                case SerializedPropertyType.Vector3Int: { var v = p.vector3IntValue; return JNode.Str_(v.x + " " + v.y + " " + v.z); }
                case SerializedPropertyType.Rect: { var r = p.rectValue; return JNode.Str_(JNode.F(r.x) + " " + JNode.F(r.y) + " " + JNode.F(r.width) + " " + JNode.F(r.height)); }
                case SerializedPropertyType.RectInt: { var r = p.rectIntValue; return JNode.Str_(r.x + " " + r.y + " " + r.width + " " + r.height); }
                case SerializedPropertyType.Quaternion: return JNode.Str_(Commands.V3(p.quaternionValue.eulerAngles));
                case SerializedPropertyType.Bounds: { var b = p.boundsValue; return JNode.Str_(Commands.V3(b.center) + " | " + Commands.V3(b.size)); }
                case SerializedPropertyType.Character: return JNode.Str_(((char)p.intValue).ToString());
                case SerializedPropertyType.ObjectReference: return JNode.Str_(RefToString(p.objectReferenceValue));
                case SerializedPropertyType.ExposedReference: return JNode.Str_(RefToString(p.exposedReferenceValue));
                case SerializedPropertyType.AnimationCurve: return JNode.Str_("<curve " + p.animationCurveValue.length + " keys>");
                case SerializedPropertyType.Gradient: return JNode.Str_("<gradient>");
                case SerializedPropertyType.Hash128: return JNode.Str_(p.hash128Value.ToString());
                case SerializedPropertyType.ManagedReference: return JNode.Str_("<managed " + p.managedReferenceFullTypename + ">");
                case SerializedPropertyType.ArraySize: return JNode.Num(p.intValue);
                case SerializedPropertyType.Generic:
                    if (depth > MaxDepth) return JNode.Str_("<...>");
                    if (p.isArray)
                    {
                        var seq = JNode.Seq();
                        int n = Math.Min(p.arraySize, MaxArrayDump);
                        for (int i = 0; i < n; i++)
                            seq.Push(Dump(p.GetArrayElementAtIndex(i), depth + 1));
                        if (p.arraySize > n) seq.Push(JNode.Str_($"<+{p.arraySize - n} more>"));
                        return seq;
                    }
                    else
                    {
                        var map = JNode.Map();
                        var child = p.Copy();
                        var end = p.GetEndProperty();
                        if (child.NextVisible(true))
                            do
                            {
                                if (SerializedProperty.EqualContents(child, end)) break;
                                map.Add(Alias(child.name), Dump(child, depth + 1));
                            } while (child.NextVisible(false));
                        return map;
                    }
                default: return JNode.Str_("<" + p.propertyType + ">");
            }
        }

        // Referans metni: asset -> yol; sahne GO -> id; sahne component -> 'goId:Type:ordinal'.
        public static string RefToString(Object o)
        {
            if (o == null) return null;
            if (EditorUtility.IsPersistent(o))
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (AssetDatabase.IsSubAsset(o)) return path + "#" + o.name;
                return path;
            }
            if (o is GameObject go) return go.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            if (o is Component c)
            {
                var comps = c.gameObject.GetComponents(c.GetType());
                int ord = Array.IndexOf(comps, c);
                return c.gameObject.GetInstanceID().ToString(CultureInfo.InvariantCulture) + ":" + Commands.TypeName(c.GetType()) + ":" + ord;
            }
            return o.name;
        }

        // --- yazim ---

        // path: 'Color' | 'Tracks[0].Keys[1].Value' | 'm_Color'. value null = yolun sonu liste elemaniysa SIL, degilse default.
        public static void Set(Component c, string path, JNode value)
        {
            var so = new SerializedObject(c);
            var segs = ParseSegments(path);
            if (segs.Count == 0)
                throw new RpcError("prop yolu bos");
            SerializedProperty cur = null;
            for (int i = 0; i < segs.Count; i++)
            {
                var seg = segs[i];
                bool last = i == segs.Count - 1;
                if (seg.Index >= 0)
                {
                    if (cur == null || !cur.isArray)
                        throw new RpcError($"'{Join(segs, i)}': liste degil, index kullanilamaz");
                    if (seg.Index > cur.arraySize)
                        throw new RpcError($"'{Join(segs, i)}': index {seg.Index} > eleman sayisi {cur.arraySize} (sona eklemek icin index == {cur.arraySize})");
                    if (seg.Index == cur.arraySize)
                    {
                        if (last && (value == null || value.IsNull))
                            throw new RpcError($"'{Join(segs, i)}': silinecek eleman yok (eleman sayisi {cur.arraySize})");
                        cur.InsertArrayElementAtIndex(seg.Index); // sona ekle
                    }
                    if (last && (value == null || value.IsNull))
                    {
                        cur.DeleteArrayElementAtIndex(seg.Index);
                        so.ApplyModifiedProperties();
                        return;
                    }
                    cur = cur.GetArrayElementAtIndex(seg.Index);
                }
                else
                {
                    var next = FindChild(so, cur, seg.Name);
                    if (next == null)
                        throw new RpcError($"'{Join(segs, i)}': alan yok ({Commands.TypeName(c.GetType())}; comp.schema ile alanlari gor" + (cur != null ? "" : "") + ")");
                    cur = next;
                }
            }
            if (value == null || value.IsNull)
                SetDefault(cur);
            else
                Assign(cur, value, path, c);
            so.ApplyModifiedProperties();
        }

        struct Seg { public string Name; public int Index; }

        static List<Seg> ParseSegments(string path)
        {
            var segs = new List<Seg>();
            if (string.IsNullOrEmpty(path)) return segs;
            int i = 0;
            while (i < path.Length)
            {
                if (path[i] == '.') { i++; continue; }
                if (path[i] == '[')
                {
                    int close = path.IndexOf(']', i);
                    if (close < 0) throw new RpcError($"prop yolu: ']' eksik: '{path}'");
                    string num = path.Substring(i + 1, close - i - 1);
                    if (!int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 0)
                        throw new RpcError($"prop yolu: index tam sayi degil: '[{num}]'");
                    segs.Add(new Seg { Name = null, Index = idx });
                    i = close + 1;
                    continue;
                }
                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                segs.Add(new Seg { Name = path.Substring(start, i - start), Index = -1 });
            }
            return segs;
        }

        static string Join(List<Seg> segs, int upTo)
        {
            var sb = new StringBuilder();
            for (int i = 0; i <= upTo && i < segs.Count; i++)
            {
                if (segs[i].Index >= 0) sb.Append('[').Append(segs[i].Index).Append(']');
                else { if (sb.Length > 0) sb.Append('.'); sb.Append(segs[i].Name); }
            }
            return sb.ToString();
        }

        // Cocuk alan: ham ad, 'm_'+ad, 'm_'+Buyuk(ad), buyuk-kucuk harf duyarsiz alias.
        static SerializedProperty FindChild(SerializedObject so, SerializedProperty parent, string name)
        {
            SerializedProperty Try(string n) => parent == null ? so.FindProperty(n) : parent.FindPropertyRelative(n);
            var p = Try(name) ?? Try("m_" + name);
            if (p == null && name.Length > 0 && char.IsLower(name[0]))
                p = Try("m_" + char.ToUpperInvariant(name[0]) + name.Substring(1));
            if (p == null && name.Length > 0 && char.IsUpper(name[0]))
                p = Try("m_" + char.ToLowerInvariant(name[0]) + name.Substring(1)) ?? Try(char.ToLowerInvariant(name[0]) + name.Substring(1));
            if (p != null) return p;
            // Son care: dogrudan cocuklar arasinda alias esitligi (case-insensitive).
            SerializedProperty it, end = null;
            if (parent == null) { it = so.GetIterator(); if (!it.NextVisible(true)) return null; }
            else { it = parent.Copy(); end = parent.GetEndProperty(); if (!it.NextVisible(true)) return null; }
            do
            {
                if (end != null && SerializedProperty.EqualContents(it, end)) break;
                if (string.Equals(Alias(it.name), name, StringComparison.OrdinalIgnoreCase) || string.Equals(it.displayName.Replace(" ", ""), name, StringComparison.OrdinalIgnoreCase))
                    return it.Copy();
            } while (it.NextVisible(false));
            return null;
        }

        static void SetDefault(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: case SerializedPropertyType.LayerMask: case SerializedPropertyType.Character: p.longValue = 0; break;
                case SerializedPropertyType.Boolean: p.boolValue = false; break;
                case SerializedPropertyType.Float: p.doubleValue = 0; break;
                case SerializedPropertyType.String: p.stringValue = ""; break;
                case SerializedPropertyType.Color: p.colorValue = Color.white; break;
                case SerializedPropertyType.Enum: p.enumValueIndex = 0; break;
                case SerializedPropertyType.Vector2: p.vector2Value = Vector2.zero; break;
                case SerializedPropertyType.Vector3: p.vector3Value = Vector3.zero; break;
                case SerializedPropertyType.Vector4: p.vector4Value = Vector4.zero; break;
                case SerializedPropertyType.Vector2Int: p.vector2IntValue = Vector2Int.zero; break;
                case SerializedPropertyType.Vector3Int: p.vector3IntValue = Vector3Int.zero; break;
                case SerializedPropertyType.Rect: p.rectValue = Rect.zero; break;
                case SerializedPropertyType.Quaternion: p.quaternionValue = Quaternion.identity; break;
                case SerializedPropertyType.ObjectReference: p.objectReferenceValue = null; break;
                case SerializedPropertyType.Generic:
                    if (p.isArray) p.arraySize = 0;
                    else throw new RpcError($"'{p.propertyPath}': ic ice nesne null yapilamaz; alanlarini tek tek yaz");
                    break;
                default: throw new RpcError($"'{p.propertyPath}': {p.propertyType} icin default yazimi desteklenmiyor");
            }
        }

        public static void Assign(SerializedProperty p, JNode v, string what, Component owner)
        {
            string s = v.Scalar;
            try
            {
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer: p.longValue = (long)Math.Round(double.Parse(Scalar(v, what), NumberStyles.Float, CultureInfo.InvariantCulture)); return;
                    case SerializedPropertyType.Boolean: p.boolValue = Commands.ParseBool(Scalar(v, what), what); return;
                    case SerializedPropertyType.Float: p.doubleValue = double.Parse(Scalar(v, what), NumberStyles.Float, CultureInfo.InvariantCulture); return;
                    case SerializedPropertyType.String: p.stringValue = Scalar(v, what); return;
                    case SerializedPropertyType.Character: p.intValue = Scalar(v, what).Length > 0 ? Scalar(v, what)[0] : 0; return;
                    case SerializedPropertyType.Color: p.colorValue = Commands.ParseColor(Scalar(v, what), what); return;
                    case SerializedPropertyType.LayerMask: p.intValue = ParseLayerMask(Scalar(v, what), what); return;
                    case SerializedPropertyType.Enum: AssignEnum(p, Scalar(v, what), what); return;
                    case SerializedPropertyType.Vector2: { var f = Commands.ParseFloats(Scalar(v, what), 2, what); p.vector2Value = new Vector2(f[0], f[1]); return; }
                    case SerializedPropertyType.Vector3: p.vector3Value = Commands.ParseV3(Scalar(v, what), what); return;
                    case SerializedPropertyType.Vector4: { var f = Commands.ParseFloats(Scalar(v, what), 4, what); p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); return; }
                    case SerializedPropertyType.Vector2Int: { var f = Commands.ParseFloats(Scalar(v, what), 2, what); p.vector2IntValue = new Vector2Int((int)f[0], (int)f[1]); return; }
                    case SerializedPropertyType.Vector3Int: { var f = Commands.ParseFloats(Scalar(v, what), 3, what); p.vector3IntValue = new Vector3Int((int)f[0], (int)f[1], (int)f[2]); return; }
                    case SerializedPropertyType.Rect: { var f = Commands.ParseFloats(Scalar(v, what), 4, what); p.rectValue = new Rect(f[0], f[1], f[2], f[3]); return; }
                    case SerializedPropertyType.RectInt: { var f = Commands.ParseFloats(Scalar(v, what), 4, what); p.rectIntValue = new RectInt((int)f[0], (int)f[1], (int)f[2], (int)f[3]); return; }
                    case SerializedPropertyType.Quaternion:
                        {
                            var parts = Scalar(v, what).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 4) { var f = Commands.ParseFloats(s, 4, what); p.quaternionValue = new Quaternion(f[0], f[1], f[2], f[3]); }
                            else p.quaternionValue = Quaternion.Euler(Commands.ParseV3(s, what));
                            return;
                        }
                    case SerializedPropertyType.ObjectReference:
                        p.objectReferenceValue = ResolveRef(Scalar(v, what), FieldType(owner, p.propertyPath), what);
                        return;
                    case SerializedPropertyType.Generic:
                        if (p.isArray)
                        {
                            if (!v.IsSeq) throw new RpcError($"'{what}' liste bekler");
                            p.arraySize = v.Items.Count;
                            for (int i = 0; i < v.Items.Count; i++)
                                Assign(p.GetArrayElementAtIndex(i), v.Items[i], what + "[" + i + "]", owner);
                            return;
                        }
                        if (!v.IsMap) throw new RpcError($"'{what}' ic ice nesne bekler ({{alan: deger}})");
                        foreach (var kv in v.Fields)
                        {
                            var child = FindChild(p.serializedObject, p, kv.Key)
                                ?? throw new RpcError($"'{what}.{kv.Key}': alan yok");
                            if (kv.Value.IsNull) SetDefault(child);
                            else Assign(child, kv.Value, what + "." + kv.Key, owner);
                        }
                        return;
                    default:
                        throw new RpcError($"'{what}': {p.propertyType} tipi RPC ile yazilamaz");
                }
            }
            catch (FormatException)
            {
                throw new RpcError($"'{what}' icin gecersiz deger: '{s}' ({p.propertyType})");
            }
        }

        static string Scalar(JNode v, string what) => v.IsScalar ? v.Str : throw new RpcError($"'{what}' skaler deger bekler");

        static void AssignEnum(SerializedProperty p, string s, string what)
        {
            var names = p.enumNames;
            int idx = Array.IndexOf(names, s);
            if (idx >= 0) { p.enumValueIndex = idx; return; }
            if (s.IndexOf('|') >= 0)
            {
                // Flags: 'A|B' → bit birlesimi (enum degerleri Type uzerinden).
                int flags = 0;
                foreach (var part in s.Split('|'))
                {
                    int i = Array.IndexOf(names, part.Trim());
                    if (i < 0) throw new RpcError($"'{what}' enum degeri yok: '{part}' (gecerli: {string.Join(", ", names)})");
                    flags |= 1 << i;
                }
                p.intValue = flags;
                return;
            }
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)) { p.intValue = raw; return; }
            int ci = Array.FindIndex(names, n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase));
            if (ci >= 0)
                throw new RpcError($"'{what}' enum buyuk-kucuk harfe duyarli: '{names[ci]}' yaz");
            throw new RpcError($"'{what}' enum degeri yok: '{s}' (gecerli: {string.Join(", ", names)})");
        }

        static int ParseLayerMask(string s, string what)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            int mask = 0;
            foreach (var part in s.Split('|'))
            {
                int layer = LayerMask.NameToLayer(part.Trim());
                if (layer < 0) throw new RpcError($"'{what}' katman yok: '{part}'");
                mask |= 1 << layer;
            }
            return mask;
        }

        // Referans cozumleme: '' → null; 'Assets/..png' | 'Assets/..png#sub' | guid → asset; '123' → GO; '123:Type:0' → component;
        // 'A/B' → hiyerarsi yolu. Hedef tip uyusmazsa GameObject'ten component cekmeyi dener.
        public static Object ResolveRef(string s, Type want, string what)
        {
            if (string.IsNullOrEmpty(s) || s == "null") return null;
            want = want ?? typeof(Object);
            Object found = null;
            string sub = null;
            int hash = s.IndexOf('#');
            if (hash > 0 && s.StartsWith("Assets/", StringComparison.Ordinal)) { sub = s.Substring(hash + 1); s = s.Substring(0, hash); }

            if (s.StartsWith("Assets/", StringComparison.Ordinal) || s.StartsWith("Packages/", StringComparison.Ordinal))
            {
                if (sub != null)
                {
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(s))
                        if (o != null && o.name == sub && want.IsInstanceOfType(o)) { found = o; break; }
                    if (found == null) throw new RpcError($"'{what}': '{s}' icinde '{sub}' alt-asseti yok");
                }
                else
                {
                    found = AssetDatabase.LoadAssetAtPath(s, want) ?? AssetDatabase.LoadMainAssetAtPath(s);
                    if (found == null) throw new RpcError($"'{what}': asset yok: '{s}' (asset.list)");
                }
            }
            else if (s.Length == 32 && IsHex(s))
            {
                string path = AssetDatabase.GUIDToAssetPath(s);
                if (string.IsNullOrEmpty(path)) throw new RpcError($"'{what}': guid bulunamadi: {s}");
                found = AssetDatabase.LoadAssetAtPath(path, want) ?? AssetDatabase.LoadMainAssetAtPath(path);
            }
            else
            {
                // Sahne referansi
                var parts = s.Split(':');
                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    var go = Commands.RequireGo(id);
                    if (parts.Length == 1) found = go;
                    else
                    {
                        var ct = Commands.RequireCompType(parts[1]);
                        int ord = parts.Length > 2 ? Commands.ParseInt(parts[2], what) : 0;
                        var comps = go.GetComponents(ct);
                        if (ord < 0 || ord >= comps.Length) throw new RpcError($"'{what}': GO {id} uzerinde {ord}. {parts[1]} yok ({comps.Length} adet)");
                        found = comps[ord];
                    }
                }
                else
                {
                    found = Commands.FindByPath(s);
                    if (found == null) throw new RpcError($"'{what}': referans cozulemedi: '{s}' (asset yolu, guid, goId, 'goId:Type:0' ya da 'A/B' yolu)");
                }
            }
            if (found == null) return null;
            if (want.IsInstanceOfType(found)) return found;
            if (found is GameObject g && typeof(Component).IsAssignableFrom(want))
            {
                var c = g.GetComponent(want);
                if (c != null) return c;
                throw new RpcError($"'{what}': GO '{g.name}' uzerinde {want.Name} yok");
            }
            if (found is Component fc && want == typeof(GameObject)) return fc.gameObject;
            // Texture2D -> Sprite gibi: ayni yoldaki alt-asset
            if (EditorUtility.IsPersistent(found))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(found)))
                    if (o != null && want.IsInstanceOfType(o)) return o;
            throw new RpcError($"'{what}': '{s}' {found.GetType().Name} tipinde, {want.Name} bekleniyor");
        }

        static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        // --- reflection: alan tipi (ObjectReference hedef tipi, liste eleman tipi) ---

        public static Type FieldType(object root, string propertyPath)
        {
            if (root == null) return null;
            Type t = root.GetType();
            foreach (var seg in propertyPath.Split('.'))
            {
                if (t == null) return null;
                if (seg == "Array") continue;
                if (seg.StartsWith("data[", StringComparison.Ordinal))
                {
                    t = ElementType(t);
                    continue;
                }
                var f = FindField(t, seg);
                if (f == null) return null;
                t = f.FieldType;
            }
            return t;
        }

        public static Type ElementType(Type t)
        {
            if (t == null) return null;
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return t.GetGenericArguments()[0];
            foreach (var i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>)) return i.GetGenericArguments()[0];
            return null;
        }

        public static FieldInfo FindField(Type t, string name)
        {
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                var f = cur.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        // --- sema ---

        public static JNode Schema(Type compType)
        {
            var go = new GameObject("__mcp_schema") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Component c;
                try { c = go.AddComponent(compType); }
                catch (Exception ex) { throw new RpcError($"{compType.Name} ornegi olusturulamadi: {ex.Message}"); }
                if (c == null) throw new RpcError($"{compType.Name} eklenemedi (RequireComponent/DisallowMultiple?)");
                var so = new SerializedObject(c);
                var fields = JNode.Seq();
                var it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script") continue;
                    fields.Push(FieldSchema(it, c, 1));
                }
                var r = JNode.Map();
                r.Add("type", Commands.TypeName(compType));
                r.Add("fullName", compType.FullName);
                r.Add("fields", fields);
                var hooks = Hooks(compType);
                if (hooks.Items.Count > 0) r.Add("pointerHandlers", hooks);
                return r;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static JNode FieldSchema(SerializedProperty p, Component owner, int depth)
        {
            var f = JNode.Map();
            f.Add("name", Alias(p.name));
            if (Alias(p.name) != p.name) f.Add("raw", p.name);
            var ft = FieldType(owner, p.propertyPath);
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: case SerializedPropertyType.LayerMask: case SerializedPropertyType.ArraySize: f.Add("type", "int"); break;
                case SerializedPropertyType.Boolean: f.Add("type", "bool"); break;
                case SerializedPropertyType.Float: f.Add("type", "float"); break;
                case SerializedPropertyType.String: f.Add("type", "string"); break;
                case SerializedPropertyType.Color: f.Add("type", "color"); f.Add("format", "#rrggbbaa"); break;
                case SerializedPropertyType.Enum:
                    f.Add("type", "enum");
                    var vals = JNode.Seq();
                    foreach (var n in p.enumNames) vals.Push(JNode.Str_(n));
                    f.Add("values", vals);
                    if (ft != null && ft.IsDefined(typeof(FlagsAttribute), false)) f.Add("format", "A|B (flags)");
                    break;
                case SerializedPropertyType.Vector2: case SerializedPropertyType.Vector2Int: f.Add("type", "vec2"); f.Add("format", "x y"); break;
                case SerializedPropertyType.Vector3: case SerializedPropertyType.Vector3Int: f.Add("type", "vec3"); f.Add("format", "x y z"); break;
                case SerializedPropertyType.Vector4: f.Add("type", "vec4"); f.Add("format", "x y z w"); break;
                case SerializedPropertyType.Rect: case SerializedPropertyType.RectInt: f.Add("type", "rect"); f.Add("format", "x y w h"); break;
                case SerializedPropertyType.Quaternion: f.Add("type", "quat"); f.Add("format", "euler 'x y z' ya da 'x y z w'"); break;
                case SerializedPropertyType.ObjectReference:
                    f.Add("type", "ref");
                    f.Add("refType", ft != null ? ft.Name : "Object");
                    f.Add("format", RefFormat(ft));
                    break;
                case SerializedPropertyType.Generic:
                    if (p.isArray)
                    {
                        f.Add("type", "list");
                        var et = ElementType(ft);
                        if (et != null)
                        {
                            f.Add("element", et.Name);
                            if (depth < 3 && !et.IsPrimitive && et != typeof(string) && !typeof(Object).IsAssignableFrom(et) && !et.IsEnum)
                                f.Add("elementFields", ReflectFields(et, depth + 1));
                            else if (typeof(Object).IsAssignableFrom(et)) f.Add("format", RefFormat(et));
                            else if (et.IsEnum) { var ev = JNode.Seq(); foreach (var n in Enum.GetNames(et)) ev.Push(JNode.Str_(n)); f.Add("values", ev); }
                        }
                        f.Add("format", "liste: 'Alan[i]' yolu (i == sayi → sona ekle, null → sil) ya da tum liste [..]");
                    }
                    else
                    {
                        f.Add("type", "object");
                        if (depth < 3)
                        {
                            var sub = JNode.Seq();
                            var child = p.Copy();
                            var end = p.GetEndProperty();
                            if (child.NextVisible(true))
                                do
                                {
                                    if (SerializedProperty.EqualContents(child, end)) break;
                                    sub.Push(FieldSchema(child, owner, depth + 1));
                                } while (child.NextVisible(false));
                            f.Add("fields", sub);
                        }
                    }
                    break;
                default: f.Add("type", p.propertyType.ToString().ToLowerInvariant()); f.Add("readonly", true); break;
            }
            if (p.propertyType != SerializedPropertyType.Generic)
                f.Add("default", Dump(p, depth));
            return f;
        }

        static string RefFormat(Type t)
        {
            if (t == null) return "asset yolu | guid | goId | 'goId:Type:0'";
            if (t == typeof(GameObject)) return "goId ya da 'A/B' hiyerarsi yolu";
            if (typeof(Component).IsAssignableFrom(t)) return $"'goId:{t.Name}:0' ya da goId (GO'dan cekilir)";
            return $"asset yolu ('Assets/..') ya da guid ({t.Name})";
        }

        // Yalnizca reflection ile (ornek yok) serializable sinifin alanlari — liste eleman tipleri icin.
        static JNode ReflectFields(Type t, int depth)
        {
            var seq = JNode.Seq();
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (var fi in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (fi.IsStatic || fi.IsInitOnly) continue;
                    if (!fi.IsPublic && !fi.IsDefined(typeof(SerializeField), true)) continue;
                    if (fi.IsDefined(typeof(NonSerializedAttribute), true)) continue;
                    var f = JNode.Map();
                    f.Add("name", Alias(fi.Name));
                    var ft = fi.FieldType;
                    if (ft == typeof(int) || ft == typeof(long) || ft == typeof(short) || ft == typeof(byte)) f.Add("type", "int");
                    else if (ft == typeof(float) || ft == typeof(double)) f.Add("type", "float");
                    else if (ft == typeof(bool)) f.Add("type", "bool");
                    else if (ft == typeof(string)) f.Add("type", "string");
                    else if (ft == typeof(Color) || ft == typeof(Color32)) { f.Add("type", "color"); f.Add("format", "#rrggbbaa"); }
                    else if (ft == typeof(Vector2) || ft == typeof(Vector2Int)) { f.Add("type", "vec2"); f.Add("format", "x y"); }
                    else if (ft == typeof(Vector3) || ft == typeof(Vector3Int)) { f.Add("type", "vec3"); f.Add("format", "x y z"); }
                    else if (ft == typeof(Vector4)) { f.Add("type", "vec4"); f.Add("format", "x y z w"); }
                    else if (ft == typeof(Quaternion)) { f.Add("type", "quat"); f.Add("format", "euler 'x y z'"); }
                    else if (ft.IsEnum) { f.Add("type", "enum"); var ev = JNode.Seq(); foreach (var n in Enum.GetNames(ft)) ev.Push(JNode.Str_(n)); f.Add("values", ev); }
                    else if (typeof(Object).IsAssignableFrom(ft)) { f.Add("type", "ref"); f.Add("refType", ft.Name); f.Add("format", RefFormat(ft)); }
                    else if (ElementType(ft) is Type et) { f.Add("type", "list"); f.Add("element", et.Name); if (depth < 3 && !et.IsPrimitive && et != typeof(string) && !typeof(Object).IsAssignableFrom(et)) f.Add("elementFields", ReflectFields(et, depth + 1)); }
                    else if (depth < 3 && (ft.IsClass || ft.IsValueType) && ft.IsDefined(typeof(SerializableAttribute), false)) { f.Add("type", "object"); f.Add("fields", ReflectFields(ft, depth + 1)); }
                    else f.Add("type", ft.Name);
                    seq.Push(f);
                }
            return seq;
        }

        // Tipin uyguladigi EventSystems pointer arayuzleri (davranis var mi?).
        static JNode Hooks(Type t)
        {
            var seq = JNode.Seq();
            foreach (var i in t.GetInterfaces())
                if (i.Namespace == "UnityEngine.EventSystems" && i.Name.StartsWith("I", StringComparison.Ordinal) && i.Name.EndsWith("Handler", StringComparison.Ordinal))
                    seq.Push(JNode.Str_(i.Name));
            return seq;
        }
    }
}
