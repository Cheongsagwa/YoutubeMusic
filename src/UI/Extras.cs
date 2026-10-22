using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OneMusic.Core;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace OneMusic.UI;

/// <summary>
/// 키보드 단축키 (YouTube · YT Music 웹과 같은 키 위주). 입력칸에 글을 쓰는 중이면 동작하지 않는다.
/// Shift + ? 로 목록을 볼 수 있다
/// </summary>
public static class Shortcuts
{
    public static readonly (string Keys, string What)[] List =
    [
        ("Space · K", "재생 / 일시정지"),
        ("Shift + N", "다음 곡"),
        ("Shift + P", "이전 곡"),
        ("J · L", "10초 뒤로 · 앞으로"),
        ("← · →", "5초 뒤로 · 앞으로"),
        ("↑ · ↓", "볼륨 5% 올리기 · 내리기"),
        ("M", "음소거"),
        ("R", "반복 모드 바꾸기"),
        ("S", "셔플"),
        ("+ · −", "좋아요 · 싫어요"),
        ("F", "재생 화면 열기 / 닫기"),
        ("I", "미니 플레이어"),
        ("< · >", "재생 속도 느리게 · 빠르게"),
        ("/ · Ctrl + F", "검색"),
        ("Alt + ← · →", "뒤로 · 앞으로"),
        ("Esc", "메뉴 · 재생 화면 닫기, 뒤로"),
        ("Shift + ?", "단축키 목록"),
    ];

    /// <summary>처리했으면 true</summary>
    public static bool Handle(MainWindow w, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or AutoSuggestBox or PasswordBox) return false;
        var p = w.Player;
        var shift = Down(VirtualKey.Shift);
        var ctrl = Down(VirtualKey.Control);
        var alt = Down(VirtualKey.Menu);
        switch (e.Key)
        {
            case VirtualKey.Space:
            case VirtualKey.K when !ctrl:
                p.TogglePlay();
                return true;
            case VirtualKey.N when shift:
                _ = p.Next();
                return true;
            case VirtualKey.P when shift:
                _ = p.Previous();
                return true;
            case VirtualKey.J:
                p.Seek(p.Position - 10);
                return true;
            case VirtualKey.L when !ctrl:
                p.Seek(p.Position + 10);
                return true;
            case VirtualKey.Left when alt:
                w.Back();
                return true;
            case VirtualKey.Right when alt:
                w.Forward();
                return true;
            case VirtualKey.Left:
                p.Seek(p.Position - 5);
                return true;
            case VirtualKey.Right:
                p.Seek(p.Position + 5);
                return true;
            case VirtualKey.Up:
                p.Volume = Math.Min(1, p.Volume + 0.05);
                Toast.Show($"볼륨 {Math.Round(p.Volume * 100)}%");
                return true;
            case VirtualKey.Down:
                p.Volume = Math.Max(0, p.Volume - 0.05);
                Toast.Show($"볼륨 {Math.Round(p.Volume * 100)}%");
                return true;
            case VirtualKey.M:
                p.Muted = !p.Muted;
                return true;
            case VirtualKey.R:
                p.CycleRepeat(); // 재생 바의 반복 아이콘이 바뀐다
                return true;
            case VirtualKey.S when !ctrl:
                p.Shuffle();
                Toast.Show("목록을 섞었어요");
                return true;
            case VirtualKey.F when ctrl:
            case (VirtualKey)191 when !shift: // '/'
                w.FocusSearch();
                return true;
            case (VirtualKey)191 when shift: // '?'
                ShowHelp(w);
                return true;
            case VirtualKey.F:
                w.TogglePlayer();
                return true;
            case VirtualKey.I when !ctrl:
                w.ToggleMini();
                return true;
            case (VirtualKey)188 when shift: // '<'
            case (VirtualKey)190 when shift: // '>'
            {
                var steps = SpeedMenu.Steps;
                var i = Array.FindIndex(steps, s => Math.Abs(s - p.Speed) < 0.01);
                if (i < 0) i = Array.IndexOf(steps, 1.0);
                i = Math.Clamp(i + (e.Key == (VirtualKey)190 ? 1 : -1), 0, steps.Length - 1);
                p.Speed = steps[i];
                Toast.Show($"재생 속도 {SpeedMenu.Label(p.Speed)}");
                return true;
            }
            case (VirtualKey)187: // '=' / '+'
            case VirtualKey.Add:
                if (p.Current is { } liked) _ = Rate(liked, true);
                return true;
            case (VirtualKey)189: // '-'
            case VirtualKey.Subtract:
                if (p.Current is { } disliked) _ = Rate(disliked, false);
                return true;
        }
        return false;
    }

    private static async Task Rate(Track t, bool like)
    {
        var ok = await InnerTube.Instance.Rate(t.VideoId, like);
        Toast.Show(!ok ? "로그인이 필요해요" : like ? "좋아요 표시한 노래에 추가했어요" : "싫어요를 표시했어요");
        if (ok && !like && PlayerService.Instance.Current?.VideoId == t.VideoId) _ = PlayerService.Instance.Next();
    }

    private static bool Down(VirtualKey k) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    public static void ShowHelp(MainWindow w)
    {
        var m = new OneMenu(width: 380);
        m.Header("단축키");
        foreach (var (k, what) in List) m.Info(k, what);
        var size = w.Root.XamlRoot?.Size ?? new Size(1200, 800);
        m.Show(w.Root, new Point(size.Width / 2 - 190, 110));
    }
}

