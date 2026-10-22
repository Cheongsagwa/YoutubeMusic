using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace OneMusic.UI;

/// <summary>
/// 재생 바 아이콘 — pear One UI 테마의 bar-icons.ts 를 그대로 옮긴 '모양이 바뀌는' 아이콘들.
///  · 재생 ↔ 일시정지 : 막대 두 개가 합쳐지며 삼각형이 되고, 삼각형이 갈라지며 막대가 된다 (380ms)
///  · 이전 / 다음     : 누르면 새 삼각형이 옆에서 커지며 기존 것을 대신한다 (420ms)
///  · 셔플            : 누르면 두 화살표 머리가 선의 처음부터 끝까지 다시 달려간다 (700ms, 둘째는 90ms 늦게)
///  · 반복            : 모드가 바뀌면 화살표가 사각형 둘레를 한 바퀴 돈다 (760ms), 한 곡 반복이면 가운데 1
///  · 볼륨 ↔ 음소거   : 스피커 옆 물결이 지워지고 X 가 그려진다 (반대도 같은 방식)
/// 모두 24×24 좌표에서 그리고 Viewbox 로 키운다.
/// </summary>
internal static class Tween
{
    private static readonly Dictionary<object, Action> Running = new();

    /// <summary>bar-icons.ts 의 ease: 3차 ease-in-out</summary>
    public static double InOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>넘치지 않는 iOS 스프링 (Anim.SmoothCurve 와 같은 식) — 아이콘 전환 · 접기 펴기</summary>
    public static double Out(double t) => Anim.SpringAt(1, t);

    /// <summary>CSS cubic-bezier 를 x 로 풀어서 y 를 돌려준다</summary>
    public static double Bezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double t = x;
        for (var i = 0; i < 8; i++)
        {
            var cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t - x;
            var dx = 3 * x1 * (1 - t) * (1 - t) + 6 * (x2 - x1) * t * (1 - t) + 3 * (1 - x2) * t * t;
            if (Math.Abs(dx) < 1e-6) break;
            t = Math.Clamp(t - cx / dx, 0, 1);
        }
        return 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
    }

    /// <summary>0→1 진행을 ms 동안 흘린다 (같은 key 에 다시 걸면 앞의 것은 멈춘다)</summary>
    public static void Run(object key, int ms, Func<double, double> ease, Action<double> frame, int delayMs = 0)
    {
        Stop(key);
        ms = Anim.D(ms);
        if (!Anim.Enabled) delayMs = 0;
        var start = Environment.TickCount64 + delayMs;
        EventHandler<object>? h = null;
        h = (_, _) =>
        {
            var t = Math.Clamp((Environment.TickCount64 - start) / (double)ms, 0, 1);
            if (Environment.TickCount64 < start) t = 0;
            frame(ease(t));
            if (t >= 1) Stop(key);
        };
        CompositionTarget.Rendering += h;
        Running[key] = () => CompositionTarget.Rendering -= h;
    }

    public static void Stop(object key)
    {
        if (Running.Remove(key, out var stop)) stop();
    }
}

/// <summary>24×24 캔버스를 size 크기로 보여 주는 아이콘 바탕</summary>
public abstract class BarIcon : Grid
{
    /// <summary>
    /// 재생 바 선 아이콘 굵기 — 정수 2. 곧은 변을 정수 좌표에 두면 정확히 두 픽셀에 맞아 번지지 않는다
    /// (1.7 굵기는 어디에 놓아도 두 픽셀에 반씩 걸쳐 흐릿했다)
    /// </summary>
    public const double StrokeW = 2;
    protected readonly Canvas C = new() { Width = 24, Height = 24 };
    protected SolidColorBrush Brush;

    protected BarIcon(double size, Color color)
    {
        Width = Height = size;
        Brush = Theme.B(color);
        Children.Add(new Viewbox { Child = C });
        IsHitTestVisible = false;
    }

    protected Path Line(string data, double width = 1.7) => Add(new Path
    {
        Data = Theme.Geo(data),
        Stroke = Brush,
        StrokeThickness = width,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeDashCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    });

    protected Path Fill(string data) => Add(new Path { Data = Theme.Geo(data), Fill = Brush });

    protected T Add<T>(T el) where T : UIElement
    {
        C.Children.Add(el);
        return el;
    }

    /// <summary>화살표 머리 — (0,0) 이 끝점, +x 방향 (pear HEAD)</summary>
    protected Path Head()
    {
        var p = Line("M-2.6 -2.7 0 0-2.6 2.7", StrokeW);
        p.RenderTransform = new CompositeTransform();
        return p;
    }

