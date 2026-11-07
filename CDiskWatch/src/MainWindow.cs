using System.Runtime.InteropServices;

namespace CDiskWatch;

internal enum ColStyle
{
    /// <summary>主键列（名称/路径），可用行级强调色。</summary>
    Primary,
    /// <summary>数值列：右对齐 + 半粗体。</summary>
    Value,
    /// <summary>说明列：小字号次要色。</summary>
    Muted,
    /// <summary>等级列：渲染成彩色胶囊。</summary>
    Tag,
    /// <summary>进度条列（可选在右侧附带文字）。</summary>
    Bar,
    /// <summary>序号列。</summary>
    Index,
}

internal readonly struct Col
{
    public readonly string Title;
    public readonly float Weight;
    public readonly uint Align;
    public readonly ColStyle Style;
    public Col(string t, float w, uint a, ColStyle s) { Title = t; Weight = w; Align = a; Style = s; }
}

internal sealed class Row
{
    public string[] Cells = Array.Empty<string>();
    public string Sub = "";
    public double Bar = -1;
    public uint BarColor = Theme.Accent;
    public uint TextColor = Theme.TextMain;
    public bool IsSection;
    /// <summary>整行文本块（用于「结论」这类长文本）。</summary>
    public bool IsBlock;
    public string? Path;
}

/// <summary>主面板：纯 Win32 自绘（无 WinForms / WPF），浅色主题。</summary>
public sealed class MainWindow
{
    public const string ClassName = "CDiskWatchWnd";
    static readonly Win32.WndProcDelegate s_wndProc = WndProcStatic;
    static MainWindow? s_this;
    public static MainWindow? Instance => s_this;

    public IntPtr Hwnd;

    static readonly string[] PageNames =
    {
        Loc.T("tab.overview"), Loc.T("tab.dirs"), Loc.T("tab.files"), Loc.T("tab.delta"),
        Loc.T("tab.cats"), Loc.T("tab.advice"), Loc.T("tab.history"), Loc.T("tab.folderAnalysis"),
    };

