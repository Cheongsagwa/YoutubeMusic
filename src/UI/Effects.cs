using Microsoft.Graphics.Canvas.Text;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 설명 말풍선 (pear tooltip.ts) — 윈도우 기본 툴팁 대신 테두리 없는 둥근 유리.
/// 450ms 머무르면 요소 아래(공간이 없으면 위)에 뜨고, 누르거나 휠을 굴리면 사라진다.
/// 나타날 때 4px 비켜 있던 자리에서 0.96 → 1 로 (opacity 220 smooth · transform 440 ease)
/// </summary>
public static class Tip
{
    private const int Delay = 450;
    private static Grid? _layer;
    private static Border? _box;
    private static TextBlock? _text;
    private static FrameworkElement? _current;
    private static int _gen;

    public static void Attach(Grid root)
    {
        _text = Theme.Text("", 13, FontWeights.SemiBold, Theme.OnSurface);
        _box = new Border
        {
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(12),
            Background = Theme.Glass(0.78, Color.FromArgb(255, 46, 46, 48)),
            Child = _text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, 20),
        };
        Anim.V(_box).Opacity = 0;
        _layer = new Grid { IsHitTestVisible = false };
        _layer.Children.Add(_box);
        root.Children.Add(_layer);
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Hide()), true);
        root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => Hide()), true);
        // 마우스가 창 밖으로 빠르게 나가면 요소의 PointerExited 가 안 올 때가 있어서 창 기준으로도 닫는다
        root.PointerExited += (_, _) => Hide();
        root.PointerCanceled += (_, _) => Hide();
    }

    /// <summary>ToolTipService.SetToolTip 대신</summary>
    public static void Set(FrameworkElement el, string text)
    {
        el.PointerEntered += (_, _) => Begin(el, text);
        el.PointerExited += (_, _) => { if (_current == el) Hide(); };
        el.Unloaded += (_, _) => { if (_current == el) Hide(); };
    }

    private static async void Begin(FrameworkElement el, string text)
    {
        Hide();
        _current = el;
        var g = ++_gen;
        await Task.Delay(Delay);
        if (g != _gen || _current != el || el.XamlRoot == null || _box == null || _text == null) return;
        Place(el, text);
    }

    private static void Place(FrameworkElement el, string text)
    {
        if (_box == null || _text == null || el.XamlRoot == null) return;
        _text.Text = text;
        // 상자 대신 글자만 재서 여백을 더한다 — 재생 화면이 열려 레이아웃이 다시 잡히는 중에는
        // 상자의 DesiredSize 가 창 높이만큼(939px) 엉뚱하게 나와서 툴팁이 창 맨 위로 튀었다
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _text.DesiredSize.Width + _box.Padding.Left + _box.Padding.Right;
        var h = _text.DesiredSize.Height + _box.Padding.Top + _box.Padding.Bottom;
        var win = el.XamlRoot.Size;
        var r = el.TransformToVisual(null).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        const double gap = 8;
        var top = r.Bottom + gap;
        var above = top + h > win.Height - 8;
        if (above)
        {
            // 플로팅 바 안의 버튼은 바 위쪽(파도 재생 바 위)으로 — 파도를 가리지 않게
            var anchorTop = r.Top;
            for (var p = VisualTreeHelper.GetParent(el); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is FloatingBar bar)
                {
                    anchorTop = bar.TransformToVisual(null).TransformPoint(new Point(0, 0)).Y;
                    break;
                }
            top = anchorTop - h - gap;
        }
        var left = Math.Clamp(r.Left + r.Width / 2 - w / 2, 8, Math.Max(8, win.Width - w - 8));
        _box.Margin = new Thickness(Math.Round(left), Math.Round(top), 0, 0);
        var v = Anim.V(_box);
        v.CenterPoint = new Vector3((float)w / 2, (float)h / 2, 0);
        v.Scale = new Vector3(0.96f, 0.96f, 1);
        Anim.SetTranslation(_box, new Vector3(0, above ? 4 : -4, 0));
        Anim.Opacity(_box, 1, Anim.Fast);
        Anim.Translate(_box, Vector3.Zero);
        Anim.Scale(_box, 1f);
    }

    /// <summary>지금 바로 그 자리 위에 띄운다 (재생 바 위 시간 미리보기) — 창 기준 x, 위쪽 y</summary>
    public static void ShowNow(string text, double x, double y)
    {
        if (_box == null || _text == null) return;
        _gen++;
        _current = null;
        _text.Text = text;
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _text.DesiredSize.Width + _box.Padding.Left + _box.Padding.Right;
        var h = _text.DesiredSize.Height + _box.Padding.Top + _box.Padding.Bottom;
        var win = _box.XamlRoot?.Size ?? new Size(4000, 4000);
        _box.Margin = new Thickness(Math.Clamp(x - w / 2, 8, Math.Max(8, win.Width - w - 8)), Math.Max(8, y - h - 10), 0, 0);
        if (Anim.V(_box).Opacity < 0.99f)
        {
            Anim.V(_box).Scale = Vector3.One;
            Anim.SetTranslation(_box, Vector3.Zero);
            Anim.Opacity(_box, 1, Anim.Fast);
        }
    }

    /// <summary>개발용: 툴팁을 즉시 띄우고 위치 진단</summary>
    public static async Task<string> Test(FrameworkElement el, string text)
    {
        if (_box == null || _layer == null) return "no box";
        _gen++;
        Place(el, text);
        await Task.Delay(400);
        var r = el.TransformToVisual(null).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        var b = _box.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var l = _layer.TransformToVisual(null).TransformPoint(new Point(0, 0));
        return $"el={r} box margin={_box.Margin} actual=({b.X:0},{b.Y:0}) size={_box.ActualWidth:0}x{_box.ActualHeight:0} layer=({l.X:0},{l.Y:0}) {_layer.ActualWidth:0}x{_layer.ActualHeight:0} win={el.XamlRoot.Size} trans={_box.Translation}";
    }

    public static void Hide()
    {
        _gen++;
        _current = null;
        if (_box != null && Anim.V(_box).Opacity > 0) Anim.Opacity(_box, 0, Anim.Fast);
    }
}

