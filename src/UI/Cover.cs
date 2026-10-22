using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Graphics.DirectX;

namespace OneMusic.UI;

/// <summary>
/// 둥근 아트 · 프로필 사진 한 칸 — 그림을 둥근 Border 의 '배경 붓(ImageBrush)'으로 그린다.
/// ⚠️ 둥근 Border 안에 Image 를 '자식'으로 넣으면 모서리 자르기가 안티앨리어싱 없이 계단지고,
/// UniformToFill 로 넘친 그림은 왼쪽 기준으로 놓여 16:9 아트가 가운데가 아니라 왼쪽이 남았다.
/// 붓으로 그리면 모서리는 매끈하고(배경 모양은 AA) 자르기는 가운데 기준(AlignmentX/Y = Center)
/// </summary>
public sealed class Cover : Grid
{
    private readonly Border _image;
    private readonly Border _placeholder;
    private bool _dim;
    private readonly ImageBrush _brush;
    private readonly ScaleTransform _zoom = new() { CenterX = 0.5, CenterY = 0.5 };
    private Storyboard? _zoomSb;
    public event Action? Opened;

    /// <param name="decode">받을 그림 폭(논리 px) — 16:9 를 정사각으로 자르면 높이에 맞게 더 크게</param>
    /// <param name="placeholder">그림이 오기 전 회색 칸 색 (null 이면 Theme.Chip)</param>
    public Cover(string? url, double width, double height, int decode, double radius, Color? placeholder = null, bool shimmer = true)
    {
        Width = width;
        Height = height;
        var corner = new CornerRadius(radius);
        _placeholder = new Border { CornerRadius = corner, Background = Theme.B(placeholder ?? Theme.Chip), IsHitTestVisible = false };
        Children.Add(_placeholder);
        Action done = () => { };
        if (shimmer)
        {
            var (host, d) = Shimmer.Create(radius);
            Children.Add(host);
            done = d;
        }
        _brush = new ImageBrush
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            RelativeTransform = _zoom,
        };
        // 화면 가까이(1500px 안) 와야 그림을 받는다 — 수백 곡 목록이 처음부터 그림을 전부 풀어 메모리를 먹던 것 (한 번 받으면 그대로)
        TypedEventHandler<FrameworkElement, EffectiveViewportChangedEventArgs>? near = null;
        near = (_, e) =>
        {
            if (e.BringIntoViewDistanceX > 1500 || e.BringIntoViewDistanceY > 1500) return;
            EffectiveViewportChanged -= near;
            _brush.ImageSource = Theme.Image(url, decode);
        };
        EffectiveViewportChanged += near;
        _image = new Border { CornerRadius = corner, Background = _brush };
        Anim.V(_image).Opacity = 0;
        _brush.ImageOpened += (_, _) =>
        {
            done();
            Anim.Opacity(_image, _dim ? DimLevel : 1, 360);
            // 그림이 오면 회색 칸은 치운다 — 둥근 칸 두 장의 가장자리가 겹쳐 모서리에 옅은 테두리가 생겼다
            _placeholder.Visibility = Visibility.Collapsed;
            Opened?.Invoke();
        };
        _brush.ImageFailed += (_, _) => done();
        Children.Add(_image);
    }

    private const float DimLevel = 0.6f;

    /// <summary>
    /// 그림 자체를 어둡게 (마우스를 올린 곡 줄) — 위에 둥근 검은 덮개를 따로 얹으면
    /// 두 도형의 가장자리 픽셀이 겹쳐 둥근 모서리가 깨져 보였다. 한 장만 투명도로 낮추면 모서리는 그대로 매끈하다
    /// </summary>
    public void Dim(bool on)
    {
        _dim = on;
        if (_placeholder.Visibility == Visibility.Collapsed) Anim.Opacity(_image, on ? DimLevel : 1, Anim.Fast);
    }

    /// <summary>
    /// 둥근 칸은 그대로 두고 안의 그림만 키운다 (마우스를 올리면 1.04 — pear 40). 칸째로 키우면 모서리가 칸 밖으로 나간다
    /// </summary>
    public void Zoom(double scale, int ms)
    {
        _zoomSb?.Stop();
        var sb = new Storyboard();
        foreach (var prop in new[] { "ScaleX", "ScaleY" })
        {
            var a = new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(Anim.Enabled ? ms : 1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(a, _zoom);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
        _zoomSb = sb;
        sb.Begin();
    }
}

/// <summary>
/// 컴포지션 비주얼용 매끈한 둥근 마스크 — CompositionGeometricClip 은 안티앨리어싱이 없어서 모서리가 계단졌다.
/// Win2D 로 AA 둥근 사각형을 '그 칸 크기 그대로' 그려 마스크로 쓴다.
/// ⚠️ 작은 정사각형을 9-grid 로 늘리는 방식은 마스크 붓이 그 정사각형 비율로 내용을 잡아 16:9 아트가 가운데 정사각형으로 잘렸다
/// </summary>
public static class RoundMask
{
    private static CompositionGraphicsDevice? _device;
    private static readonly Dictionary<(int W, int H, int R), CompositionDrawingSurface> _surfaces = new();

    /// <param name="size">칸 크기 (DIP)</param>
    /// <param name="scale">화면 배율 (선명하게 픽셀 크기로 그린다)</param>
    public static CompositionSurfaceBrush Create(Compositor c, float radius, Vector2 size, double scale)
    {
        var w = Math.Max(1, (int)Math.Round(size.X * scale));
        var h = Math.Max(1, (int)Math.Round(size.Y * scale));
        var r = (int)Math.Round(radius * scale);
        if (!_surfaces.TryGetValue((w, h, r), out var surface))
        {
            _device ??= CanvasComposition.CreateCompositionGraphicsDevice(c, CanvasDevice.GetSharedDevice());
            surface = _device.CreateDrawingSurface(new Size(w, h), DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            using (var ds = CanvasComposition.CreateDrawingSession(surface))
            {
                ds.Clear(Color.FromArgb(0, 0, 0, 0));
                ds.FillRoundedRectangle(0, 0, w, h, r, r, Colors.Black);
            }
            if (_surfaces.Count > 32) _surfaces.Clear();
            _surfaces[(w, h, r)] = surface;
        }
        var brush = c.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.Fill;
        return brush;
    }
}
