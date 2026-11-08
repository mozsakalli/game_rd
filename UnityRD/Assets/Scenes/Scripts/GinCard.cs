using UnityEngine;
using UnityEngine.UI;

public class GinCard : BoardPiece<GinCard>
{
    public Sprite[] cardSprites;
    public Sprite backSprite;
    public Sprite shadowSprite;

    Image _face;
    Image _group;

    void Awake()
    {
        var face = transform.Find("FACE");
        var group = transform.Find("GROUP");
        _face = face != null ? face.GetComponent<Image>() : null;
        _group = group != null ? group.GetComponent<Image>() : null;
        if (_group != null) _group.enabled = false;
    }

    public override void UpdateVisual()
    {
        if (_face == null) return;
        _face.sprite = pose.faceUp && no >= 0 && no < cardSprites.Length ? cardSprites[no] : backSprite;
    }

    protected override void OnFaceChanged(bool faceUp) => UpdateVisual();

    public override void OnStartedToMove() => gameObject.SetActive(true);
}