using System.Runtime.InteropServices;

namespace CDiskWatch;

/// <summary>
/// 调度与共享状态：空间探针（秒级）、每日全盘扫描、快照仓库、派生数据（对比/告警/建议）。
/// 后台线程只做采集，UI 通过 OnStateChanged / OnDataChanged 事件被动刷新（事件里只做 PostMessage）。
/// </summary>
public static class Monitor
{
    public static AppConfig Cfg = new();

    public static Snapshot? Latest;
    public static Snapshot? Prev;
    public static DiffResult? Diff;
    public static List<AlertRecord> Alerts = new();
    public static List<AdviceCard> Advice = new();

    public static volatile bool Scanning;
    /// <summary>状态阶段（用于给状态栏圆点上色，避免用文案做字符串比较）。</summary>
    public enum StatusKind { Never = 0, Working = 1, Done = 2, Failed = 3 }

    public static volatile string Status = Loc.T("hero.neverScanned");
    public static volatile StatusKind StatusKindNow = StatusKind.Never;
    public static DateTimeOffset LastScanTime;
    public static double LastScanMs;
    public static long FreeBytes;
    public static long TotalBytes;
    public static DateTimeOffset NextScanTime;
    public static ShadowInfo Shadow = new();

    /// <summary>数据版本号：每次派生数据被重算时 +1，UI 据此判断是否需要重建列表。</summary>
    public static int DataVersion;

    /// <summary>状态变化回调（毫秒级，用于刷新空间显示）。</summary>
    public static event Action? OnStateChanged;
    /// <summary>快照/派生数据变化回调（用于重建列表）。</summary>
    public static event Action? OnDataChanged;
    /// <summary>需要弹气泡通知时调用。</summary>
    public static event Action<string, string>? OnNotify;

    static System.Threading.Timer? _timer;
    static readonly object _lock = new();
    static int _scanningFlag;
    /// <summary>已经自动扫描过的日期（每天最多自动扫一次）。</summary>
    static DateTime _lastAutoScanDate = DateTime.MinValue;
    static List<SnapHeader>? _historyCache;

    public static void Init()
    {
        Store.EnsureDir();
        ProbeFreeSpace();
        RefreshDerived(fireAlerts: false);
        // 今天已经有快照 → 视为"今天已自动扫描过"，避免每次启动都重扫一遍全盘
        if (Latest != null && !Scanning) _lastAutoScanDate = Latest.Time.ToLocalTime().Date;
        ScheduleNext();
        _timer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
    }

    public static void Dispose() => _timer?.Dispose();

    static void Tick()
    {
        try
        {
            ProbeFreeSpace();
            if (AutoScanDue()) RunScan(false);
            OnStateChanged?.Invoke();
        }
        catch { }
    }

    static bool TryParseDailyTime(out int hh, out int mm)
    {
        hh = 2; mm = 0;
        try
        {
            var parts = (Cfg.DailyScanTime ?? "").Split(':');
            if (parts.Length == 0) return false;
            if (!int.TryParse(parts[0], out hh)) return false;
            if (parts.Length > 1 && !int.TryParse(parts[1], out mm)) return false;
            return hh is >= 0 and <= 23 && mm is >= 0 and <= 59;
        }
        catch { return false; }
    }

    /// <summary>是否该自动扫描：到点 + 今天还没扫过 + 当前没在扫描。</summary>
    static bool AutoScanDue()
    {
        if (Scanning) return false;
        if (!TryParseDailyTime(out int hh, out int mm)) return false;
        var now = DateTime.Now;
        var target = now.Date.AddHours(hh).AddMinutes(mm);
        if (now < target) return false;
        return _lastAutoScanDate.Date < now.Date;
    }

    /// <summary>计算下一次计划扫描时间（严格晚于 now），供状态栏显示。</summary>
    static void ScheduleNext()
    {
        if (!TryParseDailyTime(out int hh, out int mm)) { NextScanTime = DateTimeOffset.MinValue; return; }
        var now = DateTime.Now;
        var target = now.Date.AddHours(hh).AddMinutes(mm);
        if (target <= now) target = target.AddDays(1);
        NextScanTime = new DateTimeOffset(target);
    }

    /// <summary>快速空间探针（不落盘、不扫描目录）。</summary>
    public static void ProbeFreeSpace()
    {
        try
        {
            if (Win32.GetDiskFreeSpaceExW(Cfg.Drive + ":\\", out long free, out long total, out _))
            {
                FreeBytes = free;
                TotalBytes = total;
            }
        }
        catch { }
    }

