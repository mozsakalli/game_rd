using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// Demo girişi. Boş bir sahneye bu component'i koyman yeterli: Canvas + EventSystem + iki buton kodla kurulur,
    /// butonlar Resources/Views altındaki saf prefab'ları açar ve davranışı runtime'da takar.
    /// </summary>
    public sealed class DemoBootstrap : MonoBehaviour
    {
        ViewLoader _loader;
        Transform _layer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterViews()
        {
            // Tek tablo: viewId → davranış. Prefab'lar bu tipleri bilmez.
            ViewRegistry.Register<OfferDialog>("offer_starter");
            ViewRegistry.Register<WheelMiniGame>("minigame_wheel");
        }

        void Awake()
        {
            _loader = new ViewLoader(new ResourcesViewSource("Views"));

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var menu = MakeButton(canvasGo.transform, "Open Offer", new Vector2(0, 120));
            menu.onClick.AddListener(() => _loader.Open("OfferDialog", _layer, new OfferData
            {
                Title = "Starter Pack", Description = "Get 500 coins + 3 boosters", Price = 2, Reward = 500
            }));
            var mg = MakeButton(canvasGo.transform, "Open Wheel", new Vector2(0, -120));
            mg.onClick.AddListener(() => _loader.Open("WheelMiniGame", _layer, new WheelConfig()));

            // Popup katmanı menünün üstünde kalsın.
            var layer = new GameObject("PopupLayer", typeof(RectTransform));
            layer.transform.SetParent(canvasGo.transform, false);
            var lrt = (RectTransform)layer.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            _layer = layer.transform;
        }

        static Button MakeButton(Transform parent, string label, Vector2 pos)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(480, 160);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.9f);

            var txt = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txt.transform.SetParent(go.transform, false);
            var trt = (RectTransform)txt.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = txt.GetComponent<Text>();
            t.text = label; t.alignment = TextAnchor.MiddleCenter; t.fontSize = 48; t.color = Color.white;
            t.font = DefaultFont();
            return go.GetComponent<Button>();
        }

        /// <summary>Unity 2022.2+ "LegacyRuntime.ttf", öncesi "Arial.ttf".</summary>
        public static Font DefaultFont()
        {
#if UNITY_2022_2_OR_NEWER
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        }
    }
}
