using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace CDiskWatch;

public sealed class AdviceRule
{
    public string Id = "";
    public string Name = "";
    /// <summary>路径通配符（支持 %ENV%，* 匹配任意字符，路径分隔符用反斜杠）。</summary>
    public string Pattern = "";
    /// <summary>低 / 中 / 高</summary>
    public string Risk = "低";
    /// <summary>推荐度 1~3</summary>
    public int Stars = 2;
    public string Tip = "";
    /// <summary>file = 匹配文件名；dir = 匹配目录路径。</summary>
    public string Kind = "dir";
}

public sealed class AdviceCard
{
    public string Path = "";
    public long Bytes;
    public string Rule = "";
    public string Risk = "低";
    public int Stars = 2;
    public string Tip = "";
    public bool IsFile;
    public bool IsRoot;
    public bool Approximate;
    public int MatchCount;
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

    static List<AdviceRule> Build() => new()
    {
        new AdviceRule { Id="temp-user", Name="用户临时目录", Pattern="%LOCALAPPDATA%\\Temp", Risk="低", Stars=3,
            Tip="关闭正在运行的程序后，可删除该目录内的文件（系统会自动跳过占用中的文件）。" },
        new AdviceRule { Id="temp-win", Name="Windows 临时目录", Pattern="C:\\Windows\\Temp", Risk="低", Stars=3,
            Tip="可删除其中的临时文件；部分占用中的文件会跳过。" },
        new AdviceRule { Id="recycle", Name="回收站", Pattern="C:\\$Recycle.Bin", Risk="低", Stars=3,
            Tip="清空回收站即可释放（资源管理器右键回收站 → 清空回收站）。" },
        new AdviceRule { Id="wer", Name="Windows 错误报告", Pattern="C:\\ProgramData\\Microsoft\\Windows\\WER", Risk="低", Stars=3,
            Tip="崩溃报告缓存，可直接删除。" },
        new AdviceRule { Id="minidump", Name="小型崩溃转储", Pattern="C:\\Windows\\Minidump", Risk="低", Stars=3,
            Tip="蓝屏小型转储，删除不影响系统运行。" },
        new AdviceRule { Id="dmp-file", Name="内存转储文件", Pattern="*.dmp", Kind="file", Risk="低", Stars=3,
            Tip="崩溃内存转储，删除会失去该次崩溃的分析现场。" },
        new AdviceRule { Id="prefetch", Name="预读文件", Pattern="C:\\Windows\\Prefetch", Risk="低", Stars=2,
            Tip="可清空，系统会按需重建。" },
        new AdviceRule { Id="chrome-cache", Name="Chrome 缓存", Pattern="%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache", Risk="低", Stars=3,
            Tip="关闭 Chrome 后可删除；下次浏览会重新生成。" },
        new AdviceRule { Id="chrome-code", Name="Chrome 代码缓存", Pattern="%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Code Cache", Risk="低", Stars=3,
            Tip="关闭 Chrome 后可删除。" },
        new AdviceRule { Id="edge-cache", Name="Edge 缓存", Pattern="%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Cache", Risk="低", Stars=3,
            Tip="关闭 Edge 后可删除。" },
        new AdviceRule { Id="firefox-cache", Name="Firefox 缓存", Pattern="%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2", Risk="低", Stars=3,
            Tip="关闭 Firefox 后可删除。" },
        new AdviceRule { Id="npm-cache", Name="npm 缓存", Pattern="%LOCALAPPDATA%\\npm-cache", Risk="低", Stars=3,
            Tip="可执行 npm cache clean --force，或直接删除该目录。" },
        new AdviceRule { Id="pnpm-store", Name="pnpm 全局仓库", Pattern="%LOCALAPPDATA%\\pnpm\\store", Risk="低", Stars=3,
            Tip="可执行 pnpm store prune 清理未引用的包。" },
        new AdviceRule { Id="yarn-cache", Name="Yarn 缓存", Pattern="%LOCALAPPDATA%\\Yarn\\Cache", Risk="低", Stars=3,
            Tip="可执行 yarn cache clean。" },
        new AdviceRule { Id="pip-cache", Name="pip 缓存", Pattern="%LOCALAPPDATA%\\pip\\Cache", Risk="低", Stars=3,
            Tip="可执行 pip cache purge。" },
        new AdviceRule { Id="nuget", Name="NuGet 包缓存", Pattern="%USERPROFILE%\\.nuget\\packages", Risk="低", Stars=3,
            Tip="可执行 dotnet nuget locals all --clear。" },
        new AdviceRule { Id="gradle", Name="Gradle 缓存", Pattern="%USERPROFILE%\\.gradle\\caches", Risk="低", Stars=3,
            Tip="可删除，下次构建会重新下载。" },
        new AdviceRule { Id="maven", Name="Maven 仓库", Pattern="%USERPROFILE%\\.m2\\repository", Risk="中", Stars=2,
            Tip="可删除不再使用的依赖；重新构建会重新下载。" },
        new AdviceRule { Id="inetcache", Name="系统网页缓存", Pattern="%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache", Risk="低", Stars=2,
            Tip="可删除。" },
        new AdviceRule { Id="crashdump", Name="应用崩溃转储", Pattern="%LOCALAPPDATA%\\CrashDumps", Risk="低", Stars=3,
            Tip="可删除。" },
        new AdviceRule { Id="wu-cache", Name="Windows 更新缓存", Pattern="C:\\Windows\\SoftwareDistribution\\Download", Risk="中", Stars=2,
            Tip="建议用「设置 → 系统 → 存储 → 临时文件」清理，不建议直接手删。" },
        new AdviceRule { Id="winsxs", Name="组件存储 WinSxS", Pattern="C:\\Windows\\WinSxS", Risk="中", Stars=2,
            Tip="请以管理员身份自行运行 DISM /Online /Cleanup-Image /StartComponentCleanup（本软件不会代执行）。" },
        new AdviceRule { Id="logs", Name="系统日志", Pattern="C:\\Windows\\Logs", Risk="中", Stars=2,
            Tip="可清理旧日志；建议保留近期日志用于排查。" },
        new AdviceRule { Id="downloads", Name="下载目录", Pattern="%USERPROFILE%\\Downloads", Risk="中", Stars=2,
            Tip="人工确认后移走或删除大文件。" },
        new AdviceRule { Id="pkgcache", Name="安装包缓存", Pattern="C:\\ProgramData\\Package Cache", Risk="高", Stars=1,
            Tip="部分软件的修复/卸载依赖它，删除可能导致无法卸载，务必谨慎。" },
        new AdviceRule { Id="docker", Name="Docker 数据", Pattern="%LOCALAPPDATA%\\Docker", Risk="高", Stars=1,
            Tip="请在 Docker Desktop 内清理镜像/容器，不要手删。" },
        new AdviceRule { Id="wsl", Name="WSL 虚拟磁盘", Pattern="*.vhdx", Kind="file", Risk="高", Stars=1,
            Tip="请在 WSL 内用 wsl --manage <distro> --set-sparse 或导出后清理，不要手删。" },
        new AdviceRule { Id="hiberfil", Name="休眠文件 hiberfil.sys", Pattern="hiberfil.sys", Kind="file", Risk="中", Stars=2,
            Tip="若不需要休眠/快速启动，可由管理员执行 powercfg /h off（本软件不会代执行）。" },
        new AdviceRule { Id="pagefile", Name="虚拟内存 pagefile.sys", Pattern="pagefile.sys", Kind="file", Risk="高", Stars=1,
            Tip="请在「系统属性 → 高级 → 虚拟内存」中调整，不建议直接删除。" },
        new AdviceRule { Id="onedrive", Name="OneDrive 本地占位", Pattern="%USERPROFILE%\\OneDrive", Risk="中", Stars=2,
            Tip="可右键文件选择「释放空间」（仅在线保留），不要直接删除同步中的文件。" },
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
                Rule = rule.Name,
                Risk = rule.Risk,
                Stars = rule.Stars,
                Tip = rule.Tip,
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
            Rule = rule.Name,
            Risk = rule.Risk,
            Stars = rule.Stars,
            Tip = rule.Tip,
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
                    Path = f.Path, Bytes = f.Bytes, Rule = rule.Name, Risk = rule.Risk,
                    Stars = rule.Stars, Tip = rule.Tip, IsFile = true,
                });
            }
            foreach (var s in snap.SysFiles)
            {
                string name = Path.GetFileName(s.Path);
                if (!WildcardMatch(name, rule.Pattern)) continue;
                if (!seen.Add(s.Path)) continue;
                cards.Add(new AdviceCard
                {
                    Path = s.Path, Bytes = s.Bytes, Rule = rule.Name, Risk = rule.Risk,
                    Stars = rule.Stars, Tip = rule.Tip, IsFile = true, IsRoot = true,
                });
            }
        }
    }
}