    protected static void Place(Path head, double x, double y, double deg, double scale = 1, double opacity = 1)
    {
        var t = (CompositeTransform)head.RenderTransform;
        t.TranslateX = x;
        t.TranslateY = y;
        t.Rotation = deg;
        t.ScaleX = t.ScaleY = scale;
        head.Opacity = opacity;
    }
}

/// <summary>재생 ↔ 일시정지 — 두 도형의 꼭짓점 수(4)를 같게 맞춰 모양이 이어진다 (흰 원 안 검은 아이콘)</summary>
public sealed class PlayPauseIcon : BarIcon
{
    private static readonly Point[][] Pause =
    [
        [new(6.5, 5), new(10.3, 5), new(10.3, 19), new(6.5, 19)],
        [new(13.7, 5), new(17.5, 5), new(17.5, 19), new(13.7, 19)],
    ];
    private static readonly Point[][] PlayPts =
    [
        [new(7.5, 4.8), new(13, 8.3), new(13, 15.7), new(7.5, 19.2)],
        [new(13, 8.3), new(19.2, 12), new(19.2, 12), new(13, 15.7)],
    ];
    private readonly PathFigure[] _figs = new PathFigure[2];
    private Point[][] _now = PlayPts.Select(a => a.ToArray()).ToArray();
    private bool _playing;

    public PlayPauseIcon(double size, Color color) : base(size, color)
    {
        for (var i = 0; i < 2; i++)
        {
            var fig = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = _now[i][0] };
            for (var k = 1; k < 4; k++) fig.Segments.Add(new LineSegment { Point = _now[i][k] });
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            _figs[i] = fig;
            Add(new Path { Data = geo, Fill = Brush, Stroke = Brush, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round });
        }
    }

    /// <summary>playing 이면 일시정지 모양(막대 둘), 아니면 재생 삼각형</summary>
    public void Set(bool playing, bool animate = true)
    {
        if (playing == _playing && animate) return;
        _playing = playing;
        var from = _now.Select(a => a.ToArray()).ToArray();
        var to = playing ? Pause : PlayPts;
        if (!animate)
        {
            Apply(to);
            return;
        }
        Tween.Run(this, 380, Tween.Out, k =>
        {
            var cur = new Point[2][];
            for (var i = 0; i < 2; i++)
            {
                cur[i] = new Point[4];
                for (var j = 0; j < 4; j++)
                    cur[i][j] = new Point(from[i][j].X + (to[i][j].X - from[i][j].X) * k, from[i][j].Y + (to[i][j].Y - from[i][j].Y) * k);
            }
            Apply(cur);
        });
    }

    private void Apply(Point[][] pts)
    {
        _now = pts.Select(a => a.ToArray()).ToArray();
        for (var i = 0; i < 2; i++)
        {
            _figs[i].StartPoint = pts[i][0];
            for (var k = 1; k < 4; k++) ((LineSegment)_figs[i].Segments[k - 1]).Point = pts[i][k];
        }
    }
}

/// <summary>이전 · 다음 — 막대 + 삼각형. 누르면 새 삼각형이 옆에서 0.2 → 1 로 커지며 대신한다</summary>
public sealed class SkipIcon : BarIcon
{
    private readonly Canvas _tri = new() { Width = 24, Height = 24 };
    private readonly CompositeTransform _t = new();
    private readonly bool _next;

    public SkipIcon(bool next, double size, Color color) : base(size, color)
    {
        _next = next;
        var bar = new Rectangle { Width = 2.2, Height = 13.6, RadiusX = 1.1, RadiusY = 1.1, Fill = Brush };
        Canvas.SetLeft(bar, next ? 17 : 4.8);
        Canvas.SetTop(bar, 5.2);
        Add(bar);
        _tri.Children.Add(new Path
        {
            Data = Theme.Geo(next
                ? "M4.8 6.7v10.6c0 .9 1 1.4 1.7.9l7.6-5.3c.6-.4.6-1.4 0-1.8L6.5 5.8c-.7-.5-1.7 0-1.7.9z"
                : "M19.2 6.7v10.6c0 .9-1 1.4-1.7.9l-7.6-5.3c-.6-.4-.6-1.4 0-1.8l7.6-5.3c.7-.5 1.7 0 1.7.9z"),
            Fill = Brush,
        });
        // 다음은 삼각형 왼쪽 가운데, 이전은 오른쪽 가운데를 기준으로 커진다 (transform-origin)
        _t.CenterX = next ? 4.8 : 19.2;
        _t.CenterY = 12;
        _tri.RenderTransform = _t;
        Add(_tri);
    }

