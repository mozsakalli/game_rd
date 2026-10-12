using System;
using DigitoyEngine;

// [Asset] referans testi: GameSettings.asset Inspector'dan surukleyip baglanir; Awake'te okur.
// Editor Play (YAML) ve pak/player (DOBJ) ayni satiri basmali.
public sealed class SettingsProbe : Component
{
    public GameSettings settings;

    protected override void Awake()
    {
        if (settings == null)
        {
            Console.WriteLine("[probe] settings NULL");
            return;
        }
        Console.WriteLine($"[probe] game={settings.gameName} fps={settings.targetFps} music={settings.musicVolume} showFps={settings.showFps}");
    }
}
