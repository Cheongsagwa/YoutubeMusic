using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// LoadedImageSurface 를 다 불러올 때까지 붙잡아 둔다.
/// 지역 변수로만 들고 있으면 불러오는 사이에 GC 가 래퍼를 거둬 가고, 그러면 안쪽 그림도 함께 풀려 LoadCompleted 가 영영 오지 않는다
/// → 곡이 바뀌어도 아트가 그대로 남았다 (곡이 바뀔 땐 오디오 · 응답 바이트가 한꺼번에 생겨 GC 가 마침 그때 잘 돈다)
/// </summary>
internal static class ArtLoader
{
    private static readonly HashSet<LoadedImageSurface> Pending = new();

    public static void Start(byte[] bytes, Size size, Action<LoadedImageSurface, LoadedImageSourceLoadStatus> done)
    {
        var surface = LoadedImageSurface.StartLoadFromStream(new MemoryStream(bytes).AsRandomAccessStream(), size);
        Pending.Add(surface);
        surface.LoadCompleted += (s, e) =>
        {
            Pending.Remove(s);
            done(s, e.Status);
        };
    }
}

/// <summary>
/// 재생 바 · 재생 화면 앨범아트 (pear image-fade.ts).
/// 마지막으로 보여 준 아트를 바로 밑 '받침'에 깔아 두고, 새 아트를 다 불러오면
/// 가운데부터 번지듯(radial mask 0 → 180%) 드러낸다 — 700ms cubic-bezier(0.33, 0, 0.2, 1), 투명도 0.2 → 1.
/// 불러오는 동안에도 이전 아트가 그대로 보인다(회색 칸 없음). 둥근 모서리는 아트와 같게 잘라 낸다.
/// </summary>
public sealed class ArtView : Grid
{
    private readonly float _radius;
    private readonly int _decode;
    private ContainerVisual? _root;
    private SpriteVisual? _under, _top;
    private CompositionSurfaceBrush? _shown;
    /// <summary>보이는 아트 · 받침 아트의 그림 (바뀌면 다 쓴 것은 바로 놓아 GPU 메모리를 돌려준다)</summary>
    private LoadedImageSurface? _shownSurface, _underSurface;
    private string? _pendingUrl;
    private int _gen;
    /// <summary>지금 아트를 읽은 픽셀 크기 — 칸이 이보다 커지면(창 키우기) 더 크게 다시 읽는다</summary>
    private double _loadedPx;
    private int _regrowGen;

    /// <summary>개발용: 마지막으로 바꾸라 한 차례 · 실제로 드러낸 차례 (같으면 아트가 따라왔다)</summary>
    internal int RequestedGen => _gen;
    internal int ShownGen { get; private set; }

    public ArtView(float radius, int decodeSize)
    {
        _radius = radius;
        _decode = decodeSize;
        Background = Empty;
        CornerRadius = new CornerRadius(radius);
        Loaded += (_, _) => { Init(); Resize(); if (_pendingUrl != null) Show(_pendingUrl); };
        SizeChanged += (_, _) => Resize();
    }

    private Compositor C => Anim.V(this).Compositor;

    private void Init()
    {
        if (_root != null) return;
        var c = C;
        _root = c.CreateContainerVisual();
        _under = c.CreateSpriteVisual();
        _top = c.CreateSpriteVisual();
        _root.Children.InsertAtTop(_under);
        _root.Children.InsertAtTop(_top);
        ElementCompositionPreview.SetElementChildVisual(this, _root);
    }

    /// <summary>
    /// 둥근 모서리 — 그림 붓마다 매끈한 둥근 마스크(RoundMask)를 씌운다.
    /// (기하 자르기는 계단졌고, 층 전체를 VisualSurface 로 비춰 가리던 방식은 16:9 아트를 가운데 정사각형으로 잘라 먹었다)
    /// </summary>
    private CompositionMaskBrush Round(CompositionBrush source)
    {
        var m = C.CreateMaskBrush();
        m.Source = source;
        m.Mask = MaskFor();
        return m;
    }

