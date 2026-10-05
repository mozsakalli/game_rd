using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // Elle yazilabilir, tek-satirlik Op assembly'si (mnemonic tarzi: "GetArg 0", "Push int:5",
    // "Call Increment", "Br loop"). Mevcut Code.Encode/Decode'a (tam/robust format, disk kalicilik
    // icin) HIC DOKUNMAZ - bu tamamen ayri, ek bir temsil. Label'lar isimle eslenir (Decode'da tek
    // metin parcasi = tek isim alani, referans kimligi degil - CIL/Br hedefleri hala Linker'la
    // instruction index'ine cozulur, bu sadece yazim kolayligi).
    public static class OpAsm
    {
        public static string Encode(Op op, System.Func<Label, string> nameOfLabel)
        {
            switch (op.Type)
            {
                case OpType.GetArg:
                case OpType.SetArg:
                case OpType.GetLocal:
                case OpType.SetLocal:
                case OpType.AddrLocal:
                case OpType.AddrArg:
                    return $"{op.Type} {op.Slot}";

                case OpType.GetField:
                case OpType.SetField:
                    return $"{op.Type} {op.Field.Owner.Name}${op.Field.Name}";

                case OpType.New:
                    return op.TypeArguments.Count > 0
                        ? $"New {op.PrimitiveRef.Name} [{string.Join(",", op.TypeArguments.Select(t => t.Name))}]"
                        : $"New {op.PrimitiveRef.Name}";

                case OpType.NewArray:
                    return $"NewArray {op.PrimitiveRef.Name}"; // PrimitiveRef = eleman tipi

                case OpType.Conv:
                case OpType.IsType:
                case OpType.IsValueType:
                case OpType.AsType:
                case OpType.CastClass:
                case OpType.ExIs:
                case OpType.ExBind:
                    return $"{op.Type} {op.PrimitiveRef.Name}";

                case OpType.Call:
                    return op.TypeArguments.Count > 0
                        ? $"Call {op.Code.EncodeName()} [{string.Join(",", op.TypeArguments.Select(t => t.Name))}]"
                        : $"Call {op.Code.EncodeName()}";

                case OpType.CallVirtual:
                    return $"CallVirtual {op.Code.EncodeName()}"; // slot Resolver'da yeniden hesaplanir

                case OpType.Push:
                    return $"Push {LiteralTag(op.Value)}:{op.Value}";

                case OpType.Label: return $"Label {nameOfLabel(op.Label)}";
                case OpType.Br: return $"Br {nameOfLabel(op.Label)}";
                case OpType.Brtrue: return $"Brtrue {nameOfLabel(op.Label)}";
                case OpType.Brfalse: return $"Brfalse {nameOfLabel(op.Label)}";
                case OpType.TryBegin: return $"TryBegin {op.PrimitiveRef.Name} {nameOfLabel(op.Label)}";

                default: return op.Type.ToString(); // operandsiz: Dup/Pop/Return/GetIndex/SetIndex/aritmetik/Ceq-Cgt-Clt/LoadInd/StoreInd
            }
        }

        public static Op Parse(string line, System.Func<string, Label> labelFor)
        {
            var parts = line.Split(new[] { ' ' }, 2);
            var type = (OpType)System.Enum.Parse(typeof(OpType), parts[0]);
            var rest = parts.Length > 1 ? parts[1].Trim() : "";

            switch (type)
            {
                case OpType.GetArg:
                case OpType.SetArg:
                case OpType.GetLocal:
                case OpType.SetLocal:
                case OpType.AddrLocal:
                case OpType.AddrArg:
                    return new Op { Type = type, Slot = int.Parse(rest) };

                case OpType.GetField:
                case OpType.SetField:
                    {
                        var fp = rest.Split('$');
                        return new Op { Type = type, Field = new PrimitiveField { Owner = new Primitive { Name = fp[0], Unresolved = true }, Name = fp[1] } };
                    }

                case OpType.New:
                    {
                        var (name, typeArgs) = SplitNameAndTypeArgs(rest);
                        return new Op { Type = type, PrimitiveRef = new Primitive { Name = name, Unresolved = true }, TypeArguments = typeArgs };
                    }

                case OpType.NewArray:
                case OpType.Conv:
                case OpType.IsType:
                case OpType.IsValueType:
                case OpType.AsType:
                case OpType.CastClass:
                case OpType.ExIs:
                case OpType.ExBind:
                    return new Op { Type = type, PrimitiveRef = new Primitive { Name = rest, Unresolved = true } };

                case OpType.Call:
                    {
                        var (name, typeArgs) = SplitNameAndTypeArgs(rest);
                        var code = new Code(); code.DecodeName(name); code.Unresolved = true;
                        return new Op { Type = type, Code = code, TypeArguments = typeArgs };
                    }

                case OpType.CallVirtual:
                    {
                        var code = new Code(); code.DecodeName(rest); code.Unresolved = true;
                        return new Op { Type = type, Code = code };
                    }

                case OpType.Push:
                    {
                        var tp = rest.Split(new[] { ':' }, 2);
                        return new Op { Type = type, Value = ParseLiteral(tp[0], tp[1]) };
                    }

                case OpType.Label: return new Op { Type = type, Label = labelFor(rest) };
                case OpType.Br: return new Op { Type = type, Label = labelFor(rest) };
                case OpType.Brtrue: return new Op { Type = type, Label = labelFor(rest) };
                case OpType.Brfalse: return new Op { Type = type, Label = labelFor(rest) };

                case OpType.TryBegin: // "TryBegin <CatchTip> <handlerLabel>"
                    {
                        var tb = rest.Split(new[] { ' ' }, 2);
                        return new Op { Type = type, PrimitiveRef = new Primitive { Name = tb[0].Trim(), Unresolved = true }, Label = labelFor(tb[1].Trim()) };
                    }

                default: return new Op { Type = type }; // operandsiz
            }
        }

        static (string name, List<Primitive> typeArgs) SplitNameAndTypeArgs(string rest)
        {
            var bracket = rest.IndexOf('[');
            if (bracket < 0) return (rest, new List<Primitive>());
            var name = rest.Substring(0, bracket).Trim();
            var inside = rest.Substring(bracket + 1, rest.Length - bracket - 2); // '[' ve ']' haric
            var typeArgs = inside.Split(',').Select(n => (Primitive)new Primitive { Name = n.Trim(), Unresolved = true }).ToList();
            return (name, typeArgs);
        }

        static string LiteralTag(object v) =>
            v is string ? "str" : v is int ? "int" : v is float ? "float" : v is double ? "double" :
            v is bool ? "bool" : v is long ? "long" : v is char ? "char" : v is short ? "short" :
            v is byte ? "byte" : throw new System.Exception("desteklenmeyen literal tipi: " + v.GetType().Name);

        static object ParseLiteral(string tag, string value)
        {
            switch (tag)
            {
                case "str": return value;
                case "int": return int.Parse(value);
                case "float": return float.Parse(value);
                case "double": return double.Parse(value);
                case "bool": return bool.Parse(value);
                case "long": return long.Parse(value);
                case "char": return char.Parse(value);
                case "short": return short.Parse(value);
                case "byte": return byte.Parse(value);
                default: throw new System.Exception("desteklenmeyen literal etiketi: " + tag);
            }
        }
    }
}
