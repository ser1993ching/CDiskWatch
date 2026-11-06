using System.Runtime.InteropServices;
using System.Text;

namespace CDiskWatch;

/// <summary>托盘图标、气泡通知与右键菜单。</summary>
internal static class Tray
{
    const uint TrayUid = 0x57;
    static IntPtr _hwnd;
    static bool _added;
    static IntPtr _icon;
    static string _iconState = "";

    public static void Init(IntPtr hwnd)
    {
        _hwnd = hwnd;
        var nid = MakeNid("C 盘容量监控");
        _added = Win32.Shell_NotifyIconW(Win32.NIM_ADD, ref nid);
    }

    static Win32.NOTIFYICONDATAW MakeNid(string tip)
    {
        var nid = new Win32.NOTIFYICONDATAW
        {
            hWnd = _hwnd,
            uID = TrayUid,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
            uCallbackMessage = Win32.WM_APP_TRAY,
            hIcon = _icon,
            szTip = tip,
        };
        nid.cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATAW>();
        return nid;
    }

    /// <summary>按占用率切换托盘图标（绿/黄/橙/红），文件缺失时回退到模块图标。</summary>
    static void EnsureIcon(string state)
    {
        if (_icon != IntPtr.Zero && _iconState == state) return;
        if (_icon != IntPtr.Zero) { Win32.DestroyIcon(_icon); _icon = IntPtr.Zero; }
        _iconState = state;
        string file = Path.Combine(AppIcon.AppDir, "icon-" + state + ".ico");
        IntPtr h = AppIcon.LoadFromFile(file, 16);
        if (h == IntPtr.Zero) h = AppIcon.LoadFromModule();
        if (h == IntPtr.Zero)
        {
            try
            {
                var fi = new Win32.SHFILEINFOW();
                IntPtr r = Win32.SHGetFileInfoW(AppIcon.ExePath, 0, ref fi, (uint)Marshal.SizeOf<Win32.SHFILEINFOW>(),
                    Win32.SHGFI_ICON | Win32.SHGFI_SMALLICON);
                if (r != IntPtr.Zero) h = fi.hIcon;
            }
            catch { }
        }
        _icon = h;
    }

    public static string StateOf(double ratio) =>
        ratio >= 0.93 ? "crit" : ratio >= 0.85 ? "high" : ratio >= 0.70 ? "warn" : "ok";

    /// <summary>刷新托盘提示文本与图标。</summary>
    public static void UpdateTip(double usedRatio, long free, long total)
    {
        if (!_added) return;
        EnsureIcon(StateOf(usedRatio));
        string tip = total > 0
            ? $"C: 可用 {Fmt.Bytes(free)} · {Fmt.Pct((double)free / total)}"
            : "C 盘容量监控";
        if (tip.Length > 120) tip = tip[..120];   // szTip 上限 128，超出会静默失败
        var n = MakeNid(tip);
        Win32.Shell_NotifyIconW(Win32.NIM_MODIFY, ref n);
    }

    /// <summary>从 Monitor 当前状态刷新托盘（只能在 UI 线程调用）。</summary>
    public static void SyncTip()
    {
        long free = Monitor.FreeBytes, total = Monitor.TotalBytes;
        UpdateTip(total > 0 ? 1.0 - (double)free / total : 0, free, total);
    }

    public static void Notify(string title, string text)
    {
        if (!_added) return;
        var n = MakeNid("C 盘容量监控");
        n.uFlags |= Win32.NIF_INFO;
        n.szInfo = text;
        n.szInfoTitle = title;
        n.dwInfoFlags = Win32.NIIF_INFO;
        Win32.Shell_NotifyIconW(Win32.NIM_MODIFY, ref n);
    }

    public static void Destroy()
    {
        if (!_added) return;
        var n = new Win32.NOTIFYICONDATAW { hWnd = _hwnd, uID = TrayUid };
        n.cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATAW>();
        Win32.Shell_NotifyIconW(Win32.NIM_DELETE, ref n);
        _added = false;
        if (_icon != IntPtr.Zero) { Win32.DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }

    public static void HandleTrayMessage(IntPtr lParam)
    {
        uint m = (uint)lParam.ToInt64() & 0xFFFF;
        switch (m)
        {
            case 0x0202: // WM_LBUTTONUP
            case 0x0203: // WM_LBUTTONDBLCLK
                MainWindow.Instance?.ShowWin();
                break;
            case 0x0205: // WM_RBUTTONUP
                ShowMenu();
                break;
        }
    }

    static void ShowMenu()
    {
        var menu = Win32.CreatePopupMenu();
        Win32.AppendMenuW(menu, Win32.MF_STRING, 100, "打开监控面板");
        Win32.AppendMenuW(menu, Win32.MF_STRING, 101, "立即扫描");
        Win32.AppendMenuW(menu, Win32.MF_STRING, 102, "复制今日报告");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
        if (!SystemUsage.IsAdmin())
            Win32.AppendMenuW(menu, Win32.MF_STRING, 103, "以管理员身份重新运行");
        Win32.AppendMenuW(menu, Win32.MF_STRING | (Monitor.Cfg.AutoStart ? Win32.MF_CHECKED : 0u), 104, "开机自启");
        Win32.AppendMenuW(menu, Win32.MF_STRING, 105, "打开数据目录");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING, 106, "退出");

        Win32.GetCursorPos(out var pt);
        Win32.SetForegroundWindow(_hwnd);
        int cmd = (int)Win32.TrackPopupMenu(menu,
            Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_NONOTIFY | Win32.TPM_LEFTALIGN,
            pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        Win32.PostMessageW(_hwnd, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        Win32.DestroyMenu(menu);

        switch (cmd)
        {
            case 100: MainWindow.Instance?.ShowWin(); break;
            case 101: Monitor.RunScan(true); break;
            case 102: Program.CopyReport(); break;
            case 103: Program.RelaunchAsAdmin(); break;
            case 104: Program.ToggleAutoStart(); break;
            case 105: Program.OpenDataDir(); break;
            case 106: Win32.DestroyWindow(_hwnd); break;
        }
    }
}
