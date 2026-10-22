using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 지금 곡의 가사 상태 (재생 바 가사 한 줄 · 재생 화면 가사가 함께 쓴다).
/// 자동: ☆ 출처 → LRCLib → YT Music → Musixmatch 순서로 처음 찾은 것. ‹ › 로 출처를 직접 고르면 그 출처만
/// </summary>
public sealed class LyricsState
{
    public static LyricsState Instance { get; } = new();
    public List<LyricLine>? Lines { get; private set; }
    /// <summary>시간 정보가 있는 가사인지 (없으면 따라 흐르지 않고 글자만)</summary>
    public bool Synced { get; private set; }
    public bool Loading { get; private set; }
    public string? ForVideo { get; private set; }
    /// <summary>지금 보이는(또는 찾는) 출처</summary>
    public LyricsSource Source { get; private set; } = LyricsService.Enabled(Settings.Current.LyricsSource) ? Settings.Current.LyricsSource : LyricsSource.LRCLib;
    /// <summary>출처별로 찾았는지 (null = 아직 모름)</summary>
    public Dictionary<LyricsSource, bool> Found { get; } = new();
    public event Action? Changed;
    private CancellationTokenSource? _cts;
    private Track? _track;

    public void Load(Track? t)
    {
        _track = t;
        Found.Clear();
        Run(null);
    }

    /// <summary>‹ › 로 출처를 직접 고름</summary>
    public void Choose(LyricsSource source) => Run(source);

    /// <summary>☆ — 앞으로 이 출처를 먼저 찾는다</summary>
    public void SetPreferred(LyricsSource source)
    {
        Settings.Current.LyricsSource = source;
        Settings.Current.Save();
        Changed?.Invoke();
    }

    private async void Run(LyricsSource? only)
    {
        _cts?.Cancel();
        var t = _track;
        Lines = null;
        Synced = false;
        ForVideo = t?.VideoId;
        Loading = t != null;
        Source = only ?? Settings.Current.LyricsSource;
        Changed?.Invoke();
        if (t == null) return;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            foreach (var src in only is { } o ? new[] { o } : LyricsService.Order(Settings.Current.LyricsSource))
            {
                Source = src;
                Changed?.Invoke();
                var r = await LyricsService.Instance.Get(t, src, cts.Token);
                if (cts.IsCancellationRequested) return;
                Found[src] = r != null;
                if (r == null) continue;
                Lines = r.Lines;
                Synced = r.Synced;
                break;
            }
            if (Lines == null && only == null) Source = Settings.Current.LyricsSource;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Log.Warn($"가사 실패: {e.Message}"); }
        Loading = false;
        Changed?.Invoke();
    }

    /// <summary>지금 시간에 부르는 줄 번호 (-1 = 아직 시작 전 · 시간 없는 가사)</summary>
    public int IndexAt(double seconds)
    {
        var lines = Lines;
        if (lines == null || !Synced) return -1;
        var ms = seconds * 1000;
        for (var i = lines.Count - 1; i >= 0; i--)
            if (lines[i].TimeMs <= ms) return i;
        return -1;
    }
}

/// <summary>
/// 재생 화면의 실시간 가사 — Win2D 로 직접 그린다. pear synced-lyrics 'fancy' + One UI 테마(21 · 50) 값 그대로:
///  · 글자 22px, 줄 사이 여유(위아래 8 + 2), 지금 줄이 화면 가운데에 오도록 부드럽게 스크롤
///  · 다른 줄: Bold(700) · 투명도 0.3 · 크기 0.96 (왼쪽 기준) / 지금 줄: ExtraBold(800) · 1 · 1 — 440ms 로 바뀜
///  · 지금 줄은 앨범 색 빛(text-shadow 24px)이 줄 길이 동안 사그라들고, 그 절반 동안 ±1.75px 살짝 흔들린다
///  · 마우스를 올린 줄은 둥근 옅은 바탕, 누르면 그 위치로 이동. 위 · 아래 끝은 흐려진다
///  · 시간 없는 가사(YT Music 글자 가사 등)는 전부 밝게, 휠로 넘긴다
/// </summary>
public sealed class LyricsView : Grid
{
    private const float FontSize = 22;
    private const float LineGap = 20;
    private const float FadeTop = 44, FadeBottom = 56;
    private const float Inset = 12;

