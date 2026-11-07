using System.Runtime.InteropServices;
using static CDiskWatch.FileCategory;

namespace CDiskWatch;

/// <summary>文件类别（定长 15 类，用数组槽位而非字典，避免哈希分配）。</summary>
public enum FileCategory
{
    Video = 0,
    Image = 1,
    Audio = 2,
    Document = 3,
    Archive = 4,
    Installer = 5,
    Log = 6,
    Cache = 7,
    Database = 8,
    DiskImage = 9,
    DevSource = 10,
    Package = 11,
    SysFile = 12,
    Temp = 13,
    Other = 14,
}

/// <summary>文件分类器：扩展名 → 类别；按长度分桶比较，全程零分配。</summary>
public static class Cat
{
    public const int Count = 15;

    /// <summary>稳定 ID（与语言无关，写入快照/JSON，脚本可依赖）。显示名走 <see cref="Name"/>。</summary>
    public static readonly string[] Ids =
    {
        "video", "image", "audio", "doc", "archive", "installer", "log", "cache",
        "db", "disk", "dev", "pkg", "sys", "temp", "other",
    };

    /// <summary>类别显示名（跟随界面语言）。</summary>
    public static string Name(int i) => Loc.T("cat." + Ids[i]);

    /// <summary>类别颜色（#RRGGBB，供结构化输出使用）。</summary>
    public static string HexColor(int i)
    {
        uint c = Colors[i];
        return "#" + ((c & 0xFF).ToString("X2") + ((c >> 8) & 0xFF).ToString("X2") + ((c >> 16) & 0xFF).ToString("X2"));
    }

    /// <summary>类别配色（0x00BBGGRR，与 GDI 的 COLORREF 一致）。</summary>
    /// <summary>类别配色（0x00BBGGRR，与 GDI 的 COLORREF 一致；浅色背景下足够饱和）。</summary>
    public static readonly uint[] Colors =
    {
        Win32.Rgb(0xEA, 0x7E, 0x2C), // 视频     橙
        Win32.Rgb(0x22, 0xA8, 0xBE), // 图片     青
        Win32.Rgb(0x8C, 0x5C, 0xD6), // 音频     紫
        Win32.Rgb(0x5C, 0xA8, 0x5A), // 文档     绿
        Win32.Rgb(0xC8, 0x8C, 0x28), // 压缩包   琥珀
        Win32.Rgb(0xD6, 0x4C, 0x4C), // 安装包   红
        Win32.Rgb(0x8A, 0x7C, 0x6C), // 日志     灰褐
        Win32.Rgb(0x3E, 0x8A, 0xD6), // 缓存     蓝
        Win32.Rgb(0x5C, 0x6C, 0xC2), // 数据库   靛
        Win32.Rgb(0xC4, 0x60, 0x28), // 磁盘镜像 深橙
        Win32.Rgb(0x2E, 0xA0, 0x8C), // 开发产物 青绿
        Win32.Rgb(0x76, 0x6E, 0xBE), // 软件包   蓝紫
        Win32.Rgb(0x8C, 0x94, 0xA0), // 系统文件 银灰
        Win32.Rgb(0xD6, 0x8A, 0x5C), // 临时文件 浅橙
        Win32.Rgb(0x96, 0x9C, 0xA5), // 其他     灰
    };

