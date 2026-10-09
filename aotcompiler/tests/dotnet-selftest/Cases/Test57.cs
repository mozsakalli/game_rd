using System;
using System.Reflection;

namespace Demo57
{
    // Custom attribute reflection (docs/registry-removal.md Faz 4b): IsDefined / GetCustomAttribute<T> / GetCustomAttributes
    // tip, alan, property, metot uzerinde; sabit argumanlar (string/int/bool/enum/Type), named arg (alan + property),
    // turetilmis attribute tipiyle eslesme, inherit (base sinif attribute'u), pseudo-attribute bitleri
    // ([Serializable] -> TypeAttributes.Serializable, [NonSerialized] -> FieldAttributes.NotSerialized), erisim bitleri.
    enum Mode57 { A, B = 3 }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    class TagAttribute : Attribute
    {
        public string Name;
        public int Order;
        public int Weight { get; set; }
        public TagAttribute(string name) { Name = name; }
        public TagAttribute(string name, int order) { Name = name; Order = order; }
    }
    class SpecialTagAttribute : TagAttribute
    {
        public SpecialTagAttribute(string name) : base(name) { }
    }
    [AttributeUsage(AttributeTargets.All)]
    class ModeAttribute : Attribute
    {
        public Mode57 Mode; public bool Flag; public Type Target;
        public ModeAttribute(Mode57 mode, bool flag, Type target) { Mode = mode; Flag = flag; Target = target; }
    }
    class MarkerAttribute : Attribute { }

    [Serializable]
    [Tag("base-tag")]
    class Base57 { }

    [Tag("holder", 5, Weight = 9)]
    [Mode(Mode57.B, true, typeof(Base57))]
    class Holder57 : Base57
    {
        [Tag("f1")] [Tag("f1b", 2)] public int F1;
        [NonSerialized] public float Skipped;
        [SpecialTag("f2")] public int F2;
        int _priv = 4;
        [Marker] public string Prop { get; set; }
        [Marker] public void Fire() { F1++; }
        public void Plain() { }
        public int Priv => _priv;
    }

    class App57
    {
        public static int Run()
        {
            long acc = 0;
            Type t = typeof(Holder57);

            // tip: IsDefined + GetCustomAttribute<T> sabit/named arglar
            if (t.IsDefined(typeof(TagAttribute), false)) acc |= 1;
            var tag = t.GetCustomAttribute<TagAttribute>(false);
            if (tag != null && tag.Name == "holder" && tag.Order == 5 && tag.Weight == 9) acc |= 2;
            var mode = t.GetCustomAttribute<ModeAttribute>();
            if (mode != null && mode.Mode == Mode57.B && mode.Flag && mode.Target == typeof(Base57)) acc |= 4;
            if (!t.IsDefined(typeof(MarkerAttribute), false)) acc |= 8;
            // NOT: .NET her cagrida YENI attribute nesnesi uretir; AOT cache'ler (GC-free, bilincli fark) -> kimlik karsilastirilmaz
            acc |= 16;
            bool ambiguous = false;
            try { t.GetCustomAttribute<TagAttribute>(); } catch (AmbiguousMatchException) { ambiguous = true; } // inherit default true: base-tag + holder
            if (ambiguous) acc |= 8388608;

            // inherit: base sinif attribute'u; pseudo-attribute [Serializable] hem IsDefined hem TypeAttributes biti
            if (!t.IsDefined(typeof(SerializableAttribute), false) && typeof(Base57).IsDefined(typeof(SerializableAttribute), false)
                && t.GetCustomAttributes(typeof(TagAttribute), true).Length == 2) acc |= 32;
            if (typeof(Base57).IsSerializable && !t.IsSerializable) acc |= 64;
            if ((typeof(Base57).Attributes & TypeAttributes.Serializable) != 0) acc |= 128;

            // alan: coklu attribute
            FieldInfo f1 = t.GetField("F1");
            var tags = f1.GetCustomAttributes(typeof(TagAttribute), false);
            if (tags.Length == 2) acc |= 256;
            int sum = 0;
            foreach (object o in tags) sum += ((TagAttribute)o).Order;
            if (sum == 2) acc |= 512;
            if (f1.IsPublic && !f1.IsNotSerialized && !f1.IsStatic) acc |= 1024;
            FieldInfo sk = t.GetField("Skipped");
            if (sk != null && sk.IsNotSerialized && sk.IsPublic && sk.IsDefined(typeof(NonSerializedAttribute), false)) acc |= 2048;
            if ((sk.Attributes & FieldAttributes.NotSerialized) != 0 && (sk.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public) acc |= 4096;
            // turetilmis attribute tipi base tipiyle sorgulanir (public alan uzerinden; private alanlar BindingFlags -> Faz 4c)
            FieldInfo f2 = t.GetField("F2");
            if (f2 != null && f2.IsDefined(typeof(TagAttribute), false)) acc |= 8192;
            if (f2 != null && f2.GetCustomAttribute<TagAttribute>() is SpecialTagAttribute st && st.Name == "f2") acc |= 16384;
            if (f2 != null && !f2.IsDefined(typeof(MarkerAttribute), false)) acc |= 32768;

            // property + metot
            PropertyInfo pi = t.GetProperty("Prop");
            if (pi != null && pi.IsDefined(typeof(MarkerAttribute), false)) acc |= 65536;
            MethodInfo fire = t.GetMethod("Fire");
            if (fire != null && fire.IsDefined(typeof(MarkerAttribute), false) && fire.IsPublic) acc |= 131072;
            MethodInfo plain = t.GetMethod("Plain");
            if (plain != null && !plain.IsDefined(typeof(MarkerAttribute), false) && plain.GetCustomAttributes(false).Length == 0) acc |= 262144;

            // Attribute statikleri + uzanti IsDefined
            if (Attribute.IsDefined(t, typeof(ModeAttribute)) && ((ModeAttribute)Attribute.GetCustomAttribute(t, typeof(ModeAttribute))).Flag) acc |= 524288;
            if (f1.IsDefined(typeof(TagAttribute))) acc |= 1048576;

            // attribute olmayan tip
            if (!typeof(Mode57).IsDefined(typeof(TagAttribute), false) && typeof(App57).GetCustomAttributes(false).Length == 0) acc |= 2097152;

            // instance ustunde davranis: attribute nesnesi gercek (sanal olmayan uye erisimi)
            var h = new Holder57();
            fire.Invoke(h, null);
            if (h.F1 == 1 && h.Priv == 4) acc |= 4194304;

            return (int)acc;
        }
    }
}
