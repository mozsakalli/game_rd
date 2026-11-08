using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Test tezgahı: sahneyi runtime'da kurar, OnGUI butonlarıyla karışık transfer/morph senaryoları tetikler.
[RequireComponent(typeof(GinGame))]
public class BoardTestBench : MonoBehaviour
{
    GinGame _game;
    RectTransform _root;
    bool _meldZoomed;
    float _zoomT = 1f;
    readonly System.Random _rng = new System.Random();

    void Start()
    {
        _game = GetComponent<GinGame>();
        _root = transform as RectTransform;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform.parent, false);
        }

        var dragLayer = MakeRect("DragLayer", Vector2.zero);
        _game.dragLayer = dragLayer;

        _game.deck = MakeZone<StackZone>("Deck", new Vector2(-150, 0), z =>
        {
            z.faceUp = false;
            z.offset = new Vector3(0.6f, 0.6f, 0);
            z.compactAfter = 12;
            z.positionJitter = new Vector2(2f, 2f);
            z.rotationJitter = 3f;
        });
        _game.discard = MakeZone<StackZone>("Discard", new Vector2(150, 0), z =>
        {
            z.offset = new Vector3(1.5f, 0, 0);
            z.compactAfter = 6;
            z.flyOnTop = true;
            z.positionJitter = new Vector2(8f, 8f);
            z.rotationJitter = 12f;
            z.dropRect = new Rect(-110, -110, 220, 220);
        });
        _game.hand = MakeZone<RowZone>("Hand", new Vector2(0, -400), z =>
        {
            z.spacing = 75;
            z.maxSpreadCount = 12;
            z.flyOnTop = true;
            z.dropRect = new Rect(-700, -130, 1400, 260);
        });
        _game.seats = new[]
        {
            MakeZone<SeatZone>("SeatLeft", new Vector2(-820, 300), z => { z.faceUp = false; z.pieceScale = 0.4f; }),
            MakeZone<SeatZone>("SeatTop", new Vector2(0, 460), z => { z.faceUp = false; z.pieceScale = 0.4f; }),
            MakeZone<SeatZone>("SeatRight", new Vector2(820, 300), z => { z.faceUp = false; z.pieceScale = 0.4f; }),
        };
        foreach (var s in _game.seats) AddAvatar(s);
        _game.melds = new BoardZone[]
        {
            MakeZone<RowZone>("Meld0", new Vector2(-560, 150), z => { z.spacing = 45; z.transform.localScale = Vector3.one * 0.7f; z.dropRect = new Rect(-300, -80, 600, 160); }),
            // Sağa ve aşağı (satırlar) büyür: alan orijinden başlar, 2 satır.
            MakeZone<AnchoredRowZone>("Meld1", new Vector2(400, 150), z => { z.spacing = 45; z.transform.localScale = Vector3.one * 0.7f; z.dropRect = new Rect(-40, -170, 560, 240); }),
        };
        dragLayer.SetAsLastSibling();

        ResetDeck();
    }

    RectTransform MakeRect(string name, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_root, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = pos;
        return rt;
    }

    TZone MakeZone<TZone>(string name, Vector2 pos, System.Action<TZone> setup) where TZone : BoardZone
    {
        var rt = MakeRect(name, pos);
        var z = rt.gameObject.AddComponent<TZone>();
        z.defaultTransition = Transition.Default;
        setup?.Invoke(z);
        _game.RegisterZone(z);
        return z;
    }

    // Koltuğu temsil eden basit avatar dairesi + kart sayacı.
    void AddAvatar(SeatZone seat)
    {
        var go = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(seat.transform, false);
        rt.sizeDelta = new Vector2(110, 110);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.2f, 0.4f, 0.8f, 0.9f);
        img.raycastTarget = false;
        var label = new GameObject("Count", typeof(RectTransform), typeof(Text));
        var lrt = label.GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.sizeDelta = new Vector2(110, 110);
        var t = label.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 36;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
    }

    // --- Senaryolar ---

    void ResetDeck()
    {
        _game.ReleaseAll();
        foreach (var s in _game.seats) s.count = 0;
        var nos = new List<int>();
        for (var i = 0; i < 52; i++) nos.Add(i);
        Shuffle(nos);
        for (var i = 0; i < nos.Count; i++)
            _game.SetSlot(_game.AllocPiece(nos[i]), _game.deck, transition: Transition.Instant);
    }

    // Deste → el (son anda açılır) ve her koltuğa (kapalı, varınca yok olur).
    void Deal(int perPlayer)
    {
        var tr = Transition.Default.WithDuration(0.45f);
        var k = 0;
        for (var i = 0; i < perPlayer; i++)
        {
            if (_game.deck.Count == 0) return;
            _game.SetSlot(_game.deck.Top, _game.hand, transition: tr.WithDelay(k++ * 0.08f).WithFlipAt(0.85f));
            foreach (var s in _game.seats)
            {
                if (_game.deck.Count == 0) return;
                _game.SetSlot(_game.deck.Top, s, transition: tr.WithDelay(k++ * 0.08f));
            }
        }
    }

    BoardPieceBase RandomOf(BoardZone z) => z.Count == 0 ? null : z.pieces[_rng.Next(z.Count)];
    SeatZone RandomSeat() => _game.seats[_rng.Next(_game.seats.Length)];

    void HandToMeld(int count)
    {
        var meld = _game.melds[_rng.Next(_game.melds.Length)];
        for (var i = 0; i < count; i++)
        {
            var c = RandomOf(_game.hand);
            if (c == null) return;
            _game.SetSlot(c, meld, transition: Transition.Default.WithDelay(i * 0.05f));
        }
    }

    void MeldsToHand()
    {
        foreach (var m in _game.melds)
            while (m.Count > 0) _game.SetSlot(m.pieces[0], _game.hand);
    }

    // Okey değişimi: eldeki rastgele kart meld'deki rastgele kartın yerine, o kart ele (üstten uçarak).
    void SwapHandMeld()
    {
        var meld = _game.melds[_rng.Next(_game.melds.Length)];
        var a = RandomOf(_game.hand);
        var b = RandomOf(meld);
        if (a == null || b == null) return;
        _game.Swap(a, b, Transition.Default.WithLift(0.2f), Transition.Default.WithLift(0.3f).WithDelay(0.1f));
    }

    // Rakip ıskartaya atar: kart koltuktan doğar, yolda açılır.
    void OpponentDiscards()
    {
        var seat = RandomSeat();
        var no = _rng.Next(52);
        _game.Spawn(no, seat, _game.discard, transition: Transition.Default.WithDuration(0.6f).WithFlipAt(0.6f).WithLift(0.25f));
    }

    // Rakip ıskartadan/desteden çeker: kart koltuğa uçar, varınca yok olur.
    void OpponentPicks(BoardZone from)
    {
        if (from.Top == null) return;
        _game.SetSlot(from.Top, RandomSeat(), transition: Transition.Default.WithFlipAt(0.4f));
    }

    void ShuffleHand()
    {
        var list = new List<BoardPieceBase>(_game.hand.pieces);
        Shuffle(list);
        for (var i = 0; i < list.Count; i++)
            _game.SetSlot(list[i], _game.hand, i);
    }

    // Uçuş ortasında retarget stres testi: rastgele 8 hamle arka arkaya.
    void Chaos()
    {
        var zones = new List<BoardZone> { _game.hand, _game.discard, _game.melds[0], _game.melds[1], _game.seats[0], _game.seats[1], _game.seats[2] };
        for (var i = 0; i < 8; i++)
        {
            var from = zones[_rng.Next(zones.Count)];
            var to = zones[_rng.Next(zones.Count)];
            if (from == to) continue;
            if (from.HoldsPieces)
            {
                var c = RandomOf(from);
                if (c == null) continue;
                _game.SetSlot(c, to, transition: Transition.Default.WithDelay(_rng.Next(0, 30) * 0.01f));
            }
            else if (to.HoldsPieces)
            {
                _game.Spawn(_rng.Next(52), from, to, transition: Transition.Default.WithDelay(_rng.Next(0, 30) * 0.01f));
            }
        }
    }

    void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void Update()
    {
        // Koltuk sayaçları
        foreach (var s in _game.seats)
        {
            var t = s.GetComponentInChildren<Text>();
            if (t != null) t.text = s.count.ToString();
        }

        // Meld zoom: zone scale/pos animasyonu — içindeki kartlar parent ile taşınmalı.
        var target = _meldZoomed ? 0f : 1f;
        if (!Mathf.Approximately(_zoomT, target))
        {
            _zoomT = Mathf.MoveTowards(_zoomT, target, Time.deltaTime / 0.35f);
            var t = Mathf.SmoothStep(0, 1, 1f - _zoomT);
            for (var i = 0; i < _game.melds.Length; i++)
            {
                var rt = _game.melds[i].transform as RectTransform;
                var basePos = new Vector2(i == 0 ? -560 : 560, 150);
                var zoomPos = new Vector2(i == 0 ? -300 : 300, 100);
                rt.anchoredPosition = Vector2.Lerp(basePos, zoomPos, t);
                rt.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.3f, t);
            }
        }
    }

    void OnGUI()
    {
        GUI.skin.button.fontSize = 18;
        var x = 10f; var y = 10f; const float w = 230f, h = 36f;
        bool B(string label) { var r = GUI.Button(new Rect(x, y, w, h), label); y += h + 4; return r; }

        if (B("Reset deck")) ResetDeck();
        if (B("Deal 10 each")) Deal(10);
        if (B("Pick deck -> hand") && _game.deck.Top != null) _game.SetSlot(_game.deck.Top, _game.hand);
        if (B("Pick discard -> hand") && _game.discard.Top != null) _game.SetSlot(_game.discard.Top, _game.hand);
        if (B("Discard random")) { var c = RandomOf(_game.hand); if (c != null) _game.SetSlot(c, _game.discard, transition: Transition.Default.WithFlipAt(0.3f)); }
        if (B("Hand -> meld x3")) HandToMeld(3);
        if (B("Melds -> hand")) MeldsToHand();
        if (B("Swap hand <-> meld")) SwapHandMeld();
        if (B("Opponent discards")) OpponentDiscards();
        if (B("Opponent picks discard")) OpponentPicks(_game.discard);
        if (B("Opponent picks deck")) OpponentPicks(_game.deck);
        if (B("Shuffle hand")) ShuffleHand();
        if (B("Chaos x8")) Chaos();
        if (B(_meldZoomed ? "Meld zoom OFF" : "Meld zoom ON")) _meldZoomed = !_meldZoomed;
        y += 10;
        GUI.Label(new Rect(x, y, w, h), $"timeScale {Time.timeScale:0.0}"); y += h;
        Time.timeScale = GUI.HorizontalSlider(new Rect(x, y, w, h), Time.timeScale, 0.05f, 2f);
    }
}
