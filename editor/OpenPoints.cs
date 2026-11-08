using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DigitoyEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitoyEditor;

// ACIK NOKTA (docs/engine-notes.md "KOD SAHNESI"): oyun kodundaki `protected virtual float X => 140;` gibi uyeler.
// Kaynak = C#; editor bunlari Play sahnesindeki canli component'lerden (reflection) bulur, degerini kullaniciya
// dokunulabilir sunar ve degisikligi Roslyn ile KODDAKI LITERAL'e geri yazar (AssetWatcher derler, Play taze dogar).
// Yan dosya/override tablosu YOK. Desteklenen ifade: sayi, true/false, "metin", Enum.Uye, new Color(...), new Vec2/Vec3(...).
// Baska ifade (hesap, baska uyeye referans) = salt okunur: o deger AI'in yapisal karari, kullaniciya ait degil.
static class OpenPoints
{
    public sealed class Point
    {
        public string Name;
        public Type Type;
        public Type Declaring;      // literal'in yasadigi sinif (override varsa o)
        public PropertyInfo Prop;
        public Component Owner;     // canli instance (deger okumak icin)
        public object Value;
        public string File;         // kaynak dosya (bulunduysa)
        public int Line;            // 1-tabanli
        public string Expr;         // ifade metni
        public bool Writable;       // ifade desteklenen literal bicimde mi
        public string Key => Declaring.Name + "." + Name;
    }

    // --- kesif ---

    static readonly List<Point> _points = new();
    static int _scanBuild = -1;
    static Scene _scanScene;

    public static IReadOnlyList<Point> Points
    {
        get
        {
            var scene = PlayMode.PlayScene;
            if (scene != _scanScene || _scanBuild != AssetWatcher.BuildVersion)
                Rescan(scene);
            return _points;
        }
    }

    public static void Invalidate() => _scanBuild = -1;

    static void Rescan(Scene scene)
    {
        _points.Clear();
        _scanScene = scene;
        _scanBuild = AssetWatcher.BuildVersion;
        if (scene == null)
            return;
        var seen = new HashSet<string>();
        var engineAsm = typeof(Component).Assembly;
        for (int r = 0; r < scene.RootCount; r++)
            Walk(scene.GetRoot(r), engineAsm, seen);
        LocateAll();
    }

