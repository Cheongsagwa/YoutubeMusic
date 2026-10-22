using Microsoft.UI.Input;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>곡 한 줄 (검색 결과 · 다음 트랙) — 44px 둥근 아트, 곡명, 가수 • 길이</summary>
public sealed class TrackRow : Grid
{
    private readonly Border _hover;
    private readonly XRect _divider;
    private readonly Grid _body;
    private Border? _swipeBg;
    private FrameworkElement? _swipeIcon;
    private bool _armed;
    private readonly Grid _artBox;
    private FrameworkElement? _playOverlay;
    private Cover? _cover;
    private Equalizer? _eq;
    public Track Track { get; }
    public event Action<TrackRow>? Click;

    /// <param name="subtitle">둘째 줄 (기본: 가수). 플레이리스트에선 '가수 • 앨범'</param>
    /// <param name="number">앨범 수록곡처럼 앨범아트 대신 번호를 보일 때</param>
    public TrackRow(Track t, bool divider = true, string? subtitle = null, int? number = null)
    {
        Track = t;
        StreamResolver.HoverPrefetch(this, () => t.VideoId);
        Height = 68;
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        _hover = new Border { CornerRadius = new CornerRadius(14), Background = Theme.B(Theme.ChipHover) };
        Anim.V(_hover).Opacity = 0;
        Children.Add(_hover);
        _divider = new XRect { Height = 1, Fill = Theme.B(Theme.Divider), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 0) };
        Divider = divider;
        Children.Add(_divider);