/// <summary>
/// 패널 안 목록 · 가로 줄의 끝 흐림 (pear scroll-fade.ts — 위 44 · 아래 56, 가로 줄은 양옆 32 / 셸프 56).
/// 유리 패널 위라서 덮개를 칠할 수 없어, 끝 쪽에 걸친 줄 · 카드의 투명도를 낮춘다. 더 있는 쪽만 흐린다
/// </summary>
/// <summary>
/// 세로 목록 위 · 아래 끝을 '픽셀 단위'로 흐린다 — 줄마다 투명도를 먹이면 큰 줄(긴 댓글)은 통째로 흐려져서,
/// 목록 전체를 한 장으로 비춰(VisualSurface) 위 · 아래 띠에만 그라데이션 마스크를 씌워 그린다.
/// 원래 목록은 GPU 투명도만 0 으로 숨겨서 클릭 · 스크롤은 그대로 원래 목록이 받는다. 가로 줄(셸프 카드)은 왼쪽 · 오른쪽 끝
/// </summary>
public static class EdgeMask
{
    /// <param name="start">위(가로면 왼쪽) 흐림 띠 — 0 이면 흐리지 않는다</param>
    /// <param name="end">아래(가로면 오른쪽) 흐림 띠</param>
    /// <param name="horizontal">가로 줄 — 왼쪽 · 오른쪽 끝을 흐린다</param>
    public static void Attach(ScrollViewer sv, double start = 44, double end = 56, bool horizontal = false)
    {
        Grid? host = null;
        SpriteVisual? sprite = null;
        CompositionVisualSurface? surface = null;
        CompositionLinearGradientBrush? mask = null;
        CompositionColorGradientStop? t0 = null, t1 = null, b0 = null, b1 = null;
        var showTop = false;
        var showBottom = true;

        bool? shownTop = null, shownBottom = null;

        // 끝 색: 흐림이면 투명, 아니면 불투명. 바뀔 때 뚝 끊기지 않게 300ms 동안 서서히 (더 내릴 곳이 없어지면 흐림이 스르르 사라진다)
        void Edge(CompositionColorGradientStop stop, bool fade, ref bool? shown)
        {
            if (shown == fade) return;
            var to = fade ? Color.FromArgb(0, 0, 0, 0) : Colors.Black;
            if (shown == null || !Anim.Enabled) stop.Color = to;
            else
            {
                var a = stop.Compositor.CreateColorKeyFrameAnimation();
                a.InsertKeyFrame(1, to, stop.Compositor.CreateLinearEasingFunction());
                a.Duration = TimeSpan.FromMilliseconds(300);
                stop.StartAnimation("Color", a);
            }
            shown = fade;
        }

        void Stops()
        {
            if (mask == null || t0 == null || t1 == null || b0 == null || b1 == null) return;
            var h = (float)Math.Max(1, horizontal ? sv.ActualWidth : sv.ActualHeight);
            // 띠 자리는 고정, 끝 색(투명 ↔ 불투명)만 바꾼다
            var ft = start > 0 ? (float)Math.Min(start / h, 0.45) : 0f;
            var fb = end > 0 ? (float)Math.Min(end / h, 0.45) : 0f;
            t1.Offset = Math.Max(0.0001f, ft);
            b0.Offset = Math.Min(0.9999f, 1 - fb);
            Edge(t0, showTop && ft > 0, ref shownTop);
            Edge(b1, showBottom && fb > 0, ref shownBottom);
        }

        void Sync()
        {
            if (host == null || sprite == null || surface == null) return;
            var size = new Vector2((float)sv.ActualWidth, (float)sv.ActualHeight);
            sprite.Size = size;
            surface.SourceSize = size;
            var off = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
            var scrollable = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
            showTop = off > 1;
            showBottom = off < scrollable - 1;
            Stops();
        }

        void Init()
        {
            if (host != null || sv.Parent is not Panel parent) return;
            host = new Grid { IsHitTestVisible = false };
            host.HorizontalAlignment = sv.HorizontalAlignment;
            host.VerticalAlignment = sv.VerticalAlignment;
            host.Visibility = sv.Visibility;
            var idx = parent.Children.IndexOf(sv);
            if (parent is Grid)
            {
                // 격자 안: 같은 칸에 형제로 겹쳐 둔다
                Grid.SetRow(host, Grid.GetRow(sv));
                Grid.SetColumn(host, Grid.GetColumn(sv));
                Grid.SetRowSpan(host, Grid.GetRowSpan(sv));
                Grid.SetColumnSpan(host, Grid.GetColumnSpan(sv));
                host.Margin = sv.Margin;
                parent.Children.Insert(idx + 1, host);
            }
            else
            {
                // StackPanel 등: 형제를 끼우면 자리를 차지하니, 목록을 Grid 로 한 겹 감싸서 그 안에 겹친다
                var wrap = new Grid
                {
                    Margin = sv.Margin,
                    HorizontalAlignment = sv.HorizontalAlignment,
                    VerticalAlignment = sv.VerticalAlignment,
                };
                sv.Margin = new Thickness(0);
                parent.Children.RemoveAt(idx);
                wrap.Children.Add(sv);
                wrap.Children.Add(host);
                parent.Children.Insert(idx, wrap);
            }
            sv.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => host.Visibility = sv.Visibility);

            var svVisual = Anim.V(sv);
            var c = svVisual.Compositor;
            surface = c.CreateVisualSurface();
            surface.SourceVisual = svVisual;
            var src = c.CreateSurfaceBrush(surface);
            src.Stretch = CompositionStretch.None;
            src.HorizontalAlignmentRatio = 0;
            src.VerticalAlignmentRatio = 0;
            mask = c.CreateLinearGradientBrush();
            mask.StartPoint = new Vector2(0, 0);
            mask.EndPoint = horizontal ? new Vector2(1, 0) : new Vector2(0, 1);
            t0 = c.CreateColorGradientStop(0, Colors.Black);
            t1 = c.CreateColorGradientStop(0.0001f, Colors.Black);
            b0 = c.CreateColorGradientStop(0.9999f, Colors.Black);
            b1 = c.CreateColorGradientStop(1, Colors.Black);
            mask.ColorStops.Add(t0);
            mask.ColorStops.Add(t1);
            mask.ColorStops.Add(b0);
            mask.ColorStops.Add(b1);
            var masked = c.CreateMaskBrush();
            masked.Source = src;
            masked.Mask = mask;
            sprite = c.CreateSpriteVisual();
            sprite.Brush = masked;
            ElementCompositionPreview.SetElementChildVisual(host, sprite);
            // 원래 목록은 화면에서만 숨긴다 (XAML 쪽 Opacity 가 아니라서 클릭 · 휠은 그대로 받는다)
            svVisual.Opacity = 0;
            Sync();
        }

