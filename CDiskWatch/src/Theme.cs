using System.Runtime.InteropServices;

namespace CDiskWatch;

/// <summary>
/// 统一视觉：**浅色主题**配色 + GDI 绘制工具。
/// 要点：
///  1) 文字一律 SetBkMode(TRANSPARENT)，否则 DrawText 会用白色背景块刷掉卡片底色；
///  2) 画笔/画刷按颜色缓存在字典里复用，避免每次重绘创建上百个 GDI 对象；
///  3) 所有尺寸都经由调用方按 DPI 缩放后传入（本类不做缩放）。
/// </summary>
internal static class Theme
{
    // ===== 画布 / 卡片 =====
    public static readonly uint Canvas = Win32.Rgb(0xF2, 0xF5, 0xF9);
    public static readonly uint Surface = Win32.Rgb(0xFF, 0xFF, 0xFF);
    public static readonly uint SurfaceAlt = Win32.Rgb(0xF7, 0xF9, 0xFC);
    public static readonly uint SurfaceBorder = Win32.Rgb(0xDE, 0xE4, 0xEE);
    public static readonly uint Divider = Win32.Rgb(0xE9, 0xEE, 0xF5);
    public static readonly uint Zebra = Win32.Rgb(0xED, 0xF1, 0xF7);
    public static readonly uint Hover = Win32.Rgb(0xEE, 0xF3, 0xFB);
    public static readonly uint Sel = Win32.Rgb(0xE4, 0xEE, 0xFF);

    // ===== 文字 =====
    public static readonly uint TextMain = Win32.Rgb(0x1B, 0x25, 0x35);
    public static readonly uint TextSub = Win32.Rgb(0x46, 0x53, 0x67);
    public static readonly uint TextDim = Win32.Rgb(0x60, 0x6C, 0x7F);
    public static readonly uint TextOnAccent = Win32.Rgb(0xFF, 0xFF, 0xFF);

    // ===== 语义色 =====
    public static readonly uint Accent = Win32.Rgb(0x2B, 0x62, 0xE8);
    public static readonly uint AccentHover = Win32.Rgb(0x1E, 0x51, 0xD0);
    public static readonly uint AccentText = Win32.Rgb(0x1D, 0x4E, 0xD8);
    public static readonly uint AccentSoft = Win32.Rgb(0xE4, 0xEE, 0xFF);
    public static readonly uint Good = Win32.Rgb(0x15, 0x9A, 0x5B);
    public static readonly uint GoodSoft = Win32.Rgb(0xE4, 0xF6, 0xEC);
    public static readonly uint Warn = Win32.Rgb(0xC2, 0x77, 0x03);
    public static readonly uint WarnSoft = Win32.Rgb(0xFD, 0xF3, 0xDC);
    public static readonly uint Bad = Win32.Rgb(0xD3, 0x2D, 0x2D);
    public static readonly uint BadSoft = Win32.Rgb(0xFD, 0xE8, 0xE8);
    public static readonly uint BarBg = Win32.Rgb(0xDC, 0xE3, 0xEF);
    public static readonly uint ScrollTrack = Win32.Rgb(0xDF, 0xE5, 0xEE);
    public static readonly uint ScrollThumb = Win32.Rgb(0x8C, 0x9A, 0xB1);

    // ===== 文本格式 =====
    public const uint AlignLeft = Win32.DT_SINGLELINE | Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX;
    public const uint AlignRight = Win32.DT_SINGLELINE | Win32.DT_RIGHT | Win32.DT_VCENTER | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX;
    public const uint AlignCenter = Win32.DT_SINGLELINE | Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX;
    /// <summary>路径列专用：省略中间段、保留文件名所在尾部（比 END_ELLIPSIS 更有信息量）。</summary>
    public const uint AlignLeftPath = Win32.DT_SINGLELINE | Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_PATH_ELLIPSIS | Win32.DT_NOPREFIX;

    /// <summary>
    /// 创建 UI 字体。
    /// 两个关键点：
    ///  1) 高度用**负值**（= 字符高度/em），正值表示"单元格高度（含内部行距）"，会让字明显偏小；
    ///  2) 字符集必须给 DEFAULT_CHARSET，传 0（ANSI_CHARSET）时 GDI 会用拉丁字体度量 + 字链接的 CJK 字形，
    ///     字宽小于字形墨迹宽度，相邻汉字会重叠。
    /// </summary>
    public static IntPtr CreateUiFont(int emPx, int weight, string face = "Microsoft YaHei UI") =>
        Win32.CreateFontW(-Math.Max(1, emPx), 0, 0, 0, weight, 0, 0, 0,
            Win32.DEFAULT_CHARSET, 0, 0, Win32.CLEARTYPE_QUALITY, 0, face);

