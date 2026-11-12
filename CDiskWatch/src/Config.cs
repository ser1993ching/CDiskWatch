using System.Text.Json;
using System.Text.Json.Serialization;

namespace CDiskWatch;

public class AppConfig
{
    public string Drive { get; set; } = "C";
    /// <summary>界面语言：auto（跟随系统）/ zh-CN / ja-JP / en-US，或外部语言包代码。</summary>
    public string Lang { get; set; } = "auto";
    public string DailyScanTime { get; set; } = "02:00";
    public bool ScanOnStartup { get; set; } = true;
    public int MaxDirLevel { get; set; } = 4;
    public int TopFileCount { get; set; } = 100;
    public bool AutoStart { get; set; }
    public bool StartMinimized { get; set; } = true;

    public long FreeWarnGB { get; set; } = 20;
    public long FreeCriticalGB { get; set; } = 10;
    public double FreeWarnPct { get; set; } = 15;
    public double FreeCriticalPct { get; set; } = 8;
    public long DailyGrowthWarnGB { get; set; } = 5;
    public long DailyGrowthCriticalGB { get; set; } = 20;
    public long CategoryGrowthGB { get; set; } = 2;
    public long SingleFileGB { get; set; } = 1;
    public long SystemFileChangeGB { get; set; } = 1;
    public int CooldownHours { get; set; } = 6;

    public List<string> ExcludePaths { get; set; } = new();

    public AlertSettings ToAlertSettings() => new()
    {
        FreeWarnBytes = FreeWarnGB << 30,
        FreeCriticalBytes = FreeCriticalGB << 30,
        FreeWarnRatio = FreeWarnPct / 100.0,
        FreeCriticalRatio = FreeCriticalPct / 100.0,
        DailyGrowthWarnBytes = DailyGrowthWarnGB << 30,
        DailyGrowthCriticalBytes = DailyGrowthCriticalGB << 30,
        DailyGrowthWarnRatio = 0.10,
        CategoryGrowthBytes = CategoryGrowthGB << 30,
        SingleFileBytes = SingleFileGB << 30,
        SystemFileChangeBytes = SystemFileChangeGB << 30,
        CooldownHours = CooldownHours,
    };
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(LangPackFile))]
[JsonSerializable(typeof(ReportJson))]
public partial class JsonCtx : JsonSerializerContext { }

public static class Paths
{
    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CDiskWatch");

    public static string ConfigPath => Path.Combine(Dir, "config.json");

    public static void EnsureDir() => Directory.CreateDirectory(Dir);
}

public static class ConfigStore
{
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigPath))
            {
                var c = JsonSerializer.Deserialize(File.ReadAllText(Paths.ConfigPath), JsonCtx.Default.AppConfig);
                if (c != null) return c;
            }
        }
        catch { }
        return new AppConfig();
    }

    public static void Save(AppConfig c)
    {
        try
        {
            Paths.EnsureDir();
            string tmp = Paths.ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(c, JsonCtx.Default.AppConfig));
            File.Move(tmp, Paths.ConfigPath, overwrite: true);
        }
        catch { }
    }
}
