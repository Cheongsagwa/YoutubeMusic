using Microsoft.UI.Xaml.Media.Animation;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 화면 아래 떠 있는 유리 재생 바 (pear-desktop One UI 테마의 플로팅 바).
/// 왼쪽: 이전 · 재생 · 다음 · 시간 / 가운데: 앨범아트 · 곡명 · 가수 · 가사 한 줄 / 오른쪽: 볼륨 · 반복 · 셔플 · 재생 화면 열기
/// 위 가장자리를 따라 파도 재생 바.
/// </summary>
public sealed class FloatingBar : Grid
{
    private readonly PlayerService _p = PlayerService.Instance;
    private readonly WaveSeekBar _wave = new();
    private readonly TextBlock _time = Theme.Text("0:00 / 0:00", 15, FontWeights.SemiBold, Theme.Secondary);
    private readonly ArtView _art = new(12, 104) { Width = 52, Height = 52 };
    internal ArtView Art => _art;
    private readonly Marquee _title = new(18, FontWeights.Bold, Theme.OnSurface);
    private readonly Marquee _artist = new(15, FontWeights.Normal, Theme.Secondary);
    private readonly Border _lyric = new() { Height = 36, Width = 0, CornerRadius = new CornerRadius(18), Background = Theme.B(Theme.Chip), Margin = new Thickness(0) };
    private readonly Marquee _lyricText = new(14, FontWeights.Bold, Theme.OnSurface);
    private readonly Grid _playIcon = new();
    private readonly PlayPauseIcon _pp = new(28, Colors.Black);
    /// <summary>곡을 불러오는 동안 재생 버튼 흰 원 안의 검은 로딩 (pear 37)</summary>
    private readonly ProgressRing _spin = new() { Width = 26, Height = 26, IsActive = false, Foreground = Theme.B(Colors.Black), IsHitTestVisible = false };
    private bool _spinning;
    // 오른쪽 아이콘(볼륨 · 반복 · 셔플 · 미니 플레이어)은 새로 그린 한 벌 — 24px(배율 1), 굵기 2 · 곧은 변은 정수 좌표라 번지지 않고,
    // 모두 가로 16~18 · 세로 14 · 가운데 (12,12) 로 크기가 같다 (BarIcons.cs)
    private readonly VolumeIcon _vol = new(24, Colors.White);
    private readonly RepeatIcon _rep = new(24, Colors.White);
    private readonly ShuffleIcon _shuf = new(24, Colors.White);
    private FrameworkElement _openGlyph = null!;
    private RepeatMode? _lastRepeat;
    private readonly IconButton _volume = new(36), _repeat = new(36), _shuffle = new(36);
    private readonly Grid _volumeIcon = new();
    private readonly Grid _repeatIcon = new();
    private readonly MiniSlider _volSlider = new() { Width = 0, Opacity = 0 };
    private bool _lyricOpen;
    private StackPanel _info = null!;
    private ColumnDefinition _midCol = null!;
    private double _lyricWidth = -1;

    /// <summary>가운데 칸에 들어갈 만큼만 — 가사 칸 폭(0~300), 곡 정보 폭(120~240), 아트는 아주 좁을 때만 숨김</summary>
    private void Fit(bool animate)
    {
        var avail = _midCol.ActualWidth;
        if (avail <= 0) return;
        const double art = 52, gap = 14, more = 48, lyricGap = 12;
        var infoW = Math.Clamp(avail - art - gap - more, 110, 240);
        _info.Width = infoW;
        _art.Visibility = avail < 110 + gap + more + art ? Visibility.Collapsed : Visibility.Visible;
        var room = avail - art - gap - infoW - more - lyricGap;
        var lyric = _lyricOpen && room >= 140 ? Math.Min(300, room) : 0;
        if (Math.Abs(lyric - _lyricWidth) < 0.5) return;
        _lyricWidth = lyric;
        _lyric.Margin = new Thickness(lyric > 0 ? lyricGap : 0, 0, 0, 0);
        if (animate) AnimateWidth(_lyric, lyric, lyric > 0 ? 1 : 0);
        else
        {
            _lyric.Width = lyric;
            _lyric.Opacity = lyric > 0 ? 1 : 0;
        }
        Anim.Opacity(_lyric, lyric > 0 ? 1 : 0, 300);
    }
    private bool _volHover;
    private DispatcherQueueTimer? _volCloseTimer;
    private string? _shownLine;
    private bool _wasPlaying;
    private long? _busySince;
    public event Action? OpenPlayer;
    private readonly DispatcherQueueTimer _tick; // 필드로 붙잡아 둬야 GC 에 수거되지 않는다

