using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // Generic Primitive/Code'lari somut tiplere gore tek seferlik "monomorphize" eder.
    // VM hicbir zaman generic gormez; her New/Call somutlastirilmis (IsGeneric=false) hedefe baglanir.
    // GOVDE KURALI (GENERICS-S1, .NET/IL2CPP modeli): sablon govdesi HICBIR ZAMAN IR'a cevrilmez ve klonlanmaz;
    // ornek kabugu (imza) burada kurulur, govdesi CilFrontend.TranslateInstance ile SOMUT generic baglamda IL'den cevrilir.
    public static class GenericInstantiator
    {
        // GOVDE KUYRUGU: tip somutlama (kabuk kurulumu) surerken govde cevirisi ertelenir; en distaki somutlama bitince
        // bosalir. IL kaynagi henuz hazir olmayan (LoadMembers suren assembly) kabuklar loader hazir olana dek bekler.
        static readonly List<(Code shell, Context ctx)> pendingBodies = new List<(Code, Context)>();
        static readonly List<(Code shell, Context ctx)> waitingLoader = new List<(Code, Context)>();
        static int instantiationDepth;
        static bool draining;

        static void EnqueueBody(Context ctx, Code shell)
        {
            pendingBodies.Add((shell, ctx));
            if (instantiationDepth == 0) DrainBodies();
        }

        // Bir assembly'nin govdeleri hazir oldugunda (Loader.LoadBodies sonu) bekleyenler yeniden denenir.
        public static void LoaderReady() { pendingBodies.AddRange(waitingLoader); waitingLoader.Clear(); if (instantiationDepth == 0) DrainBodies(); }

        public static void DrainBodies()
        {
            if (draining) return;
            draining = true;
            try
            {
                while (pendingBodies.Count > 0)
                {
                    var (shell, ctx) = pendingBodies[0];
                    pendingBodies.RemoveAt(0);
                    if (shell.BodyCloned) continue;
                    try
                    {
                        if (!Cil.CilFrontend.TranslateInstance(ctx, shell)) { waitingLoader.Add((shell, ctx)); continue; }
                    }
                    catch (System.Exception ex)
                    {
                        // tek ornek govdesi cevrilemedi: kabuk stub kalir, digerleri etkilenmez
                        shell.Operations.Clear();
                        shell.UntranslatableReason = ex.Message;
                        System.Console.Error.WriteLine($"[inst-stub] {shell.EncodeName()}: {ex.Message.Split('\n')[0]}");
                    }
                    shell.BodyCloned = true;
                }
            }
            finally { draining = false; }
        }

        public static string MangleName(string baseName, List<Primitive> typeArgs) =>
            $"{baseName}<{string.Join(",", typeArgs.Select(a => a.Name))}>";

        public static Primitive Substitute(Context ctx, Primitive t, Dictionary<Primitive, Primitive> map)
        {
            if (t == null) return null;
            if (map.TryGetValue(t, out var mapped)) return mapped;
            if (t.GenericTemplate != null) // "Box<T>" gibi ic ice generic uygulama ifadesi
                return InstantiatePrimitive(ctx, t.GenericTemplate, t.TypeArguments.Select(a => Substitute(ctx, a, map)).ToList());
            if (t.Type == PrimitiveType.Array) return Primitive.ArrayOf(Substitute(ctx, t.ElementType, map), t.ArrayRank);
            if (t.Type == PrimitiveType.FixedArray) return Primitive.FixedArrayOf(Substitute(ctx, t.ElementType, map), t.FixedSize);
            if (t.Type == PrimitiveType.Pointer) return Primitive.PointerOf(Substitute(ctx, t.ElementType, map));
            return t; // generic parametresi de degil, uygulama da degil -> oldugu gibi kalir
        }

        // Tip generic parametre iceriyor mu (T, List<T>, T[], T*): bu tiplerle SOMUT ornek uretilmez.
        public static bool ContainsGenericParameter(Primitive t)
        {
            if (t == null) return false;
            if (t.IsGenericParameter) return true;
            if (t.ElementType != null && ContainsGenericParameter(t.ElementType)) return true;
            foreach (var a in t.TypeArguments)
                if (ContainsGenericParameter(a)) return true;
            return false;
        }

        public static Primitive InstantiatePrimitive(Context ctx, Primitive template, List<Primitive> typeArgs)
        {
            if (!template.IsGeneric) return template;
            // Acik arguman (T, List<T>...): somut ornek degil uygulama ifadesi — klonlamada Substitute bunu
            // gercek argumanlarla yeniden cozer. (Aksi halde "ListEnumerator<T>" adli sahte somut tip sizar.)
            foreach (var a in typeArgs)
                if (ContainsGenericParameter(a)) return Primitive.Apply(template, typeArgs.ToArray());
            var name = MangleName(template.Name, typeArgs);
            if (ctx.TryGetPrimitive(name, out var cached)) return cached;
            instantiationDepth++;
            try
            {

                var map = new Dictionary<Primitive, Primitive>();
                for (int i = 0; i < template.GenericParameters.Count; i++)
                    map[template.GenericParameters[i]] = typeArgs[i];

                // arity suffix'i (Action`1) gosterime sizmasin; IC ICE tip adi KORUNUR (List`1.Enumerator -> List.Enumerator),
                // yoksa List<int> ile List<int>.Enumerator ayni Display'i alir -> Type.GetType(ad) yanlis descriptor bulur.
                var baseDisp = System.Text.RegularExpressions.Regex.Replace(template.Display, "`\\d+", "");
                var instance = new Primitive
                {
                    Name = name,
                    Assembly = template.Assembly,
                    DisplayName = baseDisp + "<" + string.Join(", ", typeArgs.Select(Primitive.CsDisplay)) + ">",
                    Type = template.Type,
                    IsStruct = template.IsStruct,
                    IsInterface = template.IsInterface,
                    IsDelegate = template.IsDelegate,
                    FixedSize = template.FixedSize,
                    ArrayRank = template.ArrayRank,
                    ElementType = Substitute(ctx, template.ElementType, map),
                    InstantiatedFrom = template,
                    CilAttributes = template.CilAttributes,
                };
                instance.TypeArguments.AddRange(typeArgs);
                // oz-tip: govdede sablonun KENDISI gorunurse (ldarg.0 `this` spill'i, `new List<T>()` icinde List`1) -> bu ornek
                map[template] = instance;
                ctx.RegisterPrimitive(instance); // once kaydet: self-referansli generic (Node<T>.next:Node<T>) icin dongu kirilir
                                                 // alanlari HEMEN ekle: ic ice somutlastirma (parent/iface/method klonu) bu tipin alanlarini sorabilir
                foreach (var field in template.Fields)
                    instance.AddField(new PrimitiveField { Name = field.Name, Type = field.Type, IsStatic = field.IsStatic, IsReadonly = field.IsReadonly, CilAttributes = field.CilAttributes });
                foreach (var field in template.StaticFields) // C# semantigi: Box<Int> ve Box<Float> statics AYRI
                    instance.AddField(new PrimitiveField { Name = field.Name, Type = field.Type, IsStatic = field.IsStatic, IsReadonly = field.IsReadonly, CilAttributes = field.CilAttributes });
                // FAZ 1: uye imza kabuklarini alan/parent/iface tip somutlamasindan ONCE kaydet.
                // Alan tipi somutlamasi dongusel tipleri reentrant somutlar (List<->ListEnumerator);
                // o sirada bu tipin uyeleri MemberOf ile aranabilir -> kabuk hazir olmali.
                var memberTemplates = ctx.AllCodes.Where(m => m.Owner == template).ToList(); // snapshot: klon kaydi listeyi degistirir
                if (memberTemplates.Count == 0 && System.Environment.GetEnvironmentVariable("INSTTRACE") == "1")
                    System.Console.Error.WriteLine($"[insttrace] BOS SNAPSHOT: {name} (template nesne #{template.GetHashCode():x})\n{System.Environment.StackTrace}");
                var memberShells = memberTemplates.Select(m => CloneCodeShell(ctx, m, map, m.Name, instance)).ToList();
                for (int fieldIndex = 0; fieldIndex < instance.Fields.Count; fieldIndex++)
                    instance.Fields[fieldIndex].Type = Substitute(ctx, template.Fields[fieldIndex].Type, map);
                for (int fieldIndex = 0; fieldIndex < instance.StaticFields.Count; fieldIndex++)
                    instance.StaticFields[fieldIndex].Type = Substitute(ctx, template.StaticFields[fieldIndex].Type, map);
                instance.Parent = Substitute(ctx, template.Parent, map); // "Derived<T> : Base<T>" -> Base<Int>
                if (template.IsDelegate) // imza somutlanir (Func<int,int> -> int(int))
                {
                    instance.DelegateReturn = Substitute(ctx, template.DelegateReturn, map);
                    foreach (var dp in template.DelegateParams)
                        instance.DelegateParams.Add(Substitute(ctx, dp, map));
                }
                foreach (var iface in template.Interfaces)
                    instance.Interfaces.Add(Substitute(ctx, iface, map));

                // FAZ 2: govdeler ERTELENIR - en distaki somutlama bitince kuyruk bosalir; govde IL'den somut baglamla cevrilir
                // (vtable/itable dogrulugu icin kabuklar zaten eksiksiz kayitli). Kendi generic parametresi kalan uye (generic
                // sinifin generic metodu) sablondur: govdesi InstantiateCode ile somutlaninca cevrilir.
                foreach (var shell in memberShells)
                    if (shell.GenericParameters.Count == 0 && !shell.BodyCloned) EnqueueBody(ctx, shell);
                return instance;
            }
            finally
            {
                if (--instantiationDepth == 0) DrainBodies();
            }
        }

        public static Code InstantiateCode(Context ctx, Code template, List<Primitive> typeArgs)
        {
            if (template.GenericParameters.Count == 0) return template;
            var map = new Dictionary<Primitive, Primitive>();
            for (int i = 0; i < template.GenericParameters.Count; i++)
                map[template.GenericParameters[i]] = typeArgs[i];
            return CloneCode(ctx, template, map, MangleName(template.Name, typeArgs), template.Owner);
        }

        // Cagri hedefi somutlastirma - TypeArguments SOZLESMESI: liste = [sinif argumanlari] + [method argumanlari].
        // Owner generic ise once sinif somutlanir, uye klonu bulunur; kalan argumanlar method'un kendi parametreleri.
        public static Code ResolveCallTarget(Context ctx, Code target, List<Primitive> typeArgs)
        {
            if (target.Owner != null && target.Owner.IsGeneric)
            {
                int classCount = target.Owner.GenericParameters.Count;
                var inst = InstantiatePrimitive(ctx, target.Owner, typeArgs.GetRange(0, classCount));
                var member = MemberOf(ctx, inst, target.Name);
                var rest = typeArgs.GetRange(classCount, typeArgs.Count - classCount);
                return rest.Count > 0 ? InstantiateCode(ctx, member, rest) : member;
            }
            return InstantiateCode(ctx, target, typeArgs);
        }

        // ortak kabuk: generic METHOD somutlastirma (yeni ad, ayni owner) ve generic CLASS uyesi (ayni ad, yeni owner) ayni
        // mekanik. Govde: IL'den somut baglamla (CilFrontend.TranslateInstance), op klonu YOK.
        static Code CloneCode(Context ctx, Code template, Dictionary<Primitive, Primitive> map, string newName, Primitive newOwner)
        {
            var shell = CloneCodeShell(ctx, template, map, newName, newOwner);
            if (shell.GenericParameters.Count == 0 && !shell.BodyCloned) EnqueueBody(ctx, shell); // somutlama sirasinda ertele (reentrancy)
            return shell;
        }

        // Imza kabugu (Owner/Name/arg/return) olustur + kaydet. Locals/govde: IL cevirisi doldurur.
        static Code CloneCodeShell(Context ctx, Code template, Dictionary<Primitive, Primitive> map, string newName, Primitive newOwner)
        {
            var probe = new Code { Owner = newOwner, Name = newName };
            if (ctx.TryGetCode(probe.EncodeName(), out var cached)) return cached;

            var code = new Code
            {
                Owner = newOwner,
                Name = newName,
                Assembly = template.Assembly,
                Template = template,
                IlSource = template.IlSource,
                DisplayName = template.DisplayName,
                SourceFile = template.SourceFile,
                IsStatic = template.IsStatic,
                IsVirtual = template.IsVirtual,
                IsOverride = template.IsOverride,
                IsExternal = template.IsExternal,
                ExternalSymbol = template.ExternalSymbol,
                NativeBody = template.NativeBody,
                NativeHeader = template.NativeHeader,
                CilAttributes = template.CilAttributes,
                ExplicitInterface = Substitute(ctx, template.ExplicitInterface, map),
                ReturnType = Substitute(ctx, template.ReturnType, map)
            };
            code.Attributes.AddRange(template.Attributes);
            // generic METHOD argumanlari: kok sablonun parametre sirasiyla (map'ten; ic ice kabukta onceki kabugun listesinden)
            var root = RootTemplate(template);
            var margs = new List<Primitive>();
            for (int i = 0; i < root.GenericParameters.Count; i++)
            {
                var gp = root.GenericParameters[i];
                if (map.TryGetValue(gp, out var m)) margs.Add(m);
                else if (template.MethodTypeArguments != null && i < template.MethodTypeArguments.Count) margs.Add(template.MethodTypeArguments[i]);
                else { margs = null; break; }
            }
            foreach (var gp in template.GenericParameters)
                if (!map.ContainsKey(gp))
                    code.GenericParameters.Add(gp); // generic sinifin generic method'u: kendi parametreleri kabukta kalir (sablon)
            if (margs != null && margs.Count > 0 && code.GenericParameters.Count == 0) code.MethodTypeArguments = margs;
            foreach (var arg in template.Arguments)
            {
                var at = arg.Name == "this" && arg.Type == template.Owner ? newOwner : Substitute(ctx, arg.Type, map);
                code.Arguments.Add(new Argument { Name = arg.Name, Type = at, IsRef = arg.IsRef, IsOut = arg.IsOut });
            }
            ctx.RegisterCode(code); // self-recursive generic fonksiyonlar icin once kaydet
            return code;
        }

        // Kabuk zincirinin kokundeki sablon (IL govdesinin sahibi): Derived<int>.M<string> -> Derived<T>.M<U>
        public static Code RootTemplate(Code c) { while (c.Template != null) c = c.Template; return c; }

        public static Code MemberOf(Context ctx, Primitive owner, string methodName)
        {
            var probe = new Code { Owner = owner, Name = methodName };
            if (!ctx.TryGetCode(probe.EncodeName(), out var member))
            {
                // near-miss: owner'in kayitli uyeleri + template/instantiation durumu
                var members = new List<string>();
                foreach (var c in ctx.AllCodes)
                    if (c.Owner == owner) { members.Add(c.Name); if (members.Count >= 12) break; }
                // kimlik bolunmesi tespiti: ayni ISIMDE ama farkli NESNE owner'lar (problem: kanonik tip kimligi yok)
                var splits = new HashSet<string>();
                foreach (var c in ctx.AllCodes)
                    if (c.Owner != owner && c.Owner != null && c.Owner.Name == owner.Name)
                        splits.Add(c.Name);
                var hint = owner.GenericTemplate != null
                    ? $" (instantiation of {owner.GenericTemplate.Name})"
                    : owner.GenericParameters.Count > 0 ? " (TEMPLATE - somutlanmamis owner ile cagrilmis!)" : "";
                var splitNote = splits.Count > 0
                    ? $"\n  KIMLIK BOLUNMESI: '{owner.Name}' adinda BASKA bir Primitive nesnesinin uyeleri var: {string.Join(", ", splits.Take(12))}"
                    : "";
                var state = $"\n  owner durumu: nesne #{owner.GetHashCode():x} Unresolved={owner.Unresolved} GenericTemplate={(owner.GenericTemplate?.Name ?? "yok")} TypeArgs=[{string.Join(",", owner.TypeArguments.Select(a => a.Name))}] GenericParams={owner.GenericParameters.Count}";
                // isim-anahtarli kayit taramasi: ayni owner ADI altinda kayitli TUM code'lar (nesne kimliginden bagimsiz)
                var byName = new List<string>();
                foreach (var c in ctx.AllCodes)
                    if (c.Owner != null && c.Owner.Name == owner.Name) { byName.Add($"{c.Name}(#{c.Owner.GetHashCode():x})"); if (byName.Count >= 12) break; }
                state += $"\n  '{owner.Name}$*' isim-anahtarli kayitlar: {(byName.Count > 0 ? string.Join(", ", byName) : "yok")}";
                throw new System.Exception($"somutlastirilmis uye bulunamadi: {owner.Name}${methodName}{hint}\n"
                    + $"  owner'in kayitli uyeleri: {(members.Count > 0 ? string.Join(", ", members) : "(hic yok - tip somutlanmamis olabilir)")}{splitNote}{state}");
            }
            return member;
        }

        static PrimitiveField FieldOf(Primitive owner, string name, bool isStatic)
        {
            for (var t = owner; t != null && !t.Unresolved; t = t.Parent)
                foreach (var f in isStatic ? t.StaticFields : t.Fields)
                    if (f.Name == name) return f;
            throw new System.Exception($"somutlastirilmis alan bulunamadi: {owner.Name}.{name} (mevcut: {string.Join(",", owner.Fields.Select(field => field.Name))})");
        }
    }
}
