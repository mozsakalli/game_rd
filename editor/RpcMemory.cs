using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DigitoyEngine;

namespace DigitoyEditor;

// AI icin "debugger watch penceresi" (docs/engine-notes.md "MEM.* BELLEK IZLEYICI"): oyun modeli/servis durumu icin oyun tarafina
// hicbir kod/attribute gerekmez. Ifade yolu reflection ile yurunur ve deger agaci dokulur:
//   mem.types(filter, scope)  → yuklu tipler (kesif)
//   mem.statics(type)         → tipin static uyeleri + deger ozeti (kok bulma: GinTableService.I)
//   mem.dump(expr, depth, max)→ 'GinTableService.I.CurrentState.Players[1].Hand', 'TableServices.Factories[101]', 'Discards["x"]'
// SALT OKUNUR (mem.*/live.get): yalniz alan okuma + property getter cagrisi (debugger ile ayni risk: yan etkili getter).
// Tek yazma/tetikleme kapisi live.call (CallFrom): canli nesnede METOT cagirir — yalniz Play'de, dev araci.
// Statik tip cache yok: her cagri App.Code.CodeAssemblies'i taze okur (ALC reload guvenli). [Sensitive] maskesi RpcHost.Ok'ta.
// Release'e tek satir girmez (editor assembly'si).
public static class RpcMemory
{
    const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // Testler sahte assembly kumesi verir; null = gercek (App.Code).
    internal static Func<string, IEnumerable<Assembly>> AssembliesOverride;

    static IEnumerable<Assembly> Assemblies(string scope)
    {
        if (AssembliesOverride != null)
            return AssembliesOverride(scope);
        var list = new List<Assembly>();
        switch (scope)
        {
            case "game": AddGame(list); break;
            case "engine": list.Add(typeof(GameObject).Assembly); break;
            case "editor": list.Add(typeof(App).Assembly); break;
            case "all": AddGame(list); list.Add(typeof(GameObject).Assembly); list.Add(typeof(App).Assembly); break;
            default: throw new RpcError("scope 'game' | 'engine' | 'editor' | 'all' olmali");
        }
        return list;

        static void AddGame(List<Assembly> into)
        {
            var code = App.Code?.CodeAssemblies;
            if (code != null)
                foreach (var a in code)
                    if (a != null) into.Add(a);
        }
    }

