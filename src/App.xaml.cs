using OneMusic.Core;
using OneMusic.UI;

namespace OneMusic;

public partial class App : Application
{
    public static MainWindow? MainWin { get; private set; }

    public App()
    {
        // 작업 표시줄 미디어 창 · 알림에 '알 수 없는 앱' 대신 YouTube Music + 아이콘 (창을 만들기 전에)
        AppIdentity.Apply();
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("UnhandledException", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Info("앱 시작");
        await Auth.Load();
        UI.Notifier.Register();
        MainWin = new MainWindow();
        MainWin.Activate();
    }

    public static void SaveSession() => _ = Auth.Save();
}
