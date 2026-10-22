using Microsoft.Graphics.Canvas.Effects;

namespace OneMusic.UI;

/// <summary>
/// 진한 유리 — 뒤 내용을 크게 흐리고(가우시안) 채도를 올린 뒤 색을 한 겹 덮는다.
/// CSS 의 <c>backdrop-filter: blur(..) saturate(1.4)</c> + 반투명 배경과 같은 순서 (pear 메뉴: blur 24 · saturate 1.4).
/// 윈도우 기본 AcrylicBrush 는 블러 세기를 못 바꾸고, 창이 뒤로 가면 불투명해지며 회색 막이 끼어서 직접 그린다
/// </summary>
public sealed class BlurBrush : XamlCompositionBrushBase
{
    private static CompositionEffectFactory? _factory;
    private static Compositor? _factoryOwner;

    public float Blur { get; init; } = 30;
    public Color Tint { get; init; } = Theme.GlassTint;

    /// <summary>CSS saturate(1.4) 행렬 (밝기 가중치 0.213 · 0.715 · 0.072)</summary>
    private static Matrix5x4 Saturate(float s) => new()
    {
        M11 = 0.213f + 0.787f * s, M21 = 0.715f - 0.715f * s, M31 = 0.072f - 0.072f * s,
        M12 = 0.213f - 0.213f * s, M22 = 0.715f + 0.285f * s, M32 = 0.072f - 0.072f * s,
        M13 = 0.213f - 0.213f * s, M23 = 0.715f - 0.715f * s, M33 = 0.072f + 0.928f * s,
        M44 = 1,
    };

    protected override void OnConnected()
    {
        if (CompositionBrush != null) return;
        var c = CompositionTarget.GetCompositorForCurrentThread();
        if (_factory == null || _factoryOwner != c)
        {
            var graph = new CompositeEffect
            {
                Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver,
                Sources =
                {
                    new ColorMatrixEffect
                    {
                        ColorMatrix = Saturate(1.4f),
                        ClampOutput = true,
                        Source = new GaussianBlurEffect
                        {
                            Name = "Blur",
                            BlurAmount = 30,
                            BorderMode = EffectBorderMode.Hard,
                            Optimization = EffectOptimization.Balanced,
                            Source = new CompositionEffectSourceParameter("backdrop"),
                        },
                    },
                    new ColorSourceEffect { Name = "Tint", Color = Theme.GlassTint },
                },
            };
            _factory = c.CreateEffectFactory(graph, new[] { "Blur.BlurAmount", "Tint.Color" });
            _factoryOwner = c;
        }
        var brush = _factory.CreateBrush();
        brush.SetSourceParameter("backdrop", c.CreateBackdropBrush());
        brush.Properties.InsertScalar("Blur.BlurAmount", Blur);
        brush.Properties.InsertColor("Tint.Color", Tint);
        CompositionBrush = brush;
    }

    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
    }
}