    static void Walk(GameObject go, Assembly engineAsm, HashSet<string> seen)
    {
        for (int i = 0; i < go.ComponentCount; i++)
        {
            var c = go.ComponentAt(i);
            if (c == null || c.GetType().Assembly == engineAsm)
                continue;
            // Somut tipten tabana: override varsa en turemis bildirim kazanir (Prop.DeclaringType).
            var props = c.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var p in props)
            {
                var g = p.GetGetMethod(true);
                if (g == null || !g.IsVirtual || p.GetIndexParameters().Length != 0 || !Supported(p.PropertyType))
                    continue;
                if (p.DeclaringType.Assembly == engineAsm)
                    continue;
                var pt = new Point { Name = p.Name, Type = p.PropertyType, Declaring = p.DeclaringType, Prop = p, Owner = c };
                if (!seen.Add(pt.Key))
                    continue;
                try { pt.Value = p.GetValue(c); } catch { pt.Value = null; }
                _points.Add(pt);
            }
        }
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            Walk(t.GetChild(i).gameObject, engineAsm, seen);
    }

    static bool Supported(Type t)
        => t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(string) || t.IsEnum
           || t == typeof(Color) || t == typeof(Vec2) || t == typeof(Vec3);

    // --- kaynak konumu (Roslyn) ---

    sealed class SourceFile { public string Path; public DateTime Stamp; public SyntaxTree Tree; public string Text; }
    static readonly Dictionary<string, SourceFile> _files = new(StringComparer.OrdinalIgnoreCase);

    static IEnumerable<SourceFile> Sources()
    {
        var root = App.Project?.AssetsPath;
        if (root == null || !Directory.Exists(root))
            yield break;
        foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var stamp = File.GetLastWriteTimeUtc(f);
            if (!_files.TryGetValue(f, out var sf) || sf.Stamp != stamp)
            {
                string text = File.ReadAllText(f);
                sf = new SourceFile { Path = f, Stamp = stamp, Text = text, Tree = CSharpSyntaxTree.ParseText(text, path: f) };
                _files[f] = sf;
            }
            yield return sf;
        }
    }

    static void LocateAll()
    {
        if (_points.Count == 0)
            return;
        foreach (var sf in Sources())
        {
            var rootNode = sf.Tree.GetRoot();
            foreach (var cls in rootNode.DescendantNodes().OfTypeClass())
            {
                foreach (var pt in _points)
                {
                    if (pt.File != null || cls.Identifier.ValueText != pt.Declaring.Name)
                        continue;
                    foreach (var m in cls.Members)
                    {
                        if (m is not PropertyDeclarationSyntax pd || pd.Identifier.ValueText != pt.Name)
                            continue;
                        var expr = LiteralExpr(pd);
                        pt.File = sf.Path;
                        pt.Line = pd.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        pt.Expr = expr?.ToString() ?? pd.ToString();
                        pt.Writable = expr != null && IsSupportedExpr(expr, pt.Type);
                    }
                }
            }
        }
    }

    static IEnumerable<ClassDeclarationSyntax> OfTypeClass(this IEnumerable<SyntaxNode> nodes)
    {
        foreach (var n in nodes)
            if (n is ClassDeclarationSyntax c) yield return c;
    }

    // `X => expr;`  |  `X { get => expr; }`  |  `X { get { return expr; } }`
    static ExpressionSyntax LiteralExpr(PropertyDeclarationSyntax pd)
    {
        if (pd.ExpressionBody != null)
            return pd.ExpressionBody.Expression;
        if (pd.AccessorList == null)
            return null;
        foreach (var a in pd.AccessorList.Accessors)
        {
            if (!a.IsKind(SyntaxKind.GetAccessorDeclaration))
                continue;
            if (a.ExpressionBody != null)
                return a.ExpressionBody.Expression;
            if (a.Body != null && a.Body.Statements.Count == 1 && a.Body.Statements[0] is ReturnStatementSyntax rs)
                return rs.Expression;
        }
        return null;
    }

    static bool IsNumber(ExpressionSyntax e)
    {
        if (e is PrefixUnaryExpressionSyntax u && u.IsKind(SyntaxKind.UnaryMinusExpression))
            e = u.Operand;
        return e is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.NumericLiteralExpression);
    }

    static bool IsSupportedExpr(ExpressionSyntax e, Type t)
    {
        if (t == typeof(float) || t == typeof(int))
            return IsNumber(e);
        if (t == typeof(bool))
            return e.IsKind(SyntaxKind.TrueLiteralExpression) || e.IsKind(SyntaxKind.FalseLiteralExpression);
        if (t == typeof(string))
            return e.IsKind(SyntaxKind.StringLiteralExpression);
        if (t.IsEnum)
            return e is MemberAccessExpressionSyntax;
        if (t == typeof(Color) || t == typeof(Vec2) || t == typeof(Vec3))
        {
            if (e is not ObjectCreationExpressionSyntax oc || oc.ArgumentList == null)
                return false;
            foreach (var a in oc.ArgumentList.Arguments)
                if (!IsNumber(a.Expression)) return false;
            int n = oc.ArgumentList.Arguments.Count;
            return t == typeof(Color) ? n == 3 || n == 4 : t == typeof(Vec2) ? n == 2 : n == 3;
        }
        return false;
    }

    // --- geri yazma ---

    public static Point Find(string key)
    {
        foreach (var p in Points)
            if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    // Degeri koddaki ifadenin yerine yazar. Dosya degisimi AssetWatcher'i tetikler (derleme + Play yeniden dogar).
    public static bool Set(Point p, object value, out string error)
    {
        error = null;
        if (p == null) { error = "open point not found"; return false; }
        if (!p.Writable || p.File == null) { error = "not a literal (read-only): " + p.Key + " = " + p.Expr; return false; }
        if (!_files.TryGetValue(p.File, out var sf)) { error = "source not cached: " + p.File; return false; }
        if (File.GetLastWriteTimeUtc(p.File) != sf.Stamp) { Invalidate(); error = "source changed, rescan"; return false; }
        var rootNode = sf.Tree.GetRoot();
        PropertyDeclarationSyntax target = null;
        foreach (var cls in rootNode.DescendantNodes().OfTypeClass())
        {
            if (cls.Identifier.ValueText != p.Declaring.Name) continue;
            foreach (var m in cls.Members)
                if (m is PropertyDeclarationSyntax pd && pd.Identifier.ValueText == p.Name) { target = pd; break; }
            if (target != null) break;
        }
        var expr = target != null ? LiteralExpr(target) : null;
        if (expr == null) { error = "declaration not found: " + p.Key; return false; }
        string text = Format(value, p.Type, expr);
        if (text == null) { error = "value not representable: " + value; return false; }
        var span = expr.Span;
        string src = sf.Text.Substring(0, span.Start) + text + sf.Text.Substring(span.End);
        File.WriteAllText(p.File, src, new UTF8Encoding(false));
        p.Value = value;
        p.Expr = text;
        // Cache'i hemen tazele: ayni surukleme icinde bir sonraki yazim yeni span'i gorsun (dosya damgasi degisti).
        _files[p.File] = new SourceFile { Path = p.File, Stamp = File.GetLastWriteTimeUtc(p.File), Text = src, Tree = CSharpSyntaxTree.ParseText(src, path: p.File) };
        return true;
    }

    static string Num(float v)
    {
        if (v == MathF.Round(v) && MathF.Abs(v) < 1e7f)
            return ((int)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    static bool IsHex(ExpressionSyntax e)
        => e.ToString().StartsWith("0x", StringComparison.OrdinalIgnoreCase);

    static string Format(object value, Type t, ExpressionSyntax old)
    {
        var inv = CultureInfo.InvariantCulture;
        if (t == typeof(float)) return Num(Convert.ToSingle(value, inv));
        if (t == typeof(int)) return Convert.ToInt32(value, inv).ToString(inv);
        if (t == typeof(bool)) return (bool)value ? "true" : "false";
        if (t == typeof(string)) return SymbolDisplay.FormatLiteral((string)value ?? "", quote: true);
        if (t.IsEnum) return t.Name + "." + Enum.GetName(t, value);
        var oc = old as ObjectCreationExpressionSyntax;
        if (t == typeof(Color))
        {
            var c = (Color)value;
            bool hex = oc != null && IsHex(oc.ArgumentList.Arguments[0].Expression);
            bool four = c.a != 255 || (oc != null && oc.ArgumentList.Arguments.Count == 4);
            string B(byte b) => hex ? "0x" + b.ToString("X2") : b.ToString(inv);
            return "new Color(" + B(c.r) + ", " + B(c.g) + ", " + B(c.b) + (four ? ", " + B(c.a) : "") + ")";
        }
        if (t == typeof(Vec2)) { var v = (Vec2)value; return "new Vec2(" + Num(v.x) + ", " + Num(v.y) + ")"; }
        if (t == typeof(Vec3)) { var v = (Vec3)value; return "new Vec3(" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + ")"; }
        return null;
    }

    // RPC/metin girdisi -> deger. Color: "#RRGGBB[AA]" | "r g b [a]"; Vec: "x y [z]".
    public static object Parse(string s, Type t)
    {
        var inv = CultureInfo.InvariantCulture;
        s = (s ?? "").Trim();
        if (t == typeof(float)) return float.Parse(s, inv);
        if (t == typeof(int)) return int.Parse(s, inv);
        if (t == typeof(bool)) return bool.Parse(s);
        if (t == typeof(string)) return s;
        if (t.IsEnum) return Enum.Parse(t, s, true);
        var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (t == typeof(Color))
        {
            if (s.StartsWith("#"))
            {
                uint v = uint.Parse(s.Substring(1), NumberStyles.HexNumber, inv);
                if (s.Length == 7) return new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v);
                return new Color((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            }
            return new Color(byte.Parse(parts[0], inv), byte.Parse(parts[1], inv), byte.Parse(parts[2], inv), parts.Length > 3 ? byte.Parse(parts[3], inv) : (byte)255);
        }
        if (t == typeof(Vec2)) return new Vec2(float.Parse(parts[0], inv), float.Parse(parts[1], inv));
        if (t == typeof(Vec3)) return new Vec3(float.Parse(parts[0], inv), float.Parse(parts[1], inv), float.Parse(parts[2], inv));
        throw new ArgumentException("unsupported type " + t.Name);
    }

    public static string Show(object v)
    {
        var inv = CultureInfo.InvariantCulture;
        return v switch
        {
            null => "",
            float f => f.ToString("R", inv),
            Color c => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2") + (c.a != 255 ? c.a.ToString("X2") : ""),
            Vec2 a => a.x.ToString("R", inv) + " " + a.y.ToString("R", inv),
            Vec3 b => b.x.ToString("R", inv) + " " + b.y.ToString("R", inv) + " " + b.z.ToString("R", inv),
            _ => Convert.ToString(v, inv),
        };
    }

    // --- RPC ---

    [RpcCommand("tweak.list", "ACIK NOKTALAR: Play'deki kod sahnesi component'lerinin `virtual` literal uyeleri (ad, tip, deger, sinif, dosya:satir, writable). Kaynak = C#; deger degisimi koda yazilir")]
    static DocNode List(RpcContext ctx)
    {
        var seq = DocNode.Seq();
        foreach (var p in Points)
        {
            var m = DocNode.Map();
            m.Add("key", DocNode.Scal(p.Key));
            m.Add("type", DocNode.Scal(p.Type.Name));
            m.Add("value", DocNode.Scal(Show(p.Value)));
            m.Add("expr", DocNode.Scal(p.Expr ?? ""));
            m.Add("file", DocNode.Scal(p.File != null ? Path.GetRelativePath(App.Project.Root, p.File).Replace('\\', '/') + ":" + p.Line : ""));
            m.Add("writable", DocNode.Scal(p.Writable ? "true" : "false"));
            seq.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("points", seq);
        r.Add("count", DocNode.Scal(_points.Count.ToString(CultureInfo.InvariantCulture)));
        if (PlayMode.PlayScene == null) r.Add("note", DocNode.Scal("Play yok: sim.run ile baslat"));
        return r;
    }

    [RpcCommand("tweak.set", "Acik noktayi koda yazar: key = 'Sinif.Uye' ya da 'Uye'; value metin (float/int/bool/enum/string; Color '#RRGGBB' | 'r g b'; Vec 'x y [z]'). Dosya degisir -> derleme -> Play taze dogar")]
    static DocNode SetCmd(RpcContext ctx, string key, string value)
    {
        var p = Find(key) ?? throw new RpcError("open point not found: " + key);
        object v;
        try { v = Parse(value, p.Type); }
        catch (Exception e) { throw new RpcError("value parse: " + e.Message); }
        if (!Set(p, v, out var err))
            throw new RpcError(err);
        var r = DocNode.Map();
        r.Add("key", DocNode.Scal(p.Key));
        r.Add("expr", DocNode.Scal(p.Expr));
        r.Add("file", DocNode.Scal(Path.GetRelativePath(App.Project.Root, p.File).Replace('\\', '/') + ":" + p.Line));
        r.Add("note", DocNode.Scal("yazildi; derleme arka planda (log.tail 'code reloaded')"));
        return r;
    }
}
