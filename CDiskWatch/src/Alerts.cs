namespace CDiskWatch;

public enum AlertLevel { Info = 0, Warning = 1, Critical = 2 }

public sealed class AlertRecord
{
    public DateTimeOffset Time;
    public AlertLevel Level;
    public string Title = "";
    public string Detail = "";
    public string Key = "";

    /// <summary>稳定等级代码（JSON/脚本可依赖）。</summary>
    public string LevelCode => Level switch
    {
        AlertLevel.Critical => "critical",
        AlertLevel.Warning => "warning",
        _ => "info",
    };

    /// <summary>等级显示名（跟随界面语言）。</summary>
    public string LevelLabel => Loc.T(Level switch
    {
        AlertLevel.Critical => "alert.critical",
        AlertLevel.Warning => "alert.warning",
        _ => "alert.info",
    });
}

public sealed class AlertSettings
{
    public long FreeWarnBytes = 20L << 30;
    public long FreeCriticalBytes = 10L << 30;
    public double FreeWarnRatio = 0.15;
    public double FreeCriticalRatio = 0.08;
    public long DailyGrowthWarnBytes = 5L << 30;
    public long DailyGrowthCriticalBytes = 20L << 30;
    public double DailyGrowthWarnRatio = 0.10;
    public long CategoryGrowthBytes = 2L << 30;
    public long SingleFileBytes = 1L << 30;
    public long SystemFileChangeBytes = 1L << 30;
    public int CooldownHours = 6;
}

/// <summary>预警引擎：阈值可配，同一规则同一对象在冷却期内只报一次。</summary>
public static class AlertEngine
{
    static readonly Dictionary<string, DateTimeOffset> _lastFired = new(StringComparer.OrdinalIgnoreCase);

    public static List<AlertRecord> Evaluate(Snapshot latest, Snapshot? prev, AlertSettings s, bool useCooldown = true)
    {
        var list = new List<AlertRecord>();
        var now = DateTimeOffset.Now;

        // ---- 剩余空间 ----
        if (latest.FreeBytes < s.FreeCriticalBytes)
            Fire(list, now, s, useCooldown, "free-critical", AlertLevel.Critical,
                Loc.T("alert.free-critical.title"),
                Loc.F("alert.free-critical.detail", Fmt.Bytes(latest.FreeBytes),
                      Fmt.Pct(latest.TotalBytes > 0 ? (double)latest.FreeBytes / latest.TotalBytes : 0)));
        else if (latest.FreeBytes < s.FreeWarnBytes)
            Fire(list, now, s, useCooldown, "free-warn", AlertLevel.Warning,
                Loc.T("alert.free-warn.title"),
                Loc.F("alert.free-warn.detail", Fmt.Bytes(latest.FreeBytes), Fmt.Bytes(s.FreeWarnBytes)));

        double freeRatio = latest.TotalBytes > 0 ? (double)latest.FreeBytes / latest.TotalBytes : 0;
        if (freeRatio < s.FreeCriticalRatio)
            Fire(list, now, s, useCooldown, "free-ratio-critical", AlertLevel.Critical,
                Loc.T("alert.free-ratio-critical.title"), Loc.F("alert.free-ratio-critical.detail", Fmt.Pct(freeRatio)));
        else if (freeRatio < s.FreeWarnRatio)
            Fire(list, now, s, useCooldown, "free-ratio-warn", AlertLevel.Warning,
                Loc.T("alert.free-ratio-warn.title"), Loc.F("alert.free-ratio-warn.detail", Fmt.Pct(freeRatio)));

        if (prev == null) return list;

        // ---- 单日增长 ----
        long growth = latest.UsedBytes - prev.UsedBytes;
        if (growth > s.DailyGrowthCriticalBytes)
            Fire(list, now, s, useCooldown, "growth-critical", AlertLevel.Critical,
                Loc.T("alert.growth-critical.title"),
                Loc.F("alert.growth-critical.detail", Fmt.Bytes(growth), Fmt.Time(prev.Time), Fmt.Time(latest.Time)));
        else if (growth > s.DailyGrowthWarnBytes)
            Fire(list, now, s, useCooldown, "growth-warn", AlertLevel.Warning,
                Loc.T("alert.growth-warn.title"), Loc.F("alert.growth-warn.detail", Fmt.Bytes(growth)));

        double growthRatio = prev.UsedBytes > 0 ? (double)growth / prev.UsedBytes : 0;
        if (growthRatio > s.DailyGrowthWarnRatio && growth > 1L << 30)
            Fire(list, now, s, useCooldown, "growth-ratio", AlertLevel.Warning,
                Loc.T("alert.growth-ratio.title"), Loc.F("alert.growth-ratio.detail", Fmt.Pct(growthRatio), Fmt.Bytes(growth)));

        // ---- 类别增长 ----
        var diff = DiffEngine.Compare(prev, latest);
        foreach (var c in diff.Cats)
        {
            if (c.Delta < s.CategoryGrowthBytes) continue;
            string share = growth > 0 ? Fmt.Pct((double)c.Delta / growth) : Loc.T("alert.na");
            Fire(list, now, s, useCooldown, "cat-" + c.Index, AlertLevel.Info,
                Loc.F("alert.cat.title", c.Name, Fmt.Bytes(c.Delta)),
                Loc.F("alert.cat.detail", Fmt.Bytes(growth), share));
        }

        // ---- 单个大文件 ----
        foreach (var f in diff.NewFiles)
        {
            if (f.Bytes < s.SingleFileBytes) continue;
            Fire(list, now, s, useCooldown, "file-" + f.Path, AlertLevel.Info,
                Loc.T("alert.file.title"), Loc.F("alert.file.detail", f.Path, Fmt.Bytes(f.Bytes), Fmt.Stamp(f.Modified)));
        }

        // ---- 系统文件突变 ----
        foreach (var sf in diff.SysChanged)
        {
            if (Math.Abs(sf.Bytes) < s.SystemFileChangeBytes) continue;
            Fire(list, now, s, useCooldown, "sys-" + sf.Path, AlertLevel.Warning,
                Loc.F("alert.sys.title", System.IO.Path.GetFileName(sf.Path), Fmt.Bytes(sf.Bytes)),
                sf.Note);
        }

        return list;
    }

    static void Fire(List<AlertRecord> list, DateTimeOffset now, AlertSettings s, bool useCooldown,
                     string key, AlertLevel level, string title, string detail)
    {
        if (useCooldown)
        {
            if (_lastFired.TryGetValue(key, out var t) && (now - t).TotalHours < s.CooldownHours) return;
            // 文件级预警的 key 是完整路径，长期运行会无限增长；超过上限时清理已过冷却期的条目
            if (_lastFired.Count > 512)
            {
                var stale = _lastFired.Where(kv => (now - kv.Value).TotalHours >= s.CooldownHours)
                                      .Select(kv => kv.Key).ToList();
                foreach (var k in stale) _lastFired.Remove(k);
                if (_lastFired.Count > 4096) _lastFired.Clear();
            }
            _lastFired[key] = now;
        }
        list.Add(new AlertRecord { Time = now, Level = level, Title = title, Detail = detail, Key = key });
    }
}
