using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace CDiskWatch;

/// <summary>风险等级（稳定枚举，随快照落盘；显示文案在渲染时翻译）。</summary>
public enum RiskLevel : byte { Low = 0, Mid = 1, High = 2 }

public sealed class AdviceRule
{
    /// <summary>稳定 ID（同时是文案 key 的一部分：advice.rule.&lt;Id&gt;.name / .tip）。</summary>
    public string Id = "";
    /// <summary>路径通配符（支持 %ENV%，* 匹配任意字符，路径分隔符用反斜杠）。</summary>
    public string Pattern = "";
    /// <summary>低 / 中 / 高</summary>
    public RiskLevel Risk = RiskLevel.Low;
    /// <summary>推荐度 1~3</summary>
    public int Stars = 2;
    /// <summary>file = 匹配文件名；dir = 匹配目录路径。</summary>
    public string Kind = "dir";

    public string Name => Loc.T("advice.rule." + Id + ".name");
    public string Tip => Loc.T("advice.rule." + Id + ".tip");
}

public sealed class AdviceCard
{
    public string Path = "";
    public long Bytes;
    public string RuleId = "";
    public RiskLevel Risk = RiskLevel.Low;
    public int Stars = 2;
    public bool IsFile;
    public bool IsRoot;
    public bool Approximate;
    public int MatchCount;

    public string RuleName => AdviceEngine.RuleName(RuleId);
    public string Tip => AdviceEngine.RuleTip(RuleId);
    public string RiskLabel => Loc.T(Risk switch { RiskLevel.High => "risk.high", RiskLevel.Mid => "risk.mid", _ => "risk.low" });
    public string RiskCode => Risk switch { RiskLevel.High => "high", RiskLevel.Mid => "medium", _ => "low" };
}

/// <summary>
/// 删除建议引擎：**只给建议，绝不执行任何删除操作**。
/// 目录类规则会对目标目录做一次精确测量（独立于全盘扫描的聚合粒度），
/// 因此不受「目录聚合深度」限制；测量失败时回退到聚合数据（标注为近似）。
/// </summary>
public static class AdviceEngine
{
    static List<AdviceRule>? _rules;

    public static List<AdviceRule> Rules
    {
        get
        {
            if (_rules == null) _rules = Build();
            return _rules;
        }
    }

    static string Env(string s) => Environment.ExpandEnvironmentVariables(s);

    /// <summary>规则名（按 RuleId 翻译；未知 Id 回退为空串，绝不显示内部 key）。</summary>
    public static string RuleName(string id) => Known(id) ? Loc.T("advice.rule." + id + ".name") : "";

    public static string RuleTip(string id) => Known(id) ? Loc.T("advice.rule." + id + ".tip") : "";

    static bool Known(string id)
    {
        if (id.Length == 0) return false;
        foreach (var r in Rules) if (r.Id == id) return true;
        return false;
    }