        sv.Loaded += (_, _) => { Init(); Sync(); };
        sv.SizeChanged += (_, _) => Sync();
        sv.ViewChanged += (_, _) => Sync();
        sv.LayoutUpdated += (_, _) => Sync();
    }
}

public static class ListFade
{
    public static void Attach(ScrollViewer sv, bool horizontal = false, double start = 44, double end = 56)
    {
        // 지난번에 흐리게 한 줄들 — 끝에서 벗어나면 다시 1 로
        var faded = new HashSet<FrameworkElement>();
        (double Lo, double Hi) Span(FrameworkElement item)
        {
            var r = item.TransformToVisual(sv).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            return horizontal ? (r.Left, r.Right) : (r.Top, r.Bottom);
        }
        void Update()
        {
            if (sv.Content is not FrameworkElement content) return;
            var size = horizontal ? sv.ViewportWidth : sv.ViewportHeight;
            var offset = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
            var scrollable = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
            // 띠는 남은 거리만큼만 — 끝에 다가가면 흐림이 줄어들며 사라진다 (끝에서 뚝 끊기지 않게)
            var fadeStart = Math.Min(start, Math.Max(0, offset));
            var fadeEnd = Math.Min(end, Math.Max(0, scrollable - offset));
            if (fadeStart < 1) fadeStart = 0;
            if (fadeEnd < 1) fadeEnd = 0;
            var items = Items(content).ToList();
            if (items.Count == 0) return;
            var touched = new HashSet<FrameworkElement>();
            try
            {
                // 줄은 위에서 아래로 놓여 있으니 화면에 걸친 첫 줄을 반으로 나눠 찾고, 화면 끝까지만 본다 (수백 줄이어도 몇십 번)
                int lo = 0, hi = items.Count - 1;
                while (lo < hi)
                {
                    var midIdx = (lo + hi) / 2;
                    if (Span(items[midIdx]).Hi <= 0) lo = midIdx + 1;
                    else hi = midIdx;
                }
                for (var i = Math.Max(0, lo - 1); i < items.Count; i++)
                {
                    var item = items[i];
                    var (a0, a1) = Span(item);
                    if (a0 > size) break;
                    var mid = (a0 + a1) / 2;
                    double a = 1;
                    if (fadeStart > 0) a = Math.Min(a, Math.Clamp(mid / fadeStart, 0, 1));
                    if (fadeEnd > 0) a = Math.Min(a, Math.Clamp((size - mid) / fadeEnd, 0, 1));
                    // 0.15 밑으로는 안 내려서 끝에 걸친 줄도 무엇인지는 보이게
                    var op = a >= 1 ? 1 : 0.15 + 0.85 * a;
                    if (Math.Abs(item.Opacity - op) > 0.01) item.Opacity = op;
                    if (op < 1) touched.Add(item);
                }
            }
            catch { /* 아직 배치 전 */ }
            foreach (var old in faded)
                if (!touched.Contains(old) && old.Opacity < 1) old.Opacity = 1;
            faded.Clear();
            faded.UnionWith(touched);
        }
        sv.ViewChanged += (_, _) => Update();
        sv.SizeChanged += (_, _) => Update();
        sv.Loaded += (_, _) =>
        {
            if (sv.Content is FrameworkElement c) c.SizeChanged += (_, _) => Update();
            Update();
        };
    }

