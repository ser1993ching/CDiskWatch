using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CDiskWatch;

/// <summary>
/// 多语言门面：静态标签用 <see cref="T"/>，带参数的句子一律走 <see cref="F"/>（占位符 {0}/{1}…，禁止手工拼接）。
/// 文案的唯一来源是 tools/lang/lang.tsv，经 tools/gen_lang.py 生成 Tables.g.cs。
/// </summary>
public static class Loc
{
    public const string ZhCn = "zh-CN";
    public const string JaJp = "ja-JP";
    public const string EnUs = "en-US";

    static string _code = ZhCn;
    static Dictionary<string, string> _map = Build(Tables.ZhCn);
    static Dictionary<string, string> _zh = _map;
    static Dictionary<string, string>? _pack;
    static readonly HashSet<string> _missing = new(StringComparer.Ordinal);

    /// <summary>当前语言代码。</summary>
    public static string Code => _code;

    /// <summary>界面字体：日文用 Yu Gothic UI，英文用 Segoe UI，中文用微软雅黑（三者都覆盖拉丁字符）。</summary>
    public static string FontFace =>
        _code == EnUs ? "Segoe UI" : _code == JaJp ? "Yu Gothic UI" : "Microsoft YaHei UI";

    /// <summary>长日期格式（不用 culture 的月份名，避免 InvariantGlobalization 下输出英文月份）。</summary>
    public static string DateFullPattern => "yyyy-MM-dd HH:mm:ss";

    /// <summary>短日期格式：英文环境用完整年份，中/日沿用 MM-dd。</summary>
    public static string DateShortPattern => _code == EnUs ? "yyyy-MM-dd HH:mm" : "MM-dd HH:mm";

    /// <summary>本次运行中缺失的 key（自检会断言为空）。</summary>
    public static IReadOnlyCollection<string> Missing => _missing;

    public static int KeyCount => _map.Count;

    public static IReadOnlyList<string> Codes => Tables.Codes;

    /// <summary>解析语言代码：auto/null → 跟随系统；zh*/ja*/en* → 对应内置语言；其它保持原样（交给外部语言包）。</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return DetectSystem();
        string c = code.Trim().Replace('_', '-');
        if (c.Equals(ZhCn, StringComparison.OrdinalIgnoreCase) || c.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return ZhCn;
        if (c.Equals(JaJp, StringComparison.OrdinalIgnoreCase) || c.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return JaJp;
        if (c.Equals(EnUs, StringComparison.OrdinalIgnoreCase) || c.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return EnUs;
        return c;
    }

    /// <summary>系统界面语言（只用 Win32，不依赖 ICU）。</summary>
    public static string DetectSystem()
    {
        try
        {
            ushort lang = Win32.GetUserDefaultUILanguage();
            switch (lang & 0x3FF)
            {
                case 0x04: return ZhCn;   // LANG_CHINESE
                case 0x11: return JaJp;   // LANG_JAPANESE
                case 0x09: return EnUs;   // LANG_ENGLISH
                default: return EnUs;
            }
        }
        catch { return ZhCn; }
    }

    /// <summary>载入语言：内置表 + 可选的外部语言包覆盖。</summary>
    public static void Load(string? wanted)
    {
        _code = Normalize(wanted);
        _map = _code switch
        {
            JaJp => Build(Tables.JaJp),
            EnUs => Build(Tables.EnUs),
            _ => _zh,
        };
        _pack = LangPack.TryLoad(_code, _zh, _map);
        _missing.Clear();
    }

    /// <summary>静态文案。</summary>
    public static string T(string key)
    {
        if (_pack != null && _pack.TryGetValue(key, out var s)) return s;
        if (_map.TryGetValue(key, out var v)) return v;
        if (_zh.TryGetValue(key, out var zh)) return zh;   // 目标语言缺该 key 时回退中文，绝不显示 key
        _missing.Add(key);
        return key;
    }

    /// <summary>带占位符的句子：模板里的 {0}/{1}… 用 args 填充。</summary>
    public static string F(string key, params object?[] args)
    {
        string tpl = T(key);
        if (args.Length == 0) return tpl;
        try { return string.Format(CultureInfo.InvariantCulture, tpl, args); }
        catch (FormatException) { return tpl; }
    }

    /// <summary>固定读简体中文（用于解析旧快照里的中文字段）。</summary>
    public static string TZh(string key) => _zh.TryGetValue(key, out var v) ? v : key;

    /// <summary>语言显示名（用于文档/日志）。</summary>
    public static string DisplayName => _code switch
    {
        ZhCn => "简体中文",
        JaJp => "日本語",
        EnUs => "English",
        _ => _code,
    };

    static Dictionary<string, string> Build(string[] pairs)
    {
        var d = new Dictionary<string, string>(pairs.Length / 2, StringComparer.Ordinal);
        for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
        return d;
    }
}

/// <summary>外部语言包（%AppData%\CDiskWatch\lang\&lt;code&gt;.json），用于覆盖/扩充内置文案。</summary>
public sealed class LangPackFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("font")] public string? Font { get; set; }
    [JsonPropertyName("strings")] public Dictionary<string, string> Strings { get; set; } = new();
}

public static class LangPack
{
    public const int SchemaVersion = 1;

    public static string Dir => Path.Combine(Paths.Dir, "lang");

    public static string PathFor(string code) => Path.Combine(Dir, code + ".json");

    /// <summary>
    /// 读取语言包并与内置表合并。任何一条校验失败（占位符不一致/空值）都会被丢弃，
    /// 保证界面永远不会因为劣质译文而崩溃或排版爆掉。
    /// </summary>
    public static Dictionary<string, string>? TryLoad(string code, Dictionary<string, string> zh, Dictionary<string, string> builtin)
    {
        try
        {
            string path = PathFor(code);
            if (!File.Exists(path)) return null;
            var pack = JsonSerializer.Deserialize(File.ReadAllText(path), JsonCtx.Default.LangPackFile);
            if (pack == null || pack.Schema != SchemaVersion || pack.Strings.Count == 0) return null;

            var merged = new Dictionary<string, string>(builtin, StringComparer.Ordinal);
            foreach (var kv in pack.Strings)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                string? reference = zh.TryGetValue(kv.Key, out var z) ? z : null;
                if (reference != null && !SamePlaceholders(reference, kv.Value)) continue;
                merged[kv.Key] = kv.Value;
            }
            return merged;
        }
        catch { return null; }
    }

    internal static bool SamePlaceholders(string a, string b)
    {
        return Placeholders(a).SetEquals(Placeholders(b));
    }

    internal static HashSet<int> Placeholders(string s)
    {
        var set = new HashSet<int>();
        for (int i = 0; i < s.Length - 2; i++)
        {
            if (s[i] != '{') continue;
            int j = i + 1, n = 0;
            while (j < s.Length && char.IsAsciiDigit(s[j])) { n = n * 10 + (s[j] - '0'); j++; }
            if (j > i + 1 && j < s.Length && (s[j] == '}' || s[j] == ':')) set.Add(n);
        }
        return set;
    }
}
