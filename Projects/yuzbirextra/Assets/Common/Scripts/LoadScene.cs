using DigitoyEngine;

public class LoadScene : Component
{
    protected override void Start()
    {
        var op = SceneLoader.LoadAsync("Login/LoginScene.scene");
    }
}