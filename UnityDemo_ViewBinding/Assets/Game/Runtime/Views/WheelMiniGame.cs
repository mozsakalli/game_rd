using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    public sealed class WheelConfig
    {
        public int[] Prizes = { 10, 50, 100, 20, 500, 5, 200, 0 };
        public float SpinSeconds = 3f;
    }

    /// <summary>
    /// Remote "çark" minigame'i. Prefab'ta sadece Image/Button/Text; dönme, sonuç, ödül oyun kodunda.
    /// </summary>
    public sealed class WheelMiniGame : ViewController
    {
        [Bind("wheel")] RectTransform _wheel;
        [Bind("spin")] Button _spin;
        [Bind("result")] Text _result;
        [Bind("close")] Button _close;

        WheelConfig Cfg => (WheelConfig)Context;
        bool _spinning;

        protected override void OnInitialize()
        {
            _result.text = "Spin!";
            _spin.onClick.AddListener(Spin);
            _close.onClick.AddListener(Close);
        }

        void Spin()
        {
            if (_spinning) return;
            _spinning = true;
            _spin.interactable = false;
            int idx = Random.Range(0, Cfg.Prizes.Length);
            StartCoroutine(SpinTo(idx));
        }

        System.Collections.IEnumerator SpinTo(int idx)
        {
            float slice = 360f / Cfg.Prizes.Length;
            // İbre üstte (0°), çark saat yönünde döner → hedef dilim merkezi üste gelsin.
            float target = 360f * 4f + idx * slice + slice * 0.5f;
            float start = _wheel.localEulerAngles.z;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Cfg.SpinSeconds;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f); // OutCubic
                _wheel.localEulerAngles = new Vector3(0, 0, -(start + Mathf.Lerp(0, target, e)));
                yield return null;
            }
            _result.text = Cfg.Prizes[idx] == 0 ? "No luck!" : $"+{Cfg.Prizes[idx]} coins";
            Debug.Log($"[WheelMiniGame] result index {idx} → {Cfg.Prizes[idx]}");
            _spinning = false;
            _spin.interactable = true;
        }
    }
}
