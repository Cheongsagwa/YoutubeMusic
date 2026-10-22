using Microsoft.UI.Xaml.Documents;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 재생 화면 '댓글' 탭 (pear 스크린샷 · YouTube 댓글과 같게):
///  머리 '댓글 810개' + 정렬(인기 댓글 · 최신순) / 내 사진 + '댓글 추가...' 입력 (Enter 로 올리기) /
///  40px 동그란 사진 · @이름 · 언제 · 내용 · 좋아요 수 · 싫어요 · 답글 / '답글 N개 ⌄' 를 누르면 답글이 펼쳐진다(왼쪽 이음선).
///  끝까지 내리면 다음 묶음을 이어 받는다
/// </summary>
public sealed class CommentsView : Grid
{
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly StackPanel _list = new() { Spacing = 20, Padding = new Thickness(4, 8, 8, 24) };
    private readonly TextBlock _header = Theme.Text("댓글", 22, FontWeights.ExtraBold, Theme.OnSurface);
    private readonly IconButton _sort = new(36);
    private readonly Grid _compose = new() { Margin = new Thickness(4, 4, 8, 4), Visibility = Visibility.Collapsed, ColumnSpacing = 12 };
    private readonly TextBox _input = new() { FontFamily = Theme.Font, FontSize = 14, BorderThickness = new Thickness(0, 0, 0, 1), Background = new SolidColorBrush(Colors.Transparent), AcceptsReturn = false };
    private readonly Border _myAvatar = new() { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
    private string? _for, _next, _createParams;
    private List<(string Title, string Token, bool Selected)> _sorts = new();
    private bool _loading;
    private int _gen;

    public CommentsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(4, 16, 8, 8) };
        _header.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_header);
        _sort.Content = Theme.Icon(Icons.SortLines, 22, Colors.White, stroke: true, strokeWidth: 1.8);
        _sort.Visibility = Visibility.Collapsed;
        _sort.VerticalAlignment = VerticalAlignment.Center;
        Tip.Set(_sort, "정렬");
        _sort.Click += ShowSort;
        head.Children.Add(_sort);
        Children.Add(head);

        // 댓글 추가 — 내 사진 + 밑줄 입력칸
        _compose.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _compose.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _compose.Children.Add(_myAvatar);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            _input.Resources[key] = new SolidColorBrush(Colors.Transparent);
        _input.Resources["TextControlBorderBrush"] = Theme.B(Theme.Divider);
        _input.Resources["TextControlBorderBrushPointerOver"] = Theme.B(Theme.Secondary);
        _input.Resources["TextControlBorderBrushFocused"] = Theme.B(Colors.White);
        _input.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await Send();
        };
        SetColumn(_input, 1);
        _compose.Children.Add(_input);
        SetRow(_compose, 1);
        Children.Add(_compose);

        _scroll.Content = _list;
        EdgeMask.Attach(_scroll);
        SetRow(_scroll, 2);
        Children.Add(_scroll);
        _scroll.ViewChanged += (_, _) =>
        {
            if (_scroll.VerticalOffset > _scroll.ScrollableHeight - 400) More();
        };
    }

    /// <summary>탭을 열었을 때 · 곡이 바뀌었을 때 (같은 곡이면 그대로)</summary>
    public void Load(string? videoId)
    {
        if (videoId == null || videoId == _for) return;
        _for = videoId;
        Reload(null);
    }

    private async void Reload(string? token)
    {
        if (_for is not { } videoId) return;
        var gen = ++_gen;
        _next = null;
        _list.Children.Clear();
        var status = Theme.Text("불러오는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
        _list.Children.Add(status);
        try
        {
            var page = await InnerTube.Instance.Comments(videoId, token);
            if (gen != _gen) return;
            _list.Children.Clear();
            // 요즘은 개수 글자에 '댓글'이 이미 들어 있다 ('댓글 24개') — 두 번 붙지 않게
            if (token == null || page.Count != null) _header.Text = page.Count is { } cnt ? (cnt.StartsWith("댓글") ? cnt : $"댓글 {cnt}") : "댓글";
            // 개수가 없으면 위 탭 이름을 되풀이할 뿐이라 감춘다
            _header.Visibility = _header.Text == "댓글" ? Visibility.Collapsed : Visibility.Visible;
            if (page.Sorts.Count > 0) _sorts = page.Sorts;
            _sort.Visibility = _sorts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (page.CreateParams != null)
            {
                _createParams = page.CreateParams;
                _compose.Visibility = Visibility.Visible;
                _input.PlaceholderText = page.Placeholder ?? "댓글 추가...";
                if (page.MyAvatar != null)
                {
                    _myAvatarUrl = page.MyAvatar;
                    _myAvatar.Child = new Cover(page.MyAvatar, 28, 28, 28, 14, shimmer: false);
                }
            }
            if (page.Items.Count == 0)
            {
                status.Text = "댓글이 없거나 사용 중지된 곡이에요";
                _list.Children.Add(status);
                return;
            }
            Add(page, _list);
            _scroll.ChangeView(null, 0, null, true);
        }
        catch (Exception e)
        {
            Log.Warn($"댓글 실패: {e.Message}");
            if (gen == _gen) { status.Text = "댓글을 불러오지 못했어요"; _for = token == null ? null : _for; }
        }
    }

    /// <summary>정렬 메뉴 (인기 댓글 · 최신순)</summary>
    private void ShowSort()
    {
        var m = new OneMenu();
        foreach (var (title, token, selected) in _sorts)
            m.Item(title, Icons.SortLines, check: selected, click: () =>
            {
                _sorts = _sorts.Select(x => (x.Title, x.Token, x.Token == token)).ToList();
                Reload(token);
            });
        m.Show(_sort);
    }

    private async Task Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _createParams == null) return;
        if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
        _input.IsEnabled = false;
        try
        {
            var ok = await InnerTube.Instance.PostComment(text, _createParams);
            if (ok)
            {
                _input.Text = "";
                var mine = Row(new Comment("나", _myAvatarUrl, text, "방금 전", "", ""), reply: false);
                _list.Children.Insert(0, mine);
                Anim.Appear(mine);
                Toast.Show("댓글을 올렸어요");
            }
            else Toast.Show("댓글을 올리지 못했어요");
        }
        catch (Exception e)
        {
            Log.Warn($"댓글 쓰기 실패: {e.Message}");
            Toast.Show("댓글을 올리지 못했어요");
        }
        finally { _input.IsEnabled = true; }
    }

    private async void More()
    {
        if (_next == null || _loading || _for == null) return;
        _loading = true;
        var gen = _gen;
        try
        {
            var page = await InnerTube.Instance.Comments(_for, _next);
            if (gen == _gen) Add(page, _list);
        }
        catch (Exception e) { Log.Warn($"댓글 이어 받기 실패: {e.Message}"); _next = null; }
        finally { _loading = false; }
    }

    private void Add(CommentPage page, Panel into)
    {
        _next = page.Next;
        var delay = 0;
        foreach (var c in page.Items)
        {
            var row = Row(c, reply: false);
            into.Children.Add(row);
            Anim.Appear(row, delay);
            delay = Math.Min(delay + 30, 240);
        }
    }

    private string? _myAvatarUrl;

    /// <summary>댓글 속 시간 (1:37 · 01:02:15) — 시:분:초 또는 분:초</summary>
    private static readonly System.Text.RegularExpressions.Regex TimeRx = new(@"(?<![\d:])(?:(\d{1,2}):)?(\d{1,2}):([0-5]\d)(?![\d:])");

    private const double LineH = 21;
    private const int CollapsedLines = 5;

    /// <summary>
    /// 댓글 본문 — 시간(1:37 · 1:02:15)은 흰 바탕 검은 글씨 알약으로, 누르면 곡의 그 위치로 간다 (YT 댓글 타임스탬프).
    /// 5줄을 넘으면 접어 두고 '자세히 보기' 알약으로 펼친다 (높이가 부드럽게 늘어남), 펼치면 '간략히' 로 다시 접는다
    /// </summary>
    private static FrameworkElement CommentText(string text)
    {
        FrameworkElement body;
        var matches = TimeRx.Matches(text);
        if (matches.Count == 0)
        {
            body = new TextBlock
            {
                Text = text,
                FontFamily = Theme.Font,
                FontSize = 15,
                Foreground = Theme.B(Theme.OnSurface),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                LineHeight = LineH,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
        }
        else
        {
            var rich = new RichTextBlock
            {
                FontFamily = Theme.Font,
                FontSize = 15,
                Foreground = Theme.B(Theme.OnSurface),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = false, // 글자 선택이 알약 클릭을 가로채지 않게
                LineHeight = LineH,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            var para = new Paragraph();
            var at = 0;
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Index > at) para.Inlines.Add(new Run { Text = text[at..m.Index] });
                var h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
                var seconds = h * 3600 + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value);
                para.Inlines.Add(new InlineUIContainer { Child = TimePill(m.Value, seconds) });
                at = m.Index + m.Length;
            }
            if (at < text.Length) para.Inlines.Add(new Run { Text = text[at..] });
            rich.Blocks.Add(para);
            body = rich;
        }
        body.VerticalAlignment = VerticalAlignment.Top;
        return Collapsible(body);
    }

    /// <summary>5줄 넘는 본문을 접었다 폈다 — 바깥 칸 높이를 바꾸고 넘치는 부분은 잘라 낸다</summary>
    private static FrameworkElement Collapsible(FrameworkElement body)
    {
        const double collapsed = LineH * CollapsedLines;
        var clip = new Grid { VerticalAlignment = VerticalAlignment.Top };
        clip.Children.Add(body);
        var label = Theme.Text("자세히 보기", 13, FontWeights.Bold, Theme.OnSurface);
        label.VerticalAlignment = VerticalAlignment.Center;
        var pill = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(14, 0, 14, 0),
            Background = Theme.B(Theme.Chip),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 2),
            Child = label,
            Visibility = Visibility.Collapsed,
        };
        Anim.FadeBg(pill);
        pill.PointerEntered += (_, _) => pill.Background = Theme.B(Theme.ChipHover);
        pill.PointerExited += (_, _) => pill.Background = Theme.B(Theme.Chip);
        Anim.Pressable(pill);
        var box = new StackPanel();
        box.Children.Add(clip);
        box.Children.Add(pill);
        var expanded = false;
        double Full(double width)
        {
            body.Measure(new Size(width, double.PositiveInfinity));
            return body.DesiredSize.Height;
        }
        void Check()
        {
            if (clip.ActualWidth <= 0 || expanded) return;
            var tall = Full(clip.ActualWidth) > collapsed + 1;
            pill.Visibility = tall ? Visibility.Visible : Visibility.Collapsed;
            clip.Height = tall ? collapsed : double.NaN;
            clip.Clip = tall ? new RectangleGeometry { Rect = new Rect(0, 0, 10000, collapsed) } : null;
        }
        clip.SizeChanged += (_, e) => { if (e.PreviousSize.Width != e.NewSize.Width) Check(); };
        clip.Loaded += (_, _) => Check();
        void Toggle()
        {
            expanded = !expanded;
            label.Text = expanded ? "간략히" : "자세히 보기";
            var from = clip.ActualHeight;
            var to = expanded ? Full(clip.ActualWidth) : collapsed;
            clip.Height = from;
            var geo = new RectangleGeometry { Rect = new Rect(0, 0, 10000, Math.Max(from, to)) };
            clip.Clip = geo;
            Tween.Run(clip, Anim.D(380), t => Tween.Out(t), k =>
            {
                var h = from + (to - from) * k;
                clip.Height = h;
                geo.Rect = new Rect(0, 0, 10000, h);
                if (k >= 1 && expanded)
                {
                    // 다 펴지면 높이를 풀어 둔다 (창 폭이 바뀌어도 맞게)
                    clip.Height = double.NaN;
                    clip.Clip = null;
                }
            });
        }
        pill.Tag = (Action)Toggle; // 개발용 진단(expandfirst)이 부른다
        pill.Tapped += (_, e) => { e.Handled = true; Toggle(); };
        return box;
    }

    private static FrameworkElement TimePill(string label, int seconds)
    {
        var t = Theme.Text(label, 14, FontWeights.Bold, Colors.Black);
        t.VerticalAlignment = VerticalAlignment.Center;
        var pill = new Border
        {
            Background = Theme.B(Colors.White),
            // 완전한 캡슐(높이 20 · 반지름 10) — 반지름 8 은 낮은 해상도에서 모서리가 각져 계단처럼 보였다
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(1, 0, 1, 0),
            Child = t,
            // 글줄 기준선에 아래가 붙으므로 살짝 내려 글자 가운데에 맞춘다
            RenderTransform = new TranslateTransform { Y = 4 },
        };
        Tip.Set(pill, $"{label}(으)로 이동");
        Anim.Pressable(pill);
        Anim.FadeBg(pill);
        pill.PointerEntered += (_, _) => pill.Background = Theme.B(Color.FromArgb(255, 220, 220, 222));
        pill.PointerExited += (_, _) => pill.Background = Theme.B(Colors.White);
        pill.Tapped += (_, e) =>
        {
            e.Handled = true;
            var p = PlayerService.Instance;
            if (p.Duration > 0 && seconds > p.Duration) { Toast.Show("곡 길이를 넘는 시간이에요"); return; }
            p.Seek(seconds);
            if (!p.IsPlaying) p.TogglePlay();
        };
        return pill;
    }

    private FrameworkElement Row(Comment c, bool reply)
    {
        var size = reply ? 28 : 40;
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FrameworkElement av = c.Avatar != null
            ? new Cover(c.Avatar, size, size, size, size / 2.0, shimmer: false) { VerticalAlignment = VerticalAlignment.Top }
            : new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2.0), Background = Theme.B(Theme.Chip), VerticalAlignment = VerticalAlignment.Top };
        g.Children.Add(av);
        var body = new StackPanel { Spacing = 4 };
        var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        who.Children.Add(Theme.Text(c.Author, 13, FontWeights.SemiBold, Theme.OnSurface));
        who.Children.Add(Theme.Text(c.Time, 13, FontWeights.Normal, Theme.Secondary));
        body.Children.Add(who);
        body.Children.Add(CommentText(c.Text));
        body.Children.Add(Toolbar(c, reply, out var replyClicked));

        // 답글: 왼쪽 이음선 + 들여 쓴 목록. 'N개 ⌄' 를 누르면 펼쳐지고, 내가 쓴 답글도 여기에
        var replies = new StackPanel { Spacing = 14, Margin = new Thickness(0, 8, 0, 0) };
        var thread = new Grid { ColumnSpacing = 10, Visibility = Visibility.Collapsed };
        thread.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
        thread.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        thread.Children.Add(new XRect { Width = 2, RadiusX = 1, RadiusY = 1, Fill = Theme.B(Theme.Divider) });
        SetColumn(replies, 1);
        thread.Children.Add(replies);
        if (!reply && c.RepliesToken != null && c.Replies is { Length: > 0 } r && r != "0")
        {
            var toggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Background = new SolidColorBrush(Colors.Transparent) };
            var label = Theme.Text($"답글 {r}개", 14, FontWeights.Bold, Theme.OnSurface);
            label.VerticalAlignment = VerticalAlignment.Center;
            var chevron = Theme.Icon(Icons.ChevronDown, 18, Colors.White, stroke: true, strokeWidth: 2);
            chevron.RenderTransformOrigin = new Point(0.5, 0.5);
            chevron.RenderTransform = new RotateTransform();
            toggle.Children.Add(label);
            toggle.Children.Add(chevron);
            var loaded = false;
            toggle.Tapped += async (_, _) =>
            {
                var open = thread.Visibility != Visibility.Visible;
                thread.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                if (open) Anim.Appear(thread, 0, 8);
                var rot = (RotateTransform)chevron.RenderTransform;
                Tween.Run(rot, Anim.Medium, Tween.Out, k => rot.Angle = open ? 180 * k : 180 * (1 - k));
                if (!open || loaded || _for == null) return;
                loaded = true;
                var wait = Theme.Text("불러오는 중…", 13, FontWeights.SemiBold, Theme.Secondary);
                replies.Children.Add(wait);
                try
                {
                    var page = await InnerTube.Instance.Comments(_for, c.RepliesToken, replies: true);
                    replies.Children.Remove(wait);
                    var delay = 0;
                    foreach (var rc in page.Items)
                    {
                        var rr = Row(rc, reply: true);
                        replies.Children.Add(rr);
                        Anim.Appear(rr, delay, 8);
                        delay = Math.Min(delay + 30, 200);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"답글 실패: {e.Message}");
                    wait.Text = "답글을 불러오지 못했어요";
                    loaded = false;
                }
            };
            Anim.Pressable(toggle, 0.96f);
            body.Children.Add(toggle);
        }
        body.Children.Add(thread);

        // 답글 쓰기 칸 — '답글' 을 누르면 열린다. Enter 로 올리고, 올린 답글은 바로 이음선 안에
        if (!reply && c.ReplyParams is { } replyParams)
        {
            var box = ReplyBox(async t =>
            {
                if (!await InnerTube.Instance.PostReply(t, replyParams)) return false;
                thread.Visibility = Visibility.Visible;
                var mine = Row(new Comment("나", _myAvatarUrl, t, "방금 전", "", ""), reply: true);
                replies.Children.Insert(0, mine);
                Anim.Appear(mine, 0, 8);
                Toast.Show("답글을 올렸어요");
                return true;
            });
            body.Children.Insert(body.Children.IndexOf(thread), box);
            replyClicked = () =>
            {
                if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
                var open = box.Visibility != Visibility.Visible;
                box.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                if (!open) return;
                Anim.Appear(box, 0, 6);
                (box.Children.OfType<TextBox>().FirstOrDefault())?.Focus(FocusState.Programmatic);
            };
            SetReply(body, replyClicked);
        }
        SetColumn(body, 1);
        g.Children.Add(body);
        return g;
    }

    /// <summary>좋아요(수) · 싫어요 · 답글 — 누르면 YouTube 에 그대로 보내고 채운 아이콘으로 (실패하면 되돌림)</summary>
    private static FrameworkElement Toolbar(Comment c, bool reply, out Action? replyClicked)
    {
        replyClicked = null;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(-8, 0, 0, 0) };
        var state = c.LikeState ?? "INDIFFERENT";
        var like = new IconButton(32);
        var dislike = new IconButton(32);
        var count = Theme.Text("", 13, FontWeights.Normal, Theme.Secondary);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(0, 0, 8, 0);
        void Draw()
        {
            like.Content = state == "LIKED" ? Theme.IconFilledStroke(Icons.ThumbUp, 16, Colors.White) : Theme.Icon(Icons.ThumbUp, 16, Theme.Secondary, stroke: true);
            dislike.Content = state == "DISLIKED" ? Theme.IconFilledStroke(Icons.ThumbDown, 16, Colors.White) : Theme.Icon(Icons.ThumbDown, 16, Theme.Secondary, stroke: true);
            count.Text = state == "LIKED" ? c.LikesLiked ?? c.Likes : c.Likes;
        }
        Draw();
        async void Press(bool up)
        {
            if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
            var on = up ? state == "LIKED" : state == "DISLIKED";
            var action = up ? (on ? c.UnlikeAction : c.LikeAction) : (on ? c.UndislikeAction : c.DislikeAction);
            if (action == null) { Toast.Show("이 댓글에는 할 수 없어요"); return; }
            var prev = state;
            state = on ? "INDIFFERENT" : up ? "LIKED" : "DISLIKED";
            Draw();
            var btn = up ? like : dislike;
            Anim.V(btn.Content!).Scale = new Vector3(0.6f, 0.6f, 1);
            Anim.Scale(btn.Content!, 1f);
            try
            {
                if (await InnerTube.Instance.CommentAction(action)) return;
            }
            catch (Exception e) { Log.Warn($"댓글 좋아요 실패: {e.Message}"); }
            state = prev;
            Draw();
            Toast.Show("하지 못했어요");
        }
        like.Click += () => Press(true);
        dislike.Click += () => Press(false);
        Tip.Set(like, "좋아요");
        Tip.Set(dislike, "싫어요");
        bar.Children.Add(like);
        bar.Children.Add(count);
        bar.Children.Add(dislike);
        if (!reply && c.ReplyParams != null)
        {
            var r = new Border { Height = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 0, 12, 0), Background = new SolidColorBrush(Colors.Transparent), Margin = new Thickness(6, 0, 0, 0) };
            var rt = Theme.Text("답글", 13, FontWeights.SemiBold, Theme.OnSurface);
            rt.VerticalAlignment = VerticalAlignment.Center;
            r.Child = rt;
            Anim.FadeBg(r);
            r.PointerEntered += (_, _) => r.Background = Theme.B(Theme.ChipHover);
            r.PointerExited += (_, _) => r.Background = new SolidColorBrush(Colors.Transparent);
            Anim.Pressable(r, 0.94f);
            r.Tag = "reply";
            bar.Children.Add(r);
        }
        return bar;
    }

    /// <summary>툴바의 '답글' 단추에 동작을 붙인다</summary>
    private static void SetReply(StackPanel body, Action click)
    {
        foreach (var bar in body.Children.OfType<StackPanel>())
            if (bar.Children.OfType<Border>().FirstOrDefault(x => x.Tag as string == "reply") is { } r)
            {
                r.Tapped += (_, e) => { e.Handled = true; click(); };
                return;
            }
    }

    /// <summary>답글 입력 칸 (밑줄) — Enter 로 올린다. 올리면 비우고 닫힌다</summary>
    private Grid ReplyBox(Func<string, Task<bool>> send)
    {
        var g = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
        var input = new TextBox { FontFamily = Theme.Font, FontSize = 14, BorderThickness = new Thickness(0, 0, 0, 1), Background = new SolidColorBrush(Colors.Transparent), PlaceholderText = "답글 추가..." };
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            input.Resources[key] = new SolidColorBrush(Colors.Transparent);
        input.Resources["TextControlBorderBrush"] = Theme.B(Theme.Divider);
        input.Resources["TextControlBorderBrushPointerOver"] = Theme.B(Theme.Secondary);
        input.Resources["TextControlBorderBrushFocused"] = Theme.B(Colors.White);
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { g.Visibility = Visibility.Collapsed; e.Handled = true; return; }
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            var t = input.Text.Trim();
            if (t.Length == 0) return;
            input.IsEnabled = false;
            try
            {
                if (await send(t))
                {
                    input.Text = "";
                    g.Visibility = Visibility.Collapsed;
                }
                else Toast.Show("답글을 올리지 못했어요");
            }
            catch (Exception ex)
            {
                Log.Warn($"답글 쓰기 실패: {ex.Message}");
                Toast.Show("답글을 올리지 못했어요");
            }
            finally { input.IsEnabled = true; }
        };
        g.Children.Add(input);
        return g;
    }
}
