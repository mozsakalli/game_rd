using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // KIMLIK SOZLESMESI (CoreCLR/IL2CPP modeli, docs/engine-notes.md "KIMLIK = YAPISAL"):
    // - Tip TANIMI yalniz CLR tam adiyla bulunur (metadata TypeDef/TypeRef zaten isimle gelir) -> Definitions.
    // - Generic UYGULAMA (Box<int>, List<T>) yapisal anahtarla intern edilir: (sablon, argumanlar) -> tek nesne. Acik
    //   (T iceren) ve kapali uygulama ayni nesnedir; kapali olan somutlaninca YERINDE ornege donusur (GenericInstantiator).
    //   Sonuc: tip esitligi her yerde referans esitligi (==).
    // - Code kimligi (Owner, kok sablon, metot tip argumanlari): mangled isim YALNIZ C emisyonu icin turetilir, hicbir
    //   arama isimle yapilmaz. Isme dayali tek sorgu metadata basit adidir (SimpleName: "ctor", "finalize", "get_X").
    public class Context
    {
        readonly Dictionary<string, Primitive> definitions = new Dictionary<string, Primitive>()
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
        readonly List<Primitive> primitiveOrder = new List<Primitive>(); // kayit sirasi: deterministik emisyon
        readonly HashSet<Primitive> primitiveSet = new HashSet<Primitive>();

        // generic uygulama intern tablosu: (sablon, argumanlar) -> tek dugum
        readonly Dictionary<ApplyKey, Primitive> applications = new Dictionary<ApplyKey, Primitive>();

        readonly Dictionary<CodeKey, Code> codes = new Dictionary<CodeKey, Code>();
        readonly List<Code> codeOrder = new List<Code>();
        readonly Dictionary<Primitive, List<Code>> membersByOwner = new Dictionary<Primitive, List<Code>>();
        static readonly List<Code> noMembers = new List<Code>();

        // ---- tipler ----

        // Tanim kaydi (TypeDef, somutlanmis ornek, lowering'in urettigi yardimci tip). Ad benzersiz olmali.
        public void RegisterPrimitive(Primitive primitive)
        {
            if (primitiveSet.Add(primitive)) primitiveOrder.Add(primitive);
            definitions[primitive.Name] = primitive;
        }

        // CLR/corelib adi Primitive'in runtime ic adindan farkli olabilir (System.UInt32 -> UInt).
        // Alias kaydi semantic kimligi degistirmez ve emisyon sirasi eklemez.
        public void RegisterPrimitiveAlias(string name, Primitive primitive) => definitions[name] = primitive;

        // Tanim arama CLR tam adiyla (metadata gercegi). Generic ornek/dizi/pointer burada ARANMAZ; onlar yapisal uretilir.
        public bool TryGetPrimitive(string name, out Primitive primitive) => definitions.TryGetValue(name, out primitive);

        // Generic uygulama: ayni (sablon, argumanlar) -> ayni nesne. Kapali uygulama somutlaninca bu nesne yerinde
        // ornege donusur (InstantiatedFrom != null, GenericTemplate == null); referanslar gecerli kalir.
        public Primitive Apply(Primitive template, IReadOnlyList<Primitive> typeArgs)
        {
            if (template.GenericTemplate != null || template.InstantiatedFrom != null)
                throw new System.Exception($"generic uygulama sablon ister, uygulama verildi: {template.Name}");
            if (typeArgs.Count != template.GenericParameters.Count)
                throw new System.Exception($"generic arity uyusmuyor: {template.Name} ({template.GenericParameters.Count}) <- {typeArgs.Count} arguman");
            var key = new ApplyKey(template, typeArgs);
            if (applications.TryGetValue(key, out var node)) return node;
            node = new Primitive
            {
                Name = GenericInstantiator.MangleName(template.Name, typeArgs),
                Type = template.Type,
                IsStruct = template.IsStruct,
                IsInterface = template.IsInterface,
                IsDelegate = template.IsDelegate,
                GenericTemplate = template,
            };
            node.TypeArguments.AddRange(typeArgs);
            applications[key] = node;
            return node;
        }

        // ---- kodlar ----

        public void RegisterCode(Code code)
        {
            var key = CodeKey.Of(code);
            if (codes.TryGetValue(key, out var existing))
            {
                if (existing == code) return;
                // ayni kimlikle ikinci kayit: eskisini degistir (sira korunur)
                int idx = codeOrder.IndexOf(existing);
                codeOrder[idx] = code;
                if (existing.Owner != null && membersByOwner.TryGetValue(existing.Owner, out var oldList)) oldList.Remove(existing);
            }
            else codeOrder.Add(code);
            codes[key] = code;
            if (code.Owner != null)
            {
                if (!membersByOwner.TryGetValue(code.Owner, out var list)) membersByOwner[code.Owner] = list = new List<Code>();
                list.Add(code);
            }
        }

        // Sablon uyesinin (ya da generic metodun) belirli owner + metot argumanlariyla kayitli ornegi var mi?
        public bool TryGetInstance(Primitive owner, Code definition, IReadOnlyList<Primitive> methodTypeArgs, out Code code)
            => codes.TryGetValue(new CodeKey(owner, GenericInstantiator.RootTemplate(definition), methodTypeArgs), out code);

        // Owner'in dogrudan uyeleri (kayit sirasi). Parent zinciri DAHIL DEGIL.
        public IReadOnlyList<Code> MembersOf(Primitive owner) => owner != null && membersByOwner.TryGetValue(owner, out var l) ? l : noMembers;

        // Metadata basit adiyla uye (ctor/cctor/finalize/get_X): parametre sayisi verilirse ona gore (this haric).
        public Code FindMember(Primitive owner, string simpleName, int paramCount = -1)
        {
            foreach (var c in MembersOf(owner))
                if (c.SimpleName == simpleName && c.ExplicitInterface == null && c.GenericParameters.Count == 0 && c.MethodTypeArguments == null
                    && (paramCount < 0 || c.ParamCount == paramCount))
                    return c;
            return null;
        }

        // runtime exception kind -> corelib sinifi (DIGITOYENGINE_EX_* indeksleri; Resolver.ResolveAll doldurur,
        // corelib yuklenmemisse null kalir - runtime kind'lar o programda yakalanamaz, rapor yolu calisir)
        public Primitive[] ExceptionKindTypes = new Primitive[8];

        public IEnumerable<Primitive> AllPrimitives => primitiveOrder;
        public IEnumerable<Code> AllCodes => codeOrder;

        readonly struct ApplyKey : System.IEquatable<ApplyKey>
        {
            readonly Primitive template;
            readonly Primitive[] args;
            public ApplyKey(Primitive template, IReadOnlyList<Primitive> args) { this.template = template; this.args = args.ToArray(); }
            public bool Equals(ApplyKey o)
            {
                if (template != o.template || args.Length != o.args.Length) return false;
                for (int i = 0; i < args.Length; i++) if (args[i] != o.args[i]) return false;
                return true;
            }
            public override bool Equals(object obj) => obj is ApplyKey k && Equals(k);
            public override int GetHashCode()
            {
                int h = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(template);
                foreach (var a in args) h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
                return h;
            }
        }

        // Code kimligi: (owner, kok sablon ya da kendisi, metot tip argumanlari). Sablonun kendisi icin definition == code.
        readonly struct CodeKey : System.IEquatable<CodeKey>
        {
            readonly Primitive owner;
            readonly Code definition;
            readonly Primitive[] margs;
            public CodeKey(Primitive owner, Code definition, IReadOnlyList<Primitive> margs)
            {
                this.owner = owner; this.definition = definition;
                this.margs = margs == null || margs.Count == 0 ? System.Array.Empty<Primitive>() : margs.ToArray();
            }
            public static CodeKey Of(Code c) => new CodeKey(c.Owner, GenericInstantiator.RootTemplate(c), c.MethodTypeArguments);
            public bool Equals(CodeKey o)
            {
                if (owner != o.owner || definition != o.definition || margs.Length != o.margs.Length) return false;
                for (int i = 0; i < margs.Length; i++) if (margs[i] != o.margs[i]) return false;
                return true;
            }
            public override bool Equals(object obj) => obj is CodeKey k && Equals(k);
            public override int GetHashCode()
            {
                int h = owner == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(owner);
                h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(definition);
                foreach (var a in margs) h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
                return h;
            }
        }
    }
}
