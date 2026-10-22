using Microsoft.UI.Input;

namespace OneMusic.UI;

/// <summary>iOS / One UI 느낌의 스프링 · 감속 애니메이션 (GPU 컴포지션에서 돈다 → 화면 스레드를 막지 않음)</summary>
public static class Anim
{
    public static Visual V(UIElement el) => ElementCompositionPreview.GetElementVisual(el);

    // ── iOS 스프링 곡선 (UISpringTimingParameters · SwiftUI .smooth / .spring / .bouncy) ──
    // 감쇠 진동 식을 그대로 풀어 키프레임으로 꽂는다. 정지 상태에서 출발해 처음엔 살짝 늦게 붙고, 길고 부드럽게 내려앉는다
    // (예전 pear CSS linear() 곡선은 출발이 급하고 꼬리가 짧아 웹 느낌이었다)
    /// <summary>기본 — 감쇠비 0.86, 아주 살짝(0.5%) 넘쳤다 돌아온다 (iOS 기본 스프링)</summary>
    public static readonly float[] Ease = Sample(0.86);
    /// <summary>넘치지 않는 감속 — 임계 감쇠 (SwiftUI .smooth)</summary>
    public static readonly float[] SmoothCurve = Sample(1);
    /// <summary>눌렀다 뗄 때 튕기는 곡선 — 감쇠비 0.72, 4% 쯤 넘친다 (SwiftUI .bouncy 보다 얌전하게)</summary>
    public static readonly float[] Spring = Sample(0.72);

    /// <summary>
    /// 감쇠비 zeta 스프링의 0→1 진행 (t 도 0~1). 고유 진동수는 t=1 에서 남은 흔들림이 0.2% 아래가 되게 맞춘다 —
    /// 그래서 시간(ms)을 바꿔도 모양은 같고 길이만 늘어난다
    /// </summary>
    public static double SpringAt(double zeta, double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        if (zeta >= 1)
        {
            const double w = 8.4; // (1 + w) e^-w ≈ 0.002
            return 1 - (1 + w * t) * Math.Exp(-w * t);
        }
        var wn = 6.2 / zeta; // e^-6.2 ≈ 0.002
        var wd = wn * Math.Sqrt(1 - zeta * zeta);
        return 1 - Math.Exp(-zeta * wn * t) * (Math.Cos(wd * t) + zeta * wn / wd * Math.Sin(wd * t));
    }

    private static float[] Sample(double zeta, int n = 48) =>
        Enumerable.Range(0, n + 1).Select(i => (float)SpringAt(zeta, i / (double)n)).ToArray();

    public const int Fast = 220, Medium = 440, Morph = 560;
    public const float PressedScale = 0.9f;

    /// <summary>
    /// 윈도우 설정 '애니메이션 효과' 를 끈 사람에겐 움직임을 거의 바로 끝낸다 (pear: prefers-reduced-motion → 1ms)
    /// </summary>
    public static bool Enabled { get; } = ReadEnabled();

