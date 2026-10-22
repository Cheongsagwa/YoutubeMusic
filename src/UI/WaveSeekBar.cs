using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 삼성 뮤직 같은 파도 재생 바 (pear-desktop 테마 wave.ts 를 옮긴 것).
/// 직선은 그대로, 재생한 구간 위로 뾰족한 /\ 언덕들이 저마다 다른 진하기 · 속도로 흘러간다.
/// 멈추면 파도가 그 자리에서 굳은 채 부드럽게 가라앉는다. 누르거나 끌어서 위치를 옮길 수 있다.
/// </summary>
public sealed class WaveSeekBar : Grid
{
    private const float LineWidth = 4;
    private const float WaveHeight = 13;
    private const float KnobRadius = 9;
    private const float KnobRing = 2;
    private const float EdgeFade = 40;
    private const float Rise = 0.45f, Fall = 1.1f;

    private sealed record Layer(bool Grey, float A0, float A1, int Count, float S0, float S1, float W0, float W1, float P0, float P1);

    private static readonly Layer[] Layers =
    [
        new(true, 0.10f, 0.28f, 7, 45, 85, 40, 80, 0.55f, 1.0f),
        new(false, 0.20f, 0.50f, 8, 55, 105, 30, 65, 0.45f, 0.95f),
        new(false, 0.45f, 0.85f, 8, 65, 125, 22, 48, 0.30f, 0.70f),
    ];

    private sealed class Bump
    {
        public float X, Speed, Width, Peak, Wobble, Phase, Alpha;
    }

    private readonly CanvasControl _canvas = new();
    private readonly List<Bump>[] _bumps = Layers.Select(_ => new List<Bump>()).ToArray();
    private readonly Random _rand = new();
    private float _amp, _flow;
    private long _lastTicks;
    private bool _rendering;
    private Color _color = AlbumColor.Fallback;

    private bool _dragging;
    private double _dragFraction;

    /// <summary>재생 위치 (0~1) 를 매 프레임 물어볼 함수</summary>
    public Func<double> Progress { get; set; } = () => 0;
    public Func<bool> IsPlaying { get; set; } = () => false;
    /// <summary>곡 길이(초) — 마우스를 올린 자리의 시간을 보여 줄 때</summary>
    public Func<double> Duration { get; set; } = () => 0;
    public event Action<double>? Seek;

