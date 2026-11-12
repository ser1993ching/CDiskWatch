using System.Text.Json;
using System.Text.Json.Serialization;

namespace CDiskWatch;

/// <summary>
/// <c>--report --format json</c> 的结构化输出。
/// 约定：<c>id</c>/<c>rule</c>/<c>risk</c>/<c>level</c> 是**语言无关的稳定代码**（脚本可依赖），
/// <c>name</c>/<c>tip</c>/<c>kind</c>/<c>verdict</c> 等是给人看的文本（跟随界面语言）。
/// </summary>
public sealed class ReportJson
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("generatedAt")] public string GeneratedAt { get; set; } = "";
    [JsonPropertyName("lang")] public string Lang { get; set; } = "";
    [JsonPropertyName("drive")] public string Drive { get; set; } = "";
    [JsonPropertyName("snapshotTime")] public string SnapshotTime { get; set; } = "";
    [JsonPropertyName("baseline")] public bool Baseline { get; set; }
    [JsonPropertyName("space")] public JsonSpace? Space { get; set; }
    [JsonPropertyName("systemFiles")] public List<JsonSysFile> SystemFiles { get; set; } = new();
    [JsonPropertyName("categories")] public List<JsonCat> Categories { get; set; } = new();
    [JsonPropertyName("topDirs")] public List<JsonDir> TopDirs { get; set; } = new();
    [JsonPropertyName("topFiles")] public List<JsonFile> TopFiles { get; set; } = new();
    [JsonPropertyName("diff")] public JsonDiff? Diff { get; set; }
    [JsonPropertyName("alerts")] public List<JsonAlert> Alerts { get; set; } = new();
    [JsonPropertyName("advice")] public List<JsonAdvice> Advice { get; set; } = new();
    [JsonPropertyName("adviceTotalBytes")] public long AdviceTotalBytes { get; set; }
}

public sealed class JsonSpace
{
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("usedBytes")] public long UsedBytes { get; set; }
    [JsonPropertyName("freeBytes")] public long FreeBytes { get; set; }
    [JsonPropertyName("usedRatio")] public double UsedRatio { get; set; }
    [JsonPropertyName("fileCount")] public long FileCount { get; set; }
    [JsonPropertyName("dirCount")] public long DirCount { get; set; }
    [JsonPropertyName("logicalBytes")] public long LogicalBytes { get; set; }
    [JsonPropertyName("unaccountedBytes")] public long UnaccountedBytes { get; set; }
    [JsonPropertyName("hardlinkBytes")] public long HardlinkBytes { get; set; }
    [JsonPropertyName("shadowUsedBytes")] public long ShadowUsedBytes { get; set; }
    [JsonPropertyName("cacheBytes")] public long CacheBytes { get; set; }
    [JsonPropertyName("tempBytes")] public long TempBytes { get; set; }
}

public sealed class JsonSysFile
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("kindCode")] public string KindCode { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

public sealed class JsonCat
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("count")] public long Count { get; set; }
    [JsonPropertyName("share")] public double Share { get; set; }
    [JsonPropertyName("color")] public string Color { get; set; } = "";
}

public sealed class JsonDir
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("files")] public long Files { get; set; }
}

public sealed class JsonFile
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("modified")] public string Modified { get; set; } = "";
}

public sealed class JsonDiff
{
    [JsonPropertyName("usedDelta")] public long UsedDelta { get; set; }
    [JsonPropertyName("freeDelta")] public long FreeDelta { get; set; }
    [JsonPropertyName("verdict")] public string Verdict { get; set; } = "";
    [JsonPropertyName("growing")] public List<JsonDirDelta> Growing { get; set; } = new();
    [JsonPropertyName("shrinking")] public List<JsonDirDelta> Shrinking { get; set; } = new();
    [JsonPropertyName("categories")] public List<JsonCatDelta> Categories { get; set; } = new();
    [JsonPropertyName("newFiles")] public List<JsonFile> NewFiles { get; set; } = new();
    [JsonPropertyName("sysChanged")] public List<JsonSysFile> SysChanged { get; set; } = new();
    [JsonPropertyName("unaccountedDelta")] public long UnaccountedDelta { get; set; }
}

public sealed class JsonDirDelta
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("delta")] public long Delta { get; set; }
    [JsonPropertyName("nowBytes")] public long NowBytes { get; set; }
    [JsonPropertyName("isNew")] public bool IsNew { get; set; }
}

public sealed class JsonCatDelta
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("delta")] public long Delta { get; set; }
    [JsonPropertyName("nowBytes")] public long NowBytes { get; set; }
}

public sealed class JsonAlert
{
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("level")] public string Level { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
}

public sealed class JsonAdvice
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("rule")] public string Rule { get; set; } = "";
    [JsonPropertyName("ruleName")] public string RuleName { get; set; } = "";
    [JsonPropertyName("risk")] public string Risk { get; set; } = "";
    [JsonPropertyName("stars")] public int Stars { get; set; }
    [JsonPropertyName("tip")] public string Tip { get; set; } = "";
    [JsonPropertyName("isFile")] public bool IsFile { get; set; }
    [JsonPropertyName("approximate")] public bool Approximate { get; set; }
}

