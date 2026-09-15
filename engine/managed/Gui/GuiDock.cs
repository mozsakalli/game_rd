#if DE_EDITOR
using System;

namespace DigitoyEngine.Editor;

// Dockable panel sistemi (eski imgui.c _imgui_dock_node agacinin C# hali).
// SANAL pencere yok: paneller dock agacinda tab olarak yasar; tab dock alani
// DISINA suruklenirse gercek NATIVE pencereye detach olur, pencere kapaninca
// redock eder.
//
// KURAL: agac mutasyonlari (detach/redock/aktif tab degisimi) FRAME ORTASINDA
// uygulanmaz — pending'e yazilir, bir SONRAKI frame'in Layout pass'i basinda
// uygulanir. Aksi halde ayni frame'in pass'leri arasinda kontrol sirasi degisir
// (layout replay invariant'i kirilir).
public static class GuiDock
{
    // Panel icerigi aktif area'nin LOKAL uzayinda cizilir (dock leaf'i veya
    // detached pencere — ikisinde de ayni func calisir).
    public delegate void PanelFunc(int panelId);

    // --- Panel kaydi ---

    struct Panel
    {
        public string Title;
        public PanelFunc Draw;
        public bool Used;
        public Color TabColor;
    }

    const int MaxPanels = 32;
    static readonly Panel[] _panels = new Panel[MaxPanels];
    static int _panelCount;

    public static int RegisterPanel(string title, PanelFunc draw)
    {
        int id = _panelCount++;
        ref Panel p = ref _panels[id];
        p.Title = title;
        p.Draw = draw;
        p.Used = true;
        // Text yokken tab'lari ayirt etmek icin panel-bazli renk.
        uint h = (uint)(id + 1) * 0x9E3779B9u;
        p.TabColor = new Color((byte)(90 + (h & 63)), (byte)(90 + ((h >> 8) & 63)), (byte)(110 + ((h >> 16) & 63)), 255);
        return id;
    }

    // --- Dock agaci (struct havuzu; tab listeleri ayri duz dizide) ---

    const byte TypeLeaf = 0, TypeSplitH = 1, TypeSplitV = 2;
    const int MaxNodes = 64, MaxTabsPerLeaf = 8;

    struct Node
    {
        public byte Type;
        public bool Used;
        public float Ratio;      // split: A'nin payi
        public int A, B, Parent;
        public int TabCount;     // leaf
        public int Active;       // leaf: aktif tab indeksi
        public Rect Rect;
    }

    static readonly Node[] _nodes = new Node[MaxNodes];
    static readonly int[] _tabs = new int[MaxNodes * MaxTabsPerLeaf]; // [node*8+i] = panelId
    static int _nodeCount;
    static int _root = -1;

    // --- Coklu dock host: her detached native pencere kendi leaf'ini barindirir ---
    const int MaxWins = 16;
    struct DockWin
    {
        public bool Used;
        public NativeWindow Win; // null = kapandi, tablari ana agaca donecek
        public int Root;         // node havuzunda leaf
    }
    static readonly DockWin[] _wins = new DockWin[MaxWins];
    static int _winCount;
    static int _ctxWin = -1; // su an cizilen agacin penceresi (-1 = ana pencere)

    static int RootOf(int wi) => wi < 0 ? _root : _wins[wi].Root;
    static IntPtr HandleOf(int wi) => wi < 0 ? NativeWindow.MainWindow : _wins[wi].Win.Handle;

    static int AllocWin()
    {
        for (int i = 0; i < MaxWins; i++)
            if (!_wins[i].Used)
            {
                if (i >= _winCount)
                    _winCount = i + 1;
                return i;
            }
        return -1;
    }

    static int FindLeafWithPanelAll(int panelId)
    {
        int r = FindLeafWithPanel(_root, panelId);
        if (r >= 0)
            return r;
        for (int i = 0; i < _winCount; i++)
        {
            if (!_wins[i].Used)
                continue;
            r = FindLeafWithPanel(_wins[i].Root, panelId);
            if (r >= 0)
                return r;
        }
        return -1;
    }

    public static int Leaf(ReadOnlySpan<int> panelIds)
    {
        int ni = AllocNode();
        ref Node n = ref _nodes[ni];
        n.Type = TypeLeaf;
        n.TabCount = Math.Min(panelIds.Length, MaxTabsPerLeaf);
        for (int i = 0; i < n.TabCount; i++)
            _tabs[ni * MaxTabsPerLeaf + i] = panelIds[i];
        return ni;
    }

    public static int Split(bool horizontal, float ratio, int a, int b)
    {
        int ni = AllocNode();
        ref Node n = ref _nodes[ni];
        n.Type = horizontal ? TypeSplitH : TypeSplitV;
        n.Ratio = ratio;
        n.A = a;
        n.B = b;
        _nodes[a].Parent = ni;
        _nodes[b].Parent = ni;
        return ni;
    }

    public static void SetRoot(int node) => _root = node;

