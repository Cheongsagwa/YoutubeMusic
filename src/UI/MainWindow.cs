using Microsoft.UI.Windowing;
using OneMusic.Core;
using Windows.Graphics;

namespace OneMusic.UI;

/// <summary>
/// 메인 창 — pear-desktop One UI 테마와 같은 배치.
/// 맨 위 32px 창 끌기 / 90px 상단 바(메뉴 · 로고 · 검색) / 왼쪽 72px 레일 / 화면 / 떠 있는 재생 바
/// </summary>
public sealed class MainWindow : Window
{
    public PlayerService Player { get; }
    /// <summary>창이 앞에 있는지 (곡 바뀜 알림은 뒤에 있을 때만)</summary>
    public bool IsActiveWindow { get; private set; } = true;
    public Grid Root { get; } = new();
    public ShelvesView Home { get; private set; } = null!;
    public ShelvesView Explore { get; private set; } = null!;
    public ShelvesView Library { get; private set; } = null!;
    public SearchView Search { get; private set; } = null!;
    public PlayerView PlayerPage { get; private set; } = null!;
    public FloatingBar Bar { get; private set; } = null!;
    /// <summary>열어 둔 상세 화면들 (맨 뒤가 지금 화면) + 그 밑에 깔린 탭 · 검색 화면</summary>
    private readonly List<DetailView> _details = new();
    private FrameworkElement? _underDetails;
    private readonly Grid _wash = new() { IsHitTestVisible = false };
    private readonly Grid _playerWash = new() { IsHitTestVisible = false };
    private readonly ArtBackdrop _backdrop = new();
    /// <summary>재생목록 · 앨범 화면 배경 — 그 목록 아트를 흐려 깐다 (YT Music 처럼, 아래로 갈수록 어두워짐)</summary>
    private readonly Grid _pageWash = new() { IsHitTestVisible = false };
    private readonly ArtBackdrop _pageBackdrop = new() { Strength = 0.85f };
    private string? _pageArt;
    /// <summary>배경 아트 위 어둡게 까는 막 (글씨가 읽히게)</summary>
    private readonly XRect _backdropShade = new() { IsHitTestVisible = false };
    private Border _navGlass = null!;
    private Grid _guide = null!;
    private Grid _layoutGrid = null!;
    private bool _navGlassOn;
    private IconButton _account = null!;
    /// <summary>뒤로 간 상세 화면들 (앞으로 가기용)</summary>
    private readonly List<(string Id, string? Params)> _forward = new();
    private readonly Grid _content = new();
    private Grid _nav = null!;
    private NavRail _rail = null!;
    private AutoSuggestBox _search = null!;
    private CancellationTokenSource? _suggestCts;
    private Color _accent = AlbumColor.Fallback;
    private Color _washColor;
    private DevBridge? _dev;

    private const int MinWidth = 1000, MinHeight = 680;

    /// <summary>처음 창 크기 (사용자가 맞춰 둔 크기)</summary>
    private const int DefaultWidth = 1199, DefaultHeight = 820;