    /// <summary>빛 색 (앨범 색) — MainWindow 가 곡이 바뀔 때 넣는다</summary>
    public static Color Glow { get; set; } = Colors.White;

    private readonly CanvasControl _canvas = new();
    private readonly TextBlock _empty;
    private float _scroll, _scrollTarget;
    private float _userOffset; // 휠로 직접 굴린 양 (몇 초 뒤 제자리로)
    private long _lastWheel;
    private readonly List<(float Top, float Height)> _layout = new();
    /// <summary>줄마다 글자 배치를 한 번만 만들어 둔다 (지금 줄 굵게 · 나머지) — 매 프레임 새로 만들지 않게</summary>
    private readonly List<(CanvasTextLayout On, CanvasTextLayout? Off)> _text = new();
    private static readonly CanvasTextFormat FmtOn = Format(true), FmtOff = Format(false);
    private bool _loaded, _looping, _sleeping;
    private float _layoutWidth = -1;
    private List<LyricLine>? _layoutLines;
    private readonly float[] _glow = new float[4096];
    private long _last;
    private bool _active;
    private int _current = -1;
    private long _currentAt;
    private int _hover = -1;

    public LyricsView()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);
        _empty = Theme.Text("", 16, FontWeights.SemiBold, Theme.Secondary);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_empty);
        LyricsState.Instance.Changed += OnLyricsChanged;
        PlayerService.Instance.StateChanged += Wake;
        _canvas.CreateResources += (_, _) => ClearText();
        Loaded += (_, _) => { _loaded = true; SyncLoop(); OnLyricsChanged(); };
        Unloaded += (_, _) => { _loaded = false; SyncLoop(); };
        Tapped += OnTapped;
        PointerMoved += (_, e) =>
        {
            var h = LineAt((float)e.GetCurrentPoint(this).Position.Y);
            if (h == _hover) return;
            _hover = h;
            Wake();
        };
        PointerExited += (_, _) => { _hover = -1; Wake(); };
        PointerWheelChanged += (_, e) =>
        {
            _userOffset -= e.GetCurrentPoint(this).Properties.MouseWheelDelta * 0.6f;
            _lastWheel = Environment.TickCount64;
            e.Handled = true;
            Wake();
        };
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    }

    /// <summary>탭이 보일 때만 그린다</summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            _sleeping = false;
            SyncLoop();
            if (value) _canvas.Invalidate();
        }
    }

    /// <summary>
    /// 매 프레임 그리기는 가사 탭이 보일 때만, 그리고 멈춘 채 다 가라앉으면 쉰다
    /// (프레임 구독만 있어도 앱 전체가 1초에 60번 다시 그려져서)
    /// </summary>
    private void SyncLoop()
    {
        var want = _loaded && _active && !_sleeping;
        if (want == _looping) return;
        _looping = want;
        if (want)
        {
            _last = 0;
            CompositionTarget.Rendering += OnFrame;
        }
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void Wake()
    {
        _sleeping = false;
        SyncLoop();
    }

    private void ClearText()
    {
        foreach (var (on, off) in _text)
        {
            on.Dispose();
            off?.Dispose();
        }
        _text.Clear();
        _layoutLines = null;
    }

    private void OnLyricsChanged()
    {
        var s = LyricsState.Instance;
        _empty.Text = s.Loading ? "가사를 불러오는 중…" : s.Lines == null ? (s.ForVideo == null ? "" : "이 출처에서 가사를 찾지 못했어요") : "";
        _layoutLines = null;
        _userOffset = 0;
        _current = -1;
        if (!s.Synced) _scroll = 0;
        Array.Clear(_glow);
        Wake();
        _canvas.Invalidate();
    }

    private static CanvasTextFormat Format(bool active) => new()
    {
        FontFamily = Win2DFont.Family,
        FontSize = FontSize,
        FontWeight = active ? FontWeights.ExtraBold : FontWeights.Bold,
        WordWrapping = CanvasWordWrapping.WholeWord,
        LineSpacingMode = CanvasLineSpacingMode.Proportional,
        LineSpacing = 1.3f,
    };

    private void EnsureLayout(CanvasControl sender, float width)
    {
        var lines = LyricsState.Instance.Lines;
        if (lines == _layoutLines && Math.Abs(width - _layoutWidth) < 0.5f) return;
        ClearText();
        _layoutLines = lines;
        _layoutWidth = width;
        _layout.Clear();
        if (lines == null) return;
        var y = 0f;
        foreach (var l in lines)
        {
            var tl = new CanvasTextLayout(sender, string.IsNullOrEmpty(l.Text) ? "♪" : l.Text, FmtOn, width, 9999);
            var h = (float)tl.LayoutBounds.Height;
            _layout.Add((y, h));
            _text.Add((tl, null));
            y += h + LineGap;
        }
    }

    private void OnFrame(object? sender, object e)
    {
        if (!_active || LyricsState.Instance.Lines == null)
        {
            _sleeping = true;
            SyncLoop();
            return;
        }
        var now = Environment.TickCount64;
        if (now - _last < 15) return;
        var dt = _last == 0 ? 0.016f : Math.Min((now - _last) / 1000f, 0.1f);
        _last = now;
        var synced = LyricsState.Instance.Synced;
        // 시간 없는 가사는 휠로 굴린 자리에 그대로 둔다
        if (synced && now - _lastWheel > 3500) _userOffset += (0 - _userOffset) * (1 - MathF.Exp(-dt / 0.25f));
        if (!synced) _userOffset = Math.Clamp(_userOffset, 0, Math.Max(0, (_layout.Count > 0 ? _layout[^1].Top + _layout[^1].Height : 0) - (float)ActualHeight + 80));
        var p = PlayerService.Instance;
        var idx = LyricsState.Instance.IndexAt(p.Position + 0.15);
        if (idx != _current) { _current = idx; _currentAt = now; }
        if (idx >= 0 && idx < _layout.Count)
        {
            var (top, h) = _layout[idx];
            // 지금 줄이 가운데에 (pear: scrollToIndex align center)
            _scrollTarget = top + h / 2 - (float)ActualHeight / 2;
        }
        else _scrollTarget = synced ? -(float)ActualHeight / 2 + 40 : -8;
        var target = _scrollTarget + _userOffset;
        _scroll += (target - _scroll) * (1 - MathF.Exp(-dt / 0.14f));
        // 줄 상태 전환 440ms (ease) ≈ 시간 상수 0.11초
        var settled = MathF.Abs(target - _scroll) < 0.3f && (!synced || MathF.Abs(_userOffset) < 0.3f);
        for (var i = 0; i < _layout.Count && i < _glow.Length; i++)
        {
            var want = i == idx || !synced ? 1f : 0f;
            _glow[i] += (want - _glow[i]) * (1 - MathF.Exp(-dt / 0.11f));
            if (MathF.Abs(want - _glow[i]) > 0.003f) settled = false;
        }
        _canvas.Invalidate();
        // 멈춰 있고(또는 시간 없는 가사) 스크롤 · 빛 · 흔들림이 다 끝났으면 쉰다 — 재생 · 마우스 · 휠 · 가사 바뀜에 깬다
        if (settled && (!p.IsPlaying || !synced) && now - _currentAt > 6500)
        {
            _sleeping = true;
            SyncLoop();
        }
    }

    private int LineAt(float y)
    {
        y += _scroll;
        for (var i = 0; i < _layout.Count; i++)
        {
            var (top, lh) = _layout[i];
            if (y >= top - LineGap / 2 && y <= top + lh + LineGap / 2) return i;
        }
        return -1;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var lines = LyricsState.Instance.Lines;
        if (lines == null) return;
        var w = (float)ActualWidth - Inset * 2 - 8;
        var h = (float)ActualHeight;
        if (w <= 0) return;
        EnsureLayout(sender, w);
        var ds = args.DrawingSession;
        var synced = LyricsState.Instance.Synced;
        var now = Environment.TickCount64;
        for (var i = 0; i < _layout.Count; i++)
        {
            var (top, lh) = _layout[i];
            var y = top - _scroll;
            if (y + lh < -24 || y > h + 24) continue;
            // 위 · 아래 끝에서는 흐려진다
            // 줄 전체가 칸 안에 들어와야 진해진다 — 위는 줄 윗변, 아래는 줄 아랫변 기준 (아래 끝에서 글자가 반쯤 잘려 보이던 것)
            var edge = Math.Clamp(Math.Min((y + 8) / FadeTop, (h - (y + lh) - 4) / FadeBottom), 0, 1);
            edge = edge * edge * (3 - 2 * edge);
            if (edge <= 0.01f) continue;
            var g = _glow[i];
            var text = string.IsNullOrEmpty(lines[i].Text) ? "♪" : lines[i].Text;
            var isCurrent = synced && i == _current;

            // 마우스를 올린 줄: 둥근(16) 옅은 바탕
            if (i == _hover && synced)
                ds.FillRoundedRectangle(4, y - 8, (float)ActualWidth - 16, lh + 16, 16, 16, Color.FromArgb((byte)(15 * edge), 255, 255, 255));

            var alpha = (0.3f + 0.7f * g) * edge;
            var scale = 0.96f + 0.04f * g;
            var wobble = 0f;
            var glowAmt = 0f;
            if (isCurrent)
            {
                var dur = Math.Clamp(LineDuration(lines, i), 600, 6000);
                var t = (now - _currentAt) / (float)dur;
                glowAmt = Math.Clamp(1 - t, 0, 1);
                // lyrics-wobble: 줄 길이의 절반 동안 0 → +1.75 → −1.75 → 0
                var wt = Math.Clamp(t * 2, 0, 1);
                wobble = wt < 1 / 3f ? 1.75f * wt * 3 : wt < 2 / 3f ? 1.75f - 3.5f * (wt - 1 / 3f) * 3 : -1.75f + 1.75f * (wt - 2 / 3f) * 3;
            }
            var (on, off) = _text[i];
            if (!(isCurrent || !synced) && off == null)
            {
                off = new CanvasTextLayout(sender, text, FmtOff, w, 9999);
                _text[i] = (on, off);
            }
            var tl = isCurrent || !synced ? on : off!;
            var x = Inset;
            var oldTransform = ds.Transform;
            // 크기는 왼쪽 가운데 기준 (transform-origin: 0 50%)
            ds.Transform = Matrix3x2.CreateScale(scale, scale, new Vector2(x, y + lh / 2)) * oldTransform;
            if (glowAmt > 0.01f && Glow.A > 0)
            {
                // 앨범 색 빛 (text-shadow 0 0 1.5rem, 70%) — 줄 길이 동안 0 으로
                using var cl = new CanvasCommandList(sender);
                using (var cds = cl.CreateDrawingSession())
                    cds.DrawTextLayout(tl, 0, 0, Color.FromArgb((byte)(178 * edge), Glow.R, Glow.G, Glow.B));
                using var blur = new GaussianBlurEffect { Source = cl, BlurAmount = 12 * glowAmt, BorderMode = EffectBorderMode.Soft };
                ds.DrawImage(blur, x, y + wobble);
            }
            ds.DrawTextLayout(tl, x, y + wobble, Colors.White.WithAlpha(alpha));
            ds.Transform = oldTransform;
        }
    }

    private static int LineDuration(List<LyricLine> lines, int i) =>
        i + 1 < lines.Count ? lines[i + 1].TimeMs - lines[i].TimeMs : 2000;

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        var lines = LyricsState.Instance.Lines;
        if (lines == null || !LyricsState.Instance.Synced) return;
        var i = LineAt((float)e.GetPosition(this).Y);
        if (i < 0) return;
        PlayerService.Instance.Seek(lines[i].TimeMs / 1000.0 + 0.01);
        _userOffset = 0;
    }
}

