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
                Name = Cat.Names[i],
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
                d.SysChanged.Add(new SysFileStat { Path = s.Path, Bytes = s.Bytes - ob, Kind = s.Kind, Note = s.Note });
        }

        d.Verdict = BuildVerdict(d, from, to);
        return d;
    }

    /// <summary>把数字翻译成人话。</summary>
    public static string BuildVerdict(DiffResult d, Snapshot from, Snapshot to)
    {
        var sb = new System.Text.StringBuilder();
        string used = Fmt.Bytes(Math.Abs(d.UsedDelta));
        // 变化极小时不要输出 "68.7% → 68.7%" 这种看起来像出错的说法
        double fromPct = d.RatioFrom * 100, toPct = d.RatioTo * 100;
        string ratio = Math.Abs(toPct - fromPct) < 0.05
            ? $"占用率 {fromPct:F1}%、基本持平"
            : $"占用率 {fromPct:F1}% → {toPct:F1}%";
        // UsedDelta > 0 表示“已用变多、可用变少”，旧实现把两种情况写反了。
        if (d.UsedDelta > 0) sb.Append($"已用空间增加 {used}（{ratio}）。");
        else if (d.UsedDelta < 0) sb.Append($"已用空间减少 {used}（释放 {used}，{ratio}）。");
        else sb.Append("占用率基本不变。");

        var parts = new List<string>();

        foreach (var s in d.SysChanged)
        {
            if (s.Bytes > 0) parts.Add($"{System.IO.Path.GetFileName(s.Path)} +{Fmt.Bytes(s.Bytes)}");
            else parts.Add($"{System.IO.Path.GetFileName(s.Path)} {Fmt.Bytes(s.Bytes)}");
        }

        var topDirs = d.Growing.Take(3).ToList();
        foreach (var g in topDirs)
            parts.Add($"{(g.IsNew ? "新增目录 " : "")}{g.Path} +{Fmt.Bytes(g.Delta)}");

        var topCats = d.Cats.Where(c => c.Delta > 0).Take(3).ToList();
        foreach (var c in topCats)
            parts.Add($"{c.Name}类 +{Fmt.Bytes(c.Delta)}");

        if (d.NewFiles.Count > 0)
            parts.Add($"新增大文件 {System.IO.Path.GetFileName(d.NewFiles[0].Path)} {Fmt.Bytes(d.NewFiles[0].Bytes)}");

        if (d.UnaccountedDelta != 0)
            parts.Add($"系统级/未归因 {(d.UnaccountedDelta > 0 ? "+" : "")}{Fmt.Bytes(d.UnaccountedDelta)}");

        if (parts.Count > 0) sb.Append(" 主因：" + string.Join("；", parts) + "。");
        return sb.ToString();
    }
}

/// <summary>统一的格式化工具（GB/MB/KB + 百分比）。</summary>
public static class Fmt
{
    public static string Bytes(long b)
    {
        long a = b < 0 ? -b : b;
        string sign = b < 0 ? "-" : "";
        if (a >= 1L << 40) return sign + (a / 1024.0 / 1024 / 1024 / 1024).ToString("F2") + " TB";
        if (a >= 1L << 30) return sign + (a / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
        if (a >= 1L << 20) return sign + (a / 1024.0 / 1024).ToString("F2") + " MB";
        if (a >= 1L << 10) return sign + (a / 1024.0).ToString("F1") + " KB";
        return sign + a + " B";
    }

    public static string Size(long b) => Bytes(b);

    public static string Pct(double r) => (r * 100).ToString("F1") + "%";

    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString("MM-dd HH:mm");

    public static string TimeFull(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
