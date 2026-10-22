namespace OneMusic.Core;

/// <summary>간단한 파일 로그. %LocalAppData%\OneMusic\log.txt (개발 중이면 프로젝트의 .dev\app.log 에도)</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string AppDataDir { get; } = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneMusic");

    /// <summary>개발 중(소스 폴더에서 실행)이면 프로젝트 루트 — dev-watch.ps1 이 있는 폴더</summary>
    public static string? DevRoot { get; } = FindDevRoot();

    private static readonly string FilePath;

    static Log()
    {
        Directory.CreateDirectory(AppDataDir);
        FilePath = DevRoot != null
            ? IOPath.Combine(DevRoot, ".dev", "app.log")
            : IOPath.Combine(AppDataDir, "log.txt");
        try
        {
            Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, "");
        }
        catch { /* 무시 */ }
    }

    private static string? FindDevRoot()
    {
#if !DEBUG
        return null; // 배포판(Release)에서는 개발용 통로 · 로그를 쓰지 않는다
#endif
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(IOPath.Combine(dir.FullName, "dev-watch.ps1"))) return dir.FullName;
        }
        catch { /* 무시 */ }
        return null;
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERR ", ex == null ? msg : $"{msg}: {ex}");

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {msg}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, line); } catch { /* 무시 */ }
        }
    }
}
