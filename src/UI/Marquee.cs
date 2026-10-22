using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.UI.Text;

namespace OneMusic.UI;

/// <summary>
/// 칸보다 긴 글자를 버스 LED 처럼 흘려서 끝까지 보여 주는 한 줄 글자 (pear marquee.ts 그대로).
/// 2.2초 멈춤 → 초당 38px 로 끝까지 (cubic-bezier(.45,0,.55,1)) → 1.4초 멈춤 → 0.28초 사라졌다 처음으로 → 0.28초 나타남 → 반복.
/// 넘칠 때만 양 끝을 22px 흐린다: 처음엔 오른쪽만 → 흐르기 시작하면 왼쪽도 → 끝에 닿으면 오른쪽은 사라짐.
/// Win2D 로 직접 그린다 (XAML 글자에는 마스크를 걸 수 없어서).
/// </summary>
public sealed class Marquee : Grid
{
    private const float Speed = 38, Edge = 22;
    private const double HoldStart = 2200, HoldEnd = 1400, Fade = 280;

    private readonly CanvasControl _canvas = new();
    private readonly float _size;
    private readonly FontWeight _weight;
    private Color _color;
    private string _text = "";
    private CanvasTextLayout? _layout;
    private float _need;
    private long _start;
    private bool _running;
    private float _x, _alpha = 1;

    public Marquee(double size, FontWeight weight, Color color)
    {
        _size = (float)size;
        _weight = weight;
        _color = color;
        // 한 줄 높이 (XAML 글자와 같은 줄 간격)
        var probe = Theme.Text("가A", size, weight, color);
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Height = Math.Ceiling(probe.DesiredSize.Height);
        _canvas.Draw += OnDraw;
        _canvas.CreateResources += (_, _) => _layout = null;
        Children.Add(_canvas);
        Background = new SolidColorBrush(Colors.Transparent);
        SizeChanged += (_, _) => Restart();
        Unloaded += (_, _) => Stop();
        Loaded += (_, _) => Restart();
    }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value) return;
            _text = value;
            _layout = null;
            Restart();
        }
    }

    public Brush Foreground
    {
        set
        {
            if (value is SolidColorBrush b) _color = b.Color;
            _canvas.Invalidate();
        }
    }

    private CanvasTextLayout Layout(ICanvasResourceCreator rc)
    {
        if (_layout != null) return _layout;
        using var fmt = new CanvasTextFormat
        {
            FontFamily = Win2DFont.Family,
            FontSize = _size,
            FontWeight = _weight,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        _layout = new CanvasTextLayout(rc, _text, fmt, 100000, 1000);
        _need = (float)_layout.LayoutBounds.Width;
        return _layout;
    }

    private float Over => _need - (float)ActualWidth;

    private void Restart()
    {
        _x = 0;
        _alpha = 1;
        _start = Environment.TickCount64;
        if (_canvas.IsLoaded && _canvas.ReadyToDraw) Layout(_canvas);
        if (_layout != null && Over > 1 && ActualWidth > 0) Begin();
        else Stop();
        _canvas.Invalidate();
    }

    private void Begin()
    {
        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private long _lastFrame;

    private void OnFrame(object? s, object e)
    {
        var now = Environment.TickCount64;
        if (now - _lastFrame < 15) return; // 60fps 면 충분
        _lastFrame = now;
        var over = Over;
        if (over <= 1) { Stop(); _x = 0; _alpha = 1; _canvas.Invalidate(); return; }
        var move = Math.Max(900, over / Speed * 1000);
        var total = HoldStart + move + HoldEnd + Fade * 2;
        var t = (now - _start) % total;
        if (t < HoldStart) { _x = 0; _alpha = 1; }
        else if (t < HoldStart + move) { _x = over * (float)Tween.Bezier(0.45, 0, 0.55, 1, (t - HoldStart) / move); _alpha = 1; }
        else if (t < HoldStart + move + HoldEnd) { _x = over; _alpha = 1; }
        else if (t < HoldStart + move + HoldEnd + Fade) { _x = over; _alpha = 1 - (float)((t - HoldStart - move - HoldEnd) / Fade); }
        else { _x = 0; _alpha = (float)((t - HoldStart - move - HoldEnd - Fade) / Fade); }
        _canvas.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_text.Length == 0) return;
        var hadLayout = _layout != null;
        var layout = Layout(sender);
        // 처음 그릴 때 글자 폭을 알게 되면 흐름을 시작한다
        if (!hadLayout && Over > 1 && !_running) { _start = Environment.TickCount64; Begin(); }
        var w = (float)ActualWidth;
        var h = (float)ActualHeight;
        if (w <= 0) return;
        var y = (h - (float)layout.LayoutBounds.Height) / 2 - (float)layout.LayoutBounds.Top;
        var ds = args.DrawingSession;
        var over = _need - w;
        if (over <= 1)
        {
            ds.DrawTextLayout(layout, 0, y, _color);
            return;
        }
        // 양 끝 흐림: 왼쪽은 흐르기 시작하면(8px 안에서 서서히), 오른쪽은 끝에 닿기 전까지
        var l = Edge * Math.Clamp(_x / 8f, 0, 1);
        var r = Edge * Math.Clamp((over - _x) / 8f, 0, 1);
        var stops = new[]
        {
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb((byte)(l > 0.5f ? 0 : 255), 0, 0, 0) },
            new CanvasGradientStop { Position = Math.Clamp(l / w, 0, 0.49f), Color = Colors.Black },
            new CanvasGradientStop { Position = Math.Clamp(1 - r / w, 0.51f, 1), Color = Colors.Black },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb((byte)(r > 0.5f ? 0 : 255), 0, 0, 0) },
        };
        using var mask = new CanvasLinearGradientBrush(sender, stops) { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(w, 0) };
        using (ds.CreateLayer(mask))
            ds.DrawTextLayout(layout, -_x, y, Color.FromArgb((byte)(_color.A * Math.Clamp(_alpha, 0, 1)), _color.R, _color.G, _color.B));
    }
}
