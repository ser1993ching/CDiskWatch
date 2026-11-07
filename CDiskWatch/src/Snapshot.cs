using System.IO.Compression;

namespace CDiskWatch;

/// <summary>一次全盘扫描的快照（可持久化）。目录条目只存分区键（互不重叠），祖先汇总在加载时重算。</summary>
public sealed class Snapshot
{
    public DateTimeOffset Time;
    public long TotalBytes;
    public long FreeBytes;
    public long UsedBytes;
    public long LogicalBytes;
    public long UnaccountedBytes;
    public long FileCount;
    public long DirCount;
    public long CacheBytes;
    public long TempBytes;
    public long ShadowUsedBytes;
    /// <summary>逻辑合计超出实际占用的部分（通常来自硬链接重复计数，如 WinSxS）。</summary>
    public long HardlinkBytes;
    public long[] CatBytes = new long[Cat.Count];
    public long[] CatCounts = new long[Cat.Count];
    public List<DirStat> Dirs = new();
    public List<FileStat> TopFiles = new();
    public List<SysFileStat> SysFiles = new();
    /// <summary>本次扫描时精确测量的删除建议（随快照持久化）。</summary>
    public List<AdviceCard> Advice = new();
    public string Drive = "C";

    /// <summary>祖先汇总视图（由分区键上卷得到，不落盘）。</summary>
    public List<DirStat> Rollups = new();

    public double UsedRatio => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;

    public static Snapshot FromScan(ScanResult r, long shadowUsed)
    {
        var s = new Snapshot
        {
            Time = r.Time,
            TotalBytes = r.TotalBytes,
            FreeBytes = r.FreeBytes,
            UsedBytes = r.UsedBytes,
            LogicalBytes = r.LogicalBytes,
            UnaccountedBytes = r.UnaccountedBytes,
            FileCount = r.FileCount,
            DirCount = r.DirCount,
            CacheBytes = r.CacheBytes,
            TempBytes = r.TempBytes,
            ShadowUsedBytes = shadowUsed,
            HardlinkBytes = r.HardlinkBytes,
            Drive = r.Drive,
        };
        for (int i = 0; i < Cat.Count; i++)
        {
            s.CatBytes[i] = r.CatBytes[i];
            s.CatCounts[i] = r.CatCounts[i];
        }
        foreach (var d in r.Dirs)
        {
            if (d.IsPartition)
            {
                if (d.Bytes >= MinStoredDirBytes) s.Dirs.Add(d);
            }
            else
            {
                s.Rollups.Add(d);   // 祖先汇总（来自扫描时的全量统计，值更准）
            }
        }
        s.TopFiles.AddRange(r.TopFiles);
        s.SysFiles.AddRange(r.SysFiles);
        if (s.Rollups.Count == 0) s.ComputeRollups();
        else s.Rollups.Sort((x, y) => y.Bytes.CompareTo(x.Bytes));
        return s;
    }