    /// <summary>세로 · 가로 StackPanel 은 안으로 들어가고, 나머지는 한 덩어리 줄로 본다</summary>
    private static IEnumerable<FrameworkElement> Items(FrameworkElement el)
    {
        if (el is StackPanel sp)
        {
            foreach (var c in sp.Children.OfType<FrameworkElement>())
                foreach (var x in Items(c)) yield return x;
        }
        else yield return el;
    }
}

/// <summary>Win2D 로 그릴 때 쓸 글꼴 — XAML 과 같은 순서(Pretendard → Segoe UI Variable → 맑은 고딕)로 설치된 첫 글꼴</summary>
public static class Win2DFont
{
    private static string? _family;

    public static string Family
    {
        get
        {
            if (_family != null) return _family;
            try
            {
                var installed = CanvasTextFormat.GetSystemFontFamilies().ToHashSet(StringComparer.OrdinalIgnoreCase);
                _family = new[] { "Pretendard Variable", "Pretendard", "Segoe UI Variable Text", "Malgun Gothic" }
                    .FirstOrDefault(installed.Contains) ?? "Segoe UI";
            }
            catch (Exception e)
            {
                Log.Warn($"글꼴 목록 실패: {e.Message}");
                _family = "Segoe UI";
            }
            return _family;
        }
    }
}