        var g = _body = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _artBox = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
        if (number is { } n)
        {
            var num = Theme.Text(n.ToString(), 15, FontWeights.SemiBold, Theme.Secondary);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            _artBox.Children.Add(num);
        }
        else
        {
            _cover = new Cover(t.Thumb, 44, 44, 78, Theme.RadiusThumb, Color.FromArgb(20, 255, 255, 255));
            _artBox.Children.Add(_cover);
            // 마우스를 올리면 아트를 어둡게(pear 80: 검은 38% 덮개 느낌) + 재생 아이콘.
            // 둥근 검은 덮개를 따로 얹으면 아트와 덮개 가장자리가 겹쳐 모서리가 깨져 보여서, 아트 자체를 어둡게 한다 (Cover.Dim)
            _playOverlay = Theme.Icon(Icons.Play, 20, Colors.White);
            Anim.V(_playOverlay).Opacity = 0;
            _artBox.Children.Add(_playOverlay);
        }
        g.Children.Add(_artBox);
        var text = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Theme.Text(t.Title, 14, FontWeights.Bold, Theme.OnSurface));
        // 빈 부제("")면 곡명 한 줄만 (앨범 화면에서 가수가 앨범 가수와 같을 때)
        if ((subtitle ?? t.Artist).Length > 0) text.Children.Add(Theme.Text(subtitle ?? t.Artist, 14, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var dur = Theme.Text(t.DurationText, 14, FontWeights.Normal, Theme.Secondary);
        dur.VerticalAlignment = VerticalAlignment.Center;
        dur.HorizontalAlignment = HorizontalAlignment.Right;
        // 마우스를 올리면 길이 자리에 ⋮ (pear 재생 목록 줄)
        var more = new IconButton(36) { Content = Theme.Icon(Icons.More, 22, Colors.White), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -6, 0) };
        Tip.Set(more, "작업 메뉴");
        more.Click += () => TrackMenu.Show(more, Track, QueueIndex);
        Anim.V(more).Opacity = 0;
        more.IsHitTestVisible = false;
        var right = new Grid { MinWidth = 36, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(dur);
        right.Children.Add(more);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        Children.Add(g);

        PointerEntered += (_, _) =>
        {
            Anim.Opacity(_hover, 1, 160);
            Anim.Opacity(dur, 0, 120);
            Anim.Opacity(more, 1, 160);
            more.IsHitTestVisible = true;
            if (_playOverlay != null && _eq == null) { Anim.Opacity(_playOverlay, 1, 160); _cover?.Dim(true); }
        };
        PointerExited += (_, _) =>
        {
            Anim.Opacity(_hover, _eq != null ? 1 : 0, 240);
            Anim.Opacity(dur, 1, 240);
            Anim.Opacity(more, 0, 200);
            more.IsHitTestVisible = false;
            if (_playOverlay != null) Anim.Opacity(_playOverlay, 0, 200);
            _cover?.Dim(false);
        };
        Tapped += (_, _) => Click?.Invoke(this);
        RightTapped += (_, e) =>
        {
            e.Handled = true;
            TrackMenu.Show(this, Track, QueueIndex, e.GetPosition(null));
        };
    }

    /// <summary>순서 바꾸려고 잡은 줄 — 살짝 떠오르며 커지고 배경이 밝아진다</summary>
    public void Lift(bool on)
    {
        Anim.Scale(this, on ? 1.03f : 1f);
        _divider.Opacity = on ? 0 : 1;
        if (on) { _hover.Background = Theme.B(Color.FromArgb(46, 255, 255, 255)); Anim.Opacity(_hover, 1, 120); }
        else _hover.Background = Theme.B(_eq != null ? Theme.Selected : Theme.ChipHover);
    }

    /// <summary>옆으로 밀기 (목록에서 빼기) — 줄이 손을 따라가고 뒤에 빨간 휴지통이 드러난다. armed = 놓으면 빠지는 거리</summary>
    public void Swipe(double dx, bool armed)
    {
        if (_swipeBg == null)
        {
            _swipeIcon = Theme.Icon(Icons.Trash, 22, Colors.White, true);
            _swipeBg = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = Theme.B(Color.FromArgb(255, 0xE5, 0x48, 0x4D)),
                Padding = new Thickness(26, 0, 26, 0),
                Child = _swipeIcon,
                IsHitTestVisible = false,
            };
            Children.Insert(0, _swipeBg);
        }
        _swipeIcon!.VerticalAlignment = VerticalAlignment.Center;
        _swipeIcon.HorizontalAlignment = dx > 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        Anim.V(_swipeBg).Opacity = (float)Math.Clamp(Math.Abs(dx) / 64, 0, 1);
        if (armed != _armed)
        {
            _armed = armed;
            Anim.Scale(_swipeIcon, armed ? 1.25f : 1f);
        }
        var to = new Vector3((float)dx, 0, 0);
        Anim.SetTranslation(_hover, to);
        Anim.SetTranslation(_body, to);
    }

    /// <summary>덜 밀고 놓으면 제자리로</summary>
    public void SwipeBack()
    {
        Anim.TranslateTo(_hover, Vector3.Zero, 320);
        Anim.TranslateTo(_body, Vector3.Zero, 320);
        if (_swipeBg != null) Anim.Opacity(_swipeBg, 0, 320);
        if (_swipeIcon != null && _armed) Anim.Scale(_swipeIcon, 1f);
        _armed = false;
    }

    /// <summary>끝까지 밀어 낸다 (dir: 1 오른쪽, -1 왼쪽)</summary>
    public void SwipeOut(int dir)
    {
        var to = new Vector3((float)(dir * (ActualWidth + 24)), 0, 0);
        Anim.TranslateTo(_hover, to, 200);
        Anim.TranslateTo(_body, to, 200);
        if (_swipeBg != null) Anim.Opacity(_swipeBg, 0, 200, 120);
    }

    /// <summary>목록에서 빠진 뒤 (줄은 다시 쓸 수 있으니) 원래대로</summary>
    public void SwipeReset()
    {
        Anim.SetTranslation(_hover, Vector3.Zero);
        Anim.SetTranslation(_body, Vector3.Zero);
        if (_swipeBg != null) Anim.V(_swipeBg).Opacity = 0;
        if (_swipeIcon != null) Anim.V(_swipeIcon).Scale = Vector3.One;
        _armed = false;
    }

    /// <summary>다음 트랙 목록 안의 자리 (있으면 우클릭 메뉴에 '현재 재생목록에서 삭제')</summary>
    public int? QueueIndex { get; set; }

    /// <summary>아래 구분선 (다음 트랙 목록은 줄을 다시 쓰므로 바꿀 수 있게)</summary>
    public bool Divider
    {
        set => _divider.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>지금 재생 중인 곡 표시 — 아트 오른쪽 아래 이퀄라이저</summary>
    public void SetCurrent(bool current)
    {
        if (current && _eq == null)
        {
            _eq = new Equalizer { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 4) };
            _artBox.Children.Add(_eq);
            _hover.Background = Theme.B(Theme.Selected);
            Anim.V(_hover).Opacity = 1;
        }
        else if (!current && _eq != null)
        {
            _artBox.Children.Remove(_eq);
            _eq.Stop();
            _eq = null;
            _hover.Background = Theme.B(Theme.ChipHover);
            Anim.V(_hover).Opacity = 0;
        }
    }
}

/// <summary>막대 세 개 이퀄라이저 — 재생 중엔 불규칙하게 오르내리고, 멈추면 점으로 부드럽게 내려앉는다</summary>
public sealed class Equalizer : StackPanel
{
    private readonly XRect[] _bars = new XRect[3];
    private readonly (float F, float P, float A)[][] _waves;
    private float _amp, _t;
    private long _last;
    private bool _running;

