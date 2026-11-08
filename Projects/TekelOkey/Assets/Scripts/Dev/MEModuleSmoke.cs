using Digibo.Model;
using Digibo.Module;
using DigitoyEngine;

// DEV DOGRULAMA: MEModule + Binding uctan uca (kod sahnesi). Start'ta agac kurulur, frame 2/4'te stdout'a PASS/FAIL.
// Uretim sahnesinde yeri yok; RPC ile bos sahneye eklenip sim.run ile kosulur.
[AddComponentMenu("Dev/ME Module Smoke")]
public sealed class MEModuleSmoke : Component
{
    LayoutBox _name, _money, _face;
    int _frame;

    protected override void Start()
    {
        GlobalModel.ME["name"] = "Oz";
        GlobalModel.ME["money"] = 9_007_199_254_740_993L; // 2^53+1: float/double'da kaybolur, long'da kalmali
        GlobalModel.ME["faceId"] = 7;

        var me = new GameObject("ME");
        me.transform.SetParent(transform, false);
        me.AddComponent<MEModule>();

        _name = Label(me.transform, "Name");
        _money = Label(me.transform, "Money");
        _face = Label(me.transform, "Face");
        var b = me.AddComponent<Binding>();
        b.Add(_name, "Text", "name");
        b.Add(_money, "Text", "money");
        b.Add(_face, "Text", "faceId");
    }

    static LayoutBox Label(Transform parent, string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        return go.AddComponent<LayoutBox>();
    }

    protected override void Update()
    {
        _frame++;
        if (_frame == 2)
        {
            bool ok = _name.Text == "Oz" && _money.Text == "9007199254740993" && _face.Text == "7";
            System.Console.WriteLine($"[me-smoke] {(ok ? "PASS" : "FAIL")} ilk yazim: name='{_name.Text}' money='{_money.Text}' face='{_face.Text}'");
            GlobalModel.ME["money"] = 5L;
            GlobalModel.ME.Remove("faceId");
        }
        else if (_frame == 4)
        {
            bool ok = _money.Text == "5" && _face.Text == "7";
            System.Console.WriteLine($"[me-smoke] {(ok ? "PASS" : "FAIL")} degisim: money='{_money.Text}' (5) face='{_face.Text}' (silinen anahtar: son deger kalir, 7)");
        }
    }
}
