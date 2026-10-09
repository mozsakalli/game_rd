using System;
using System.Reflection;
namespace Demo32
{
    public class Base32
    {
        public int baseValue;
        public readonly int frozen = 3;
        public int score;
        public Node32 friend;
        public static int staticValue;
        public static int Global { get; set; }
        public int Auto { get; set; }
        public int ReadOnly { get { return 1; } }
        public virtual int Score { get { return score; } set { score = value; } }
        public Node32 Friend { get { return friend; } set { friend = value; } }
    }
    public class Derived32 : Base32
    {
        int overrideScore;
        public override int Score { get { return overrideScore * 10; } set { overrideScore = value; } }
    }
    public class Node32 { }
    class App32
    {
        public static int Run()
        {
            int acc = 0;
            Derived32 d = new Derived32();
            FieldInfo f = typeof(Derived32).GetField("baseValue");
            if (f == typeof(Derived32).GetField("baseValue")) { acc += 1; }
            if (f.Name == "baseValue" && f.DeclaringType == typeof(Base32)) { acc += 2; }
            if (f.FieldType == typeof(int) && !f.IsStatic && !f.IsInitOnly) { acc += 4; }
            f.SetValue(d, 13);
            if ((int)f.GetValue(d) == 13) { acc += 8; }
            FieldInfo sf = typeof(Base32).GetField("staticValue"); // .NET: kalitilan static alan FlattenHierarchy ister -> bildiren tipten
            if (sf.IsStatic) { acc += 16; }
            object noTarget = null;
            sf.SetValue(noTarget, 17);
            if ((int)sf.GetValue(noTarget) == 17) { acc += 32; }
            if (typeof(Derived32).GetField("frozen").IsInitOnly) { acc += 64; }
            PropertyInfo p = typeof(Derived32).GetProperty("Score");
            if (p == typeof(Derived32).GetProperty("Score")) { acc += 128; }
            if (p.Name == "Score" && p.DeclaringType == typeof(Derived32) && p.PropertyType == typeof(int)) { acc += 256; }
            if (p.CanRead && p.CanWrite && !p.GetMethod.IsStatic && p.SetMethod != null && !p.SetMethod.IsStatic && typeof(Derived32).GetProperty("ReadOnly").SetMethod == null) { acc += 512; }
            p.SetValue(d, 9);
            if ((int)p.GetValue(d) == 90) { acc += 1024; }
            PropertyInfo sp = typeof(Base32).GetProperty("Global"); // .NET: kalitilan static property FlattenHierarchy ister -> bildiren tipten
            sp.SetValue(noTarget, 21);
            if (sp.GetMethod.IsStatic && sp.SetMethod.IsStatic && (int)sp.GetValue(noTarget) == 21) { acc += 2048; }
            PropertyInfo rp = typeof(Derived32).GetProperty("Friend");
            Node32 node = new Node32();
            rp.SetValue(d, node);
            if ((Node32)rp.GetValue(d) == node) { acc += 4096; }
            if (typeof(Derived32).GetField("Auto__bk") == null) { acc += 8192; }
            return acc;
        }
    }
}