/// <summary>
/// 그림을 불러오는 동안 빛이 지나가는 임시 배경 (pear 65 oneui-shimmer) — 다 불러오면 빛은 사라지고 그림이 페이드인.
/// 빛 띠는 칸 폭의 두 배짜리 그라데이션이 150% → −50% 로 흘러간다 (1.6초 반복).
/// 흘러가는 값 하나(T)를 모든 칸이 같이 쓰므로 카드가 수백 개여도 애니메이션은 하나다
/// </summary>
public static class Shimmer
{
    private static CompositionPropertySet? _clock;
    private static CompositionLinearGradientBrush? _brush;

    /// <summary>img 를 빛 지나가는 칸과 겹쳐 담은 Grid 를 돌려준다 (그림이 열리거나 실패하면 빛은 지운다)</summary>
    public static Grid Wrap(Image img)
    {
        var g = new Grid();
        var (host, done) = Create(0);
        g.Children.Add(host);
        g.Children.Add(img);
        img.ImageOpened += (_, _) => done();
        img.ImageFailed += (_, _) => done();
        return g;
    }

    /// <summary>
    /// 빛이 지나가는 칸 하나와, 그림이 다 오면 부를 '끝' 동작. radius 를 주면 둥근 칸 안에서만 지나간다
    /// (빛은 아주 옅어서 둥근 자르기의 계단은 보이지 않는다)
    /// </summary>
    public static (Grid Host, Action Done) Create(double radius)
    {
        var host = new Grid { IsHitTestVisible = false };
        if (!Anim.Enabled) { host.Visibility = Visibility.Collapsed; return (host, () => { }); }
        SpriteVisual? sprite = null;
        void Done()
        {
            if (sprite == null) { host.Visibility = Visibility.Collapsed; return; }
            ElementCompositionPreview.SetElementChildVisual(host, null);
            sprite.Dispose();
            sprite = null;
            host.Visibility = Visibility.Collapsed;
        }
        host.Loaded += (_, _) =>
        {
            if (host.Visibility != Visibility.Visible || sprite != null) return;
            var hv = Anim.V(host);
            var c = hv.Compositor;
            Ensure(c);
            sprite = c.CreateSpriteVisual();
            sprite.Brush = _brush;
            // 칸 폭 두 배 (background-size 200%) · 위치는 −w × (1.5 − 2T)
            var size = c.CreateExpressionAnimation("Vector2(host.Size.X * 2, host.Size.Y)");
            size.SetReferenceParameter("host", hv);
            sprite.StartAnimation("Size", size);
            var offset = c.CreateExpressionAnimation("Vector3(-host.Size.X * (1.5 - 2 * clock.T), 0, 0)");
            offset.SetReferenceParameter("host", hv);
            offset.SetReferenceParameter("clock", _clock!);
            sprite.StartAnimation("Offset", offset);
            if (radius > 0)
            {
                var geo = c.CreateRoundedRectangleGeometry();
                geo.CornerRadius = new Vector2((float)radius);
                geo.Size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);
                host.SizeChanged += (_, _) => geo.Size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);
                hv.Clip = c.CreateGeometricClip(geo);
            }
            else hv.Clip = c.CreateInsetClip();
            ElementCompositionPreview.SetElementChildVisual(host, sprite);
        };
        host.Unloaded += (_, _) => Done();
        return (host, Done);
    }

    private static void Ensure(Compositor c)
    {
        if (_clock != null) return;
        _clock = c.CreatePropertySet();
        _clock.InsertScalar("T", 0);
        var run = c.CreateScalarKeyFrameAnimation();
        run.InsertKeyFrame(0f, 0f);
        run.InsertKeyFrame(1f, 1f, c.CreateLinearEasingFunction());
        run.Duration = TimeSpan.FromMilliseconds(1600);
        run.IterationBehavior = AnimationIterationBehavior.Forever;
        _clock.StartAnimation("T", run);
        // linear-gradient(100deg, transparent 30%, 흰 8% 50%, transparent 70%)
        _brush = c.CreateLinearGradientBrush();
        _brush.StartPoint = new Vector2(0, 0.4f);
        _brush.EndPoint = new Vector2(1, 0.6f);
        _brush.ColorStops.Add(c.CreateColorGradientStop(0.3f, Color.FromArgb(0, 255, 255, 255)));
        _brush.ColorStops.Add(c.CreateColorGradientStop(0.5f, Color.FromArgb(22, 255, 255, 255)));
        _brush.ColorStops.Add(c.CreateColorGradientStop(0.7f, Color.FromArgb(0, 255, 255, 255)));
    }
}