    /// <summary>执行一次全盘扫描并落盘快照。</summary>
    public static void RunScan(bool foreground)
    {
        if (Interlocked.CompareExchange(ref _scanningFlag, 1, 0) != 0) return;
        Scanning = true;
        Status = Loc.F("hero.scanningStatus", Cfg.Drive);
        StatusKindNow = StatusKind.Working;
        OnStateChanged?.Invoke();

        var t = new Thread(() =>
        {
            try
            {
                var opt = new ScanOptions
                {
                    Drive = Cfg.Drive,
                    MaxDirLevel = Math.Clamp(Cfg.MaxDirLevel, 1, 6),
                    TopFileCount = Math.Clamp(Cfg.TopFileCount, 10, 1000),
                    ExcludePrefixes = new List<string> { Paths.Dir },
                };
                foreach (var e in Cfg.ExcludePaths)
                    if (!string.IsNullOrWhiteSpace(e)) opt.ExcludePrefixes.Add(e.TrimEnd('\\'));

                var res = Scanner.Scan(opt, CancellationToken.None);

                Status = Loc.T("status.readingSys");
                StatusKindNow = StatusKind.Working;
                OnStateChanged?.Invoke();
                var shadow = SystemUsage.QueryShadow();
                Shadow = shadow;

                var snap = Snapshot.FromScan(res, shadow.UsedBytes);
                string scanError = res.Error;
                double durationMs = res.DurationMs;
                Status = Loc.T("status.measuring");
                StatusKindNow = StatusKind.Working;
                OnStateChanged?.Invoke();
                snap.Advice = AdviceEngine.Measure(snap);
                Store.Save(snap);
                res = null;               // 允许 GC 回收扫描期临时对象（<1MB 的分区条目等）

                LastScanTime = snap.Time;
                LastScanMs = durationMs;
                Status = scanError.Length > 0 ? Loc.F("status.doneWithWarn", scanError) : Loc.T("status.done");
                StatusKindNow = StatusKind.Done;
                _lastAutoScanDate = snap.Time.ToLocalTime().Date;
                ScheduleNext();

                ReleaseMemory();          // Aggressive 收集：把空闲段还给系统
                RefreshDerived(fireAlerts: true, fresh: snap);
                // 工作集回收不在这里做：紧接着 UI 要重绘一遍，现在 trim 等于白做。
                // 由 MainWindow 在扫描结束后隔两次心跳（约 2s，重绘已完成）再调用 TrimWorkingSet()。
            }
            catch (Exception ex)
            {
                Status = Loc.F("status.scanFailed", ex.Message);
                StatusKindNow = StatusKind.Failed;
            }
            finally
            {
                Scanning = false;
                Interlocked.Exchange(ref _scanningFlag, 0);
                OnStateChanged?.Invoke();
            }
        })
        {
            IsBackground = true,
            Priority = foreground ? ThreadPriority.Normal : ThreadPriority.BelowNormal,
        };
        t.Start();
    }

    /// <summary>
    /// 重新计算派生数据（对比、告警、建议）。
    /// <paramref name="fresh"/> 不为 null 表示「刚扫完、快照已在内存里」，此时不再从磁盘重载最新两份，
    /// 直接把新快照当 Latest、原 Latest 降为 Prev（省两次反序列化 + 一份重复对象图）。
    /// </summary>
    public static void RefreshDerived(bool fireAlerts, Snapshot? fresh = null)
    {
        lock (_lock)
        {
            if (fresh != null)
            {
                if (Latest == null || Latest.Time != fresh.Time) Prev = Latest ?? Store.Previous();
                Latest = fresh;
            }
            else
            {
                Latest = Store.Latest();
                Prev = Store.Previous();
            }
            // 冷启动时 LastScanTime 还是默认值：用最新快照的时间兜底，避免界面显示"尚未完成过扫描"
            if (LastScanTime <= DateTimeOffset.MinValue && Latest != null) LastScanTime = Latest.Time;
            // 状态栏同理：有历史快照就不该显示"尚未扫描"
            if (fresh == null && Latest != null && StatusKindNow == StatusKind.Never)
            {
                Status = Loc.F("status.fromSnapshot", Fmt.TimeFull(Latest.Time));
                StatusKindNow = StatusKind.Done;
            }
            Diff = (Latest != null && Prev != null) ? DiffEngine.Compare(Prev, Latest) : null;
            Advice = Latest != null
                ? (Latest.Advice.Count > 0 ? Latest.Advice : AdviceEngine.Evaluate(Latest))
                : new List<AdviceCard>();
            Alerts = Latest != null ? AlertEngine.Evaluate(Latest, Prev, Cfg.ToAlertSettings(), useCooldown: fireAlerts) : new List<AlertRecord>();
            _historyCache = null;      // 快照序列可能已经变化
            DataVersion++;
        }
        if (fireAlerts && Alerts.Count > 0)
        {
            var a = Alerts.OrderByDescending(x => x.Level).First();
            OnNotify?.Invoke(a.Title, a.Detail);
        }
        OnDataChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    /// <summary>与基线对比。</summary>
    public static DiffResult? DiffWithBaseline()
    {
        var b = Store.Baseline();
        var l = Store.Latest();
        if (b == null || l == null || b.Time == l.Time) return null;
        return DiffEngine.Compare(b, l);
    }

    /// <summary>
    /// 历史序列（缓存）。只读「快照头」（每份约 100 字节），
    /// 不再把完整快照留在内存里——否则历史仓库涨到 300MB 时会吃掉几百 MB 内存。
    /// </summary>
    public static List<SnapHeader> History()
    {
        var c = _historyCache;
        if (c != null) return c;
        lock (_lock)
        {
            if (_historyCache == null) _historyCache = Store.Headers();
            return _historyCache;
        }
    }

    /// <summary>
    /// 扫描结束后把膨胀的堆压回去。Aggressive 收集会顺带把空闲段 decommit 给系统；
    /// 实测（本机）：私有内存 30MB → 10MB、已提交 24MB → 4MB，普通 compacting GC 做不到。
    /// </summary>
    public static void ReleaseMemory()
    {
        try
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.Default;
        }
        catch { }
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    /// <summary>
    /// 把工作集交还系统（页进入 standby，下次访问是软缺页：实测多 800~2300 次、约 5~15ms，
    /// 且 DWM 保留隐藏前的画面，不会白屏）。只在扫描结束后调用，不在隐藏窗口时调用。
    /// </summary>
    public static void TrimWorkingSet()
    {
        try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
    }
}