/// <summary>출력 장치 고르기 (pear custom-output-device) — 스피커 · 헤드폰 · 블루투스 …</summary>
public static class OutputMenu
{
    public static string Label => PlayerService.Instance.OutputName ?? "기본 장치";

    public static async void Show(FrameworkElement at)
    {
        var p = PlayerService.Instance;
        List<Windows.Devices.Enumeration.DeviceInformation> devices;
        try
        {
            var found = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioRenderSelector());
            devices = found.Where(d => d.IsEnabled).ToList();
        }
        catch (Exception e)
        {
            Log.Warn($"출력 장치 목록 실패: {e.Message}");
            devices = new();
        }
        var current = Settings.Current.AudioDevice;
        var m = new OneMenu(width: 320);
        m.Header("출력 장치");
        m.Item("기본 장치", Icons.Output, async () => await p.SetOutput(null), check: current == null);
        foreach (var d in devices)
        {
            var dev = d;
            m.Item(dev.Name, Icons.Output, async () => await p.SetOutput(dev.Id), check: dev.Id == current);
        }
        m.Show(at);
    }
}

/// <summary>재생 속도 (pear playback-speed) — 0.5 ~ 2배</summary>
public static class SpeedMenu
{
    public static readonly double[] Steps = [0.5, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];

    public static string Label(double s) => Math.Abs(s - 1) < 0.001 ? "보통" : $"{s:0.##}배";

    public static void Show(FrameworkElement at)
    {
        var p = PlayerService.Instance;
        var m = new OneMenu();
        m.Header("재생 속도");
        foreach (var s in Steps)
        {
            var v = s;
            m.Item(Label(s), Icons.Speed, () => p.Speed = v, check: Math.Abs(s - p.Speed) < 0.01);
        }
        m.Show(at);
    }
}

/// <summary>취침 타이머 — 정한 시간이 지나면(또는 지금 곡이 끝나면) 멈춘다</summary>
public static class SleepTimer
{
    private static DispatcherQueueTimer? _timer;
    public static DateTime? Until { get; private set; }
    private static int _chosen;

    public static string Label => PlayerService.Instance.StopAfterCurrent ? "지금 곡이 끝나면"
        : Until is { } u ? $"{Math.Max(1, (int)Math.Ceiling((u - DateTime.Now).TotalMinutes))}분 뒤" : "꺼짐";

    public static void Set(int minutes)
    {
        _timer?.Stop();
        Until = null;
        PlayerService.Instance.StopAfterCurrent = false;
        if (minutes == 0) return;
        if (minutes < 0)
        {
            PlayerService.Instance.StopAfterCurrent = true;
            return;
        }
        _chosen = minutes;
        Until = DateTime.Now.AddMinutes(minutes);
        _timer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(minutes);
        _timer.IsRepeating = false;
        _timer.Tick -= Fire;
        _timer.Tick += Fire;
        _timer.Start();
    }

    private static void Fire(DispatcherQueueTimer t, object o)
    {
        Until = null;
        PlayerService.Instance.Pause();
        Toast.Show("취침 타이머로 멈췄어요");
    }

    public static void ShowMenu(FrameworkElement at)
    {
        var m = new OneMenu();
        m.Header("취침 타이머");
        // 지금 고른 것에 ✓ (분 단위는 남은 시간이 아니라 고른 길이로 표시) — 메뉴만 보고도 상태를 안다
        var on = PlayerService.Instance.StopAfterCurrent ? -1 : Until == null ? 0 : _chosen;
        foreach (var (text, min) in new[] { ("끄기", 0), ("15분", 15), ("30분", 30), ("45분", 45), ("1시간", 60), ("2시간", 120), ("지금 곡이 끝나면", -1) })
            m.Item(text, Icons.Moon, () => Set(min), check: min == on);
        m.Show(at);
    }
}

