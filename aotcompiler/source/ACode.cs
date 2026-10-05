using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // .acode: insan dostu, elle yazilabilir tam Code formati. Binary Encode/Decode'a (.code, disk
    // kalicilik icin robust format) HIC DOKUNMAZ - tamamen ayri/paralel bir temsil. Op satirlari
    // OpAsm sozdizimini kullanir; imza/flag/arg/local bilgisi anahtar-kelimeli satirlarla verilir.
    //
    // Ornek:
    //   # yorum
    //   code Mat4$Multiply
    //   static
    //   returns Int
    //   generic T
    //   arg a: Mat4
    //   arg amount: ref Float
    //   local sum: Int
    //   native_header <<<
    //   #include <math.h>
    //   >>>
    //   ops:
    //     Push int:0
    //     SetLocal sum      # arg/local isimleri otomatik slot index'ine cevrilir, sayi da gecerli
    //     Label loop
    //     ...
    //     Brtrue loop
    //     Return
    public static class ACode
    {
        public static string Encode(Code code)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("code ").Append(code.EncodeName()).Append('\n');
            if (code.IsStatic) sb.Append("static\n");
            if (code.IsExternal) sb.Append("external\n");
            if (code.ExternalSymbol != null) sb.Append("extern_symbol ").Append(code.ExternalSymbol).Append('\n');
            if (code.Inline) sb.Append("inline\n");
            if (code.IsVirtual) sb.Append("virtual\n");
            if (code.IsOverride) sb.Append("override\n");
            if (code.ReturnType != null && code.ReturnType.Name != "Void")
                sb.Append("returns ").Append(code.ReturnType.Name).Append('\n');

            foreach (var gp in code.GenericParameters)
                sb.Append("generic ").Append(gp.Name).Append('\n');

            foreach (var arg in code.Arguments)
            {
                var mod = arg.IsOut ? "out " : arg.IsRef ? "ref " : "";
                sb.Append("arg ").Append(arg.Name).Append(": ").Append(mod).Append(arg.Type != null ? arg.Type.Name : "null").Append('\n');
            }

            for (int li = 0; li < code.Locals.Count; li++)
            {
                var localName = li < code.LocalNames.Count ? code.LocalNames[li] : null;
                sb.Append("local ");
                if (localName != null) sb.Append(localName).Append(": ");
                sb.Append(code.Locals[li] != null ? code.Locals[li].Name : "null").Append('\n');
            }

            if (code.NativeHeader != null)
                sb.Append("native_header <<<\n").Append(code.NativeHeader).Append("\n>>>\n");
            if (code.NativeBody != null)
                sb.Append("native_body <<<\n").Append(code.NativeBody).Append("\n>>>\n");

            if (code.Operations.Count > 0)
            {
                RebuildLabels(code); // binary'den decode edilmisse Label objeleri yoktur, Slot'lardan uret
                sb.Append("ops:\n");
                foreach (var line in code.ToAssembly().Split('\n'))
                    sb.Append("    ").Append(SlotToName(code, line)).Append('\n');
            }
            return sb.ToString();
        }

        public static Code Decode(string text)
        {
            var code = new Code();
            var lines = text.Split('\n');
            int i = 0;
            bool sawCode = false;

            for (; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line == "ops:") { i++; break; }

                if (line.StartsWith("code "))
                {
                    code.DecodeName(line.Substring(5).Trim());
                    sawCode = true;
                }
                else if (line == "static") code.IsStatic = true;
                else if (line == "external") code.IsExternal = true;
                else if (line.StartsWith("extern_symbol ")) code.ExternalSymbol = line.Substring(14).Trim();
                else if (line == "inline") code.Inline = true;
                else if (line == "virtual") code.IsVirtual = true;
                else if (line == "override") code.IsOverride = true;
                else if (line.StartsWith("returns "))
                    code.ReturnType = TypeRef(line.Substring(8).Trim());
                else if (line.StartsWith("generic "))
                    code.GenericParameters.Add(new Primitive { Name = line.Substring(8).Trim(), IsGenericParameter = true });
                else if (line.StartsWith("arg "))
                    code.Arguments.Add(ParseArg(line.Substring(4)));
                else if (line.StartsWith("local "))
                    ParseLocal(code, line.Substring(6).Trim());
                else if (line.StartsWith("native_header <<<"))
                    code.NativeHeader = ReadBlock(lines, ref i);
                else if (line.StartsWith("native_body <<<"))
                    code.NativeBody = ReadBlock(lines, ref i);
                else
                    throw new System.Exception($"Taninmayan .acode satiri: '{line}'");
            }
            if (!sawCode)
                throw new System.Exception(".acode 'code <isim>' satiri icermiyor");

            // ops: sonrasi tum satirlar OpAsm assembly'si; arg/local isimleri once slot index'ine cevrilir
            if (i < lines.Length)
                code.Operations = Code.ParseAssembly(string.Join("\n", lines.Skip(i).Select(l => NameToSlot(code, l.Trim()))));
            return code;
        }

        static readonly HashSet<string> ArgOps = new HashSet<string> { "GetArg", "SetArg", "AddrArg" };
        static readonly HashSet<string> LocalOps = new HashSet<string> { "GetLocal", "SetLocal", "AddrLocal" };

        // "GetLocal sum" -> "GetLocal 0" (isim gecerse index'e cevir, sayi zaten gecerliyse dokunma)
        static string NameToSlot(Code code, string line)
        {
            var parts = line.Split(new[] { ' ' }, 2);
            if (parts.Length < 2) return line;
            var operand = parts[1].Trim();
            if (int.TryParse(operand, out _)) return line;

            if (ArgOps.Contains(parts[0]))
            {
                var idx = code.Arguments.FindIndex(a => a.Name == operand);
                if (idx < 0) throw new System.Exception($"Bilinmeyen arg ismi: '{operand}' ({parts[0]})");
                return parts[0] + " " + idx;
            }
            if (LocalOps.Contains(parts[0]))
            {
                var idx = code.LocalNames.IndexOf(operand);
                if (idx < 0) throw new System.Exception($"Bilinmeyen local ismi: '{operand}' ({parts[0]})");
                return parts[0] + " " + idx;
            }
            return line;
        }

        // "GetLocal 0" -> "GetLocal sum" (isim varsa; yoksa numerik kalir)
        static string SlotToName(Code code, string line)
        {
            var parts = line.Split(new[] { ' ' }, 2);
            if (parts.Length < 2 || !int.TryParse(parts[1].Trim(), out var slot)) return line;

            if (ArgOps.Contains(parts[0]) && slot < code.Arguments.Count && code.Arguments[slot].Name != null)
                return parts[0] + " " + code.Arguments[slot].Name;
            if (LocalOps.Contains(parts[0]) && slot < code.LocalNames.Count && code.LocalNames[slot] != null)
                return parts[0] + " " + code.LocalNames[slot];
            return line;
        }

        // "local sum: Int" (isimli) ya da "local Int" (isimsiz)
        static void ParseLocal(Code code, string rest)
        {
            var colon = rest.IndexOf(':');
            if (colon >= 0)
            {
                code.LocalNames.Add(rest.Substring(0, colon).Trim());
                code.Locals.Add(TypeRef(rest.Substring(colon + 1).Trim()));
            }
            else
            {
                code.LocalNames.Add(null);
                code.Locals.Add(TypeRef(rest));
            }
        }

        static Argument ParseArg(string rest)
        {
            var colon = rest.IndexOf(':');
            if (colon < 0) throw new System.Exception($"Gecersiz arg satiri (': Tip' eksik): 'arg {rest}'");
            var name = rest.Substring(0, colon).Trim();
            var typePart = rest.Substring(colon + 1).Trim();
            bool isRef = false, isOut = false;
            if (typePart.StartsWith("ref ")) { isRef = true; typePart = typePart.Substring(4).Trim(); }
            else if (typePart.StartsWith("out ")) { isOut = true; typePart = typePart.Substring(4).Trim(); }
            return new Argument { Name = name, Type = TypeRef(typePart), IsRef = isRef, IsOut = isOut };
        }

        static Primitive TypeRef(string name) =>
            name == "null" ? null : new Primitive { Name = name, Unresolved = true };

        // '<<<' satirindan sonraki satirlari tek basina '>>>' gorene kadar toplar.
        // i, blok acilis satirini gosterirken cagrilir; donuste kapanis satirini gosterir.
        static string ReadBlock(string[] lines, ref int i)
        {
            var body = new List<string>();
            for (i++; i < lines.Length; i++)
            {
                if (lines[i].Trim() == ">>>") return string.Join("\n", body);
                body.Add(lines[i]);
            }
            throw new System.Exception(".acode blok kapatilmamis ('>>>' bulunamadi)");
        }

        // Binary .code'dan decode edilen op'larda Label objesi yoktur (sadece linklenmis Slot int'i).
        // Assembly'ye cevirmeden once Slot'lardan Label kimliklerini yeniden kurar; zaten Label'i
        // olan op'lara dokunmaz (elle kurulan/parse edilen kodlar oldugu gibi kalir).
        static void RebuildLabels(Code code)
        {
            var ops = code.Operations;
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if ((op.Type == OpType.Br || op.Type == OpType.Brtrue || op.Type == OpType.Brfalse) && op.Label == null)
                {
                    if (op.Slot < 0 || op.Slot >= ops.Count || ops[op.Slot].Type != OpType.Label)
                        throw new System.Exception($"Gecersiz branch hedefi: Slot={op.Slot} bir Label op'u degil");
                    var target = ops[op.Slot];
                    if (target.Label == null) target.Label = new Label { Name = "L" + op.Slot };
                    op.Label = target.Label;
                }
            }
            // hedefsiz kalan Label op'lari da isimlensin (ToAssembly null Label'da patlamasin)
            for (int i = 0; i < ops.Count; i++)
                if (ops[i].Type == OpType.Label && ops[i].Label == null)
                    ops[i].Label = new Label { Name = "L" + i };
        }
    }
}
