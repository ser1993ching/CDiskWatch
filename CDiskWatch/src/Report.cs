using System.Text;

namespace CDiskWatch;

/// <summary>文本报告（CLI 输出 / 面板「复制报告」）。所有文案走 Loc，支持中/日/英。</summary>
public static class Report
{
    public static string Build(Snapshot? s, DiffResult? diff, List<AdviceCard> advice, List<AlertRecord> alerts, bool baseline)
    {
        var sb = new StringBuilder();
        if (s == null) return Loc.T("report.noSnapshot");

        string rule = new string('═', 62);
        sb.AppendLine(rule);
        sb.AppendLine(Loc.F("report.header", Fmt.TimeFull(s.Time), baseline ? Loc.T("report.vsBaseline") : ""));
        sb.AppendLine(rule);
        sb.AppendLine();

        sb.AppendLine(Loc.T("report.sec.overview"));
        Row(sb, Loc.T("report.total"), Fmt.Bytes(s.TotalBytes));
        Row(sb, Loc.T("report.used"), Fmt.Bytes(s.UsedBytes) + "   (" + Fmt.Pct(s.UsedRatio) + ")");
        Row(sb, Loc.T("report.free"), Fmt.Bytes(s.FreeBytes) + "   (" + Fmt.Pct(s.TotalBytes > 0 ? (double)s.FreeBytes / s.TotalBytes : 0) + ")");
        sb.AppendLine("  " + Loc.F("report.logical", Fmt.Bytes(s.LogicalBytes), Fmt.Num(s.FileCount)));
        Row(sb, Loc.T("report.unaccounted"), Fmt.Bytes(s.UnaccountedBytes));
        if (s.HardlinkBytes > 0) sb.AppendLine("  " + Loc.F("report.hardlinkNote", Fmt.Bytes(s.HardlinkBytes)));
        if (s.ShadowUsedBytes > 0) sb.AppendLine("  " + Loc.F("report.shadow", Fmt.Bytes(s.ShadowUsedBytes)));
        sb.AppendLine();

        if (s.SysFiles.Count > 0)
        {
            sb.AppendLine(Loc.T("report.sec.sys"));
            foreach (var f in s.SysFiles.OrderByDescending(x => x.Bytes))
                sb.AppendLine($"  {Fmt.Bytes(f.Bytes),12}  {f.Path}");
            sb.AppendLine();
        }

        sb.AppendLine(Loc.T("report.sec.cats"));
        var cats = Enumerable.Range(0, Cat.Count).Select(i => (i, b: s.CatBytes[i], c: s.CatCounts[i]))
            .Where(x => x.b > 0).OrderByDescending(x => x.b).ToList();
        long tot = cats.Sum(x => x.b);
        foreach (var (i, b, c) in cats)
            sb.AppendLine("  " + Loc.F("report.catLine",
                Pad(Cat.Name(i), 14), Fmt.Bytes(b).PadLeft(10),
                Fmt.Pct(tot > 0 ? (double)b / tot : 0).PadLeft(6), Fmt.Num(c)));
        sb.AppendLine();

        sb.AppendLine(Loc.F("report.sec.dirs", 30));
        foreach (var d in s.Rollups.Where(x => x.Level <= 3).Take(30))
            sb.AppendLine("  " + Loc.F("report.dirLine", Fmt.Bytes(d.Bytes).PadLeft(10), d.Level, d.Path));
        sb.AppendLine();

        sb.AppendLine(Loc.F("report.sec.files", s.TopFiles.Count));
        int n = 0;
        foreach (var f in s.TopFiles)
        {
            sb.AppendLine("  " + Loc.F("report.fileLine", (n + 1).ToString().PadLeft(3), Fmt.Bytes(f.Bytes).PadLeft(10), Fmt.Stamp(f.Modified), f.Path));
            n++;
        }
        sb.AppendLine();

        if (diff != null)
        {
            sb.AppendLine(Loc.T("report.sec.diff") + (baseline ? Loc.T("report.vsBaseline") : Loc.T("report.vsPrev")));
            sb.AppendLine("  " + diff.Verdict);
            sb.AppendLine();
            sb.AppendLine("  " + Loc.F("report.diff.growing", 20));
            int k = 0;
            foreach (var g in diff.Growing.Take(20))
            {
                sb.AppendLine("    " + Loc.F("report.growLine", (k + 1).ToString().PadLeft(3), Fmt.Bytes(g.Delta).PadLeft(10),
                    Fmt.Bytes(g.NewBytes), g.IsNew ? Loc.T("report.newTag") : "", g.Path));
                k++;
            }
            sb.AppendLine();
            sb.AppendLine("  " + Loc.T("report.diff.cats"));
            foreach (var c in diff.Cats.Where(x => x.Delta != 0).Take(10))
                sb.AppendLine("    " + Loc.F("report.catDeltaLine", Pad(c.Name, 14),
                    ((c.Delta > 0 ? "+" : "") + Fmt.Bytes(c.Delta)).PadLeft(10), Fmt.Bytes(c.NewBytes)));
            sb.AppendLine();
            if (diff.NewFiles.Count > 0)
            {
                sb.AppendLine("  " + Loc.T("report.diff.newFiles"));
                foreach (var f in diff.NewFiles.Take(10))
                    sb.AppendLine("    " + Loc.F("report.newFileLine", Fmt.Bytes(f.Bytes).PadLeft(10), f.Path));
                sb.AppendLine();
            }
        }

        if (alerts.Count > 0)
        {
            sb.AppendLine(Loc.T("report.sec.alerts"));
            foreach (var a in alerts)
                sb.AppendLine("  " + Loc.F("report.alertLine", a.LevelLabel, a.Title, a.Detail));
            sb.AppendLine();
        }

        if (advice.Count > 0)
        {
            sb.AppendLine(Loc.T("report.sec.advice"));
            foreach (var c in advice.Take(30))
            {
                sb.AppendLine("  " + Loc.F("report.adviceLine", Fmt.Bytes(c.Bytes).PadLeft(10),
                    c.RiskLabel, new string('★', c.Stars), c.Path));
                sb.AppendLine("      " + Loc.F("report.adviceRule", c.RuleName, c.Tip));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>结构化报告（--report --format json）。</summary>
    public static string BuildJson(Snapshot? s, DiffResult? diff, List<AdviceCard> advice, List<AlertRecord> alerts, bool baseline)
        => ReportJsonBuilder.Serialize(ReportJsonBuilder.Build(s, diff, advice, alerts, baseline));

    static void Row(StringBuilder sb, string label, string value) => sb.AppendLine("  " + Pad(label, 10) + value);

    /// <summary>按显示宽度补空格（CJK 记 2 列，保证中日英混排时列对齐；超宽时至少留一个空格）。</summary>
    static string Pad(string s, int width)
    {
        int w = 0;
        foreach (char ch in s) w += ch >= 0x2E80 ? 2 : 1;
        return w >= width ? s + " " : s + new string(' ', width - w);
    }
}
