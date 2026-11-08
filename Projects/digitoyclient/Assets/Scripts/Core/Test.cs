using DigitoyEngine;

public class Test : Component
{

    protected override void Awake()
    {
        Digitoy.Server.GameId = 7;
        Digitoy.Server.Start();
    }

    // Sahne yikimi / hot reload: soket motora emanet, sonraki Start geri alir.
    protected override void OnDestroy() => Digitoy.Server.Park();
}