    public FloatingBar()
    {
        Height = Theme.BarHeight;
        CornerRadius = new CornerRadius(28);
        Background = Theme.Glass(0.62);
        BorderBrush = Theme.B(Color.FromArgb(20, 255, 255, 255));
        BorderThickness = new Thickness(1);
        // 그림자는 MainWindow 가 따로 까는 받침(Anim.ShadowFor)이 드리운다 — 바에 직접 ThemeShadow + Translation Z 를 주면
        // 바가 히트 테스트에서 빠져 클릭이 뒤 화면으로 통과했다

        // 파도 재생 바 — 위 가장자리 (좌우 28px 안쪽)
        _wave.VerticalAlignment = VerticalAlignment.Top;
        _wave.Margin = new Thickness(28 - 17, -9, 28 - 17, 0);
        _wave.Progress = () => _p.Duration > 0 ? _p.Position / _p.Duration : 0;
        _wave.IsPlaying = () => _p.IsPlaying;
        _wave.Duration = () => _p.Duration;
        _wave.Seek += f => _p.Seek(f * _p.Duration);
        Children.Add(_wave);

        // 파도 선과 바 바닥 사이 한가운데 (위 · 아래 여백 같게 — 전엔 아래로 붙어 위 17 · 아래 8 이었다)
        var row = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Height = 64, Margin = new Thickness(14, 0, 22, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(row);

        // ── 왼쪽 ──
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        // 아이콘은 pear bar-icons.ts 처럼 모양이 이어지며 바뀐다 (BarIcons.cs)
        var prevIcon = new SkipIcon(false, 24, Colors.White);
        var prev = new IconButton(44) { Content = prevIcon };
        prev.Click += () => { prevIcon.Swap(); _ = _p.Previous(); };
        var play = new IconButton(52);
        play.HoverBrush = Theme.B(Colors.Transparent);
        var circle = new XEllipse { Fill = Theme.B(Colors.White), Width = 52, Height = 52 };
        _playIcon.Children.Add(circle);
        _playIcon.Children.Add(_pp);
        Anim.V(_spin).Opacity = 0;
        _playIcon.Children.Add(_spin);
        play.Content = _playIcon;
        play.Margin = new Thickness(4, 0, 4, 0);
        play.Click += () => _p.TogglePlay();
        var nextIcon = new SkipIcon(true, 24, Colors.White);
        var next = new IconButton(44) { Content = nextIcon };
        next.Click += () => { nextIcon.Swap(); _ = _p.Next(); };
        _time.Width = 108;
        _time.Margin = new Thickness(12, 0, 0, 0);
        _time.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(prev);
        left.Children.Add(play);
        left.Children.Add(next);
        left.Children.Add(_time);
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        // ── 가운데 ──
        var mid = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Anim.Pressable(_art);
        var info = _info = new StackPanel { Width = 240, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        info.Children.Add(_title);
        info.Children.Add(_artist);
        // 가수 이름을 누르면 아티스트 화면
        _artist.Tapped += (_, e) =>
        {
            if (_p.Current?.ArtistId is not { } id) return;
            e.Handled = true;
            App.MainWin?.OpenBrowse(id);
        };
        info.Background = new SolidColorBrush(Colors.Transparent);
        _lyricText.Margin = new Thickness(16, 0, 16, 0);
        _lyricText.VerticalAlignment = VerticalAlignment.Center;
        _lyric.Child = _lyricText;
        _lyric.VerticalAlignment = VerticalAlignment.Center;
        Anim.V(_lyric).Opacity = 0;
        mid.Children.Add(_art);
        mid.Children.Add(info);
        // 곡 이름 옆 ⋮ (pear 재생 바) — 곡 메뉴
        var more = new IconButton(40) { Content = Theme.Icon(Icons.More, 22, Colors.White), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        more.Click += () => { if (_p.Current is { } cur) TrackMenu.Show(more, cur, _p.Index, null, fromBar: true); };
        mid.Children.Add(more);
        mid.Children.Add(_lyric);
        Grid.SetColumn(mid, 1);
        row.Children.Add(mid);
        _midCol = row.ColumnDefinitions[1];
        // 창이 좁아지면 가사 칸 → 곡 정보 칸 → 앨범아트 순서로 줄여서 가운데 묶음이 잘리지 않게
        row.SizeChanged += (_, _) => Fit(false);

        // ── 오른쪽 ──
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
        _volSlider.VerticalAlignment = VerticalAlignment.Center;
        _volSlider.Value = _p.Volume;
        _volSlider.ValueChanged += v => _p.Volume = v;
        _volumeIcon.Children.Add(_vol);
        _volume.Content = _volumeIcon;
        _volume.Click += () => _p.Muted = !_p.Muted;
        // 볼륨: 스피커 아이콘이나 슬라이더 위에 있을 때만 펼친다 (사이 틈에서 닫히지 않게 조금 기다림)
        foreach (var el in new FrameworkElement[] { _volume, _volSlider })
        {
            el.PointerEntered += (_, _) => SetVolumeOpen(true);
            el.PointerExited += (_, _) => SetVolumeOpen(false);
        }
        // 볼륨 아이콘 · 슬라이더 위에서 휠을 굴리면 2% 씩 (pear precise-volume)
        foreach (var el in new FrameworkElement[] { _volume, _volSlider })
            el.PointerWheelChanged += (_, e) =>
            {
                var d = e.GetCurrentPoint(el).Properties.MouseWheelDelta;
                _p.Volume = Math.Clamp(_p.Volume + Math.Sign(d) * 0.02, 0, 1);
                Toast.Show($"볼륨 {Math.Round(_p.Volume * 100)}%");
                e.Handled = true;
            };
        _repeatIcon.Children.Add(_rep);
        _repeat.Content = _repeatIcon;
        _repeat.Click += () => _p.CycleRepeat();
        _shuffle.Content = _shuf;
        Anim.V(_shuf).Opacity = 0.55f;
        // 섞는 동안만 밝게 (pear: '한 번 섞기' 라서 켜짐 상태가 없다)
        _shuf.ActiveChanged += on => Anim.Opacity(_shuf, on ? 1 : 0.55f, on ? Anim.Fast : Anim.Medium);
        _shuffle.Click += () =>
        {
            _p.Shuffle();
            _shuf.Run();
        };
        Tip.Set(_shuffle, "셔플");
        Tip.Set(_repeat, "반복");
        Tip.Set(_volume, "음소거");
        Tip.Set(prev, "이전");
        Tip.Set(next, "다음");
        var open = new IconButton(36);
        open.HoverBrush = Theme.B(Colors.Transparent);
        var openGrid = new Grid();
        openGrid.Children.Add(new XEllipse { Fill = Theme.B(Colors.White), Width = 36, Height = 36 });
        _openGlyph = Theme.Icon(Icons.ChevronUp, 20, Colors.Black, stroke: true, strokeWidth: 2.2);
        openGrid.Children.Add(_openGlyph);
        open.Content = openGrid;
        Tip.Set(open, "재생 화면 열기");
        open.Click += () => OpenPlayer?.Invoke();
        // 미니 플레이어 (pear picture-in-picture)
        var pip = new IconButton(36) { Content = new PipIcon(24, Colors.White) };
        Tip.Set(pip, "미니 플레이어");
        // 셔플 · 반복처럼 쓰지 않을 때는 회색(흰색 55%) — 미니 플레이어를 켜면 이 바는 숨는다
        Anim.V((UIElement)pip.Content).Opacity = 0.55f;
        pip.Click += () => App.MainWin?.ToggleMini();
        right.Children.Add(_volSlider);
        right.Children.Add(_volume);
        right.Children.Add(_repeat);
        right.Children.Add(_shuffle);
        right.Children.Add(pip);
        right.Children.Add(open);
        Grid.SetColumn(right, 2);
        row.Children.Add(right);

        // 바 어디를 눌러도 재생 화면 (버튼 · 가수 이름 · 파도 바 · 볼륨 슬라이더는 각자 처리하고 Handled 로 막는다)
        Tapped += (_, e) => { if (!e.Handled) OpenPlayer?.Invoke(); };
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);

        _p.TrackChanged += OnTrack;
        _p.StateChanged += OnState;
        _p.VolumeChanged += OnVolume;
        _p.RepeatChanged += OnRepeat;
        LyricsState.Instance.Changed += () => _shownLine = null;
        OnVolume();
        OnRepeat();
        OnTrack();

        _tick = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(150);
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
    }

    public void SetAccent(Color c) => _wave.Color = c;
    public Task<string> TipTest() => Tip.Test(_shuffle, "셔플");

    /// <summary>재생 화면이 열리면 열기 버튼의 화살표가 부드럽게 180° 돈다 (pear 27)</summary>
    public void SetPlayerOpen(bool open)
    {
        var v = Anim.V(_openGlyph);
        v.CenterPoint = new Vector3((float)_openGlyph.ActualWidth / 2, (float)_openGlyph.ActualHeight / 2, 0);
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, open ? 180f : 0f, Anim.Smooth(c));
        a.Duration = TimeSpan.FromMilliseconds(Anim.Medium);
        v.StartAnimation("RotationAngleInDegrees", a);
    }

    private void OnTrack()
    {
        var t = _p.Current;
        if (_title.Text != (t?.Title ?? "재생 중인 곡 없음"))
        {
            // pear 40: 곡명 · 가수는 바뀔 때 440ms 로 페이드인
            Anim.V(_title).Opacity = 0;
            Anim.V(_artist).Opacity = 0;
            Anim.Opacity(_title, 1, Anim.Medium);
            Anim.Opacity(_artist, 1, Anim.Medium, 40);
        }
        _title.Text = t?.Title ?? "재생 중인 곡 없음";
        _artist.Text = t?.Artist ?? "";
        // 이전 아트를 받침에 깔아 두고, 새 아트를 다 불러오면 가운데부터 번지듯 드러낸다 (ArtView)
        _art.Show(t?.Thumb);
        _shownLine = null;
    }

    private void OnState()
    {
        var playing = _p.IsPlaying || (_p.IsBuffering && _wasPlaying);
        if (playing == _wasPlaying) return;
        _wasPlaying = playing;
        // 재생 ↔ 일시정지: 막대 둘이 합쳐지며 삼각형이 되고, 삼각형이 갈라지며 막대가 된다
        _pp.Set(playing);
    }

    private void OnVolume()
    {
        var muted = _p.Muted;
        _vol.Set(muted);
        // 꺼짐 · 음소거는 회색(흐리게), 켜짐은 흰색
        Anim.Opacity(_volumeIcon, muted ? 0.55f : 1f, 240);
        if (!_volSlider.Dragging) _volSlider.Value = _p.Volume;
    }

    private void OnRepeat()
    {
        var mode = _p.Repeat;
        Anim.Opacity(_repeatIcon, mode == RepeatMode.Off ? 0.55f : 1f, 240);
        // 모드가 바뀌면 화살표가 둘레를 한 바퀴 돌고, 한 곡 반복이면 가운데 1
        if (_lastRepeat != null && _lastRepeat != mode) _rep.Spin();
        _rep.SetOne(mode == RepeatMode.One, _lastRepeat != null);
        _lastRepeat = mode;
    }

    private void SetVolumeOpen(bool open)
    {
        _volHover = open;
        if (_volCloseTimer == null)
        {
            _volCloseTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _volCloseTimer.Interval = TimeSpan.FromMilliseconds(260);
            _volCloseTimer.IsRepeating = false;
            _volCloseTimer.Tick += (_, _) => { if (!_volHover && !_volSlider.Dragging) AnimateWidth(_volSlider, 0, 0); };
        }
        _volCloseTimer.Stop();
        if (open) AnimateWidth(_volSlider, 112, 1);
        else _volCloseTimer.Start();
    }

    /// <summary>폭을 부드럽게 바꾼다 (레이아웃이 함께 움직여서 옆 요소들도 자연스럽게 비켜 감)</summary>
    private static void AnimateWidth(FrameworkElement el, double to, double opacity)
    {
        var sb = new Storyboard();
        var a = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(a, el);
        Storyboard.SetTargetProperty(a, "Width");
        sb.Children.Add(a);
        var o = new DoubleAnimation { To = opacity, Duration = TimeSpan.FromMilliseconds(260) };
        Storyboard.SetTarget(o, el);
        Storyboard.SetTargetProperty(o, "Opacity");
        sb.Children.Add(o);
        sb.Begin();
    }

    private void Tick()
    {
        var time = $"{Fmt.Time(_p.Position)} / {Fmt.Time(_p.Duration)}";
        if (time != _time.Text) _time.Text = time;
        // 불러오는 중이면 재생 아이콘 대신 로딩 (0.3초 넘게 걸릴 때만 — 바로 열리는 곡은 깜빡이지 않게)
        var busy = _p.Current != null && _p.IsBuffering;
        if (busy != _spinning)
        {
            if (busy) _busySince ??= Environment.TickCount64;
            if (!busy || Environment.TickCount64 - _busySince > 300)
            {
                _spinning = busy;
                _spin.IsActive = busy;
                Anim.Opacity(_spin, busy ? 1 : 0, Anim.Fast);
                Anim.Opacity(_pp, busy ? 0 : 1, Anim.Fast);
            }
        }
        if (!busy) _busySince = null;
        if (_p.IsPlaying != _wasPlaying && !_p.IsBuffering) OnState();

        // 가사 한 줄
        var lines = LyricsState.Instance.Lines;
        // 시간 정보가 있는 가사일 때만 (pear: 없으면 아무것도 띄우지 않는다)
        var has = lines != null && lines.Count > 0 && LyricsState.Instance.Synced;
        if (has != _lyricOpen)
        {
            _lyricOpen = has;
            _lyricWidth = -1;
            Fit(true);
        }
        if (has)
        {
            var idx = LyricsState.Instance.IndexAt(_p.Position + 0.15);
            var line = idx >= 0 ? lines![idx].Text : "";
            if (string.IsNullOrWhiteSpace(line)) line = "♪";
            if (line != _shownLine)
            {
                _shownLine = line;
                _lyricText.Text = line;
                Anim.Appear(_lyricText, 0, 8);
            }
        }
    }
}