    public void Swap()
    {
        var dx = _next ? -7 : 7;
        Tween.Run(this, 420, Tween.Out, k =>
        {
            _t.TranslateX = dx * (1 - k);
            _t.ScaleX = _t.ScaleY = 0.2 + 0.8 * k;
        });
        // 투명도는 60% 지점에서 다 나타난다 (keyframes 60% opacity 1)
        Tween.Run(_tri, 420, t => Math.Min(1, t / 0.6), k => _tri.Opacity = k);
    }
}

/// <summary>선 위의 점 · 방향을 길이로 찾기 위해 미리 잘게 나눠 둔 경로</summary>
internal sealed class IconTrack
{
    private readonly List<(double X, double Y, double L)> _pts = new();
    public double Length => _pts[^1].L;

    public IconTrack(params Func<double, Point>[] parts)
    {
        foreach (var f in parts)
            for (var i = 0; i <= 60; i++)
            {
                if (_pts.Count > 0 && i == 0) continue;
                var p = f(i / 60.0);
                var l = _pts.Count == 0 ? 0 : _pts[^1].L + Math.Sqrt(Math.Pow(p.X - _pts[^1].X, 2) + Math.Pow(p.Y - _pts[^1].Y, 2));
                _pts.Add((p.X, p.Y, l));
            }
    }

    public static Func<double, Point> L(double x0, double y0, double x1, double y1) => t => new Point(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);

    public static Func<double, Point> C(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3) => t =>
    {
        var u = 1 - t;
        return new Point(u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
                         u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3);
    };

    /// <summary>원호 (중심 · 반지름 · 시작각 → 끝각, 도)</summary>
    public static Func<double, Point> A(double cx, double cy, double r, double a0, double a1) => t =>
    {
        var a = (a0 + (a1 - a0) * t) * Math.PI / 180;
        return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    };

    public (double X, double Y) At(double d)
    {
        d = Math.Clamp(d, 0, Length);
        var lo = 0;
        var hi = _pts.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_pts[mid].L < d) lo = mid; else hi = mid;
        }
        var a = _pts[lo];
        var b = _pts[hi];
        var k = b.L - a.L < 1e-9 ? 0 : (d - a.L) / (b.L - a.L);
        return (a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);
    }

    /// <summary>d 지점의 점과 진행 방향(도) — pear placeHead 와 같게 앞뒤 0.3 으로 방향을 잰다</summary>
    public (double X, double Y, double Deg) Pose(double d, bool wrap = false)
    {
        var len = Length;
        var at = wrap ? ((d % len) + len) % len : Math.Clamp(d, 0, len);
        var p = At(at);
        var b = At(Math.Min(len, at + 0.3));
        var c = At(Math.Max(0, at - 0.3));
        return (p.X, p.Y, Math.Atan2(b.Y - c.Y, b.X - c.X) * 180 / Math.PI);
    }
}

/// <summary>셔플 — 선은 그대로, 누르면 화살표 머리가 선의 시작에서 피어나 끝까지 달려간다</summary>
public sealed class ShuffleIcon : BarIcon
{
    // 가로 4 ~ 20 · 세로 7 ~ 17 (곧은 변은 정수 y) — 다른 아이콘과 같은 틀(약 18×14, 가운데 12)
    private readonly IconTrack _ta = new(IconTrack.L(4, 17, 6.5, 17), IconTrack.C(6.5, 17, 11, 17, 13, 7, 17.5, 7), IconTrack.L(17.5, 7, 20, 7));
    private readonly IconTrack _tb = new(IconTrack.L(4, 7, 6.5, 7), IconTrack.C(6.5, 7, 11, 7, 13, 17, 17.5, 17), IconTrack.L(17.5, 17, 20, 17));
    private const double HeadScale = 0.85;
    private readonly Path _ha, _hb, _ha0, _hb0;
    private double _ka = 1, _kb = 1;
    public event Action<bool>? ActiveChanged;

    public ShuffleIcon(double size, Color color) : base(size, color)
    {
        Line("M4 17H6.5c4.5 0 6.5-10 11-10H20", StrokeW);
        Line("M4 7H6.5c4.5 0 6.5 10 11 10H20", StrokeW);
        _ha0 = Head();
        _hb0 = Head();
        _ha = Head();
        _hb = Head();
        Apply();
    }

