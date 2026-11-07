using System.Runtime.InteropServices;
using System.Text;

namespace CDiskWatch;

/// <summary>入口：单实例、消息循环、CLI 子命令、托盘与面板装配。</summary>
public static class Program
{
    const string MutexName = "Local\\CDiskWatch_SingleInstance";
    static Mutex? _mutex;
    static AppConfig _cfg = new();
    static string[] _args = Array.Empty<string>();
    static bool Baseline => _args.Contains("--baseline");

    public static int Main(string[] args)
    {
        _args = args;
        InitLanguage(args);

        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Contains("--scan")) return CliScan();
        if (args.Contains("--report")) return CliReport();
        if (args.Contains("--winsxs")) return CliWinSxS();

        return RunGui(args);
    }

    /// <summary>语言优先级：--lang 参数 &gt; 环境变量 CDISKWATCH_LANG &gt; 配置 Lang（auto = 跟随系统）。</summary>
    static void InitLanguage(string[] args)
    {
        string? code = ArgValue(args, "--lang");
        if (string.IsNullOrWhiteSpace(code)) code = Environment.GetEnvironmentVariable("CDISKWATCH_LANG");
        if (string.IsNullOrWhiteSpace(code))
        {
            try { code = ConfigStore.Load().Lang; } catch { code = null; }
        }
        Loc.Load(code);
    }

    /// <summary>取形如 --lang en-US 或 --lang=en-US 的参数值。</summary>
    static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return args[i].Substring(name.Length + 1);
        }
        return null;
    }

    /// <summary>--format json（默认 text）。</summary>
    static bool JsonFormat => string.Equals(ArgValue(_args, "--format") ?? "", "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 提权重启。清单里用 requireAdministrator 会让部分系统的 SxS 激活失败，
    /// 因此改为运行时用 ShellExecuteEx("runas") 自提权（已提权则不再重复）。
    /// </summary>
    static bool TryElevate(string extraArgs, out IntPtr hProcess)
    {
        hProcess = IntPtr.Zero;
        try
        {
            string? p = Environment.ProcessPath;
            string file = AppIcon.ExePath;
            string parms = extraArgs;
            // 开发态（dotnet CDiskWatch.dll）需要带上 dll 路径；单文件发布时 Location 为空
            string dll = Path.Combine(AppContext.BaseDirectory, "CDiskWatch.dll");
            if (p != null && p.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(dll))
            {
                file = p;
                parms = "\"" + dll + "\" " + extraArgs;
            }
            var info = new Win32.SHELLEXECUTEINFOW
            {
                cbSize = Marshal.SizeOf<Win32.SHELLEXECUTEINFOW>(),
                fMask = Win32.SEE_MASK_NOCLOSEPROCESS,
                lpVerb = "runas",
                lpFile = file,
                lpParameters = string.IsNullOrEmpty(parms) ? null : parms,
                nShow = 1,
            };
            if (!Win32.ShellExecuteExW(ref info)) return false;
            hProcess = info.hProcess;
            return true;
        }
        catch { return false; }
    }

    static int WaitElevated(IntPtr hProcess)
    {
        try
        {
            if (hProcess == IntPtr.Zero) return 1;
            Win32.WaitForSingleObject(hProcess, 30 * 60 * 1000);
            Win32.CloseHandle(hProcess);
        }
        catch { }
        return 0;
    }

    // ================= GUI =================

    static int RunGui(string[] args)
    {
        // 已有实例在跑：直接把它的面板叫到前台，然后退出。
        // （放在提权之前，避免用户再次双击 exe 时先弹一次 UAC）
        IntPtr existing = Win32.FindWindowW(MainWindow.ClassName, null);
        if (existing != IntPtr.Zero)
        {
            if (args.Contains("--preview")) Win32.ShowWindow(existing, Win32.SW_SHOW);
            Win32.SetForegroundWindow(existing);
            return 0;
        }

        // 需要管理员：卷影副本/还原点占用、系统文件归因。非管理员时先自提权。
        if (!SystemUsage.IsAdmin() && !args.Contains("--no-elevate"))
        {
            // 提权时把原参数透传过去（如 --preview）
            string pass = string.Join(" ", args.Where(a => a != "--no-elevate")).Trim();
            if (TryElevate(pass, out _)) return 0;   // 已以管理员身份重新启动，本进程退出
            Tray.Notify(Loc.T("app.title"), Loc.T("msg.noAdmin"));
        }

        _cfg = ConfigStore.Load();
        Monitor.Cfg = _cfg;

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew) return 0;
        _mutex = mutex;

        Dpi.EnableAwareness();
        Monitor.Init();

        var win = MainWindow.Create();
        Tray.Init(win.Hwnd);

        // 后台线程只负责把「状态已变化」丢回 UI 线程；托盘提示与重绘都在 UI 线程完成。
        Monitor.OnStateChanged += () =>
            Win32.PostMessageW(win.Hwnd, Win32.WM_APP_TICK, IntPtr.Zero, IntPtr.Zero);
        Monitor.OnDataChanged += () =>
            Win32.PostMessageW(win.Hwnd, Win32.WM_APP_SCAN_DONE, IntPtr.Zero, IntPtr.Zero);
        Monitor.OnNotify += (title, detail) => Tray.Notify(title, detail);

        // 首次运行：托盘提示 + 立即扫描一次
        if (_cfg.ScanOnStartup)
        {
            Monitor.RunScan(false);
        }
        else
        {
            long free = Monitor.FreeBytes, total = Monitor.TotalBytes;
            Tray.UpdateTip(total > 0 ? 1.0 - (double)free / total : 0, free, total);
        }

        // 首次运行（还没有任何快照）时直接显示面板，避免用户只看到托盘图标不知道发生什么
        bool firstRun = Monitor.Latest == null;
        if (!_cfg.StartMinimized || firstRun || args.Contains("--preview")) win.ShowWin();

        int rc = RunLoop();
        ConfigStore.Save(_cfg);
        Tray.Destroy();
        Monitor.Dispose();
        return rc;
    }

    static int RunLoop()
    {
        while (Win32.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
        return 0;
    }

    // ================= CLI =================

    static int CliScan()
    {
        if (!SystemUsage.IsAdmin() && !_args.Contains("--no-elevate"))
        {
            if (TryElevate("--scan", out var hp)) return WaitElevated(hp);
            NativeOut.WriteLine(Loc.T("cli.elevateFailed"));
        }
        _cfg = ConfigStore.Load();
        Monitor.Cfg = _cfg;
        NativeOut.WriteLine(Loc.F("cli.scanning", _cfg.Drive));
        var opt = new ScanOptions
        {
            Drive = _cfg.Drive,
            MaxDirLevel = Math.Clamp(_cfg.MaxDirLevel, 1, 6),
            TopFileCount = Math.Clamp(_cfg.TopFileCount, 10, 1000),
            ExcludePrefixes = new List<string> { Paths.Dir },
        };
        foreach (var e in _cfg.ExcludePaths) if (!string.IsNullOrWhiteSpace(e)) opt.ExcludePrefixes.Add(e.TrimEnd('\\'));

        var res = Scanner.Scan(opt, CancellationToken.None, m => NativeOut.WriteLine(m));
        var shadow = SystemUsage.QueryShadow();
        var snap = Snapshot.FromScan(res, shadow.UsedBytes);
        snap.Advice = AdviceEngine.Measure(snap);
        string path = Store.Save(snap);
        Monitor.ReleaseMemory();

        NativeOut.WriteLine(Loc.F("cli.scanDone", Fmt.Seconds(res.DurationMs / 1000.0), path));
        var gi = GC.GetGCMemoryInfo();
        NativeOut.WriteLine(Loc.F("cli.memInfo", GC.GetTotalMemory(false) / 1024 / 1024, gi.TotalCommittedBytes / 1024 / 1024,
            Environment.WorkingSet / 1024 / 1024, System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024));
        NativeOut.WriteLine(JsonFormat
            ? Report.BuildJson(snap, null, snap.Advice, new List<AlertRecord>(), false)
            : Report.Build(snap, null, snap.Advice, new List<AlertRecord>(), false));
        return 0;
    }

    static int CliReport()
    {
        _cfg = ConfigStore.Load();
        Monitor.Cfg = _cfg;
        bool baseline = Baseline;
        var latest = Store.Latest();
        if (latest == null)
        {
            NativeOut.WriteLine(Loc.T("cli.noSnapshot"));
            return 1;
        }
        DiffResult? diff = baseline ? Monitor.DiffWithBaseline() : (Store.Previous() != null ? DiffEngine.Compare(Store.Previous()!, latest) : null);
        var advice = latest.Advice.Count > 0 ? latest.Advice : AdviceEngine.Evaluate(latest);
        var alerts = AlertEngine.Evaluate(latest, Store.Previous(), _cfg.ToAlertSettings(), useCooldown: false);
        NativeOut.WriteLine(JsonFormat
            ? Report.BuildJson(latest, diff, advice, alerts, baseline)
            : Report.Build(latest, diff, advice, alerts, baseline));
        return 0;
    }

    static int CliWinSxS()
    {
        NativeOut.WriteLine(SystemUsage.AnalyzeWinSxS());
        return 0;
    }

    // ================= 辅助 =================

    public static void CopyReport()
    {
        var latest = Store.Latest();
        if (latest == null) { Tray.Notify(Loc.T("app.title"), Loc.T("cli.noSnapshot")); return; }
        var diff = Store.Previous() != null ? DiffEngine.Compare(Store.Previous()!, latest) : null;
        string text = Report.Build(latest, diff, AdviceEngine.Evaluate(latest), new List<AlertRecord>(), false);
        if (SetClipboardText(text))
            Tray.Notify(Loc.T("app.title"), Loc.T("msg.reportCopied"));
        else
            Tray.Notify(Loc.T("app.title"), Loc.T("err.copyFailed"));
    }

    static bool SetClipboardText(string text)
    {
        try
        {
            if (!Win32.OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                Win32.EmptyClipboard();
                int bytes = (text.Length + 1) * 2;
                IntPtr h = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, bytes);
                if (h == IntPtr.Zero) return false;
                IntPtr p = Win32.GlobalLock(h);
                if (p == IntPtr.Zero) { Win32.GlobalFree(h); return false; }
                Marshal.Copy(Encoding.Unicode.GetBytes(text + "\0"), 0, p, bytes);
                Win32.GlobalUnlock(h);
                Win32.SetClipboardData(Win32.CF_UNICODETEXT, h);
                return true;
            }
            finally { Win32.CloseClipboard(); }
        }
        catch { return false; }
    }

    public static void RelaunchAsAdmin()
    {
        try
        {
            var info = new Win32.SHELLEXECUTEINFOW
            {
                cbSize = Marshal.SizeOf<Win32.SHELLEXECUTEINFOW>(),
                fMask = Win32.SEE_MASK_NOCLOSEPROCESS,
                lpVerb = "runas",
                lpFile = AppIcon.ExePath,
                nShow = 1,
            };
            Win32.ShellExecuteExW(ref info);
        }
        catch { }
    }

    public static void ToggleAutoStart()
    {
        _cfg.AutoStart = !_cfg.AutoStart;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (k == null)
            {
                _cfg.AutoStart = !_cfg.AutoStart;   // 回滚
                Tray.Notify(Loc.T("app.title"), Loc.T("msg.autoStartFailed"));
                return;
            }
            if (_cfg.AutoStart)
            {
                string exe = AppIcon.ExePath;
                if (!string.IsNullOrEmpty(exe)) k.SetValue("CDiskWatch", "\"" + exe + "\"");
            }
            else
            {
                k.DeleteValue("CDiskWatch", throwOnMissingValue: false);
            }
        }
        catch { }
        ConfigStore.Save(_cfg);
        Tray.Notify(Loc.T("app.title"), Loc.T(_cfg.AutoStart ? "msg.autoStartOn" : "msg.autoStartOff"));
    }

    public static void OpenDataDir()
    {
        try
        {
            var info = new Win32.SHELLEXECUTEINFOW
            {
                cbSize = Marshal.SizeOf<Win32.SHELLEXECUTEINFOW>(),
                lpVerb = "open",
                lpFile = "explorer.exe",
                lpParameters = "\"" + Paths.Dir + "\"",
                nShow = 1,
            };
            Win32.ShellExecuteExW(ref info);
        }
        catch { }
    }

    /// <summary>在资源管理器中定位（只打开，不会修改任何文件）。</summary>
    public static void OpenInExplorer(string path)
    {
        try
        {
            bool isFile = File.Exists(path);
            var info = new Win32.SHELLEXECUTEINFOW
            {
                cbSize = Marshal.SizeOf<Win32.SHELLEXECUTEINFOW>(),
                lpVerb = "open",
                lpFile = "explorer.exe",
                lpParameters = isFile ? "/select,\"" + path + "\"" : "\"" + path + "\"",
                nShow = 1,
            };
            Win32.ShellExecuteExW(ref info);
        }
        catch { }
    }
}