    // --- Layout kalicilik: agac panel TITLE'lariyla yazilir (panel id'leri kayit
    // sirasina bagli oldugundan diske id yazilmaz). v2: native dock pencereleri de
    // "W x y w h <agac>" kayitlariyla saklanir; dosyada olmayan kayitli paneller
    // yuklemede ilk leaf'e eklenir.
    public static bool SaveLayout(string path)
    {
        if (_root < 0)
            return false;
        var sb = new System.Text.StringBuilder("v2 ");
        WriteNode(sb, _root);
        for (int i = 0; i < _winCount; i++)
        {
            ref DockWin w = ref _wins[i];
            if (!w.Used || w.Win == null)
                continue;
            GLFW.GetWindowPos(w.Win.Handle, out int wx, out int wy);
            // Boyut EKRAN biriminde yazilir (Open ayni birimi bekler); Width/Height
            // framebuffer px'tir — macOS retina'da her kayit/yukleme boyutu ikiye katlardi.
            GLFW.GetWindowSize(w.Win.Handle, out int sww, out int swh);
            sb.Append(" W ").Append(wx).Append(' ').Append(wy).Append(' ')
              .Append(sww).Append(' ').Append(swh).Append(' ');
            WriteNode(sb, w.Root);
        }
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, sb.ToString());
            return true;
        }
        catch { return false; }
    }

    static void WriteNode(System.Text.StringBuilder sb, int ni)
    {
        ref Node n = ref _nodes[ni];
        if (n.Type == TypeLeaf)
        {
            sb.Append("(L ").Append(n.Active).Append(' ');
            for (int i = 0; i < n.TabCount; i++)
            {
                if (i > 0)
                    sb.Append('|');
                sb.Append(_panels[_tabs[ni * MaxTabsPerLeaf + i]].Title);
            }
            sb.Append(')');
        }
        else
        {
            sb.Append(n.Type == TypeSplitH ? "(H " : "(V ")
              .Append(n.Ratio.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            WriteNode(sb, n.A);
            sb.Append(' ');
            WriteNode(sb, n.B);
            sb.Append(')');
        }
    }

    // Paneller KAYITLI olduktan sonra cagrilir. Basarisizsa agac bos kalir —
    // cagiran varsayilan layout'u kurmali.
    public static bool LoadLayout(string path)
    {
        string s;
        try { s = System.IO.File.ReadAllText(path); }
        catch { return false; }
        bool v2 = s.StartsWith("v2 ");
        if (!v2 && !s.StartsWith("v1 "))
            return false;
        for (int i = 0; i < MaxNodes; i++)
            _nodes[i].Used = false;
        _root = -1;
        int pos = 3;
        int root = ParseNode(s, ref pos, out bool ok);
        if (!ok || root < 0)
        {
            for (int i = 0; i < MaxNodes; i++)
                _nodes[i].Used = false;
            return false;
        }
        _root = root;
        // v2: pencere kayitlari — acilamayan/bozuk pencere sessizce atlanir
        // (panelleri asagidaki 'kayip panel' toplayicisi ilk leaf'e alir).
        while (v2)
        {
            while (pos < s.Length && s[pos] == ' ')
                pos++;
            if (pos >= s.Length || s[pos] != 'W')
                break;
            pos += 2; // "W "
            if (!ParseInt(s, ref pos, out int wx) || !ParseInt(s, ref pos, out int wy)
                || !ParseInt(s, ref pos, out int ww) || !ParseInt(s, ref pos, out int wh))
                break;
            int wroot = ParseNode(s, ref pos, out bool wok);
            if (!wok || wroot < 0)
                break;
            ClampToVisible(ref wx, ref wy, ref ww, ref wh); // ekran disi kayit kurtarilir
            int wi = AllocWin();
            var win = wi >= 0
                ? NativeWindow.Open(_panels[FirstPanelIn(wroot)].Title, wx, wy, ww, wh)
                : null;
            if (win == null)
            {
                FreeSubtree(wroot); // paneller agacsiz kaldi: asagida ilk leaf'e toplanir
                continue;
            }
            win.Camera.BackgroundColor = new Color(28, 30, 38, 255);
            _wins[wi].Used = true;
            _wins[wi].Win = win;
            _wins[wi].Root = wroot;
        }
        // Dosyada gecmeyen paneller ilk leaf'e (kaybolmasinlar).
        int first = FindFirstLeaf(_root);
        for (int p = 0; p < _panelCount; p++)
        {
            if (!_panels[p].Used || FindLeafWithPanelAll(p) >= 0)
                continue;
            ref Node n = ref _nodes[first];
            if (n.TabCount < MaxTabsPerLeaf)
                _tabs[first * MaxTabsPerLeaf + n.TabCount++] = p;
        }
        return true;
    }

    // Kayitli pencere konumunu gorunur bir monitore ceker: eski/bug'li kayitlar
    // veya sokulmus monitorler pencereyi erisilemez birakmasin.
    static void ClampToVisible(ref int x, ref int y, ref int w, ref int h)
    {
        w = Math.Clamp(w, 120, 8192);
        h = Math.Clamp(h, 90, 8192);
        IntPtr monitors = GLFW.GetMonitors(out int count);
        // Baslik cubugunun en az bir parcasi bir monitorun workarea'sinda mi?
        for (int i = 0; i < count; i++)
        {
            IntPtr mon = System.Runtime.InteropServices.Marshal.ReadIntPtr(monitors, i * IntPtr.Size);
            GLFW.GetMonitorWorkarea(mon, out int mx, out int my, out int mw, out int mh);
            if (x + w > mx + 40 && x < mx + mw - 40 && y >= my - 20 && y < my + mh - 40)
                return; // yeterince gorunur
        }
        IntPtr primary = GLFW.GetPrimaryMonitor();
        if (primary == IntPtr.Zero)
            return;
        GLFW.GetMonitorWorkarea(primary, out int px, out int py, out int pw, out int ph);
        w = Math.Min(w, pw);
        h = Math.Min(h, ph);
        x = Math.Clamp(x, px, px + pw - w);
        y = Math.Clamp(y, py, py + ph - h);
    }

    static int ParseNode(string s, ref int pos, out bool ok)
    {
        ok = false;
        while (pos < s.Length && s[pos] == ' ')
            pos++;
        if (pos + 2 >= s.Length || s[pos] != '(')
            return -1;
        char t = s[pos + 1];
        pos += 3; // "(X "
        if (t == 'L')
        {
            int sp = s.IndexOf(' ', pos);
            if (sp < 0 || !int.TryParse(s.AsSpan(pos, sp - pos), out int active))
                return -1;
            pos = sp + 1;
            int end = s.IndexOf(')', pos);
            if (end < 0)
                return -1;
            string tabsStr = s.Substring(pos, end - pos);
            pos = end + 1;
            Span<int> ids = stackalloc int[MaxTabsPerLeaf];
            int count = 0;
            foreach (var title in tabsStr.Split('|'))
            {
                int pid = FindPanelByTitle(title);
                if (pid >= 0 && count < MaxTabsPerLeaf)
                    ids[count++] = pid;
            }
            if (count == 0)
                return -1;
            int ni = Leaf(ids.Slice(0, count));
            _nodes[ni].Active = Math.Clamp(active, 0, count - 1);
            ok = true;
            return ni;
        }
        else
        {
            int sp = s.IndexOf(' ', pos);
            if (sp < 0 || !float.TryParse(s.AsSpan(pos, sp - pos),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float ratio))
                return -1;
            pos = sp;
            int a = ParseNode(s, ref pos, out bool okA);
            if (!okA)
                return -1;
            int b = ParseNode(s, ref pos, out bool okB);
            if (!okB)
                return -1;
            while (pos < s.Length && s[pos] == ' ')
                pos++;
            if (pos >= s.Length || s[pos] != ')')
                return -1;
            pos++;
            ok = true;
            return Split(t == 'H', ratio, a, b);
        }
    }

    static int FindPanelByTitle(string title)
    {
        for (int i = 0; i < _panelCount; i++)
            if (_panels[i].Used && _panels[i].Title == title)
                return i;
        return -1;
    }

    static bool ParseInt(string s, ref int pos, out int value)
    {
        while (pos < s.Length && s[pos] == ' ')
            pos++;
        int start = pos;
        while (pos < s.Length && s[pos] != ' ')
            pos++;
        return int.TryParse(s.AsSpan(start, pos - start), out value);
    }

    static int FirstPanelIn(int ni)
    {
        if (ni < 0 || !_nodes[ni].Used)
            return 0;
        if (_nodes[ni].Type == TypeLeaf)
            return _nodes[ni].TabCount > 0 ? _tabs[ni * MaxTabsPerLeaf] : 0;
        int r = FirstPanelIn(_nodes[ni].A);
        return r != 0 ? r : FirstPanelIn(_nodes[ni].B);
    }

    static void FreeSubtree(int ni)
    {
        if (ni < 0 || !_nodes[ni].Used)
            return;
        if (_nodes[ni].Type != TypeLeaf)
        {
            FreeSubtree(_nodes[ni].A);
            FreeSubtree(_nodes[ni].B);
        }
        _nodes[ni].Used = false;
    }

    // Paneli one getirir: dock'luysa tab'i aktif olur, degilse ilk leaf'e dock'lanir.
    // Mutasyon pending'e yazilir (Layout basinda uygulanir — kontrol sirasi korunur).
    public static void FocusPanel(int panelId)
    {
        if (panelId < 0 || _root < 0)
            return;
        int leaf = FindLeafWithPanelAll(panelId);
        if (leaf < 0)
        {
            _pendingMovePanel = panelId;
            _pendingMoveLeaf = FindFirstLeaf(_root);
            return;
        }
        int baseIdx = leaf * MaxTabsPerLeaf;
        for (int i = 0; i < _nodes[leaf].TabCount; i++)
        {
            if (_tabs[baseIdx + i] == panelId)
            {
                _pendingActiveLeaf = leaf;
                _pendingActiveIndex = i;
                break;
            }
        }
    }

    // Teshis: agac yapisini ve cozulmus rect'leri stdout'a doker.
    internal static void DebugDump()
    {
        Console.WriteLine($"[dock] root={_root} nodeCount={_nodeCount}");
        for (int i = 0; i < _nodeCount; i++)
        {
            ref Node n = ref _nodes[i];
            if (!n.Used)
                continue;
            Console.WriteLine($"  n{i} type={n.Type} A={n.A} B={n.B} parent={n.Parent} ratio={n.Ratio} tabs={n.TabCount} rect=({n.Rect.x},{n.Rect.y},{n.Rect.width},{n.Rect.height})");
        }
    }

    static int AllocNode()
    {
        for (int i = 0; i < MaxNodes; i++)
        {
            if (!_nodes[i].Used)
            {
                _nodes[i] = default;
                _nodes[i].Used = true;
                _nodes[i].Parent = -1;
                if (i >= _nodeCount)
                    _nodeCount = i + 1;
                return i;
            }
        }
        throw new InvalidOperationException("GuiDock: node havuzu doldu");
    }

    // --- Pending mutasyonlar (Layout basinda uygulanir) ---

    static int _pendingDetachPanel = -1;
    static Vec2 _pendingDetachScreenPos;
    static int _pendingActiveLeaf = -1, _pendingActiveIndex;
    static int _pendingMovePanel = -1, _pendingMoveLeaf, _pendingMoveIndex = -1;
    static int _pendingSplitPanel = -1, _pendingSplitLeaf, _pendingSplitDir;

    static void ApplyPending()
    {
        if (_pendingActiveLeaf >= 0)
        {
            _nodes[_pendingActiveLeaf].Active = _pendingActiveIndex;
            _pendingActiveLeaf = -1;
        }
        if (_pendingMovePanel >= 0)
        {
            MoveNow(_pendingMovePanel, _pendingMoveLeaf, _pendingMoveIndex);
            _pendingMovePanel = -1;
            _pendingMoveIndex = -1;
        }
        if (_pendingSplitPanel >= 0)
        {
            SplitNow(_pendingSplitPanel, _pendingSplitLeaf, _pendingSplitDir);
            _pendingSplitPanel = -1;
        }
        if (_pendingDetachPanel >= 0)
        {
            DetachNow(_pendingDetachPanel, _pendingDetachScreenPos);
            _pendingDetachPanel = -1;
        }
        // Pencere temizligi: X ile kapananlarin panelleri dock'suz kalir (redock YOK —
        // Window menusunden tekrar acilir, FocusPanel ilk leaf'e dock'lar);
        // bosalan pencereler yok edilir.
        for (int i = 0; i < _winCount; i++)
        {
            ref DockWin w = ref _wins[i];
            if (!w.Used)
                continue;
            ref Node rn = ref _nodes[w.Root];
            if (w.Win == null)
                rn.TabCount = 0;
            if (rn.TabCount == 0)
            {
                w.Win?.Destroy();
                w.Win = null;
                rn.Used = false;
                w.Used = false;
            }
        }
    }

    // Paneli bulundugu leaf'ten cikarir (gerekirse bos leaf'i collapse eder).
    static int RemoveFromTree(int panelId)
    {
        int leaf = FindLeafWithPanelAll(panelId);
        if (leaf < 0)
            return -1;
        ref Node n = ref _nodes[leaf];
        int baseIdx = leaf * MaxTabsPerLeaf;
        for (int i = 0; i < n.TabCount; i++)
        {
            if (_tabs[baseIdx + i] == panelId)
            {
                for (int j = i; j < n.TabCount - 1; j++)
                    _tabs[baseIdx + j] = _tabs[baseIdx + j + 1];
                n.TabCount--;
                if (n.Active >= n.TabCount)
                    n.Active = n.TabCount > 0 ? n.TabCount - 1 : 0;
                break;
            }
        }
        if (n.TabCount == 0)
            CollapseLeaf(leaf);
        return leaf;
    }

    // Tab'i baska leaf'e (veya ayni leaf'te baska konuma) tasi. insertIndex<0 = sona.
    static void MoveNow(int panelId, int targetLeaf, int insertIndex = -1)
    {
        if (targetLeaf < 0 || !_nodes[targetLeaf].Used || _nodes[targetLeaf].Type != TypeLeaf)
            return;
        int baseIdx = targetLeaf * MaxTabsPerLeaf;
        // Ayni leaf icinde tasima YERINDE yapilir: RemoveFromTree leaf'i bosaltip
        // collapse edebilir (tek tab'li leaf kendi icine tasinirken zone olurdu).
        if (FindLeafWithPanelAll(panelId) == targetLeaf)
        {
            ref Node s = ref _nodes[targetLeaf];
            int src = -1;
            for (int i = 0; i < s.TabCount; i++)
                if (_tabs[baseIdx + i] == panelId) { src = i; break; }
            int dst = insertIndex < 0 ? s.TabCount : insertIndex;
            if (src < dst)
                dst--; // kaldirma sonrasi kayma
            if (dst > s.TabCount - 1)
                dst = s.TabCount - 1;
            if (src != dst)
            {
                if (src < dst)
                    for (int j = src; j < dst; j++)
                        _tabs[baseIdx + j] = _tabs[baseIdx + j + 1];
                else
                    for (int j = src; j > dst; j--)
                        _tabs[baseIdx + j] = _tabs[baseIdx + j - 1];
                _tabs[baseIdx + dst] = panelId;
            }
            s.Active = dst;
            return;
        }
        RemoveFromTree(panelId);
        ref Node t = ref _nodes[targetLeaf];
        if (!t.Used || t.Type != TypeLeaf || t.TabCount >= MaxTabsPerLeaf)
            return;
        int idx = insertIndex < 0 || insertIndex > t.TabCount ? t.TabCount : insertIndex;
        for (int j = t.TabCount; j > idx; j--)
            _tabs[baseIdx + j] = _tabs[baseIdx + j - 1];
        _tabs[baseIdx + idx] = panelId;
        t.TabCount++;
        t.Active = idx;
    }

    // Mouse x'ine gore tab bar'da ekleme bosluu indeksi (0..TabCount).
    static int TabInsertIndexAt(int leaf, Vec2 p)
    {
        ref Node n = ref _nodes[leaf];
        int idx = (int)((p.x - n.Rect.x) / (TabW + 2f) + 0.5f);
        if (idx < 0) idx = 0;
        if (idx > n.TabCount) idx = n.TabCount;
        return idx;
    }

    // Icerik kenar bandina birakma = split yonu (0=sol 1=sag 2=ust 3=alt), -1 = tab olarak.
    static int SplitDirAt(int leaf, Vec2 p)
    {
        Rect r = _nodes[leaf].Rect;
        if (p.y < r.y + TabH)
            return -1; // tab bar
        var c = new Rect(r.x, r.y + TabH, r.width, r.height - TabH);
        float bx = c.width * 0.25f, by = c.height * 0.25f;
        if (p.x < c.x + bx) return 0;
        if (p.x > c.xMax - bx) return 1;
        if (p.y < c.y + by) return 2;
        if (p.y > c.yMax - by) return 3;
        return -1; // orta: tab olarak ekle
    }

    // Hedef leaf'i boler, panel yeni yarim zone olur (Unity drop-to-split).
    static void SplitNow(int panelId, int targetLeaf, int dir)
    {
        if (targetLeaf < 0 || !_nodes[targetLeaf].Used || _nodes[targetLeaf].Type != TypeLeaf)
            return;
        // Tek tab'li leaf kendi icine split = no-op (kaynak yok olur, anlamsiz).
        if (FindLeafWithPanelAll(panelId) == targetLeaf && _nodes[targetLeaf].TabCount == 1)
            return;
        RemoveFromTree(panelId);
        if (!_nodes[targetLeaf].Used || _nodes[targetLeaf].Type != TypeLeaf)
            return; // hedef removal sirasindaki collapse'ta yok oldu
        // Parent removal SONRASI okunur (collapse kardes baglantilarini degistirebilir).
        int parent = _nodes[targetLeaf].Parent;
        int newLeaf = Leaf(stackalloc int[] { panelId });
        bool horizontal = dir == 0 || dir == 1;
        bool newFirst = dir == 0 || dir == 2;
        int split = Split(horizontal, 0.5f,
            newFirst ? newLeaf : targetLeaf,
            newFirst ? targetLeaf : newLeaf);
        _nodes[split].Parent = parent;
        if (parent < 0)
        {
            if (targetLeaf == _root)
                _root = split;
            else
                for (int i = 0; i < _winCount; i++)
                    if (_wins[i].Used && _wins[i].Root == targetLeaf)
                        _wins[i].Root = split;
        }
        else if (_nodes[parent].A == targetLeaf)
            _nodes[parent].A = split;
        else
            _nodes[parent].B = split;
    }

    static void DetachNow(int panelId, Vec2 screenPos)
    {
        if (FindLeafWithPanelAll(panelId) < 0)
            return;
        int wi = AllocWin();
        if (wi < 0)
            return; // pencere havuzu dolu: panel yerinde kalir
        var win = NativeWindow.Open(_panels[panelId].Title, (int)screenPos.x, (int)screenPos.y, 380, 320);
        if (win == null)
            return; // pencere acilamadi: panel yerinde kalir
        win.Camera.BackgroundColor = new Color(28, 30, 38, 255);
        RemoveFromTree(panelId);
        _wins[wi].Used = true;
        _wins[wi].Win = win;
        _wins[wi].Root = Leaf(stackalloc int[] { panelId });
    }

    // Bos leaf'i agactan dusur: parent split'in yerine kardesi gecer.
    static void CollapseLeaf(int leaf)
    {
        int parent = _nodes[leaf].Parent;
        if (parent < 0)
            return; // root bos leaf olarak kalir
        ref Node pn = ref _nodes[parent];
        int sibling = pn.A == leaf ? pn.B : pn.A;
        int grand = pn.Parent;
        _nodes[sibling].Parent = grand;
        if (grand < 0)
            _root = sibling;
        else if (_nodes[grand].A == parent)
            _nodes[grand].A = sibling;
        else
            _nodes[grand].B = sibling;
        _nodes[leaf].Used = false;
        _nodes[parent].Used = false;
    }

    static int FindLeafWithPanel(int ni, int panelId)
    {
        if (ni < 0 || !_nodes[ni].Used)
            return -1;
        ref Node n = ref _nodes[ni];
        if (n.Type == TypeLeaf)
        {
            for (int i = 0; i < n.TabCount; i++)
                if (_tabs[ni * MaxTabsPerLeaf + i] == panelId)
                    return ni;
            return -1;
        }
        int r = FindLeafWithPanel(n.A, panelId);
        return r >= 0 ? r : FindLeafWithPanel(n.B, panelId);
    }

    static int FindFirstLeaf(int ni)
    {
        if (ni < 0 || !_nodes[ni].Used)
            return -1;
        if (_nodes[ni].Type == TypeLeaf)
            return ni;
        int r = FindFirstLeaf(_nodes[ni].A);
        return r >= 0 ? r : FindFirstLeaf(_nodes[ni].B);
    }

    // Noktanin ustundeki leaf (tab bar drop hedefi icin; rect'ler bu frame cozuldu).
    static int FindLeafAt(int ni, Vec2 p)
    {
        if (ni < 0 || !_nodes[ni].Used || !_nodes[ni].Rect.Contains(p))
            return -1;
        if (_nodes[ni].Type == TypeLeaf)
            return ni;
        int r = FindLeafAt(_nodes[ni].A, p);
        return r >= 0 ? r : FindLeafAt(_nodes[ni].B, p);
    }

    // --- Frame: dock alanini coz ve ciz ---

    const float TabW = 84f, TabH = 22f, SplitterSize = 8f;

    static readonly int _splitHash = "GuiDock.Split".GetHashCode();
    static readonly int _tabHash = "GuiDock.Tab".GetHashCode();

    static Rect _rootRect;
    // Tab surukleme durumu (hot control uzerinden yasar).
    static int _dragPanel = -1;
    static bool _dragging;
    static int _dragWin = -1; // suruklemenin basladigi pencere (-1 = ana)

    // Pencere-lokal mantiksal koordinat <-> ekran (screen-coord) koordinati.
    // Carpan platforma gore degisir: Windows'ta content scale (koordinatlar
    // fiziksel px), macOS'ta 1 (koordinatlar zaten point) — ScreenScale hesaplar.
    static Vec2 ToScreen(int wi, Vec2 logical)
    {
        IntPtr h = HandleOf(wi);
        GLFW.GetWindowPos(h, out int wx, out int wy);
        float k = NativeWindow.ScreenScale(h);
        return new Vec2(wx + logical.x * k, wy + logical.y * k);
    }

    static Vec2 FromScreen(int wi, Vec2 screen)
    {
        IntPtr h = HandleOf(wi);
        GLFW.GetWindowPos(h, out int wx, out int wy);
        float k = NativeWindow.ScreenScale(h);
        return new Vec2((screen.x - wx) / k, (screen.y - wy) / k);
    }

    // Surukleme sirasinda imlecin _ctxWin lokalindeki CANLI konumu: capture origin
    // penceresinde oldugundan diger pencerelerin event mouse'u bayattir; ekran
    // uzayindan hesaplanir (GetCursorPos origin'e pencere-lokal fiziksel px verir).
    static Vec2 DragMouseLocal()
    {
        IntPtr src = HandleOf(_dragWin);
        GLFW.GetWindowPos(src, out int wx, out int wy);
        GLFW.GetCursorPos(src, out double cx, out double cy);
        return FromScreen(_ctxWin, new Vec2(wx + (float)cx, wy + (float)cy));
    }

    // Ekran noktasindaki drop hedefi: once diger dock pencereleri, sonra ana agac.
    // (Rect'ler son GUI turunda cozulmus haliyle kullanilir.)
    static int FindLeafAtScreen(Vec2 screen, int excludeWin, out Vec2 local)
    {
        for (int i = 0; i < _winCount; i++)
        {
            if (!_wins[i].Used || _wins[i].Win == null || i == excludeWin)
                continue;
            Vec2 l = FromScreen(i, screen);
            int leaf = FindLeafAt(_wins[i].Root, l);
            if (leaf >= 0)
            {
                local = l;
                return leaf;
            }
        }
        if (excludeWin != -1)
        {
            Vec2 l = FromScreen(-1, screen);
            int leaf = FindLeafAt(_root, l);
            if (leaf >= 0)
            {
                local = l;
                return leaf;
            }
        }
        local = default;
        return -1;
    }

    public static void DockSpace(in Rect rect)
    {
        if (_root < 0)
            return;
        if (Event.Current.Type == EventType.Layout)
            ApplyPending();
        _rootRect = rect;
        _ctxWin = -1;
        ResolveAndDraw(_root, rect);
    }

    static void ResolveAndDraw(int ni, in Rect r)
    {
        ref Node n = ref _nodes[ni];
        n.Rect = r;
        if (n.Type == TypeLeaf)
        {
            DrawLeaf(ni, r);
            return;
        }

        bool horizontal = n.Type == TypeSplitH;
        float total = horizontal ? r.width : r.height;
        float aSize = total * n.Ratio - SplitterSize * 0.5f;

        Rect ra, rb, splitter;
        if (horizontal)
        {
            ra = new Rect(r.x, r.y, aSize, r.height);
            splitter = new Rect(r.x + aSize, r.y, SplitterSize, r.height);
            rb = new Rect(r.x + aSize + SplitterSize, r.y, r.width - aSize - SplitterSize, r.height);
        }
        else
        {
            ra = new Rect(r.x, r.y, r.width, aSize);
            splitter = new Rect(r.x, r.y + aSize, r.width, SplitterSize);
            rb = new Rect(r.x, r.y + aSize + SplitterSize, r.width, r.height - aSize - SplitterSize);
        }

        // Splitter: ratio degistirmek kontrol sayisini degistirmez, aninda uygulanabilir.
        // Isabet alani gorsel cizgiden 3px genis (ince hedefe tiklamak zor).
        var hit = horizontal
            ? new Rect(splitter.x - 3, splitter.y, splitter.width + 6, splitter.height)
            : new Rect(splitter.x, splitter.y - 3, splitter.width, splitter.height + 6);
        int id = GuiUtility.GetControlID(_splitHash, FocusType.Passive);
        Event ev = Event.Current;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (hit.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    float t = horizontal
                        ? (ev.MousePosition.x - r.x) / r.width
                        : (ev.MousePosition.y - r.y) / r.height;
                    n.Ratio = t < 0.1f ? 0.1f : (t > 0.9f ? 0.9f : t);
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    bool hov = hit.Contains(ev.MousePosition) || GuiUtility.HotControl == id;
                    if (hov)
                        GuiCursorManager.Request(horizontal ? GuiCursor.ResizeH : GuiCursor.ResizeV);
                    GuiRenderer.DrawRect(splitter, hov ? new Color(110, 150, 235, 255) : new Color(64, 68, 84, 255), 1);
                    // Ortada tutamac cizgisi: splitter'i gorunur kilar.
                    if (horizontal)
                        GuiRenderer.DrawRect(new Rect(splitter.Center.x - 1, splitter.Center.y - 12, 2, 24),
                            new Color(140, 145, 165, 255), 2);
                    else
                        GuiRenderer.DrawRect(new Rect(splitter.Center.x - 12, splitter.Center.y - 1, 24, 2),
                            new Color(140, 145, 165, 255), 2);
                    break;
                }
        }

        ResolveAndDraw(n.A, ra);
        ResolveAndDraw(n.B, rb);
    }

    static void DrawLeaf(int ni, in Rect r)
    {
        ref Node n = ref _nodes[ni];
        Event ev = Event.Current;

        var barRect = new Rect(r.x, r.y, r.width, TabH);
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(barRect, new Color(24, 26, 32, 255), 0);

        int baseIdx = ni * MaxTabsPerLeaf;
        for (int i = 0; i < n.TabCount; i++)
        {
            int panelId = _tabs[baseIdx + i];
            var tabRect = new Rect(r.x + i * (TabW + 2), r.y, TabW, TabH);
            int id = GuiUtility.GetControlID(_tabHash, FocusType.Passive);

            switch (ev.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (tabRect.Contains(ev.MousePosition))
                    {
                        GuiUtility.HotControl = id;
                        _dragPanel = panelId;
                        _dragging = false;
                        _dragWin = _ctxWin;
                        ev.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GuiUtility.HotControl == id)
                    {
                        _dragging = true;
                        ev.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GuiUtility.HotControl == id)
                    {
                        GuiUtility.HotControl = 0;
                        ev.Use();
                        if (_dragging)
                        {
                            // Once bu pencerenin agaci, sonra ekran uzayindan
                            // diger pencereler (ana dahil).
                            int target = FindLeafAt(RootOf(_ctxWin), ev.MousePosition);
                            Vec2 tlocal = ev.MousePosition;
                            if (target < 0)
                            {
                                Vec2 screen = ToScreen(_ctxWin, ev.GlobalMousePosition);
                                target = FindLeafAtScreen(screen, _ctxWin, out tlocal);
                                if (target < 0)
                                {
                                    // Hicbir dock alanina denk gelmedi: yeni pencereye detach.
                                    _pendingDetachPanel = panelId;
                                    _pendingDetachScreenPos = screen;
                                }
                            }
                            if (target >= 0)
                            {
                                int dir = SplitDirAt(target, tlocal);
                                if (dir >= 0)
                                {
                                    // Kenar bandina birakildi: yeni zone (split).
                                    _pendingSplitPanel = panelId;
                                    _pendingSplitLeaf = target;
                                    _pendingSplitDir = dir;
                                }
                                else
                                {
                                    _pendingMovePanel = panelId;
                                    _pendingMoveLeaf = target;
                                    _pendingMoveIndex = TabInsertIndexAt(target, tlocal);
                                }
                            }
                        }
                        else
                        {
                            // Tiklama: aktif tab (sonraki frame'de).
                            _pendingActiveLeaf = ni;
                            _pendingActiveIndex = i;
                        }
                        _dragging = false;
                        _dragPanel = -1;
                        _dragWin = -1;
                    }
                    break;
                case EventType.Repaint:
                    {
                        Color c = _panels[panelId].TabColor;
                        bool active = i == n.Active;
                        bool hov = tabRect.Contains(ev.MousePosition);
                        var col = active ? c
                            : hov ? new Color((byte)(c.r / 2 + 40), (byte)(c.g / 2 + 40), (byte)(c.b / 2 + 40), 255)
                                  : new Color((byte)(c.r / 2), (byte)(c.g / 2), (byte)(c.b / 2), 255);
                        GuiRenderer.DrawRect(tabRect, col, 1);
                        GuiRenderer.DrawTextIn(tabRect, _panels[panelId].Title, Gui.FontSize - 1f,
                            active ? new Color(255, 255, 255, 255) : new Color(200, 202, 212, 255),
                            centerX: true, layerOffset: 2);
                        if (active) // aktif tab altinda vurgu cizgisi
                            GuiRenderer.DrawRect(new Rect(tabRect.x, tabRect.yMax - 2, tabRect.width, 2),
                                new Color(255, 255, 255, 180), 2);
                        break;
                    }
            }
        }

        var content = new Rect(r.x, r.y + TabH, r.width, r.height - TabH);
        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(content, new Color(33, 35, 43, 245), 0);
            // Drop bolgesi vurgusu: tab suruklenirken tum bar'lar soluk,
            // uzerinde durulan hedef parlak mavi. Imlec konumu ekran uzayindan
            // canli hesaplanir ki BASKA pencereden suruklerken de gosterge ciksin.
            if (_dragging && _dragPanel >= 0)
            {
                Vec2 dm = DragMouseLocal();
                bool over = barRect.Contains(dm);
                GuiRenderer.DrawRect(barRect,
                    over ? new Color(90, 150, 240, 140) : new Color(90, 150, 240, 45), 3);
                if (over)
                {
                    // Ekleme konumu cizgisi: hangi boslua birakilacagini gosterir.
                    int idx = TabInsertIndexAt(ni, dm);
                    float lx = r.x + idx * (TabW + 2) - 1;
                    GuiRenderer.DrawRect(new Rect(lx, r.y, 2, TabH), new Color(255, 220, 80, 255), 4);
                }
                else if (content.Contains(dm))
                {
                    // Split onizlemesi: yeni zone'un kaplayacagi yarim alan.
                    int dir = SplitDirAt(ni, dm);
                    Rect ov = dir switch
                    {
                        0 => new Rect(content.x, content.y, content.width * 0.5f, content.height),
                        1 => new Rect(content.Center.x, content.y, content.width * 0.5f, content.height),
                        2 => new Rect(content.x, content.y, content.width, content.height * 0.5f),
                        3 => new Rect(content.x, content.y + content.height * 0.5f, content.width, content.height * 0.5f),
                        _ => content, // orta: tab olarak eklenecek
                    };
                    GuiRenderer.DrawRect(ov, new Color(90, 150, 240, dir >= 0 ? (byte)90 : (byte)45), 4);
                }
            }
        }

        if (n.TabCount > 0)
        {
            int activePanel = _tabs[baseIdx + n.Active];
            GuiLayoutUtility.BeginArea(new Rect(content.x + 4, content.y + 4, content.width - 8, content.height - 8));
            _panels[activePanel].Draw(activePanel);
            GuiLayoutUtility.EndArea();
        }
    }

    // --- Detached dock pencereleri (App frame dongusu cagirir) ---

    static readonly GuiHost.GuiFunc _winFunc = DrawCurrentWin;
    static int _currentWin;
    static Rect _currentWinRect;

    // Ana GUI turundan sonra: kapanan pencereleri isaretle, kalanlarin GUI turunu kos.
    public static void UpdateWindows()
    {
        for (int i = 0; i < _winCount; i++)
        {
            ref DockWin w = ref _wins[i];
            if (!w.Used || w.Win == null)
                continue;
            if (w.Win.ShouldClose)
            {
                w.Win.Destroy();
                w.Win = null; // ApplyPending tablari ana agaca dondurup slotu bosaltir
                continue;
            }
            if (!w.Win.UpdateSize())
                continue; // minimize
            GLFW.GetWindowContentScale(w.Win.Handle, out float psx, out _);
            if (psx <= 0) psx = 1f;
            float plw = w.Win.Width / psx, plh = w.Win.Height / psx;
            w.Win.Camera.SetPixelOrtho((int)plw, (int)plh); // mantiksal ortho, fiziksel viewport
            GuiRenderer.Queue = w.Win.Camera.Queue;
            _currentWin = i;
            _currentWinRect = new Rect(0, 0, plw, plh);
            // Fare bolen'i = ekran-birimi/mantiksal orani (macOS'ta 1, Windows'ta cs).
            w.Win.Gui.Frame(w.Win.Handle, _currentWinRect, _winFunc, NativeWindow.ScreenScale(w.Win.Handle));
        }
        UpdateDragGhost();
    }

    // --- Surukleme hayaleti: imleci takip eden dekorasyonsuz native pencere
    // (pencereler ARASI suruklerken de gorunur; canvas-ici ghost bunu yapamazdi) ---

    static NativeWindow _ghostWin;
    static Rect _ghostRect;
    static readonly GuiHost.GuiFunc _ghostFunc = DrawGhostWin;

    static void UpdateDragGhost()
    {
        if (!_dragging || _dragPanel < 0)
        {
            if (_ghostWin != null)
            {
                _ghostWin.Destroy();
                _ghostWin = null;
            }
            return;
        }
        IntPtr src = HandleOf(_dragWin);
        float k = NativeWindow.ScreenScale(src); // ekran-birimi/mantiksal orani
        int gw = (int)(TabW * k), gh = (int)(TabH * k);
        if (_ghostWin == null)
        {
            // Yalniz bu cagrinin hint'leri elle geri alinir: DefaultWindowHints
            // GL context hint'lerini de sifirlar (sonraki detach pencereleri bozulur).
            GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.FALSE);
            GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.TRUE);
            GLFW.WindowHint(GLFWConst.FOCUSED, GLFWConst.FALSE);
            GLFW.WindowHint(GLFWConst.FOCUS_ON_SHOW, GLFWConst.FALSE);
            GLFW.WindowHint(GLFWConst.MOUSE_PASSTHROUGH, GLFWConst.TRUE);
            GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.FALSE);
            _ghostWin = NativeWindow.Open("drag", -10000, -10000, gw, gh);
            GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.TRUE);
            GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.FALSE);
            GLFW.WindowHint(GLFWConst.FOCUSED, GLFWConst.TRUE);
            GLFW.WindowHint(GLFWConst.FOCUS_ON_SHOW, GLFWConst.TRUE);
            GLFW.WindowHint(GLFWConst.MOUSE_PASSTHROUGH, GLFWConst.FALSE);
            GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.TRUE);
            if (_ghostWin == null)
                return;
            Color bg = _panels[_dragPanel].TabColor;
            _ghostWin.Camera.BackgroundColor = bg;
        }
        // Imlec ekran konumu: pencere pos + pencere-lokal imlec (ikisi de fiziksel px).
        GLFW.GetWindowPos(src, out int wx, out int wy);
        GLFW.GetCursorPos(src, out double cxd, out double cyd);
        GLFW.SetWindowPos(_ghostWin.Handle, wx + (int)cxd - gw / 2, wy + (int)cyd - gh / 2);
        if (!_ghostWin.UpdateSize())
            return;
        GLFW.GetWindowContentScale(_ghostWin.Handle, out float ps, out _);
        if (ps <= 0) ps = 1f;
        float lw = _ghostWin.Width / ps, lh = _ghostWin.Height / ps;
        _ghostWin.Camera.SetPixelOrtho((int)lw, (int)lh);
        GuiRenderer.Queue = _ghostWin.Camera.Queue;
        _ghostRect = new Rect(0, 0, lw, lh);
        _ghostWin.Gui.Frame(_ghostWin.Handle, _ghostRect, _ghostFunc, ps);
    }

    static void DrawGhostWin()
    {
        if (Event.Current.Type != EventType.Repaint || _dragPanel < 0)
            return;
        Color c = _panels[_dragPanel].TabColor;
        GuiRenderer.DrawRect(_ghostRect, c, 0);
        GuiRenderer.DrawTextIn(_ghostRect, _panels[_dragPanel].Title, Gui.FontSize - 1f,
            new Color(255, 255, 255, 255), centerX: true, layerOffset: 1);
    }

    // Pencere = mini dock host: kendi leaf'i (tab bar + icerik) ayni DrawLeaf yoluyla.
    static void DrawCurrentWin()
    {
        _ctxWin = _currentWin;
        ResolveAndDraw(_wins[_currentWin].Root, _currentWinRect);
        _ctxWin = -1;
    }

    public static void EncodeWindows(CommandBuffer cb)
    {
        for (int i = 0; i < _winCount; i++)
        {
            ref DockWin w = ref _wins[i];
            if (w.Used && w.Win != null)
                w.Win.Camera.Encode(cb, w.Win.Width, w.Win.Height);
        }
        if (_ghostWin != null)
            _ghostWin.Camera.Encode(cb, _ghostWin.Width, _ghostWin.Height);
    }

    public static void PresentWindows()
    {
        for (int i = 0; i < _winCount; i++)
        {
            ref DockWin w = ref _wins[i];
            if (w.Used && w.Win != null)
                w.Win.Present();
        }
        _ghostWin?.Present();
    }
}
#endif
