namespace OneMusic.UI;

/// <summary>얇은 One UI 슬라이더 (볼륨용) — 트랙 + 작은 흰 손잡이</summary>
public sealed class MiniSlider : Grid
{
    private readonly XRect _track = new() { Height = 4, RadiusX = 2, RadiusY = 2, Fill = Theme.B(Color.FromArgb(56, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
    private readonly XRect _fill = new() { Height = 4, RadiusX = 2, RadiusY = 2, Fill = Theme.B(Colors.White), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly XEllipse _knob = new() { Width = 12, Height = 12, Fill = Theme.B(Colors.White), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private double _value;
    private bool _drag;
    public event Action<double>? ValueChanged;

    public MiniSlider()
    {
        Height = 28;
        Background = new SolidColorBrush(Colors.Transparent);
        Children.Add(_track);
        Children.Add(_fill);
        Children.Add(_knob);
        SizeChanged += (_, _) => Layout();
        PointerPressed += (_, e) => { _drag = true; CapturePointer(e.Pointer); Set(e.GetCurrentPoint(this).Position.X); };
        PointerMoved += (_, e) => { if (_drag) Set(e.GetCurrentPoint(this).Position.X); };
        PointerReleased += (_, e) => { _drag = false; ReleasePointerCapture(e.Pointer); };
        Tapped += (_, e) => e.Handled = true;
        PointerWheelChanged += (_, e) =>
        {
            Value = Math.Clamp(_value + e.GetCurrentPoint(this).Properties.MouseWheelDelta / 120.0 * 0.05, 0, 1);
            ValueChanged?.Invoke(_value);
            e.Handled = true;
        };
    }

    public bool Dragging => _drag;

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Layout();
        }
    }

    private void Set(double x)
    {
        var w = ActualWidth - 12;
        if (w <= 0) return;
        Value = (x - 6) / w;
        ValueChanged?.Invoke(_value);
    }

    private void Layout()
    {
        var w = Math.Max(0, ActualWidth - 12);
        _track.Margin = new Thickness(6, 0, 6, 0);
        _fill.Margin = new Thickness(6, 0, 0, 0);
        _fill.Width = w * _value;
        _knob.Margin = new Thickness(w * _value, 0, 0, 0);
    }
}