    static readonly Col[][] Cols =
    {
        new[] { new Col(Loc.T("col.item"), 0.26f, Theme.AlignLeft, ColStyle.Primary), new Col(Loc.T("col.value"), 0.18f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.note"), 0.56f, Theme.AlignLeft, ColStyle.Muted) },
        new[] { new Col(Loc.T("col.dir"), 0.52f, Theme.AlignLeft, ColStyle.Primary), new Col(Loc.T("col.size"), 0.14f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.share"), 0.10f, Theme.AlignRight, ColStyle.Muted), new Col("", 0.24f, Theme.AlignLeft, ColStyle.Bar) },
        new[] { new Col("#", 0.05f, Theme.AlignRight, ColStyle.Index), new Col(Loc.T("col.size"), 0.11f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.mtime"), 0.16f, Theme.AlignLeft, ColStyle.Muted), new Col(Loc.T("col.path"), 0.68f, Theme.AlignLeft, ColStyle.Primary) },
        new[] { new Col(Loc.T("col.folder"), 0.44f, Theme.AlignLeft, ColStyle.Primary), new Col(Loc.T("col.delta"), 0.13f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.nowSize"), 0.13f, Theme.AlignRight, ColStyle.Muted), new Col("", 0.30f, Theme.AlignLeft, ColStyle.Bar) },
        new[] { new Col(Loc.T("col.cat"), 0.16f, Theme.AlignLeft, ColStyle.Primary), new Col(Loc.T("col.size"), 0.16f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.count"), 0.12f, Theme.AlignRight, ColStyle.Muted), new Col(Loc.T("col.share"), 0.10f, Theme.AlignRight, ColStyle.Muted), new Col("", 0.46f, Theme.AlignLeft, ColStyle.Bar) },
        new[] { new Col(Loc.T("col.path"), 0.52f, Theme.AlignLeft, ColStyle.Primary), new Col(Loc.T("col.size"), 0.12f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.risk"), 0.10f, Theme.AlignLeft, ColStyle.Tag), new Col(Loc.T("col.rule"), 0.26f, Theme.AlignLeft, ColStyle.Muted) },
        new[] { new Col(Loc.T("col.time"), 0.18f, Theme.AlignLeft, ColStyle.Muted), new Col(Loc.T("col.free"), 0.15f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.used"), 0.15f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.change"), 0.15f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("col.usage"), 0.37f, Theme.AlignLeft, ColStyle.Bar) },
        new[] { new Col(Loc.T("folder.col.kind"), 0.15f, Theme.AlignLeft, ColStyle.Muted), new Col(Loc.T("folder.col.path"), 0.55f, Theme.AlignLeft, ColStyle.Primary), new Col(Loc.T("folder.col.size"), 0.15f, Theme.AlignRight, ColStyle.Value), new Col(Loc.T("folder.col.risk"), 0.15f, Theme.AlignLeft, ColStyle.Tag) },
    };

    static readonly uint[] LevelColors =
    {
        Theme.Accent, Win32.Rgb(0x4F, 0x86, 0xE8), Win32.Rgb(0x6E, 0x9B, 0xEE), Win32.Rgb(0x93, 0xB4, 0xF2), Win32.Rgb(0xB6, 0xCC, 0xF7),
    };

    static readonly (string text, int action)[] Actions =
    {
        (Loc.T("action.dataDir"), 2), (Loc.T("action.copyReport"), 1), (Loc.T("action.rescan"), 0),
        (Loc.T("action.folderScan"), 3),
    };

    // ===== 状态 =====
    int _page;
    readonly List<Row> _rows = new();
    int _scroll, _scrollMax;
    /// <summary>-1 = 无悬停；-2 = 悬停在滚动条滑块上；>=0 = 行索引。</summary>
    int _hoverRow = -1, _hoverTab = -1, _hoverAction = -1;
    int _selRow = -1;
    bool _thumbDrag;
    int _dragY0, _dragScroll0;
    int _renderedVersion = -1;
    long _lastFree = -1;
    bool _lastScanning;
    /// <summary>扫描结束后延迟几次心跳再回收工作集（等到 UI 重绘完成）。</summary>
    int _trimCountdown;

    // ===== 度量 =====
    float _scale = 1f;
    int _dpi = 96;
    int _w, _h, _defW, _defH;
    bool _layoutValid;
    int _pad, _appBarH, _heroH, _tabH, _statusH, _rowH, _secH, _subRowH, _hdrH, _gutter;
    int _colX0, _colW, _listTop, _listBottom, _chartH;

    Win32.RECT _heroRc, _tabsRc, _cardRc, _hdrRc, _statusRc, _chartRc;
    Win32.RECT _btnRescan, _btnCopy, _btnData;
    Win32.RECT _scrollTrack, _scrollThumb;
    readonly Win32.RECT[] _tabRc = new Win32.RECT[PageNames.Length];
    Win32.RECT _btnFolder;
    volatile bool _folderAnalyzing;
    List<FolderUsage>? _folderResults;
    string _folderError = "";

    IntPtr _fTitle, _fBig, _fSemi, _fMain, _fSub, _fSmall;

    /// <summary>背景层：用于在行/卡片上做局部裁剪时恢复。</summary>
    static readonly IntPtr CursorArrow = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW);
    static readonly IntPtr CursorHand = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_HAND);

    MainWindow() { }

    public static MainWindow Create()
    {
        var self = new MainWindow();
        s_this = self;
        self.RecalcMetrics();

        var hinst = Win32.GetModuleHandleW(null);
        IntPtr ico = AppIcon.Load();
        var wc = new Win32.WNDCLASSW
        {
            style = Win32.CS_DBLCLKS,     // 双击行打开资源管理器依赖它
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = hinst,
            hCursor = CursorArrow,
            hIcon = ico,
            hbrBackground = IntPtr.Zero,
            lpszClassName = ClassName,
        };
        ushort atom = Win32.RegisterClassW(ref wc);
        if (atom == 0) throw new InvalidOperationException(Loc.F("err.registerClass", Marshal.GetLastWin32Error()));

        IntPtr hwnd = Win32.CreateWindowExW(0, ClassName, Loc.T("app.title"),
            Win32.WS_OVERLAPPEDWINDOW | Win32.WS_CLIPCHILDREN,
            80, 60, self._defW, self._defH, IntPtr.Zero, IntPtr.Zero, hinst, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException(Loc.F("err.createWindow", Marshal.GetLastWin32Error()));
        self.Hwnd = hwnd;

        self.ApplyDpi();
        if (ico != IntPtr.Zero)
        {
            Win32.SendMessageW(hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_BIG, ico);
            IntPtr smallIco = AppIcon.LoadFromFile(Path.Combine(AppIcon.AppDir, "app.ico"), 16);
            Win32.SendMessageW(hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_SMALL, smallIco != IntPtr.Zero ? smallIco : ico);
        }
        Theme.ApplyModernFrame(hwnd);
        self.RebuildRows();
        return self;
    }

    public void ShowWin()
    {
        Win32.ShowWindow(Hwnd, Win32.SW_SHOW);
        Win32.SetForegroundWindow(Hwnd);
        _renderedVersion = -1;
        RebuildRows();
        Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
    }

    public void HideWin() => Win32.ShowWindow(Hwnd, Win32.SW_HIDE);

    public void RequestRepaint() => Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);

    public void OnDataChanged()
    {
        RebuildRows();
        Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
    }

    /// <summary>每秒心跳（UI 线程）：刷新托盘、按需重建列表与重绘。</summary>
    public void OnTick()
    {
        Tray.SyncTip();
        bool scanning = Monitor.Scanning;
        if (Monitor.DataVersion != _renderedVersion) RebuildRows();

        // 扫描刚结束：等两次心跳（列表重建 + 重绘都已完成）再把工作集交还系统
        if (_lastScanning && !scanning) _trimCountdown = 2;
        if (_trimCountdown > 0 && !scanning)
        {
            _trimCountdown--;
            if (_trimCountdown == 0)
            {
                // 列表重建/重绘又分配了一批对象：这里再做一次「会 decommit 的」收集，然后把工作集交还系统
                Monitor.ReleaseMemory();
                Monitor.TrimWorkingSet();
            }
        }

        if (Monitor.FreeBytes != _lastFree || scanning != _lastScanning)
        {
            _lastFree = Monitor.FreeBytes;
            _lastScanning = scanning;
            Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
        }
    }

    // ================= DPI / 字体 / 度量 =================

    int S(int v) => (int)Math.Round(v * _scale);

    void RecalcMetrics()
    {
        _pad = S(18);
        _appBarH = S(68);
        _heroH = S(152);
        _tabH = S(40);
        _statusH = S(32);
        _rowH = S(34);
        _secH = S(34);
        _subRowH = S(54);
        _hdrH = S(34);
        _gutter = S(18);
        _defW = S(1180);
        _defH = S(780);
    }

    void ApplyDpi()
    {
        uint dpi = Dpi.ForWindow(Hwnd);
        if (dpi < 72 || dpi > 480) dpi = 96;
        _dpi = (int)dpi;
        _scale = dpi / 96f;
        RecalcMetrics();
        CreateFonts();

        Win32.GetWindowRect(Hwnd, out var wr);
        int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
        int w = Math.Min(Math.Max(wr.Width, S(1000)), Math.Max(S(600), sw - S(80)));
        int h = Math.Min(Math.Max(wr.Height, S(640)), Math.Max(S(400), sh - S(80)));
        int x = Math.Max(0, (sw - w) / 2), y = Math.Max(0, (sh - h) / 2);
        Win32.SetWindowPos(Hwnd, IntPtr.Zero, x, y, w, h, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        _layoutValid = false;
    }

    void CreateFonts()
    {
        DeleteFonts();
        string face = "Microsoft YaHei UI";
        _fTitle = Theme.CreateUiFont(S(17), 600, face);
        _fBig = Theme.CreateUiFont(S(34), 700, face);
        _fSemi = Theme.CreateUiFont(S(14), 600, face);
        _fMain = Theme.CreateUiFont(S(14), 400, face);
        _fSub = Theme.CreateUiFont(S(12), 400, face);
        _fSmall = Theme.CreateUiFont(S(11), 400, face);
    }

    void DeleteFonts()
    {
        foreach (IntPtr f in new[] { _fTitle, _fBig, _fSemi, _fMain, _fSub, _fSmall })
            if (f != IntPtr.Zero) Win32.DeleteObject(f);
        _fTitle = _fBig = _fSemi = _fMain = _fSub = _fSmall = IntPtr.Zero;
    }

    // ================= 布局（绘制与命中测试共用同一套矩形） =================

    void EnsureLayout()
    {
        Win32.GetClientRect(Hwnd, out var rc);
        int w = Math.Max(1, rc.Width), h = Math.Max(1, rc.Height);
        if (_layoutValid && w == _w && h == _h) return;
        _w = w; _h = h;
        IntPtr dc = Win32.GetDC(Hwnd);
        try
        {
            if (_fMain == IntPtr.Zero) CreateFonts();
            LayoutUi(dc);
        }
        finally { Win32.ReleaseDC(Hwnd, dc); }
        _layoutValid = true;
        UpdateThumb();
    }

    void LayoutUi(IntPtr dc)
    {
        int w = _w, h = _h, pad = _pad;

        // ---- 顶栏按钮（右对齐排列：文件夹分析 / 数据目录 / 复制报告 / 立即扫描）----
        int btnH = S(38);
        int by = (_appBarH - btnH) / 2;
        int x = w - pad;
        int[] widths =
        {
            Theme.Measure(dc, _fSemi, Actions[2].text) + S(38),
            Theme.Measure(dc, _fSemi, Actions[1].text) + S(30),
            Theme.Measure(dc, _fSemi, Actions[0].text) + S(30),
            Theme.Measure(dc, _fSemi, Actions[3].text) + S(30),
        };
        _btnRescan = new Win32.RECT { L = x - widths[0], T = by, R = x, B = by + btnH };
        x = _btnRescan.L - S(8);
        _btnCopy = new Win32.RECT { L = x - widths[1], T = by, R = x, B = by + btnH };
        x = _btnCopy.L - S(8);
        _btnData = new Win32.RECT { L = x - widths[2], T = by, R = x, B = by + btnH };
        x = _btnData.L - S(8);
        _btnFolder = new Win32.RECT { L = x - widths[3], T = by, R = x, B = by + btnH };

        // ---- Hero 卡片 ----
        int heroY = _appBarH + S(2);
        _heroRc = new Win32.RECT { L = pad, T = heroY, R = w - pad, B = heroY + _heroH };

        // ---- 页签 ----
        int tabY = _heroRc.B + S(12);
        _tabsRc = new Win32.RECT { L = pad, T = tabY, R = w - pad, B = tabY + _tabH };
        int tx = _tabsRc.L;
        for (int i = 0; i < PageNames.Length; i++)
        {
            int tw = Theme.Measure(dc, _fSemi, PageNames[i]) + S(30);
            _tabRc[i] = new Win32.RECT { L = tx, T = tabY, R = tx + tw, B = tabY + _tabH };
            tx += tw + S(6);
        }

        // ---- 内容卡片 ----
        int cardY = _tabsRc.B + S(10);
        _cardRc = new Win32.RECT { L = pad, T = cardY, R = w - pad, B = Math.Max(cardY + S(120), h - _statusH - S(10)) };

        _chartH = (_page == 6 && Monitor.History().Count >= 2) ? S(170) : 0;
        _chartRc = new Win32.RECT { L = _cardRc.L + S(18), T = _cardRc.T + S(14), R = _cardRc.R - S(18), B = _cardRc.T + S(14) + _chartH - S(14) };

        int hdrTop = _cardRc.T + S(8) + _chartH;
        _hdrRc = new Win32.RECT { L = _cardRc.L, T = hdrTop, R = _cardRc.R, B = hdrTop + _hdrH };
        _listTop = _hdrRc.B;
        _listBottom = _cardRc.B - S(6);
        _colX0 = _cardRc.L + S(18);
        _colW = Math.Max(S(200), (_cardRc.R - S(18) - _gutter) - _colX0);

        _scrollTrack = new Win32.RECT { L = _cardRc.R - _gutter + S(4), T = _listTop + S(4), R = _cardRc.R - S(4), B = _listBottom - S(4) };

        _statusRc = new Win32.RECT { L = pad, T = h - _statusH, R = w - pad, B = h };
        ClampScroll();
    }

    int RowHeight(Row r) => r.IsSection ? _secH : r.IsBlock ? S(64)
        : r.Sub.Length > 0 ? (_page == 7 ? S(82) : _subRowH) : _rowH;

    int ContentHeight()
    {
        int h = 0;
        foreach (var r in _rows) h += RowHeight(r);
        return h;
    }

    int ViewHeight() => Math.Max(1, _listBottom - _listTop);

    void ClampScroll()
    {
        int content = ContentHeight();
        _scrollMax = Math.Max(0, content - ViewHeight());
        if (_scroll > _scrollMax) _scroll = _scrollMax;
        if (_scroll < 0) _scroll = 0;
        UpdateThumb();
    }

    void UpdateThumb()
    {
        int view = ViewHeight(), content = ContentHeight();
        if (content <= view || _scrollTrack.B - _scrollTrack.T <= S(20))
        {
            _scrollThumb = new Win32.RECT { L = _scrollTrack.L, T = 0, R = _scrollTrack.R, B = 0 };
            return;
        }
        int trackH = _scrollTrack.B - _scrollTrack.T;
        int thumbH = Math.Max(S(36), (int)(trackH * (double)view / content));
        int maxScroll = Math.Max(1, content - view);
        int top = _scrollTrack.T + (int)Math.Round((trackH - thumbH) * (double)_scroll / maxScroll);
        _scrollThumb = new Win32.RECT { L = _scrollTrack.L, T = top, R = _scrollTrack.R, B = top + thumbH };
    }

    // ================= 数据 → 行 =================

    void RebuildRows()
    {
        _rows.Clear();
        _hoverRow = -1;
        _selRow = -1;
        var s = Monitor.Latest;
        switch (_page)
        {
            case 0: BuildOverview(s); break;
            case 1: BuildDirs(s); break;
            case 2: BuildBigFiles(s); break;
            case 3: BuildGrowth(s); break;
            case 4: BuildCategories(s); break;
            case 5: BuildAdvice(); break;
            case 6: BuildHistory(); break;
            case 7: BuildFolderAnalysis(); break;
        }
        _renderedVersion = Monitor.DataVersion;
        _layoutValid = false;
        EnsureLayout();
        ClampScroll();
    }

    static Row Section(string title) => new() { Cells = new[] { title }, IsSection = true };

    static Row Single(string text) => new() { Cells = new[] { text }, Sub = "" };

    static Row Block(string text, uint color = 0) => new()
    {
        Cells = new[] { text },
        IsBlock = true,
        TextColor = color == 0 ? Theme.TextMain : color,
    };

    void BuildOverview(Snapshot? s)
    {
        if (s == null)
        {
            _rows.Add(Single(Loc.T("state.noScanHint")));
            return;
        }

        double usedRatio = s.UsedRatio;
        uint c = usedRatio >= 0.93 ? Theme.Bad : usedRatio >= 0.85 ? Theme.Warn : Theme.Good;
        double freeRatio = s.TotalBytes > 0 ? (double)s.FreeBytes / s.TotalBytes : 0;

        _rows.Add(new Row
        {
            Cells = new[] { Loc.T("hero.freeSpace"), Fmt.Bytes(s.FreeBytes), Loc.F("hero.freeDetail", Fmt.Pct(freeRatio), Fmt.Pct(usedRatio)) },
            Bar = usedRatio, BarColor = c, TextColor = c,
        });
        _rows.Add(new Row
        {
            Cells = new[]
            {
                Loc.T("hero.usedSpace"), Fmt.Bytes(s.UsedBytes),
                Loc.F("hero.capacityFull", Fmt.Bytes(s.TotalBytes), Fmt.Num(s.FileCount), Fmt.Num(s.DirCount)),
            },
        });

        var diff = Monitor.Diff;
        if (diff == null)
        {
            _rows.Add(new Row { Cells = new[] { Loc.T("hero.change"), "—", Loc.T("hero.changeFirst") } });
        }
        else
        {
            _rows.Add(new Row
            {
                Cells = new[]
                {
                    Loc.T("hero.change"),
                    (diff.UsedDelta >= 0 ? "+" : "") + Fmt.Bytes(diff.UsedDelta),
                    Loc.F("hero.changeSince", Fmt.Time(diff.From) + " → " + Fmt.Time(diff.To)),
                },
                Bar = Math.Min(1.0, Math.Abs(diff.UsedDelta) / Math.Max(1.0, s.UsedBytes)),
                BarColor = diff.UsedDelta > 0 ? Theme.Bad : Theme.Good,
                TextColor = diff.UsedDelta > 0 ? Theme.Warn : Theme.Good,
            });
        }

        _rows.Add(Section(Loc.T("hero.conclusion")));
        _rows.Add(Block(diff?.Verdict ?? Loc.T("hero.conclusionFirst"), Theme.TextMain));

        _rows.Add(new Row
        {
            Cells = new[] { Loc.T("hero.unaccounted"), Fmt.Bytes(s.UnaccountedBytes), BuildUnaccountedNote() },
        });
        if (s.HardlinkBytes > 0)
            _rows.Add(new Row
            {
                Cells = new[] { Loc.T("hero.hardlink"), Fmt.Bytes(s.HardlinkBytes), Loc.T("hero.hardlinkNote") },
                TextColor = Theme.TextSub,
            });
        if (s.ShadowUsedBytes > 0)
            _rows.Add(new Row { Cells = new[] { Loc.T("hero.shadow"), Fmt.Bytes(s.ShadowUsedBytes), Loc.T("hero.shadowNote") } });
        if (s.CacheBytes > 0 || s.TempBytes > 0)
            _rows.Add(new Row { Cells = new[] { Loc.T("hero.cacheTemp"), Fmt.Bytes(s.CacheBytes) + " / " + Fmt.Bytes(s.TempBytes), Loc.T("hero.cacheTempNote") } });

        _rows.Add(Section(Loc.T("hero.sysTitle")));
        if (s.SysFiles.Count == 0)
            _rows.Add(new Row { Cells = new[] { Loc.T("hero.none"), "", Loc.T("hero.sysEmpty") } });
        foreach (var sf in s.SysFiles.OrderByDescending(x => x.Bytes))
            _rows.Add(new Row
            {
                Cells = new[] { System.IO.Path.GetFileName(sf.Path), Fmt.Bytes(sf.Bytes), sf.Note },
                TextColor = sf.Bytes > 1L << 30 ? Theme.Warn : Theme.TextMain,
                Path = sf.Path,
            });

        _rows.Add(Section(Loc.T("hero.alerts")));
        if (Monitor.Alerts.Count == 0)
            _rows.Add(new Row { Cells = new[] { Loc.T("hero.none"), "", Loc.T("hero.alertsEmpty") } });
        foreach (var a in Monitor.Alerts)
            _rows.Add(new Row
            {
                Cells = new[] { a.Title, a.LevelLabel, a.Detail },
                TextColor = a.Level == AlertLevel.Critical ? Theme.Bad : a.Level == AlertLevel.Warning ? Theme.Warn : Theme.TextSub,
            });

    }

    static string BuildUnaccountedNote() =>
        SystemUsage.IsAdmin()
            ? Loc.T("hero.unaccountedNote")
            : Loc.F("hero.unaccountedNoteNoAdmin", Loc.T("hero.unaccountedNote"));

    void BuildDirs(Snapshot? s)
    {
        if (s == null) { _rows.Add(Single(Loc.T("state.noData"))); return; }

        var list = new List<DirStat>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in s.Rollups)
            if (d.Level is >= 1 and <= 3 && seenPaths.Add(d.Path)) list.Add(d);
        for (int i = 0; i < s.Dirs.Count && i < 300; i++)
        {
            // 分区键可能与祖先汇总同路径：汇总值是整棵子树，取它即可
            if (seenPaths.Add(s.Dirs[i].Path)) list.Add(s.Dirs[i]);
        }
        if (list.Count == 0) { _rows.Add(Single(Loc.T("dirs.empty"))); return; }
        list.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

        _rows.Add(Section(Loc.T("dirs.hint")));
        long logical = s.LogicalBytes > 0 ? s.LogicalBytes : Math.Max(1, s.UsedBytes);
        int shown = 0;
        foreach (var d in list)
        {
            if (shown++ >= 200) break;
            int indent = Math.Clamp(d.Level - 1, 0, 3);
            _rows.Add(new Row
            {
                Cells = new[]
                {
                    new string(' ', indent * 3) + d.Path,
                    Fmt.Bytes(d.Bytes),
                    Fmt.Pct((double)d.Bytes / logical),
                    "",
                },
                Bar = (double)d.Bytes / logical,
                BarColor = LevelColors[Math.Clamp(d.Level, 0, LevelColors.Length - 1)],
                Path = d.Path,
            });
        }
    }

    void BuildBigFiles(Snapshot? s)
    {
        if (s == null) { _rows.Add(Single(Loc.T("state.noData"))); return; }
        if (s.TopFiles.Count == 0) { _rows.Add(Single(Loc.T("files.none"))); return; }

        var prevPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Monitor.Prev != null) foreach (var f in Monitor.Prev.TopFiles) prevPaths.Add(f.Path);

        _rows.Add(Section(Loc.F("files.hint", Fmt.Num(s.TopFiles.Count))));
        int i = 0;
        foreach (var f in s.TopFiles)
        {
            bool isNew = prevPaths.Count > 0 && !prevPaths.Contains(f.Path);
            _rows.Add(new Row
            {
                Cells = new[]
                {
                    (i + 1).ToString(),
                    Fmt.Bytes(f.Bytes),
                    Fmt.Stamp(f.Modified),
                    f.Path,
                },
                TextColor = isNew ? Theme.Bad : Theme.TextMain,
                Sub = isNew ? Loc.T("files.newMark") : "",
                Path = f.Path,
            });
            i++;
        }
    }

    void BuildGrowth(Snapshot? s)
    {
        var diff = Monitor.Diff;
        if (s == null || diff == null)
        {
            _rows.Add(Section(Loc.T("delta.title")));
            _rows.Add(Single(Loc.T("delta.needTwo")));
            return;
        }

        _rows.Add(Section(Loc.F("delta.growTitle", Fmt.Num(Math.Min(20, diff.Growing.Count)))));
        if (diff.Growing.Count == 0)
            _rows.Add(new Row { Cells = new[] { Loc.T("delta.growEmpty"), "—", Loc.T("delta.growEmptyNote"), "" } });
        long max = 1;
        foreach (var g in diff.Growing.Take(20)) if (g.Delta > max) max = g.Delta;
        foreach (var g in diff.Growing.Take(20))
            _rows.Add(new Row
            {
                Cells = new[] { g.Path, "+" + Fmt.Bytes(g.Delta), Fmt.Bytes(g.NewBytes), "" },
                Bar = (double)g.Delta / max,
                BarColor = g.IsNew ? Theme.Accent : Theme.Warn,
                TextColor = Theme.TextMain,
                Sub = g.IsNew ? Loc.T("delta.newDirMark") : "",
                Path = g.Path,
            });

        _rows.Add(Section(Loc.T("delta.catTitle")));
        var cats = diff.Cats.Where(x => x.Delta != 0).Take(12).ToList();
        if (cats.Count == 0) _rows.Add(new Row { Cells = new[] { Loc.T("delta.catEmpty"), "—", "", "" } });
        foreach (var c in cats)
            _rows.Add(new Row
            {
                Cells = new[] { c.Name, (c.Delta > 0 ? "+" : "") + Fmt.Bytes(c.Delta), Fmt.Bytes(c.NewBytes), "" },
                Bar = c.Delta > 0 ? Math.Min(1.0, (double)c.Delta / Math.Max(1, Math.Abs(diff.UsedDelta))) : 0,
                BarColor = Cat.Colors[c.Index],
            });

        if (diff.Shrinking.Count > 0)
        {
            _rows.Add(Section(Loc.F("delta.shrinkTitle", Fmt.Num(Math.Min(10, diff.Shrinking.Count)))));
            foreach (var g in diff.Shrinking.Take(10))
                _rows.Add(new Row
                {
                    Cells = new[] { g.Path, Fmt.Bytes(g.Delta), g.IsGone ? Loc.T("delta.gone") : Fmt.Bytes(g.NewBytes), "" },
                    TextColor = Theme.Good,
                    Path = g.IsGone ? null : g.Path,
                });
        }

        if (diff.NewFiles.Count > 0)
        {
            _rows.Add(Section(Loc.T("delta.newFiles")));
            foreach (var f in diff.NewFiles.Take(10))
                _rows.Add(new Row
                {
                    Cells = new[] { f.Path, "+" + Fmt.Bytes(f.Bytes), Fmt.Time(f.Modified), "" },
                    TextColor = Theme.Warn,
                    Path = f.Path,
                });
        }
    }

    void BuildCategories(Snapshot? s)
    {
        if (s == null) { _rows.Add(Single(Loc.T("state.noData"))); return; }
        long tot = 0;
        for (int i = 0; i < Cat.Count; i++) tot += s.CatBytes[i];
        if (tot <= 0) { _rows.Add(Single(Loc.T("state.noFiles"))); return; }

        _rows.Add(Section(Loc.F("cats.title", Fmt.Bytes(tot))));
        var order = Enumerable.Range(0, Cat.Count).Where(i => s.CatBytes[i] > 0).OrderByDescending(i => s.CatBytes[i]).ToList();
        foreach (var i in order)
            _rows.Add(new Row
            {
                Cells = new[] { Cat.Name(i), Fmt.Bytes(s.CatBytes[i]), Fmt.Num(s.CatCounts[i]), Fmt.Pct((double)s.CatBytes[i] / tot), "" },
                Bar = (double)s.CatBytes[i] / tot,
                BarColor = Cat.Colors[i],
            });
    }

    void BuildAdvice()
    {
        if (Monitor.Advice.Count == 0)
        {
            _rows.Add(Section(Loc.T("advice.title")));
            _rows.Add(Single(Loc.T("advice.emptyHint")));
            return;
        }
        _rows.Add(Section(Loc.F("advice.topTitle", Fmt.Num(Math.Min(40, Monitor.Advice.Count)))));
        long total = 0;
        int n = 0;
        foreach (var c in Monitor.Advice)
        {
            if (n++ >= 40) break;
            total += c.Bytes;
            _rows.Add(new Row
            {
                Cells = new[]
                {
                    c.Path + (c.MatchCount > 1 ? Loc.F("advice.count", c.MatchCount) : ""),
                    Fmt.Bytes(c.Bytes),
                    Loc.F("advice.anyRisk", c.RiskLabel),
                    c.RuleName + "　" + new string('★', Math.Clamp(c.Stars, 1, 3)),
                },
                Sub = Loc.F("advice.tip", c.Tip) + (c.Approximate ? Loc.T("advice.approx") : ""),
                TextColor = c.Risk == RiskLevel.High ? Theme.Bad : c.Risk == RiskLevel.Mid ? Theme.Warn : Theme.TextMain,
                Path = c.IsRoot ? null : c.Path,
            });
        }
        _rows.Add(Section(Loc.T("advice.total")));
        _rows.Add(new Row
        {
            Cells = new[] { Loc.T("advice.totalLabel"), Fmt.Bytes(total), Loc.T("advice.totalNote") },
            TextColor = Theme.Good,
        });
    }

    void BuildFolderAnalysis()
    {
        _rows.Add(Block(Loc.T("folder.hint"), Theme.TextSub));
        if (_folderAnalyzing) { _rows.Add(Single(Loc.T("folder.scanning"))); return; }
        if (_folderError.Length > 0) { _rows.Add(Block(Loc.F("folder.error", _folderError), Theme.Bad)); return; }
        if (_folderResults == null) { _rows.Add(Single(Loc.T("folder.startHint"))); return; }
        if (_folderResults.Count == 0) { _rows.Add(Single(Loc.T("folder.empty"))); return; }

        long total = 0;
        foreach (var item in _folderResults) total += item.Bytes;
        _rows.Add(Section(Loc.F("folder.summary", Fmt.Num(_folderResults.Count), Fmt.Bytes(total))));
        foreach (var item in _folderResults)
        {
            string kind = item.Kind switch
            {
                FolderKind.DevCache => Loc.T("folder.kind.devCache"),
                FolderKind.BuildOutput => Loc.T("folder.kind.build"),
                FolderKind.ChatData => Loc.T("folder.kind.chat"),
                FolderKind.AppData => Loc.T("folder.kind.app"),
                FolderKind.Extensions => Loc.T("folder.kind.extensions"),
                FolderKind.Downloads => Loc.T("folder.kind.download"),
                _ => Loc.T("folder.kind.personal"),
            };
            string risk = item.Risk switch
            {
                FolderRisk.Critical => Loc.T("folder.risk.critical"),
                FolderRisk.High => Loc.T("folder.risk.high"),
                _ => Loc.T("folder.risk.medium"),
            };
            string advice = item.Kind switch
            {
                FolderKind.DevCache => Loc.T("folder.advice.devCache"),
                FolderKind.BuildOutput => Loc.T("folder.advice.build"),
                FolderKind.ChatData => Loc.T("folder.advice.chat"),
                FolderKind.AppData => Loc.T("folder.advice.app"),
                FolderKind.Extensions => Loc.T("folder.advice.extensions"),
                FolderKind.Downloads => Loc.T("folder.advice.download"),
                _ => Loc.T("folder.advice.personal"),
            };
            string impact = item.Kind switch
            {
                FolderKind.DevCache => Loc.T("folder.impact.devCache"),
                FolderKind.BuildOutput => Loc.T("folder.impact.build"),
                FolderKind.ChatData => Loc.T("folder.impact.chat"),
                FolderKind.AppData => Loc.T("folder.impact.app"),
                FolderKind.Extensions => Loc.T("folder.impact.extensions"),
                FolderKind.Downloads => Loc.T("folder.impact.download"),
                _ => Loc.T("folder.impact.personal"),
            };
            uint color = item.Risk == FolderRisk.Critical ? Theme.Bad
                      : item.Risk == FolderRisk.High ? Theme.Warn : Theme.TextMain;
            string size = Fmt.Bytes(item.Bytes) + (item.UnreadableDirs > 0 ? Loc.T("folder.partial") : "");
            _rows.Add(new Row
            {
                Cells = new[] { kind, item.Path, size, risk },
                Sub = Loc.F("folder.note", advice, impact),
                TextColor = color,
                Path = item.Path,
            });
        }
    }

    void BuildHistory()
    {
        var hist = Monitor.History();
        if (hist.Count == 0) { _rows.Add(Single(Loc.T("history.empty"))); return; }

        _rows.Add(Section(hist.Count >= 2
            ? Loc.F("history.title", Fmt.Num(hist.Count))
            : Loc.F("history.titleNoChart", Fmt.Num(hist.Count))));
        for (int i = hist.Count - 1; i >= 0; i--)
        {
            var h = hist[i];
            long delta = i > 0 ? h.UsedBytes - hist[i - 1].UsedBytes : 0;
            _rows.Add(new Row
            {
                Cells = new[]
                {
                    Fmt.TimeFull(h.Time),
                    Fmt.Bytes(h.FreeBytes),
                    Fmt.Bytes(h.UsedBytes),
                    i > 0 ? (delta >= 0 ? "+" : "") + Fmt.Bytes(delta) : "—",
                    Fmt.Pct(h.UsedRatio),
                },
                Bar = h.UsedRatio,
                BarColor = h.UsedRatio >= 0.93 ? Theme.Bad : h.UsedRatio >= 0.85 ? Theme.Warn : Theme.Accent,
            });
        }
    }

    // ================= 绘制 =================

    static bool PtIn(in Win32.RECT rc, int x, int y) => x >= rc.L && x < rc.R && y >= rc.T && y < rc.B;

    void DrawAll(IntPtr hdc)
    {
        Theme.Fill(hdc, new Win32.RECT { L = 0, T = 0, R = _w, B = _h }, Theme.Canvas);
        DrawAppBar(hdc);
        DrawHero(hdc);
        DrawTabs(hdc);
        DrawCard(hdc);
        DrawStatus(hdc);
    }

    void DrawAppBar(IntPtr hdc)
    {
        int cy = _appBarH / 2;
        // Logo
        var logo = new Win32.RECT { L = _pad, T = cy - S(14), R = _pad + S(28), B = cy + S(14) };
        Theme.FillRound(hdc, logo, S(9), Theme.Accent);
        Theme.Text(hdc, _fSemi, "C", logo, Theme.TextOnAccent, Theme.AlignCenter);

        // 标题 + 副标题
        int tx = logo.R + S(12);
        int textRight = Math.Max(tx + S(120), _btnFolder.L - S(12));
        Theme.Text(hdc, _fTitle, Loc.T("app.title"), new Win32.RECT { L = tx, T = cy - S(22), R = Math.Min(tx + S(460), textRight), B = cy - S(2) }, Theme.TextMain, Theme.AlignLeft);
        Theme.Text(hdc, _fSmall, Loc.T("app.subtitle"), new Win32.RECT { L = tx + S(1), T = cy + S(1), R = Math.Min(tx + S(620), textRight), B = cy + S(19) }, Theme.TextDim, Theme.AlignLeft);

        DrawActionButton(hdc, _btnRescan, Actions[2].text, primary: true, enabled: !Monitor.Scanning, actionIndex: 0,
            labelOverride: Monitor.Scanning ? Loc.T("status.scanningShort") : null);
        DrawActionButton(hdc, _btnCopy, Actions[1].text, primary: false, enabled: Monitor.Latest != null, actionIndex: 1);
        DrawActionButton(hdc, _btnData, Actions[0].text, primary: false, enabled: true, actionIndex: 2);
        DrawActionButton(hdc, _btnFolder, Actions[3].text, primary: false, enabled: true, actionIndex: 3);
    }

    void DrawActionButton(IntPtr hdc, in Win32.RECT rc, string text, bool primary, bool enabled, int actionIndex, string? labelOverride = null)
    {
        bool hover = enabled && _hoverAction == actionIndex;
        uint bg, fg, border;
        if (!enabled) { bg = Theme.SurfaceAlt; fg = Theme.TextDim; border = Theme.SurfaceBorder; }
        else if (primary) { bg = hover ? Theme.AccentHover : Theme.Accent; fg = Theme.TextOnAccent; border = bg; }
        else { bg = hover ? Theme.Hover : Theme.Surface; fg = Theme.TextSub; border = Theme.SurfaceBorder; }

        Theme.Card(hdc, rc, S(9), bg, border);
        Theme.Text(hdc, _fSemi, labelOverride ?? text, rc, fg, Theme.AlignCenter);
    }

    void DrawHero(IntPtr hdc)
    {
        Theme.Card(hdc, _heroRc, S(14), Theme.Surface, Theme.SurfaceBorder);
        int padL = _heroRc.L + S(22);
        int padR = _heroRc.R - S(22);
        int top = _heroRc.T + S(10);

        long free = Monitor.FreeBytes > 0 ? Monitor.FreeBytes : (Monitor.Latest?.FreeBytes ?? 0);
        long total = Monitor.TotalBytes > 0 ? Monitor.TotalBytes : (Monitor.Latest?.TotalBytes ?? 0);
        long used = Math.Max(0, total - free);
        double usedRatio = total > 0 ? (double)used / total : 0;
        uint sev = usedRatio >= 0.93 ? Theme.Bad : usedRatio >= 0.85 ? Theme.Warn : Theme.Good;

        // ---- 左：可用空间 ----
        Theme.Text(hdc, _fSub, Loc.T("hero.freeLabel"), new Win32.RECT { L = padL, T = top, R = padL + S(460), B = top + S(20) }, Theme.TextDim, Theme.AlignLeft);
        string big = total > 0 ? Fmt.Bytes(free) : "—";
        Theme.Text(hdc, _fBig, big, new Win32.RECT { L = padL, T = top + S(18), R = padL + S(460), B = top + S(62) }, total > 0 ? sev : Theme.TextDim, Theme.AlignLeft);
        string detail = total > 0
            ? Loc.F("hero.capacity", Fmt.Bytes(total), Fmt.Pct((double)free / total))
            : Loc.T("hero.noDisk");
        Theme.Text(hdc, _fSub, detail, new Win32.RECT { L = padL, T = top + S(64), R = padL + S(600), B = top + S(84) }, Theme.TextSub, Theme.AlignLeft);

        // ---- 右：状态徽标 + 扫描信息（右对齐，纵向不与左侧文案重叠）----
        int rx = padR;
        var diff = Monitor.Diff;
        string badgeText; uint badgeFg, badgeBg;
        if (Monitor.Scanning) { badgeText = Loc.T("hero.scanning"); badgeFg = Theme.AccentText; badgeBg = Theme.AccentSoft; }
        else if (diff == null) { badgeText = Loc.T("hero.firstScan"); badgeFg = Theme.TextSub; badgeBg = Theme.SurfaceAlt; }
        else if (diff.UsedDelta > 0)
        {
            badgeText = Loc.F("hero.grew", Fmt.Bytes(diff.UsedDelta));
            badgeFg = diff.UsedDelta > 1L << 30 ? Theme.Bad : Theme.Warn;
            badgeBg = diff.UsedDelta > 1L << 30 ? Theme.BadSoft : Theme.WarnSoft;
        }
        else if (diff.UsedDelta < 0)
        {
            badgeText = Loc.F("hero.shrank", Fmt.Bytes(-diff.UsedDelta));
            badgeFg = Theme.Good; badgeBg = Theme.GoodSoft;
        }
        else { badgeText = Loc.T("hero.noChange"); badgeFg = Theme.TextSub; badgeBg = Theme.SurfaceAlt; }

        int pillH = S(28);
        int pw = Theme.PillWidth(hdc, _fSub, badgeText, S(14));
        Theme.Pill(hdc, rx - pw, top + S(2), pillH, badgeText, _fSub, badgeFg, badgeBg, S(14));

        string scanInfo;
        if (Monitor.LastScanTime <= DateTimeOffset.MinValue) scanInfo = Loc.T("hero.neverScanned");
        else if (Monitor.LastScanMs > 0) scanInfo = Loc.F("hero.lastScan", Fmt.TimeFull(Monitor.LastScanTime), Fmt.Seconds(Monitor.LastScanMs / 1000.0));
        else scanInfo = Loc.F("hero.lastScanNoMs", Fmt.TimeFull(Monitor.LastScanTime));
        Theme.Text(hdc, _fSmall, scanInfo, new Win32.RECT { L = rx - S(460), T = top + S(36), R = rx, B = top + S(56) }, Theme.TextDim, Theme.AlignRight);

        string nextInfo = Monitor.NextScanTime > DateTimeOffset.MinValue
            ? Loc.F("hero.nextScan", Fmt.Time(Monitor.NextScanTime))
            : Loc.T("hero.autoOff");
        Theme.Text(hdc, _fSmall, nextInfo, new Win32.RECT { L = rx - S(460), T = top + S(58), R = rx, B = top + S(78) }, Theme.TextDim, Theme.AlignRight);

        // ---- 底部：占用率进度条（含语义明确的左右标注）----
        int barH = S(10);
        int barY = _heroRc.B - S(18) - barH;
        int barW = padR - padL;
        Theme.Text(hdc, _fSmall, Loc.F("hero.usedLine", Fmt.Bytes(used), Fmt.Pct(usedRatio)),
            new Win32.RECT { L = padL, T = barY - S(22), R = padL + S(400), B = barY - S(4) }, Theme.TextSub, Theme.AlignLeft);
        Theme.Text(hdc, _fSmall, Loc.F("hero.freeLine", Fmt.Bytes(free), Fmt.Pct(total > 0 ? (double)free / total : 0)),
            new Win32.RECT { L = padR - S(400), T = barY - S(22), R = padR, B = barY - S(4) }, Theme.TextDim, Theme.AlignRight);
        Theme.Bar(hdc, padL, barY, barW, barH, usedRatio, sev);
    }

    void DrawTabs(IntPtr hdc)
    {
        for (int i = 0; i < PageNames.Length; i++)
        {
            var rc = _tabRc[i];
            bool active = i == _page;
            if (active)
            {
                Theme.Card(hdc, rc, S(9), Theme.AccentSoft, Win32.Rgb(0xC2, 0xD6, 0xFB));
                Theme.Text(hdc, _fSemi, PageNames[i], rc, Theme.AccentText, Theme.AlignCenter);
            }
            else
            {
                if (i == _hoverTab) Theme.FillRound(hdc, rc, S(9), Theme.Hover);
                Theme.Text(hdc, _fSemi, PageNames[i], rc, Theme.TextSub, Theme.AlignCenter);
            }
        }
    }

    void DrawCard(IntPtr hdc)
    {
        Theme.Card(hdc, _cardRc, S(12), Theme.Surface, Theme.SurfaceBorder);

        if (_chartH > 0) DrawHistoryChart(hdc);

        // ---- 表头（裁剪到卡片圆角内，避免直角盖住卡片圆角）----
        var hdrBand = new Win32.RECT { L = _cardRc.L, T = _hdrRc.T, R = _cardRc.R, B = _hdrRc.B };
        int hdrSaved = Win32.SaveDC(hdc);
        IntPtr hdrRgn = Win32.CreateRoundRectRgn(_cardRc.L, _cardRc.T, _cardRc.R + 1, _cardRc.B + 1, S(12) * 2, S(12) * 2);
        Win32.SelectClipRgn(hdc, hdrRgn);
        Theme.Fill(hdc, hdrBand, Theme.SurfaceAlt);
        Win32.RestoreDC(hdc, hdrSaved);
        Win32.DeleteObject(hdrRgn);
        Theme.CardBorder(hdc, _cardRc, S(12), Theme.SurfaceBorder);
        Theme.HLine(hdc, _cardRc.L + S(1), _cardRc.R - S(1), _hdrRc.B, Theme.Divider);

        var cols = Cols[_page];
        int cx = _colX0;
        foreach (var col in cols)
        {
            int cw = (int)(_colW * col.Weight);
            if (col.Title.Length > 0)
            {
                int hp = (col.Style == ColStyle.Value || col.Style == ColStyle.Index) ? S(2) : S(6);
                var trc = new Win32.RECT { L = cx + hp, T = _hdrRc.T, R = cx + cw - S(10), B = _hdrRc.B };
                Theme.Text(hdc, _fSmall, col.Title, trc, Theme.TextDim, col.Align);
            }
            cx += cw;
        }

        // ---- 行（裁剪在列表区域内，避免滚到表头下面）----
        if (_rows.Count == 1 && !_rows[0].IsSection && !_rows[0].IsBlock && _rows[0].Cells.Length == 1)
        {
            DrawEmptyState(hdc, _rows[0].Cells[0]);
            DrawScrollbar(hdc);
            return;
        }

        int saved = Win32.SaveDC(hdc);
        IntPtr rgn = Win32.CreateRectRgn(_cardRc.L + S(1), _listTop, _cardRc.R - S(1), _listBottom);
        Win32.SelectClipRgn(hdc, rgn);

        int y = _listTop - _scroll;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            int h = RowHeight(r);
            if (y + h > _listTop && y < _listBottom) DrawRow(hdc, r, i, y, h);
            y += h;
            if (y > _listBottom) break;
        }

        Win32.RestoreDC(hdc, saved);
        Win32.DeleteObject(rgn);
        DrawScrollbar(hdc);
    }

    void DrawRow(IntPtr hdc, Row r, int index, int y, int h)
    {
        int bandRight = _scrollTrack.L;
        var band = new Win32.RECT { L = _cardRc.L + S(1), T = y, R = bandRight, B = y + h };
        int innerRight = _colX0 + _colW;

        if (r.IsSection)
        {
            Theme.Fill(hdc, band, Theme.SurfaceAlt);
            var mark = new Win32.RECT { L = _colX0, T = y + S(10), R = _colX0 + S(3), B = y + h - S(10) };
            Theme.Fill(hdc, mark, Theme.Accent);
            Theme.Text(hdc, _fSemi, r.Cells.Length > 0 ? r.Cells[0] : "",
                new Win32.RECT { L = _colX0 + S(10), T = y, R = innerRight, B = y + h }, Theme.TextSub, Theme.AlignLeft);
            return;
        }

        bool hover = index == _hoverRow;
        bool selected = index == _selRow;

        if (r.IsBlock)
        {
            Theme.Fill(hdc, band, index % 2 == 1 ? Theme.Zebra : Theme.Surface);
            Theme.TextWrap(hdc, _fSub, r.Cells.Length > 0 ? r.Cells[0] : "",
                new Win32.RECT { L = _colX0, T = y + S(6), R = innerRight, B = y + h - S(6) }, r.TextColor);
            return;
        }
        uint bg = selected ? Theme.Sel : hover ? Theme.Hover : (index % 2 == 1 ? Theme.Zebra : Theme.Surface);
        Theme.Fill(hdc, band, bg);
        Theme.HLine(hdc, band.L, band.R - 1, y + h - 1, Win32.Rgb(0xE2, 0xE8, 0xF2));
        if (selected) Theme.Fill(hdc, new Win32.RECT { L = band.L, T = y + S(4), R = band.L + S(3), B = y + h - S(4) }, Theme.Accent);

        int textTop = y + S(1);
        int textBottom = r.Sub.Length > 0
            ? y + h - S(_page == 7 ? 44 : 20)
            : y + h - S(1);

        var cols = Cols[_page];
        int cx = _colX0;
        for (int ci = 0; ci < cols.Length; ci++)
        {
            var col = cols[ci];
            int cw = (int)(_colW * col.Weight);
            string text = ci < r.Cells.Length ? r.Cells[ci] : "";
            int cellPadL = (col.Style == ColStyle.Value || col.Style == ColStyle.Index) ? S(2) : S(6);
            var cell = new Win32.RECT { L = cx + cellPadL, T = textTop, R = cx + cw - S(10), B = textBottom };

            switch (col.Style)
            {
                case ColStyle.Bar:
                    {
                        int barH = S(9);
                        int barW = cw - S(10);
                        int extra = 0;
                        if (text.Length > 0) { extra = S(56); barW = Math.Max(S(40), cw - extra); }
                        int by = y + (h - barH) / 2;
                        if (r.Sub.Length > 0) by = y + (h - S(20) - barH) / 2;
                        Theme.Bar(hdc, cx, by, barW, barH, r.Bar < 0 ? 0 : r.Bar, r.BarColor);
                        if (extra > 0)
                            Theme.Text(hdc, _fSub, text,
                                new Win32.RECT { L = cx + barW + S(8), T = by - S(6), R = cx + cw - S(8), B = by + barH + S(6) },
                                Theme.TextSub, Theme.AlignRight);
                        break;
                    }
                case ColStyle.Tag:
                    {
                        if (text.Length == 0) break;
                        uint fg = r.TextColor == Theme.TextMain ? Theme.TextSub : r.TextColor;
                        uint soft = r.TextColor == Theme.Bad ? Theme.BadSoft : r.TextColor == Theme.Warn ? Theme.WarnSoft : Theme.SurfaceAlt;
                        int pillH = S(20);
                        int py = y + (h - pillH) / 2;
                        Theme.Pill(hdc, cx, py, pillH, text, _fSub, fg, soft, S(9));
                        break;
                    }
                case ColStyle.Primary:
                    Theme.Text(hdc, _fMain, text, cell, r.TextColor,
                        col.Align == Theme.AlignRight ? Theme.AlignRight : Theme.AlignLeftPath);
                    break;
                case ColStyle.Value:
                    Theme.Text(hdc, _fSemi, text, cell, Theme.TextMain, col.Align);
                    break;
                case ColStyle.Index:
                    Theme.Text(hdc, _fSub, text, cell, Theme.TextDim, col.Align);
                    break;
                default:
                    Theme.Text(hdc, _fSub, text, cell, Theme.TextSub, col.Align);
                    break;
            }
            cx += cw;
        }

        if (r.Sub.Length > 0)
        {
            var subRc = new Win32.RECT
            {
                L = _colX0, T = y + h - S(_page == 7 ? 42 : 19),
                R = innerRight, B = y + h - S(2)
            };
            if (_page == 7) Theme.TextWrap(hdc, _fSmall, r.Sub, subRc, Theme.TextDim);
            else Theme.Text(hdc, _fSmall, r.Sub, subRc, Theme.TextDim, Theme.AlignLeft);
        }
    }

    void DrawEmptyState(IntPtr hdc, string message)
    {
        int cx = (_cardRc.L + _cardRc.R) / 2;
        int cy = (_listTop + _listBottom) / 2;
        Theme.Dot(hdc, cx, cy - S(28), S(18), Theme.SurfaceAlt);
        Theme.Text(hdc, _fSemi, "!", new Win32.RECT { L = cx - S(20), T = cy - S(44), R = cx + S(20), B = cy - S(12) }, Theme.TextDim, Theme.AlignCenter);
        Theme.Text(hdc, _fMain, message, new Win32.RECT { L = _cardRc.L + S(40), T = cy, R = _cardRc.R - S(40), B = cy + S(26) }, Theme.TextSub, Theme.AlignCenter);
    }

    void DrawScrollbar(IntPtr hdc)
    {
        if (_scrollThumb.B <= _scrollThumb.T) return;
        int w = _scrollThumb.R - _scrollThumb.L;
        bool active = _thumbDrag || (_hoverRow == -2);
        // 轨道：浅色圆角底，让滚动条有明确的"可拖动"暗示
        var track = new Win32.RECT { L = _scrollTrack.L, T = _scrollTrack.T, R = _scrollTrack.R, B = _scrollTrack.B };
        Theme.FillRound(hdc, track, Math.Max(2, w / 2), Theme.ScrollTrack);
        Theme.FillRound(hdc, _scrollThumb, Math.Max(2, w / 2), active ? Theme.Accent : Theme.ScrollThumb);
    }

    void DrawHistoryChart(IntPtr hdc)
    {
        var hist = Monitor.History();
        if (hist.Count < 2) return;

        var box = new Win32.RECT { L = _chartRc.L, T = _chartRc.T, R = _chartRc.R, B = _chartRc.B };
        Theme.Card(hdc, box, S(10), Theme.SurfaceAlt, Theme.Divider);

        long min = long.MaxValue, max = long.MinValue;
        foreach (var h in hist) { if (h.FreeBytes < min) min = h.FreeBytes; if (h.FreeBytes > max) max = h.FreeBytes; }
        if (max - min < 1) { max = min + 1; }

        Theme.Text(hdc, _fSemi, Loc.T("history.chartTitle"), new Win32.RECT { L = box.L + S(16), T = box.T + S(8), R = box.R - S(16), B = box.T + S(28) }, Theme.TextSub, Theme.AlignLeft);
        Theme.Text(hdc, _fSmall, Loc.F("history.chartInfo", Fmt.Num(hist.Count), Fmt.Bytes(max), Fmt.Bytes(min)),
            new Win32.RECT { L = box.L + S(16), T = box.T + S(8), R = box.R - S(16), B = box.T + S(28) }, Theme.TextDim, Theme.AlignRight);

        int plotL = box.L + S(72), plotR = box.R - S(20);
        int plotT = box.T + S(42), plotB = box.B - S(30);
        if (plotR - plotL < S(80) || plotB - plotT < S(24)) return;

        // 水平网格 + y 轴刻度（3 条）
        for (int g = 0; g <= 2; g++)
        {
            int gy = plotB - (plotB - plotT) * g / 2;
            Theme.HLine(hdc, plotL, plotR, gy, Theme.Divider);
            long v = min + (long)((max - min) * (g / 2.0));
            Theme.Text(hdc, _fSmall, Fmt.Bytes(v),
                new Win32.RECT { L = box.L + S(10), T = gy - S(9), R = plotL - S(8), B = gy + S(9) },
                Theme.TextDim, Theme.AlignRight);
        }

        int n = hist.Count;
        var line = new Win32.POINT[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / (n - 1);
            double v = (double)(hist[i].FreeBytes - min) / (max - min);
            line[i] = new Win32.POINT
            {
                X = plotL + (int)Math.Round((plotR - plotL) * t),
                Y = plotB - (int)Math.Round((plotB - plotT) * v),
            };
        }

        // 面积（半透明感的浅蓝）+ 折线 + 末点
        var area = new Win32.POINT[n + 2];
        Array.Copy(line, area, n);
        area[n] = new Win32.POINT { X = line[n - 1].X, Y = plotB };
        area[n + 1] = new Win32.POINT { X = line[0].X, Y = plotB };
        Theme.Polygon(hdc, area, Win32.Rgb(0xE9, 0xF0, 0xFE));

        Theme.Polyline(hdc, line, Theme.Accent, S(2));
        Theme.Dot(hdc, line[n - 1].X, line[n - 1].Y, S(4), Theme.Accent);

        // x 轴标签放在绘图区下方
        Theme.Text(hdc, _fSmall, Fmt.Time(hist[0].Time),
            new Win32.RECT { L = plotL, T = plotB + S(4), R = plotL + S(180), B = plotB + S(22) }, Theme.TextDim, Theme.AlignLeft);
        Theme.Text(hdc, _fSmall, Fmt.Time(hist[n - 1].Time),
            new Win32.RECT { L = plotR - S(180), T = plotB + S(4), R = plotR, B = plotB + S(22) }, Theme.TextDim, Theme.AlignRight);
    }

    void DrawStatus(IntPtr hdc)
    {
        Theme.HLine(hdc, _statusRc.L, _statusRc.R, _statusRc.T, Theme.Divider);

        uint dot = Monitor.StatusKindNow == Monitor.StatusKind.Failed ? Theme.Bad
                 : Monitor.Scanning ? Theme.Accent
                 : Theme.Good;
        int cy = (_statusRc.T + _statusRc.B) / 2;
        Theme.Dot(hdc, _statusRc.L + S(5), cy, S(4), dot);

        string? pathHint = null;
        int hintRow = _hoverRow >= 0 ? _hoverRow : _selRow;
        if (hintRow >= 0 && hintRow < _rows.Count && !_rows[hintRow].IsSection) pathHint = _rows[hintRow].Path;
        string status = !string.IsNullOrEmpty(pathHint)
            ? Loc.F("status.path", pathHint)
            : _page == 7 && _folderAnalyzing ? Loc.T("folder.scanning")
            : Monitor.Scanning ? Loc.T("status.scanning") : Monitor.Status;
        Theme.Text(hdc, _fSmall, status, new Win32.RECT { L = _statusRc.L + S(16), T = _statusRc.T, R = _statusRc.L + S(760), B = _statusRc.B }, Theme.TextSub, Theme.AlignLeft);

        string right = Loc.T("status.hint");
        Theme.Text(hdc, _fSmall, right, new Win32.RECT { L = _statusRc.R - S(620), T = _statusRc.T, R = _statusRc.R, B = _statusRc.B }, Theme.TextDim, Theme.AlignRight);
    }

    // ================= 双缓冲绘制 =================

    // 后台缓冲：复用同一块位图/DC（原来每帧新建 1180×780 兼容位图 = 3.5MB，鼠标悬停时白扔）
    IntPtr _memDC, _memBmp;
    int _memW, _memH;

    void EnsureBackBuffer(IntPtr hdc)
    {
        if (_memDC == IntPtr.Zero) _memDC = Win32.CreateCompatibleDC(hdc);
        int w = Math.Max(1, _w), h = Math.Max(1, _h);
        if (_memBmp == IntPtr.Zero)
        {
            _memBmp = Win32.CreateCompatibleBitmap(hdc, w, h);
            Win32.SelectObject(_memDC, _memBmp);
            _memW = w;
            _memH = h;
            return;
        }
        if (_memW == w && _memH == h) return;
        IntPtr nb = Win32.CreateCompatibleBitmap(hdc, w, h);
        Win32.SelectObject(_memDC, nb);     // 旧位图先被选出去，才能安全删除
        Win32.DeleteObject(_memBmp);
        _memBmp = nb;
        _memW = w;
        _memH = h;
    }

    void FreeBackBuffer()
    {
        // 先删 DC（解除位图选中），再删位图
        if (_memDC != IntPtr.Zero) { Win32.DeleteDC(_memDC); _memDC = IntPtr.Zero; }
        if (_memBmp != IntPtr.Zero) { Win32.DeleteObject(_memBmp); _memBmp = IntPtr.Zero; }
        _memW = _memH = 0;
    }

    void PaintTo(IntPtr hdc)
    {
        EnsureLayout();
        EnsureBackBuffer(hdc);
        DrawAll(_memDC);
        Win32.BitBlt(hdc, 0, 0, Math.Max(1, _w), Math.Max(1, _h), _memDC, 0, 0, Win32.SRCCOPY);
    }

    // ================= 命中测试 =================

    int _lastMouseX = -1, _lastMouseY = -1;

    int RowIndexAt(int x, int y)
    {
        if (y < _listTop || y >= _listBottom) return -1;
        if (x < _cardRc.L || x >= _cardRc.R) return -1;
        int top = _listTop - _scroll;
        for (int i = 0; i < _rows.Count; i++)
        {
            int h = RowHeight(_rows[i]);
            if (y >= top && y < top + h) return _rows[i].IsSection ? -1 : i;
            top += h;
            if (top > _listBottom) break;
        }
        return -1;
    }

    int TabAt(int x, int y)
    {
        for (int i = 0; i < _tabRc.Length; i++) if (PtIn(_tabRc[i], x, y)) return i;
        return -1;
    }

    int ActionAt(int x, int y)
    {
        if (PtIn(_btnRescan, x, y)) return 0;
        if (PtIn(_btnCopy, x, y)) return 1;
        if (PtIn(_btnData, x, y)) return 2;
        if (PtIn(_btnFolder, x, y)) return 3;
        return -1;
    }

    // ================= 交互 =================

    void SwitchPage(int page)
    {
        if (page < 0 || page >= PageNames.Length || page == _page) return;
        _page = page;
        _scroll = 0;
        _layoutValid = false;
        RebuildRows();
        if (page == 7 && _folderResults == null && !_folderAnalyzing) StartFolderAnalysis();
        Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
    }

    void StartFolderAnalysis()
    {
        if (_folderAnalyzing) return;
        _page = 7;
        _scroll = 0;
        _folderResults = null;
        _folderError = "";
        _folderAnalyzing = true;
        RebuildRows();
        Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { _folderResults = FolderAnalyzer.Scan(Monitor.Cfg.Drive); }
            catch (Exception ex) { _folderError = ex.Message; }
            finally
            {
                _folderAnalyzing = false;
                Win32.PostMessageW(Hwnd, Win32.WM_APP_FOLDER_DONE, IntPtr.Zero, IntPtr.Zero);
            }
        });
    }

    void EnsureVisible(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        int top = 0;
        for (int i = 0; i < index; i++) top += RowHeight(_rows[i]);
        int h = RowHeight(_rows[index]);
        int view = ViewHeight();
        if (top < _scroll) _scroll = top;
        else if (top + h > _scroll + view) _scroll = top + h - view;
        ClampScroll();
    }

    void MoveSelection(int delta)
    {
        if (_rows.Count == 0) return;
        int start = _selRow < 0 ? (delta > 0 ? -1 : _rows.Count) : _selRow;
        int i = start;
        for (int guard = 0; guard < _rows.Count + 2; guard++)
        {
            i += delta;
            if (i < 0) { i = 0; break; }
            if (i >= _rows.Count) { i = _rows.Count - 1; break; }
            if (!_rows[i].IsSection) break;
        }
        _selRow = i;
        EnsureVisible(i);
        Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
    }

    void OnClick(int mx, int my)
    {
        int action = ActionAt(mx, my);
        if (action >= 0)
        {
            switch (action)
            {
                case 0: Monitor.RunScan(true); break;
                case 1: Program.CopyReport(); break;
                case 2: Program.OpenDataDir(); break;
                case 3: StartFolderAnalysis(); break;
            }
            Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
            return;
        }

        int tab = TabAt(mx, my);
        if (tab >= 0) { SwitchPage(tab); return; }

        // 滚动条
        if (_scrollThumb.B > _scrollThumb.T && PtIn(_scrollThumb, mx, my))
        {
            _thumbDrag = true;
            _dragY0 = my;
            _dragScroll0 = _scroll;
            Win32.SetCapture(Hwnd);
            return;
        }
        if (PtIn(_scrollTrack, mx, my))
        {
            int dir = my < _scrollThumb.T ? -1 : 1;
            _scroll = Math.Clamp(_scroll + dir * ViewHeight() / 2, 0, _scrollMax);
            UpdateThumb();
            Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
            return;
        }

        int row = RowIndexAt(mx, my);
        if (row >= 0)
        {
            _selRow = row;
            Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
        }
    }

    void OnMouseMove(int mx, int my)
    {
        _lastMouseX = mx;
        _lastMouseY = my;

        if (_thumbDrag)
        {
            int content = ContentHeight(), view = ViewHeight();
            int trackH = _scrollTrack.B - _scrollTrack.T;
            int thumbH = Math.Max(1, _scrollThumb.B - _scrollThumb.T);
            int range = Math.Max(1, trackH - thumbH);
            int delta = my - _dragY0;
            _scroll = Math.Clamp(_dragScroll0 + (int)Math.Round((double)delta * Math.Max(1, content - view) / range), 0, _scrollMax);
            UpdateThumb();
            Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
            return;
        }

        int newTab = TabAt(mx, my);
        int newAction = ActionAt(mx, my);
        int newRow = RowIndexAt(mx, my);
        bool thumbHover = _scrollThumb.B > _scrollThumb.T && PtIn(_scrollThumb, mx, my);

        bool clickable = newTab >= 0 || newAction >= 0 || thumbHover || (newRow >= 0 && _rows[newRow].Path != null);
        Win32.SetCursor(clickable ? CursorHand : CursorArrow);

        int hoverRow = thumbHover ? -2 : newRow;
        if (hoverRow != _hoverRow || newTab != _hoverTab || newAction != _hoverAction)
        {
            _hoverRow = hoverRow;
            _hoverTab = newTab;
            _hoverAction = newAction;
            Win32.InvalidateRect(Hwnd, IntPtr.Zero, false);
        }
    }

    // ================= 消息处理 =================

    static IntPtr WndProcStatic(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = s_this;
        if (self == null) return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        return self.WndProc(hwnd, msg, wParam, lParam);
    }

    static int LoWord(IntPtr v) => (short)(v.ToInt64() & 0xFFFF);
    static int HiWord(IntPtr v) => (short)((v.ToInt64() >> 16) & 0xFFFF);

    IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_PAINT:
                {
                    IntPtr hdc = Win32.BeginPaint(hwnd, out var ps);
                    PaintTo(hdc);
                    Win32.EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                }
            case 0x0317: // WM_PRINT（截图工具用）
            case 0x0318: // WM_PRINTCLIENT
                {
                    IntPtr hdc = lParam;
                    if (hdc != IntPtr.Zero) PaintTo(hdc);
                    return IntPtr.Zero;
                }
            case Win32.WM_ERASEBKGND:
                return (IntPtr)1;
            case Win32.WM_SIZE:
                _layoutValid = false;
                EnsureLayout();
                Win32.InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            case Win32.WM_DPICHANGED:
                {
                    _layoutValid = false;
                    ApplyDpi();
                    EnsureLayout();
                    Win32.InvalidateRect(hwnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                }
            case Win32.WM_GETMINMAXINFO:
                {
                    var mmi = Marshal.PtrToStructure<Win32.MINMAXINFO>(lParam);
                    mmi.ptMinTrackSize.X = S(900);
                    mmi.ptMinTrackSize.Y = S(560);
                    Marshal.StructureToPtr(mmi, lParam, false);
                    return IntPtr.Zero;
                }
            case Win32.WM_LBUTTONDOWN:
                {
                    // 自绘窗口不调 DefWindowProc 就收不到键盘消息（F5/方向键/滚轮都依赖焦点）
                    Win32.SetFocus(hwnd);
                    OnClick(LoWord(lParam), HiWord(lParam));
                    return IntPtr.Zero;
                }
            case Win32.WM_LBUTTONUP:
                if (_thumbDrag)
                {
                    _thumbDrag = false;
                    Win32.ReleaseCapture();
                }
                return IntPtr.Zero;
            case Win32.WM_LBUTTONDBLCLK:
                {
                    int mx = LoWord(lParam), my = HiWord(lParam);
                    int i = RowIndexAt(mx, my);
                    if (i >= 0 && _rows[i].Path != null) Program.OpenInExplorer(_rows[i].Path!);
                    return IntPtr.Zero;
                }
            case Win32.WM_MOUSEMOVE:
                OnMouseMove(LoWord(lParam), HiWord(lParam));
                {
                    var tme = new Win32.TRACKMOUSEEVENT
                    {
                        cbSize = Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
                        dwFlags = Win32.TME_LEAVE,
                        hwndTrack = hwnd,
                    };
                    Win32.TrackMouseEvent(ref tme);
                }
                return IntPtr.Zero;
            case Win32.WM_MOUSELEAVE:
                _hoverRow = -1;
                _hoverTab = -1;
                _hoverAction = -1;
                _lastMouseX = _lastMouseY = -1;
                Win32.InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL:
                {
                    int delta = HiWord(wParam);
                    int step = Math.Max(S(28), _rowH);
                    _scroll = Math.Clamp(_scroll - Math.Sign(delta) * step * 3, 0, _scrollMax);
                    UpdateThumb();
                    Win32.InvalidateRect(hwnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                }
            case Win32.WM_KEYDOWN:
                {
                    int key = (int)wParam;
                    switch (key)
                    {
                        case 0x26: MoveSelection(-1); return IntPtr.Zero;          // ↑
                        case 0x28: MoveSelection(1); return IntPtr.Zero;           // ↓
                        case 0x21: _scroll = Math.Clamp(_scroll - ViewHeight(), 0, _scrollMax); break; // PageUp
                        case 0x22: _scroll = Math.Clamp(_scroll + ViewHeight(), 0, _scrollMax); break; // PageDown
                        case 0x24: _scroll = 0; break;                             // Home
                        case 0x23: _scroll = _scrollMax; break;                     // End
                        case 0x25: SwitchPage((_page + PageNames.Length - 1) % PageNames.Length); return IntPtr.Zero; // ←
                        case 0x27: SwitchPage((_page + 1) % PageNames.Length); return IntPtr.Zero;                     // →
                        case 0x0D:                                                 // Enter
                            if (_selRow >= 0 && _selRow < _rows.Count && _rows[_selRow].Path != null)
                                Program.OpenInExplorer(_rows[_selRow].Path!);
                            return IntPtr.Zero;
                        case 0x74: Monitor.RunScan(true); return IntPtr.Zero;       // F5
                        default: return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
                    }
                    UpdateThumb();
                    Win32.InvalidateRect(hwnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                }
            case Win32.WM_APP_SCAN_DONE:
                OnDataChanged();
                return IntPtr.Zero;
            case Win32.WM_APP_FOLDER_DONE:
                RebuildRows();
                Win32.InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            case Win32.WM_APP_TRAY:
                Tray.HandleTrayMessage(lParam);
                return IntPtr.Zero;
            case Win32.WM_APP_TICK:
                OnTick();
                return IntPtr.Zero;
            case Win32.WM_CLOSE:
                // 最小化到托盘：静默隐藏，不弹气泡打扰用户
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                return IntPtr.Zero;
            case Win32.WM_DESTROY:
                FreeBackBuffer();
                DeleteFonts();
                Theme.Release();
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