/// <summary>
/// 가사 출처 고르기 (pear synced-lyrics 와 같은 모양) — 유리 카드 안에 ‹ [✓ 출처 이름 ☆] ›, 아래 점(지금 몇 번째).
/// ✓ = 이 출처에서 찾음, ☆ 누르면 앞으로 먼저 찾을 출처(★)
/// </summary>
public sealed class LyricsSourceBar : Grid
{
    private static readonly (LyricsSource Src, string Name)[] Sources =
        new (LyricsSource Src, string Name)[] { (LyricsSource.LRCLib, "LRCLib"), (LyricsSource.YTMusic, "YTMusic"), (LyricsSource.Musixmatch, "MusixMatch"), (LyricsSource.Genius, "Genius") }
            .Where(x => LyricsService.Enabled(x.Src)).ToArray();
    private readonly StackPanel _center = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>바깥 칸 — 이 높이를 0 ↔ 84 로 접었다 폈다 한다 (카드는 그 안에서 잘린다)</summary>
    public Grid Host { get; } = new() { Height = FullHeight, VerticalAlignment = VerticalAlignment.Top };
    private const double FullHeight = 84;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _pointerIn, _shown = true;

    /// <summary>잠깐 보여 주고 3초 뒤 접는다 (가사 탭을 열 때 · 곡 · 출처가 바뀔 때 · 가사 위쪽에 마우스를 올릴 때)</summary>
    public void Peek()
    {
        if (!_shown)
        {
            _shown = true;
            AnimateHost(FullHeight, 1);
        }
        _hideTimer.Stop();
        if (!_pointerIn) _hideTimer.Start();
    }

