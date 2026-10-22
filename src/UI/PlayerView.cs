using Microsoft.UI.Input;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>One UI 알약 탭 — 둥근 트랙 안에서 고른 탭만 알약으로 채워지고, 알약이 스프링으로 미끄러진다</summary>
public sealed class PillTabs : Grid
{
    private readonly Border _pill = new() { CornerRadius = new CornerRadius(18), Background = Theme.B(Theme.Selected), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _items = new();
    private readonly List<TextBlock> _labels = new();
    public int Selected { get; private set; }
    public event Action<int>? Changed;

    public PillTabs(params string[] names)
    {
        Height = 44;
        Padding = new Thickness(4);
        CornerRadius = new CornerRadius(22);
        Background = Theme.B(Theme.Chip);
        Children.Add(_pill);
        Children.Add(_items);
        for (var i = 0; i < names.Length; i++)
        {
            _items.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Theme.Text(names[i], 15, FontWeights.SemiBold, Theme.Secondary);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var hit = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            hit.Children.Add(label);
            var idx = i;
            hit.Tapped += (_, _) => Select(idx);
            hit.PointerPressed += (_, _) => Anim.Scale(label, 0.94f, 0.9f, 30);
            hit.PointerReleased += (_, _) => Anim.Scale(label, 1f);
            hit.PointerExited += (_, _) => Anim.Scale(label, 1f);
            Grid.SetColumn(hit, i);
            _items.Children.Add(hit);
            _labels.Add(label);
        }
        SizeChanged += (_, _) => Place(false);
        Loaded += (_, _) => Place(false);
        Select(0, false);
    }

    public void Select(int i, bool notify = true)
    {
        Selected = i;
        for (var k = 0; k < _labels.Count; k++)
        {
            _labels[k].Foreground = Theme.B(k == i ? Theme.OnSurface : Theme.Secondary);
            _labels[k].FontWeight = k == i ? FontWeights.Bold : FontWeights.SemiBold;
        }
        Place(true);
        if (notify) Changed?.Invoke(i);
    }

    private void Place(bool animate)
    {
        if (_labels.Count == 0 || ActualWidth <= 0) return;
        var w = (ActualWidth - 8) / _labels.Count;
        _pill.Width = w;
        var to = new Vector3((float)(w * Selected), 0, 0);
        if (animate) Anim.Translate(_pill, to, 0.75f, 55);
        else Anim.SetTranslation(_pill, to);
    }
}

/// <summary>
/// 재생 화면 (pear-desktop One UI 테마의 재생 화면과 같은 배치) — 아래에서 스프링으로 올라온다.
/// 왼쪽: 노래/동영상 전환 + 큰 앨범아트 / 오른쪽: 450px 유리 패널 [다음 트랙 | 가사 | 댓글 | 관련 항목]
/// </summary>
public sealed class PlayerView : Grid
{
    private readonly PlayerService _p = PlayerService.Instance;
    private readonly Border _art = new() { CornerRadius = new CornerRadius(Theme.RadiusCard) };
    private readonly ArtView _artView = new((float)Theme.RadiusCard, 670);
    private readonly MediaPlayerElement _videoView = new() { AreTransportControlsEnabled = false, Stretch = Stretch.UniformToFill };
    private readonly Grid _artBox = new() { Width = 670, Height = 648, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _artHolder = new();
    private readonly Grid _artStack = new();
    /// <summary>아트 위 전체 화면 버튼 (뮤직비디오일 때만)</summary>
    private IconButton _fsButton = null!;
    /// <summary>전체 화면일 때 창 전체를 덮는 검은 판 (영상을 여기로 옮겨 붙인다)</summary>
    private Grid? _fsHost;
    private bool _portrait, _arranged;
    /// <summary>아트 칸 비율의 세로 (가로 670 기준) — 노래 648, 영상 · 16:9 썸네일 377</summary>
    private double _artH = 648;
    /// <summary>아트 뒤 번지는 빛 (pear ambient-mode)</summary>
    private readonly AmbientGlow _glow = new();
    private readonly PillTabs _tabs = new("다음 트랙", "가사", "댓글", "관련 항목");
    private readonly StackPanel _queue = new();
    private readonly ScrollViewer _queueScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Grid _queueTab = new();
    private readonly TextBlock _source = Theme.Text("", 16, FontWeights.Bold, Theme.OnSurface);
    private readonly OneToggle _autoplay;
    private Grid _autoRow = null!;
    private Grid _queueHead = null!;
    private readonly LyricsView _lyrics = new();
    private readonly Grid _lyricsTab = new();
    private readonly LyricsSourceBar _sourceBar = new();
    private readonly CommentsView _comments = new();
    private readonly ScrollViewer _related = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly StackPanel _relatedList = new() { Spacing = 8 };
    private string? _relatedFor;
    private readonly List<TrackRow> _rows = new();
    /// <summary>곡마다 한 번 만든 줄을 다시 쓴다 (칩을 바꾸거나 곡을 넣을 때마다 수백 줄을 새로 만들지 않게)</summary>
    private readonly Dictionary<Track, TrackRow> _rowCache = new(ReferenceEqualityComparer.Instance);
    private bool _queueDirty;
    private readonly Grid _panel;
    private readonly Grid _left = new();
    public bool IsOpen { get; private set; }

    public PlayerView()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        Padding = new Thickness(56, 54, 56, Theme.BarHeight + Theme.FloatGap * 2 + 28);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });
        ColumnSpacing = 56;
        // 세로 모니터에서는 위에 아트, 아래에 패널 (Arrange)
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        SizeChanged += (_, _) => Arrange();