    /// <summary>由分区键向上汇总出层级视图（C:\Users = 其下所有分区键之和）。</summary>
    public void ComputeRollups()
    {
        var anc = new Dictionary<string, DirStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Dirs)
        {
            // lv <= p.Level：包含分区键自身所在层级，否则「目录直属文件」不会被汇总进去
            for (int lv = 1; lv <= p.Level; lv++)
            {
                string a = Scanner.TruncateToLevel(p.Path, lv);
                if (a.Length == 0) continue;
                if (!anc.TryGetValue(a, out var acc))
                {
                    acc = new DirStat { Path = a, Level = lv, IsPartition = false };
                    anc[a] = acc;
                }
                acc.Bytes += p.Bytes;
                acc.FileCount += p.FileCount;
            }
        }
        Rollups = anc.Values.ToList();
        Rollups.Sort((x, y) => y.Bytes.CompareTo(x.Bytes));
    }

    // ================= 序列化（GZip + BinaryWriter） =================

    internal const uint Magic = 0x53574443; // "CDWS"
    internal const int Version = 4;
    /// <summary>快照只保留 ≥1MB 的目录条目，避免历史仓库无限膨胀。</summary>
    public const long MinStoredDirBytes = 1L << 20;

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(gz, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(Time.UtcTicks);
            w.Write(Drive);
            w.Write(TotalBytes); w.Write(FreeBytes); w.Write(UsedBytes);
            w.Write(LogicalBytes); w.Write(UnaccountedBytes);
            w.Write(FileCount); w.Write(DirCount);
            w.Write(CacheBytes); w.Write(TempBytes); w.Write(ShadowUsedBytes); w.Write(HardlinkBytes);
            for (int i = 0; i < Cat.Count; i++) { w.Write(CatBytes[i]); w.Write(CatCounts[i]); }

            w.Write(Dirs.Count);
            foreach (var d in Dirs) { w.Write(d.Path); w.Write(d.Bytes); w.Write(d.FileCount); w.Write(d.Level); }

            w.Write(TopFiles.Count);
            foreach (var f in TopFiles) { w.Write(f.Path); w.Write(f.Bytes); w.Write(f.Modified.UtcTicks); }

            // v4：种类/风险改为稳定枚举，文案在读取时按当前语言翻译（不再把中文写进快照）
            w.Write(SysFiles.Count);
            foreach (var s in SysFiles) { w.Write(s.Path); w.Write(s.Bytes); w.Write((byte)s.Flavor); }

            w.Write(Advice.Count);
            foreach (var a in Advice)
            {
                w.Write(a.Path); w.Write(a.Bytes); w.Write(a.RuleId); w.Write((byte)a.Risk);
                w.Write(a.Stars); w.Write(a.IsFile); w.Write(a.IsRoot);
                w.Write(a.Approximate); w.Write(a.MatchCount);
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static Snapshot? Load(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var r = new BinaryReader(gz, System.Text.Encoding.UTF8);
            if (r.ReadUInt32() != Magic) return null;
            int ver = r.ReadInt32();
            if (ver > Version) return null;
            var s = new Snapshot
            {
                Time = new DateTimeOffset(r.ReadInt64(), TimeSpan.Zero),
                Drive = r.ReadString(),
                TotalBytes = r.ReadInt64(),
                FreeBytes = r.ReadInt64(),
                UsedBytes = r.ReadInt64(),
                LogicalBytes = r.ReadInt64(),
                UnaccountedBytes = r.ReadInt64(),
                FileCount = r.ReadInt64(),
                DirCount = r.ReadInt64(),
                CacheBytes = r.ReadInt64(),
                TempBytes = r.ReadInt64(),
                ShadowUsedBytes = r.ReadInt64(),
                HardlinkBytes = r.ReadInt64(),
            };
            for (int i = 0; i < Cat.Count; i++) { s.CatBytes[i] = r.ReadInt64(); s.CatCounts[i] = r.ReadInt64(); }
            int nd = r.ReadInt32();
            for (int i = 0; i < nd; i++)
                s.Dirs.Add(new DirStat { Path = r.ReadString(), Bytes = r.ReadInt64(), FileCount = r.ReadInt64(), Level = r.ReadInt32(), IsPartition = true });
            int nf = r.ReadInt32();
            for (int i = 0; i < nf; i++)
                s.TopFiles.Add(new FileStat { Path = r.ReadString(), Bytes = r.ReadInt64(), Modified = new DateTimeOffset(r.ReadInt64(), TimeSpan.Zero) });
            int ns = r.ReadInt32();
            for (int i = 0; i < ns; i++)
            {
                string sysPath = r.ReadString();
                long bytes = r.ReadInt64();
                SysFlavor flavor;
                if (ver >= 4) flavor = (SysFlavor)r.ReadByte();
                else
                {
                    // v3 存的是中文「系统文件/崩溃转储」+ 中文备注 → 依据文件名还原种类
                    _ = r.ReadString();
                    _ = r.ReadString();
                    flavor = SysFileStat.FlavorOf(Path.GetFileName(sysPath));
                }
                s.SysFiles.Add(new SysFileStat { Path = sysPath, Bytes = bytes, Flavor = flavor });
            }
            if (ver >= 4)
            {
                int na = r.ReadInt32();
                for (int i = 0; i < na; i++)
                {
                    s.Advice.Add(new AdviceCard
                    {
                        Path = r.ReadString(),
                        Bytes = r.ReadInt64(),
                        RuleId = r.ReadString(),
                        Risk = (RiskLevel)r.ReadByte(),
                        Stars = r.ReadInt32(),
                        IsFile = r.ReadBoolean(),
                        IsRoot = r.ReadBoolean(),
                        Approximate = r.ReadBoolean(),
                        MatchCount = r.ReadInt32(),
                    });
                }
            }
            else if (ver == 3)
            {
                int na = r.ReadInt32();
                for (int i = 0; i < na; i++)
                {
                    string cardPath = r.ReadString();
                    long bytes = r.ReadInt64();
                    string legacyRule = r.ReadString();
                    string legacyRisk = r.ReadString();
                    int stars = r.ReadInt32();
                    _ = r.ReadString();   // 旧 Tip（不再落盘）
                    bool isFile = r.ReadBoolean(), isRoot = r.ReadBoolean(), approx = r.ReadBoolean();
                    int match = r.ReadInt32();
                    s.Advice.Add(new AdviceCard
                    {
                        Path = cardPath, Bytes = bytes,
                        RuleId = AdviceEngine.IdFromLegacyName(legacyRule),
                        Risk = AdviceEngine.RiskFromLegacy(legacyRisk),
                        Stars = stars, IsFile = isFile, IsRoot = isRoot, Approximate = approx, MatchCount = match,
                    });
                }
            }
            s.ComputeRollups();
            return s;
        }
        catch { return null; }
    }
}

/// <summary>
/// 快照的「头信息」：只解析到 HardlinkBytes 为止（约一百多字节），
/// 用于历史列表与趋势图——**不加载目录/大文件/建议明细**，
/// 因此内存占用与历史快照数量无关（否则 300MB 仓库全量加载会吃掉几百 MB 内存）。
/// </summary>
public sealed class SnapHeader
{
    public DateTimeOffset Time;
    public string Drive = "C";
    public long TotalBytes;
    public long FreeBytes;
    public long UsedBytes;
    public long LogicalBytes;
    public long UnaccountedBytes;
    public long FileCount;
    public long DirCount;
    public long CacheBytes;
    public long TempBytes;
    public long ShadowUsedBytes;
    public long HardlinkBytes;

    public double UsedRatio => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;

    public static SnapHeader? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var r = new BinaryReader(gz, System.Text.Encoding.UTF8);
            if (r.ReadUInt32() != Snapshot.Magic) return null;
            int ver = r.ReadInt32();
            if (ver > Snapshot.Version) return null;
            return new SnapHeader
            {
                Time = new DateTimeOffset(r.ReadInt64(), TimeSpan.Zero),
                Drive = r.ReadString(),
                TotalBytes = r.ReadInt64(),
                FreeBytes = r.ReadInt64(),
                UsedBytes = r.ReadInt64(),
                LogicalBytes = r.ReadInt64(),
                UnaccountedBytes = r.ReadInt64(),
                FileCount = r.ReadInt64(),
                DirCount = r.ReadInt64(),
                CacheBytes = r.ReadInt64(),
                TempBytes = r.ReadInt64(),
                ShadowUsedBytes = r.ReadInt64(),
                HardlinkBytes = r.ReadInt64(),
            };
        }
        catch { return null; }
    }
}
