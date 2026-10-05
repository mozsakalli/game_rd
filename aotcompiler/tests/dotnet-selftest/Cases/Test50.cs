using DigitoyEngine;
namespace Demo50
{
    // Engine conformance: CIL frontend'in engine kodunu (Transform/Vec3/Mat4, ref-return,
    // MathF, fixed buffer) .NET ile BIREBIR derleyip derleyemedigini olcer. Float sonuclar
    // Near() ile epsilon-toleransli bit'lere cevrilir (libm vs MathF son-bit sapmasi zararsiz).
    class App50
    {
        static bool Near(float a, float b)
        {
            float d = a - b;
            if (d < 0) d = -d;
            return d < 0.001f;
        }

        public static int Run()
        {
            int acc = 0;

            var root = new Transform();
            root.localScale = new Vec3(1, 1, 1);
            root.localPosition = new Vec3(10, 20, 30);
            var p = root.position;
            if (Near(p.x, 10) && Near(p.y, 20) && Near(p.z, 30)) acc += 1;

            var child = new Transform();
            child.localScale = new Vec3(1, 1, 1);
            child.localPosition = new Vec3(5, 0, 0);
            child.SetParent(root, false);
            if (root.childCount == 1) acc += 2;
            var wc = child.position;
            if (Near(wc.x, 15) && Near(wc.y, 20) && Near(wc.z, 30)) acc += 4;

            if (child.IsChildOf(root)) acc += 8;

            // Parent hareket edince cocugun world'u guncellenir (dirty propagation).
            root.localPosition = new Vec3(0, 0, 0);
            var wc2 = child.position;
            if (Near(wc2.x, 5) && Near(wc2.y, 0) && Near(wc2.z, 0)) acc += 16;

            // Sibling siralama.
            var c2 = new Transform();
            c2.localScale = new Vec3(1, 1, 1);
            c2.SetParent(root, false);
            if (root.childCount == 2) acc += 32;
            if (child.GetSiblingIndex() == 0 && c2.GetSiblingIndex() == 1) acc += 64;
            c2.SetAsFirstSibling();
            if (c2.GetSiblingIndex() == 0 && child.GetSiblingIndex() == 1) acc += 128;

            // World position set -> parent inverse ile local'e cevrilir.
            child.position = new Vec3(100, 0, 0);
            var lp = child.localPosition;
            if (Near(lp.x, 100)) acc += 256;

            // Rotasyon: 90 derece Z. lossyScale ~1 kalmali, eulerAngles roundtrip.
            var r = new Transform();
            r.localScale = new Vec3(1, 1, 1);
            r.localEulerAngles = new Vec3(0, 0, 90);
            var ls = r.lossyScale;
            if (Near(ls.x, 1) && Near(ls.y, 1) && Near(ls.z, 1)) acc += 512;
            var ea = r.eulerAngles;
            if (Near(ea.z, 90)) acc += 1024;

            root.DetachChildren();
            if (root.childCount == 0) acc += 2048;

            return acc;
        }
    }
}
