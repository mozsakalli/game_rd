using System;
using System.Reflection;

namespace Demo55
{
    // Metot reflection (tek meta): GetMethod/Invoke (sanal dispatch dahil), GetConstructor/ConstructorInfo.Invoke,
    // GetMethods/GetParameters, ReturnType, IsValueType/IsInterface, struct metot Invoke, static Invoke, Activator ctor.
    // Sonuc .NET baseline ile birebir karsilastirilir (bit maskesi).
    interface IShape55 { int Area(); }
    class Shape55 : IShape55
    {
        public int w, h;
        public Shape55() { w = 2; h = 3; }
        public Shape55(int w, int h) { this.w = w; this.h = h; }
        public virtual int Area() { return w * h; }
        public int Scale(int k) { return Area() * k; }
        public static int Twice(int x) { return x * 2; }
        public string Label(string p, int n) { return p + n; }
    }
    class Square55 : Shape55
    {
        public Square55() : base(4, 4) { }
        public override int Area() { return w * w + 1; }
    }
    struct Pt55
    {
        public int x, y;
        public int Sum() { return x + y; }
    }
    class App55
    {
        public static int Run()
        {
            int acc = 0;
            Type t = typeof(Shape55);
            MethodInfo area = t.GetMethod("Area");
            if (area != null && area == typeof(Shape55).GetMethod("Area")) acc |= 1;             // kimlik
            if (area != null && (int)area.Invoke(new Shape55(), null) == 6) acc |= 2;           // instance, parametresiz
            if (area != null && (int)area.Invoke(new Square55(), null) == 17) acc |= 4;         // sanal dispatch (Square override)
            MethodInfo scale = t.GetMethod("Scale");
            if (scale != null && (int)scale.Invoke(new Shape55(3, 5), new object[] { 2 }) == 30) acc |= 8; // int arguman unbox
            MethodInfo twice = t.GetMethod("Twice");
            if (twice != null && twice.IsStatic && (int)twice.Invoke(null, new object[] { 21 }) == 42) acc |= 16; // static
            MethodInfo label = t.GetMethod("Label");
            if (label != null && (string)label.Invoke(new Shape55(), new object[] { "n", 7 }) == "n7") acc |= 32; // string + int
            if (label != null && label.ReturnType == typeof(string) && label.GetParameters().Length == 2 && label.GetParameters()[1].ParameterType == typeof(int)) acc |= 64;
            ConstructorInfo c0 = t.GetConstructor(Type.EmptyTypes);
            ConstructorInfo c2 = t.GetConstructor(new[] { typeof(int), typeof(int) });
            if (c0 != null && c2 != null && c0 != c2 && ((Shape55)c0.Invoke(null)).Area() == 6) acc |= 128;
            if (c2 != null && ((Shape55)c2.Invoke(new object[] { 5, 6 })).Area() == 30) acc |= 256;
            if (t.GetMethod("Yok") == null) acc |= 512;
            if (typeof(Pt55).IsValueType && !typeof(Shape55).IsValueType && typeof(IShape55).IsInterface && !typeof(Shape55).IsInterface) acc |= 1024;
            MethodInfo sum = typeof(Pt55).GetMethod("Sum");
            object boxed = new Pt55 { x = 4, y = 9 };
            if (sum != null && (int)sum.Invoke(boxed, null) == 13) acc |= 2048;                 // struct instance (kutu uzerinden)
            MethodInfo iarea = typeof(IShape55).GetMethod("Area");
            if (iarea != null && (int)iarea.Invoke(new Square55(), null) == 17) acc |= 4096;    // iface dispatch
            int named = 0;
            foreach (var m in t.GetMethods()) if (m.Name == "Scale" || m.Name == "Twice") named++;
            if (named == 2) acc |= 8192;
            var created = Activator.CreateInstance(typeof(Square55)) as Square55;
            if (created != null && created.Area() == 17) acc |= 16384;
            return acc;
        }
    }
}
