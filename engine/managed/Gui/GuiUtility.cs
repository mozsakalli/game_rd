#if DE_EDITOR
namespace DigitoyEngine.Editor;

// Unity FocusType karsiligi: kontrol keyboard focus alabilir mi?
public enum FocusType : byte
{
    Passive,  // focus almaz (label, group)
    Keyboard, // tab/klik ile keyboard focus alabilir
}

// Unity GUIUtility karsiligi: control ID uretimi + hot/keyboard control +
// kalici kontrol state havuzu.
//
// ID = hint hash'i ile SIRALI SAYAC karisimi (Unity deseni). Sayac her event
// pass'inde sifirlanir; kontrol cagri sirasi tum event'lerde ayni oldugu
// surece ayni kontrol ayni ID'yi alir. Sadece hash(hint) yeterli DEGIL
// (ayni hint'li iki buton cakisir — eski C imgui'nin zayifligi).
public static class GuiUtility
{
    // Mouse'u yakalamis kontrol (MouseDown'da alinir, MouseUp'ta birakilir).
    public static int HotControl;

    // Keyboard focus'lu kontrol.
    public static int KeyboardControl;

    static int _counter;

    // --- Tab navigasyonu: FocusType.Keyboard kontroller pass sirasiyla gezilir ---
    // MoveFocus istegi bir TAM pass izlenerek cozulur (istek pass ortasinda
    // gelir; oncesindeki kontroller o pass'te kaydedilmemistir). Cozum sonraki
    // BeginPass'te KeyboardControl'e yazilir; yeni kontrol ConsumeTabFocus ile
    // odagi devraldigini ogrenir (select-all / edit moduna giris icin).
    static bool _tabPending, _tabFresh, _tabBackward, _tabSeenFrom;
    static int _tabFrom, _tabFocusId;
    static int _passFirstKb, _passLastKb, _passBeforeFrom, _passAfterFrom;

    // Tab/Shift+Tab: odagi fromId'den sonraki/onceki keyboard kontrole tasi.
    public static void MoveFocus(int fromId, bool backward)
    {
        _tabPending = true;
        _tabFresh = true;
        _tabFrom = fromId;
        _tabBackward = backward;
    }

    // Kontrol tab ile odak aldiysa bir kez true doner (widget select-all yapar).
    public static bool ConsumeTabFocus(int controlId)
    {
        if (_tabFocusId != controlId)
            return false;
        _tabFocusId = 0;
        return true;
    }

    // Bir event pass'i baslatir: sayac sifirlanir, Event.Current atanir,
    // clip stack'i ekran rect'iyle kurulur. Host her event icin cagirir.
    public static void BeginPass(Event ev, Rect screen)
    {
        if (_tabPending)
        {
            if (_tabFresh)
            {
                _tabFresh = false; // istek pass ortasinda geldi; once tam bir pass izle
            }
            else
            {
                int target = _tabBackward
                    ? (_passBeforeFrom != 0 ? _passBeforeFrom : _passLastKb)   // geri: onceki, yoksa sona sar
                    : (_passAfterFrom != 0 ? _passAfterFrom : _passFirstKb);   // ileri: sonraki, yoksa basa sar
                _tabPending = false;
                if (target != 0 && target != _tabFrom)
                {
                    KeyboardControl = target;
                    _tabFocusId = target;
                }
            }
        }
        _passFirstKb = _passLastKb = _passBeforeFrom = _passAfterFrom = 0;
        _tabSeenFrom = false;
        _counter = 0;
        Event.Current = ev;
        GuiClip.Reset(screen, ev);
    }

    public static int GetControlID(int hint, FocusType focusType)
    {
        _counter++;
        // Fibonacci karisim: hint ve sira birlikte deterministik benzersiz id.
        unchecked
        {
            uint h = (uint)hint * 0x9E3779B9u;
            h ^= (uint)_counter * 0x85EBCA6Bu;
            h ^= h >> 16;
            int id = (int)h;
            if (id == 0)
                id = 1; // 0 = "kontrol yok" sentineli
            if (focusType == FocusType.Keyboard)
                TrackKeyboard(id);
            return id;
        }
    }

    static void TrackKeyboard(int id)
    {
        if (_passFirstKb == 0)
            _passFirstKb = id;
        if (_tabPending)
        {
            if (id == _tabFrom)
            {
                _tabSeenFrom = true;
                _passBeforeFrom = _passLastKb;
            }
            else if (_tabSeenFrom && _passAfterFrom == 0)
            {
                _passAfterFrom = id;
            }
        }
        _passLastKb = id;
    }

    // Kalici per-control state (Unity GUIStateObjects'in boxing'siz hali).
    // Tip basina statik open-addressing havuz; ref doner, alloc yok.
    public static ref T GetState<T>(int controlId) where T : struct
        => ref GuiState<T>.GetRef(controlId);
}

// Tip basina int-anahtarli open-addressing hash tablosu (struct, boxing yok).
// Sadece buyur (editor UI kontrol sayisi sinirli); steady-state alloc yok.
static class GuiState<T> where T : struct
{
    static int[] _keys = new int[256];   // 0 = bos slot
    static T[] _values = new T[256];
    static int _count;

    public static ref T GetRef(int key)
    {
        if (key == 0)
            key = 1;
        if (_count * 4 >= _keys.Length * 3)
            Grow();
        int mask = _keys.Length - 1;
        int i = (key * -1640531527) & mask; // knuth karisim
        while (true)
        {
            int k = _keys[i];
            if (k == key)
                return ref _values[i];
            if (k == 0)
            {
                _keys[i] = key;
                _count++;
                return ref _values[i];
            }
            i = (i + 1) & mask;
        }
    }

    static void Grow()
    {
        int[] oldKeys = _keys;
        T[] oldValues = _values;
        _keys = new int[oldKeys.Length * 2];
        _values = new T[oldValues.Length * 2];
        _count = 0;
        for (int j = 0; j < oldKeys.Length; j++)
            if (oldKeys[j] != 0)
                GetRef(oldKeys[j]) = oldValues[j];
    }
}
#endif