/// <summary>곡이 바뀌면 윈도우 알림 (pear notifications) — 창이 앞에 있을 때는 띄우지 않는다</summary>
public static class Notifier
{
    private static bool _registered;

    public static void Register()
    {
        try
        {
            _ui = DispatcherQueue.GetForCurrentThread();
            // 알림의 이전 · 재생/일시정지 · 다음 버튼 (창을 띄우지 않고 바로 조작)
            AppNotificationManager.Default.NotificationInvoked += (_, e) =>
            {
                if (!e.Arguments.TryGetValue("action", out var action)) return;
                _ui?.TryEnqueue(async () =>
                {
                    var p = PlayerService.Instance;
                    switch (action)
                    {
                        case "prev": await p.Previous(); break;
                        case "next": await p.Next(); break;
                        case "toggle":
                            p.TogglePlay();
                            await Task.Delay(300);
                            if (p.Current is { } cur) Show(cur, update: true); // 버튼 글자(재생 ↔ 일시정지)만 바꿔서 조용히 갱신
                            break;
                    }
                });
            };
            var icon = IOPath.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icon)) AppNotificationManager.Default.Register(AppIdentity.Name, new Uri(icon));
            else AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception e) { Log.Warn($"알림 등록 실패: {e.Message}"); }
    }

    public static void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static DispatcherQueue? _ui;

    public static void TrackChanged(Track t, bool windowActive)
    {
        if (!_registered || windowActive || !Settings.Current.Notifications) return;
        Show(t, update: false);
    }

    /// <summary>개발용: 지금 곡 알림을 바로 띄운다</summary>
    public static void TestShow(Track t) => Show(t, update: false);

    private static AppNotificationButton NotifyButton(string name, string b64, string tip) =>
        new AppNotificationButton().SetIcon(NotifyIcons.File(name, b64)).SetToolTip(tip);

    private static async void Show(Track t, bool update)
    {
        try
        {
            var playing = PlayerService.Instance.IsPlaying || PlayerService.Instance.IsBuffering;
            var b = new AppNotificationBuilder()
                .AddText(t.Title)
                .AddText(t.Artist)
                // 글자 없이 흰 아이콘만 (사용자 요청) — 마우스를 올리면 설명이 나온다
                .AddButton(NotifyButton("prev", NotifyIcons.Prev, "이전").AddArgument("action", "prev"))
                .AddButton((playing ? NotifyButton("pause", NotifyIcons.Pause, "일시정지") : NotifyButton("play", NotifyIcons.Play, "재생")).AddArgument("action", "toggle"))
                .AddButton(NotifyButton("next", NotifyIcons.Next, "다음").AddArgument("action", "next"))
                .MuteAudio();
            // 패키지 없는 앱은 인터넷 그림을 바로 못 쓰니 앨범아트를 파일로 받아 둔다
            try
            {
                if (Fmt.Thumb(t.Thumb, 200) is { } thumb)
                {
                    var file = IOPath.Combine(Log.AppDataDir, "notify-art.jpg");
                    await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(thumb));
                    b.SetAppLogoOverride(new Uri(file), AppNotificationImageCrop.Default);
                }
            }
            catch { /* 그림 없이 */ }
            if (PlayerService.Instance.Current?.VideoId != t.VideoId) return;
            var n = b.BuildNotification();
            n.Tag = "track";
            n.Group = "onemusic";
            n.ExpiresOnReboot = true;
            n.SuppressDisplay = update; // 버튼만 바꿀 땐 다시 띄우지 않고 알림 센터의 것만 갱신
            AppNotificationManager.Default.Show(n);
        }
        catch (Exception e) { Log.Warn($"알림 실패: {e.Message}"); }
    }
}

/// <summary>들었던 곡 디스크 캐시 — 상한 고르기 · 지금 크기 · 비우기</summary>
public static class CacheMenu
{
    public static void Show(FrameworkElement at)
    {
        var s = Settings.Current;
        var m = new OneMenu(width: 300);
        m.Header("음악 캐시", AudioCache.SizeLabel(AudioCache.Size()));
        foreach (var mb in AudioCache.Limits)
        {
            var v = mb;
            var label = mb <= 0 ? "끄기" : $"최대 {AudioCache.LimitLabel(mb)}";
            m.Item(label, Icons.Storage, check: v == s.CacheLimitMB, click: async () =>
            {
                await AudioCache.SetLimit(v);
                // 끄면 있던 캐시를 지운다 — 눈에 안 보이는 결과라 알린다
                if (v <= 0) Toast.Show("캐시를 비웠어요");
            });
        }
        m.Separator();
        m.Item("캐시 비우기", Icons.MinusCircle, async () =>
        {
            await AudioCache.ClearAsync();
            Toast.Show("캐시를 비웠어요");
        });
        m.Show(at);
    }
}

