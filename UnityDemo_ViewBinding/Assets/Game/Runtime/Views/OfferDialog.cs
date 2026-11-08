using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    public sealed class OfferData
    {
        public string Title;
        public string Description;
        public int Price;
        public int Reward;
    }

    /// <summary>
    /// Remote "offer dialog" prefab'ının davranışı. Prefab'ta yalnızca uGUI built-in'leri var;
    /// her bağ [Bind] ile, her tıklama AddListener ile.
    /// </summary>
    public sealed class OfferDialog : ViewController
    {
        [Bind("title")] Text _title;
        [Bind("desc")] Text _desc;
        [Bind("price")] Text _price;
        [Bind("buy")] Button _buy;
        [Bind("close")] Button _close;
        [Bind("panel")] RectTransform _panel;
        [Bind("badge", Optional = true)] GameObject _badge;   // prefab sürümüne göre olmayabilir

        OfferData Data => (OfferData)Context;

        protected override void OnInitialize()
        {
            _title.text = Data.Title;
            _desc.text = Data.Description;
            _price.text = $"${Data.Price}";
            _buy.onClick.AddListener(OnBuy);
            _close.onClick.AddListener(Close);
            if (_badge != null) _badge.SetActive(Data.Reward > 100);

            _panel.localScale = Vector3.zero;
            StartCoroutine(PopIn());
        }

        System.Collections.IEnumerator PopIn()
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 4f;
                float x = Mathf.Clamp01(t) - 1f;
                float s = 1f + c3 * x * x * x + c1 * x * x; // OutBack
                _panel.localScale = Vector3.one * s;
                yield return null;
            }
            _panel.localScale = Vector3.one;
        }

        void OnBuy()
        {
            Debug.Log($"[OfferDialog] purchased '{Data.Title}' for ${Data.Price}, reward {Data.Reward}");
            Close();
        }

        /// <summary>Animation Event hedefi: prefab'taki clip'e "OnIntroDone" event'i konursa buraya düşer (script referansı gerekmez).</summary>
        void OnIntroDone() => Debug.Log("[OfferDialog] intro done (animation event)");
    }
}
