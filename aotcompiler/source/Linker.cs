using System.Collections.Generic;

namespace DigitoyEngine.Language
{
    // Label op'larinin instruction index'ini bulup Br/Brtrue/Brfalse'in Slot'una yazar.
    // Label referans (object identity) ile eslendigi icin ic ice/tekrarli bloklarda
    // ayni isimli label olsa bile hicbir zaman yanlis hedefe atlamaz.
    public static class Linker
    {
        public static void LinkLabels(Code code)
        {
            var positions = new Dictionary<Label, int>();
            for (int i = 0; i < code.Operations.Count; i++)
                if (code.Operations[i].Type == OpType.Label)
                    positions[code.Operations[i].Label] = i;

            foreach (var op in code.Operations)
            {
                if (op.Type == OpType.Br || op.Type == OpType.Brtrue || op.Type == OpType.Brfalse || op.Type == OpType.TryBegin)
                {
                    if (op.Label == null) continue; // binary'den decode edilmis: Slot zaten linkli, Label objesi yok
                    if (!positions.TryGetValue(op.Label, out var target))
                        throw new System.Exception("Cozulemeyen label (goto hedefi bulunamadi)");
                    op.Slot = target;
                }
            }
        }
    }
}
