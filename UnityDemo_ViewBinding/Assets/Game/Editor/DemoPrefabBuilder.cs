using System.IO;
using Game.ViewContract;
using Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor
{
    /// <summary>
    /// Demo prefab'larını kodla üretir (YAML elle yazmak kırılgan). Üretilen prefab'larda OYUN SCRIPT'İ YOK:
    /// yalnızca uGUI built-in'leri + kökte donmuş ViewRefs. Menü: Tools/ViewBinding Demo/Build Demo Prefabs.
    /// </summary>
    public static class DemoPrefabBuilder
    {
        const string Folder = "Assets/Resources/Views";

        [MenuItem("Tools/ViewBinding Demo/Build Demo Prefabs")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Folder);
            Save(BuildOfferDialog(), "OfferDialog");
            Save(BuildWheel(), "WheelMiniGame");
            AssetDatabase.Refresh();
            Debug.Log("[DemoPrefabBuilder] prefabs written to " + Folder);
        }

        static void Save(GameObject root, string name)
        {
            string path = $"{Folder}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        // ---------------- Offer dialog ----------------
        static GameObject BuildOfferDialog()
        {
            var root = FullRect("OfferDialog");
            var refs = root.AddComponent<ViewRefs>();
            refs.viewId = "offer_starter";

            var dim = Image(root.transform, "Dim", new Color(0, 0, 0, 0.6f));
            Stretch(dim);

            var panel = Image(root.transform, "@panel", new Color(0.95f, 0.95f, 0.97f));
            Size(panel, 820, 1000);
            refs.Set("panel", panel);

            refs.Set("title", Label(panel.transform, "Title", "TITLE", 64, new Vector2(0, 380), 700, 100, FontStyle.Bold));
            refs.Set("desc", Label(panel.transform, "Desc", "description", 40, new Vector2(0, 150), 700, 300));

            var badge = Image(panel.transform, "Badge", new Color(0.95f, 0.35f, 0.2f));
            Size(badge, 220, 70); ((RectTransform)badge.transform).anchoredPosition = new Vector2(300, 470);
            Label(badge.transform, "Text", "BEST VALUE", 28, Vector2.zero, 220, 70, FontStyle.Bold).color = Color.white;
            refs.Set("badge", badge);

            var buy = Button(panel.transform, "Buy", new Color(0.2f, 0.7f, 0.3f), new Vector2(0, -250), 500, 140, "BUY", out var priceLabel);
            refs.Set("buy", buy);
            refs.Set("price", priceLabel);

            var close = Button(panel.transform, "Close", new Color(0.6f, 0.6f, 0.65f), new Vector2(360, 460), 80, 80, "X", out _);
            refs.Set("close", close);
            return root;
        }

        // ---------------- Wheel minigame ----------------
        static GameObject BuildWheel()
        {
            var root = FullRect("WheelMiniGame");
            var refs = root.AddComponent<ViewRefs>();
            refs.viewId = "minigame_wheel";

            var dim = Image(root.transform, "Dim", new Color(0, 0, 0, 0.7f));
            Stretch(dim);

            // Çark: 8 renkli dilim yerine 8 renkli "kol" (sprite gerektirmeden görünür olsun)
            var wheel = new GameObject("@wheel", typeof(RectTransform));
            wheel.transform.SetParent(root.transform, false);
            Size(wheel, 700, 700);
            Color[] cols = { Color.red, Color.yellow, Color.green, Color.cyan, Color.blue, Color.magenta, Color.white, Color.gray };
            for (int i = 0; i < 8; i++)
            {
                var arm = Image(wheel.transform, $"Slice{i}", cols[i]);
                var rt = (RectTransform)arm.transform;
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(90, 330);
                rt.anchoredPosition = Vector2.zero;
                rt.localEulerAngles = new Vector3(0, 0, -i * 45f);
                var n = Label(arm.transform, "N", i.ToString(), 36, new Vector2(0, 250), 90, 60, FontStyle.Bold);
                n.color = Color.black;
            }
            refs.Set("wheel", wheel);

            var pointer = Image(root.transform, "Pointer", Color.black);
            Size(pointer, 30, 90); ((RectTransform)pointer.transform).anchoredPosition = new Vector2(0, 360);

            refs.Set("result", Label(root.transform, "Result", "Spin!", 56, new Vector2(0, 520), 800, 100, FontStyle.Bold));
            refs.Set("spin", Button(root.transform, "Spin", new Color(0.9f, 0.6f, 0.1f), new Vector2(0, -520), 420, 140, "SPIN", out _));
            refs.Set("close", Button(root.transform, "Close", new Color(0.6f, 0.6f, 0.65f), new Vector2(0, -720), 300, 100, "CLOSE", out _));
            return root;
        }

        // ---------------- uGUI yardımcıları ----------------
        static GameObject FullRect(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Stretch(go);
            return go;
        }

        static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Size(GameObject go, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
        }

        static GameObject Image(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            return go;
        }

        static Text Label(Transform parent, string name, string text, int size, Vector2 pos, float w, float h, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Size(go, w, h);
            ((RectTransform)go.transform).anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.text = text; t.fontSize = size; t.fontStyle = style; t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.1f, 0.1f, 0.15f); t.font = DemoBootstrap.DefaultFont();
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static Button Button(Transform parent, string name, Color c, Vector2 pos, float w, float h, string label, out Text text)
        {
            var go = Image(parent, name, c);
            go.AddComponent<Button>();
            Size(go, w, h);
            ((RectTransform)go.transform).anchoredPosition = pos;
            text = Label(go.transform, "Label", label, 44, Vector2.zero, w, h, FontStyle.Bold);
            text.color = Color.white;
            return go.GetComponent<Button>();
        }
    }
}