    private void Apply()
    {
        foreach (var (track, head, old, k) in new[] { (_ta, _ha, _ha0, _ka), (_tb, _hb, _hb0, _kb) })
        {
            var len = track.Length;
            var grow = Math.Min(1, k / 0.35);
            var fade = 1 - Math.Min(1, k / 0.3);
            var p = track.Pose(len * k);
            Place(head, p.X, p.Y, p.Deg, (0.35 + 0.65 * grow) * HeadScale, grow);
            var e = track.Pose(len);
            Place(old, e.X, e.Y, e.Deg, (0.4 + 0.6 * fade) * HeadScale, k >= 1 ? 0 : fade);
        }
    }

    public void Run()
    {
        _ka = _kb = 0;
        Apply();
        // 셔플은 켜고 끄는 모드가 아니라 '한 번 섞기' 라서 섞는 동안만 밝게
        ActiveChanged?.Invoke(true);
        Tween.Run(_ha, 700, Tween.InOut, k => { _ka = k; Apply(); });
        Tween.Run(_hb, 700, Tween.InOut, k => { _kb = k; Apply(); }, 90);
        Tween.Run(this, 900, t => t, k => { if (k >= 1) ActiveChanged?.Invoke(false); });
    }
}

/// <summary>반복 — 둥근 사각형 둘레를 화살표가 돈다. 머리 앞에 작은 틈. 한 곡 반복이면 가운데 1</summary>
public sealed class RepeatIcon : BarIcon
{
    // 화살촉 두 개 — 위 변(→)과 반대편 아래 변(←), 둘레의 절반씩 떨어져 위아래가 대칭이라 아이콘이 가운데에 선다.
    // Home: 위 화살촉이 쉬는 자리 (위 변 x 14)
    private const double Gap = 3, Home = 7, Th = StrokeW;
    // 둥근 사각형 가로 4 ~ 20 · 세로 6 ~ 18 (정수 좌표 — 2px 굵기가 픽셀에 딱 맞는다), 반지름 3. (7,6) 에서 오른쪽으로 시계 방향
    private readonly IconTrack _loop = new(
        IconTrack.L(7, 6, 17, 6), IconTrack.A(17, 9, 3, -90, 0),
        IconTrack.L(20, 9, 20, 15), IconTrack.A(17, 15, 3, 0, 90),
        IconTrack.L(17, 18, 7, 18), IconTrack.A(7, 15, 3, 90, 180),
        IconTrack.L(4, 15, 4, 9), IconTrack.A(7, 9, 3, 180, 270));
    private readonly Path _path, _head, _head2, _one;
    private readonly CompositeTransform _oneT = new() { CenterX = 11.8, CenterY = 12, ScaleX = 0.4, ScaleY = 0.4 };
    private bool _isOne;

    public RepeatIcon(double size, Color color) : base(size, color)
    {
        _path = Line("M7 6h10a3 3 0 0 1 3 3v6a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V9a3 3 0 0 1 3-3z", Th);
        _head = Head();
        _head2 = Head();
        _one = Line("M11 10.5l1.5-1v5", Th);
        _one.Opacity = 0;
        _one.RenderTransform = _oneT;
        SetPos(Home);
    }

    private void SetPos(double p)
    {
        var len = _loop.Length;
        // 대시 단위는 선 굵기의 배수 — 둘레 절반마다 틈 하나 (화살촉 앞)
        _path.StrokeDashArray = new DoubleCollection { (len / 2 - Gap) / Th, Gap / Th };
        _path.StrokeDashOffset = -(p + Gap) / Th;
        var h = _loop.Pose(p, wrap: true);
        Place(_head, h.X, h.Y, h.Deg, 0.85);
        var h2 = _loop.Pose(p + len / 2, wrap: true);
        Place(_head2, h2.X, h2.Y, h2.Deg, 0.85);
    }

    public void Spin()
    {
        var len = _loop.Length;
        Tween.Run(this, 760, Tween.InOut, k => SetPos(Home + len * k));
    }

    /// <summary>한 곡 반복이면 가운데 '1' 이 튕기며 나타난다 (opacity 440 smooth · scale 440 spring)</summary>
    public void SetOne(bool one, bool animate = true)
    {
        if (one == _isOne && animate) return;
        _isOne = one;
        if (!animate)
        {
            _one.Opacity = one ? 1 : 0;
            _oneT.ScaleX = _oneT.ScaleY = one ? 1 : 0.4;
            return;
        }
        var from = _one.Opacity;
        var fromS = _oneT.ScaleX;
        Tween.Run(_one, Anim.Medium, t => Curve(Anim.SmoothCurve, t), k => _one.Opacity = from + ((one ? 1 : 0) - from) * k);
        Tween.Run(_oneT, Anim.Medium, t => Curve(Anim.Spring, t), k => _oneT.ScaleX = _oneT.ScaleY = fromS + ((one ? 1 : 0.4) - fromS) * k);
    }

