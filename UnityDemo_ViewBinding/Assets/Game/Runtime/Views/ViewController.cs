using System;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// Runtime'da remote prefab'ın köküne AddComponent ile takılan davranış tabanı.
    /// Awake prefab instantiate anında DEĞİL, AddComponent anında koşar; bu yüzden init Initialize'da.
    /// </summary>
    public abstract class ViewController : MonoBehaviour
    {
        public event Action<ViewController> Closed;
        public object Context { get; private set; }

        internal void InternalInitialize(object context)
        {
            Context = context;
            ViewBinder.Bind(this, transform);
            OnInitialize();
        }

        /// <summary>Alanlar bağlandıktan sonra; listener'lar, metinler, başlangıç durumu burada.</summary>
        protected abstract void OnInitialize();

        public void Close()
        {
            OnClose();
            Closed?.Invoke(this);
            Destroy(gameObject);
        }

        protected virtual void OnClose() { }
    }
}