public static class ReportJsonBuilder
{
    /// <summary>构造结构化报告（同时复用文本报告的同一条数据链路）。</summary>
    public static ReportJson Build(Snapshot? s, DiffResult? diff, List<AdviceCard> advice, List<AlertRecord> alerts, bool baseline)
    {
        var r = new ReportJson
        {
            GeneratedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            Lang = Loc.Code,
            Baseline = baseline,
        };
        if (s == null) return r;

        r.Drive = s.Drive;
        r.SnapshotTime = s.Time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
        r.Space = new JsonSpace
        {
            TotalBytes = s.TotalBytes,
            UsedBytes = s.UsedBytes,
            FreeBytes = s.FreeBytes,
            UsedRatio = Math.Round(s.UsedRatio, 6),
            FileCount = s.FileCount,
            DirCount = s.DirCount,
            LogicalBytes = s.LogicalBytes,
            UnaccountedBytes = s.UnaccountedBytes,
            HardlinkBytes = s.HardlinkBytes,
            ShadowUsedBytes = s.ShadowUsedBytes,
            CacheBytes = s.CacheBytes,
            TempBytes = s.TempBytes,
        };

        foreach (var f in s.SysFiles.OrderByDescending(x => x.Bytes))
            r.SystemFiles.Add(new JsonSysFile { Path = f.Path, Bytes = f.Bytes, KindCode = f.KindCode, Kind = f.Kind, Note = f.Note });

        long catTotal = 0;
        for (int i = 0; i < Cat.Count; i++) catTotal += s.CatBytes[i];
        for (int i = 0; i < Cat.Count; i++)
        {
            if (s.CatBytes[i] <= 0) continue;
            r.Categories.Add(new JsonCat
            {
                Id = Cat.Ids[i],
                Name = Cat.Name(i),
                Bytes = s.CatBytes[i],
                Count = s.CatCounts[i],
                Share = catTotal > 0 ? Math.Round((double)s.CatBytes[i] / catTotal, 6) : 0,
                Color = Cat.HexColor(i),
            });
        }
        r.Categories.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

        foreach (var d in s.Rollups.Where(x => x.Level <= 3).Take(30))
            r.TopDirs.Add(new JsonDir { Path = d.Path, Bytes = d.Bytes, Level = d.Level, Files = d.FileCount });

        foreach (var f in s.TopFiles)
            r.TopFiles.Add(new JsonFile { Path = f.Path, Bytes = f.Bytes, Modified = f.Modified.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture) });

        if (diff != null)
        {
            var dj = new JsonDiff
            {
                UsedDelta = diff.UsedDelta,
                FreeDelta = diff.FreeDelta,
                Verdict = diff.Verdict,
                UnaccountedDelta = diff.UnaccountedDelta,
            };
            foreach (var g in diff.Growing.Take(20))
                dj.Growing.Add(new JsonDirDelta { Path = g.Path, Delta = g.Delta, NowBytes = g.NewBytes, IsNew = g.IsNew });
            foreach (var g in diff.Shrinking.Take(20))
                dj.Shrinking.Add(new JsonDirDelta { Path = g.Path, Delta = g.Delta, NowBytes = g.NewBytes, IsNew = g.IsNew });
            foreach (var c in diff.Cats.Where(x => x.Delta != 0))
                dj.Categories.Add(new JsonCatDelta { Id = Cat.Ids[c.Index], Name = c.Name, Delta = c.Delta, NowBytes = c.NewBytes });
            foreach (var f in diff.NewFiles.Take(20))
                dj.NewFiles.Add(new JsonFile { Path = f.Path, Bytes = f.Bytes, Modified = f.Modified.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture) });
            foreach (var f in diff.SysChanged)
                dj.SysChanged.Add(new JsonSysFile { Path = f.Path, Bytes = f.Bytes, KindCode = f.KindCode, Kind = f.Kind, Note = f.Note });
            r.Diff = dj;
        }

        foreach (var a in alerts)
            r.Alerts.Add(new JsonAlert { Time = a.Time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture), Level = a.LevelCode, Key = a.Key, Title = a.Title, Detail = a.Detail });

        foreach (var c in advice)
        {
            r.Advice.Add(new JsonAdvice
            {
                Path = c.Path, Bytes = c.Bytes, Rule = c.RuleId, RuleName = c.RuleName,
                Risk = c.RiskCode, Stars = c.Stars, Tip = c.Tip,
                IsFile = c.IsFile, Approximate = c.Approximate,
            });
            r.AdviceTotalBytes += c.Bytes;
        }

        return r;
    }

    /// <summary>报告同时给人看和给脚本看，中文不转义成 Unicode 转义序列。</summary>
    /// <remarks>resolver 就是源生成的 <see cref="JsonCtx"/>（无反射），因此裁剪发布同样安全；
    /// 分析器无法识别这一点会报 IL2026，这里显式豁免。</remarks>
    static readonly JsonSerializerOptions Opt = new(JsonCtx.Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = JsonCtx.Default,   // 仍走源生成的元数据（无反射）
    };

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "TypeInfoResolver is the source-generated JsonCtx (no reflection), so trimming is safe.")]
    public static string Serialize(ReportJson r) => JsonSerializer.Serialize(r, Opt);
}