        // ── 왼쪽: 노래/동영상 + 앨범아트 ──
        var left = _left;
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var av = new AvToggle { Margin = new Thickness(0, 0, 0, 24) };
        left.Children.Add(av);
        // 뮤직비디오는 아트 자리에서 화면으로 (재생기 하나를 그대로 붙인다)
        var artStack = _artStack;
        artStack.Children.Add(_artView);
        _videoView.SetMediaPlayer(_p.Media);
        _videoView.Visibility = Visibility.Collapsed;
        _videoView.DoubleTapped += (_, _) => ToggleFullScreen();
        artStack.Children.Add(_videoView);
        artStack.Children.Add(HoverChrome(_art, false, out _fsButton));
        _fsButton.Visibility = Visibility.Collapsed;
        _art.Child = artStack;
        _p.VideoChanged += OnVideo;
        _artBox.Children.Add(_glow);
        _artBox.Children.Add(_art);
        // 창 크기에 맞춰 아트 칸을 키우고 줄인다 (비율 유지). Viewbox 로 늘리면 읽어 둔 그림이 그대로 확대돼 흐려져서 칸 크기를 직접 바꾼다
        var artHolder = _artHolder;
        artHolder.Children.Add(_artBox);
        artHolder.SizeChanged += (_, _) => FitArt();
        _art.Shadow = new ThemeShadow();
        _art.Translation = new Vector3(0, 0, 40);
        Grid.SetRow(artHolder, 1);
        left.Children.Add(artHolder);
        Children.Add(left);

        // ── 오른쪽: 유리 패널 ──
        _panel = new Grid
        {
            CornerRadius = new CornerRadius(32),
            Background = Theme.Glass(0.32, Color.FromArgb(255, 30, 30, 34)),
            Padding = new Thickness(12),
        };
        _panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _panel.Children.Add(_tabs);
        var content = new Grid { Margin = new Thickness(0, 8, 0, 0) };