    // ===== GDI 对象缓存 =====
    static readonly Dictionary<uint, IntPtr> _brushes = new();
    static readonly Dictionary<(uint color, int width), IntPtr> _pens = new();

    static IntPtr Brush(uint color)
    {
        if (_brushes.TryGetValue(color, out var h) && h != IntPtr.Zero) return h;
        h = Win32.CreateSolidBrush(color);
        _brushes[color] = h;
        return h;
    }

    static IntPtr Pen(uint color, int width = 1)
    {
        var key = (color, width);
        if (_pens.TryGetValue(key, out var h) && h != IntPtr.Zero) return h;
        h = Win32.CreatePen(Win32.PS_SOLID, width, color);
        _pens[key] = h;
        return h;
    }

    /// <summary>释放所有缓存的 GDI 对象（窗口销毁时调用）。</summary>
    public static void Release()
    {
        foreach (var h in _brushes.Values) if (h != IntPtr.Zero) Win32.DeleteObject(h);
        foreach (var h in _pens.Values) if (h != IntPtr.Zero) Win32.DeleteObject(h);
        _brushes.Clear();
        _pens.Clear();
    }

    // ===== 基础绘制 =====

    public static void Fill(IntPtr hdc, in Win32.RECT rc, uint color)
    {
        if (rc.R <= rc.L || rc.B <= rc.T) return;
        var r = rc;
        Win32.FillRect(hdc, ref r, Brush(color));
    }

