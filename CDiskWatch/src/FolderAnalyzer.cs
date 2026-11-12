namespace CDiskWatch;

public enum FolderKind : byte { DevCache, BuildOutput, ChatData, AppData, Extensions, Personal, Downloads }
public enum FolderRisk : byte { Medium, High, Critical }

public sealed class FolderUsage
{
    public string Path = "";
    public long Bytes;
    public FolderKind Kind;
    public FolderRisk Risk;
    public int UnreadableDirs;
}

/// <summary>只读识别常见大文件夹；大小测量复用低句柄的原生扫描器。</summary>
public static class FolderAnalyzer
{
    const long MinBytes = 64L * 1024 * 1024;
    const int MaxCandidates = 200, MaxProjectDirs = 600, MaxDepth = 4;

    static readonly HashSet<string> BuildFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".venv", "venv", "bin", "obj", "target", "dist", ".next", ".nuxt", ".turbo"
    };
    static readonly HashSet<string> WorkspaceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "source", "src", "dev", "projects", "project", "workspace", "work", "repos", "github"
    };
    static readonly HashSet<string> ChatFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WeChat Files", "xwechat_files", "Tencent Files", "QQ Files", "微信 Files"  // lang:keep 磁盘上的真实目录名
    };

    public static FolderRisk RiskOf(FolderKind kind) => kind switch
    {
        FolderKind.ChatData => FolderRisk.Critical,
        FolderKind.Personal or FolderKind.AppData or FolderKind.Extensions or FolderKind.Downloads => FolderRisk.High,
        _ => FolderRisk.Medium,
    };

    public static List<FolderUsage> Scan(string drive)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string downloads = Path.Combine(profile, "Downloads");
        var candidates = new Dictionary<string, FolderKind>(StringComparer.OrdinalIgnoreCase);

        void Add(string path, FolderKind kind)
        {
            if (string.IsNullOrWhiteSpace(path) || candidates.Count >= MaxCandidates) return;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (full.Length >= 3 && Directory.Exists(full)) candidates.TryAdd(full, kind);
            }
            catch { }
        }

        Add(Path.Combine(profile, ".nuget", "packages"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".npm"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".pnpm-store"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".cache", "pip"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".cache", "uv"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".cargo", "registry"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".gradle", "caches"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".m2", "repository"), FolderKind.DevCache);
        Add(Path.Combine(profile, ".vscode", "extensions"), FolderKind.Extensions);
        Add(Path.Combine(profile, ".vscode-insiders", "extensions"), FolderKind.Extensions);
        Add(Path.Combine(profile, ".vscode-server", "extensions"), FolderKind.Extensions);
        Add(Path.Combine(local, "npm-cache"), FolderKind.DevCache);
        Add(Path.Combine(local, "pip", "Cache"), FolderKind.DevCache);
        Add(Path.Combine(local, "Yarn", "Cache"), FolderKind.DevCache);
        Add(Path.Combine(local, "NuGet", "v3-cache"), FolderKind.DevCache);

        foreach (string name in ChatFolderNames) Add(Path.Combine(docs, name), FolderKind.ChatData);
        Add(Path.Combine(roaming, "Tencent", "WeChat"), FolderKind.ChatData);
        Add(Path.Combine(local, "Tencent", "WeChat"), FolderKind.ChatData);
        Add(Path.Combine(roaming, "Tencent", "QQ"), FolderKind.ChatData);
        Add(Path.Combine(local, "Tencent", "QQ"), FolderKind.ChatData);

        Add(Path.Combine(roaming, "Discord"), FolderKind.AppData);
        Add(Path.Combine(local, "Discord"), FolderKind.AppData);
        Add(Path.Combine(roaming, "Slack"), FolderKind.AppData);
        Add(Path.Combine(local, "Slack"), FolderKind.AppData);
        Add(Path.Combine(roaming, "Telegram Desktop"), FolderKind.AppData);
        Add(Path.Combine(roaming, "DingTalk"), FolderKind.AppData);
        Add(Path.Combine(local, "DingTalk"), FolderKind.AppData);
        Add(Path.Combine(roaming, "Feishu"), FolderKind.AppData);
        Add(Path.Combine(local, "Feishu"), FolderKind.AppData);
        Add(Path.Combine(roaming, "Lark"), FolderKind.AppData);
        Add(Path.Combine(local, "Lark"), FolderKind.AppData);
        Add(Path.Combine(local, "Google", "Chrome", "User Data"), FolderKind.AppData);
        Add(Path.Combine(local, "Microsoft", "Edge", "User Data"), FolderKind.AppData);
        Add(Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"), FolderKind.AppData);
        Add(Path.Combine(local, "Docker", "wsl"), FolderKind.AppData);
        Add(Path.Combine(local, "Android", "Sdk"), FolderKind.AppData);
        Add(Path.Combine(profile, ".android", "avd"), FolderKind.AppData);
        AddAppDataChildren(Path.Combine(local, "Packages"), Add);
        Add(Path.Combine(roaming, "Code", "Cache"), FolderKind.DevCache);
        Add(Path.Combine(roaming, "Code", "CachedData"), FolderKind.DevCache);
        Add(Path.Combine(roaming, "Code", "User", "workspaceStorage"), FolderKind.AppData);

        Add(downloads, FolderKind.Downloads);

        string[] workspaces =
        {
            Path.Combine(profile, "source"), Path.Combine(profile, "src"),
            Path.Combine(profile, "dev"), Path.Combine(profile, "projects"),
            Path.Combine(profile, "workspace"), Path.Combine(profile, "work"),
            Path.Combine(profile, "repos"), Path.Combine(profile, "GitHub"),
            Path.Combine(docs, "GitHub"), Path.Combine(docs, "Projects"),
            Path.Combine(docs, "source"), Path.Combine(docs, "workspace"),
            Path.Combine(desktop, "Projects"),
        };
        foreach (string root in workspaces) DiscoverBuildFolders(root, Add);
        AddPersonalChildren(docs, Add);
        AddPersonalChildren(desktop, Add);

        string driveRoot = drive.Trim().TrimEnd(':', '\\') + ":\\";
        var result = new List<FolderUsage>();
        foreach (var candidate in candidates)
        {
            if (result.Count >= MaxCandidates) break;
            string root;
            try { root = Path.GetPathRoot(candidate.Key) ?? ""; }
            catch { continue; }
            if (!root.Equals(driveRoot, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                long bytes = Scanner.MeasureTree(candidate.Key, out int unreadable);
                if (bytes < MinBytes) continue;
                result.Add(new FolderUsage
                {
                    Path = candidate.Key, Bytes = bytes, Kind = candidate.Value,
                    Risk = RiskOf(candidate.Value), UnreadableDirs = unreadable,
                });
            }
            catch { }
        }
        result.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        if (result.Count > 100) result.RemoveRange(100, result.Count - 100);
        return result;
    }

    static void AddPersonalChildren(string root, Action<string, FolderKind> add)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        int count = 0;
        try
        {
            foreach (string path in Directory.EnumerateDirectories(root))
            {
                if (++count > 60) break;
                string name = Path.GetFileName(path);
                if (ChatFolderNames.Contains(name) || WorkspaceNames.Contains(name)) continue;
                try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue; }
                catch { continue; }
                add(path, FolderKind.Personal);
            }
        }
        catch { }
    }

    static void AddAppDataChildren(string root, Action<string, FolderKind> add)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        var paths = new List<string>();
        try
        {
            foreach (string path in Directory.EnumerateDirectories(root))
            {
                paths.Add(path);
                if (paths.Count >= 120) break;
            }
        }
        catch { }

        // 先识别聊天软件包，避免它们被普通应用数据的数量上限挤掉。
        int ordinary = 0;
        foreach (string path in paths)
        {
            string name = Path.GetFileName(path);
            bool chat = name.Contains("wechat", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("mobileqq", StringComparison.OrdinalIgnoreCase)
                     || name.StartsWith("qq", StringComparison.OrdinalIgnoreCase)
                     || name.Contains(".qq", StringComparison.OrdinalIgnoreCase);
            if (!chat && ++ordinary > 40) continue;
            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue; }
            catch { continue; }
            add(path, chat ? FolderKind.ChatData : FolderKind.AppData);
        }
    }

    static void DiscoverBuildFolders(string root, Action<string, FolderKind> add)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        var pending = new Stack<(string path, int depth)>();
        pending.Push((root, 0));
        int visited = 0;
        while (pending.Count > 0 && visited++ < MaxProjectDirs)
        {
            var current = pending.Pop();
            if (current.depth >= MaxDepth) continue;
            try
            {
                foreach (string path in Directory.EnumerateDirectories(current.path))
                {
                    string name = Path.GetFileName(path);
                    try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue; }
                    catch { continue; }
                    if (BuildFolderNames.Contains(name))
                    {
                        add(path, FolderKind.BuildOutput);
                        continue;
                    }
                    pending.Push((path, current.depth + 1));
                    if (visited + pending.Count >= MaxProjectDirs) break;
                }
            }
            catch { }
        }
    }
}