    private void HideNow()
    {
        if (!_shown || _pointerIn) return;
        _shown = false;
        AnimateHost(0, 0);
    }

    private void AnimateHost(double height, double opacity)
    {
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var h = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = height,
            Duration = TimeSpan.FromMilliseconds(Anim.D(360)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.ExponentialEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Exponent = 5 },
            EnableDependentAnimation = true,
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(h, Host);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(h, "Height");
        sb.Children.Add(h);
        sb.Begin();
        Anim.Opacity(this, (float)opacity, Anim.Fast);
    }

    public LyricsSourceBar()
    {
        Host.Children.Add(this);
        VerticalAlignment = VerticalAlignment.Top;
        _hideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hideTimer.Interval = TimeSpan.FromSeconds(3);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideNow();
        PointerEntered += (_, _) => { _pointerIn = true; _hideTimer.Stop(); };
        PointerExited += (_, _) => { _pointerIn = false; _hideTimer.Stop(); _hideTimer.Start(); };
        Height = 70;
        // pear 21: 가사 출처 카드 — 칩 색, 16px 둥근 모서리, 테두리 없음
        CornerRadius = new CornerRadius(16);
        Background = Theme.B(Theme.Chip);
        Margin = new Thickness(0, 6, 0, 8);
        var prev = new IconButton(36) { Content = Theme.Icon(Icons.ChevronLeft, 20, Colors.White, stroke: true, strokeWidth: 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24, 0, 0, 0) };
        var next = new IconButton(36) { Content = Theme.Icon(Icons.ChevronRight, 20, Colors.White, stroke: true, strokeWidth: 2), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 24, 0) };
        prev.Click += () => Step(-1);
        next.Click += () => Step(1);
        Children.Add(prev);
        Children.Add(next);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
        mid.Children.Add(_center);
        mid.Children.Add(_dots);
        Children.Add(mid);
        LyricsState.Instance.Changed += Update;
        Update();
    }

    private static int IndexOf(LyricsSource s) => Math.Max(0, Array.FindIndex(Sources, x => x.Src == s));

    private void Step(int d)
    {
        var i = (IndexOf(LyricsState.Instance.Source) + d + Sources.Length) % Sources.Length;
        Anim.Appear(_center, 0, 0);
        LyricsState.Instance.Choose(Sources[i].Src);
    }

    private void Update()
    {
        Peek();
        var s = LyricsState.Instance;
        var i = IndexOf(s.Source);
        _center.Children.Clear();
        if (s.Found.TryGetValue(s.Source, out var found) && found)
        {
            // ✓ 흰 동그라미 안 체크
            var g = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new XEllipse { Fill = Theme.B(Colors.White) });
            g.Children.Add(Theme.Icon(Icons.Check, 14, Colors.Black, stroke: true, strokeWidth: 2.6));
            _center.Children.Add(g);
        }
        var name = Theme.Text(Sources[i].Name, 15, FontWeights.SemiBold, Theme.OnSurface);
        name.VerticalAlignment = VerticalAlignment.Center;
        _center.Children.Add(name);
        var preferred = Settings.Current.LyricsSource == s.Source;
        var star = new IconButton(28) { Content = Theme.Icon(Icons.Star, 20, Colors.White, stroke: !preferred, strokeWidth: 1.6) };
        Tip.Set(star, preferred ? "기본 가사 출처" : "기본 가사 출처로 정하기");
        var src = s.Source;
        star.Click += () => LyricsState.Instance.SetPreferred(src);
        _center.Children.Add(star);
        _dots.Children.Clear();
        for (var k = 0; k < Sources.Length; k++)
            _dots.Children.Add(new XEllipse { Width = 5, Height = 5, Fill = Theme.B(k == i ? Colors.White : Color.FromArgb(90, 255, 255, 255)) });
    }
}
