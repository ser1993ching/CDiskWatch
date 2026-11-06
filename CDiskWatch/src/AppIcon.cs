using System.Runtime.InteropServices;
using System.Text;

namespace CDiskWatch;

/// <summary>应用图标：exe 模块资源 → 内嵌 ico → exe 关联图标（兜底）。</summary>
internal static class AppIcon
{
    public static IntPtr Load()
    {
        IntPtr h = LoadFromModule();
        if (h != IntPtr.Zero) return h;
        h = LoadFromFile(Path.Combine(AppDir, "app.ico"));
        if (h != IntPtr.Zero) return h;
        return LoadFromFile(Path.Combine(AppDir, "CDiskWatch.ico"));
    }

    /// <summary>从 exe 模块的 RT_GROUP_ICON（ID 32512）加载。</summary>
    public static IntPtr LoadFromModule()
    {
        try
        {
            IntPtr hinst = Win32.GetModuleHandleW(null);
            return Win32.LoadImageW_Res(hinst, (IntPtr)32512, Win32.IMAGE_ICON, 0, 0, Win32.LR_DEFAULTSIZE);
        }
        catch { return IntPtr.Zero; }
    }

    /// <summary>从磁盘加载图标文件（按尺寸）。</summary>
    public static IntPtr LoadFromFile(string path, int size = 16)
    {
        try
        {
            if (!File.Exists(path)) return IntPtr.Zero;
            return Win32.LoadImageW(IntPtr.Zero, path, Win32.IMAGE_ICON, size, size, Win32.LR_LOADFROMFILE);
        }
        catch { return IntPtr.Zero; }
    }

    public static string AppDir
    {
        get
        {
            // 单文件发布时 BaseDirectory = exe 所在目录；开发时 = dll 所在目录
            string b = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(b)) return b.TrimEnd('\\');
            string? p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p)) return Path.GetDirectoryName(p) ?? AppContext.BaseDirectory;
            return AppContext.BaseDirectory;
        }
    }

    public static string ExePath => Environment.ProcessPath ?? "CDiskWatch.exe";
}