/// <summary>
/// 불러오는 동안 보여 주는 회색 자리 (스켈레톤) — 카드 아트가 아직 안 온 회색 칸과 같은 색으로,
/// 천천히 숨 쉬듯 밝아졌다 어두워진다. 내용이 오면 이 자리를 치우고 진짜 셸프가 나타난다
/// </summary>
public static class Skeleton
{
    private static readonly Color Fill = Color.FromArgb(20, 255, 255, 255);

    private static Border Box(double w, double h, double r) =>
        new() { Width = w, Height = h, CornerRadius = new CornerRadius(r), Background = Theme.B(Fill), HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>셸프 모양 (제목 줄 + 카드 줄) × rows</summary>
    public static FrameworkElement Shelves(double left, int rows = 3, double size = 164)
    {
        var root = new StackPanel { IsHitTestVisible = false };
        for (var r = 0; r < rows; r++)
        {
            var title = Box(r == 0 ? 220 : 160 + r * 30 % 90, 24, 8);
            title.Margin = new Thickness(left, r == 0 ? 24 : 40, 0, 16);
            root.Children.Add(title);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(left, 0, 0, 0) };
            for (var i = 0; i < 10; i++)
            {
                var card = new StackPanel { Spacing = 8 };
                card.Children.Add(Box(size, size, 18));
                card.Children.Add(Box(size * 0.82, 14, 6));
                card.Children.Add(Box(size * 0.52, 12, 6));
                row.Children.Add(card);
            }
            root.Children.Add(new Border { Child = row, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        Pulse(root);
        return root;
    }

    /// <summary>
    /// 앨범 · 재생목록 화면 모양 — DetailView.BuildTwoColumn 과 같은 치수(왼쪽 360 칸 · 간격 48 · 아트 264 · 버튼 40/64 ·
    /// 오른쪽 카드 최대 760 · 줄 68)라서 내용이 와도 자리가 움직이지 않는다
    /// </summary>
    public static FrameworkElement Detail(double left, double right, int rows = 10)
    {
        var root = new Grid { IsHitTestVisible = false, Margin = new Thickness(left, 8, right, 0), ColumnSpacing = 48 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 760 });
        var head = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
        var art = Box(264, 264, Theme.RadiusArt); art.HorizontalAlignment = HorizontalAlignment.Center; art.Margin = new Thickness(0, 0, 0, 8);
        head.Children.Add(art);
        var title = Box(190, 34, 10); title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 4, 0, 4);
        head.Children.Add(title);
        foreach (var w in new[] { 150.0, 110 })
        {
            var sub = Box(w, 13, 6); sub.HorizontalAlignment = HorizontalAlignment.Center;
            head.Children.Add(sub);
        }
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var d in new[] { 40.0, 40, 40, 64, 40, 40 }) btns.Children.Add(new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = Theme.B(Fill), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(btns);
        root.Children.Add(head);
        var list = new StackPanel();
        var panel = new Border
        {
            CornerRadius = new CornerRadius(Theme.RadiusCard),
            Background = Theme.B(Color.FromArgb(20, 255, 255, 255)),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 24, 0, 0),
            Child = list,
            VerticalAlignment = VerticalAlignment.Top,
        };
        for (var i = 0; i < rows; i++)
        {
            var row = new Grid { Height = 68, Padding = new Thickness(8, 0, 8, 0) };
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(Box(44, 44, Theme.RadiusThumb));
            var t = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(Box(140 + i * 37 % 140, 14, 6));
            t.Children.Add(Box(90 + i * 23 % 110, 12, 5));
            inner.Children.Add(t);
            row.Children.Add(inner);
            var len = Box(32, 12, 5); len.HorizontalAlignment = HorizontalAlignment.Right; len.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(len);
            if (i < rows - 1) row.Children.Add(new Border { Height = 1, Background = Theme.B(Theme.Divider), VerticalAlignment = VerticalAlignment.Bottom });
            list.Children.Add(row);
        }
        Grid.SetColumn(panel, 1);
        root.Children.Add(panel);
        Pulse(root);
        return root;
    }

    /// <summary>숨 쉬듯 1 ↔ 0.45 (1.1초) — GPU 쪽 투명도라 가볍다</summary>
    private static void Pulse(UIElement el)
    {
        var v = Anim.V(el);
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, 1f);
        a.InsertKeyFrame(0.5f, 0.45f, v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f)));
        a.InsertKeyFrame(1f, 1f, v.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f)));
        a.Duration = TimeSpan.FromMilliseconds(2200);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Opacity", a);
    }
}