    /// <summary>pear linear() 곡선 점들 사이를 이어 읽는다</summary>
    internal static double Curve(float[] pts, double t)
    {
        var x = Math.Clamp(t, 0, 1) * (pts.Length - 1);
        var i = Math.Min(pts.Length - 2, (int)x);
        return pts[i] + (pts[i + 1] - pts[i]) * (x - i);
    }
}

/// <summary>볼륨 ↔ 음소거 — 물결이 지워지고 X 가 그려진다 (320ms, 순서대로 조금씩 늦게)</summary>
public sealed class VolumeIcon : BarIcon
{
    private readonly (Path P, double Len)[] _waves, _x;
    private bool _muted;

    public VolumeIcon(double size, Color color) : base(size, color)
    {
        // (소수 이동은 획을 흐리게 해서 빼고, 좌표 자체를 가운데(12)에 맞췄다)
        var g = new Canvas();
        C.Children.Add(g);
        Path L(string d, double len)
        {
            var p = new Path
            {
                Data = Theme.Geo(d), Stroke = Brush, StrokeThickness = StrokeW,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { len / StrokeW, len / StrokeW + 2 },
            };
            g.Children.Add(p);
            return p;
        }
        // 스피커 외곽선 — 곧은 변(x 4 · 12, y 9 · 15)은 정수 좌표라 2px 굵기가 픽셀에 딱 맞는다. 세로 5 ~ 19 (다른 아이콘과 같은 높이 16)
        g.Children.Add(new Path
        {
            // 나팔 끝(위 · 아래 예각 꼭짓점)은 둥글게 — 뾰족하면 2px 선이 꼭짓점에서 가시처럼 튀어나왔다
            Data = Theme.Geo("M4 9h3.5l3.4-3c.45-.4 1.1-.08 1.1.5v11c0 .58-.65.9-1.1.5L7.5 15H4z"),
            Stroke = Brush, StrokeThickness = StrokeW, StrokeLineJoin = PenLineJoin.Round,
        });
        // 물결: 스피커 쪽 (12.5,12) 을 중심으로 퍼지는 반지름 4 · 7.5 의 90° 원호 (길이 6.28 · 11.78), X 의 획은 5√2
        _waves = [(L("M15.33 9.17a4 4 0 0 1 0 5.66", 6.28), 6.28), (L("M17.8 6.7a7.5 7.5 0 0 1 0 10.6", 11.78), 11.78)];
        _x = [(L("M15.5 9.5l5 5", 7.07), 7.07), (L("M20.5 9.5l-5 5", 7.07), 7.07)];
        Set(false, false);
    }

    /// <summary>보이는 정도(0 = 지워짐, 1 = 다 그려짐)를 대시 위치로</summary>
    private static void Show((Path P, double Len) s, double shown)
    {
        s.P.StrokeDashOffset = (1 - shown) * s.Len / StrokeW;
        // 다 지운 획은 아예 숨긴다 — 길이 0 인 대시도 둥근 끝 모양이 점으로 남아, 숨은 X 끝이 물결 옆에 삐죽 · 음소거 X 위에 물결 점이 보였다
        s.P.Opacity = shown <= 0.02 ? 0 : 1;
    }

    public void Set(bool muted, bool animate = true)
    {
        if (muted == _muted && animate) return;
        _muted = muted;
        void Go((Path P, double Len) s, double to, int delay)
        {
            if (!animate) { Show(s, to); return; }
            var from = 1 - s.P.StrokeDashOffset * StrokeW / s.Len;
            Tween.Run(s.P, 320, Tween.Out, k => Show(s, from + (to - from) * k), delay);
        }
        if (muted)
        {
            Go(_waves[0], 0, 0);
            Go(_waves[1], 0, 40);
            Go(_x[0], 1, 160);
            Go(_x[1], 1, 240);
        }
        else
        {
            Go(_x[0], 0, 0);
            Go(_x[1], 0, 0);
            Go(_waves[0], 1, 140);
            Go(_waves[1], 1, 200);
        }
    }
}

/// <summary>미니 플레이어 — 둥근 화면(외곽선 2px, 정수 좌표) 오른쪽 아래에 작은 채운 화면. 다른 재생 바 아이콘과 같은 틀(18×14)</summary>
public sealed class PipIcon : BarIcon
{
    public PipIcon(double size, Color color) : base(size, color)
    {
        Line("M6 6h12a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z", StrokeW);
        Fill("M12 12h5v3.5h-5z");
    }
}
