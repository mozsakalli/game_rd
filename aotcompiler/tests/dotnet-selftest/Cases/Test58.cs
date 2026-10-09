using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Demo58
{
    // Faz 4c: BindingFlags secimi (.NET kurallari: kalitilan private asla, kalitilan static yalniz FlattenHierarchy,
    // DeclaredOnly), GetMethod(name, flags, binder, Type[], mods) override tespiti, acik generic tanim
    // (IsGenericType / GetGenericTypeDefinition == typeof(List<>)), Type.Assembly / Assembly.GetTypes, List<T> : IList,
    // Activator.CreateInstance(Type, nonPublic), AppDomain.GetAssemblies.
    class Base58
    {
        public int pubBase;
        int privBase;
        protected int protBase;
        public static int statBase;
        public virtual void Tick() { privBase++; }
        public virtual void Ping(int x) { pubBase = x; }
        public int Prop { get; set; }
        private int Hidden { get; set; }
        public static int SProp { get; set; }
    }
    class Derived58 : Base58
    {
        public float pubDer;
        [NonSerialized] int privDer = 1;
        public static int statDer;
        public override void Tick() { pubDer += 1; protBase++; }
        public void Own() { }
        public int Peek() { return privDer; }
    }
    class Quiet58
    {
        Quiet58() { Value = 7; }
        public int Value;
    }
    class App58
    {
        public static int Run()
        {
            long acc = 0;
            Type d = typeof(Derived58);
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // GetFields default: public instance + public static (bildirilen) + kalitilan public instance; kalitilan static YOK
            FieldInfo[] def = d.GetFields();
            if (Has(def, "pubDer") && Has(def, "statDer") && Has(def, "pubBase") && !Has(def, "statBase") && !Has(def, "privDer")) acc |= 1;
            FieldInfo[] all = d.GetFields(All);
            if (Has(all, "privDer") && Has(all, "protBase") && !Has(all, "privBase") && !Has(all, "statDer")) acc |= 2;     // kalitilan private yok; static istenmedi
            FieldInfo[] decl = d.GetFields(All | BindingFlags.DeclaredOnly);
            if (Has(decl, "privDer") && Has(decl, "pubDer") && !Has(decl, "pubBase") && !Has(decl, "protBase")) acc |= 4;
            FieldInfo[] flat = d.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (Has(flat, "statBase") && Has(flat, "statDer")) acc |= 8;
            if (d.GetField("statBase") == null && typeof(Base58).GetField("statBase") != null) acc |= 16;
            if (d.GetField("privDer") == null && d.GetField("privDer", All) != null) acc |= 32;
            FieldInfo pd = d.GetField("privDer", All);
            if (pd != null && pd.IsPrivate && pd.IsNotSerialized && pd.DeclaringType == d) acc |= 64;

            // GetMethod(name, flags, binder, Type[], mods): override tespiti (DeclaringType)
            MethodInfo tick = d.GetMethod("Tick", All, null, Type.EmptyTypes, null);
            MethodInfo ping = d.GetMethod("Ping", All, null, new[] { typeof(int) }, null);
            if (tick != null && tick.DeclaringType == d && ping != null && ping.DeclaringType == typeof(Base58)) acc |= 128;
            if (d.GetMethod("Ping", All, null, Type.EmptyTypes, null) == null) acc |= 256;     // parametre uyusmaz
            MethodInfo[] ms = d.GetMethods(All);
            int tickCount = 0;
            foreach (var m in ms) if (m.Name == "Tick") tickCount++;
            if (tickCount == 1 && HasM(ms, "Own") && HasM(ms, "Ping")) acc |= 512;              // override gizler, kalitilan gorunur

            // property'ler
            PropertyInfo[] ps = d.GetProperties(All);
            if (HasP(ps, "Prop") && !HasP(ps, "Hidden") && !HasP(ps, "SProp")) acc |= 1024;      // kalitilan private/static yok
            if (d.GetProperty("Prop") != null && d.GetProperty("SProp") == null && typeof(Base58).GetProperty("SProp") != null) acc |= 2048;

            // acik generic tanim
            Type lt = typeof(List<int>);
            if (lt.IsGenericType && !lt.IsGenericTypeDefinition && lt.GetGenericTypeDefinition() == typeof(List<>)) acc |= 4096;
            if (typeof(List<>).IsGenericTypeDefinition && typeof(List<>).IsGenericType && !typeof(Derived58).IsGenericType) acc |= 8192;
            if (lt.GetGenericArguments().Length == 1 && lt.GetGenericArguments()[0] == typeof(int)) acc |= 16384;
            if (typeof(Dictionary<string, int>).GetGenericTypeDefinition() == typeof(Dictionary<,>)) acc |= 32768;

            // Assembly
            Assembly asm = d.Assembly;
            if (asm != null && asm == typeof(Base58).Assembly) acc |= 65536;
            Type[] types = asm.GetTypes();
            bool sawD = false, sawArr = false;
            foreach (var t in types) { if (t == d) sawD = true; if (t.IsArray) sawArr = true; }
            if (sawD && !sawArr && types.Length > 10) acc |= 131072;
            if (AppDomain.CurrentDomain.GetAssemblies().Length >= 1) acc |= 262144;

            // List<T> : IList (boxing yolu)
            object lo = Activator.CreateInstance(typeof(List<int>));
            IList il = lo as IList;
            if (il != null) { il.Add(5); il.Add(9); il[0] = 6; }
            if (il != null && il.Count == 2 && (int)il[0] == 6 && (int)il[1] == 9 && ((List<int>)lo)[1] == 9) acc |= 524288;
            if (il != null && il.IndexOf(9) == 1 && il.Contains(6) && !il.Contains(7)) acc |= 1048576;
            int sum = 0;
            foreach (object o in (IEnumerable)lo) sum += (int)o;
            if (sum == 15) acc |= 2097152;

            // Activator(nonPublic)
            object q = Activator.CreateInstance(typeof(Quiet58), true);
            if (q is Quiet58 qq && qq.Value == 7) acc |= 4194304;

            return (int)acc;
        }
        static bool Has(FieldInfo[] a, string n) { foreach (var f in a) if (f.Name == n) return true; return false; }
        static bool HasM(MethodInfo[] a, string n) { foreach (var m in a) if (m.Name == n) return true; return false; }
        static bool HasP(PropertyInfo[] a, string n) { foreach (var p in a) if (p.Name == n) return true; return false; }
    }
}
