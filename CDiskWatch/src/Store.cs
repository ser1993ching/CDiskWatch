namespace CDiskWatch;

/// <summary>快照仓库：%AppData%\CDiskWatch\history\</summary>
public static class Store
{
    public static string RootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDiskWatch");

    public static string HistoryDir => Path.Combine(RootDir, "history");

    public static void EnsureDir() => Directory.CreateDirectory(HistoryDir);

    public static string NameOf(DateTimeOffset t) => t.ToLocalTime().ToString("yyyyMMdd-HHmmss-fffffff");

    public static string SavePath(DateTimeOffset t) => Path.Combine(HistoryDir, "snap-" + NameOf(t) + ".snap");

    /// <summary>保存快照；若目录里还没有任何快照，同时存一份 baseline。</summary>
    public static string Save(Snapshot s)
    {
        EnsureDir();
        bool first = !Directory.Exists(HistoryDir) || Directory.GetFiles(HistoryDir, "snap-*.snap").Length == 0;
        string path = SavePath(s.Time);
        s.Save(path);
        if (first)
        {
            try { s.Save(Path.Combine(HistoryDir, "baseline.snap")); } catch { }
        }
        ApplyRetention();
        return path;
    }

    /// <summary>历史仓库上限 300MB，超出时删除最旧的快照（基线快照永不删除）。</summary>
    public static void ApplyRetention(long maxBytes = 300L << 20)
    {
        try
        {
            var files = Directory.GetFiles(HistoryDir, "snap-*.snap");
            long total = files.Sum(f => new FileInfo(f).Length);
            if (total <= maxBytes) return;
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length - 1 && total > maxBytes; i++)
            {
                long sz = new FileInfo(files[i]).Length;
                try { File.Delete(files[i]); } catch { continue; }
                total -= sz;
            }
        }
        catch { }
    }

    /// <summary>全部快照文件（按时间升序）。</summary>
    public static List<string> ListFiles()
    {
        try
        {
            if (!Directory.Exists(HistoryDir)) return new List<string>();
            var files = Directory.GetFiles(HistoryDir, "snap-*.snap");
            Array.Sort(files, StringComparer.Ordinal);
            return files.ToList();
        }
        catch { return new List<string>(); }
    }

    public static Snapshot? Load(string path) => Snapshot.Load(path);

    /// <summary>按时间倒序读取最近的有效快照，自动跳过损坏文件。</summary>
    static List<Snapshot> LoadRecent(int count)
    {
        var result = new List<Snapshot>(count);
        var files = ListFiles();
        for (int i = files.Count - 1; i >= 0 && result.Count < count; i--)
        {
            var snapshot = Snapshot.Load(files[i]);
            if (snapshot != null) result.Add(snapshot);
        }
        return result;
    }

    /// <summary>最新一份快照。</summary>
    public static Snapshot? Latest() => LoadRecent(1).FirstOrDefault();

    /// <summary>最新一份有效快照的前一份（用于日对比）。</summary>
    public static Snapshot? Previous()
    {
        var recent = LoadRecent(2);
        return recent.Count > 1 ? recent[1] : null;
    }

    /// <summary>基线（最早的一份快照）。</summary>
    public static Snapshot? Baseline()
    {
        try
        {
            string bp = Path.Combine(HistoryDir, "baseline.snap");
            if (File.Exists(bp))
            {
                var b = Snapshot.Load(bp);
                if (b != null) return b;
            }
        }
        catch { }
        var files = ListFiles();
        foreach (var f in files)
        {
            var s = Snapshot.Load(f);
            if (s != null) return s;
        }
        return null;
    }

    /// <summary>历史头信息序列（按时间升序）：趋势图与历史列表用，不加载明细。</summary>
    public static List<SnapHeader> Headers()
    {
        var list = new List<SnapHeader>();
        foreach (var f in ListFiles())
        {
            var h = SnapHeader.Read(f);
            if (h != null) list.Add(h);
        }
        return list;
    }

    /// <summary>历史序列（完整快照，慎用：每份约 1.3MB 内存）。</summary>
    public static List<Snapshot> History()
    {
        var list = new List<Snapshot>();
        foreach (var f in ListFiles())
        {
            var s = Snapshot.Load(f);
            if (s != null) list.Add(s);
        }
        return list;
    }
}
