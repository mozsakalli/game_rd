using DigitoyEngine;

// Isaretli GO yalniz Apple platformlarda gorunur (simdilik dummy kontrol).
public class ApplePlatformOnly : Component
{
    protected override void Awake()
    {
        bool isApple = System.OperatingSystem.IsMacOS() || System.OperatingSystem.IsIOS();
        if (!isApple)
            gameObject.SetActive(false);
    }
}