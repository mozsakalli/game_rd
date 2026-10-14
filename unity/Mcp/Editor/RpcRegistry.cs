using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;

namespace Digitoy.Mcp
{
    // AI/otomasyon icin RPC komutu: static metodu isimle disari acar. Parametreler JSON args'tan
    // ada gore baglanir (int/float/bool/string/JNode; default degerli = opsiyonel). Proje kodu da
    // kendi [RpcCommand]'ini ekleyebilir (TypeCache tum assembly'leri tarar).
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RpcCommandAttribute : Attribute
    {
        public readonly string Name;
        public readonly string Help;

        public RpcCommandAttribute(string name, string help = "")
        {
            Name = name;
            Help = help;
        }
    }

    // Kullaniciya/AI'a donecek beklenen hatalar (stack trace'siz temiz mesaj).
    public sealed class RpcError : Exception
    {
        public RpcError(string message) : base(message) { }
    }

    public static class RpcRegistry
    {
        public sealed class Entry
        {
            public string Name;
            public string Help;
            public MethodInfo Method;
            public ParameterInfo[] Params;
        }

        static readonly Dictionary<string, Entry> _byName = new Dictionary<string, Entry>();
        static bool _built;

        static void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _byName.Clear();
            foreach (var m in TypeCache.GetMethodsWithAttribute<RpcCommandAttribute>())
            {
                if (!m.IsStatic) continue;
                var attr = m.GetCustomAttribute<RpcCommandAttribute>();
                _byName[attr.Name] = new Entry { Name = attr.Name, Help = attr.Help, Method = m, Params = m.GetParameters() };
            }
        }

        public static Entry Find(string name)
        {
            EnsureBuilt();
            return _byName.TryGetValue(name, out var e) ? e : null;
        }

        // Tum komutlarin makine-okunur semasi (MCP koprusu bunu tool listesine cevirir).
        public static JNode Describe()
        {
            EnsureBuilt();
            var root = JNode.Map();
            var names = new List<string>(_byName.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var e = _byName[name];
                var cmd = JNode.Map();
                if (e.Help.Length > 0)
                    cmd.Add("help", e.Help);
                var ps = JNode.Seq();
                foreach (var p in e.Params)
                {
                    var pd = JNode.Map();
                    pd.Add("name", p.Name);
                    pd.Add("type", TypeLabel(p.ParameterType));
                    if (p.HasDefaultValue)
                        pd.Add("default", Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) ?? "null");
                    ps.Push(pd);
                }
                cmd.Add("params", ps);
                root.Add(name, cmd);
            }
            return root;
        }

        static string TypeLabel(Type t) => t == typeof(int) ? "int"
            : t == typeof(float) ? "float"
            : t == typeof(bool) ? "bool"
            : t == typeof(string) ? "string"
            : t == typeof(JNode) ? "node"
            : t.Name;

        public static JNode Invoke(Entry e, JNode args)
        {
            var values = new object[e.Params.Length];
            for (int i = 0; i < e.Params.Length; i++)
            {
                var p = e.Params[i];
                var node = args != null && args.IsMap ? args.Get(p.Name) : null;
                if (node != null && !(node.IsNull && p.ParameterType != typeof(JNode)))
                {
                    values[i] = BindArg(node, p.ParameterType, p.Name);
                    continue;
                }
                if (!p.HasDefaultValue)
                    throw new RpcError($"eksik parametre: '{p.Name}'");
                values[i] = p.DefaultValue;
            }
            try
            {
                return (JNode)e.Method.Invoke(null, values);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        static object BindArg(JNode n, Type t, string name)
        {
            if (t == typeof(JNode))
                return n.IsNull ? null : n.Clone();
            if (!n.IsScalar)
                throw new RpcError($"parametre '{name}' skaler olmali ({TypeLabel(t)})");
            try
            {
                if (t == typeof(string)) return n.Str;
                if (t == typeof(int)) return (int)Math.Round(double.Parse(n.Str, NumberStyles.Float, CultureInfo.InvariantCulture));
                if (t == typeof(float)) return float.Parse(n.Str, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (t == typeof(bool))
                    return n.Str == "true" ? true : n.Str == "false" ? false : throw new FormatException();
            }
            catch (Exception ex) when (!(ex is RpcError))
            {
                throw new RpcError($"parametre '{name}' {TypeLabel(t)} degil: {n.Str}");
            }
            throw new RpcError($"desteklenmeyen parametre tipi: {t.Name} ('{name}')");
        }
    }
}