    private CompositionSurfaceBrush MaskFor() =>
        RoundMask.Create(C, _radius, new Vector2((float)Math.Max(1, ActualWidth), (float)Math.Max(1, ActualHeight)), XamlRoot?.RasterizationScale ?? 1);

    private void Resize()
    {
        if (_root == null || _under == null || _top == null) return;
        var size = new Vector2((float)ActualWidth, (float)ActualHeight);
        _root.Size = size;
        _under.Size = size;
        _top.Size = size;
        _top.CenterPoint = new Vector3(size / 2, 0);
        // 칸 크기가 바뀌면 (노래 ↔ 16:9 영상 썸네일) 둥근 마스크도 그 크기로 다시
        if (size.X < 1 || size.Y < 1) return;
        if (_under.Brush is CompositionMaskBrush u) u.Mask = MaskFor();
        if (_top.Brush is CompositionMaskBrush t) t.Mask = MaskFor();
        if (_pendingUrl != null && _loadedPx > 0 && Px() > _loadedPx * 1.15) Regrow();
    }

    /// <summary>화면에 그려질 픽셀 크기 (아직 배치 전이면 기본값)</summary>
    private double Px() => ActualWidth > 0 ? ActualWidth * (XamlRoot?.RasterizationScale ?? 1) : _decode;

    /// <summary>창을 키워 칸이 커지면 흐려지지 않게 그 크기로 다시 읽는다 (끌며 키우는 동안 매번 읽지 않게 잠깐 기다렸다가)</summary>
    private async void Regrow()
    {
        var g = ++_regrowGen;
        await Task.Delay(300);
        if (g == _regrowGen && _pendingUrl != null && Px() > _loadedPx * 1.15) Show(_pendingUrl);
    }

    /// <summary>새 아트로 바꾼다 (주소가 같으면 그대로)</summary>
    public void Show(string? url)
    {
        _pendingUrl = url;
        if (_root == null) return;
        var gen = ++_gen;
        var (u, fallback) = Theme.ImageUrl(url, Math.Max(_decode, (int)Px()));
        // 아트가 없는 곡이면 이전 곡 아트를 남겨 두지 않는다
        if (u == null) { Clear(); return; }
        Load(u, fallback, gen);
    }

    private async void Load(string url, string? fallback, int gen)
    {
        // 바이트는 캐시에서 (다음 곡 아트는 미리 받아 둔다 → 곡이 바뀌면 바로)
        byte[] bytes;
        try { bytes = await ImageCache.Get(url); }
        catch (Exception e)
        {
            if (gen != _gen) return;
            if (fallback != null && fallback != url) Load(fallback, null, gen);
            else Log.Warn($"아트 받기 실패: {e.Message} ({url})");
            return;
        }
        if (gen != _gen) return;
        // 화면에 그려질 실제 픽셀 크기로 읽는다 — 2배로 읽어 GPU 가 4:1 로 줄이면(재생 바 52px) 테두리 · 글씨가 계단져 깨졌다.
        // 가로는 16/9 만큼 여유를 줘서 16:9 그림도 높이에 맞춰 꽉 채운다 (LoadedImageSurface 는 비율을 지켜 이 안에 맞춘다)
        var px = _loadedPx = Px();
        var want = new Size(Math.Ceiling(px * 16 / 9), Math.Ceiling(px));
        try
        {
            ArtLoader.Start(bytes, want, (s, status) =>
            {
                if (gen != _gen) { s.Dispose(); return; }
                if (status != LoadedImageSourceLoadStatus.Success)
                {
                    s.Dispose();
                    if (fallback != null && fallback != url) Load(fallback, null, gen);
                    else Log.Warn($"아트 읽기 실패: {status} ({url})");
                    return;
                }
                ShownGen = gen;
                Reveal(s);
                DropBackdrop(gen);
            });
        }
        catch (Exception e) { Log.Warn($"아트 불러오기 실패: {e.Message}"); }
    }

    private static readonly Brush Empty = Theme.B(Color.FromArgb(20, 255, 255, 255));

