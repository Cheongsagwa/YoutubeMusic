using System.IO;
using System.Windows;

namespace OneMusicSetup;

public partial class App : Application
{
    /// <summary>뜻밖의 오류는 %TEMP%\YouTubeMusicSetup.log 에 남긴다</summary>
    public static void Log(string text)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "YouTubeMusicSetup.log"), $"[{DateTime.Now:HH:mm:ss}] {text}\r\n"); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, a) => { Log(a.Exception.ToString()); };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject.ToString() ?? "");
        Exit += (_, a) => Log($"종료 (코드 {a.ApplicationExitCode})");
        base.OnStartup(e);
    }
}