    /// <summary>圆角实心填充。</summary>
    public static void FillRound(IntPtr hdc, in Win32.RECT rc, int radius, uint color)
    {
        if (rc.R <= rc.L || rc.B <= rc.T) return;
        int d = Math.Max(1, radius) * 2;
        IntPtr oldBr = Win32.SelectObject(hdc, Brush(color));
        IntPtr oldPen = Win32.SelectObject(hdc, Win32.GetStockObject(Win32.NULL_PEN));
        Win32.RoundRect(hdc, rc.L, rc.T, rc.R, rc.B, d, d);
        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBr);
    }

    /// <summary>圆角填充 + 1px 边框（卡片）。</summary>
    public static void Card(IntPtr hdc, in Win32.RECT rc, int radius, uint fill, uint border)
    {
        if (rc.R <= rc.L || rc.B <= rc.T) return;
        int d = Math.Max(1, radius) * 2;
        IntPtr oldBr = Win32.SelectObject(hdc, Brush(fill));
        IntPtr oldPen = Win32.SelectObject(hdc, Pen(border));
        Win32.RoundRect(hdc, rc.L, rc.T, rc.R, rc.B, d, d);
        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBr);
    }

    /// <summary>圆角描边（无填充）。</summary>
    public static void CardBorder(IntPtr hdc, in Win32.RECT rc, int radius, uint border, int width = 1)
    {
        if (rc.R <= rc.L || rc.B <= rc.T) return;
        int d = Math.Max(1, radius) * 2;
        IntPtr oldBr = Win32.SelectObject(hdc, Win32.GetStockObject(Win32.NULL_BRUSH));
        IntPtr oldPen = Win32.SelectObject(hdc, Pen(border, width));
        Win32.RoundRect(hdc, rc.L, rc.T, rc.R, rc.B, d, d);
        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBr);
    }

    public static void HLine(IntPtr hdc, int x1, int x2, int y, uint color)
    {
        if (x2 <= x1) return;
        var rc = new Win32.RECT { L = x1, T = y, R = x2, B = y + 1 };
        Fill(hdc, rc, color);
    }

    public static void VLine(IntPtr hdc, int x, int y1, int y2, uint color)
    {
        if (y2 <= y1) return;
        var rc = new Win32.RECT { L = x, T = y1, R = x + 1, B = y2 };
        Fill(hdc, rc, color);
    }

    /// <summary>水平进度条（圆角轨道 + 圆角填充）。</summary>
    public static void Bar(IntPtr hdc, int x, int y, int w, int h, double ratio, uint color, uint? track = null)
    {
        if (w <= 0 || h <= 0) return;
        var bg = new Win32.RECT { L = x, T = y, R = x + w, B = y + h };
        FillRound(hdc, bg, h / 2, track ?? BarBg);

        int fw = (int)Math.Round(w * Math.Clamp(ratio, 0, 1));
        if (fw <= 0) return;
        if (fw < h) fw = h;
        var fg = new Win32.RECT { L = x, T = y, R = x + fw, B = y + h };
        FillRound(hdc, fg, h / 2, color);
    }

    /// <summary>先画底色再画填充的分段条（用于类别分布）。</summary>
    public static void StackedBar(IntPtr hdc, int x, int y, int w, int h, IReadOnlyList<(double ratio, uint color)> parts)
    {
        if (w <= 0 || h <= 0) return;
        var bg = new Win32.RECT { L = x, T = y, R = x + w, B = y + h };
        FillRound(hdc, bg, h / 2, BarBg);
        int cx = x;
        foreach (var (ratio, color) in parts)
        {
            int seg = (int)Math.Round(w * Math.Clamp(ratio, 0, 1));
            if (seg <= 0) continue;
            if (cx + seg > x + w) seg = x + w - cx;
            if (seg <= 0) break;
            var rc = new Win32.RECT { L = cx, T = y, R = cx + seg, B = y + h };
            Fill(hdc, rc, color);
            cx += seg;
        }
    }

    // ===== 文本 =====

    public static int Measure(IntPtr hdc, IntPtr font, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        IntPtr old = Win32.SelectObject(hdc, font);
        Win32.SetBkMode(hdc, Win32.TRANSPARENT);
        var r = new Win32.RECT { L = 0, T = 0, R = 0, B = 0 };
        Win32.DrawTextW(hdc, text, -1, ref r, Win32.DT_SINGLELINE | Win32.DT_CALCRECT | Win32.DT_NOPREFIX);
        Win32.SelectObject(hdc, old);
        return r.R - r.L;
    }

    public static void Text(IntPtr hdc, IntPtr font, string text, in Win32.RECT rc, uint color, uint format)
    {
        if (string.IsNullOrEmpty(text) || rc.R <= rc.L) return;
        IntPtr old = Win32.SelectObject(hdc, font);
        Win32.SetBkMode(hdc, Win32.TRANSPARENT);   // 关键：否则文字背后会被刷成白色矩形
        Win32.SetTextColor(hdc, unchecked((int)color));
        var r = rc;
        Win32.DrawTextW(hdc, text, -1, ref r, format);
        Win32.SelectObject(hdc, old);
    }

    /// <summary>多行文本（自动换行，用于长段落）。</summary>
    public static void TextWrap(IntPtr hdc, IntPtr font, string text, in Win32.RECT rc, uint color)
    {
        if (string.IsNullOrEmpty(text) || rc.R <= rc.L) return;
        IntPtr old = Win32.SelectObject(hdc, font);
        Win32.SetBkMode(hdc, Win32.TRANSPARENT);
        Win32.SetTextColor(hdc, unchecked((int)color));
        var r = rc;
        Win32.DrawTextW(hdc, text, -1, ref r,
            Win32.DT_LEFT | Win32.DT_TOP | Win32.DT_WORDBREAK | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX);
        Win32.SelectObject(hdc, old);
    }

    /// <summary>在矩形内居中的徽标（圆角胶囊）。</summary>
    public static void Pill(IntPtr hdc, int x, int y, int h, string text, IntPtr font, uint fg, uint bg, int padX)
    {
        int w = Measure(hdc, font, text) + padX * 2;
        var rc = new Win32.RECT { L = x, T = y, R = x + w, B = y + h };
        FillRound(hdc, rc, h / 2, bg);
        Text(hdc, font, text, rc, fg, AlignCenter);
    }

    public static int PillWidth(IntPtr hdc, IntPtr font, string text, int padX) => Measure(hdc, font, text) + padX * 2;

    // ===== 图形 =====

    public static void Polygon(IntPtr hdc, Win32.POINT[] pts, uint fill)
    {
        if (pts.Length < 3) return;
        IntPtr oldBr = Win32.SelectObject(hdc, Brush(fill));
        IntPtr oldPen = Win32.SelectObject(hdc, Win32.GetStockObject(Win32.NULL_PEN));
        Win32.Polygon(hdc, pts, pts.Length);
        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBr);
    }

    public static void Polyline(IntPtr hdc, Win32.POINT[] pts, uint color, int width = 2)
    {
        if (pts.Length < 2) return;
        IntPtr oldPen = Win32.SelectObject(hdc, Pen(color, Math.Max(1, width)));
        Win32.Polyline(hdc, pts, pts.Length);
        Win32.SelectObject(hdc, oldPen);
    }

    public static void Dot(IntPtr hdc, int cx, int cy, int r, uint color)
    {
        if (r <= 0) return;
        IntPtr oldBr = Win32.SelectObject(hdc, Brush(color));
        IntPtr oldPen = Win32.SelectObject(hdc, Win32.GetStockObject(Win32.NULL_PEN));
        Win32.Ellipse(hdc, cx - r, cy - r, cx + r, cy + r);
        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBr);
    }

    // ===== 窗口外观 =====

    /// <summary>Win11 圆角 + 浅色标题栏（不支持的版本静默忽略）。</summary>
    public static void ApplyModernFrame(IntPtr hwnd)
    {
        try
        {
            int pref = Win32.DWMWCP_ROUND;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { }
        try
        {
            int light = 0;   // DWMWA_USE_IMMERSIVE_DARK_MODE = 0 → 浅色标题栏
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref light, sizeof(int));
        }
        catch { }
    }
}
