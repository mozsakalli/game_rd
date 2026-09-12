using System;
using DigitoyEngine;

// [Serializable] = serilestirilebilir; [CreateAssetMenu] = Create menusunde gorunur (opt-in).
[Serializable]
[CreateAssetMenu(MenuName = "Config/Game Settings", FileName = "GameSettings")]
public class GameSettings
{
    public string gameName = "Sandbox";
    public int targetFps = 60;
    public float musicVolume = 1f;
    public bool showFps;
}