        // 다음 트랙: 출처 머리 + 자동재생 + 목록
        _queueTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _queueTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _queueTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // 자동재생 줄이 없으면 목록과의 간격을 [저장] 위쪽 간격(탭과 20)과 같게
        var head = _queueHead = new Grid { Margin = new Thickness(8, 12, 8, 8) };
        var headText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 96, 0) };
        // 출처 이름만 — '재생 중인 트랙 출처' 같은 설명 글씨 없이 자리로 알 수 있다
        headText.Children.Add(_source);
        head.Children.Add(headText);
        var save = new Border { Height = 32, CornerRadius = new CornerRadius(16), Background = Theme.B(Theme.Chip), Padding = new Thickness(12, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        saveRow.Children.Add(Theme.Icon(20, Colors.White, Icons.PlaylistAdd));
        var saveText = Theme.Text("저장", 14, FontWeights.SemiBold, Theme.OnSurface);
        saveText.VerticalAlignment = VerticalAlignment.Center;
        saveRow.Children.Add(saveText);
        save.Child = saveRow;
        Tip.Set(save, "재생목록에 저장");
        save.Tapped += (_, _) => SaveQueue(save);
        Anim.Pressable(save);
        head.Children.Add(save);
        _queueTab.Children.Add(head);
        var autoRow = _autoRow = new Grid { Height = 52, Margin = new Thickness(8, 0, 8, 4), Padding = new Thickness(8, 0, 2, 0), Background = new SolidColorBrush(Colors.Transparent) };
        var autoText = Theme.Text("자동재생", 14, FontWeights.Bold, Theme.OnSurface);
        autoText.VerticalAlignment = VerticalAlignment.Center;
        autoRow.Children.Add(autoText);
        _autoplay = new OneToggle(_p.Autoplay) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _autoplay.Toggled += on => _p.Autoplay = on;
        autoRow.Children.Add(_autoplay);
        // 줄 아무 데나 눌러도 켜고 끈다
        autoRow.Tapped += (_, _) =>
        {
            var on = !_p.Autoplay;
            _autoplay.Set(on, true);
            _p.Autoplay = on;
        };
        Grid.SetRow(autoRow, 1);
        _queueTab.Children.Add(autoRow);
        _queueScroll.Content = _queue;
        // 패널 안 목록도 더 있는 쪽 끝을 흐린다 (pear scroll-fade: 위 44 · 아래 56)
        EdgeMask.Attach(_queueScroll);
        Grid.SetRow(_queueScroll, 2);
        _queueTab.Children.Add(_queueScroll);
        content.Children.Add(_queueTab);

        // 가사 탭: 출처 고르기 카드 + 가사
        _lyricsTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _lyricsTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // 출처 카드는 3초만 보이고 접힌다 — 가사 위쪽(90px)에 마우스를 올리면 다시
        _lyricsTab.Children.Add(_sourceBar.Host);
        _lyricsTab.PointerMoved += (_, e) => { if (e.GetCurrentPoint(_lyricsTab).Position.Y < 90) _sourceBar.Peek(); };
        Grid.SetRow(_lyrics, 1);
        _lyricsTab.Children.Add(_lyrics);
        content.Children.Add(_lyricsTab);
        content.Children.Add(_comments);
        _related.Content = _relatedList;
        EdgeMask.Attach(_related);
        content.Children.Add(_related);
        Grid.SetRow(content, 1);
        _panel.Children.Add(content);
        Grid.SetColumn(_panel, 1);
        Children.Add(_panel);
        ShowTab(0);
        _tabs.Changed += ShowTab;

        _p.TrackChanged += OnTrack;
        _p.QueueChanged += BuildQueue;
        OnTrack();
        BuildQueue();
        Visibility = Visibility.Collapsed;
    }

    private void ShowTab(int i)
    {
        var views = new FrameworkElement[] { _queueTab, _lyricsTab, _comments, _related };
        for (var k = 0; k < views.Length; k++) views[k].Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;
        _lyrics.Active = i == 1 && IsOpen;
        if (i == 1) _sourceBar.Peek();
        Anim.Appear(views[i], 0, 10);
        if (i == 2) _comments.Load(_p.Current?.VideoId);
        if (i == 3) LoadRelated();
    }

    public void ShowLyricsTab() => _tabs.Select(1);
    public void SelectTab(int i) => _tabs.Select(i);

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visibility = Visibility.Visible;
        // pear 64: 화면은 640ms 우아한 감속으로 올라오고, 왼쪽 · 오른쪽 패널은 720ms 동안 떠오른다(오른쪽 60ms 늦게)
        var h = (float)Math.Max(300, ActualHeight * 0.35);
        Anim.V(this).Opacity = 0;
        Anim.SetTranslation(this, new Vector3(0, h, 0));
        Anim.Slide(this, Vector3.Zero, 640);
        Anim.Opacity(this, 1, 300);
        Anim.Rise(_left, 0);
        Anim.Rise(_panel, 60);
        _lyrics.Active = _tabs.Selected == 1;
        if (_queueDirty) BuildQueue();
        ScrollToCurrent();
        // 영상 화면(스왑체인)은 부모의 이동 · 투명도를 안 따라서 화면이 다 올라온 뒤에 보인다
        if (_p.PlayingVideo && _fsHost == null)
        {
            _videoView.Visibility = Visibility.Visible;
            Anim.Appear(_videoView, 380, 0);
        }
    }

    public async void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _lyrics.Active = false;
        // 영상 화면은 내려가는 애니메이션을 안 따라가고 잔상으로 남는다 → 바로 숨김 (Open 에서 다시)
        if (_fsHost == null) _videoView.Visibility = Visibility.Collapsed;
        Anim.Slide(this, new Vector3(0, (float)Math.Max(300, ActualHeight * 0.35), 0), 640);
        Anim.Opacity(this, 0, 240);
        await Task.Delay(320);
        if (!IsOpen) Visibility = Visibility.Collapsed;
    }

    /// <summary>노래 ↔ 동영상: 영상이면 16:9 화면, 노래면 아트</summary>
    private void OnVideo()
    {
        var video = _p.PlayingVideo;
        _videoView.Visibility = video && (IsOpen || _fsHost != null) ? Visibility.Visible : Visibility.Collapsed;
        _fsButton.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        if (video) Anim.Appear(_videoView, 0, 0);
        else if (_fsHost != null) ToggleFullScreen();
        var wide = video || _p.Current?.Thumb?.Contains("ytimg.com") == true;
        MorphArt(wide ? 377 : 648);
    }

    /// <summary>창이 세로로 길면(세로 모니터) 2행: 위 아트 · 아래 패널. 가로면 2열: 왼쪽 아트 · 오른쪽 450px 패널</summary>
    private void Arrange()
    {
        if (ActualWidth <= 0) return;
        var portrait = ActualHeight > ActualWidth;
        if (portrait == _portrait && _arranged) return;
        _portrait = portrait;
        _arranged = true;
        ColumnDefinitions[1].Width = new GridLength(portrait ? 0 : 450);
        ColumnSpacing = portrait ? 0 : 56;
        RowDefinitions[0].Height = new GridLength(portrait ? 0.85 : 1, GridUnitType.Star);
        RowDefinitions[1].Height = portrait ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RowSpacing = portrait ? 32 : 0;
        Grid.SetColumn(_panel, portrait ? 0 : 1);
        Grid.SetRow(_panel, portrait ? 1 : 0);
    }

    /// <summary>
    /// 아트 · 영상 위에 마우스를 올리면 위쪽이 어둡게 번지고(YouTube 플레이어처럼) 오른쪽 위에 전체 화면 버튼.
    /// exit = 전체 화면 안에서 쓰는 것 (나가기 버튼, 모서리 없음)
    /// </summary>
    private Grid HoverChrome(FrameworkElement host, bool exit, out IconButton button)
    {
        var chrome = new Grid();
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(150, 0, 0, 0), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(60, 0, 0, 0), Offset = 0.5 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 1 });
        chrome.Children.Add(new Border
        {
            Height = exit ? 160 : 128,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = exit ? new CornerRadius(0) : new CornerRadius(Theme.RadiusCard, Theme.RadiusCard, 0, 0),
            Background = shade,
            IsHitTestVisible = false,
        });
        var btn = button = new IconButton(44)
        {
            Content = Theme.Icon(exit ? Icons.FullscreenExit : Icons.Fullscreen, 24, Colors.White, true),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = exit ? new Thickness(0, 24, 24, 0) : new Thickness(0, 12, 12, 0),
            IsHitTestVisible = false,
        };
        Tip.Set(btn, exit ? "전체 화면 종료 (Esc)" : "전체 화면");
        btn.Click += ToggleFullScreen;
        chrome.Children.Add(btn);
        Anim.V(chrome).Opacity = 0;
        host.PointerEntered += (_, _) => { Anim.Opacity(chrome, 1, 180); btn.IsHitTestVisible = true; };
        host.PointerExited += (_, _) => { Anim.Opacity(chrome, 0, 280); btn.IsHitTestVisible = false; };
        return chrome;
    }

    public bool IsFullScreen => _fsHost != null;

    /// <summary>뮤직비디오 전체 화면 ↔ 원래대로 — 영상(MediaPlayerElement)을 창 전체를 덮는 판으로 옮겨 붙이고 창도 전체 화면으로</summary>
    public void ToggleFullScreen()
    {
        if (App.MainWin is not { } w) return;
        if (_fsHost == null)
        {
            if (!_p.PlayingVideo) return;
            _artStack.Children.Remove(_videoView);
            var host = _fsHost = new Grid { Background = Theme.B(Colors.Black) };
            // 전체 화면은 잘리지 않게 다 보이도록, 카드 안에선 칸을 꽉 채워 검은 테두리가 없게
            _videoView.Stretch = Stretch.Uniform;
            host.Children.Add(_videoView);
            host.Children.Add(HoverChrome(host, true, out _));
            w.Root.Children.Add(host);
            w.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            Anim.Appear(host, 0, 0);
        }
        else
        {
            var host = _fsHost;
            _fsHost = null;
            host.Children.Remove(_videoView);
            _videoView.Stretch = Stretch.UniformToFill;
            w.Root.Children.Remove(host);
            _artStack.Children.Insert(1, _videoView);
            w.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        }
    }

    /// <summary>노래(정사각) ↔ 뮤직비디오(16:9) 로 바뀔 때 아트 칸이 뚝 바뀌지 않고 스프링으로 늘었다 줄었다</summary>
    private void MorphArt(double h)
    {
        if (Math.Abs(h - _artH) < 0.5) return;
        if (!IsOpen || !Anim.Enabled) { _artH = h; FitArt(); return; }
        var from = _artH;
        Tween.Run(_artBox, 520, Tween.Out, k => { _artH = from + (h - from) * k; FitArt(); });
    }

    private void FitArt()
    {
        var w = _artHolder.ActualWidth;
        var h = _artHolder.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var s = Math.Min(w / 670, h / _artH);
        _artBox.Width = Math.Floor(670 * s);
        _artBox.Height = Math.Floor(_artH * s);
    }

    private void OnTrack()
    {
        var t = _p.Current;
        // 뮤직비디오(동영상 썸네일)는 16:9, 노래는 정사각형에 가깝게 (pear 와 같은 670×648)
        // 뮤직비디오 · 동영상(16:9 썸네일)은 자르지 않고 16:9 그대로 (pear 와 같게), 노래는 670×648. 둘 다 세로 가운데
        var wide = _p.PlayingVideo || t?.Thumb?.Contains("ytimg.com") == true;
        MorphArt(wide ? 377 : 648);
        _artView.Show(t?.Thumb);
        _glow.Show(t?.Thumb);
        for (var i = 0; i < _rows.Count; i++) _rows[i].SetCurrent(i == _p.Index);
        _relatedFor = null;
        if (_tabs.Selected == 3) LoadRelated();
        if (_tabs.Selected == 2) _comments.Load(t?.VideoId);
        ScrollToCurrent();
    }

    /// <summary>
    /// 다음 트랙 목록 — 고른 곡들, 그 아래 '자동재생 사용 중' + 칩 줄 + 자동재생 곡들 (YT Music 과 같은 배치).
    /// 자동재생을 끄면 아래 구간은 숨긴다
    /// </summary>
    private void BuildQueue()
    {
        // 닫혀 있으면 열 때 한 번에 (재생 화면 밖에서 목록이 바뀔 때마다 그리지 않게)
        if (!IsOpen && _rows.Count > 0)
        {
            _queueDirty = true;
            return;
        }
        _queueDirty = false;
        _source.Text = _p.QueueTitle ?? (_p.Current != null ? $"{_p.Current.Title} 믹스" : "");
        _autoplay.Set(_p.Autoplay, false);
        foreach (var r in _rows) (r.Parent as Panel)?.Children.Remove(r);
        _queue.Children.Clear();
        _rows.Clear();
        var q = _p.Queue;
        var autoStart = Math.Min(_p.AutoStart, q.Count);
        // 칩을 바꿔 다시 받는 동안: 지금 곡 뒤만 흐리게
        var dimFrom = _p.AutoLoading ? (_p.ShowAutoplayToggle ? (q.Count > autoStart ? autoStart : int.MaxValue) : (q.Count > 1 ? _p.Index + 1 : int.MaxValue)) : int.MaxValue;
        void AddRows(int from, int to, Panel into)
        {
            for (var i = from; i < to; i++)
            {
                // 지금 곡은 보이는 모습 그대로 (이어 준 영상으로 볼 땐 영상 제목 · 채널 · 썸네일)
                var row = Row(i == _p.Index && _p.Current is { } cur ? cur : q[i]);
                row.Divider = i < to - 1 && i != _p.Index && i + 1 != _p.Index;
                row.QueueIndex = i;
                row.SetCurrent(i == _p.Index);
                var dim = i >= dimFrom ? 0.45f : 1f;
                if (Math.Abs(Anim.V(row).Opacity - dim) > 0.01f) Anim.Opacity(row, dim, 220);
                _rows.Add(row);
                into.Children.Add(row);
            }
        }
        // 목록에서 빠진 곡의 줄은 버린다
        if (_rowCache.Count > q.Count + 32)
        {
            var keep = new HashSet<Track>(q, ReferenceEqualityComparer.Instance);
            foreach (var k in _rowCache.Keys.Where(k => !keep.Contains(k)).ToList()) _rowCache.Remove(k);
        }
        // 검색에서 튼 곡이 아니면: 알고리즘 칩이 맨 위, 목록은 하나로 (자동재생 토글 없음)
        _autoRow.Visibility = _p.ShowAutoplayToggle ? Visibility.Visible : Visibility.Collapsed;
        _queueHead.Margin = new Thickness(8, 12, 8, _p.ShowAutoplayToggle ? 8 : 20);
        if (!_p.ShowAutoplayToggle)
        {
            if (_p.AutoChips.Count > 0) _queue.Children.Add(BuildAutoChips());
            var all = new StackPanel { Spacing = 2 };
            AddRows(0, q.Count, all);
            if (q.Count <= 1 && _p.AutoLoading)
            {
                var w = Theme.Text("비슷한 곡을 찾는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
                w.Margin = new Thickness(16, 8, 8, 8);
                all.Children.Add(w);
            }
            _queue.Children.Add(all);
            return;
        }
        var user = new StackPanel { Spacing = 2 };
        AddRows(0, autoStart, user);
        _queue.Children.Add(user);

        var autoOn = _p.Autoplay;
        var justOn = autoOn && _shownAutoplay == false;
        _shownAutoplay = autoOn;
        if (!autoOn) return;
        var section = new StackPanel { Spacing = 2 };
        // 자동재생을 막 켰으면 그 구간이 떠오르며 나타난다
        if (justOn) Anim.Rise(section, 0, 520);
        var label = Theme.Text("자동재생 사용 중", 14, FontWeights.Bold, Theme.Secondary);
        label.Margin = new Thickness(16, 24, 8, 12);
        section.Children.Add(label);
        if (_p.AutoChips.Count > 0) section.Children.Add(BuildAutoChips());
        var autoRows = new StackPanel { Spacing = 2 };
        AddRows(autoStart, q.Count, autoRows);
        if (q.Count == autoStart && _p.AutoLoading)
        {
            var wait = Theme.Text("비슷한 곡을 찾는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
            wait.Margin = new Thickness(16, 8, 8, 8);
            autoRows.Children.Add(wait);
        }
        section.Children.Add(autoRows);
        _queue.Children.Add(section);
    }

    private ScrollViewer? _chipScroll;
    private bool? _shownAutoplay;

    /// <summary>자동재생 칩 줄 — 36px 알약, 고르면 흰 칩 · 검은 글씨, 휠로 옆으로 넘김 (pear 의 OneUiChip)</summary>
    private FrameworkElement BuildAutoChips()
    {
        var offset = _chipScroll?.HorizontalOffset ?? 0;
        var sv = ShelvesView.HorizontalRow(out var row, pixelFade: false);
        _chipScroll = sv;
        row.Spacing = 8;
        row.Padding = new Thickness(8, _p.ShowAutoplayToggle ? 0 : 14, 8, 12);
        // 칩 줄 위에서는 Shift 없이도 휠로 옆으로
        sv.PointerWheelChanged += (_, e) =>
        {
            var props = e.GetCurrentPoint(sv).Properties;
            if (props.IsHorizontalMouseWheel || sv.ScrollableWidth <= 0) return;
            sv.ChangeView(sv.HorizontalOffset - props.MouseWheelDelta, null, null);
            e.Handled = true;
        };
        for (var i = 0; i < _p.AutoChips.Count; i++)
        {
            var c = _p.AutoChips[i];
            var idx = i;
            var chip = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 0, 14, 0),
                Background = Theme.B(c.Selected ? Colors.White : Theme.Chip),
                Child = Theme.Text(c.Text, 14, FontWeights.SemiBold, c.Selected ? Colors.Black : Theme.OnSurface),
            };
            ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
            Anim.FadeBg(chip);
            chip.PointerEntered += (_, _) => { if (!c.Selected) chip.Background = Theme.B(Theme.Selected); };
            chip.PointerExited += (_, _) => { if (!c.Selected) chip.Background = Theme.B(Theme.Chip); };
            Anim.Pressable(chip);
            chip.Tapped += (_, _) => { if (!c.Selected) _ = _p.SelectChip(idx); };
            row.Children.Add(chip);
        }
        sv.Loaded += (_, _) => sv.ChangeView(offset, null, null, true);
        return sv;
    }

    /// <summary>곡의 줄 (처음이면 만들고, 있으면 다시 쓴다)</summary>
    private TrackRow Row(Track t)
    {
        if (_rowCache.TryGetValue(t, out var row)) return row;
        row = new TrackRow(t);
        row.Click += r => { if (!_justDragged && r.QueueIndex is { } i) _ = _p.PlayAt(i); };
        EnableDrag(row);
        _rowCache[t] = row;
        return row;
    }

    // ── 다음 트랙 줄 끌기 ──
    // 위아래: 잡은 줄이 떠올라 손을 따라가고 다른 줄들이 비켜선다 (놓으면 그 자리로, 휴대폰 앱처럼).
    // 옆으로: 줄이 밀려나며 뒤에 휴지통 — 줄 너비의 1/3 넘게 밀고 놓으면 목록에서 뺀다 (지금 곡은 안 밀린다).
    // 예전엔 Windows 끌어 놓기(반투명 그림 + 흰 줄)를 써서 웹 같았다
    private TrackRow? _dragRow;
    private int _gesture; // 0 없음 · 1 방향 보는 중 · 2 위아래 · 3 옆으로
    private Point _gStart;
    private List<TrackRow> _dragSibs = new();
    private int _dragFrom, _dragTo;
    private double _dragStep, _dragY;
    private bool _justDragged;

    private void EnableDrag(TrackRow row)
    {
        row.PointerPressed += (_, e) =>
        {
            _justDragged = false;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || row.QueueIndex == null || _gesture >= 2) return;
            _dragRow = row;
            _gesture = 1;
            _gStart = e.GetCurrentPoint(_queue).Position;
        };
        row.PointerMoved += (_, e) =>
        {
            if (_dragRow != row || _gesture == 0) return;
            var pt = e.GetCurrentPoint(_queue);
            if (!pt.Properties.IsLeftButtonPressed && _gesture == 1) { _gesture = 0; return; }
            double dx = pt.Position.X - _gStart.X, dy = pt.Position.Y - _gStart.Y;
            if (_gesture == 1)
            {
                if (Math.Abs(dx) < 6 && Math.Abs(dy) < 6) return;
                if (Math.Abs(dy) >= Math.Abs(dx)) BeginReorder(row);
                else if (row.QueueIndex != _p.Index) _gesture = 3;
                else { _gesture = 0; return; }
                row.CapturePointer(e.Pointer);
            }
            if (_gesture == 2)
            {
                Reorder(dy);
                // 목록 끝 가까이 끌면 그쪽으로 굴린다
                var y = e.GetCurrentPoint(_queueScroll).Position.Y;
                if (y < 48) _queueScroll.ChangeView(null, _queueScroll.VerticalOffset - 14, null, true);
                else if (y > _queueScroll.ActualHeight - 48) _queueScroll.ChangeView(null, _queueScroll.VerticalOffset + 14, null, true);
            }
            else row.Swipe(dx, Math.Abs(dx) > row.ActualWidth / 3);
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            if (_dragRow != row) return;
            var x = e.GetCurrentPoint(_queue).Position.X - _gStart.X;
            EndGesture(x);
            row.ReleasePointerCapture(e.Pointer);
        };
        row.PointerCaptureLost += (_, _) => { if (_dragRow == row && _gesture >= 2) EndGesture(0); };
    }

    private void BeginReorder(TrackRow row)
    {
        _gesture = 2;
        var panel = (Panel)row.Parent;
        _dragSibs = panel.Children.OfType<TrackRow>().ToList();
        _dragFrom = _dragTo = _dragSibs.IndexOf(row);
        _dragStep = row.ActualHeight + ((panel as StackPanel)?.Spacing ?? 0);
        _dragY = 0;
        Canvas.SetZIndex(row, 10);
        row.Lift(true);
    }

    private void Reorder(double dy)
    {
        var row = _dragRow!;
        // 목록 위 · 아래 끝 밖으로는 반 칸까지만
        dy = Math.Clamp(dy, -(_dragFrom + 0.5) * _dragStep, (_dragSibs.Count - 1 - _dragFrom + 0.5) * _dragStep);
        _dragY = dy;
        Anim.SetTranslation(row, new Vector3(0, (float)dy, 0));
        var to = Math.Clamp(_dragFrom + (int)Math.Round(dy / _dragStep), 0, _dragSibs.Count - 1);
        if (to == _dragTo) return;
        _dragTo = to;
        for (var i = 0; i < _dragSibs.Count; i++)
        {
            if (i == _dragFrom) continue;
            var shift = i > _dragFrom && i <= to ? -_dragStep : i < _dragFrom && i >= to ? _dragStep : 0;
            Anim.TranslateTo(_dragSibs[i], new Vector3(0, (float)shift, 0), 240);
        }
    }

    private void EndGesture(double dx)
    {
        var row = _dragRow;
        var g = _gesture;
        _gesture = 0;
        _dragRow = null;
        if (row == null || g < 2) return;
        _justDragged = true;
        if (g == 2)
        {
            foreach (var s in _dragSibs) if (s != row) Anim.SetTranslation(s, Vector3.Zero);
            var from = row.QueueIndex;
            var to = _dragSibs[_dragTo].QueueIndex;
            // 목록을 다시 그리면 잡은 줄은 새 자리에 놓인다 → 지금 보이는 자리에서 그 자리로 미끄러져 내려앉게
            Anim.SetTranslation(row, new Vector3(0, (float)(_dragY - (_dragTo - _dragFrom) * _dragStep), 0));
            if (from is { } f && to is { } t && f != t) _p.MoveQueue(f, t);
            Anim.TranslateTo(row, Vector3.Zero, 260);
            row.Lift(false);
            Canvas.SetZIndex(row, 0);
            _dragSibs.Clear();
            return;
        }
        if (Math.Abs(dx) > row.ActualWidth / 3 && row.QueueIndex is { } qi && qi != _p.Index) RemoveBySwipe(row, qi, Math.Sign(dx));
        else row.SwipeBack();
    }

    private async void RemoveBySwipe(TrackRow row, int index, int dir)
    {
        row.SwipeOut(dir);
        await Task.Delay(Anim.D(200));
        // 아래 줄들은 빈자리로 올라오게 (목록을 다시 그린 뒤 원래 자리에서 출발)
        var step = row.ActualHeight + ((row.Parent as StackPanel)?.Spacing ?? 0);
        var below = _rows.SkipWhile(r => r != row).Skip(1).ToList();
        _p.RemoveFromQueue(index);
        row.SwipeReset();
        foreach (var r in below)
        {
            Anim.SetTranslation(r, new Vector3(0, (float)step, 0));
            Anim.TranslateTo(r, Vector3.Zero, 280);
        }
    }

    /// <summary>다음 트랙 '저장' — 지금 목록(자동재생 구간 빼고)을 새 재생목록으로 또는 내 재생목록에</summary>
    private async void SaveQueue(FrameworkElement at)
    {
        var ids = _p.Queue.Take(_p.ShowAutoplayToggle ? _p.AutoStart : _p.Queue.Count).Select(t => t.VideoId).Distinct().ToList();
        if (ids.Count == 0) return;
        if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
        List<(string Title, string PlaylistId, bool Contains)> lists;
        try { lists = await InnerTube.Instance.MyPlaylists(ids[0]); }
        catch (Exception e) { Log.Warn($"재생목록 목록 실패: {e.Message}"); lists = new(); }
        var m = new OneMenu();
        m.Header($"재생목록에 저장 · {ids.Count}곡");
        m.Item("새 재생목록", Icons.QueueAdd, async () =>
        {
            await NewPlaylistDialog.Create(ids, _p.QueueTitle);
        });
        if (lists.Count > 0) m.Separator();
        foreach (var (title, id, _) in lists.Take(30))
            m.Item(title, Icons.PlaylistAddPath, async () =>
            {
                var ok = await InnerTube.Instance.AddVideosToPlaylist(id, ids);
                Toast.Show(ok ? $"'{title}'에 {ids.Count}곡을 저장했어요" : "저장하지 못했어요");
            });
        m.Show(at);
    }

    /// <summary>관련 항목 — 지금 곡의 '관련' 페이지 셸프들을 패널 안에 세로로</summary>
    private async void LoadRelated()
    {
        var id = _p.RelatedBrowseId;
        if (id == null || id == _relatedFor) return;
        _relatedFor = id;
        _relatedList.Children.Clear();
        var loading = Theme.Text("불러오는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
        loading.Margin = new Thickness(8);
        _relatedList.Children.Add(loading);
        try
        {
            var shelves = await InnerTube.Instance.BrowseShelves(id);
            if (_relatedFor != id) return;
            _relatedList.Children.Clear();
            // pear 관련 항목: 셸프 제목 + ‹ ›, 곡은 4줄 × 2칸씩 넘기는 격자, 나머지는 카드(플레이리스트는 동그라미)
            foreach (var s in shelves.Take(8))
            {
                var head = new Grid { Margin = new Thickness(4, 16, 0, 8) };
                head.Children.Add(Theme.Text(s.Title, 22, FontWeights.ExtraBold, Theme.OnSurface));
                var sv = ShelvesView.HorizontalRow(out var row, pixelFade: false);
                row.Spacing = 16;
                row.Padding = new Thickness(4, 0, 4, 4);
                if (s.Tracks != null && s.Tracks.Count == s.Items.Count)
                {
                    for (var k = 0; k < s.Tracks.Count; k += 4)
                    {
                        var col = new StackPanel { Width = 196, Spacing = 16 };
                        foreach (var t in s.Tracks.Skip(k).Take(4)) col.Children.Add(ShelvesView.QuickRow(t, 196));
                        row.Children.Add(col);
                    }
                }
                else
                {
                    foreach (var c in s.Items)
                    {
                        var round = c.PageType is "MUSIC_PAGE_TYPE_PLAYLIST" or "MUSIC_PAGE_TYPE_ARTIST" || c.BrowseId?.StartsWith("VL") == true;
                        var card = new CardView(c, 160, square: true, round: round);
                        card.Click += cc => Play.Card(cc, s);
                        row.Children.Add(card);
                    }
                }
                var arrows = ShelvesView.ShelfArrows(sv);
                arrows.Margin = new Thickness(0, 0, 4, 0);
                arrows.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(arrows);
                _relatedList.Children.Add(head);
                _relatedList.Children.Add(sv);
            }
            _relatedList.Children.Add(new Border { Height = 16 });
        }
        catch (Exception e)
        {
            Log.Warn($"관련 항목 실패: {e.Message}");
            loading.Text = "불러오지 못했어요";
        }
    }

    private void ScrollToCurrent()
    {
        if (_p.Index < 0 || _p.Index >= _rows.Count) return;
        var row = _rows[_p.Index];
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _queue.UpdateLayout();
                var pt = row.TransformToVisual(_queue).TransformPoint(new Point(0, 0));
                // 칩 줄(약 62px) 바로 아래 첫 곡이면 pt.Y - 60 이 2px 쯤 → 맨 위인데 위쪽 흐림이 켜졌다. 그만큼 가까우면 맨 위로
                var y = pt.Y - 60;
                _queueScroll.ChangeView(null, y < 24 ? 0 : y, null, false);
            }
            catch { /* 아직 배치 전 */ }
        });
    }
}
