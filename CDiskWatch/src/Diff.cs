namespace CDiskWatch;

public sealed class DirDelta
{
    public string Path = "";
    public long Delta;
    public long NewBytes;
    public long OldBytes;
    public int Level;
    public bool IsNew;
    public bool IsGone;
}

public sealed class CatDelta
{
    public int Index;
    public string Name = "";
    public long Delta;
    public long NewBytes;
    public long OldBytes;
    public long NewCount;
}

public sealed class DiffResult
{
    public DateTimeOffset From;
    public DateTimeOffset To;
    public long UsedDelta;
    public long FreeDelta;
    public long LogicalDelta;
    public long UnaccountedDelta;
    public double RatioFrom;
    public double RatioTo;
    public List<DirDelta> Growing = new();
    public List<DirDelta> Shrinking = new();
    public List<CatDelta> Cats = new();
    public List<FileStat> NewFiles = new();
    public List<FileStat> GoneFiles = new();
    public List<SysFileStat> SysChanged = new();
    public string Verdict = "";
}

/// <summary>快照对比引擎：目录级 / 类别级 / 文件级 三级归因。</summary>
public static class DiffEngine
{
    public static DiffResult Compare(Snapshot from, Snapshot to)
    {
        var d = new DiffResult
        {
            From = from.Time,
            To = to.Time,
            UsedDelta = to.UsedBytes - from.UsedBytes,
            FreeDelta = to.FreeBytes - from.FreeBytes,
            LogicalDelta = to.LogicalBytes - from.LogicalBytes,
            UnaccountedDelta = to.UnaccountedBytes - from.UnaccountedBytes,
            RatioFrom = from.UsedRatio,
            RatioTo = to.UsedRatio,
        };

        // ---- 目录级（分区键，互不重叠） ----
        var oldMap = new Dictionary<string, DirStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in from.Dirs) oldMap[x.Path] = x;
        // 本次快照的路径集合：用于判断旧条目是否已经消失（避免 O(n²) 的 Any 扫描）
        var toPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in to.Dirs) toPaths.Add(x.Path);

        foreach (var x in to.Dirs)
        {
            bool had = oldMap.TryGetValue(x.Path, out var o);
            long oldBytes = o?.Bytes ?? 0;
            long delta = had ? x.Bytes - oldBytes : x.Bytes;
            if (delta == 0 && had) continue;
            var dd = new DirDelta
            {
                Path = x.Path,
                Delta = delta,
                NewBytes = x.Bytes,
                OldBytes = oldBytes,
                Level = x.Level,
                IsNew = !had,
            };
            if (delta > 0) d.Growing.Add(dd); else if (had) d.Shrinking.Add(dd);
        }
        foreach (var kv in oldMap)
        {
            if (toPaths.Contains(kv.Key)) continue;
            d.Shrinking.Add(new DirDelta
            {
                Path = kv.Key,
                Delta = -kv.Value.Bytes,
                OldBytes = kv.Value.Bytes,
                NewBytes = 0,
                Level = kv.Value.Level,
                IsGone = true,
            });
        }
        d.Growing.Sort((a, b) => b.Delta.CompareTo(a.Delta));
        d.Shrinking.Sort((a, b) => a.Delta.CompareTo(b.Delta));

        // ---- 类别级 ----
        for (int i = 0; i < Cat.Count; i++)
        {
            long nb = to.CatBytes[i], ob = from.CatBytes[i];
            if (nb == 0 && ob == 0) continue;
            d.Cats.Add(new CatDelta
            {
                Index = i,
                Name = Cat.Name(i),
                NewBytes = nb,
                OldBytes = ob,
                Delta = nb - ob,
                NewCount = to.CatCounts[i],
            });
        }
        d.Cats.Sort((a, b) => b.Delta.CompareTo(a.Delta));

        // ---- 文件级 ----
        var oldFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in from.TopFiles) oldFiles.Add(f.Path);
        foreach (var f in to.TopFiles)
            if (!oldFiles.Contains(f.Path)) d.NewFiles.Add(f);
        d.NewFiles.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

        var newFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in to.TopFiles) newFiles.Add(f.Path);
        foreach (var f in from.TopFiles)
            if (!newFiles.Contains(f.Path)) d.GoneFiles.Add(f);

        // ---- 系统文件变化 ----
        foreach (var s in to.SysFiles)
        {
            var o = from.SysFiles.FirstOrDefault(x => string.Equals(x.Path, s.Path, StringComparison.OrdinalIgnoreCase));
            long ob = o?.Bytes ?? 0;
            if (ob != s.Bytes)
                d.SysChanged.Add(new SysFileStat { Path = s.Path, Bytes = s.Bytes - ob, Flavor = s.Flavor });
        }

        d.Verdict = BuildVerdict(d, from, to);
        return d;
    }

    /// <summary>把数字翻译成人话（模板化：先拼结构化条目，再按语言渲染，避免中文语序写死）。</summary>
    public static string BuildVerdict(DiffResult d, Snapshot from, Snapshot to)
    {
        // 变化极小时不要输出 "68.7% → 68.7%" 这种看起来像出错的说法
        double fromPct = d.RatioFrom * 100, toPct = d.RatioTo * 100;
        string ratio = Math.Abs(toPct - fromPct) < 0.05
            ? Loc.F("verdict.ratioFlat", fromPct.ToString("F1"))
            : Loc.F("verdict.ratioMove", fromPct.ToString("F1"), toPct.ToString("F1"));

        string head;
        if (d.UsedDelta > 0) head = Loc.F("verdict.headGrew", Fmt.Bytes(d.UsedDelta), ratio);
        else if (d.UsedDelta < 0) head = Loc.F("verdict.headShrank", Fmt.Bytes(d.UsedDelta), ratio);
        else head = Loc.T("verdict.headSame");

        var parts = new List<string>();

        foreach (var s in d.SysChanged)
        {
            parts.Add(s.Bytes > 0
                ? Loc.F("verdict.part.sysUp", System.IO.Path.GetFileName(s.Path), Fmt.Bytes(s.Bytes))
                : Loc.F("verdict.part.sysDown", System.IO.Path.GetFileName(s.Path), Fmt.Bytes(s.Bytes)));
        }

        foreach (var g in d.Growing.Take(3))
        {
            parts.Add(g.IsNew
                ? Loc.F("verdict.part.dirNew", g.Path, Fmt.Bytes(g.Delta))
                : Loc.F("verdict.part.dirGrew", g.Path, Fmt.Bytes(g.Delta)));
        }

        foreach (var c in d.Cats.Where(c => c.Delta > 0).Take(3))
            parts.Add(Loc.F("verdict.part.catGrew", c.Name, Fmt.Bytes(c.Delta)));

        if (d.NewFiles.Count > 0)
            parts.Add(Loc.F("verdict.part.newFile", System.IO.Path.GetFileName(d.NewFiles[0].Path), Fmt.Bytes(d.NewFiles[0].Bytes)));

        if (d.UnaccountedDelta != 0)
            parts.Add(Loc.F("verdict.part.unaccounted", d.UnaccountedDelta > 0 ? "+" : "", Fmt.Bytes(d.UnaccountedDelta)));

        if (parts.Count == 0) return head;
        return head + Loc.F("verdict.cause", string.Join(Loc.T("verdict.sep"), parts));
    }
}