    /// <summary>
    /// 아트가 다 드러나면 옅은 회색 바탕을 치운다 — 바탕도 같은 둥근 칸이라 두 장의 가장자리 AA 가 겹쳐
    /// 검은 아트 둥근 모서리에 흰 줄이 비쳤다
    /// </summary>
    private async void DropBackdrop(int gen)
    {
        await Task.Delay(Anim.Enabled ? 760 : 0);
        if (gen == _gen && _shown != null) Background = null;
    }

    /// <summary>보이던 아트를 서서히 지워 빈 칸(옅은 회색 바탕)으로</summary>
    private void Clear()
    {
        Background = Empty;
        if (_under == null || _top == null || _shown == null) return;
        _under.Brush?.Dispose();
        _under.Brush = null;
        _underSurface?.Dispose();
        _underSurface = null;
        var top = _top;
        var brush = top.Brush;
        var shown = _shown;
        var shownSurface = _shownSurface;
        _shown = null;
        _shownSurface = null;
        var c = C;
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f, Anim.Smooth(c));
        fade.Duration = TimeSpan.FromMilliseconds(Anim.D(300));
        top.StartAnimation("Opacity", fade);
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(Anim.Enabled ? 350 : 0);
            if (top.Brush == brush)
            {
                top.StopAnimation("Opacity");
                top.Opacity = 0;
                top.Brush = null;
                brush?.Dispose();
            }
            shown.Dispose();
            shownSurface?.Dispose();
        });
    }

    private void Reveal(LoadedImageSurface surface)
    {
        if (_under == null || _top == null) return;
        var c = C;
        var brush = c.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.UniformToFill;
        brush.HorizontalAlignmentRatio = 0.5f;
        brush.VerticalAlignmentRatio = 0.5f;
        // 지금 보이던 아트는 받침으로, 그 전 받침은 버린다
        var oldUnder = _under.Brush;
        var oldUnderSurface = _underSurface;
        if (_shown != null)
        {
            _under.Brush = Round(_shown);
            _underSurface = _shownSurface;
            oldUnder?.Dispose();
            oldUnderSurface?.Dispose();
        }
        _shown = brush;
        _shownSurface = surface;

        // 새 아트는 이전 아트 위로 서서히 나타나며 살짝(1.04 → 1) 자리 잡는다 — 700ms cubic-bezier(0.33, 0, 0.2, 1).
        // (예전엔 가운데서 번지는 원형 마스크였지만, 둥근 모서리 마스크와 마스크를 겹칠 수 없어 바꿨다)
        var oldTop = _top.Brush;
        var top = _top;
        var rounded = Round(brush);
        top.Brush = rounded;
        oldTop?.Dispose();
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.33f, 0), new Vector2(0.2f, 1));
        top.Opacity = 0f;
        top.Scale = new Vector3(1.04f, 1.04f, 1);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(Anim.D(700));
        top.StartAnimation("Opacity", fade);
        var settle = c.CreateVector3KeyFrameAnimation();
        settle.InsertKeyFrame(1f, Vector3.One, ease);
        settle.Duration = TimeSpan.FromMilliseconds(Anim.D(700));
        top.StartAnimation("Scale", settle);
        // 앱이 막 뜬 직후엔 애니메이션이 안 돌고 멈춰 첫 아트가 투명하게 남았다(재생 바 아트 빈 칸) → 끝날 즈음 최종 상태를 못 박는다
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(Anim.Enabled ? 760 : 0);
            if (top.Brush != rounded || _shown != brush) return;
            top.StopAnimation("Opacity");
            top.StopAnimation("Scale");
            top.Opacity = 1;
            top.Scale = Vector3.One;
        });
    }
}

/// <summary>
/// 앰비언트 모드 (pear ambient-mode) — 재생 화면 앨범아트 뒤로 아트가 크게 흐려진 빛이 번진다.
/// 곡이 바뀌면 새 빛이 천천히(0.9초) 피어나고 옛 빛은 사라진다. 아트를 작게(96px) 받아 크게 흐려서 가볍다
/// </summary>
public sealed class AmbientGlow : Grid
{
    private static CompositionEffectFactory? _factory;
    private SpriteVisual? _current;
    private LoadedImageSurface? _currentSurface;
    private ContainerVisual? _root;
    private string? _url;
    private int _gen;

