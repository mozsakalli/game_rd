using UnityEngine;
using UnityEngine.EventSystems;

public class GinGame : BoardGame<GinCard>
{
    // Test tezgahı runtime'da doldurur.
    public StackZone deck, discard;
    public RowZone hand;
    public SeatZone[] seats;
    public BoardZone[] melds;

    // Senaryo 1: deck/ıskarta üstüne basınca kart anında ele alınır (yolda açılır) ve aynı pointer'la drag başlar.
    // Eldeki/meld'deki kart doğrudan drag. Bırakma kuralları (1b) gelene kadar bırakınca slot'una döner.
    protected override void OnPiecePointerDown(GinCard card, PointerEventData ev)
    {
        var z = card.slot.zone;
        if (z == null) return;
        if (z == deck || z == discard)
        {
            if (card != z.Top) return;
            SetSlot(card, hand, transition: Transition.Default.WithDuration(0.35f).WithFlipAt(0.4f));
        }
        BeginDrag(card, ev);
    }

    // Deste ve koltuklara bırakılmaz; el, ıskarta ve meld'ler serbest (test).
    public override bool CanDrop(GinCard card, BoardZone zone) => zone != deck;
}