    public WaveSeekBar()
    {
        Height = 44;
        Background = new SolidColorBrush(Colors.Transparent);
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);
        Loaded += (_, _) => StartRendering();
        Unloaded += (_, _) =>
        {
            StopRendering();
            _idle?.Stop();
            _canvas.RemoveFromVisualTree();
        };
        PointerPressed += (_, e) =>
        {
            _dragging = true;
            StartRendering();
            CapturePointer(e.Pointer);
            _dragFraction = Fraction(e.GetCurrentPoint(this).Position.X);
            _canvas.Invalidate();
        };
        PointerMoved += (_, e) =>
        {
            var x = e.GetCurrentPoint(this).Position.X;
            // 마우스를 올린 자리의 시간 (YT Music 재생 바처럼 말풍선으로)
            var dur = Duration();
            if (dur > 0)
            {
                var p = TransformToVisual(null).TransformPoint(new Point(x, 0));
                Tip.ShowNow(Fmt.Time(Fraction(x) * dur), p.X, p.Y + 10);
            }
            if (!_dragging) return;
            _dragFraction = Fraction(x);
            _canvas.Invalidate();
        };
        PointerExited += (_, _) => { if (!_dragging) Tip.Hide(); };
        PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            ReleasePointerCapture(e.Pointer);
            Seek?.Invoke(_dragFraction);
            Tip.Hide();
        };
        PointerCaptureLost += (_, _) => { _dragging = false; Tip.Hide(); };
        // 넘기기만 하고 재생 화면은 열지 않게 (바를 누르면 재생 화면이 열린다)
        Tapped += (_, e) => e.Handled = true;
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            _canvas.Invalidate();
        }
    }

    private float PadX => KnobRadius + 8;
    private float TrackWidth => (float)Math.Max(0, ActualWidth - PadX * 2);

    private double Fraction(double x) => TrackWidth <= 0 ? 0 : Math.Clamp((x - PadX) / TrackWidth, 0, 1);

    private DispatcherQueueTimer? _idle;
    private double _idleProgress = -1;

    /// <summary>
    /// 멈춰서 파도가 다 가라앉으면 프레임 구독을 끊고 0.25초마다 위치만 다시 그린다
    /// (프레임 구독이 있으면 앱 전체가 1초에 60번 다시 그려져서). 재생되면 다시 매 프레임
    /// </summary>
    private void Idle()
    {
        StopRendering();
        if (_idle == null)
        {
            _idle = DispatcherQueue.CreateTimer();
            _idle.Interval = TimeSpan.FromMilliseconds(250);
            _idle.Tick += (_, _) =>
            {
                if (IsPlaying() || _dragging) { StartRendering(); return; }
                var p = Progress();
                if (Math.Abs(p - _idleProgress) < 1e-6) return;
                _idleProgress = p;
                _canvas.Invalidate();
            };
        }
        _idleProgress = Progress();
        _idle.Start();
    }

    private void StartRendering()
    {
        _idle?.Stop();
        if (_rendering) return;
        _rendering = true;
        _lastTicks = 0;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private long _lastDraw;

    private void OnFrame(object? sender, object e)
    {
        var now = Environment.TickCount64;
        // 60fps 로 제한 (고주사율 화면에서도)
        if (now - _lastDraw < 15) return;
        var dt = _lastTicks == 0 ? 0.016f : Math.Min((now - _lastTicks) / 1000f, 0.1f);
        _lastTicks = now;
        _lastDraw = now;

        var playing = IsPlaying();
        var target = playing ? 1f : 0f;
        _amp += (target - _amp) * (1 - MathF.Exp(-dt / ((playing ? Rise : Fall) / 3)));
        if (MathF.Abs(target - _amp) < 0.002f) _amp = target;
        _flow += (target - _flow) * (1 - MathF.Exp(-dt / 0.18f));

        var end = (float)(Progress() * TrackWidth);
        if (_amp > 0) Advance(dt * _flow, end);
        _canvas.Invalidate();
        // 멈춰서 완전히 가라앉았으면 쉰다 (위치는 느린 타이머로)
        if (_amp == 0 && !_dragging && !playing) Idle();
    }

    private float R(float a, float b) => a + (float)_rand.NextDouble() * (b - a);

    private Bump Spawn(Layer l, float length, bool atStart)
    {
        var w = R(l.W0, l.W1);
        return new Bump
        {
            X = atStart ? -w * R(0.5f, 1.5f) : R(0, Math.Max(1, length)),
            Speed = R(l.S0, l.S1),
            Width = w,
            Peak = R(l.P0, l.P1),
            Wobble = R(1.4f, 3.6f),
            Phase = R(0, MathF.PI * 2),
            Alpha = R(l.A0, l.A1),
        };
    }

    private void Advance(float dt, float length)
    {
        for (var i = 0; i < Layers.Length; i++)
        {
            var l = Layers[i];
            var list = _bumps[i];
            var want = Math.Max(2, (int)MathF.Round(l.Count * MathF.Max(0.5f, length / 420f)));
            while (list.Count < want) list.Add(Spawn(l, length, false));
            if (list.Count > want + 2) list.RemoveRange(want, list.Count - want);
            for (var j = 0; j < list.Count; j++)
            {
                var b = list[j];
                b.X += b.Speed * dt;
                b.Phase += b.Wobble * dt;
                if (b.X - b.Width > length) list[j] = Spawn(l, length, true);
            }
        }
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var width = TrackWidth;
        if (width <= 0) return;
        var baseY = (float)ActualHeight - KnobRadius - 8;
        var progress = _dragging ? _dragFraction : Progress();
        var end = (float)(Math.Clamp(progress, 0, 1) * width);
        var rise = Smooth(_amp);
        var px = PadX;

        // ① 재생한 구간 위로 솟는 언덕들 — 하나씩 따로, 진하기도 제각각
        if (rise > 0 && end > 6)
        {
            var fade = MathF.Min(EdgeFade, end / 2);
            for (var i = 0; i < Layers.Length; i++)
            {
                foreach (var b in _bumps[i])
                {
                    var from = MathF.Max(0, b.X - b.Width);
                    var to = MathF.Min(end, b.X + b.Width);
                    if (to <= from) continue;
                    var breathe = 0.4f + 0.6f * (0.5f + 0.5f * MathF.Sin(b.Phase));
                    var color = Layers[i].Grey ? Colors.White.WithAlpha(b.Alpha) : _color.WithAlpha(b.Alpha);
                    using var pb = new CanvasPathBuilder(ds);
                    pb.BeginFigure(px + from, baseY);
                    for (var x = from; x <= to + 0.01f; x += 2)
                    {
                        var d = MathF.Abs(x - b.X) / b.Width;
                        var shape = 1 - Smooth(MathF.Min(1, d));
                        var edge = Smooth(MathF.Min(1, MathF.Min(x / fade, (end - x) / fade)));
                        pb.AddLine(px + x, baseY - WaveHeight * rise * edge * b.Peak * breathe * shape);
                    }
                    pb.AddLine(px + to, baseY);
                    pb.EndFigure(CanvasFigureLoop.Closed);
                    using var geo = CanvasGeometry.CreatePath(pb);
                    ds.FillGeometry(geo, color);
                }
            }
        }

        // ② 직선 — 재생한 쪽은 앨범 색, 남은 쪽은 흐린 흰색
        var style = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
        var half = LineWidth / 2;
        if (end < width - half)
            ds.DrawLine(px + MathF.Max(end, half), baseY, px + width - half, baseY, Colors.White.WithAlpha(0.22), LineWidth, style);
        if (end > half)
            ds.DrawLine(px + half, baseY, px + end, baseY, _color, LineWidth, style);

        // ③ 손잡이 — 같은 색 빛(그림자 번짐) + 흰 테두리 + 앨범 색
        using (var cl = new CanvasCommandList(ds))
        {
            using (var cds = cl.CreateDrawingSession())
                cds.FillCircle(px + end, baseY, KnobRadius, Colors.White);
            var glow = new ShadowEffect { Source = cl, BlurAmount = 5, ShadowColor = _color };
            ds.DrawImage(glow);
        }
        ds.FillCircle(px + end, baseY, KnobRadius, Colors.White);
        ds.FillCircle(px + end, baseY, KnobRadius - KnobRing, _color);
    }
}
