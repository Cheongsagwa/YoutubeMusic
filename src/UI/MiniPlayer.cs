using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 미니 플레이어 (pear picture-in-picture) — 창을 작게 '항상 위' 로 띄우고 앨범아트를 꽉 채운다.
/// 아래는 어둡게 번진 곡명 · 가수 + 얇은 재생 위치, 마우스를 올리면 가운데에 이전 · 재생 · 다음,
/// 왼쪽 위에 '원래 크기로'(두 번 눌러도 됨). 맨 위 줄을 잡고 창을 옮긴다
/// </summary>
public sealed class MiniPlayer : Grid
{
    private readonly PlayerService _p = PlayerService.Instance;
    private readonly ArtView _art = new(0, 480);
    private readonly Marquee _title = new(16, FontWeights.Bold, Colors.White);
    private readonly TextBlock _artist = Theme.Text("", 13, FontWeights.SemiBold, Color.FromArgb(200, 255, 255, 255));
    private readonly PlayPauseIcon _pp = new(26, Colors.Black);
    private readonly Grid _controls = new();
    private readonly XRect _progress = new() { Height = 3, Fill = Theme.B(Colors.White), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly DispatcherQueueTimer _tick;
    private bool _playing;
    /// <summary>창 끌기 영역 (맨 위 34px)</summary>
    public Grid DragArea { get; } = new() { Height = 34, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Colors.Transparent) };
    public event Action? ExitRequested;

    public MiniPlayer()
    {
        Background = Theme.B(Colors.Black);
        Visibility = Visibility.Collapsed;
        Children.Add(_art);

        // 아래 어둡게 (글자가 보이게) + 마우스를 올리면 전체를 살짝 어둡게
        var shade = new XRect { IsHitTestVisible = false };
        var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0.45 });
        grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(200, 0, 0, 0), Offset = 1 });
        shade.Fill = grad;
        Children.Add(shade);
        var dim = new XRect { Fill = Theme.B(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false };
        Anim.V(dim).Opacity = 0;
        Children.Add(dim);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 18), Spacing = 2 };
        info.Children.Add(_title);
        info.Children.Add(_artist);
        Children.Add(info);
        Children.Add(_progress);

        // 가운데 조작 (마우스를 올리면)
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var prevIcon = new SkipIcon(false, 26, Colors.White);
        var prev = new IconButton(48) { Content = prevIcon };
        prev.Click += () => { prevIcon.Swap(); _ = _p.Previous(); };
        var play = new IconButton(60) { HoverBrush = new SolidColorBrush(Colors.Transparent) };
        var pg = new Grid();
        pg.Children.Add(new XEllipse { Width = 60, Height = 60, Fill = Theme.B(Colors.White) });
        pg.Children.Add(_pp);
        play.Content = pg;
        play.Click += () => _p.TogglePlay();
        var nextIcon = new SkipIcon(true, 26, Colors.White);
        var next = new IconButton(48) { Content = nextIcon };
        next.Click += () => { nextIcon.Swap(); _ = _p.Next(); };
        row.Children.Add(prev);
        row.Children.Add(play);
        row.Children.Add(next);
        _controls.Children.Add(row);
        Anim.V(_controls).Opacity = 0;
        _controls.IsHitTestVisible = false;
        Children.Add(_controls);

        Children.Add(DragArea);
        // 왼쪽 위(창 끌기 줄 바로 밑): 원래 크기로 — 끌기 줄 위는 눌리지 않고, 오른쪽 위에는 창 닫기 단추가 있다
        var exit = new IconButton(36)
        {
            Content = Theme.Icon(Icons.Pip, 20, Colors.White, stroke: true, strokeWidth: 1.8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 40, 0, 0),
        };
        Tip.Set(exit, "원래 크기로");
        exit.Click += () => ExitRequested?.Invoke();
        Anim.V(exit).Opacity = 0;
        Children.Add(exit);

        PointerEntered += (_, _) =>
        {
            Anim.Opacity(dim, 1, Anim.Medium);
            Anim.Opacity(_controls, 1, Anim.Fast);
            Anim.Opacity(exit, 1, Anim.Fast);
            _controls.IsHitTestVisible = true;
        };
        PointerExited += (_, _) =>
        {
            Anim.Opacity(dim, 0, Anim.Medium);
            Anim.Opacity(_controls, 0, Anim.Medium);
            Anim.Opacity(exit, 0, Anim.Medium);
            _controls.IsHitTestVisible = false;
        };
        DoubleTapped += (_, _) => ExitRequested?.Invoke();

        _p.TrackChanged += OnTrack;
        _p.StateChanged += OnState;
        _tick = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(250);
        _tick.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        OnTrack();
        OnState();
        Tick();
        _tick.Start();
        Anim.Appear(this, 0, 0);
    }

    public void Stop() => _tick.Stop();

    private void OnTrack()
    {
        if (Visibility != Visibility.Visible) return;
        var t = _p.Current;
        _title.Text = t?.Title ?? "재생 중인 곡 없음";
        _artist.Text = t?.Artist ?? "";
        _art.Show(t?.Thumb);
    }

    private void OnState()
    {
        var playing = _p.IsPlaying || (_p.IsBuffering && _playing);
        if (playing == _playing) return;
        _playing = playing;
        _pp.Set(playing);
    }

    private void Tick()
    {
        var d = _p.Duration;
        _progress.Width = d > 0 ? Math.Clamp(_p.Position / d, 0, 1) * ActualWidth : 0;
    }
}
