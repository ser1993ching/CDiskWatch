using System.Runtime.InteropServices;
using System.Text;

namespace CDiskWatch;

/// <summary>全部 Win32 互操作：声明、结构体、常量与小工具函数。</summary>
internal static class Win32
{
    // ===== 消息 =====
    public const int WM_NULL = 0x0000;
    public const int WM_CREATE = 0x0001;
    public const int WM_DESTROY = 0x0002;
    public const int WM_CLOSE = 0x0010;
    public const int WM_SIZE = 0x0005;
    public const int WM_ACTIVATE = 0x0006;
    public const int WM_SETICON = 0x0080;
    public const int WM_ERASEBKGND = 0x0014;
    public const int WM_PAINT = 0x000F;
    public const int WM_SETFONT = 0x0030;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_COMMAND = 0x0111;
    public const int WM_TIMER = 0x0113;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_CHAR = 0x0102;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSELEAVE = 0x02A3;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_APP = 0x8000;
    public const int WM_APP_SCAN_DONE = WM_APP + 1;
    public const int WM_APP_TRAY = WM_APP + 2;
    public const int WM_APP_TICK = WM_APP + 3;

    // ===== 窗口样式 =====
    /// <summary>窗口类样式：没有 CS_DBLCLKS 就永远收不到 WM_LBUTTONDBLCLK。</summary>
    public const uint CS_DBLCLKS = 0x0008;
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_VISIBLE = 0x10000000u;
    public const uint WS_CHILD = 0x40000000u;
    public const uint WS_CLIPCHILDREN = 0x02000000u;
    public const uint WS_EX_APPWINDOW = 0x00040000u;
    public const uint WS_EX_TOOLWINDOW = 0x00000080u;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    public const int SWP_NOSIZE = 0x0001;
    public const int SWP_NOMOVE = 0x0002;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_FRAMECHANGED = 0x0020;
    public const int SWP_SHOWWINDOW = 0x0040;

    // ===== 绘制 =====
    public const int SRCCOPY = 0x00CC0020;
    public const uint PS_SOLID = 0;
    public const uint TRANSPARENT = 1;
    public const uint OPAQUE = 2;
    public const int DT_LEFT = 0x00000000;
    public const int DT_RIGHT = 0x00000002;
    public const int DT_CENTER = 0x00000001;
    public const int DT_VCENTER = 0x00000004;
    public const int DT_SINGLELINE = 0x00000020;
    public const int DT_NOPREFIX = 0x00000800;
    public const int DT_PATH_ELLIPSIS = 0x00004000;
    public const int DT_END_ELLIPSIS = 0x00008000;
    public const int DT_NOCLIP = 0x00000100;
    public const int DT_CALCRECT = 0x00000400;
    public const int DT_TOP = 0x00000000;
    public const int DT_WORDBREAK = 0x00000010;
    public const uint CLEARTYPE_QUALITY = 5;
    public const uint DEFAULT_CHARSET = 1;
    public const int NULL_BRUSH = 5;
    public const int NULL_PEN = 8;
    public const uint TA_LEFT = 0;
    public const uint TA_CENTER = 6;
    public const uint TA_RIGHT = 2;

    public const int WM_MENU_FILESIZE = 0x0001;
    public const int LF_FACESIZE = 32;

    // ===== 托盘 =====
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_INFO = 0x00000010;
    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_WARNING = 0x00000002;
    public const uint NIIF_ERROR = 0x00000003;
    public const uint NIIF_NONE = 0x00000000;

    // ===== 菜单 =====
    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_DISABLED = 0x00000002;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_LEFTALIGN = 0x0000;

    // ===== 通用 =====
    public const int IDC_ARROW = 32512;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int IDC_HAND = 32649;
    public const int IMAGE_ICON = 1;
    public const int LR_LOADFROMFILE = 0x0010;
    public const int LR_DEFAULTSIZE = 0x0040;
    public const uint DI_NORMAL = 0x0003;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;
    public const uint CLR_DEFAULT = 0xFF000000;
    public const uint CLR_NONE = 0xFFFFFFFF;