    /// <summary>旧快照（v3）里存的是中文规则名 → 反查成 Id。</summary>
    public static string IdFromLegacyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        foreach (var r in Rules)
        {
            if (r.Id == name) return name;
            if (Loc.TZh("advice.rule." + r.Id + ".name") == name) return r.Id;
        }
        return "";
    }

    /// <summary>旧快照（v3）里存的是「低/中/高」或 low/medium/high → 枚举。</summary>
    public static RiskLevel RiskFromLegacy(string s)
    {
        if (string.IsNullOrEmpty(s)) return RiskLevel.Low;
        if (s == Loc.TZh("risk.high") || s.Equals("high", StringComparison.OrdinalIgnoreCase)) return RiskLevel.High;
        if (s == Loc.TZh("risk.mid") || s.Equals("medium", StringComparison.OrdinalIgnoreCase)) return RiskLevel.Mid;
        return RiskLevel.Low;
    }

    static List<AdviceRule> Build() => new()
    {
        new AdviceRule { Id="temp-user", Pattern="%LOCALAPPDATA%\\Temp", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="temp-win", Pattern="C:\\Windows\\Temp", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="recycle", Pattern="C:\\$Recycle.Bin", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="wer", Pattern="C:\\ProgramData\\Microsoft\\Windows\\WER", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="minidump", Pattern="C:\\Windows\\Minidump", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="dmp-file", Pattern="*.dmp", Kind="file", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="prefetch", Pattern="C:\\Windows\\Prefetch", Risk=RiskLevel.Low, Stars=2 },
        new AdviceRule { Id="chrome-cache", Pattern="%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="chrome-code", Pattern="%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Code Cache", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="edge-cache", Pattern="%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Cache", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="firefox-cache", Pattern="%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="npm-cache", Pattern="%LOCALAPPDATA%\\npm-cache", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="pnpm-store", Pattern="%LOCALAPPDATA%\\pnpm\\store", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="yarn-cache", Pattern="%LOCALAPPDATA%\\Yarn\\Cache", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="pip-cache", Pattern="%LOCALAPPDATA%\\pip\\Cache", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="nuget", Pattern="%USERPROFILE%\\.nuget\\packages", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="gradle", Pattern="%USERPROFILE%\\.gradle\\caches", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="maven", Pattern="%USERPROFILE%\\.m2\\repository", Risk=RiskLevel.Mid, Stars=2 },
        new AdviceRule { Id="inetcache", Pattern="%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache", Risk=RiskLevel.Low, Stars=2 },
        new AdviceRule { Id="crashdump", Pattern="%LOCALAPPDATA%\\CrashDumps", Risk=RiskLevel.Low, Stars=3 },
        new AdviceRule { Id="wu-cache", Pattern="C:\\Windows\\SoftwareDistribution\\Download", Risk=RiskLevel.Mid, Stars=2 },
        new AdviceRule { Id="winsxs", Pattern="C:\\Windows\\WinSxS", Risk=RiskLevel.Mid, Stars=2 },
        new AdviceRule { Id="logs", Pattern="C:\\Windows\\Logs", Risk=RiskLevel.Mid, Stars=2 },
        new AdviceRule { Id="downloads", Pattern="%USERPROFILE%\\Downloads", Risk=RiskLevel.Mid, Stars=2 },
        new AdviceRule { Id="pkgcache", Pattern="C:\\ProgramData\\Package Cache", Risk=RiskLevel.High, Stars=1 },
        new AdviceRule { Id="docker", Pattern="%LOCALAPPDATA%\\Docker", Risk=RiskLevel.High, Stars=1 },
        new AdviceRule { Id="wsl", Pattern="*.vhdx", Kind="file", Risk=RiskLevel.High, Stars=1 },
        new AdviceRule { Id="hiberfil", Pattern="hiberfil.sys", Kind="file", Risk=RiskLevel.Mid, Stars=2 },
        new AdviceRule { Id="pagefile", Pattern="pagefile.sys", Kind="file", Risk=RiskLevel.High, Stars=1 },
        new AdviceRule { Id="onedrive", Pattern="%USERPROFILE%\\OneDrive", Risk=RiskLevel.Mid, Stars=2 },
    };

    // ================= 通配符 =================

    /// <summary>名称通配符匹配（* 与 ?）。</summary>
    public static bool WildcardMatch(string name, string pattern)
    {
        return WildcardMatchSpan(name.AsSpan(), pattern.AsSpan());
    }

    static bool WildcardMatchSpan(ReadOnlySpan<char> s, ReadOnlySpan<char> p)
    {
        int si = 0, pi = 0, star = -1, mark = 0;
        while (si < s.Length)
        {
            if (pi < p.Length && (p[pi] == '?' || char.ToLowerInvariant(p[pi]) == char.ToLowerInvariant(s[si])))
            {
                si++; pi++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                star = pi++; mark = si;
            }
            else if (star >= 0)
            {
                pi = star + 1; si = ++mark;
            }
            else return false;
        }
        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }

    /// <summary>把规则模式解析成实际存在的路径列表（支持单个 * 段）。</summary>
    public static List<string> ResolveTargets(string pattern)
    {
        var result = new List<string>();
        string p = Env(pattern);
        var parts = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        int wi = -1;
        for (int i = 0; i < parts.Length; i++) if (parts[i].Contains('*')) { wi = i; break; }

        if (wi < 0)
        {
            if (Directory.Exists(p)) result.Add(p);
            return result;
        }

        string prefix = wi == 0 ? "" : string.Join("\\", parts.Take(wi));
        if (prefix.Length == 0) prefix = "C:\\";
        string seg = parts[wi];
        string rest = string.Join("\\", parts.Skip(wi + 1));

        foreach (var d in EnumerateDirs(prefix))
        {
            string name = Path.GetFileName(d.TrimEnd('\\'));
            if (!WildcardMatch(name, seg)) continue;
            result.Add(rest.Length > 0 ? d.TrimEnd('\\') + "\\" + rest : d);
        }
        return result;
    }

    static List<string> EnumerateDirs(string path)
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(path)) return list;
            var opt = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var d in new FileSystemEnumerable<string>(path,
                (ref FileSystemEntry e) => e.IsDirectory ? e.Directory.ToString() + "\\" + e.FileName.ToString() : "", opt))
            {
                if (d.Length > 0) list.Add(d);
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// 递归求目录占用（只读，跳过联接/符号链接）。
    /// 复用扫描器的原生遍历：结果一致，但句柄峰值从 6 万降到几百、几乎不产生托管垃圾。
    /// </summary>
    public static long MeasureDir(string path) => Scanner.MeasureTree(path, out _);

    // ================= 评估 =================

    /// <summary>精确测量：对每条目录规则直接测量目标目录大小。</summary>
    public static List<AdviceCard> Measure(Snapshot snap)
    {
        var cards = new List<AdviceCard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in Rules)
        {
            if (rule.Kind != "dir") continue;
            var targets = ResolveTargets(rule.Pattern);
            if (targets.Count == 0)
            {
                // 目标不存在或无法解析 → 用聚合数据兜底
                var approx = ApproximateFrom(snap, rule);
                if (approx != null && approx.Bytes > 0) cards.Add(approx);
                continue;
            }
            long total = 0;
            string first = "";
            foreach (var t in targets)
            {
                if (!seen.Add(t)) continue;
                if (first.Length == 0) first = t;
                total += MeasureDir(t);
            }
            if (total <= 0) continue;
            cards.Add(new AdviceCard
            {
                Path = targets.Count == 1 ? targets[0] : first,
                Bytes = total,
                RuleId = rule.Id,
                Risk = rule.Risk,
                Stars = rule.Stars,
                MatchCount = targets.Count,
            });
        }

        AddFileCards(snap, cards, seen);
        cards.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return cards;
    }

    /// <summary>仅用聚合数据评估（不碰磁盘，快；用于历史快照回看）。</summary>
    public static List<AdviceCard> Evaluate(Snapshot snap)
    {
        var cards = new List<AdviceCard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules)
        {
            if (rule.Kind != "dir") continue;
            var card = ApproximateFrom(snap, rule);
            if (card != null && card.Bytes > 0) cards.Add(card);
        }
        AddFileCards(snap, cards, seen);
        cards.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return cards;
    }

    /// <summary>用聚合目录数据近似匹配规则（规则的字面前缀落在聚合键之下时取该键）。</summary>
    static AdviceCard? ApproximateFrom(Snapshot snap, AdviceRule rule)
    {
        string target = Env(rule.Pattern);
        string literal = target;
        int star = literal.IndexOf('*');
        if (star >= 0) literal = literal.Substring(0, star).TrimEnd('\\');

        long sum = 0;
        int count = 0;
        string? best = null;
        foreach (var d in snap.Dirs)
        {
            bool under = literal.Length > 0 && d.Path.StartsWith(literal, StringComparison.OrdinalIgnoreCase);
            if (!under) continue;
            sum += d.Bytes;
            count++;
            if (best == null || d.Path.Length < best.Length) best = d.Path;
        }
        if (count == 0) return null;
        return new AdviceCard
        {
            Path = best ?? literal,
            Bytes = sum,
            RuleId = rule.Id,
            Risk = rule.Risk,
            Stars = rule.Stars,
            Approximate = true,
            MatchCount = count,
        };
    }

    static void AddFileCards(Snapshot snap, List<AdviceCard> cards, HashSet<string> seen)
    {
        foreach (var rule in Rules)
        {
            if (rule.Kind != "file") continue;
            foreach (var f in snap.TopFiles)
            {
                string name = Path.GetFileName(f.Path);
                if (!WildcardMatch(name, rule.Pattern)) continue;
                if (!seen.Add(f.Path)) continue;
                cards.Add(new AdviceCard
                {
                    Path = f.Path, Bytes = f.Bytes, RuleId = rule.Id, Risk = rule.Risk,
                    Stars = rule.Stars, IsFile = true,
                });
            }
            foreach (var s in snap.SysFiles)
            {
                string name = Path.GetFileName(s.Path);
                if (!WildcardMatch(name, rule.Pattern)) continue;
                if (!seen.Add(s.Path)) continue;
                cards.Add(new AdviceCard
                {
                    Path = s.Path, Bytes = s.Bytes, RuleId = rule.Id, Risk = rule.Risk,
                    Stars = rule.Stars, IsFile = true, IsRoot = true,
                });
            }
        }
    }
}