    public Equalizer()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        Height = 16;
        IsHitTestVisible = false;
        var r = Random.Shared;
        _waves = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, 3)
            .Select(_ => ((float)(0.6 + r.NextDouble() * 2.2), (float)(r.NextDouble() * Math.PI * 2), (float)(0.25 + r.NextDouble() * 0.5))).ToArray()).ToArray();
        for (var i = 0; i < 3; i++)
        {
            _bars[i] = new XRect { Width = 3, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.B(Colors.White), VerticalAlignment = VerticalAlignment.Bottom };
            Children.Add(_bars[i]);
        }
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private DispatcherQueueTimer? _idle;

    private void Start()
    {
        if (_running) return;
        _running = true;
        _last = 0;
        _idle?.Stop();
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop()
    {
        _idle?.Stop();
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>
    /// 멈춰서 점으로 가라앉았거나 화면에 안 보이면(재생 화면이 닫힘 등) 프레임 구독을 끊고
    /// 0.4초마다만 살펴본다 — 프레임 구독이 있으면 앱 전체가 계속 다시 그려져서
    /// </summary>
    private void Idle()
    {
        if (_running)
        {
            _running = false;
            CompositionTarget.Rendering -= OnFrame;
        }
        if (_idle == null)
        {
            _idle = DispatcherQueue.CreateTimer();
            _idle.Interval = TimeSpan.FromMilliseconds(400);
            _idle.Tick += (_, _) =>
            {
                if (IsLoaded && Shown() && (PlayerService.Instance.IsPlaying || _amp > 0)) Start();
            };
        }
        _idle.Start();
    }

    private bool Shown()
    {
        for (DependencyObject? d = this; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private int _frames;

    private void OnFrame(object? s, object e)
    {
        var now = Environment.TickCount64;
        if (_last != 0 && now - _last < 30) return; // 30fps 면 충분
        var dt = _last == 0 ? 0.016f : Math.Min((now - _last) / 1000f, 0.1f);
        _last = now;
        var playing = PlayerService.Instance.IsPlaying;
        _amp += ((playing ? 1 : 0) - _amp) * (1 - MathF.Exp(-dt / (playing ? 0.12f : 0.16f)));
        if (!playing && _amp < 0.005f) _amp = 0;
        if ((!playing && _amp == 0) || (++_frames % 15 == 0 && !Shown()))
        {
            foreach (var b in _bars) b.Height = 3 + 13 * _amp;
            Idle();
            return;
        }
        _t += dt * (0.4f + 0.6f * _amp);
        for (var i = 0; i < 3; i++)
        {
            float v = 0, n = 0;
            foreach (var w in _waves[i])
            {
                v += w.A * (0.5f + 0.5f * MathF.Sin(_t * w.F * MathF.PI * 2 + w.P));
                n += w.A;
            }
            _bars[i].Height = 3 + 13 * _amp * (v / n);
        }
    }
}

/// <summary>메인 화면 카드 — 둥근 아트(정사각 · 16:9), 제목, 부제목. 마우스를 올리면 아트가 살짝 커진다</summary>
public sealed class CardView : StackPanel
{
    public Card Card { get; }
    public event Action<Card>? Click;

    /// <param name="square">16:9 아트도 가운데를 잘라 정사각으로 (격자가 흐트러지지 않게)</param>
    /// <param name="fixedHeight">제목 · 부제목 두 줄씩 자리를 늘 비워 둬서 아래 줄 카드 위치가 같게</param>
    /// <param name="playButton">마우스를 올리면 오른쪽 아래 재생 버튼 (다시 듣기 2행은 pear 57 처럼 없음)</param>
    public CardView(Card c, double size = 164, bool square = false, bool fixedHeight = false, bool round = false, bool playButton = true)
    {
        Card = c;
        StreamResolver.HoverPrefetch(this, () => c.PlaylistId == null ? c.VideoId : null);
        var w = c.Wide && !square ? size * 16 / 9 : size;
        if (fixedHeight) Height = size + 92;
        Width = w;
        Spacing = 8;
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        var radius = round ? size / 2 : 18;
        // 칸에는 둥근 자르기를 걸지 않는다 (자식 자르기는 계단짐) — 아트 · 덮개가 각자 둥근 배경으로 그린다
        var art = new Grid { Width = w, Height = size };
        // 16:9 를 정사각으로 자를 땐 가운데 기준(Cover), 높이에 맞게 더 크게 받아 흐리지 않게
        var cropWide = c.Wide && square;
        var img = new Cover(c.Thumb, w, size, (int)(cropWide ? size * 16 / 9 : w), radius, Color.FromArgb(20, 255, 255, 255));
        // 마우스를 올리면: 위쪽만 어둡게(0.55 → 0, 45%) + 오른쪽 위 ⋮ + 오른쪽 아래 재생 (YT Music 카드 · pear 57)
        var artGrid = art;
        artGrid.Children.Add(img);
        var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(140, 0, 0, 0), Offset = 0 });
        grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0.45 });
        var shade = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(radius), Background = grad };
        Anim.V(shade).Opacity = 0;
        artGrid.Children.Add(shade);
        var more = new IconButton(36) { Content = Theme.Icon(Icons.More, 22, Colors.White), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 4, 0) };
        more.Click += () => TrackMenu.ShowCard(more, c, more.TransformToVisual(null).TransformPoint(new Point(-220, 36)));
        Anim.V(more).Opacity = 0;
        more.IsHitTestVisible = false;
        artGrid.Children.Add(more);
        IconButton? play = null;
        if (playButton && !round)
        {
            play = new IconButton(40) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) };
            var pg = new Grid();
            pg.Children.Add(new XEllipse { Width = 40, Height = 40, Fill = Theme.B(Color.FromArgb(150, 0, 0, 0)) });
            pg.Children.Add(Theme.Icon(Icons.Play, 20, Colors.White));
            play.Content = pg;
            play.HoverBrush = new SolidColorBrush(Colors.Transparent);
            play.Click += () => Play.Card(c with { PageType = "play" });
            Anim.V(play).Opacity = 0;
            play.IsHitTestVisible = false;
            artGrid.Children.Add(play);
        }
        Children.Add(art);
        var title = Theme.Text(c.Title, 14, FontWeights.Bold, Theme.OnSurface, 2);
        Children.Add(title);
        var subtitle = ShelvesView.WithoutMe(c.Subtitle);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = Theme.Text(subtitle, 14, FontWeights.Normal, Theme.Secondary, 2);
            sub.Margin = new Thickness(0, -6, 0, 0);
            Children.Add(sub);
        }
        // pear 40: 아트 1.04 (morph 560ms ease)
        PointerEntered += (_, _) =>
        {
            img.Zoom(1.04, Anim.Morph);
            Anim.Opacity(shade, 1, Anim.Medium);
            Anim.Opacity(more, 1, Anim.Fast);
            more.IsHitTestVisible = true;
            if (play != null) { Anim.Opacity(play, 1, Anim.Fast); play.IsHitTestVisible = true; }
        };
        PointerExited += (_, _) =>
        {
            img.Zoom(1, Anim.Morph);
            Anim.Opacity(shade, 0, Anim.Medium);
            Anim.Opacity(more, 0, Anim.Fast);
            more.IsHitTestVisible = false;
            if (play != null) { Anim.Opacity(play, 0, Anim.Fast); play.IsHitTestVisible = false; }
        };
        Anim.Pressable(art);
        Tapped += (_, _) => Click?.Invoke(c);
        RightTapped += (_, e) =>
        {
            e.Handled = true;
            TrackMenu.ShowCard(this, c, e.GetPosition(null));
        };
    }
}