/// <summary>统一的格式化工具（GB/MB/KB + 百分比 + 日期）。数字固定用不变量文化，避免跟随系统区域出现 "1,5 GB"。</summary>
public static class Fmt
{
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public static string Bytes(long b)
    {
        long a = b < 0 ? -b : b;
        string sign = b < 0 ? "-" : "";
        if (a >= 1L << 40) return sign + (a / 1024.0 / 1024 / 1024 / 1024).ToString("F2", Inv) + " TB";
        if (a >= 1L << 30) return sign + (a / 1024.0 / 1024 / 1024).ToString("F2", Inv) + " GB";
        if (a >= 1L << 20) return sign + (a / 1024.0 / 1024).ToString("F2", Inv) + " MB";
        if (a >= 1L << 10) return sign + (a / 1024.0).ToString("F1", Inv) + " KB";
        return sign + a.ToString(Inv) + " B";
    }

    public static string Size(long b) => Bytes(b);

    public static string Pct(double r) => (r * 100).ToString("F1", Inv) + "%";

    public static string Num(long n) => n.ToString("N0", Inv);

    /// <summary>快照时间（短）：中文/日文 MM-dd HH:mm，英文 yyyy-MM-dd HH:mm。</summary>
    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString(Loc.DateShortPattern, Inv);

    /// <summary>完整时间戳。</summary>
    public static string TimeFull(DateTimeOffset t) => t.ToLocalTime().ToString(Loc.DateFullPattern, Inv);

    /// <summary>带年份的分钟级时间戳（跨年场景用）。</summary>
    public static string Stamp(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv);

    /// <summary>秒数（保留 1 位小数）。</summary>
    public static string Seconds(double s) => s.ToString("F1", Inv);
}
