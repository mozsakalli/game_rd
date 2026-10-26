using System;
using System.Collections.Generic;
using DigitoyEngine;

// Sahte sunucu: zaman damgali masa olaylari. Gercek sunucu gibi DAVRANIR: olaylari sunumun hizina bakmadan,
// kendi takvimiyle yayar (dagitim 20 kart tek anda gelir; sira/discard hemen arkasindan). Director bunlarla bas eder.
public enum TableEventKind : byte { Dealt, Discarded, Picked, DrewFromDeck }

public struct TableEvent
{
    public float Time;
    public TableEventKind Kind;
    public int Seat;
    public int Card; // kart kimligi; Picked icin yerden alinan kart
}

public static class FakeServer
{
    // 4 koltuk × 5 kart, sonra kisa bir tur: 0 atar, 1 yerden alir, 1 atar, 2 desteden ceker, 2 atar.
    public static List<TableEvent> Script()
    {
        var list = new List<TableEvent>();
        int id = 0;
        for (int round = 0; round < 5; round++)
            for (int seat = 0; seat < 4; seat++)
                list.Add(new TableEvent { Time = 0.5f, Kind = TableEventKind.Dealt, Seat = seat, Card = id++ });
        // Sunucu dagitimin bittigini beklemez: 1.2 sn sonra oyun basliyor (sunum ~2.1 sn surer → cakisma testi).
        list.Add(new TableEvent { Time = 1.2f, Kind = TableEventKind.Discarded, Seat = 0, Card = 4 });
        list.Add(new TableEvent { Time = 2.6f, Kind = TableEventKind.Picked, Seat = 1, Card = 4 });
        list.Add(new TableEvent { Time = 3.4f, Kind = TableEventKind.Discarded, Seat = 1, Card = 9 });
        list.Add(new TableEvent { Time = 4.4f, Kind = TableEventKind.DrewFromDeck, Seat = 2, Card = id++ });
        list.Add(new TableEvent { Time = 5.2f, Kind = TableEventKind.Discarded, Seat = 2, Card = 2 });
        return list;
    }
}
