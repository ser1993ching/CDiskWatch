using System.Text;

namespace CDiskWatch;

/// <summary>文本报告（CLI 输出 / 面板「复制报告」）。</summary>
public static class Report
{
    public static string Build(Snapshot? s, DiffResult? diff, List<AdviceCard> advice, List<AlertRecord> alerts, bool baseline)
    {
        var sb = new StringBuilder();
        if (s == null) return "（尚无快照，请先运行 CDiskWatch.exe --scan）";

        sb.AppendLine("══════════════════════════════════════════════════════════════");
        sb.AppendLine($" C 盘容量监控报告    快照时间 {Fmt.TimeFull(s.Time)}{(baseline ? "（与基线对比）" : "")}");
        sb.AppendLine("══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("【空间概览】");
        sb.AppendLine($"  总容量   {Fmt.Bytes(s.TotalBytes)}");
        sb.AppendLine($"  已用     {Fmt.Bytes(s.UsedBytes)}   ({Fmt.Pct(s.UsedRatio)})");
        sb.AppendLine($"  可用     {Fmt.Bytes(s.FreeBytes)}   ({Fmt.Pct(s.TotalBytes > 0 ? (double)s.FreeBytes / s.TotalBytes : 0)})");
        sb.AppendLine($"  文件逻辑合计 {Fmt.Bytes(s.LogicalBytes)}   文件数 {s.FileCount:N0}");
        sb.AppendLine($"  未归因（非文件夹占用） {Fmt.Bytes(s.UnaccountedBytes)}");
        if (s.HardlinkBytes > 0)
            sb.AppendLine($"  ※ 逻辑合计比实际占用多 {Fmt.Bytes(s.HardlinkBytes)}：WinSxS 等目录存在硬链接，同一份数据被重复计数");
        if (s.ShadowUsedBytes > 0)
            sb.AppendLine($"  卷影副本/还原点占用 {Fmt.Bytes(s.ShadowUsedBytes)}");
        sb.AppendLine();

        if (s.SysFiles.Count > 0)
        {
            sb.AppendLine("【系统级占用（不在普通文件夹里）】");
            foreach (var f in s.SysFiles.OrderByDescending(x => x.Bytes))
                sb.AppendLine($"  {Fmt.Bytes(f.Bytes),12}  {f.Path}");
            sb.AppendLine();
        }

        sb.AppendLine("【文件类型分布】");
        var cats = Enumerable.Range(0, Cat.Count).Select(i => (i, b: s.CatBytes[i], c: s.CatCounts[i]))
            .Where(x => x.b > 0).OrderByDescending(x => x.b).ToList();
        long tot = cats.Sum(x => x.b);
        foreach (var (i, b, c) in cats)
            sb.AppendLine($"  {Cat.Names[i],-8} {Fmt.Bytes(b),12}  {Fmt.Pct(tot > 0 ? (double)b / tot : 0),8}   {c:N0} 个文件");
        sb.AppendLine();

        sb.AppendLine("【大目录 Top 30】（层级视图，同级可包含）");
        foreach (var d in s.Rollups.Where(x => x.Level <= 3).Take(30))
            sb.AppendLine($"  {Fmt.Bytes(d.Bytes),12}  L{d.Level}  {d.Path}");
        sb.AppendLine();

        sb.AppendLine("【Top 100 大文件】");
        int n = 0;
        foreach (var f in s.TopFiles)
        {
            sb.AppendLine($"  {n + 1,3}. {Fmt.Bytes(f.Bytes),12}  {f.Modified:yyyy-MM-dd HH:mm}  {f.Path}");
            n++;
        }
        sb.AppendLine();

        if (diff != null)
        {
            sb.AppendLine("【变化对比】" + (baseline ? "（相对基线）" : "（相对上次快照）"));
            sb.AppendLine("  " + diff.Verdict);
            sb.AppendLine();
            sb.AppendLine("  ▸ 增量文件夹 Top 20");
            int k = 0;
            foreach (var g in diff.Growing.Take(20))
            {
                sb.AppendLine($"    {k + 1,3}. +{Fmt.Bytes(g.Delta),12}  (现 {Fmt.Bytes(g.NewBytes)})  {(g.IsNew ? "[新增] " : "")}{g.Path}");
                k++;
            }
            sb.AppendLine();
            sb.AppendLine("  ▸ 文件类型增量");
            foreach (var c in diff.Cats.Where(x => x.Delta != 0).Take(10))
                sb.AppendLine($"    {c.Name,-8} {(c.Delta > 0 ? "+" : "")}{Fmt.Bytes(c.Delta),12}   现 {Fmt.Bytes(c.NewBytes)}");
            sb.AppendLine();
            if (diff.NewFiles.Count > 0)
            {
                sb.AppendLine("  ▸ 新增大文件");
                foreach (var f in diff.NewFiles.Take(10))
                    sb.AppendLine($"    +{Fmt.Bytes(f.Bytes),12}  {f.Path}");
                sb.AppendLine();
            }
        }

        if (alerts.Count > 0)
        {
            sb.AppendLine("【预警】");
            foreach (var a in alerts)
                sb.AppendLine($"  [{a.Level}] {a.Title} — {a.Detail}");
            sb.AppendLine();
        }

        if (advice.Count > 0)
        {
            sb.AppendLine("【删除建议】（本软件不会自动删除任何文件，仅供参考）");
            foreach (var c in advice.Take(30))
            {
                sb.AppendLine($"  {Fmt.Bytes(c.Bytes),12}  [{c.Risk}风险 {new string('★', c.Stars)}]  {c.Path}");
                sb.AppendLine($"      规则：{c.Rule} —— {c.Tip}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