    static IEnumerable<Type> TypesOf(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types; }
        foreach (var t in types)
            if (t != null && !t.IsDefined(typeof(CompilerGeneratedAttribute), false) && !t.Name.StartsWith('<'))
                yield return t;
    }

    [RpcCommand("mem.types", "BELLEK IZLEYICI kesif: yuklu tipler (ad, namespace, kind, statics = static alan/property sayisi — kok adaylari). filter: ad alt-dizesi ('Service'); scope: game (oyun/plugin assembly'leri) | engine | editor | all. Oyun modeli/servis durumu icin kod yazma: mem.types → mem.statics → mem.dump")]
    static DocNode Types(string filter = "", string scope = "game")
    {
        var found = new List<Type>();
        foreach (var asm in Assemblies(scope))
            foreach (var t in TypesOf(asm))
                if (filter.Length == 0 || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    found.Add(t);
        found.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        var root = DocNode.Map();
        var seq = DocNode.Seq();
        foreach (var t in found)
        {
            var m = DocNode.Map();
            m.Add("name", DocNode.Scal(DisplayName(t)));
            if (t.Namespace != null) m.Add("ns", DocNode.Scal(t.Namespace));
            m.Add("kind", DocNode.Scal(Kind(t)));
            int statics = 0;
            foreach (var f in t.GetFields(Stat)) if (!IsBackingField(f)) statics++;
            foreach (var p in t.GetProperties(Stat)) if (p.GetIndexParameters().Length == 0 && p.GetMethod != null) statics++;
            if (statics > 0) m.Add("statics", DocNode.Scal(statics.ToString(CultureInfo.InvariantCulture)));
            seq.Items.Add(m);
        }
        root.Add("count", DocNode.Scal(found.Count.ToString(CultureInfo.InvariantCulture)));
        root.Add("types", seq);
        return root;
    }

    [RpcCommand("mem.statics", "Tipin static uyeleri + deger ozeti (kok bulma: 'GinTableService' → I = {GinTableService}). type: kisa ad ya da tam ad (Ns.Tip, ic tip Ns.Dis.Ic). Sonra mem.dump('Tip.Uye...')")]
    static DocNode Statics(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new RpcError("type bos olamaz");
        var segs = Parse(type);
        int used = ResolveType(segs, out var t);
        if (used != segs.Count)
            throw new RpcError($"'{type}' tip degil; tipin ardinda uye var. mem.dump kullan");
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(t.FullName));
        root.Add("members", StaticMembers(t));
        return root;
    }

    [RpcCommand("mem.dump", "Bellekteki nesneyi DEBUGGER gibi dok (salt okunur; oyun tarafinda kod/attribute gerekmez). expr: 'Tip.StaticUye.alan.Prop[2].Dict[\"k\"]' (Tip kisa ya da tam ad; kisa ad cakisirsa adaylar listelenir; ic tip Dis.Ic; index int, dictionary anahtari int/string/enum). depth: nesne derinligi (otesi '{Tip}' ozeti); max: koleksiyon basina eleman. Alanlar + property getter'lari (istisna → '!Tip: mesaj'); '~ad' = non-public alan; '$type' = calisma zamani tipi. Motor nesneleri (GameObject/Component/Texture) tek satir ozet — sahne icin scene.dumpLive")]
    static DocNode Dump(string expr, int depth = 2, int max = 50)
    {
        if (string.IsNullOrWhiteSpace(expr))
            throw new RpcError("expr bos olamaz (ornek: 'GinTableService.I.CurrentState')");
        if (depth < 0) depth = 0;
        if (max < 1) max = 1;
        var segs = Parse(expr);
        int i = ResolveType(segs, out var type);
        var root = DocNode.Map();
        root.Add("expr", DocNode.Scal(expr));
        if (i == segs.Count)
        {
            root.Add("type", DocNode.Scal(type.FullName));
            root.Add("members", StaticMembers(type));
            return root;
        }
        // Ilk uye static; kalanlar deger uzerinde.
        var seg = segs[i++];
        if (seg.IsIndex)
            throw new RpcError($"tipten sonra static uye adi gelmeli: '{type.Name}'");
        object cur = ReadStatic(type, seg.Name, out var declared);
        string path = type.Name + "." + seg.Name;
        for (; i < segs.Count; i++)
        {
            seg = segs[i];
            if (cur == null)
                throw new RpcError($"'{path}' null; '{seg}' okunamaz");
            if (seg.IsIndex) { cur = Index(cur, seg.Name, path); declared = null; path += "[" + seg.Name + "]"; }
            else { cur = ReadInstance(cur, seg.Name, path, out declared); path += "." + seg.Name; }
        }
        root.Add("value", Render(cur, declared, depth, max, new Stack<object>(), isRoot: true));
        return root;
    }

    // live.get: kok verilmis nesneden (canli component) uye yolu okur — tip ön eki yok; bos expr = kokun kendisi.
    internal static DocNode DumpFrom(object rootObj, string expr, int depth = 2, int max = 50)
    {
        if (depth < 0) depth = 0;
        if (max < 1) max = 1;
        object cur = rootObj;
        Type declared = null;
        string path = rootObj.GetType().Name;
        if (!string.IsNullOrWhiteSpace(expr))
        {
            var segs = ParseMembers(expr);
            foreach (var seg in segs)
            {
                if (cur == null)
                    throw new RpcError($"'{path}' null; '{seg}' okunamaz");
                if (seg.IsIndex) { cur = Index(cur, seg.Name, path); declared = null; path += "[" + seg.Name + "]"; }
                else { cur = ReadInstance(cur, seg.Name, path, out declared); path += "." + seg.Name; }
            }
        }
        return Render(cur, declared, depth, max, new Stack<object>(), isRoot: true);
    }

    // ── ifade ayristirma ─────────────────────────────────────────────────────
    readonly struct Seg
    {
        public readonly string Name; // uye adi ya da index/anahtar metni
        public readonly bool IsIndex;
        public readonly bool Quoted;  // ["x"] → string anahtar
        public Seg(string name, bool isIndex, bool quoted) { Name = name; IsIndex = isIndex; Quoted = quoted; }
        public override string ToString() => IsIndex ? "[" + Name + "]" : Name;
    }

    static List<Seg> Parse(string expr)
    {
        var segs = ParseMembers(expr);
        if (segs.Count == 0 || segs[0].IsIndex)
            throw new RpcError("ifade tip adiyla baslamali (mem.types ile bul)");
        return segs;
    }

    // Uye yolu ayristirma (tip sarti yok): 'a.b[2]["k"]'.
    static List<Seg> ParseMembers(string expr)
    {
        var segs = new List<Seg>();
        int i = 0, n = expr.Length;
        while (i < n)
        {
            char c = expr[i];
            if (c == '.' || char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '[')
            {
                int close = expr.IndexOf(']', i);
                if (close < 0)
                    throw new RpcError($"']' eksik: '{expr}'");
                string inner = expr.Substring(i + 1, close - i - 1).Trim();
                bool quoted = inner.Length >= 2 && (inner[0] == '"' || inner[0] == '\'') && inner[^1] == inner[0];
                if (quoted) inner = inner.Substring(1, inner.Length - 2);
                if (inner.Length == 0 && !quoted)
                    throw new RpcError($"bos index: '{expr}'");
                segs.Add(new Seg(inner, true, quoted));
                i = close + 1;
                continue;
            }
            int start = i;
            while (i < n && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_' || expr[i] == '`' || expr[i] == '+')) i++;
            if (i == start)
                throw new RpcError($"beklenmeyen karakter '{c}' (konum {i}): '{expr}'");
            segs.Add(new Seg(expr.Substring(start, i - start), false, false));
        }
        return segs;
    }

    // Tip cozumu: en uzun on-ekten kisaya (Ns.Dis.Ic → Dis.Ic → Dis): tam ada esit YA DA namespace'siz sonek ('+' ic tip ayraci '.'
    // kabul). Eslesme tekil olmali; kisa ad cakisirsa adaylar listelenir. Donus: tuketilen segment sayisi.
    static int ResolveType(List<Seg> segs, out Type type)
    {
        int idents = 0;
        while (idents < segs.Count && !segs[idents].IsIndex) idents++;
        var all = new List<Type>();
        foreach (var asm in Assemblies("all"))
            all.AddRange(TypesOf(asm));
        for (int len = idents; len >= 1; len--)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < len; k++) { if (k > 0) sb.Append('.'); sb.Append(segs[k].Name); }
            string full = sb.ToString(), suffix = "." + full;
            var hits = new List<Type>();
            foreach (var t in all)
            {
                string n = t.FullName?.Replace('+', '.');
                if (n != null && (n == full || n.EndsWith(suffix, StringComparison.Ordinal))) hits.Add(t);
            }
            if (hits.Count == 1) { type = hits[0]; return len; }
            if (hits.Count > 1)
            {
                var names = new List<string>();
                foreach (var h in hits) names.Add(h.FullName.Replace('+', '.'));
                throw new RpcError($"'{full}' birden cok tipe uyuyor, tam ad ver: {string.Join(", ", names)}");
            }
        }
        throw new RpcError($"tip yok: '{segs[0].Name}' (mem.types(filter) ile ara; tam ad 'Ns.Tip' da olur)");
    }

    // ── uye okuma ────────────────────────────────────────────────────────────
    static object ReadStatic(Type type, string name, out Type declared)
    {
        GuardSensitive(name, type.Name);
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Stat);
            if (f != null) { declared = f.FieldType; return f.GetValue(null); }
            var p = t.GetProperty(name, Stat);
            if (p != null && p.GetMethod != null && p.GetIndexParameters().Length == 0)
            {
                declared = p.PropertyType;
                try { return p.GetValue(null); }
                catch (TargetInvocationException e) { throw new RpcError($"{type.Name}.{name} getter istisna: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
            }
        }
        throw new RpcError($"static uye yok: {type.Name}.{name} (mem.statics('{type.Name}') ile listele)");
    }

    static object ReadInstance(object target, string name, string path, out Type declared)
    {
        GuardSensitive(name, path);
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Inst);
            if (f != null) { declared = f.FieldType; return f.GetValue(target); }
            var p = t.GetProperty(name, Inst);
            if (p != null && p.GetMethod != null && p.GetIndexParameters().Length == 0)
            {
                declared = p.PropertyType;
                try { return p.GetValue(target); }
                catch (TargetInvocationException e) { throw new RpcError($"{path}.{name} getter istisna: {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
            }
        }
        throw new RpcError(MemberMissing(target.GetType(), name, path));
    }

    // "uye yok" mesaji: ad bir METOT'a denk geliyorsa acikca soyle (AI 'Svc.Ready' ile metot cagirmaya calisiyordu,
    // 'uye yok' aliyordu). Yoksa alan/property listesine yonlendir + metotlar icin live.call ipucu.
    static string MemberMissing(Type type, string name, string path)
    {
        var sigs = MethodSignatures(type, name);
        if (sigs.Count > 0)
            return $"{path}.{name} bir METOT, deger degil: {string.Join(" | ", sigs)} — cagirmak icin live.call(expr='{(path.Contains('.') ? path[(path.IndexOf('.') + 1)..] + "." : "")}{name}', args=[...])";
        return $"uye yok: {path}.{name} ({type.Name}; alanlar: mem.dump('{path}', depth=1); metotlar: live.call(expr='{(path.Contains('.') ? path[(path.IndexOf('.') + 1)..] : "")}', list=true))";
    }

    internal static List<string> MethodSignatures(Type type, string name, bool all = false)
    {
        var sigs = new List<string>();
        for (var t = type; t != null && t != typeof(object) && t != typeof(Component); t = t.BaseType)
            foreach (var m in t.GetMethods(Inst))
            {
                if (m.IsSpecialName || (!all && m.Name != name) || (all && m.DeclaringType == typeof(Component)))
                    continue;
                var ps = m.GetParameters();
                var sb = new StringBuilder();
                sb.Append(m.Name).Append('(');
                for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType.Name).Append(' ').Append(ps[i].Name); }
                sb.Append(')');
                if (m.ReturnType != typeof(void)) sb.Append(" → ").Append(m.ReturnType.Name);
                sigs.Add(sb.ToString());
            }
        return sigs;
    }

    // live.call(list=true): yolun sonundaki nesne (metot listesi icin).
    internal static object ResolveObject(object rootObj, string expr)
    {
        object cur = rootObj;
        string path = rootObj.GetType().Name;
        foreach (var seg in ParseMembers(expr))
        {
            if (cur == null)
                throw new RpcError($"'{path}' null; '{seg}' okunamaz");
            if (seg.IsIndex) { cur = Index(cur, seg.Name, path); path += "[" + seg.Name + "]"; }
            else { cur = ReadInstance(cur, seg.Name, path, out _); path += "." + seg.Name; }
        }
        return cur;
    }

    // live.call: 'A.B.Method' yolunu kok nesneden yurur, son segmenti METOT olarak cagirir. args kanonik doc skalerleri
    // (sayi/metin/bool/enum adi/'x y z'/'#rrggbbaa'); parametre sayisi eslesen tek overload; tip donusumu parametreye gore.
    // Donus: Render(sonuc) | void | Task (bloklanmaz: durum raporlanir). Istisna → RpcError (ic mesaj).
    internal static DocNode CallFrom(object rootObj, string expr, List<DocNode> args, int depth, int max, out string resolvedPath)
    {
        var segs = ParseMembers(expr ?? "");
        if (segs.Count == 0 || segs[^1].IsIndex)
            throw new RpcError("expr metot adiyla bitmeli: 'Ready', 'Svc.Deal', 'Hand[2].Flip'");
        object cur = rootObj;
        string path = rootObj.GetType().Name;
        for (int i = 0; i < segs.Count - 1; i++)
        {
            var seg = segs[i];
            if (cur == null)
                throw new RpcError($"'{path}' null; '{seg}' okunamaz");
            if (seg.IsIndex) { cur = Index(cur, seg.Name, path); path += "[" + seg.Name + "]"; }
            else { cur = ReadInstance(cur, seg.Name, path, out _); path += "." + seg.Name; }
        }
        if (cur == null)
            throw new RpcError($"'{path}' null; metot cagrilamaz");
        string name = segs[^1].Name;
        GuardSensitive(name, path);
        resolvedPath = path + "." + name;
        int argc = args?.Count ?? 0;
        MethodInfo found = null;
        var candidates = new List<MethodInfo>();
        for (var t = cur.GetType(); t != null && found == null; t = t.BaseType)
            foreach (var m in t.GetMethods(Inst))
            {
                if (m.Name != name || m.IsSpecialName) continue;
                candidates.Add(m);
                if (m.GetParameters().Length == argc && !m.IsGenericMethodDefinition) { found = m; break; }
            }
        if (found == null)
        {
            if (candidates.Count == 0)
            {
                // Alan/property ise soyle: AI yanlis arac sectiyse dogrusunu gostersin.
                for (var t = cur.GetType(); t != null; t = t.BaseType)
                    if (t.GetField(name, Inst) != null || t.GetProperty(name, Inst) != null)
                        throw new RpcError($"{path}.{name} bir ALAN/PROPERTY, metot degil — okumak icin live.get(expr='...')");
                throw new RpcError($"metot yok: {path}.{name} ({cur.GetType().Name}); metotlar: {string.Join(" | ", MethodSignatures(cur.GetType(), null, all: true))}");
            }
            throw new RpcError($"{path}.{name}: {argc} argumanli overload yok; mevcut: {string.Join(" | ", MethodSignatures(cur.GetType(), name))}");
        }
        var ps = found.GetParameters();
        var values = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
            values[i] = CoerceArg(args[i], ps[i].ParameterType, $"{name} arg{i} ({ps[i].Name})");
        object result;
        try { result = found.Invoke(cur, values); }
        catch (TargetInvocationException e)
        {
            var ie = e.InnerException ?? e;
            throw new RpcError($"{resolvedPath} istisna: {ie.GetType().Name}: {ie.Message}");
        }
        var r = DocNode.Map();
        if (found.ReturnType == typeof(void))
            r.Add("returned", DocNode.Scal("void"));
        else if (result is System.Threading.Tasks.Task task)
        {
            // Async metot: beklemeden donulur (frame'ler sim.run ile ilerler); tamamlanma durumu raporlanir.
            r.Add("async", DocNode.Scal("true"));
            r.Add("status", DocNode.Scal(task.Status.ToString()));
            if (task.IsFaulted) r.Add("error", DocNode.Scal(task.Exception?.GetBaseException().Message ?? ""));
        }
        else
            r.Add("returned", Render(result, found.ReturnType, depth, max, new Stack<object>(), isRoot: true));
        return r;
    }

    // Kanonik doc skaleri → parametre tipi. Tip bilinmeyen/karmasik (nesne) ise RpcError.
    static object CoerceArg(DocNode node, Type pt, string what)
    {
        string s = node?.Scalar;
        if (node != null && (node.IsMap || node.IsSeq))
            throw new RpcError($"{what}: skaler bekleniyor (sayi/metin/bool/'x y z'/'#rrggbbaa')");
        var u = Nullable.GetUnderlyingType(pt) ?? pt;
        if (s == null)
            return u.IsValueType && Nullable.GetUnderlyingType(pt) == null ? Activator.CreateInstance(u) : null;
        var ci = CultureInfo.InvariantCulture;
        try
        {
            if (u == typeof(string)) return s;
            if (u == typeof(bool)) return s == "true" || s == "1";
            if (u == typeof(int)) return int.Parse(s, ci);
            if (u == typeof(long)) return long.Parse(s, ci);
            if (u == typeof(short)) return short.Parse(s, ci);
            if (u == typeof(byte)) return byte.Parse(s, ci);
            if (u == typeof(float)) return float.Parse(s, NumberStyles.Float, ci);
            if (u == typeof(double)) return double.Parse(s, NumberStyles.Float, ci);
            if (u.IsEnum) return Enum.Parse(u, s, ignoreCase: true);
            if (u == typeof(Vec2)) return SerializedType.ParseVec2(s);
            if (u == typeof(Vec3)) return SerializedType.ParseVec3(s);
            if (u == typeof(Vec4)) return SerializedType.ParseVec4(s);
            if (u == typeof(Color)) return SerializedType.ParseColor(s);
        }
        catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
        {
            throw new RpcError($"{what}: '{s}' {u.Name} olarak okunamadi");
        }
        throw new RpcError($"{what}: {u.Name} tipi RPC'den verilemez (skaler/enum/Vec/Color desteklenir)");
    }

    // [Sensitive] uyeden GECIS yasak: 'LocalCache._tokens[7]' degeri 'value' anahtariyla donerdi ve maske (alan adi bazli) kacardi.
    static void GuardSensitive(string member, string path)
    {
        if (RpcRedact.IsSensitiveKey(member))
            throw new RpcError($"{path}.{member} [Sensitive]: bu alan AI'a acik degil (degerini okuma/tahmin etme)");
    }

    static object Index(object target, string key, string path)
    {
        if (target is IDictionary dict)
        {
            Type keyType = typeof(object);
            foreach (var it in target.GetType().GetInterfaces())
                if (it.IsGenericType && it.GetGenericTypeDefinition() == typeof(IDictionary<,>)) { keyType = it.GetGenericArguments()[0]; break; }
            object k;
            try { k = ConvertKey(key, keyType); }
            catch (Exception) { throw new RpcError($"{path}[{key}]: anahtar {keyType.Name} tipine cevrilemedi"); }
            if (!dict.Contains(k))
                throw new RpcError($"{path}[{key}]: anahtar yok (mem.dump('{path}') ile anahtarlari gor)");
            return dict[k];
        }
        if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 0)
            throw new RpcError($"{path}[{key}]: index negatif olmayan tamsayi olmali");
        if (target is IList list)
        {
            if (idx >= list.Count)
                throw new RpcError($"{path}[{idx}]: sinir disi (Count={list.Count})");
            return list[idx];
        }
        if (target is IEnumerable en && target is not string)
        {
            int i = 0;
            foreach (var item in en)
                if (i++ == idx) return item;
            throw new RpcError($"{path}[{idx}]: sinir disi ({i} eleman)");
        }
        throw new RpcError($"{path}: indekslenemez ({target.GetType().Name})");
    }

    static object ConvertKey(string key, Type keyType)
    {
        if (keyType == typeof(object) || keyType == typeof(string)) return key;
        if (keyType.IsEnum) return Enum.Parse(keyType, key, ignoreCase: false);
        return Convert.ChangeType(key, keyType, CultureInfo.InvariantCulture);
    }

    // ── deger dokumu ─────────────────────────────────────────────────────────
    // Uye adi ANAHTAR (liste degil): [Sensitive] maskesi (RpcRedact, alan adi bazli) girdinin tamamini kapatir.
    static DocNode StaticMembers(Type type)
    {
        var map = DocNode.Map();
        var seen = new HashSet<string>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(Stat))
            {
                if (IsBackingField(f) || !seen.Add(f.Name)) continue;
                var m = DocNode.Map();
                m.Add("type", DocNode.Scal(DisplayName(f.FieldType)));
                object v;
                try { v = f.GetValue(null); } catch (Exception e) { v = e; }
                m.Add("value", v is Exception ex ? DocNode.Scal("!" + ex.GetType().Name + ": " + ex.Message) : Summary(v, f.FieldType));
                map.Add((f.IsPublic ? "" : "~") + f.Name, m);
            }
            foreach (var p in t.GetProperties(Stat))
            {
                if (p.GetMethod == null || p.GetIndexParameters().Length > 0 || !seen.Add(p.Name)) continue;
                var m = DocNode.Map();
                m.Add("type", DocNode.Scal(DisplayName(p.PropertyType)));
                m.Add("value", SafeGet(() => p.GetValue(null), p.PropertyType));
                map.Add(p.Name, m);
            }
        }
        return map;
    }

    static DocNode SafeGet(Func<object> get, Type declared)
    {
        try { return Summary(get(), declared); }
        catch (TargetInvocationException e) { return DocNode.Scal("!" + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message); }
        catch (Exception e) { return DocNode.Scal("!" + e.GetType().Name + ": " + e.Message); }
    }

    // Tek satir ozet (depth bitti / koleksiyon basligi / motor nesnesi).
    static DocNode Summary(object v, Type declared)
    {
        if (v == null) return DocNode.Scal("null");
        if (IsScalar(v)) return DocNode.Scal(ScalarText(v));
        var t = v.GetType();
        if (v is string) return DocNode.Scal((string)v);
        if (v is IDictionary d) return DocNode.Scal("{" + DisplayName(t) + "}[" + d.Count + "]");
        if (v is ICollection c) return DocNode.Scal("{" + DisplayName(t) + "}[" + c.Count + "]");
        if (v is IEnumerable) return DocNode.Scal("{" + DisplayName(t) + "}[…]");
        return DocNode.Scal(EngineSummary(v) ?? "{" + DisplayName(t) + "}");
    }

    static DocNode Render(object v, Type declared, int depth, int max, Stack<object> stack, bool isRoot)
    {
        if (v == null) return DocNode.Scal("null");
        if (IsScalar(v)) return DocNode.Scal(ScalarText(v));
        if (v is string s) return DocNode.Scal(s);
        var t = v.GetType();
        if (v is Delegate del)
        {
            // Hook teshisi: kim bagli? Target tipi + metot (lambda ise derleyici adi).
            var parts = new List<string>();
            foreach (var d in del.GetInvocationList())
                parts.Add((d.Target != null ? DisplayName(d.Target.GetType()) : d.Method.DeclaringType?.Name ?? "?") + "." + d.Method.Name);
            return DocNode.Scal("{" + DisplayName(t) + "} → " + (parts.Count > 0 ? string.Join(", ", parts) : "(bos)"));
        }
        if (v is Type || v is MemberInfo || v is System.Threading.Tasks.Task)
            return DocNode.Scal("{" + DisplayName(t) + "}");
        if (!isRoot && EngineSummary(v) is string es)
            return DocNode.Scal(es);
        if (depth <= 0)
            return Summary(v, declared);
        if (!t.IsValueType)
        {
            foreach (var o in stack)
                if (ReferenceEquals(o, v)) return DocNode.Scal("{" + DisplayName(t) + "} (cycle)");
        }
        stack.Push(v);
        try
        {
            if (v is IDictionary dict)
            {
                var m = DocNode.Map();
                m.Add("$type", DocNode.Scal(DisplayName(t) + "[" + dict.Count + "]"));
                int i = 0;
                foreach (DictionaryEntry e in dict)
                {
                    if (i++ >= max) { m.Add("…", DocNode.Scal("+" + (dict.Count - max))); break; }
                    m.Add(e.Key is null ? "null" : ScalarOrSummary(e.Key), Render(e.Value, null, depth - 1, max, stack, false));
                }
                return m;
            }
            if (v is IEnumerable en)
            {
                var seq = DocNode.Seq();
                int i = 0, total = v is ICollection c ? c.Count : -1;
                foreach (var item in en)
                {
                    if (i++ >= max) { seq.Items.Add(DocNode.Scal("…+" + (total >= 0 ? (total - max).ToString(CultureInfo.InvariantCulture) : "?"))); break; }
                    seq.Items.Add(Render(item, null, depth - 1, max, stack, false));
                }
                return seq;
            }
            // Struct'lar: ToString override'i varsa (Vector3, Color...) kompakt metin.
            if (t.IsValueType && HasToStringOverride(t))
                return DocNode.Scal(Convert.ToString(v, CultureInfo.InvariantCulture));
            var obj = DocNode.Map();
            obj.Add("$type", DocNode.Scal(DisplayName(t)));
            var seen = new HashSet<string>();
            for (var tt = t; tt != null && tt != typeof(object) && tt != typeof(ValueType); tt = tt.BaseType)
            {
                foreach (var f in tt.GetFields(Inst))
                {
                    if (IsBackingField(f) || typeof(Delegate).IsAssignableFrom(f.FieldType) || !seen.Add(f.Name)) continue;
                    object fv;
                    try { fv = f.GetValue(v); } catch (Exception e) { obj.Add(f.Name, DocNode.Scal("!" + e.GetType().Name)); continue; }
                    obj.Add((f.IsPublic ? "" : "~") + f.Name, Render(fv, f.FieldType, depth - 1, max, stack, false));
                }
                foreach (var p in tt.GetProperties(Inst))
                {
                    if (p.GetMethod == null || p.GetIndexParameters().Length > 0 || !p.GetMethod.IsPublic || !seen.Add(p.Name)) continue;
                    if (typeof(Delegate).IsAssignableFrom(p.PropertyType)) continue;
                    object pv;
                    try { pv = p.GetValue(v); }
                    catch (TargetInvocationException e) { obj.Add(p.Name, DocNode.Scal("!" + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message)); continue; }
                    catch (Exception e) { obj.Add(p.Name, DocNode.Scal("!" + e.GetType().Name + ": " + e.Message)); continue; }
                    obj.Add(p.Name, Render(pv, p.PropertyType, depth - 1, max, stack, false));
                }
            }
            return obj;
        }
        finally { stack.Pop(); }
    }

    static string ScalarOrSummary(object k) => IsScalar(k) || k is string ? ScalarText(k) : Summary(k, null).Scalar;

    // Motor nesneleri sahneyi dokmesin: tek satir kimlik. Kok ifade motor nesnesiyse (kullanici bilerek istedi) acilir.
    static string EngineSummary(object v)
    {
        switch (v)
        {
            case GameObject go: return "{GameObject '" + go.name + "'}";
            case Component c: return "{" + c.GetType().Name + " on '" + (c.gameObject?.name ?? "?") + "'}";
        }
        var t = v.GetType();
        if (t.Assembly == typeof(GameObject).Assembly && !t.IsValueType && v is not IEnumerable)
            return "{" + DisplayName(t) + "}";
        return null;
    }

    static bool IsScalar(object v)
        => v is bool || v is char || v is sbyte || v is byte || v is short || v is ushort || v is int || v is uint || v is long || v is ulong
        || v is float || v is double || v is decimal || v is Enum || v is DateTime || v is DateTimeOffset || v is TimeSpan || v is Guid || v is IntPtr || v is UIntPtr;

    static string ScalarText(object v) => v switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "null",
    };

    static bool HasToStringOverride(Type t)
    {
        var m = t.GetMethod("ToString", Type.EmptyTypes);
        return m != null && m.DeclaringType != typeof(object) && m.DeclaringType != typeof(ValueType);
    }

    static bool IsBackingField(FieldInfo f) => f.Name.Length > 0 && f.Name[0] == '<';

    static string Kind(Type t)
        => t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : t.IsAbstract && t.IsSealed ? "static" : "class";

    static string DisplayName(Type t)
    {
        if (t == null) return "?";
        if (t.IsNested && !t.IsGenericParameter)
            return DisplayName(t.DeclaringType) + "." + Simple(t);
        return Simple(t);

        static string Simple(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            var sb = new StringBuilder();
            int tick = t.Name.IndexOf('`');
            sb.Append(tick >= 0 ? t.Name.Substring(0, tick) : t.Name).Append('<');
            var args = t.GetGenericArguments();
            for (int i = 0; i < args.Length; i++) { if (i > 0) sb.Append(','); sb.Append(DisplayName(args[i])); }
            return sb.Append('>').ToString();
        }
    }

    // Testler icin dogrudan erisim (RPC katmanini atlar).
    internal static DocNode DumpForTest(string expr, int depth = 2, int max = 50) => Dump(expr, depth, max);
    internal static DocNode StaticsForTest(string type) => Statics(type);
    internal static DocNode TypesForTest(string filter, string scope) => Types(filter, scope);
}
