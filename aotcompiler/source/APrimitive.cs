using System.Collections.Generic;

namespace DigitoyEngine.Language
{
    // .aprimitive: insan dostu, elle yazilabilir Primitive formati (.acode'un tip karsiligi).
    // Binary Encode/Decode'a (.primitive) HIC DOKUNMAZ - tamamen ayri/paralel bir temsil.
    //
    // Ornek:
    //   # yorum
    //   primitive Mat4
    //   struct                  # deger tipi (GC yok); yoksa heap/GC referansi
    //   generic T               # generic template parametresi
    //   parent Base             # opsiyonel
    //   field m: ]Float:16      # 'field isim: Tip' (static ise 'static field ...')
    //   field x: Float
    public static class APrimitive
    {
        public static string Encode(Primitive p)
        {
            if (p.GenericTemplate != null)
                throw new System.Exception($"Generic 'Apply' node ({p.Name}) .aprimitive'e encode edilemez");
            var sb = new System.Text.StringBuilder();
            sb.Append("primitive ").Append(p.Name).Append('\n');
            if (p.IsStruct) sb.Append("struct\n");
            if (p.Type != PrimitiveType.Model) sb.Append("type ").Append(p.Type).Append('\n');
            foreach (var gp in p.GenericParameters)
                sb.Append("generic ").Append(gp.Name).Append('\n');
            if (p.Parent != null) sb.Append("parent ").Append(p.Parent.Name).Append('\n');
            foreach (var f in p.Fields)
                sb.Append(f.IsStatic ? "static field " : "field ").Append(f.Name).Append(": ").Append(f.Type != null ? f.Type.Name : "null").Append('\n');
            return sb.ToString();
        }

        public static Primitive Decode(string text)
        {
            var p = new Primitive { Type = PrimitiveType.Model };
            bool sawName = false;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("primitive "))
                {
                    p.Name = line.Substring(10).Trim();
                    sawName = true;
                }
                else if (line == "struct") p.IsStruct = true;
                else if (line.StartsWith("type "))
                    p.Type = (PrimitiveType)System.Enum.Parse(typeof(PrimitiveType), line.Substring(5).Trim());
                else if (line.StartsWith("generic "))
                    p.GenericParameters.Add(new Primitive { Name = line.Substring(8).Trim(), IsGenericParameter = true });
                else if (line.StartsWith("parent "))
                    p.Parent = new Primitive { Name = line.Substring(7).Trim(), Unresolved = true };
                else if (line.StartsWith("static field "))
                    p.AddField(ParseField(line.Substring(13), isStatic: true));
                else if (line.StartsWith("field "))
                    p.AddField(ParseField(line.Substring(6), isStatic: false));
                else
                    throw new System.Exception($"Taninmayan .aprimitive satiri: '{line}'");
            }
            if (!sawName)
                throw new System.Exception(".aprimitive 'primitive <isim>' satiri icermiyor");
            return p;
        }

        static PrimitiveField ParseField(string rest, bool isStatic)
        {
            var colon = rest.IndexOf(':');
            if (colon < 0) throw new System.Exception($"Gecersiz field satiri (': Tip' eksik): 'field {rest}'");
            return new PrimitiveField
            {
                Name = rest.Substring(0, colon).Trim(),
                Type = new Primitive { Name = rest.Substring(colon + 1).Trim(), Unresolved = true },
                IsStatic = isStatic
            };
        }
    }
}
