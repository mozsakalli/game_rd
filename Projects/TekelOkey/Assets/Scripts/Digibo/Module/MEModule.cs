using Digibo.Model;
using DigitoyEngine;

// PORT: digiplay-net/Digibo.Module/MEModule.cs (Group2D + IBindingContext, "ME" etiketiyle GlobalModel.ME.Clone()
// dispatch ederdi). Yeni motor: ModuleBase; model = GlobalModel.ME CANLI; alt agactaki Binding'ler "ME.<anahtar>"
// yerine dogrudan "<anahtar>" yoluyla okur (context = ME). Pull oldugu icin Clone/yeniden dispatch yok:
// ME degisince gorunum sonraki frame'de guncellenir (eski davranisin ust kumesi).
namespace Digibo.Module
{
    [AddComponentMenu("Digibo/ME Module")]
    public sealed class MEModule : ModuleBase
    {
        public const string METag = "ME";

        protected override object Model => GlobalModel.ME;
    }
}