    public AmbientGlow()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { Init(); if (_url != null) Show(_url); };
        SizeChanged += (_, _) => Resize();
    }

    private void Init()
    {
        if (_root != null) return;
        var c = Anim.V(this).Compositor;
        _root = c.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(this, _root);
        Resize();
    }

    /// <summary>
    /// 빛은 아트보다 사방으로 12% 더 크게. 가장자리는 둥근 그라데이션으로 0 까지 흐려서
    /// 재생 화면 칸 경계에 잘려도 네모난 모서리가 보이지 않게 한다
    /// </summary>
    private void Resize()
    {
        if (_root == null) return;
        var w = (float)ActualWidth;
        var h = (float)ActualHeight;
        _root.Size = new Vector2(w * 1.24f, h * 1.24f);
        _root.Offset = new Vector3(-w * 0.12f, -h * 0.12f, 0);
        foreach (var v in _root.Children.OfType<SpriteVisual>()) v.Size = _root.Size;
    }

    public void Show(string? url)
    {
        _url = url;
        if (_root == null || !Anim.Enabled) return;
        var gen = ++_gen;
        var (u, fallback) = Theme.ImageUrl(url, 48);
        if (u == null) { FadeOut(); return; }
        Load(u, fallback, gen);
    }

    private async void Load(string url, string? fallback, int gen)
    {
        byte[] bytes;
        try { bytes = await ImageCache.Get(url); }
        catch
        {
            if (gen == _gen && fallback != null) Load(fallback, null, gen);
            return;
        }
        if (gen != _gen || _root == null) return;
        try { ArtLoader.Start(bytes, new Size(96, 96), Done); }
        catch { }

        void Done(LoadedImageSurface s, LoadedImageSourceLoadStatus status)
        {
            if (gen != _gen || _root == null) { s.Dispose(); return; }
            if (status != LoadedImageSourceLoadStatus.Success)
            {
                s.Dispose();
                if (fallback != null) Load(fallback, null, gen);
                return;
            }
            var c = _root.Compositor;
            _factory ??= c.CreateEffectFactory(new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                BlurAmount = 60,
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Soft,
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Speed,
                Source = new CompositionEffectSourceParameter("art"),
            });
            var art = c.CreateSurfaceBrush(s);
            art.Stretch = CompositionStretch.UniformToFill;
            var brush = _factory.CreateBrush();
            brush.SetSourceParameter("art", art);
            // 가운데는 진하고 바깥으로 갈수록 0 — 네모난 가장자리가 생기지 않는 둥근 빛
            var fade = c.CreateRadialGradientBrush();
            fade.EllipseCenter = new Vector2(0.5f, 0.5f);
            fade.EllipseRadius = new Vector2(0.5f, 0.5f);
            fade.ColorStops.Add(c.CreateColorGradientStop(0f, Colors.Black));
            fade.ColorStops.Add(c.CreateColorGradientStop(0.55f, Color.FromArgb(200, 0, 0, 0)));
            fade.ColorStops.Add(c.CreateColorGradientStop(1f, Color.FromArgb(0, 0, 0, 0)));
            var masked = c.CreateMaskBrush();
            masked.Source = brush;
            masked.Mask = fade;
            var sprite = c.CreateSpriteVisual();
            sprite.Brush = masked;
            sprite.Size = _root.Size;
            sprite.Opacity = 0;
            _root.Children.InsertAtTop(sprite);
            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(1f, 0.55f, Anim.Smooth(c));
            a.Duration = TimeSpan.FromMilliseconds(900);
            sprite.StartAnimation("Opacity", a);
            FadeOut();
            _current = sprite;
            _currentSurface = s;
        }
    }

    /// <summary>지금 빛을 서서히 끄고 치운다</summary>
    private async void FadeOut()
    {
        var old = _current;
        var oldSurface = _currentSurface;
        _current = null;
        _currentSurface = null;
        if (old == null || _root == null) return;
        var c = _root.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, 0f, Anim.Smooth(c));
        a.Duration = TimeSpan.FromMilliseconds(900);
        old.StartAnimation("Opacity", a);
        await Task.Delay(950);
        _root?.Children.Remove(old);
        if (old.Brush is CompositionMaskBrush mb)
        {
            (mb.Source as CompositionEffectBrush)?.GetSourceParameter("art")?.Dispose();
            mb.Source?.Dispose();
            mb.Mask?.Dispose();
        }
        old.Brush?.Dispose();
        old.Dispose();
        oldSurface?.Dispose();
    }
}

