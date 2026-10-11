using System;
using System.Collections.Generic;

namespace DigitoyEngine.Language.Trim
{
    // Reflection facet'i: bir tip icin hangi meta emit edilir (ILLink DynamicallyAccessedMembers karsiligi).
    [Flags]
    public enum MemberKinds
    {
        None = 0,
        Name = 1,      // Type.name string'i (ToString/GetType(string))
        Fields = 2,    // DigitoyEngineMember alan kayitlari + refget/refset
        Props = 4,     // property kayitlari
        Ctors = 8,     // ctor MethodInfo tam kayit + sekil thunk'u (Activator)
        Methods = 16,  // metot MethodInfo tam kayit + sekil thunk'u (GetMethod/Invoke)
        Attrs = 32,    // custom attribute tablolari (tip + uyeler)
        All = Name | Fields | Props | Ctors | Methods | Attrs,
        // Katalog koku (pak'taki component tipi): serilestirme + lifecycle + animasyon + attribute
        Component = All,
        // Kok alanlarindan erisilen inline [Serializable] tip / struct
        Inline = Name | Fields | Ctors | Attrs,
    }

    public enum MethodRecordKind
    {
        None,  // kayit yok (emit edilmeyen kod)
        Trace, // yalniz trace oneki (name/file/trypc/declaringType): her canli metot
        Full,  // tam MethodInfo (reflection/Invoke/modul baglama)
    }

    // Trim profili: PlayerSettings/komut satirindan gelir. `Full` bugunku davranis (modul host + tek meta).
    public sealed class TrimProfile
    {
        public string Name = "full";
        public bool ModuleHost = true;   // vmint + her metot icin tam kayit/thunk/trampoline (docs/modules.md "tek meta")
        public bool TrimMeta = false;    // reflection meta yalniz kok kumeye
        public bool TrimCode = false;    // erisilemeyen kod emit edilmez (RTA) — faz 3
        public string ReportPath;        // --trim-report: tutulan/atilan ogeler ve kok yollari

        public static TrimProfile Full => new TrimProfile();
        public static TrimProfile Lean => new TrimProfile { Name = "lean", ModuleHost = false, TrimMeta = true, TrimCode = true };
        public static TrimProfile Meta => new TrimProfile { Name = "meta", ModuleHost = false, TrimMeta = true, TrimCode = false }; // kod trim suphesinde karsilastirma

        public static TrimProfile Parse(string name) => name switch
        {
            null or "" or "full" => Full,
            "lean" => Lean,
            "meta" => Meta,
            _ => throw new Exception("bilinmeyen trim profili: " + name + " (full | lean | meta)"),
        };
    }
}