/// <summary>
/// 상단 바 프로필 사진(계정) 메뉴 — 자주 쓰는 순서로 묶는다: (로그인 전이면 로그인) → 재생 중에 바꾸는 것
/// → 소리 설정 → 앱 동작 → 가끔 여는 것 → 로그아웃
/// </summary>
public static class AccountMenu
{
    public static void Show(MainWindow w, FrameworkElement at)
    {
        var s = Settings.Current;
        var m = new OneMenu(width: 300);
        // 로그인 전엔 로그인이 가장 먼저 할 일
        if (!Auth.IsLoggedIn)
        {
            m.Item("구글 계정으로 로그인", Icons.Person, async () =>
            {
                var lw = new LoginWindow();
                lw.Activate();
                if (await lw.Result) w.ReloadAll();
            });
            m.Separator();
        }
        var p = PlayerService.Instance;
        // 미니 플레이어는 재생 바 버튼 · 단축키(I)에 있다 — 메뉴에 또 두지 않는다
        m.Item("재생 속도", Icons.Speed, () => SpeedMenu.Show(at), keepOpen: true, value: SpeedMenu.Label(p.Speed));
        m.Item("출력 장치", Icons.Output, () => OutputMenu.Show(at), keepOpen: true, value: OutputMenu.Label);
        m.Item("취침 타이머", Icons.Moon, () => SleepTimer.ShowMenu(at), keepOpen: true, value: SleepTimer.Label);
        m.Separator();
        // 켜고 끄는 항목은 메뉴 안 스위치로 결과가 보인다 — 알림은 사용자가 뭘 해야 할 때만
        m.Toggle("음량 표준화", Icons.Level, () => p.Normalize, () => p.Normalize = !p.Normalize);
        m.Toggle("높은 음질", Icons.Bars, () => s.HighQuality, () =>
        {
            s.HighQuality = !s.HighQuality;
            s.Save();
            // 켰는데 쓸 수 없는 상태면 무엇이 필요한지
            if (s.HighQuality && !StreamResolver.Instance.PremiumReady)
                Toast.Show(Auth.IsLoggedIn ? "높은 음질엔 Node.js 가 필요해요" : "높은 음질은 프리미엄 계정으로 로그인해야 돼요");
        });
        m.Toggle("지수 볼륨", Icons.Curve, () => p.ExponentialVolume, () => p.ExponentialVolume = !p.ExponentialVolume);
        m.Separator();
        m.Toggle("이어 듣기", Icons.PlayNextIcon, () => s.ResumeSession, () => { s.ResumeSession = !s.ResumeSession; s.Save(); });
        m.Toggle("켤 때 바로 재생", Icons.Play, () => s.PlayOnStart, () =>
        {
            s.PlayOnStart = !s.PlayOnStart;
            // 바로 재생하려면 이어 듣기가 켜져 있어야 한다 (메뉴의 이어 듣기 스위치도 같이 바뀐다)
            if (s.PlayOnStart) s.ResumeSession = true;
            s.Save();
        });
        m.Toggle("곡 바뀜 알림", Icons.Bell, () => s.Notifications, () => { s.Notifications = !s.Notifications; s.Save(); });
        m.Separator();
        m.Item("다운로드 폴더 열기", Icons.Folder, async () =>
        {
            Directory.CreateDirectory(Offline.DownloadDir);
            await Launcher.LaunchFolderPathAsync(Offline.DownloadDir);
        });
        m.Item("음악 캐시", Icons.Storage, () => CacheMenu.Show(at), keepOpen: true, value: AudioCache.LimitLabel(s.CacheLimitMB));
        m.Item("단축키", Icons.Keyboard, () => Shortcuts.ShowHelp(w));
        m.Item("앱 정보", Icons.Info, AboutDialog.Show);
        if (Auth.IsLoggedIn)
        {
            m.Separator();
            m.Item("로그아웃", Icons.Person, () => { Auth.Logout(); w.ReloadAll(); });
        }
        m.Show(at);
    }
}

