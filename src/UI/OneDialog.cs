namespace OneMusic.UI;

/// <summary>
/// One UI 대화상자 — 윈도우 기본 ContentDialog 대신 앱 맨 위 층에 직접 그린다
/// (그래야 뒤가 진하게 흐려지고, pear 처럼 스프링으로 튀어나온다: oneui-pop morph 560ms spring).
/// 뒤는 검게 덮이고(45%), 가운데 둥근(28) 유리 카드에 제목 · 내용 · [취소] [확인]. Esc = 취소, 바깥을 눌러도 취소
/// </summary>
public static class OneDialog
{
    private static Grid? _layer;
    private static TaskCompletionSource<bool>? _open;
    private static Border? _card;
    private static XRect? _smoke;

    public static bool IsOpen => _open != null;

    public static void Attach(Grid root)
    {
        _layer = new Grid { Visibility = Visibility.Collapsed };
        root.Children.Add(_layer);
    }

    /// <summary>확인을 누르면 true. ready 에 '확인 누르기' 동작을 넘겨 준다 (입력 칸 Enter 용)</summary>
    public static Task<bool> Show(string title, FrameworkElement? content, string primary, string close = "취소", Action<Action>? ready = null, double width = 420)
    {
        if (_layer == null) return Task.FromResult(false);
        Cancel();
        OneMenu.CloseAll();
        var tcs = _open = new TaskCompletionSource<bool>();
        _layer.Children.Clear();
        _smoke = new XRect { Fill = Theme.B(Color.FromArgb(115, 0, 0, 0)) };
        _smoke.PointerPressed += (_, e) => { e.Handled = true; Finish(false); };
        _layer.Children.Add(_smoke);

        var stack = new StackPanel { Spacing = 16 };
        var t = Theme.Text(title, 20, FontWeights.ExtraBold, Theme.OnSurface, 2);
        stack.Children.Add(t);
        if (content != null) stack.Children.Add(content);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        // close 가 "" 이면 확인 버튼 하나만 (알려 주기만 하는 창)
        if (close.Length > 0) buttons.Children.Add(Button(close, false, () => Finish(false)));
        buttons.Children.Add(Button(primary, true, () => Finish(true)));
        stack.Children.Add(buttons);
        _card = new Border
        {
            Width = width,
            Padding = new Thickness(28, 24, 24, 20),
            CornerRadius = new CornerRadius(28),
            Background = Theme.Glass(0.78, Color.FromArgb(255, 46, 46, 48), blur: 44),
            BorderBrush = Theme.B(Color.FromArgb(24, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
        };
        _layer.Children.Add(Anim.ShadowFor(_card, 48));
        _layer.Children.Add(_card);
        _layer.Visibility = Visibility.Visible;
        Anim.V(_smoke).Opacity = 0;
        Anim.Opacity(_smoke, 1, Anim.Medium);
        _card.Measure(new Size(width, double.PositiveInfinity));
        Anim.PopSpring(_card);
        ready?.Invoke(() => Finish(true));
        return tcs.Task;
    }

    /// <summary>열려 있으면 취소로 닫는다 (Esc)</summary>
    public static void Cancel() => Finish(false);

    private static async void Finish(bool ok)
    {
        if (_open is not { } tcs || _layer == null) return;
        _open = null;
        tcs.TrySetResult(ok);
        var card = _card;
        var smoke = _smoke;
        if (card != null)
        {
            // oneui-pop-out: 200ms 동안 0.94 로 줄며 사라짐
            card.IsHitTestVisible = false;
            Anim.Opacity(card, 0, 200);
            Anim.Scale(card, 0.94f);
        }
        if (smoke != null)
        {
            smoke.IsHitTestVisible = false;
            Anim.Opacity(smoke, 0, Anim.Fast);
        }
        await Task.Delay(220);
        if (_open != null) return; // 그 사이 새 대화상자가 열림
        _layer.Children.Clear();
        _layer.Visibility = Visibility.Collapsed;
    }

    private static FrameworkElement Button(string text, bool primary, Action click)
    {
        var label = Theme.Text(text, 15, FontWeights.Bold, primary ? Colors.Black : Theme.OnSurface);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var idle = primary ? Colors.White : Theme.Chip;
        var hover = primary ? Color.FromArgb(255, 230, 230, 232) : Theme.ChipHover;
        var b = new Border
        {
            Height = 44,
            MinWidth = 96,
            Padding = new Thickness(20, 0, 20, 0),
            CornerRadius = new CornerRadius(22),
            Background = Theme.B(idle),
            Child = label,
        };
        Anim.FadeBg(b);
        b.PointerEntered += (_, _) => b.Background = Theme.B(hover);
        b.PointerExited += (_, _) => b.Background = Theme.B(idle);
        Anim.Pressable(b);
        b.Tapped += (_, e) => { e.Handled = true; click(); };
        return b;
    }
}