    static bool Is(ReadOnlySpan<char> s, string t) => s.Equals(t.AsSpan(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 扩展名（可带前导点）→ 类别。
    /// 按长度分桶，桶内再做大小写不敏感的 span 比较，全程零分配。
    /// 注意：每个 Is(ext, "...") 的字面量长度必须与本 case 的长度一致，
    /// 否则该分支永远不可达（历史上的 .wav / .webm / .svg 等就是这样被漏掉的）。
    /// </summary>
    public static FileCategory ByExtension(ReadOnlySpan<char> ext)
    {
        if (ext.Length > 0 && ext[0] == '.') ext = ext[1..];
        switch (ext.Length)
        {
            case 1:
                if (Is(ext, "c")) return DevSource;
                if (Is(ext, "h")) return DevSource;
                if (Is(ext, "r")) return DevSource;
                if (Is(ext, "s")) return DevSource;
                if (Is(ext, "t")) return DevSource;
                if (Is(ext, "g")) return DevSource;
                if (Is(ext, "w")) return DevSource;
                if (Is(ext, "m")) return Audio;
                if (Is(ext, "v")) return Video;
                if (Is(ext, "p")) return Image;
                break;
            case 2:
                if (Is(ext, "js")) return DevSource;
                if (Is(ext, "ts")) return DevSource;
                if (Is(ext, "py")) return DevSource;
                if (Is(ext, "go")) return DevSource;
                if (Is(ext, "rs")) return DevSource;
                if (Is(ext, "cs")) return DevSource;
                if (Is(ext, "io")) return DevSource;
                if (Is(ext, "so")) return DevSource;
                if (Is(ext, "mp")) return Video;
                if (Is(ext, "rm")) return Video;
                if (Is(ext, "db")) return Database;
                if (Is(ext, "7z")) return Archive;
                if (Is(ext, "gz")) return Archive;
                if (Is(ext, "bz")) return Archive;
                if (Is(ext, "xz")) return Archive;
                if (Is(ext, "ai")) return Image;
                if (Is(ext, "ps")) return Image;
                if (Is(ext, "md")) return Document;
                if (Is(ext, "au")) return Audio;
                break;
            case 3:
                if (Is(ext, "mp4")) return Video;
                if (Is(ext, "avi")) return Video;
                if (Is(ext, "mov")) return Video;
                if (Is(ext, "wmv")) return Video;
                if (Is(ext, "flv")) return Video;
                if (Is(ext, "mkv")) return Video;
                if (Is(ext, "m4v")) return Video;
                if (Is(ext, "mpg")) return Video;
                if (Is(ext, "vob")) return Video;
                if (Is(ext, "jpg")) return Image;
                if (Is(ext, "png")) return Image;
                if (Is(ext, "gif")) return Image;
                if (Is(ext, "bmp")) return Image;
                if (Is(ext, "tif")) return Image;
                if (Is(ext, "svg")) return Image;
                if (Is(ext, "ico")) return Image;
                if (Is(ext, "raw")) return Image;
                if (Is(ext, "eps")) return Image;
                if (Is(ext, "mp3")) return Audio;
                if (Is(ext, "wma")) return Audio;
                if (Is(ext, "aac")) return Audio;
                if (Is(ext, "ogg")) return Audio;
                if (Is(ext, "ape")) return Audio;
                if (Is(ext, "m4a")) return Audio;
                if (Is(ext, "wav")) return Audio;
                if (Is(ext, "doc")) return Document;
                if (Is(ext, "xls")) return Document;
                if (Is(ext, "ppt")) return Document;
                if (Is(ext, "pdf")) return Document;
                if (Is(ext, "txt")) return Document;
                if (Is(ext, "rtf")) return Document;
                if (Is(ext, "csv")) return Document;
                if (Is(ext, "zip")) return Archive;
                if (Is(ext, "rar")) return Archive;
                if (Is(ext, "tar")) return Archive;
                if (Is(ext, "cab")) return Archive;
                if (Is(ext, "iso")) return Archive;
                if (Is(ext, "img")) return Archive;
                if (Is(ext, "tgz")) return Archive;
                if (Is(ext, "zst")) return Archive;
                if (Is(ext, "exe")) return Installer;
                if (Is(ext, "msi")) return Installer;
                if (Is(ext, "msu")) return Installer;
                if (Is(ext, "dll")) return DevSource;
                if (Is(ext, "obj")) return DevSource;
                if (Is(ext, "lib")) return DevSource;
                if (Is(ext, "pdb")) return DevSource;
                if (Is(ext, "pch")) return DevSource;
                if (Is(ext, "cpp")) return DevSource;
                if (Is(ext, "hpp")) return DevSource;
                if (Is(ext, "res")) return SysFile;
                if (Is(ext, "sys")) return SysFile;
                if (Is(ext, "mui")) return SysFile;
                if (Is(ext, "dat")) return Database;
                if (Is(ext, "log")) return Log;
                if (Is(ext, "old")) return Log;
                if (Is(ext, "bak")) return Log;
                if (Is(ext, "swp")) return Log;
                if (Is(ext, "tmp")) return Temp;
                if (Is(ext, "etl")) return Log;
                if (Is(ext, "dmp")) return Log;
                if (Is(ext, "vhd")) return DiskImage;
                if (Is(ext, "vdi")) return DiskImage;
                if (Is(ext, "jar")) return Package;
                if (Is(ext, "gem")) return Package;
                if (Is(ext, "deb")) return Package;
                if (Is(ext, "rpm")) return Package;
                if (Is(ext, "lnk")) return Other;
                if (Is(ext, "ini")) return Other;
                break;
            case 4:
                if (Is(ext, "mpeg")) return Video;
                if (Is(ext, "rmvb")) return Video;
                if (Is(ext, "m2ts")) return Video;
                if (Is(ext, "divx")) return Video;
                if (Is(ext, "webm")) return Video;
                if (Is(ext, "jpeg")) return Image;
                if (Is(ext, "tiff")) return Image;
                if (Is(ext, "heic")) return Image;
                if (Is(ext, "heif")) return Image;
                if (Is(ext, "webp")) return Image;
                if (Is(ext, "docx")) return Document;
                if (Is(ext, "xlsx")) return Document;
                if (Is(ext, "pptx")) return Document;
                if (Is(ext, "html")) return Document;
                if (Is(ext, "json")) return Document;
                if (Is(ext, "yaml")) return Document;
                if (Is(ext, "epub")) return Document;
                if (Is(ext, "mobi")) return Document;
                if (Is(ext, "java")) return DevSource;
                if (Is(ext, "vhdx")) return DiskImage;
                if (Is(ext, "vmdk")) return DiskImage;
                if (Is(ext, "evtx")) return Log;
                if (Is(ext, "vsix")) return Package;
                if (Is(ext, "appx")) return Installer;
                if (Is(ext, "msix")) return Installer;
                break;
            case 5:
                if (Is(ext, "class")) return DevSource;
                if (Is(ext, "nupkg")) return Package;
                if (Is(ext, "qcow2")) return DiskImage;
                if (Is(ext, "jsonl")) return Document;
                break;
            case 6:
                if (Is(ext, "sqlite")) return Database;
                break;
            case 7:
                if (Is(ext, "sqlite3")) return Database;
                break;
            case 10:
                if (Is(ext, "appxbundle")) return Installer;
                break;
        }
        return FileCategory.Other;
    }

    /// <summary>路径关键词判定：缓存 / 临时目录（用于概览的专项统计与建议规则）。</summary>
    public static bool IsCachePath(ReadOnlySpan<char> dir) => Contains(dir, "cache");
    public static bool IsTempPath(ReadOnlySpan<char> dir) => Contains(dir, "temp") || Contains(dir, "\\tmp");

    static bool Contains(ReadOnlySpan<char> hay, string needle)
    {
        if (hay.Length < needle.Length) return false;
        for (int i = 0; i <= hay.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
            {
                char a = hay[i + j], b = needle[j];
                if (char.IsLetter(b)) { if (char.ToLowerInvariant(a) != b) { ok = false; break; } }
                else if (a != b) { ok = false; break; }
            }
            if (ok) return true;
        }
        return false;
    }
}
