using System.Collections.Generic;

namespace DigitoyEngine.Language
{

    public class Context
    {
        Dictionary<string, Primitive> Primitives = new Dictionary<string, Primitive>()
        {
            { Primitive.Int.Name, Primitive.Int },
            { Primitive.Byte.Name, Primitive.Byte },
            { Primitive.Short.Name, Primitive.Short },
            { Primitive.Char.Name, Primitive.Char },
            { Primitive.Float.Name, Primitive.Float },
            { Primitive.Double.Name, Primitive.Double },
            { Primitive.Long.Name, Primitive.Long },
            { Primitive.IntArray.Name, Primitive.IntArray },
            { Primitive.ByteArray.Name, Primitive.ByteArray },
            { Primitive.ShortArray.Name, Primitive.ShortArray },
            { Primitive.CharArray.Name, Primitive.CharArray },
            { Primitive.FloatArray.Name, Primitive.FloatArray },
            { Primitive.DoubleArray.Name, Primitive.DoubleArray },
            { Primitive.LongArray.Name, Primitive.LongArray },
            { Primitive.Object.Name, Primitive.Object },
            { Primitive.String.Name, Primitive.String },
            { Primitive.ValueType.Name, Primitive.ValueType },
            { Primitive.Void.Name, Primitive.Void }
        };

        Dictionary<string, Code> Codes = new Dictionary<string, Code>();
        List<string> PrimitiveOrder = new List<string>(); // kayit sirasi, deterministik emisyon icin (transpiler)
        List<string> CodeOrder = new List<string>();

        public void RegisterPrimitive(Primitive primitive)
        {
            if (!Primitives.ContainsKey(primitive.Name))
                PrimitiveOrder.Add(primitive.Name);
            Primitives[primitive.Name] = primitive;
        }

        // CLR/corelib adi Primitive'in runtime ic adindan farkli olabilir (System.UInt32 -> UInt).
        // Alias kaydi semantic kimligi degistirmez ve emisyon sirasi eklemez.
        public void RegisterPrimitiveAlias(string name, Primitive primitive) => Primitives[name] = primitive;

        public void RegisterCode(Code code)
        {
            var key = code.EncodeName();
            if (!Codes.ContainsKey(key))
                CodeOrder.Add(key);
            Codes[key] = code;
        }

        public bool TryGetPrimitive(string name, out Primitive primitive) => Primitives.TryGetValue(name, out primitive);

        // runtime exception kind -> prelude sinifi (DIGITOYENGINE_EX_* indeksleri; Resolver.ResolveAll doldurur,
        // prelude derlenmemisse null kalir - runtime kind'lar o programda yakalanamaz, rapor yolu calisir)
        public Primitive[] ExceptionKindTypes = new Primitive[8];

        // front-end'in derleme birimleri arasi tasidigi durum (orn. method cozum tablosu).
        // Language katmani icerigini BILMEZ (opak); CsCompiler kurar/devralir.
        public object FrontendState;
        public object FrontendExtensions; // extension method kayitlari (opak, birimler-arasi)

        public bool TryGetCode(string encodedName, out Code code) => Codes.TryGetValue(encodedName, out code);

        public IEnumerable<Primitive> AllPrimitives => PrimitiveOrder.ConvertAll(n => Primitives[n]);
        public IEnumerable<Code> AllCodes => CodeOrder.ConvertAll(n => Codes[n]);
    }
}