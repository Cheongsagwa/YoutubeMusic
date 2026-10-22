using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace OneMusicSetup;

/// <summary>
/// 설치 화면 — 옵션 → 진행 → 끝, 세 쪽을 One UI 처럼 미끄러지며 넘긴다.
/// 사용자 폴더(%LocalAppData%\Programs)에 깔아서 관리자 권한이 필요 없다.
/// 이미 깔려 있으면 업데이트 / 제거를 고르는 화면부터 (제거는 '설정 → 앱' 의 uninstall.cmd 로도 된다)
/// </summary>
public partial class MainWindow : Window
{
    private const string Name_ = "YouTube Music";
    private const string Aumid = "Cheongsagwa.OneMusic.YouTubeMusic";
    private const string ExeName = "OneMusic.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OneMusic.YouTubeMusic";
    // 설치할 버전 — 저장소 맨 위 Directory.Build.props 의 <Version> 하나에서 앱과 같이 나온다
    private static readonly string Version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    private string _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name_);

    public MainWindow()
    {
        InitializeComponent();
        PathText.Text = _dir;
        PathText.ToolTip = _dir;
        if (Assembly.GetExecutingAssembly().GetManifestResourceInfo("payload.zip") == null)
        {
            ErrorText.Text = "설치 파일이 비어 있어요 (payload.zip 없음)";
            ErrorText.Visibility = Visibility.Visible;
            InstallButton.IsEnabled = false;
        }
        // 이미 깔려 있으면 (설정 → 앱 에 등록된 위치 우선) 업데이트 / 제거를 고르게 한다
        var installed = FindInstalled();
        if (installed != null)
        {
            _dir = installed;
            PathText.Text = _dir;
            PathText.ToolTip = _dir;
        }
        OptionsVersion.Text = Version;
        // 깔린 버전 → 설치할 버전 (예전 설치라 버전을 모르면 설치할 버전만)
        var current = installed == null ? null : ReadInstallJson()?.version;
        MaintainVersion.Text = current == null ? Version : $"{current} → {Version}";
        Loaded += (_, _) => Enter(installed != null ? MaintainPage : OptionsPage, 0);
    }

    private sealed record InstallInfo(string? version, bool startMenu = true, bool desktop = true);

    private static string? FindInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is string loc && File.Exists(Path.Combine(loc, "install.json"))) return loc;
        }
        catch { }
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name_);
        return File.Exists(Path.Combine(def, "install.json")) ? def : null;
    }

    private InstallInfo? ReadInstallJson()
    {
        try { return JsonSerializer.Deserialize<InstallInfo>(File.ReadAllText(Path.Combine(_dir, "install.json"))); }
        catch { return null; }
    }

    private static ZipArchive? OpenPayload()
    {
        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        return s == null ? null : new ZipArchive(s, ZipArchiveMode.Read);
    }

    // ───── 창 ─────

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "설치할 폴더 고르기", InitialDirectory = Path.GetDirectoryName(_dir) };
        if (dlg.ShowDialog(this) != true) return;
        var picked = dlg.FolderName;
        // 고른 폴더 안에 'YouTube Music' 폴더를 만든다 (고른 폴더를 통째로 지우는 일이 없게)
        if (!string.Equals(Path.GetFileName(picked.TrimEnd('\\')), Name_, StringComparison.OrdinalIgnoreCase))
            picked = Path.Combine(picked, Name_);
        _dir = picked;
        PathText.Text = _dir;
        PathText.ToolTip = _dir;
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        Launch();
        Close();
    }

    private void Launch()
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(_dir, ExeName)) { UseShellExecute = true, WorkingDirectory = _dir }); }
        catch { }
    }

    // ───── 쪽 넘기기 (One UI: 살짝 아래에서 떠오르며 나타남) ─────

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static void Enter(FrameworkElement page, double delayMs)
    {
        page.Visibility = Visibility.Visible;
        var move = new TranslateTransform(0, 24);
        page.RenderTransform = move;
        page.Opacity = 0;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)) { BeginTime = begin, EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(520)) { BeginTime = begin, EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
    }

    private static void Leave(FrameworkElement page)
    {
        var move = new TranslateTransform(0, 0);
        page.RenderTransform = move;
        page.IsHitTestVisible = false;
        var fade = new DoubleAnimation(page.Opacity, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease };
        fade.Completed += (_, _) => page.Visibility = Visibility.Collapsed;
        page.BeginAnimation(OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -16, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
    }

    private double _shown;

    /// <summary>진행 막대 — 값이 튀지 않게 늘 부드럽게 따라간다</summary>
    private void SetProgress(double p)
    {
        p = Math.Clamp(p, 0, 1);
        if (p < _shown) return;
        _shown = p;
        Fill.BeginAnimation(WidthProperty, new DoubleAnimation(Track.ActualWidth * p, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ease });
    }

    // ───── 설치 ─────

    private void OnInstall(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        Run(OptionsPage, StartMenuToggle.IsChecked == true, DesktopToggle.IsChecked == true);
    }

    /// <summary>업데이트 — 지난번 바로가기 선택 그대로 같은 폴더에 새로 깐다</summary>
    private void OnUpdate(object sender, RoutedEventArgs e)
    {
        var old = ReadInstallJson();
        ProgressTitle.Text = "업데이트하는 중…";
        DoneTitle.Text = "업데이트를 마쳤어요";
        Run(MaintainPage, old?.startMenu ?? true, old?.desktop ?? true);
    }

    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        CloseButton.IsEnabled = false;
        ProgressTitle.Text = "제거하는 중…";
        Leave(MaintainPage);
        Enter(ProgressPage, 160);
        await Task.Delay(500);

        var progress = new Progress<double>(SetProgress);
        string? error = null;
        try { await Task.Run(() => Uninstall(progress)); }
        catch (Exception ex) { error = ex.Message; }

        SetProgress(1);
        await Task.Delay(450);
        CloseButton.IsEnabled = true;
        DoneTitle.Text = error == null ? "제거했어요" : "제거하지 못했어요";
        if (error != null)
        {
            DoneText.Text = error;
            DoneText.Visibility = Visibility.Visible;
            DoneCircle.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5F));
            DoneMark.Data = Geometry.Parse("M8 8l8 8M16 8l-8 8");
            DoneMark.Stroke = Brushes.White;
        }
        OpenButton.Visibility = Visibility.Collapsed;
        Grid.SetColumnSpan(CloseDone, 3);
        Leave(ProgressPage);
        Enter(DonePage, 160);
        PopCheck();
    }

    private async void Run(FrameworkElement from, bool startMenu, bool desktop)
    {
        CloseButton.IsEnabled = false;
        Leave(from);
        Enter(ProgressPage, 160);
        await Task.Delay(500);

        var progress = new Progress<double>(SetProgress);
        string? error = null;
        try { await Task.Run(() => Install(startMenu, desktop, progress)); }
        catch (Exception ex) { error = ex.Message; }

        SetProgress(1);
        await Task.Delay(450);
        CloseButton.IsEnabled = true;
        if (error != null)
        {
            DoneTitle.Text = from == MaintainPage ? "업데이트하지 못했어요" : "설치하지 못했어요";
            DoneText.Text = error;
            DoneText.Visibility = Visibility.Visible;
            DoneCircle.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5F));
            DoneMark.Data = Geometry.Parse("M8 8l8 8M16 8l-8 8");
            DoneMark.Stroke = Brushes.White;
            OpenButton.Visibility = Visibility.Collapsed;
            Grid.SetColumnSpan(CloseDone, 3);
        }
        Leave(ProgressPage);
        Enter(DonePage, 160);
        PopCheck();
    }

    /// <summary>끝 표시 동그라미가 톡 튀어나오고 체크가 그려진다</summary>
    private void PopCheck()
    {
        var circle = (Grid)DoneCircle.Parent;
        var scale = new ScaleTransform(0.4, 0.4);
        circle.RenderTransformOrigin = new Point(0.5, 0.5);
        circle.RenderTransform = scale;
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(200), EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        // 선 길이(≈14 → 굵기 2.4 단위로 ≈6)만큼 대시를 밀어 '그려지는' 체크
        DoneMark.StrokeDashArray = new DoubleCollection { 6.2, 6.2 };
        DoneMark.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(6.2, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(420), EasingFunction = Ease });
    }

    /// <summary>이 폴더에서 실행 중인 앱만 끈다 (개발용 빌드 등 다른 위치의 앱은 그대로)</summary>
    private void CloseRunning()
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try
            {
                var exe = p.MainModule?.FileName;
                if (exe == null || exe.StartsWith(_dir, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(5000); }
            }
            catch { }
        }
    }

    private static string StartLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name_ + ".lnk");
    private static string DeskLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name_ + ".lnk");

    /// <summary>uninstall.cmd 와 같은 일 — 계정 · 설정 · 캐시(%LocalAppData%\OneMusic)는 남긴다</summary>
    private void Uninstall(IProgress<double> progress)
    {
        progress.Report(0.05);
        CloseRunning();
        progress.Report(0.2);
        TryDelete(StartLnk);
        TryDelete(DeskLnk);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{Aumid}", false); } catch { }
        progress.Report(0.4);
        if (Directory.Exists(_dir) && File.Exists(Path.Combine(_dir, "install.json")))
        {
            var files = Directory.GetFiles(_dir, "*", SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                TryDelete(files[i]);
                if (i % 20 == 0) progress.Report(0.4 + 0.55 * i / files.Length);
            }
            Directory.Delete(_dir, true);
        }
        progress.Report(1);
    }

    private void Install(bool startMenu, bool desktop, IProgress<double> progress)
    {
        progress.Report(0.01);
        CloseRunning();

        // 예전 버전 파일이 남지 않게 — 우리가 깐 폴더(install.json 있음)일 때만 비운다
        if (Directory.Exists(_dir) && File.Exists(Path.Combine(_dir, "install.json")))
        {
            progress.Report(0.03);
            foreach (var f in Directory.GetFiles(_dir)) TryDelete(f);
            foreach (var d in Directory.GetDirectories(_dir)) { try { Directory.Delete(d, true); } catch { } }
        }
        Directory.CreateDirectory(_dir);

        using (var zip = OpenPayload() ?? throw new InvalidOperationException("설치 파일이 비어 있어요"))
        {
            var total = Math.Max(1, zip.Entries.Sum(x => x.Length));
            long done = 0;
            var root = Path.GetFullPath(_dir) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                var dest = Path.GetFullPath(Path.Combine(_dir, entry.FullName));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
                done += entry.Length;
                progress.Report(0.05 + 0.85 * done / total);
            }
        }

        progress.Report(0.92);
        var exePath = Path.Combine(_dir, ExeName);
        var icon = Path.Combine(_dir, "Assets", "app.ico");
        if (!File.Exists(icon)) icon = exePath;
        var startLnk = StartLnk;
        var deskLnk = DeskLnk;
        if (startMenu) Shortcut.Create(startLnk, exePath, icon, Name_, Aumid); else TryDelete(startLnk);
        if (desktop) Shortcut.Create(deskLnk, exePath, icon, Name_, Aumid); else TryDelete(deskLnk);

        progress.Report(0.96);
        File.WriteAllText(Path.Combine(_dir, "install.json"), JsonSerializer.Serialize(new
        {
            version = Version,
            installedAt = DateTime.Now.ToString("o"),
            startMenu,
            desktop,
        }, new JsonSerializerOptions { WriteIndented = true }));

        var uninstall = Path.Combine(_dir, "uninstall.cmd");
        WriteUninstaller(uninstall, startLnk, deskLnk);

        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}"))
        {
            key.SetValue("DisplayName", Name_);
            key.SetValue("IconUri", icon);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", Name_);
            key.SetValue("DisplayIcon", icon);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Cheongsagwa");
            key.SetValue("InstallLocation", _dir);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("EstimatedSize", (int)(DirSize(_dir) / 1024), RegistryValueKind.DWord);
            key.SetValue("UninstallString", $"cmd.exe /c \"\"{uninstall}\"\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        progress.Report(1);
    }

    /// <summary>
    /// 제거 스크립트 — 앱 끄기 → 바로가기 · 레지스트리 지우기 → 설치 폴더 지우기.
    /// 계정 · 설정 · 캐시(%LocalAppData%\OneMusic)는 다시 깔 때를 위해 남긴다.
    /// '(goto) 2>nul &amp;' 은 배치 파일이 자기 폴더를 지울 때 쓰는 요령 (자신을 다 읽은 다음 지운다)
    /// </summary>
    private void WriteUninstaller(string path, string startLnk, string deskLnk)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine($"taskkill /f /im {ExeName} >nul 2>&1");
        sb.AppendLine("ping -n 2 127.0.0.1 >nul"); // 꺼질 때까지 잠깐 (timeout 은 콘솔 입력 없으면 실패)
        sb.AppendLine($"del /f /q \"{startLnk}\" >nul 2>&1");
        sb.AppendLine($"del /f /q \"{deskLnk}\" >nul 2>&1");
        sb.AppendLine($"reg delete \"HKCU\\{UninstallKey}\" /f >nul 2>&1");
        sb.AppendLine($"reg delete \"HKCU\\Software\\Classes\\AppUserModelId\\{Aumid}\" /f >nul 2>&1");
        sb.AppendLine("cd /d \"%TEMP%\"");
        sb.AppendLine($"(goto) 2>nul & rmdir /s /q \"{_dir}\"");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static long DirSize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
}
