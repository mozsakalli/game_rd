using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Digitoy.Mcp
{
    public static partial class Commands
    {
        static readonly HashSet<string> BatchAllowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "scene.addGo", "scene.removeGo", "scene.rename", "scene.reparent", "scene.apply",
            "comp.add", "comp.remove", "comp.setEnabled", "prop.set", "transform.set",
            "go.setActive", "go.setLayer", "select.set", "prefab.instantiate",
        };

        static int _batchDepth;

        // Tek undo grubu + hata halinde geri alma. Ic ice cagri (apply icinde batch) dis gruba katilir.
        static void RunBatch(Action body, string name)
        {
            if (_batchDepth > 0) { body(); return; }
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("MCP " + name);
            int group = Undo.GetCurrentGroup();
            _batchDepth++;
            try
            {
                body();
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
            finally { _batchDepth--; }
        }

        [RpcCommand("edit.undo", "Geri al")]
        static JNode EditUndo() { Undo.PerformUndo(); return JNode.Map().Add("ok", true); }

        [RpcCommand("edit.redo", "Yinele")]
        static JNode EditRedo() { Undo.PerformRedo(); return JNode.Map().Add("ok", true); }

        [RpcCommand("edit.batch", "Birden cok mutasyon komutunu TEK istekte, TEK undo adiminda, ATOMIK kosar (hata → hepsi geri alinir). ops: [{cmd, args, as?}] — izinli: scene.addGo/removeGo/rename/reparent/apply, comp.add/remove/setEnabled, prop.set, transform.set, go.setActive/setLayer, select.set, prefab.instantiate. as='btn' sonucu adlandirir; sonraki args'ta '$btn' → o sonucun id'si (comp.add icin index), '$btn.alan' → alan, '$btn:Image:0' → CompRef. Donus: op sonuclari sirayla")]
        static JNode EditBatch(JNode ops)
        {
            if (ops == null || !ops.IsSeq)
                throw new RpcError("ops bir liste olmali: [{cmd, args, as?}]");
            var refs = new Dictionary<string, JNode>(StringComparer.Ordinal);
            var results = JNode.Seq();
            int at = 0;
            string cur = "";
            try
            {
                RunBatch(() =>
                {
                    for (at = 0; at < ops.Items.Count; at++)
                    {
                        var op = ops.Items[at];
                        cur = op.IsMap ? op.GetScalar("cmd", null) : null;
                        if (string.IsNullOrEmpty(cur)) throw new RpcError("op 'cmd' alani icermiyor");
                        if (!BatchAllowed.Contains(cur)) throw new RpcError($"'{cur}' batch icinde kosamaz");
                        var entry = RpcRegistry.Find(cur) ?? throw new RpcError($"bilinmeyen komut: '{cur}'");
                        var args = op.Get("args");
                        if (args != null && !args.IsMap) throw new RpcError("'args' bir nesne olmali");
                        args = args == null ? JNode.Map() : SubstituteRefs(args.Clone(), s => ResolveBatchRef(s, refs));
                        var result = RpcRegistry.Invoke(entry, args) ?? JNode.Map();
                        string alias = op.GetScalar("as", null);
                        if (!string.IsNullOrEmpty(alias)) refs[alias] = result;
                        results.Push(result);
                    }
                }, "batch");
            }
            catch (RpcError ex)
            {
                throw new RpcError($"ops[{at}] {cur}: {ex.Message} — batch geri alindi, hicbir degisiklik uygulanmadi");
            }
            catch (Exception ex)
            {
                throw new RpcError($"ops[{at}] {cur}: {ex.GetType().Name}: {ex.Message} — batch geri alindi, hicbir degisiklik uygulanmadi");
            }
            return results;
        }

        // '$ad' → sonucun id'si (yoksa index'i); '$ad.alan' → alan; '$ad:Tip:0' → CompRef. Alias yoksa sahnedeki tekil GO adi.
        static string ResolveBatchRef(string s, Dictionary<string, JNode> refs)
        {
            int end = s.IndexOfAny(new[] { '.', ':' }, 1);
            string name = end < 0 ? s.Substring(1) : s.Substring(1, end - 1);
            if (!refs.TryGetValue(name, out var r))
            {
                if (end >= 0 && s[end] == '.')
                    throw new RpcError($"'{s}': '{name}' adli onceki sonuc yok (as= ile adlandir)");
                return ResolveGoName(s, name, end < 0 ? "" : s.Substring(end), null);
            }
            string field = end >= 0 && s[end] == '.' ? s.Substring(end + 1) : null;
            string tail = end >= 0 && s[end] == ':' ? s.Substring(end) : "";
            JNode v = field != null ? r.Get(field) : (r.Get("id") ?? r.Get("index"));
            if (v == null || !v.IsScalar)
                throw new RpcError($"'{s}': '{name}' sonucunda {(field ?? "id/index")} alani yok");
            return v.Str + tail;
        }

        // '$Ad' → apply'da adlandirilan GO; yoksa sahnede o adla TEK GO.
        static string ResolveApplyRef(string s, Dictionary<string, int> named)
        {
            int end = s.IndexOf(':', 1);
            string name = end < 0 ? s.Substring(1) : s.Substring(1, end - 1);
            string tail = end < 0 ? "" : s.Substring(end);
            if (named.TryGetValue(name, out int id))
                return id.ToString(CultureInfo.InvariantCulture) + tail;
            return ResolveGoName(s, name, tail, named);
        }

        static string ResolveGoName(string s, string name, string tail, Dictionary<string, int> named)
        {
            GameObject hit = null;
            int count = 0;
            foreach (var g in AllGos())
                if (g.name == name) { hit = g; count++; }
            if (count == 0) throw new RpcError($"'{s}': '{name}' adli GO yok" + (named != null ? " (bu apply'da da tanimlanmadi)" : ""));
            if (count > 1) throw new RpcError($"'{s}': '{name}' adli {count} GO var, belirsiz — id kullan");
            return hit.GetInstanceID().ToString(CultureInfo.InvariantCulture) + tail;
        }

        static JNode SubstituteRefs(JNode n, Func<string, string> resolve)
        {
            if (n == null) return null;
            if (n.Kind == JNode.K.String && n.Str.Length > 1 && n.Str[0] == '$')
                n.Str = resolve(n.Str);
            if (n.Items != null) foreach (var it in n.Items) SubstituteRefs(it, resolve);
            if (n.Fields != null) foreach (var kv in n.Fields) SubstituteRefs(kv.Value, resolve);
            return n;
        }

        [RpcCommand("scene.apply", "Bildirimsel alt agac: GO'lari component+prop+cocuklariyla TEK istekte kurar/gunceller (tek undo, atomik). objects: [{name, id?, parent?, insertBefore?, active?, layer?, ui?, prefab?, pos, rot, scale, anchoredPos, size, anchorMin, anchorMax, pivot, components:[{type, index?, enabled?, props:{Alan: deger, 'Tracks[0].Path': deger}}], children:[...]}]. Prop null = default/sil. id verilirse MEVCUT GO guncellenir (component: index varsa o, yoksa ayni tipten ilki, o da yoksa eklenir); prefab='Assets/x.prefab' → prefab instance. '$Ad' = bu apply'da once gecen GO'nun id'si ('$Ad:Image:0' CompRef). Donus: {name,id,components:[index..],children:[..]}")]
        static JNode SceneApply(JNode objects, int parent = 0)
        {
            if (objects == null) throw new RpcError("objects gerekli");
            if (objects.IsMap) { var one = JNode.Seq(); one.Push(objects); objects = one; }
            if (!objects.IsSeq) throw new RpcError("objects bir liste (ya da tek nesne) olmali");
            if (parent != 0) RequireGo(parent);
            var named = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = JNode.Seq();
            try
            {
                RunBatch(() =>
                {
                    for (int i = 0; i < objects.Items.Count; i++)
                        result.Push(ApplyGo(objects.Items[i], parent, named, $"objects[{i}]"));
                }, "apply");
            }
            catch (RpcError ex)
            {
                throw new RpcError(ex.Message + " — apply geri alindi, hicbir degisiklik uygulanmadi");
            }
            return result;
        }

        static JNode ApplyGo(JNode spec, int defaultParent, Dictionary<string, int> named, string where)
        {
            if (spec == null || !spec.IsMap) throw new RpcError($"{where}: GO tanimi bir nesne olmali");
            try
            {
                string RefStr(string s) => s.StartsWith("$", StringComparison.Ordinal) ? ResolveApplyRef(s, named) : s;
                int RefInt(string key, int def)
                {
                    string s = spec.GetScalar(key, null);
                    if (string.IsNullOrEmpty(s)) return def;
                    return ParseInt(RefStr(s), key);
                }

                int parentId = RefInt("parent", defaultParent);
                if (parentId != 0) RequireGo(parentId);
                int insertBefore = RefInt("insertBefore", 0);
                string name = spec.GetScalar("name", "");
                bool ui = spec.GetScalar("ui", "false") == "true";

                GameObject g;
                int existing = RefInt("id", 0);
                if (existing != 0)
                {
                    g = RequireGo(existing);
                    if (spec.Get("parent") != null || spec.Get("insertBefore") != null)
                        SceneReparent(existing, parentId, insertBefore);
                }
                else if (spec.GetScalar("prefab", null) is string prefabPath && prefabPath.Length > 0)
                {
                    var r0 = PrefabInstantiate(prefabPath, parentId, name);
                    g = RequireGo(ParseInt(r0.GetScalar("id", "0"), "id"));
                    if (insertBefore != 0) PlaceBefore(g, insertBefore);
                }
                else
                {
                    // Parent RectTransform ise cocuk da UI olsun (aksi istenmedikce).
                    if (spec.Get("ui") == null && parentId != 0 && RequireGo(parentId).transform is RectTransform) ui = true;
                    var r0 = SceneAddGo(name.Length > 0 ? name : "GameObject", parentId, insertBefore, ui);
                    g = RequireGo(ParseInt(r0.GetScalar("id", "0"), "id"));
                }
                if (name.Length > 0)
                {
                    if (g.name != name) SceneRename(g.GetInstanceID(), name);
                    named[name] = g.GetInstanceID();
                }
                if (spec.Get("active") is JNode act && act.IsScalar) GoSetActive(g.GetInstanceID(), ParseBool(act.Str, "active"));
                if (spec.GetScalar("layer", null) is string layer) GoSetLayer(g.GetInstanceID(), layer);

                string pos = spec.GetScalar("pos", ""), rot = spec.GetScalar("rot", ""), scale = spec.GetScalar("scale", "");
                string ap = spec.GetScalar("anchoredPos", ""), size = spec.GetScalar("size", "");
                string amin = spec.GetScalar("anchorMin", ""), amax = spec.GetScalar("anchorMax", ""), pivot = spec.GetScalar("pivot", "");
                if (pos.Length + rot.Length + scale.Length + ap.Length + size.Length + amin.Length + amax.Length + pivot.Length > 0)
                    TransformSet(g.GetInstanceID(), pos, rot, scale, ap, size, amin, amax, pivot);

                var r = JNode.Map();
                r.Add("name", g.name);
                r.Add("id", g.GetInstanceID());

                if (spec.Get("components") is JNode comps)
                {
                    if (!comps.IsSeq) throw new RpcError("'components' bir liste olmali");
                    var outComps = JNode.Seq();
                    for (int ci = 0; ci < comps.Items.Count; ci++)
                    {
                        var cs = comps.Items[ci];
                        if (cs == null || !cs.IsMap) throw new RpcError($"components[{ci}] bir nesne olmali");
                        string type = cs.GetScalar("type", "");
                        Component cd;
                        if (cs.GetScalar("index", null) is string idxS)
                        {
                            cd = RequireComp(g, ParseInt(idxS, $"components[{ci}].index"));
                            if (type.Length > 0 && cd.GetType() != RequireCompType(type))
                                throw new RpcError($"components[{ci}]: index {idxS} '{TypeName(cd.GetType())}' tipinde, '{type}' degil");
                        }
                        else
                        {
                            if (type.Length == 0) throw new RpcError($"components[{ci}]: 'type' (ya da 'index') gerekli");
                            var t = RequireCompType(type);
                            cd = existing != 0 || t == typeof(Transform) || t == typeof(RectTransform) ? g.GetComponent(t) : null;
                            if (cd == null)
                            {
                                var ra = CompAdd(g.GetInstanceID(), type);
                                cd = RequireComp(g, ParseInt(ra.GetScalar("index", "0"), "index"));
                            }
                        }
                        if (cs.Get("enabled") is JNode en && en.IsScalar)
                            CompSetEnabled(g.GetInstanceID(), CompIndex(cd), ParseBool(en.Str, "enabled"));
                        if (cs.Get("props") is JNode props)
                        {
                            if (!props.IsMap) throw new RpcError($"components[{ci}].props bir nesne olmali");
                            foreach (var kv in props.Fields)
                            {
                                try
                                {
                                    PropIO.Set(cd, kv.Key, kv.Value.IsNull ? null : SubstituteRefs(kv.Value.Clone(), s => ResolveApplyRef(s, named)));
                                }
                                catch (RpcError ex)
                                {
                                    throw new RpcError($"components[{ci}] {TypeName(cd.GetType())}.{kv.Key}: {ex.Message}");
                                }
                            }
                        }
                        outComps.Push(JNode.Num(CompIndex(cd)));
                    }
                    r.Add("components", outComps);
                }

                if (spec.Get("children") is JNode children)
                {
                    if (!children.IsSeq) throw new RpcError("'children' bir liste olmali");
                    var outChildren = JNode.Seq();
                    for (int k = 0; k < children.Items.Count; k++)
                        outChildren.Push(ApplyGo(children.Items[k], g.GetInstanceID(), named, $"children[{k}]"));
                    r.Add("children", outChildren);
                }
                MarkDirty();
                return r;
            }
            catch (RpcError ex) when (!ex.Message.StartsWith(where, StringComparison.Ordinal))
            {
                throw new RpcError($"{where}: {ex.Message}");
            }
        }
    }
}
