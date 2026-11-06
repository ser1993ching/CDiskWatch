using System.Runtime.InteropServices;

namespace CDiskWatch;

/// <summary>DPI 感知与 DPI 查询（Win10 1703 以前的系统自动降级）。</summary>
internal static class Dpi
{
    static bool _aware;

    public static void EnableAwareness()
    {
        if (_aware) return;
        _aware = true;
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            return;
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
        catch (BadImageFormatException) { }
        try { SetProcessDPIAware(); } catch { }
    }

    public static uint ForWindow(IntPtr hwnd)
    {
        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi > 0) return dpi;
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
        catch (BadImageFormatException) { }
        return FromDeviceCaps(hwnd);
    }

    public static uint FromDeviceCaps(IntPtr hwnd)
    {
        IntPtr hdc = GetDC(hwnd);
        if (hdc == IntPtr.Zero) return 96;
        try
        {
            int x = Win32.GetDeviceCaps(hdc, LOGPIXELSX);
            int y = Win32.GetDeviceCaps(hdc, LOGPIXELSY);
            if (x <= 0) x = 96;
            if (y <= 0) y = 96;
            return (uint)((x + y) / 2);
        }
        finally { ReleaseDC(hwnd, hdc); }
    }

    const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(int value);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;
}