/// <summary>
/// 창 전체 앨범아트 배경 — 앨범아트를 창 크기로 크게 늘려 아주 세게 흐린다 (앨범의 여러 색이 그대로 번져 보이게).
/// 글씨가 읽히게 어둡게 깔고, 곡이 바뀌면 서서히 바뀐다
/// </summary>
public sealed class ArtBackdrop : Grid
{
    private static CompositionEffectFactory? _factory;
    private ContainerVisual? _root;
    private SpriteVisual? _current;
    private LoadedImageSurface? _currentSurface;
    private string? _url;
    private int _gen;
    public float Strength { get; set; } = 0.75f;

    public ArtBackdrop()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { Init(); if (_url != null) Show(_url); };
        SizeChanged += (_, _) => Resize();
    }

    private void Init()
    {
        if (_root != null) return;
        _root = Anim.V(this).Compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(this, _root);
        Resize();
    }

    private void Resize()
    {
        if (_root == null) return;
        _root.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
        foreach (var v in _root.Children.OfType<SpriteVisual>()) v.Size = _root.Size;
    }

    public void Show(string? url)
    {
        _url = url;
        if (_root == null) return;
        var gen = ++_gen;
        var (u, fallback) = Theme.ImageUrl(url, 64);
        if (u == null) { FadeOut(); return; }
        Load(u, fallback, gen);
    }

    private async void Load(string url, string? fallback, int gen)
    {
        byte[] bytes;
        try { bytes = await ImageCache.Get(url); }
        catch
        {
            if (gen == _gen && fallback != null) Load(fallback, null, gen);
            return;
        }
        if (gen != _gen || _root == null) return;
        try { ArtLoader.Start(bytes, new Size(64, 64), Done); }
        catch { }

        void Done(LoadedImageSurface s, LoadedImageSourceLoadStatus status)
        {
            if (gen != _gen || _root == null) { s.Dispose(); return; }
            if (status != LoadedImageSourceLoadStatus.Success)
            {
                s.Dispose();
                if (fallback != null) Load(fallback, null, gen);
                return;
            }
            var c = _root.Compositor;
            // 흐림 → 채도 살짝 올림 (흐리면 색이 탁해져서)
            _factory ??= c.CreateEffectFactory(new Microsoft.Graphics.Canvas.Effects.SaturationEffect
            {
                Saturation = 1.35f,
                Source = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
                {
                    BlurAmount = 90,
                    BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Hard,
                    Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Speed,
                    Source = new CompositionEffectSourceParameter("art"),
                },
            });
            var art = c.CreateSurfaceBrush(s);
            art.Stretch = CompositionStretch.UniformToFill;
            var brush = _factory.CreateBrush();
            brush.SetSourceParameter("art", art);
            var sprite = c.CreateSpriteVisual();
            sprite.Brush = brush;
            sprite.Size = _root.Size;
            sprite.Opacity = 0;
            _root.Children.InsertAtTop(sprite);
            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(1f, Strength, Anim.Smooth(c));
            a.Duration = TimeSpan.FromMilliseconds(1100);
            sprite.StartAnimation("Opacity", a);
            FadeOut();
            _current = sprite;
            _currentSurface = s;
        }
    }

    private async void FadeOut()
    {
        var old = _current;
        var oldSurface = _currentSurface;
        _current = null;
        _currentSurface = null;
        if (old == null || _root == null) return;
        var c = _root.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, 0f, Anim.Smooth(c));
        a.Duration = TimeSpan.FromMilliseconds(1100);
        old.StartAnimation("Opacity", a);
        await Task.Delay(1150);
        _root?.Children.Remove(old);
        (old.Brush as CompositionEffectBrush)?.GetSourceParameter("art")?.Dispose();
        old.Brush?.Dispose();
        old.Dispose();
        oldSurface?.Dispose();
    }
}