/// <summary>재생 바 위에 잠깐 떴다 사라지는 알림 (pear 의 토스트)</summary>
public static class Toast
{
    private static Border? _box;
    private static TextBlock? _text;
    private static int _gen;

    public static void Attach(Grid root)
    {
        _text = Theme.Text("", 14, FontWeights.SemiBold, Colors.White);
        _box = new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = Theme.Glass(0.75),
            Padding = new Thickness(20, 10, 20, 10),
            Child = _text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, Theme.BarHeight + Theme.FloatGap * 2 + 12),
            IsHitTestVisible = false,
        };
        Anim.V(_box).Opacity = 0;
        root.Children.Add(_box);
    }

    public static async void Show(string message)
    {
        if (_box == null || _text == null) return;
        var g = ++_gen;
        _text.Text = message;
        Anim.RiseSpring(_box);
        await Task.Delay(2600);
        if (g == _gen) Anim.Opacity(_box, 0, Anim.Fast);
    }
}

/// <summary>가로로 채우다 넘치면 다음 줄로 (보관함 격자)</summary>
public sealed class WrapPanel : Panel
{
    public double HSpacing { get; set; } = 16;
    public double VSpacing { get; set; } = 24;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowH = 0, maxW = 0;
        foreach (var c in Children)
        {
            c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > available.Width)
            {
                y += rowH + VSpacing;
                x = 0;
                rowH = 0;
            }
            x += d.Width + HSpacing;
            maxW = Math.Max(maxW, x - HSpacing);
            rowH = Math.Max(rowH, d.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? maxW : available.Width, y + rowH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowH = 0;
        foreach (var c in Children)
        {
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > final.Width)
            {
                y += rowH + VSpacing;
                x = 0;
                rowH = 0;
            }
            c.Arrange(new Rect(x, y, d.Width, d.Height));
            x += d.Width + HSpacing;
            rowH = Math.Max(rowH, d.Height);
        }
        return final;
    }
}

