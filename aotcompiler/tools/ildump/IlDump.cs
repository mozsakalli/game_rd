using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

class IlDump
{
    static void Main(string[] args)
    {
        var dll = args[0];
        var typeName = args.Length > 1 ? args[1] : null;
        var methodName = args.Length > 2 ? args[2] : null;
        using var fs = File.OpenRead(dll);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            var tn = md.GetString(td.Name);
            var ns = md.GetString(td.Namespace);
            if (typeName != null && tn != typeName) continue;
            foreach (var mh in td.GetMethods())
            {
                var mdf = md.GetMethodDefinition(mh);
                var mn = md.GetString(mdf.Name);
                if (methodName != null && mn != methodName) continue;
                Console.WriteLine($"=== {ns}.{tn}.{mn} ===");
                if (mdf.RelativeVirtualAddress == 0) { Console.WriteLine("  (no body)"); continue; }
                var body = pe.GetMethodBody(mdf.RelativeVirtualAddress);
                foreach (var r in body.ExceptionRegions)
                    Console.WriteLine($"  EH {r.Kind} try=IL_{r.TryOffset:X4}..IL_{r.TryOffset + r.TryLength:X4} handler=IL_{r.HandlerOffset:X4}..IL_{r.HandlerOffset + r.HandlerLength:X4} catchType={r.CatchType.Kind}");
                var il = body.GetILBytes();
                DumpIl(il);
            }
        }
    }

    static void DumpIl(byte[] il)
    {
        int p = 0;
        while (p < il.Length)
        {
            int at = p;
            byte b = il[p];
            int code = b;
            if (b == 0xFE) { code = 0xFE00 | il[p + 1]; p += 2; } else p += 1;
            string name = code.ToString("X2");
            int operand = OperandSize(code, il, p);
            string extra = "";
            if (IsBranch(code))
            {
                int sz = BranchSize(code);
                int rel = sz == 1 ? (sbyte)il[p] : BitConverter.ToInt32(il, p);
                extra = $"-> IL_{(p + sz + rel):X4}";
                operand = sz;
            }
            Console.WriteLine($"  IL_{at:X4}: {OpName(code)} {extra}");
            p += operand;
        }
    }

    static bool IsBranch(int c) => c switch
    {
        0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 or 0xDE
        or 0x38 or 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0xDD => true,
        _ => false
    };
    static int BranchSize(int c) => c switch
    {
        0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 or 0xDE => 1,
        _ => 4
    };
    static string OpName(int c) => c switch
    {
        0x00 => "nop", 0x2B => "br.s", 0x2C => "brfalse.s", 0x2D => "brtrue.s", 0x38 => "br", 0x39 => "brfalse", 0x3A => "brtrue",
        0xDE => "leave.s", 0xDD => "leave", 0xFE1A => "rethrow", 0xDC => "endfinally", 0x7A => "throw",
        0x28 => "call", 0x6F => "callvirt", 0x73 => "newobj", 0x0A => "stloc.0", 0x0B => "stloc.1",
        _ => "0x" + c.ToString("X2")
    };

    static int OperandSize(int c, byte[] il, int p) => c switch
    {
        0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x1F => 1,
        0xFE09 or 0xFE0A or 0xFE0B or 0xFE0C or 0xFE0D or 0xFE0E => 2,
        0x20 or 0x22 or 0x28 or 0x6F or 0x73 or 0x72 or 0x7B or 0x7D or 0x7E or 0x80 or 0x8D or 0x8C or 0x79 or 0xA5 or 0x74 or 0x75 or 0xFE06 or 0xFE07 => 4,
        0x21 or 0x23 => 8,
        0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 or 0xDE => 1,
        0x38 or 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0xDD => 4,
        _ => 0
    };
}