    public static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    // ===== 委托 =====
    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ===== 结构体 =====
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; public readonly int Width => R - L; public readonly int Height => B - T; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }
    public const uint TME_LEAVE = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression; public uint biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRIVERTEX
    {
        public ushort Red, Green, Blue, Alpha;
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GRADIENT_RECT
    {
        public uint UpperLeft;
        public uint LowerRight;
    }

    public const uint GRADIENT_FILL_RECT_V = 1;
    public const uint GRADIENT_FILL_RECT_H = 0;

    [DllImport("msimg32.dll")]
    public static extern bool GradientFill(IntPtr hdc, IntPtr pVertex, int dwVertexCount,
        IntPtr pMesh, int dwMeshCount, uint dwMode);

    // ===== API =====
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassW(ref WNDCLASSW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hwnd, IntPtr lpRect, bool erase);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowTextW(IntPtr hwnd, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder s, int max);

    [DllImport("user32.dll")]
    public static extern bool PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern uint SetTimer(IntPtr hwnd, uint id, uint ms, IntPtr callback);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hwnd, uint id);

    // GDI
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(uint style, int width, uint color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int i);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(IntPtr hdc, uint mode);

    [DllImport("gdi32.dll")]
    public static extern int SetTextColor(IntPtr hdc, int color);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextAlign(IntPtr hdc, uint align);

    [DllImport("user32.dll")]
    public static extern bool FillRect(IntPtr hdc, ref RECT rc, IntPtr brush);

    [DllImport("gdi32.dll")]
    public static extern bool Rectangle(IntPtr hdc, int l, int t, int r, int b);

    [DllImport("gdi32.dll")]
    public static extern bool RoundRect(IntPtr hdc, int l, int t, int r, int b, int w, int h);

    [DllImport("gdi32.dll")]
    public static extern bool Polyline(IntPtr hdc, POINT[] pts, int n);

    [DllImport("gdi32.dll")]
    public static extern bool Ellipse(IntPtr hdc, int l, int t, int r, int b);

    [DllImport("gdi32.dll")]
    public static extern bool Pie(IntPtr hdc, int l, int t, int r, int b, int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    public static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr pt);

    [DllImport("gdi32.dll")]
    public static extern bool LineTo(IntPtr hdc, int x, int y);

    [DllImport("gdi32.dll")]
    public static extern bool Polygon(IntPtr hdc, POINT[] pts, int n);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation,
        int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision,
        uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint dwFormat);

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int X, Y; }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetTextExtentPoint32W(IntPtr hdc, string s, int c, out SIZE size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TEXTMETRICW
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading;
        public int tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetTextMetricsW(IntPtr hdc, ref TEXTMETRICW tm);

    [DllImport("gdi32.dll")]
    public static extern int SaveDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int RestoreDC(IntPtr hdc, int nSavedDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);

    [DllImport("gdi32.dll")]
    public static extern bool FillRgn(IntPtr hdc, IntPtr hrgn, IntPtr hbr);

    [DllImport("gdi32.dll")]
    public static extern bool SelectClipRgn(IntPtr hdc, IntPtr hrgn);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteClipRgn(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int x, int y);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    // 用户交互
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpTrackMouseEvent);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern bool SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    public static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr lpRect);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadImageW(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", EntryPoint = "LoadImageW")]
    public static extern IntPtr LoadImageW_Res(IntPtr hinst, IntPtr lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstFileW(IntPtr lpFileName, IntPtr lpFindFileData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool FindNextFileW(IntPtr hFindFile, IntPtr lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindClose(IntPtr hFindFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetModuleFileNameW(IntPtr hModule, StringBuilder lpFilename, int nSize);

    [DllImport("kernel32.dll")]
    public static extern bool GetDiskFreeSpaceExW(string lpDirectoryName, out long lpFreeBytesAvailableToCaller,
        out long lpTotalNumberOfBytes, out long lpTotalNumberOfFreeBytes);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    // 定时器回调
    public delegate void TimerProc(IntPtr hwnd, uint msg, UIntPtr id, UIntPtr dwTime);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes,
        ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    public const uint SHGFI_ICON = 0x00000100;
    public const uint SHGFI_SMALLICON = 0x00000001;
    public const uint SHGFI_LARGEICON = 0x00000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public IntPtr lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    public const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;

    // 剪贴板
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalAlloc(uint uFlags, int dwBytes);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    /// <summary>系统界面语言（LANGID：低 10 位是主语言，0x04 中文 / 0x11 日文 / 0x09 英文）。</summary>
    [DllImport("kernel32.dll")]
    public static extern ushort GetUserDefaultUILanguage();

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    // GetProcAddress 的参数是 ANSI 字符串（该 API 无 A/W 之分）
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr LoadLibraryW(string lpLibFileName);

    /// <summary>校验某个 DLL 里是否存在指定导出函数（防止 P/Invoke 挂错 DLL）。</summary>
    public static bool HasEntryPoint(string dll, string func)
    {
        try
        {
            IntPtr h = LoadLibraryW(dll);
            if (h == IntPtr.Zero) return false;
            return GetProcAddress(h, func) != IntPtr.Zero;
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll")]
    public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nBytesToWrite,
        out uint lpBytesWritten, IntPtr lpOverlapped);
}