    /// <summary>
    /// 시작 창 크기: 저장된 크기를 쓰되, 너무 작거나(최소화 상태로 저장 등) 화면보다 크면
    /// 기본 크기(1199×820, 화면보다 크면 화면에 맞춤)로 가운데에 연다
    /// </summary>
    private void PlaceWindow()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var s = Settings.Current;
        var w = s.WindowWidth;
        var h = s.WindowHeight;
        if (w < MinWidth || h < MinHeight || w > work.Width || h > work.Height)
        {
            w = Math.Min(DefaultWidth, work.Width);
            h = Math.Min(DefaultHeight, work.Height);
        }
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }

    public MainWindow()
    {
        Title = AppIdentity.Name;
        Player = new PlayerService(DispatcherQueue.GetForCurrentThread());
        ExtendsContentIntoTitleBar = true;
        PlaceWindow();
        // 작업 표시줄 · Alt+Tab 아이콘 (앱아이콘.svg → Assets/app.ico)
        var icon = IOPath.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        AppWindow.Closing += (_, _) =>
        {
            var size = IsMini ? _normalSize : AppWindow.Size;
            // 최소화한 채로 닫으면 창 크기가 손톱만 하게 저장돼서 다음에 그렇게 열렸다 → 그럴 땐 저장하지 않는다
            var minimized = AppWindow.Presenter is OverlappedPresenter op && op.State == OverlappedPresenterState.Minimized;
            if (!minimized && size.Width >= MinWidth && size.Height >= MinHeight)
            {
                Settings.Current.WindowWidth = size.Width;
                Settings.Current.WindowHeight = size.Height;
            }
            Settings.Current.Save();
            App.SaveSession();
            // 이어 듣기: 지금 목록 · 위치를 남긴다
            if (Settings.Current.ResumeSession) Player.Snapshot().Save();
            Notifier.Unregister();
        };
        Activated += (_, e) =>
        {
            IsActiveWindow = e.WindowActivationState != WindowActivationState.Deactivated;
            if (IsActiveWindow) return;
            Tip.Hide();
            MemoryTrim.Later(DispatcherQueue, () => !IsActiveWindow);
        };
        // 최소화하면 바로 쓰지 않는 메모리를 돌려준다 (화면 품질과 무관 — 다시 열 때 필요한 만큼 돌아온다)
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidPresenterChange || e.DidSizeChange)
                if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) MemoryTrim.Now();
        };
        Build();
        LyricsState.Instance.Load(null);
        Player.TrackChanged += OnTrack;
        // 작업 표시줄 미리보기의 이전 · 재생 · 다음 (pear taskbar-mediacontrol)
        TaskbarButtons.Attach(this);
        if (Log.DevRoot != null) _dev = new DevBridge(this);
    }

    private void Build()
    {
        Root.Background = Theme.B(Theme.Bg);
        Root.RequestedTheme = ElementTheme.Dark;
        // 메인 화면 배경: YT Music 기본 무늬 (위쪽 청록 → 회청 → 올리브 띠가 아래로 바탕색에 녹아듦)
        Root.Children.Add(BuildHomeBackground());
        // 재생목록 · 앨범 화면: 그 목록 아트 블러 (위는 아트 색, 아래로 바탕색에 녹아듦)
        _pageWash.Background = Theme.B(Theme.Bg);
        Anim.V(_pageWash).Opacity = 0;
        _pageWash.Children.Add(_pageBackdrop);
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop { Color = Theme.Bg.WithAlpha(0.58), Offset = 0 });
        fade.GradientStops.Add(new GradientStop { Color = Theme.Bg.WithAlpha(0.82), Offset = 0.45 });
        fade.GradientStops.Add(new GradientStop { Color = Theme.Bg.WithAlpha(0.96), Offset = 1 });
        _pageWash.Children.Add(new XRect { Fill = fade });
        Root.Children.Add(_pageWash);
        // 앨범아트 블러 + 앨범 색 번짐 — 재생 화면에서만 (메인 화면엔 앨범 색을 입히지 않는다)
        _wash.Background = Theme.B(Theme.Bg);
        Anim.V(_wash).Opacity = 0;
        Root.Children.Add(_wash);
        _wash.Children.Add(_backdrop);
        _backdropShade.Fill = Theme.B(Theme.Bg.WithAlpha(0.36));
        _wash.Children.Add(_backdropShade);
        // 재생 화면: 앨범 색을 더 진하게 (pear 22 — 900×700 at 30% 45%, 50%)
        Root.Children.Add(_playerWash);
        Anim.V(_playerWash).Opacity = 0;
        Root.SizeChanged += (_, _) => SetWash(_accent, false);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.Children.Add(layout);

        // 창 끌기 영역
        var drag = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetColumnSpan(drag, 2);
        layout.Children.Add(drag);
        SetTitleBar(drag);
        _drag = drag;

        // 상단 바: ≡ · 로고 · 검색(480×40 알약) · 설정
        _nav = new Grid();
        Grid.SetRow(_nav, 1);
        Grid.SetColumnSpan(_nav, 2);
        var menu = new IconButton(40) { Content = Theme.Icon(24, Colors.White, Icons.Drawer), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 0, 0, 0) };
        _nav.Children.Add(menu);
        // 로고 (내부 아이콘.svg → Assets/logo.png) — 누르면 홈으로 (YT Music 처럼)
        var logo = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(68, 0, 0, 0),
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        logo.Children.Add(new Image
        {
            Height = 26,
            Stretch = Stretch.Uniform,
            // 화면 크기 그대로 디코딩 (DPI 반영) — 3배로 받아 GPU 가 줄이면 테두리가 계단졌다
            Source = new BitmapImage(new Uri(IOPath.Combine(AppContext.BaseDirectory, "Assets", "logo.png"))) { DecodePixelHeight = 26, DecodePixelType = DecodePixelType.Logical },
        });
        Anim.Pressable(logo);
        Tip.Set(logo, "홈");
        logo.Tapped += (_, _) => GoHome();
        _nav.Children.Add(logo);
        _nav.Children.Add(BuildSearch());
        // 오른쪽: 프로필 사진(누르면 메뉴)만 — 뒤로는 화면 안 ‹ · 마우스 뒤로 버튼 · Esc
        var rightBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 100, 0) };
        var settings = new IconButton(40) { Content = Theme.Icon(24, Colors.White, Icons.Settings) };
        Tip.Set(settings, "계정");
        settings.Click += () => AccountMenu.Show(this, settings);
        rightBar.Children.Add(settings);
        _nav.Children.Add(rightBar);
        _account = settings;
        LoadAvatar();
        layout.Children.Add(_nav);

        // 왼쪽 레일
        _rail = new NavRail();
        Grid.SetRow(_rail, 2);
        _rail.Changed += ShowTab;
        layout.Children.Add(_rail);

        // 화면
        Home = new ShelvesView("FEmusic_home", withChips: true);
        Explore = new ShelvesView("FEmusic_explore", withChips: true) { Visibility = Visibility.Collapsed };
        Library = new ShelvesView("FEmusic_library_landing", withChips: true) { Visibility = Visibility.Collapsed };
        Search = new SearchView { Visibility = Visibility.Collapsed };
        foreach (var v in new FrameworkElement[] { Home, Explore, Library, Search }) _content.Children.Add(v);
        // 화면은 창 맨 위부터 — 위 122px(창 끌기 + 상단 바) 밑으로 흘러 들어가며 유리 상단 바 뒤로 흐리게 비친다 (pear 6 · 22)
        Grid.SetRow(_content, 0);
        Grid.SetRowSpan(_content, 3);
        Grid.SetColumn(_content, 1);
        Canvas.SetZIndex(_content, 0);
        layout.Children.Add(_content);
        // 상단 바 유리 — 내용이 스크롤되면 나타난다 (YT: nav-bar-background, opacity medium ease)
        _navGlass = new Border { Background = Theme.Glass(0.55, Color.FromArgb(255, 36, 36, 40)), IsHitTestVisible = false };
        Grid.SetRowSpan(_navGlass, 2);
        Grid.SetColumnSpan(_navGlass, 2);
        Canvas.SetZIndex(_navGlass, 1);
        Anim.V(_navGlass).Opacity = 0;
        layout.Children.Add(_navGlass);
        Canvas.SetZIndex(drag, 2);
        Canvas.SetZIndex(_nav, 3);
        Canvas.SetZIndex(_rail, 3);
        ScrollPage.ScrolledChanged += (page, scrolled) => { if (page.Visibility == Visibility.Visible) SetNavGlass(scrolled); };
        // 펼친 사이드바 (≡)
        _guide = BuildGuide();
        Grid.SetRow(_guide, 2);
        Grid.SetColumnSpan(_guide, 2);
        Canvas.SetZIndex(_guide, 4);
        layout.Children.Add(_guide);
        menu.Click += ToggleGuide;
        _layoutGrid = layout;

        // 재생 화면 (상단 바 · 레일까지 덮음)
        PlayerPage = new PlayerView();
        Grid.SetRow(PlayerPage, 1);
        Grid.SetRowSpan(PlayerPage, 2);
        Grid.SetColumnSpan(PlayerPage, 2);
        Canvas.SetZIndex(PlayerPage, 5);
        layout.Children.Add(PlayerPage);

        // 떠 있는 재생 바
        Bar = new FloatingBar
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(Theme.FloatGap, 0, Theme.FloatGap, Theme.FloatGap),
        };
        Bar.OpenPlayer += TogglePlayer;
        Root.Children.Add(Anim.ShadowFor(Bar, Bar.CornerRadius, 24));
        Root.Children.Add(Bar);
        Toast.Attach(Root);
        _drop = new SearchDropdown(Root);
        _drop.Chosen += Suggest;
        _drop.Remove = RemoveHistory;
        Tip.Attach(Root);
        OneMenu.Attach(Root);
        OneDialog.Attach(Root);

        Content = Root;
        Root.Loaded += (_, _) =>
        {
            Home.LoadOnce();
            if (Settings.Current.GuideOpen) ToggleGuide();
            // 지난번에 고른 출력 장치
            if (Settings.Current.AudioDevice is { } dev) _ = Player.SetOutput(dev);
            // 이어 듣기: 지난번 목록 · 위치 (멈춘 채로)
            if (Settings.Current.ResumeSession && SessionState.Load() is { } session) Player.Restore(session, Settings.Current.PlayOnStart);
            if (Player.Current == null) ShowBar(false);
        };
        // 단축키 — 목록 · 스크롤이 키를 먼저 써도(handled) 받도록
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            _tabPressed = e.Key == Windows.System.VirtualKey.Tab;
            if (e.Key != Windows.System.VirtualKey.Escape && !OneDialog.IsOpen && Shortcuts.Handle(this, e)) { e.Handled = true; return; }
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            if (PlayerPage.IsFullScreen) { PlayerPage.ToggleFullScreen(); e.Handled = true; return; }
            // 대화상자는 입력 칸에서 Esc 를 눌러도 닫는다
            if (OneDialog.IsOpen) { OneDialog.Cancel(); e.Handled = true; return; }
            if (e.Handled) return;
            if (OneMenu.IsOpen) OneMenu.CloseAll();
            else if (PlayerPage.IsOpen) ClosePlayer();
            else if (e.OriginalSource is not TextBox) Back();
        }), true);
        // 마우스 뒤로 · 앞으로 버튼
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var pt = e.GetCurrentPoint(Root).Properties;
            if (pt.IsXButton2Pressed) Forward();
            else if (!pt.IsXButton1Pressed) return;
            else if (PlayerPage.IsOpen) ClosePlayer();
            else Back();
            e.Handled = true;
        }), true);
    }

    /// <summary>
    /// YT Music 메인 화면 기본 배경 — 사용자가 준 YT Music 화면에서 카드 · 글자를 빼고 바탕색만 뽑아 부드럽게 늘린 그림
    /// (Assets/home-bg.png: 왼쪽 위 청록 · 가운데 왼쪽 올리브 · 오른쪽 위 블루 · 가운데 오른쪽 진한 남색 → 아래로 #17171a).
    /// 창 폭에 맞춰 늘리고 위에 붙인다
    /// </summary>
    private static FrameworkElement BuildHomeBackground() => new Image
    {
        IsHitTestVisible = false,
        Stretch = Stretch.UniformToFill,
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Center,
        Source = new BitmapImage(new Uri(IOPath.Combine(AppContext.BaseDirectory, "Assets", "home-bg.png"))),
    };

    /// <summary>검색 알약 — 480×40, 반투명 흰 배경, 왼쪽 돋보기 (pear 테마와 같은 위치 x=172)</summary>
    private FrameworkElement BuildSearch()
    {
        var pill = new Grid
        {
            Width = 480,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            // pear 22: 검색창은 흰 12% 유리 알약 (뒤가 흐리게)
            Background = Theme.GlassLight(0.12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(172, 0, 0, 0),
        };
        var icon = Theme.Icon(18, Theme.Secondary, Icons.SearchSesl);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.Margin = new Thickness(17, 0, 0, 0);
        pill.Children.Add(icon);
        _search = new AutoSuggestBox
        {
            PlaceholderText = "노래, 앨범, 아티스트, 팟캐스트 검색",
            FontFamily = Theme.Font,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40, 0, 8, 0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        // 입력칸의 기본 배경 · 테두리(포커스 때 진해지는 것) 지우기
        var clear = new SolidColorBrush(Colors.Transparent);
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
                 })
            _search.Resources[key] = clear;
        _search.Resources["TextControlForeground"] = Theme.B(Colors.White);
        _search.Resources["TextControlForegroundFocused"] = Theme.B(Colors.White);
        _search.Resources["TextControlPlaceholderForeground"] = Theme.B(Theme.Secondary);
        _search.Resources["TextControlPlaceholderForegroundFocused"] = Theme.B(Theme.Secondary);
        _search.TextChanged += OnSearchText;
        // 추천 줄은 글자가 아니라 화면 요소라서, 고를 때 입력칸 글자를 바꾸지 않는다
        _search.UpdateTextOnSelect = false;
        _search.QuerySubmitted += (box, e) =>
        {
            // ↑↓ 로 고른 추천이 있으면 그것, 아니면 입력한 글자로 검색
            var chosen = _drop?.Selected;
            _drop?.Hide();
            if (chosen != null) { Suggest(chosen); return; }
            if (!string.IsNullOrWhiteSpace(e.QueryText)) ShowSearch(e.QueryText.Trim());
        };
        _search.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (_drop == null) return;
            if (e.Key == Windows.System.VirtualKey.Down) { _drop.Move(1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Up) { _drop.Move(-1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Escape && _drop.IsOpen) { _drop.Hide(); e.Handled = true; }
        }), true);
        _search.LostFocus += async (_, _) =>
        {
            await Task.Delay(180);
            if (_drop is { PointerInside: false }) _drop.Hide();
        };
        // 눌렀을 때 비어 있으면 최근 검색어 (YT Music 검색창)
        // 사용자가 직접 들어왔을 때만 띄운다: 검색창을 누름 · Tab 으로 옴 · / 또는 Ctrl+F (FocusSearch).
        // 창이 뜨거나 다시 앞으로 올 때 윈도우가 저절로 주는 포커스(Programmatic/Keyboard 둘 다 옴)엔 띄우지 않는다
        // — 앱 켜자마자 최근 검색어 줄이 떠 있던 문제
        // (창이 뜰 때도 FocusState 가 Pointer 로 와서 상태만으로는 못 가른다 → 검색창 위에서 실제로 누른 것만 인정.
        //  GotFocus 는 누름 처리 뒤에 따로 오므로, 손을 떼면 다음 차례에 표시를 지운다)
        _search.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _focusWanted = true), true);
        _search.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) =>
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _focusWanted = false)), true);
        _search.GotFocus += (_, e) =>
        {
            var fs = (e.OriginalSource as Control)?.FocusState ?? FocusState.Unfocused;
            var wanted = _focusWanted || (fs == FocusState.Keyboard && _tabPressed);
            _focusWanted = false;
            _tabPressed = false;
            if (wanted) OnSearchText(_search, null);
        };
        pill.Children.Add(_search);
        _searchPill = pill;
        return pill;
    }

    private void ShowTab(int i)
    {
        var views = new ShelvesView[] { Home, Explore, Library };
        // 이미 보고 있는 탭을 다시 누르면 맨 위로
        if (_details.Count == 0 && !PlayerPage.IsOpen && Search.Visibility != Visibility.Visible && views[i].Visibility == Visibility.Visible)
        {
            views[i].ScrollToTop();
            return;
        }
        ClosePlayer();
        ClearDetails();
        Search.Visibility = Visibility.Collapsed;
        for (var k = 0; k < views.Length; k++)
        {
            var show = k == i;
            if (show && views[k].Visibility != Visibility.Visible) Anim.Appear(views[k], 0, 12);
            views[k].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        views[i].LoadOnce();
        SyncNavGlass();
    }

    public void ShowHome() => _rail.Select(0);
    public void SelectTab(int i) => _rail.Select(i);

    /// <summary>개발용: 지금 화면을 y 까지 스크롤</summary>
    public void ScrollCurrent(double y) => _content.Children.OfType<ScrollPage>().LastOrDefault(v => v.Visibility == Visibility.Visible)?.ScrollTo(y);

    /// <summary>로고를 누르면: 재생 화면 · 상세 화면 · 검색을 닫고 홈 (이미 홈이면 맨 위로)</summary>
    public void GoHome()
    {
        _drop?.Hide();
        if (_rail.Selected == 0) ShowTab(0);
        else _rail.Select(0);
    }

    // ───────────────────────── 펼친 사이드바 (≡) ─────────────────────────

    private bool _guideOpen;
    private readonly List<(Border Pill, TextBlock Label, FrameworkElement Outline, FrameworkElement Filled)> _guideItems = new();
    private readonly StackPanel _guideLists = new() { Spacing = 2 };

    /// <summary>
    /// ≡ 를 누르면 펼쳐지는 사이드바 (YT Music guide) — 홈 · 둘러보기 · 보관함 + 새 재생목록 + 내 재생목록들.
    /// 미끄러지며 나타나고 사라진다 (pear 39: transform medium ease · opacity), 화면은 옆으로 비켜 간다
    /// </summary>
    private Grid BuildGuide()
    {
        // pear: ytmusic-guide-renderer 도 유리 (--oneui-glass + blur)
        var g = new Grid { Width = 240, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Background = Theme.Glass(0.42, Color.FromArgb(255, 36, 36, 40)), CornerRadius = new CornerRadius(0, 26, 26, 0) };
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(8, 16, 8, Theme.BarHeight + Theme.FloatGap * 2) };
        var stack = new StackPanel { Spacing = 2 };
        var entries = new[] { ("홈", Icons.HomeOutline, Icons.HomeFilled), ("둘러보기", Icons.ExploreOutline, Icons.ExploreFilled), ("보관함", Icons.LibraryOutline, Icons.LibraryFilled) };
        for (var i = 0; i < entries.Length; i++)
        {
            var idx = i;
            var (label, outline, filled) = entries[i];
            var row = new Grid { Height = 48, Background = new SolidColorBrush(Colors.Transparent) };
            var pill = new Border { CornerRadius = new CornerRadius(23), Background = Theme.B(Color.FromArgb(128, 80, 80, 84)) };
            Anim.V(pill).Opacity = 0;
            row.Children.Add(pill);
            var hover = new Border { CornerRadius = new CornerRadius(23), Background = Theme.B(Color.FromArgb(15, 255, 255, 255)) };
            Anim.V(hover).Opacity = 0;
            row.Children.Add(hover);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            // 선 · 채움 아이콘을 겹쳐 두고 고른 탭만 채움으로 (레일과 같게)
            var icons = new Grid { Width = 24, Height = 24 };
            var o = Theme.Icon(24, Colors.White, outline);
            var f = Theme.Icon(24, Colors.White, filled);
            icons.Children.Add(o);
            icons.Children.Add(f);
            Anim.V(f).Opacity = 0;
            line.Children.Add(icons);
            var t = Theme.Text(label, 16, FontWeights.SemiBold, Theme.OnSurface);
            t.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(t);
            row.Children.Add(line);
            row.PointerEntered += (_, _) => Anim.Opacity(hover, 1, Anim.Medium);
            row.PointerExited += (_, _) => Anim.Opacity(hover, 0, Anim.Medium);
            row.Tapped += (_, _) => _rail.Select(idx);
            Anim.Pressable(row, 0.96f);
            _guideItems.Add((pill, t, o, f));
            stack.Children.Add(row);
        }
        stack.Children.Add(new XRect { Height = 1, Fill = Theme.B(Theme.Divider), Margin = new Thickness(12, 14, 12, 14) });
        // 새 재생목록 — 재생목록 줄과 같은 모양의 한 줄 (목록 맨 위, 무거운 알약 대신)
        var add = new Grid { Height = 44, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Colors.Transparent), Padding = new Thickness(16, 0, 12, 0) };
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        addRow.Children.Add(Theme.Icon(Icons.Plus, 18, Theme.Secondary, stroke: true, strokeWidth: 2));
        var addText = Theme.Text("새 재생목록", 14, FontWeights.SemiBold, Theme.Secondary);
        addText.VerticalAlignment = VerticalAlignment.Center;
        addRow.Children.Add(addText);
        add.Children.Add(addRow);
        add.PointerEntered += (_, _) => add.Background = Theme.B(Color.FromArgb(15, 255, 255, 255));
        add.PointerExited += (_, _) => add.Background = new SolidColorBrush(Colors.Transparent);
        Anim.Pressable(add);
        add.Tapped += async (_, _) =>
        {
            var id = await NewPlaylistDialog.Create(Array.Empty<string>());
            if (id != null) LoadGuideLists();
        };
        stack.Children.Add(add);
        stack.Children.Add(_guideLists);
        sv.Content = stack;
        g.Children.Add(sv);
        _rail.Changed += SyncGuide;
        SyncGuide(_rail.Selected);
        return g;
    }

    private void SyncGuide(int idx)
    {
        for (var i = 0; i < _guideItems.Count; i++)
        {
            var (pill, label, o, f) = _guideItems[i];
            var on = i == idx;
            Anim.Opacity(pill, on ? 1 : 0, Anim.Medium);
            label.FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold;
            Anim.Opacity(o, on ? 0 : 1, Anim.Fast);
            Anim.Opacity(f, on ? 1 : 0, Anim.Fast);
        }
    }

    /// <summary>내 재생목록 (보관함 → 재생목록) 을 사이드바에</summary>
    private async void LoadGuideLists()
    {
        _guideLists.Children.Clear();
        if (!Auth.IsLoggedIn) return;
        try
        {
            var shelves = await InnerTube.Instance.BrowseShelves("FEmusic_liked_playlists");
            foreach (var c in shelves.SelectMany(x => x.Items).Where(c => c.BrowseId != null || c.PlaylistId != null).Take(60))
            {
                // 이름만 — 부제(내 이름 · 트랙 수)는 줄마다 같은 말이라 뺐다
                var row = new Grid { Height = 44, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Colors.Transparent), Padding = new Thickness(18, 0, 12, 0) };
                var name = Theme.Text(c.Title, 14, FontWeights.SemiBold, Theme.OnSurface);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.Margin = new Thickness(0, 0, 40, 0);
                row.Children.Add(name);
                // 마우스를 올리면 오른쪽에 흰 원 재생 버튼 — 누르면 그 재생목록을 바로 튼다 (YT Music 사이드바)
                var play = new IconButton(32) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                play.HoverBrush = new SolidColorBrush(Colors.Transparent);
                var pg = new Grid();
                pg.Children.Add(new XEllipse { Width = 28, Height = 28, Fill = Theme.B(Colors.White) });
                var tri = Theme.Icon(Icons.Play, 14, Colors.Black);
                tri.Margin = new Thickness(2, 0, 0, 0);
                pg.Children.Add(tri);
                play.Content = pg;
                Anim.V(play).Opacity = 0;
                Tip.Set(play, "재생");
                play.Click += () => Play.Card(c with { PageType = "play" });
                row.Children.Add(play);
                row.PointerEntered += (_, _) =>
                {
                    row.Background = Theme.B(Color.FromArgb(15, 255, 255, 255));
                    Anim.Opacity(play, 1, Anim.Fast);
                    play.IsHitTestVisible = true;
                };
                row.PointerExited += (_, _) =>
                {
                    row.Background = new SolidColorBrush(Colors.Transparent);
                    Anim.Opacity(play, 0, Anim.Fast);
                    play.IsHitTestVisible = false;
                };
                row.Tapped += (_, _) => { if (c.BrowseId != null) OpenBrowse(c.BrowseId); else Play.Card(c); };
                row.RightTapped += (_, e) => { e.Handled = true; TrackMenu.ShowCard(row, c, e.GetPosition(null)); };
                _guideLists.Children.Add(row);
            }
        }
        catch (Exception e) { Log.Warn($"사이드바 재생목록 실패: {e.Message}"); }
    }

    public void ToggleGuide()
    {
        _guideOpen = !_guideOpen;
        Settings.Current.GuideOpen = _guideOpen;
        var from = _content.Margin.Left;
        var to = _guideOpen ? 240 - 72 : 0;
        Tween.Run(_content, Anim.Medium, t => RepeatIcon.Curve(Anim.Ease, t), k => _content.Margin = new Thickness(from + (to - from) * k, 0, 0, 0));
        if (_guideOpen)
        {
            if (_guideLists.Children.Count == 0) LoadGuideLists();
            _guide.Visibility = Visibility.Visible;
            Anim.SetTranslation(_guide, new Vector3(-40, 0, 0));
            Anim.V(_guide).Opacity = 0;
            Anim.Translate(_guide, Vector3.Zero);
            Anim.Opacity(_guide, 1, Anim.Medium);
            Anim.Opacity(_rail, 0, Anim.Fast);
            _rail.IsHitTestVisible = false;
        }
        else
        {
            Anim.Translate(_guide, new Vector3(-40, 0, 0));
            Anim.Opacity(_guide, 0, Anim.Fast);
            Anim.Opacity(_rail, 1, Anim.Medium);
            _rail.IsHitTestVisible = true;
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(Anim.Medium);
                if (!_guideOpen) _guide.Visibility = Visibility.Collapsed;
            });
        }
    }

    /// <summary>상단 바 유리 — 지금 화면이 스크롤되어 있으면 보인다</summary>
    private void SetNavGlass(bool on)
    {
        if (on == _navGlassOn) return;
        _navGlassOn = on;
        if (!PlayerPage.IsOpen) Anim.Opacity(_navGlass, on ? 1 : 0, Anim.Medium);
    }

    private void SyncNavGlass()
    {
        var page = _content.Children.OfType<ScrollPage>().LastOrDefault(v => v.Visibility == Visibility.Visible);
        SetNavGlass(page?.IsScrolled == true);
    }

    public void FocusSearch()
    {
        ClosePlayer();
        _focusWanted = true;
        _search.Focus(FocusState.Programmatic);
    }

    private bool _focusWanted, _tabPressed;

    public void ReloadAll()
    {
        InnerTube.Instance.ForgetAccount();
        LoadAvatar();
        if (_guideOpen) LoadGuideLists();
        else _guideLists.Children.Clear();
        Home.Reload();
        Explore.Reload();
        Library.Reload();
    }

    public void ShowSearch(string q)
    {
        _drop?.Hide();
        var recent = Settings.Current.RecentSearches;
        recent.Remove(q);
        recent.Insert(0, q);
        if (recent.Count > 20) recent.RemoveRange(20, recent.Count - 20);
        Settings.Current.Save();
        ClosePlayer();
        ClearDetails();
        _search.Text = q;
        foreach (var v in new FrameworkElement[] { Home, Explore, Library }) v.Visibility = Visibility.Collapsed;
        Search.Visibility = Visibility.Visible;
        Anim.Appear(Search, 0, 12);
        Search.Search(q);
    }

    /// <summary>앨범 · 플레이리스트 · 아티스트 상세 화면을 지금 화면 위에 연다 (뒤로 가면 원래 화면 · 스크롤 위치 그대로)</summary>
    public void OpenBrowse(string browseId, string? @params = null) => OpenBrowse(browseId, @params, fromForward: false);

    private void OpenBrowse(string browseId, string? @params, bool fromForward)
    {
        ClosePlayer();
        if (!fromForward) _forward.Clear();
        var current = _content.Children.OfType<FrameworkElement>().LastOrDefault(v => v.Visibility == Visibility.Visible);
        if (_details.Count == 0) _underDetails = current;
        if (current != null) current.Visibility = Visibility.Collapsed;
        // 너무 깊어지면 가장 오래된 상세 화면부터 버린다 (메모리)
        if (_details.Count >= 8)
        {
            _content.Children.Remove(_details[0]);
            _details.RemoveAt(0);
        }
        // 뒤에 숨은 상세 화면은 바로 밑 1장만 살려 두고, 더 아래는 빈 껍데기로 바꿔 둔다 — 뒤로 가면 그때 다시 불러온다 (메모리)
        if (_details.Count >= 2 && _content.Children.Contains(_details[^2]))
        {
            var old = _details[^2];
            _content.Children.Remove(old);
            _details[^2] = NewDetail(old.BrowseId, old.Params);
        }
        var d = NewDetail(browseId, @params);
        _details.Add(d);
        _content.Children.Add(d);
        Anim.Appear(d, 0, 12);
        d.Load();
        SetNavGlass(false);
        SyncPageBg();
    }

    private DetailView NewDetail(string browseId, string? @params)
    {
        var d = new DetailView(browseId, @params);
        d.BackRequested += Back;
        d.ArtReady += SyncPageBg;
        return d;
    }

    /// <summary>맨 위 상세 화면(재생목록 · 앨범)의 아트로 배경을 바꾸거나, 상세 화면이 아니면 원래 배경으로</summary>
    private void SyncPageBg()
    {
        var top = _details.Count > 0 && _details[^1].Visibility == Visibility.Visible ? _details[^1] : null;
        var art = top?.Art;
        if (art == null)
        {
            Anim.Opacity(_pageWash, 0, Anim.Medium);
            return;
        }
        if (art != _pageArt)
        {
            _pageArt = art;
            _pageBackdrop.Show(art);
        }
        Anim.Opacity(_pageWash, 1, Anim.Medium);
    }

    public void Forward()
    {
        if (_forward.Count == 0) return;
        var (id, p) = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        OpenBrowse(id, p, fromForward: true);
    }

    /// <summary>계정 버튼을 프로필 사진으로 (로그인했을 때)</summary>
    private async void LoadAvatar()
    {
        if (!Auth.IsLoggedIn) return;
        try
        {
            var url = await InnerTube.Instance.AccountPhoto();
            if (url == null) return;
            _account.Content = new Cover(url, 32, 32, 32, 16, shimmer: false);
        }
        catch (Exception e) { Log.Warn($"프로필 사진 실패: {e.Message}"); }
    }

    public void Back()
    {
        if (_details.Count == 0) return;
        _forward.Add((_details[^1].BrowseId, _details[^1].Params));
        _content.Children.Remove(_details[^1]);
        _details.RemoveAt(_details.Count - 1);
        FrameworkElement? prev = _details.Count > 0 ? _details[^1] : _underDetails;
        if (prev == null) return;
        // 메모리 때문에 비워 둔 화면이면 지금 다시 불러온다
        if (prev is DetailView pd && !_content.Children.Contains(pd))
        {
            _content.Children.Add(pd);
            pd.Load();
        }
        prev.Visibility = Visibility.Visible;
        Anim.Appear(prev, 0, 12);
        SyncNavGlass();
        SyncPageBg();
    }

    private void ClearDetails()
    {
        foreach (var d in _details) _content.Children.Remove(d);
        _details.Clear();
        _underDetails = null;
        _forward.Clear();
        SyncPageBg();
    }

    public void TogglePlayer()
    {
        if (PlayerPage.IsOpen) ClosePlayer();
        else OpenPlayer();
    }

    public void OpenPlayer()
    {
        PlayerPage.Open();
        Anim.Opacity(_wash, 1, Anim.Morph);
        Anim.Opacity(_playerWash, 1, Anim.Morph);
        Anim.Opacity(_navGlass, 0, Anim.Fast);
        Bar.SetPlayerOpen(true);
        // 펼친 사이드바(≡)도 같이 숨긴다 — 안 숨기면 반투명 재생 화면 뒤로 비쳤다
        foreach (var el in new UIElement[] { _content, _nav, _rail, _guide })
        {
            Anim.Opacity(el, 0, 220);
            el.IsHitTestVisible = false;
        }
    }

    public void ClosePlayer()
    {
        if (!PlayerPage.IsOpen) return;
        PlayerPage.Close();
        Anim.Opacity(_wash, 0, Anim.Medium);
        Anim.Opacity(_playerWash, 0, Anim.Medium);
        if (_navGlassOn) Anim.Opacity(_navGlass, 1, Anim.Medium);
        Bar.SetPlayerOpen(false);
        foreach (var el in new UIElement[] { _content, _nav, _rail, _guide })
        {
            Anim.Opacity(el, 1, 320);
            el.IsHitTestVisible = true;
        }
        // 사이드바가 펼쳐져 있으면 레일은 숨긴 채로 (ToggleGuide 와 같게)
        if (_guideOpen)
        {
            Anim.Opacity(_rail, 0, 1);
            _rail.IsHitTestVisible = false;
        }
    }

    private Grid? _searchPill;
    private SearchDropdown? _drop;

    /// <summary>추천을 고르면: 검색어는 검색, 곡은 재생(검색에서 튼 곡 → 자동재생 토글), 앨범 · 아티스트는 그 화면</summary>
    private void Suggest(object tag)
    {
        switch (tag)
        {
            case string q:
                ShowSearch(q);
                break;
            case SearchItem { Track: { } t }:
                _ = Player.PlaySong(t, fromSearch: true);
                break;
            case SearchItem { BrowseId: { } id }:
                OpenBrowse(id);
                break;
        }
    }

    /// <summary>최근 검색어 ✕ — YT 기록이면 YT 에서, 이 PC 기록이면 여기서 지운다</summary>
    private async Task<bool> RemoveHistory(Suggestion s)
    {
        Settings.Current.RecentSearches.Remove(s.Text);
        Settings.Current.Save();
        if (s.RemoveToken is null or "local" or "") return true;
        try { return await InnerTube.Instance.Feedback(s.RemoveToken); }
        catch (Exception e)
        {
            Log.Warn($"검색 기록 지우기 실패: {e.Message}");
            Toast.Show("지우지 못했어요");
            return false;
        }
    }

    private bool SearchHasFocus()
    {
        if (_search.XamlRoot == null) return false;
        var f = FocusManager.GetFocusedElement(_search.XamlRoot) as DependencyObject;
        while (f != null)
        {
            if (f == _search) return true;
            f = VisualTreeHelper.GetParent(f);
        }
        return false;
    }

    /// <summary>개발용 진단: 추천 검색어를 받아 오고 드롭다운을 띄워 크기를 알려 준다</summary>
    public async Task<string> TestSuggest(string q)
    {
        var (words, items) = await InnerTube.Instance.SuggestionsFull(q, CancellationToken.None);
        if (_drop == null || _searchPill == null) return "no drop";
        _drop.Show(_searchPill, words, items);
        await Task.Delay(600);
        return $"words {words.Count} items {items.Count} open {_drop.IsOpen} | {_drop.Describe()}";
    }

    private async void OnSearchText(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs? e)
    {
        if (e != null && e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _suggestCts?.Cancel();
        var q = sender.Text.Trim();
        var cts = _suggestCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(q.Length == 0 ? 0 : 180, cts.Token);
            var (words, items) = Auth.IsLoggedIn ? await InnerTube.Instance.SuggestionsFull(q, cts.Token) : (new List<Suggestion>(), new List<SearchItem>());
            if (cts.IsCancellationRequested || _searchPill == null || _drop == null) return;
            // 비어 있는데 YT 기록이 없으면 이 PC 의 최근 검색어
            if (q.Length == 0 && words.Count == 0)
                words = Settings.Current.RecentSearches.Take(10).Select(t => new Suggestion(t, "local")).ToList();
            if (q.Length == 0 && words.Count == 0) { _drop.Hide(); return; }
            // 글자를 칠 때 포커스는 검색창 '안의 입력칸'에 있어서 검색창 자신은 Unfocused 로 보인다 → 안쪽까지 확인
            if (!SearchHasFocus() && !_drop.PointerInside) return;
            _drop.Show(_searchPill, words, items);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warn($"추천 검색어 실패: {ex.Message}"); }
    }

    // ───────────────────────── 미니 플레이어 (pear picture-in-picture) ─────────────────────────

    private MiniPlayer? _mini;
    private Grid _drag = null!;
    private SizeInt32 _normalSize;
    private PointInt32 _normalPos;
    public bool IsMini { get; private set; }

    /// <summary>작게 '항상 위' 로 ↔ 원래대로 (창 크기 · 자리도 돌려 놓는다)</summary>
    public void ToggleMini()
    {
        if (_mini == null)
        {
            _mini = new MiniPlayer();
            _mini.ExitRequested += ToggleMini;
            Root.Children.Add(_mini);
        }
        IsMini = !IsMini;
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        if (IsMini)
        {
            _normalSize = AppWindow.Size;
            _normalPos = AppWindow.Position;
            OneMenu.CloseAll();
            ClosePlayer();
            _drop?.Hide();
            _layoutGrid.Visibility = Visibility.Collapsed;
            Bar.Visibility = Visibility.Collapsed;
            _mini.Visibility = Visibility.Visible;
            _mini.Start();
            SetTitleBar(_mini.DragArea);
            AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            AppWindow.Resize(new SizeInt32((int)(340 * scale), (int)(340 * scale)));
        }
        else
        {
            _mini.Stop();
            _mini.Visibility = Visibility.Collapsed;
            _layoutGrid.Visibility = Visibility.Visible;
            Bar.Visibility = Visibility.Visible;
            SetTitleBar(_drag);
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            AppWindow.Resize(_normalSize);
            AppWindow.Move(_normalPos);
            Anim.Appear(_layoutGrid, 0, 0);
        }
    }

    private bool _barShown = true;
    private string? _notifiedFor;

    /// <summary>재생할 곡이 없으면 재생 바를 아래로 내려 숨긴다 (YT Music: 처음 · 현재 재생목록 닫기)</summary>
    private void ShowBar(bool show)
    {
        if (show == _barShown) return;
        _barShown = show;
        Bar.IsHitTestVisible = show;
        Anim.Slide(Bar, new Vector3(0, show ? 0 : (float)(Theme.BarHeight + Theme.FloatGap * 2), 0), 640);
        Anim.Opacity(Bar, show ? 1 : 0, show ? Anim.Medium : Anim.Fast);
        if (!show) ClosePlayer();
    }

    private async void OnTrack()
    {
        var t = Player.Current;
        LyricsState.Instance.Load(t);
        ShowBar(t != null);
        if (t == null) return;
        if (t.VideoId != _notifiedFor)
        {
            _notifiedFor = t.VideoId;
            Notifier.TrackChanged(t, IsActiveWindow);
            // 곡이 바뀔 때마다 이어 듣기 상태를 남긴다 — 창을 닫지 않고 끝나도(강제 종료 · 꺼짐) 다음에 이 곡부터
            if (Settings.Current.ResumeSession) Player.Snapshot().Save();
        }
        // 다음 곡 아트를 미리 받아 둔다 (재생 바 · 재생 화면 · 앰비언트 빛 크기 모두) → 곡이 바뀌면 바로 보이게
        if (Player.Index + 1 < Player.Queue.Count)
        {
            var nextThumb = Player.Queue[Player.Index + 1].Thumb;
            foreach (var size in new[] { 104, 670, 48 }) ImageCache.Prefetch(Theme.ImageUrl(nextThumb, size).Url);
        }
        _backdrop.Show(t.Thumb);
        var c = await AlbumColor.FromUrl(t.Thumb);
        if (Player.Current?.VideoId != t.VideoId) return;
        _accent = c;
        Bar.SetAccent(c);
        LyricsView.Glow = c;
        SetWash(c, true);
    }

    /// <summary>
    /// 앨범 색 번짐 (pear 테마의 --oneui-album-wash 와 같은 두 개의 타원):
    /// 오른쪽 위 1100×720 (58%) · 왼쪽 아래 900×640 (36%). 새 색 층이 서서히 나타나고 옛 층은 사라진다.
    /// </summary>
    /// <summary>지금 번짐 층의 붓들 — 창 크기만 바뀌면 새로 만들지 않고 가운데만 옮긴다</summary>
    private readonly List<(RadialGradientBrush Brush, double Fx, double Fy)> _washBrushes = new();

    private void SetWash(Color c, bool animate)
    {
        var w = Math.Max(1, Root.ActualWidth);
        var h = Math.Max(1, Root.ActualHeight);
        if (!animate && _washBrushes.Count > 0 && c == _washColor)
        {
            foreach (var (brush, fx, fy) in _washBrushes)
                brush.Center = brush.GradientOrigin = new Point(w * fx, h * fy);
            return;
        }
        _washColor = c;
        _washBrushes.Clear();
        RadialGradientBrush Wash(Color color, double fx, double fy, double rx, double ry)
        {
            var b = Radial(color, new Point(w * fx, h * fy), rx, ry);
            _washBrushes.Add((b, fx, fy));
            return b;
        }
        var layer = new Grid { IsHitTestVisible = false };
        // 이제 창 전체 흐린 앨범아트(_backdrop)가 주 배경 — 색 번짐은 살짝만 얹어 앨범 대표색을 받쳐 준다
        layer.Children.Add(new XRect { Fill = Wash(c.WithAlpha(0.28), 0.88, -0.12, 1100, 720) });
        layer.Children.Add(new XRect { Fill = Wash(c.WithAlpha(0.18), -0.08, 1.08, 900, 640) });
        // 재생 화면 진한 앨범 색
        _playerWash.Children.Clear();
        _playerWash.Children.Add(new XRect { Fill = Wash(c.WithAlpha(0.5), 0.3, 0.45, 900, 700) });
        var old = _wash.Children.Where(x => x != _backdrop && x != _backdropShade).ToList();
        _wash.Children.Add(layer);
        if (!animate)
        {
            foreach (var o in old) _wash.Children.Remove(o);
            return;
        }
        Anim.V(layer).Opacity = 0;
        Anim.Opacity(layer, 1, 900);
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(950);
            foreach (var o in old) _wash.Children.Remove(o);
        });
    }

    private static RadialGradientBrush Radial(Color c, Point center, double rx, double ry)
    {
        var b = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = rx,
            RadiusY = ry,
            MappingMode = BrushMappingMode.Absolute,
        };
        b.GradientStops.Add(new GradientStop { Color = c, Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = c.WithAlpha(0), Offset = 0.7 });
        return b;
    }
}
