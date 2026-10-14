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


        public static string MangleName(string baseName, IReadOnlyList<Primitive> typeArgs) =>
            $"{baseName}<{string.Join(",", typeArgs.Select(a => a.Name))}>";

        public static Primitive Substitute(Context ctx, Primitive t, Dictionary<Primitive, Primitive> map)
        {
            if (t == null) return null;
            if (map.TryGetValue(t, out var mapped)) return mapped;
            if (t.GenericTemplate != null) // acik uygulama ("Box<T>"): argumanlari yerine koy, kapaliysa ornekle
                return InstantiatePrimitive(ctx, t.GenericTemplate, t.TypeArguments.Select(a => Substitute(ctx, a, map)).ToList());
            if (t.Type == PrimitiveType.Array) return Primitive.ArrayOf(Substitute(ctx, t.ElementType, map), t.ArrayRank);
            if (t.Type == PrimitiveType.FixedArray) return Primitive.FixedArrayOf(Substitute(ctx, t.ElementType, map), t.FixedSize);
            if (t.Type == PrimitiveType.Pointer) return Primitive.PointerOf(Substitute(ctx, t.ElementType, map));
            return t; // generic parametresi de degil, uygulama da degil -> oldugu gibi kalir (somut ornek dahil)
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

        // Somut ornek: (sablon, argumanlar) icin intern'li uygulama dugumu YERINDE ornege donusur (Context.Apply ayni nesneyi
        // verir; onceden alinmis referanslar gecerli kalir). Acik argumanli uygulama dugum olarak kalir.
        public static Primitive InstantiatePrimitive(Context ctx, Primitive template, List<Primitive> typeArgs)
        {
            if (!template.IsGeneric) return template;
            var instance = ctx.Apply(template, typeArgs);
            if (instance.InstantiatedFrom != null) return instance; // zaten somut
            foreach (var a in typeArgs)
                if (ContainsGenericParameter(a)) return instance; // acik: sablon govdesinde yasar, Substitute gercek argumanlarla cozer
            // CANON: marker'li uygulama (canon govdedeki List<__Canon#0>) kimlik olarak canon ornegine gider; dugum acik kalir
            // (op.TypeArguments'ta slot anahtari olarak yasar). Exact ref-tip argumanli ornek: once canon ornegi kurulur,
            // exact kabuklar ona baglanir (govdesiz).
            Primitive canon = null;
            if (Canon.Enabled)
            {
                var cargs = Canon.Canonicalize(ctx, typeArgs, out bool changed);
                // marker'li ya da yari-canon (Func<__Canon,string>) uygulama: kimlik TAM canon ornegi; dugum acik kalir
                if (typeArgs.Any(Canon.ContainsCanon) && (changed || typeArgs.Any(Canon.ContainsMarker))) return InstantiatePrimitive(ctx, template, cargs);
                if (changed) canon = InstantiatePrimitive(ctx, template, cargs);
                else if (cargs.Any(Canon.ContainsCanon)) instance.IsCanonForm = true;
            }
            instantiationDepth++;
            try
            {
                var map = new Dictionary<Primitive, Primitive>();
                for (int i = 0; i < template.GenericParameters.Count; i++)
                    map[template.GenericParameters[i]] = typeArgs[i];

                // arity suffix'i (Action`1) gosterime sizmasin; IC ICE tip adi KORUNUR (List`1.Enumerator -> List.Enumerator),
                // yoksa List<int> ile List<int>.Enumerator ayni Display'i alir -> Type.GetType(ad) yanlis descriptor bulur.
                var baseDisp = System.Text.RegularExpressions.Regex.Replace(template.Display, "`\\d+", "");
                instance.GenericTemplate = null;
                instance.InstantiatedFrom = template;
                instance.Assembly = template.Assembly;
                instance.DisplayName = baseDisp + "<" + string.Join(", ", typeArgs.Select(Primitive.CsDisplay)) + ">";
                instance.FixedSize = template.FixedSize;
                instance.ArrayRank = template.ArrayRank;
                instance.ElementType = Substitute(ctx, template.ElementType, map);
                instance.CilAttributes = template.CilAttributes;
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
                var memberTemplates = ctx.MembersOf(template).ToList(); // snapshot: klon kaydi listeyi degistirmez ama guvenli
                var memberShells = memberTemplates.Select(m => CloneCodeShell(ctx, m, map, null, instance)).ToList();
                if (canon != null)
                {
                    instance.Canonical = canon;
                    ctx.RegisterSharedExact(instance, canon);
                    for (int i = 0; i < memberShells.Count; i++)
                    {
                        memberShells[i].Canonical = MemberOf(ctx, canon, memberTemplates[i]);
                        memberShells[i].BodyCloned = true; // govde yok: kod canon'da
                    }
                }
                else if (instance.IsCanonForm) instance.Canonical = instance;
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
            // CANON: marker'li argumanlar -> canon metot ornegi (kimlik). Ref-tip argumanlar ya da paylasimli exact sahip
            // -> once canon ornegi (canon sahibin sablonu + canon argumanlar), exact kabuk govdesiz ona baglanir.
            if (Canon.Enabled)
            {
                var cargs = Canon.Canonicalize(ctx, typeArgs, out bool changed);
                if (typeArgs.Any(Canon.ContainsCanon) && (changed || typeArgs.Any(Canon.ContainsMarker))) return InstantiateCode(ctx, template, cargs);
                bool ownerShared = template.Owner != null && template.Owner.IsSharedExact;
                if (changed || ownerShared)
                {
                    var canonTemplate = ownerShared ? MemberOf(ctx, template.Owner.Canonical, template) : template;
                    var canonCode = InstantiateCode(ctx, canonTemplate, cargs);
                    var emap = new Dictionary<Primitive, Primitive>();
                    for (int i = 0; i < template.GenericParameters.Count; i++) emap[template.GenericParameters[i]] = typeArgs[i];
                    var shell = CloneCodeShell(ctx, template, emap, typeArgs, template.Owner);
                    if (shell.Canonical == null)
                    {
                        shell.Canonical = canonCode;
                        shell.BodyCloned = true;
                        ctx.RegisterSharedExactCode(shell, canonCode);
                    }
                    return shell;
                }
            }
            var map = new Dictionary<Primitive, Primitive>();
            for (int i = 0; i < template.GenericParameters.Count; i++)
                map[template.GenericParameters[i]] = typeArgs[i];
            return CloneCode(ctx, template, map, typeArgs, template.Owner);
        }

        // Cagri hedefi somutlastirma - TypeArguments SOZLESMESI: liste = [sinif argumanlari] + [method argumanlari].
        // Owner generic ise once sinif somutlanir, uye klonu bulunur; kalan argumanlar method'un kendi parametreleri.
        public static Code ResolveCallTarget(Context ctx, Code target, List<Primitive> typeArgs)
        {
            if (target.Owner != null && target.Owner.IsGeneric)
            {
                int classCount = target.Owner.GenericParameters.Count;
                var inst = InstantiatePrimitive(ctx, target.Owner, typeArgs.GetRange(0, classCount));
                var member = MemberOf(ctx, inst, target);
                var rest = typeArgs.GetRange(classCount, typeArgs.Count - classCount);
                return rest.Count > 0 ? InstantiateCode(ctx, member, rest) : member;
            }
            return InstantiateCode(ctx, target, typeArgs);
        }

        // ortak kabuk: generic METHOD somutlastirma (metot argumanlari, ayni owner) ve generic CLASS uyesi (yeni owner) ayni
        // mekanik. Govde: IL'den somut baglamla (CilFrontend.TranslateInstance), op klonu YOK.
        static Code CloneCode(Context ctx, Code template, Dictionary<Primitive, Primitive> map, List<Primitive> methodArgs, Primitive newOwner)
        {
            var shell = CloneCodeShell(ctx, template, map, methodArgs, newOwner);
            if (shell.GenericParameters.Count == 0 && !shell.BodyCloned) EnqueueBody(ctx, shell); // somutlama sirasinda ertele (reentrancy)
            return shell;
        }

        // Imza kabugu (Owner/arg/return) olustur + kaydet. Locals/govde: IL cevirisi doldurur.
        // Kimlik: (newOwner, kok sablon, metot tip argumanlari) - Context yapisal anahtarla intern'ler.
        static Code CloneCodeShell(Context ctx, Code template, Dictionary<Primitive, Primitive> map, List<Primitive> methodArgs, Primitive newOwner)
        {
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
            var remainingGenerics = template.GenericParameters.Where(gp => !map.ContainsKey(gp)).ToList();
            var finalMargs = margs != null && margs.Count > 0 && remainingGenerics.Count == 0 ? margs : null;
            if (ctx.TryGetInstance(newOwner, root, finalMargs, out var cached)) return cached;

            var code = new Code
            {
                Owner = newOwner,
                SimpleName = template.SimpleName,
                ByrefNamedMangle = template.ByrefNamedMangle,
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
                ReturnType = Substitute(ctx, template.ReturnType, map),
                MethodTypeArguments = finalMargs
            };
            code.Attributes.AddRange(template.Attributes);
            code.GenericParameters.AddRange(remainingGenerics); // generic sinifin generic method'u: kendi parametreleri kabukta kalir (sablon)
            foreach (var arg in template.Arguments)
            {
                var at = arg.Name == "this" && arg.Type == template.Owner ? newOwner : Substitute(ctx, arg.Type, map);
                code.Arguments.Add(new Argument { Name = arg.Name, Type = at, IsRef = arg.IsRef, IsOut = arg.IsOut, IsOutParam = arg.IsOutParam });
            }
            ctx.RegisterCode(code); // self-recursive generic fonksiyonlar icin once kaydet
            return code;
        }

        // Kabuk zincirinin kokundeki sablon (IL govdesinin sahibi): Derived<int>.M<string> -> Derived<T>.M<U>
        public static Code RootTemplate(Code c) { while (c.Template != null) c = c.Template; return c; }

        // Somut ornegin, sablon uyesine karsilik gelen klonu (Box<int>.Get <- Box<T>.Get). Ornek kuruluysa kabuk kayitlidir.
        public static Code MemberOf(Context ctx, Primitive owner, Code templateMember)
        {
            if (ctx.TryGetInstance(owner, templateMember, null, out var member)) return member;
            var members = ctx.MembersOf(owner).Take(12).Select(c => c.DisplayName ?? c.Name);
            var hint = owner.InstantiatedFrom != null ? $" (instantiation of {owner.InstantiatedFrom.Name})"
                : owner.GenericParameters.Count > 0 ? " (TEMPLATE - somutlanmamis owner ile cagrilmis!)"
                : owner.GenericTemplate != null ? " (ACIK uygulama dugumu - somutlanmamis)" : "";
            throw new System.Exception($"somutlastirilmis uye bulunamadi: {owner.Name}.{templateMember.DisplayName ?? templateMember.Name}{hint}\n"
                + $"  owner'in kayitli uyeleri: {(members.Any() ? string.Join(", ", members) : "(hic yok - tip somutlanmamis olabilir)")}");
        }
    }
}