    private static bool ReadEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return true; }
    }

    /// <summary>애니메이션 길이 (움직임 줄이기면 1ms)</summary>
    public static int D(int ms) => Enabled ? ms : 1;

    private static CompositionEasingFunction? _smooth, _linear;
    public static CompositionEasingFunction Smooth(Compositor c) =>
        _smooth ??= c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));
    private static CompositionEasingFunction Linear(Compositor c) => _linear ??= c.CreateLinearEasingFunction();

    private static string F(float f) => f.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 만든 애니메이션은 다시 쓴다 (키프레임 40개짜리 식을 마우스를 올릴 때마다 새로 풀지 않게).
    /// 컴포지션 애니메이션은 틀이라서 여러 요소에 동시에 걸어도 된다
    /// </summary>
    private static readonly Dictionary<(float[], float, int, int), ScalarKeyFrameAnimation> ScalarCache = new();
    private static readonly Dictionary<(float[], Vector3, int, int), Vector3KeyFrameAnimation> VecCache = new();

    private static ScalarKeyFrameAnimation Scalar(Compositor c, float to, float[] curve, int ms, int delayMs)
    {
        var key = (curve, to, ms, delayMs);
        if (ScalarCache.TryGetValue(key, out var cached)) return cached;
        if (ScalarCache.Count > 300) ScalarCache.Clear();
        return ScalarCache[key] = MakeScalar(c, to, curve, ms, delayMs);
    }

    private static Vector3KeyFrameAnimation Vec(Compositor c, Vector3 to, float[] curve, int ms, int delayMs)
    {
        var key = (curve, to, ms, delayMs);
        if (VecCache.TryGetValue(key, out var cached)) return cached;
        if (VecCache.Count > 300) VecCache.Clear();
        return VecCache[key] = MakeVec(c, to, curve, ms, delayMs);
    }

    private static ScalarKeyFrameAnimation MakeScalar(Compositor c, float to, float[] curve, int ms, int delayMs)
    {
        ms = D(ms);
        if (!Enabled) delayMs = 0;
        var a = c.CreateScalarKeyFrameAnimation();
        for (var i = 1; i < curve.Length; i++)
            a.InsertExpressionKeyFrame((float)i / (curve.Length - 1), $"this.StartingValue + ({F(to)} - this.StartingValue) * {F(curve[i])}", Linear(c));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        if (delayMs > 0) a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        return a;
    }

    private static Vector3KeyFrameAnimation MakeVec(Compositor c, Vector3 to, float[] curve, int ms, int delayMs)
    {
        ms = D(ms);
        if (!Enabled) delayMs = 0;
        var a = c.CreateVector3KeyFrameAnimation();
        var target = $"Vector3({F(to.X)}, {F(to.Y)}, {F(to.Z)})";
        for (var i = 1; i < curve.Length; i++)
            a.InsertExpressionKeyFrame((float)i / (curve.Length - 1), $"this.StartingValue + ({target} - this.StartingValue) * {F(curve[i])}", Linear(c));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        if (delayMs > 0) a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        return a;
    }

    private static void Center(UIElement el, Visual v)
    {
        var s = el.ActualSize;
        v.CenterPoint = new Vector3(s.X / 2, s.Y / 2, 0);
    }

    /// <summary>
    /// 크기 — pear 와 같게: 줄어들 때는 빠른 감속(220ms smooth), 커질 때(떼거나 마우스 올림)는 600ms 스프링 곡선.
    /// (damping · period 는 예전 호출과 맞추려고 남겨 둔 값이라 쓰지 않는다)
    /// </summary>
    public static void Scale(UIElement el, float to, float damping = 0.55f, int periodMs = 45)
    {
        var v = V(el);
        Center(el, v);
        var shrinking = to < v.Scale.X - 0.001f;
        v.StartAnimation("Scale", Vec(v.Compositor, new Vector3(to, to, 1), shrinking ? SmoothCurve : Spring, shrinking ? Fast : 600, 0));
    }

    /// <summary>투명도 — 기본 440ms ease (pear: opacity var(--oneui-medium) var(--oneui-ease))</summary>
    public static void Opacity(UIElement el, float to, int ms = Medium, int delayMs = 0)
    {
        var v = V(el);
        v.StartAnimation("Opacity", Scalar(v.Compositor, to, SmoothCurve, ms, delayMs));
    }

    /// <summary>위치 이동 — 440ms ease (탭 알약 · 스위치 손잡이 · 재생 화면 올라오기)</summary>
    public static void Translate(UIElement el, Vector3 to, float damping = 0.8f, int periodMs = 60)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        v.StartAnimation("Translation", Vec(v.Compositor, to, Ease, periodMs >= 70 ? Morph : Medium, 0));
    }

    /// <summary>위치 이동 (시간 지정, ease) — 목록 줄이 비켜서기 · 제자리로 돌아가기</summary>
    public static void TranslateTo(UIElement el, Vector3 to, int ms)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        v.StartAnimation("Translation", Vec(v.Compositor, to, Ease, ms, 0));
    }

    public static void SetTranslation(UIElement el, Vector3 value)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        v.StopAnimation("Translation");
        v.Properties.InsertVector3("Translation", value);
    }

    /// <summary>위치 · 크기가 바뀌면(레이아웃 변화) 자동으로 부드럽게 따라가게 한다</summary>
    public static void ImplicitLayout(UIElement el, int ms = Medium)
    {
        var v = V(el);
        var c = v.Compositor;
        var offset = c.CreateVector3KeyFrameAnimation();
        offset.Target = "Offset";
        for (var i = 1; i < Ease.Length; i++)
            offset.InsertExpressionKeyFrame((float)i / (Ease.Length - 1), $"this.StartingValue + (this.FinalValue - this.StartingValue) * {F(Ease[i])}", Linear(c));
        offset.Duration = TimeSpan.FromMilliseconds(ms);
        var col = c.CreateImplicitAnimationCollection();
        col["Offset"] = offset;
        v.ImplicitAnimations = col;
    }

    /// <summary>나타날 때 — pear 의 oneui-rise: 14px 아래에서 떠오르며 페이드인, 440ms ease</summary>
    public static void Appear(UIElement el, int delayMs = 0, float fromY = 14)
    {
        var v = V(el);
        v.Opacity = 0;
        SetTranslation(el, new Vector3(0, fromY, 0));
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, Ease, Medium, delayMs));
        v.StartAnimation("Translation", Vec(v.Compositor, Vector3.Zero, Ease, Medium, delayMs));
    }

    private static CompositionEasingFunction? _decel;
    /// <summary>cubic-bezier(0.22, 1, 0.36, 1) — 재생 화면 전환 · 아이콘 전환의 '튕김 없는 우아한 감속'</summary>
    public static CompositionEasingFunction Decel(Compositor c) =>
        _decel ??= c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));

    /// <summary>위치를 넘치지 않는 스프링으로 (재생 화면 올라오기 640ms)</summary>
    public static void Slide(UIElement el, Vector3 to, int ms)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = V(el);
        v.StartAnimation("Translation", Vec(v.Compositor, to, SmoothCurve, ms, 0));
    }

    /// <summary>pear oneui-rise 720ms: 14px 아래에서 떠오르며 나타남 (넘치지 않는 스프링)</summary>
    public static void Rise(UIElement el, int delayMs, int ms = 720)
    {
        var v = V(el);
        v.Opacity = 0;
        SetTranslation(el, new Vector3(0, 14, 0));
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, SmoothCurve, ms, delayMs));
        v.StartAnimation("Translation", Vec(v.Compositor, Vector3.Zero, SmoothCurve, ms, delayMs));
    }

    /// <summary>크기 애니메이션 틀 (x · y 따로 — 스위치 손잡이 늘이기)</summary>
    public static Vector3KeyFrameAnimation ScaleAnim(Compositor c, Vector3 to, int ms) => Vec(c, to, Ease, ms, 0);

    /// <summary>크기를 pear 곡선으로 (카드 아트 hover 1.04 — morph 560ms ease)</summary>
    public static void ScaleTo(UIElement el, float to, int ms, float[] curve)
    {
        var v = V(el);
        Center(el, v);
        v.StartAnimation("Scale", Vec(v.Compositor, new Vector3(to, to, 1), curve, ms, 0));
    }

    /// <summary>pear oneui-drop: 6px 위 · 0.97 에서 내려앉으며 (검색 추천 줄)</summary>
    public static void Drop(UIElement el, int delayMs = 0)
    {
        var v = V(el);
        Center(el, v);
        v.Opacity = 0;
        v.Scale = new Vector3(0.97f, 0.97f, 1);
        SetTranslation(el, new Vector3(0, -6, 0));
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, Ease, Medium, delayMs));
        v.StartAnimation("Scale", Vec(v.Compositor, Vector3.One, Ease, Medium, delayMs));
        v.StartAnimation("Translation", Vec(v.Compositor, Vector3.Zero, Ease, Medium, delayMs));
    }

    /// <summary>pear 토스트: oneui-rise 를 morph 560ms 스프링으로</summary>
    public static void RiseSpring(UIElement el)
    {
        var v = V(el);
        v.Opacity = 0;
        SetTranslation(el, new Vector3(0, 14, 0));
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, SmoothCurve, Medium, 0));
        v.StartAnimation("Translation", Vec(v.Compositor, Vector3.Zero, Spring, Morph, 0));
    }

    /// <summary>나타날 때 — pear 의 oneui-pop: 0.92 에서 커지며 (메뉴 · 팝업). origin 은 커지는 기준점(0~1)</summary>
    public static void Pop(UIElement el, int delayMs = 0, Vector2? origin = null)
    {
        var v = V(el);
        Center(el, v);
        if (origin is { } o)
        {
            var s = el.ActualSize;
            if (s.X <= 0 && el is FrameworkElement fe) s = new Vector2((float)fe.DesiredSize.Width, (float)fe.DesiredSize.Height);
            v.CenterPoint = new Vector3(s.X * o.X, s.Y * o.Y, 0);
        }
        v.Opacity = 0;
        v.Scale = new Vector3(0.92f, 0.92f, 1);
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, SmoothCurve, Medium, delayMs));
        v.StartAnimation("Scale", Vec(v.Compositor, Vector3.One, Ease, Medium, delayMs));
    }

    /// <summary>대화상자 — pear: oneui-pop 을 morph 560ms 스프링으로 (가운데 기준)</summary>
    public static void PopSpring(FrameworkElement el)
    {
        var v = V(el);
        var s = el.ActualSize;
        if (s.X <= 0) s = new Vector2((float)el.DesiredSize.Width, (float)el.DesiredSize.Height);
        v.CenterPoint = new Vector3(s.X / 2, s.Y / 2, 0);
        v.Opacity = 0;
        v.Scale = new Vector3(0.92f, 0.92f, 1);
        v.StartAnimation("Opacity", Scalar(v.Compositor, 1, SmoothCurve, Medium, 0));
        v.StartAnimation("Scale", Vec(v.Compositor, Vector3.One, Spring, Morph, 0));
    }

    /// <summary>
    /// 그림자만 드리우는 '받침' — 카드 바로 뒤에 같은 자리 · 같은 크기로 깔고, 투명도 · 크기 변화를 따라간다.
    /// ⚠️ 클릭할 수 있는 카드에 직접 ThemeShadow + Translation(Z) 를 주면 맨 위 층에 띄운 경우
    /// 카드가 클릭(히트 테스트)에서 통째로 빠져 버렸다(⋮ 메뉴 항목이 안 눌리던 원인). 그래서 그림자는 클릭 안 되는 받침이 맡는다
    /// </summary>
    public static Border ShadowFor(Border card, float z) => ShadowFor(card, card.CornerRadius, z);

    public static Border ShadowFor(FrameworkElement card, CornerRadius radius, float z)
    {
        var caster = new Border
        {
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            CornerRadius = radius,
            HorizontalAlignment = card.HorizontalAlignment,
            VerticalAlignment = card.VerticalAlignment,
            Shadow = new ThemeShadow(),
            Translation = new Vector3(0, 0, z),
        };
        void Sync()
        {
            caster.Width = card.ActualWidth;
            caster.Height = card.ActualHeight;
            caster.Margin = card.Margin;
            caster.HorizontalAlignment = card.HorizontalAlignment;
            caster.VerticalAlignment = card.VerticalAlignment;
        }
        card.SizeChanged += (_, _) => Sync();
        card.LayoutUpdated += (_, _) => { if (caster.Margin != card.Margin) caster.Margin = card.Margin; };
        Sync();
        var cv = V(caster);
        var bv = V(card);
        var c = cv.Compositor;
        foreach (var prop in new[] { "Opacity", "Scale", "CenterPoint" })
        {
            var e = c.CreateExpressionAnimation("b." + prop);
            e.SetReferenceParameter("b", bv);
            cv.StartAnimation(prop, e);
        }
        return caster;
    }

    /// <summary>배경색을 바꿀 때(마우스 올림 · 고름) 뚝 바뀌지 않고 서서히 — XAML BrushTransition</summary>
    public static T FadeBg<T>(T el, int ms = 160) where T : FrameworkElement
    {
        var t = new BrushTransition { Duration = TimeSpan.FromMilliseconds(D(ms)) };
        if (el is Border b) b.BackgroundTransition = t;
        else if (el is Panel p) p.BackgroundTransition = t;
        return el;
    }

    /// <summary>누르면 0.9 로 줄었다가 놓으면 스프링으로 (pear: --oneui-pressed-scale 0.9)</summary>
    public static void Pressable(UIElement el, float pressed = PressedScale)
    {
        el.PointerPressed += (_, _) => Scale(el, pressed);
        el.PointerReleased += (_, _) => Scale(el, 1f);
        el.PointerCanceled += (_, _) => Scale(el, 1f);
        el.PointerCaptureLost += (_, _) => Scale(el, 1f);
        el.PointerExited += (_, _) => Scale(el, 1f);
    }
}

/// <summary>동그란 아이콘 버튼 — 마우스를 올리면 은은한 원, 누르면 스프링</summary>
public class IconButton : Grid
{
    private readonly XEllipse _hover;
    private readonly Grid _content = new();
    public event Action? Click;

    public IconButton(double size = 40)
    {
        Width = Height = size;
        Background = new SolidColorBrush(Colors.Transparent);
        _hover = new XEllipse { Fill = Theme.B(Theme.ChipHover) };
        Anim.V(_hover).Opacity = 0; // XAML Opacity 가 아니라 GPU 쪽 투명도를 움직인다
        Children.Add(_hover);
        Children.Add(_content);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        PointerEntered += (_, _) => Anim.Opacity(_hover, 1, 180);
        PointerExited += (_, _) => Anim.Opacity(_hover, 0, 220);
        Tapped += (_, e) =>
        {
            e.Handled = true;
            Click?.Invoke();
        };
        Anim.Pressable(_content);
    }

    public UIElement? Content
    {
        get => _content.Children.FirstOrDefault();
        set
        {
            _content.Children.Clear();
            if (value != null) _content.Children.Add(value);
        }
    }

    public Brush HoverBrush
    {
        set => _hover.Fill = value;
    }

    public void SetCursorHand() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}