/// <summary>앱 정보 — 로고 · 버전 · 저작권 · 상표 · 오픈소스 (계정 메뉴 '앱 정보')</summary>
public static class AboutDialog
{
    public static void Show()
    {
        var asm = typeof(AboutDialog).Assembly;
        var version = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
            ?? asm.GetName().Version?.ToString(3) ?? "";
        var copyright = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyCopyrightAttribute), false)
            .OfType<System.Reflection.AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright ?? "";

        var body = new StackPanel { Spacing = 20 };
        // 로고(▶ Music)가 이름이라 이름 글씨는 따로 두지 않는다
        var head = new StackPanel { Spacing = 8 };
        head.Children.Add(new Microsoft.UI.Xaml.Controls.Image
        {
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png")))
                { DecodePixelHeight = 28, DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical },
        });
        // 버전 줄을 누르면 문제 알릴 때 쓸 환경 정보(앱 버전 · Windows 빌드 · 아키텍처)를 복사
        var ver = Theme.Text($"버전 {version}", 14, FontWeights.SemiBold, Theme.Secondary);
        var verHit = new Border { Child = ver, Background = Theme.B(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Left };
        ToolTipService.SetToolTip(verHit, "버전 정보 복사");
        verHit.PointerEntered += (_, _) => ver.Foreground = Theme.B(Colors.White);
        verHit.PointerExited += (_, _) => ver.Foreground = Theme.B(Theme.Secondary);
        verHit.Tapped += (_, _) =>
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText($"YouTube Music {version} · Windows {Environment.OSVersion.Version} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            Toast.Show("버전 정보를 복사했어요");
        };
        head.Children.Add(verHit);
        body.Children.Add(head);
        var legal = new StackPanel { Spacing = 6 };
        legal.Children.Add(Theme.Text(copyright, 13, FontWeights.Normal, Theme.Secondary));
        legal.Children.Add(Theme.Text("Google 의 공식 앱이 아니에요. YouTube Music 은 Google LLC 의 상표예요.", 13, FontWeights.Normal, Theme.Secondary, 3));
        body.Children.Add(legal);
        body.Children.Add(Licenses());
        _ = OneDialog.Show("앱 정보", body, "확인", close: "", width: 420);
    }

    // 앱과 같이 배포하는 오픈소스 — 고지 의무가 있어 이름 · 라이선스 · 원문 링크를 둔다
    private static readonly (string Name, string License, string Url)[] OpenSource =
    [
        (".NET", "MIT", "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT"),
        ("Windows App SDK", "MIT", "https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE"),
        ("Win2D", "MIT", "https://github.com/microsoft/Win2D/blob/main/LICENSE.txt"),
        ("TagLibSharp", "LGPL-2.1", "https://github.com/mono/taglib-sharp/blob/main/COPYING"),
    ];

    /// <summary>"오픈소스 라이선스" 접힌 줄 — 펼치면 구성 요소별 줄, 누르면 원문</summary>
    private static FrameworkElement Licenses()
    {
        var list = new StackPanel { Visibility = Visibility.Collapsed };
        foreach (var (name, license, url) in OpenSource)
        {
            var row = Row(Theme.Text(name, 14, FontWeights.Normal, Colors.White), Theme.Text(license, 13, FontWeights.Normal, Theme.Secondary));
            ToolTipService.SetToolTip(row, url);
            row.Tapped += async (_, _) => await Launcher.LaunchUriAsync(new Uri(url));
            list.Children.Add(row);
        }
        var chevron = Theme.Icon(Icons.ChevronDown, 16, Theme.Secondary, stroke: true);
        var header = Row(Theme.Text("오픈소스 라이선스", 14, FontWeights.SemiBold, Colors.White), chevron);
        header.Tapped += (_, _) =>
        {
            var open = list.Visibility == Visibility.Collapsed;
            list.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            chevron.RenderTransform = new RotateTransform { Angle = open ? 180 : 0 };
        };
        var box = new StackPanel { Margin = new Thickness(-12, 0, -12, 0) };
        box.Children.Add(header);
        box.Children.Add(list);
        return box;

        static Grid Row(FrameworkElement left, FrameworkElement right)
        {
            var g = new Grid { Height = 40, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(12), Background = Theme.B(Colors.Transparent) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            left.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            g.Children.Add(left);
            g.Children.Add(right);
            g.PointerEntered += (_, _) => g.Background = Theme.B(Theme.ChipHover);
            g.PointerExited += (_, _) => g.Background = Theme.B(Colors.Transparent);
            return g;
        }
    }
}
