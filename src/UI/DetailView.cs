using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 앨범 · 플레이리스트 · 아티스트 상세 화면.
/// 뒤로 가기 / 머리(아트 · 제목 · 가수 · 재생 · 셔플) / 곡 목록(유리 카드) / 아래 셸프(다른 앨범 · 싱글 · 관련 아티스트 …)
/// </summary>
public sealed class DetailView : ScrollPage
{
    private readonly string _browseId;
    public string BrowseId => _browseId;
    public string? Params { get; }
    /// <summary>앨범 · 플레이리스트 · 아티스트가 아닌 화면 (최신 음악 · 차트 · 분위기 및 장르 · 더보기) — 셸프 · 버튼만</summary>
    private bool Generic => Params != null || _browseId.StartsWith("FEmusic_");
    private readonly List<TrackRow> _rows = new();
    private readonly TextBlock _status = Theme.Text("불러오는 중…", 15, FontWeights.SemiBold, Theme.Secondary);
    private PageData? _page;
    /// <summary>100곡 넘는 목록의 나머지 (스크롤 · 재생하면서 이어 받는다 — 재생기와 함께 쓴다)</summary>
    private TrackPager? _pager;
    private StackPanel? _trackList;
    private ShelvesView.MoreSentinel? _sentinel;
    private readonly HashSet<string> _known = new();
    public event Action? BackRequested;
    /// <summary>재생목록 · 앨범 아트가 정해짐 → 창 배경을 그 아트 블러로 (아티스트 화면은 없음)</summary>
    public event Action? ArtReady;
    public string? Art { get; private set; }

    /// <summary>이 카드를 누르면 바로 재생하는 대신 상세 화면을 여는지</summary>
    public static bool Opens(Card c) =>
        c.BrowseId != null && c.VideoId == null && (c.PageType switch
        {
            "MUSIC_PAGE_TYPE_ALBUM" or "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_PLAYLIST" => true,
            null => c.BrowseId.StartsWith("MPRE") || c.BrowseId.StartsWith("UC") || c.BrowseId.StartsWith("VL"),
            _ => false,
        });

    public DetailView(string browseId, string? @params = null)
    {
        _browseId = browseId;
        Params = @params;
        var back = new IconButton(40)
        {
            Content = Theme.Icon(Icons.ChevronLeft, 24, Colors.White, stroke: true, strokeWidth: 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(Left - 12, 8, 0, 0),
        };
        Tip.Set(back, "뒤로");
        back.Click += () => BackRequested?.Invoke();
        Body.Children.Add(back);
        _status.Margin = new Thickness(Left, 24, 0, 0);
        Body.Children.Add(_status);

        var p = PlayerService.Instance;
        Loaded += (_, _) =>
        {
            p.TrackChanged += MarkCurrent;
            TrackMenu.PlaylistEdited += OnPlaylistEdited;
            TrackMenu.PlaylistDeleted += OnPlaylistDeleted;
            MarkCurrent();
        };
        Unloaded += (_, _) =>
        {
            p.TrackChanged -= MarkCurrent;
            TrackMenu.PlaylistEdited -= OnPlaylistEdited;
            TrackMenu.PlaylistDeleted -= OnPlaylistDeleted;
            if (_pager != null) _pager.Loaded -= OnMoreTracks;
        };
    }

    private FrameworkElement? _skel;

    /// <summary>스켈레톤을 치우고 상태 글자를 다시 쓸 수 있게</summary>
    private void Unskel()
    {
        if (_skel != null) Body.Children.Remove(_skel);
        _skel = null;
        _status.Visibility = Visibility.Visible;
    }

    public async void Load()
    {
        // '불러오는 중…' 대신 회색 자리 (스켈레톤)
        _status.Visibility = Visibility.Collapsed;
        _skel = Generic ? Skeleton.Shelves(Left) : Skeleton.Detail(Left, Right);
        Body.Children.Add(_skel);
        if (Generic)
        {
            LoadGeneric();
            return;
        }
        try
        {
            var page = await InnerTube.Instance.BrowsePage(_browseId);
            _page = page;
            if (page.Kind != PageKind.Artist && page.Thumb != null)
            {
                Art = page.Thumb;
                ArtReady?.Invoke();
            }
            foreach (var t in page.Tracks) _known.Add(t.VideoId);
            if (page.TracksContinuation != null)
            {
                _pager = new TrackPager(page.TracksContinuation, page.Thumb);
                _pager.Loaded += OnMoreTracks;
            }
            Body.Children.Remove(_status);
            Unskel();
            if (page.Title.Length == 0 && page.Tracks.Count == 0 && page.Shelves.Count == 0)
            {
                _status.Text = "보여 줄 내용이 없어요.";
                Body.Children.Add(_status);
                return;
            }
            if (page.Kind != PageKind.Artist)
            {
                // 앨범 · 플레이리스트: pear(YT Music) 처럼 왼쪽 가운데 정렬 머리 + 오른쪽 곡 목록 카드
                var two = BuildTwoColumn(page);
                Body.Children.Add(two);
                Anim.Appear(two);
            }
            else
            {
                var head = BuildHeader(page);
                Body.Children.Add(head);
                Anim.Appear(head);
            }
            if (page.Kind == PageKind.Artist && page.Tracks.Count > 0)
            {
                var list = BuildTracks(page);
                Body.Children.Add(list);
                Anim.Appear(list, 80);
            }
            var delay = 140;
            foreach (var s in page.Shelves)
            {
                var shelf = ShelvesView.BuildShelf(s);
                Body.Children.Add(shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 400);
            }
            Body.Children.Add(BottomSpace());
            MarkCurrent();
        }
        catch (Exception e)
        {
            Log.Error("상세 화면 불러오기 실패", e);
            Unskel();
            _status.Text = "불러오지 못했어요. 인터넷 연결을 확인해 주세요.";
        }
    }

    private async void LoadGeneric()
    {
        try
        {
            var data = await InnerTube.Instance.Home(_browseId, Params);
            Body.Children.Remove(_status);
            Unskel();
            if (data.Title != null)
            {
                var t = Theme.Text(data.Title, 32, FontWeights.ExtraBold, Theme.OnSurface);
                t.Margin = new Thickness(Left, 8, Right, 0);
                Body.Children.Add(t);
                Anim.Appear(t);
            }
            if (data.Buttons.Count > 0)
            {
                var b = ShelvesView.BuildButtons(data.Buttons);
                Body.Children.Add(b);
                Anim.Appear(b, 60);
            }
            var delay = 100;
            foreach (var s in data.Shelves)
            {
                var shelf = ShelvesView.BuildShelf(s);
                Body.Children.Add(shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 400);
            }
            if (data.Shelves.Count == 0 && data.Buttons.Count == 0)
            {
                _status.Text = "보여 줄 내용이 없어요.";
                Body.Children.Add(_status);
            }
            Body.Children.Add(BottomSpace());
        }
        catch (Exception e)
        {
            Log.Error("화면 불러오기 실패", e);
            Unskel();
            _status.Text = "불러오지 못했어요. 인터넷 연결을 확인해 주세요.";
        }
    }

    /// <summary>머리 — 왼쪽 큰 아트(아티스트는 동그라미), 오른쪽 종류 · 제목 · 가수 · 곡 수 · 설명 · 버튼</summary>
    private FrameworkElement BuildHeader(PageData page)
    {
        var artist = page.Kind == PageKind.Artist;
        var g = new Grid { Margin = new Thickness(Left, 16, Right, 8), ColumnSpacing = 32 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var size = artist ? 220 : 240;
        var art = new Cover(page.Thumb, size, size, size, artist ? size / 2.0 : 22) { VerticalAlignment = VerticalAlignment.Top };
        g.Children.Add(art);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 6, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        // 앨범 · 플레이리스트는 YT Music 부제목('앨범 • 2023', 'EP • 2024') 그대로, 아티스트는 구독자 수 앞에 '아티스트'
        var sub = artist ? (page.Subtitle == null ? "아티스트" : $"아티스트 • {page.Subtitle}")
            : page.Subtitle ?? (page.Kind == PageKind.Album ? "앨범" : "플레이리스트");
        info.Children.Add(Theme.Text(sub, 15, FontWeights.SemiBold, Theme.Secondary));
        var title = Theme.Text(page.Title, artist ? 48 : 40, FontWeights.ExtraBold, Theme.OnSurface, 2);
        title.LineHeight = artist ? 56 : 48;
        info.Children.Add(title);

        if (page.Strapline != null)
        {
            var strap = Theme.Text(page.Strapline, 18, FontWeights.Bold, Theme.OnSurface);
            if (page.StraplineId != null)
            {
                var link = new Border { Background = new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Left, Child = strap };
                link.PointerEntered += (_, _) => strap.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                link.PointerExited += (_, _) => strap.TextDecorations = Windows.UI.Text.TextDecorations.None;
                link.Tapped += (_, _) => App.MainWin?.OpenBrowse(page.StraplineId);
                info.Children.Add(link);
            }
            else info.Children.Add(strap);
        }
        if (page.SecondSubtitle != null) info.Children.Add(Theme.Text(page.SecondSubtitle, 14, FontWeights.Normal, Theme.Secondary));
        if (page.Description != null)
        {
            var d = Theme.Text(page.Description, 14, FontWeights.Normal, Theme.Secondary, 3);
            d.Margin = new Thickness(0, 4, 0, 0);
            info.Children.Add(d);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 14, 0, 0) };
        var canPlay = page.Tracks.Count > 0 || page.PlayPlaylistId != null;
        if (canPlay)
        {
            buttons.Children.Add(Pill("재생", true, () => PlayAll(false), c => Theme.Icon(Icons.Play, 20, c)));
            if (page.Tracks.Count > 1)
                buttons.Children.Add(Pill("셔플", false, () => PlayAll(true), c => Theme.Icon(Icons.Shuffle, 20, c, stroke: true)));
        }
        if (page.RadioPlaylistId is { } radio)
            buttons.Children.Add(Pill("라디오", false, () => PlayPlaylist(radio, $"{page.Title} 라디오"), c => Theme.Icon(Icons.Radio, 20, c, stroke: true)));
        if (page.ChannelId is { } channel) buttons.Children.Add(SubscribeButton(channel, page.Subscribed));
        if (page.Menu != null)
        {
            FrameworkElement? moreBtn = null;
            moreBtn = Circle(44, false, Icons.More, false, "더보기", () =>
                TrackMenu.ShowCard(moreBtn!, AsCard(page), moreBtn!.TransformToVisual(null).TransformPoint(new Point(-110, 50))));
            buttons.Children.Add(moreBtn);
        }
        if (buttons.Children.Count > 0) info.Children.Add(buttons);

        Grid.SetColumn(info, 1);
        g.Children.Add(info);
        return g;
    }

    /// <summary>
    /// 앨범 · 플레이리스트 화면 — 왼쪽 칸(아트 · 제목 32 · 만든 사람 · 정보 · 셔플 / ▶ / 링크) 가운데 정렬,
    /// 오른쪽 칸은 둥근 카드 안의 곡 목록
    /// </summary>
    private FrameworkElement BuildTwoColumn(PageData page)
    {
        var g = new Grid { Margin = new Thickness(Left, 8, Right, 0), ColumnSpacing = 48 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 760 });

        var left = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
        // 동영상 썸네일(ytimg)로 된 플레이리스트 표지는 16:9, 나머지는 정사각
        var wide = page.Thumb?.Contains("ytimg.com") == true;
        var art = new Cover(page.Thumb, 264, wide ? 148 : 264, 264, Theme.RadiusArt) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        left.Children.Add(art);
        var title = Theme.Text(page.Title, 32, FontWeights.ExtraBold, Theme.OnSurface, 2);
        title.TextAlignment = TextAlignment.Center;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        left.Children.Add(title);

        if (page.Strapline != null)
        {
            var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Colors.Transparent) };
            if (page.StraplineThumb != null)
                who.Children.Add(new Cover(page.StraplineThumb, 24, 24, 24, 12, shimmer: false));
            var name = Theme.Text(page.Strapline, 14, FontWeights.SemiBold, Theme.OnSurface);
            name.VerticalAlignment = VerticalAlignment.Center;
            who.Children.Add(name);
            if (page.StraplineId != null)
            {
                who.PointerEntered += (_, _) => name.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                who.PointerExited += (_, _) => name.TextDecorations = Windows.UI.Text.TextDecorations.None;
                who.Tapped += (_, _) => App.MainWin?.OpenBrowse(page.StraplineId);
            }
            left.Children.Add(who);
        }
        foreach (var line in new[] { page.Subtitle, page.SecondSubtitle })
        {
            if (line == null) continue;
            var t = Theme.Text(line, 14, FontWeights.Normal, Theme.Secondary);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, -4, 0, 0);
            left.Children.Add(t);
        }

        // 버튼: (보관함 저장) · 셔플 · ▶(64 흰 동그라미) · ⋮ — 자주 누르는 것만 밖에, 다운로드 · 모두 좋아요 · 공유는 ⋮ 안으로
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        // 보관함에 저장 (+ ↔ ✓) — YT 머리 버튼 그대로
        if (page.LibraryToggle is { } lib) buttons.Children.Add(LibraryButton(page, lib));
        if (page.Tracks.Count > 1) buttons.Children.Add(Circle(40, false, Icons.Shuffle, true, "셔플", () => PlayAll(true)));
        if (page.Tracks.Count > 0) buttons.Children.Add(Circle(64, true, Icons.Play, false, "재생", () => PlayAll(false)));
        FrameworkElement? moreBtn = null;
        moreBtn = Circle(40, false, Icons.More, false, "더보기", () =>
            TrackMenu.ShowCard(moreBtn!, AsCard(page), moreBtn!.TransformToVisual(null).TransformPoint(new Point(-112, 46)), PageRows(page, moreBtn!)));
        buttons.Children.Add(moreBtn);
        left.Children.Add(buttons);
        if (page.Description != null)
        {
            var d = Theme.Text(page.Description, 14, FontWeights.Normal, Theme.Secondary, 4);
            d.TextAlignment = TextAlignment.Center;
            d.Margin = new Thickness(0, 12, 0, 0);
            left.Children.Add(d);
        }
        g.Children.Add(left);

        if (page.Tracks.Count > 0)
        {
            var list = new StackPanel();
            var album = page.Kind == PageKind.Album;
            for (var i = 0; i < page.Tracks.Count; i++)
            {
                var idx = i;
                var t = page.Tracks[i];
                // 앨범은 번호, 부제는 SubtitleFor (화면이 이미 말해 주는 가수는 줄마다 되풀이하지 않는다)
                var r = new TrackRow(t, divider: i < page.Tracks.Count - 1, subtitle: SubtitleFor(page, t),
                    number: album ? i + 1 : null);
                r.Click += row => PlayFrom(page, page.Tracks, IndexOf(page, row.Track, idx));
                _rows.Add(r);
                list.Children.Add(r);
            }
            _trackList = list;
            if (_pager is { HasMore: true } pg) list.Children.Add(_sentinel = new ShelvesView.MoreSentinel(pg));
            var card = new Border
            {
                CornerRadius = new CornerRadius(Theme.RadiusCard),
                Background = Theme.B(Color.FromArgb(20, 255, 255, 255)),
                Padding = new Thickness(12, 8, 12, 8),
                Child = list,
                Margin = new Thickness(0, 24, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(card, 1);
            g.Children.Add(card);
        }
        return g;
    }

    /// <summary>머리 ⋮ 에만 들어가는 줄 — 목록 전체 다운로드 · 오프라인 저장 · 모두 좋아요 (공유는 카드 메뉴에 이미 있다)</summary>
    private List<(int, string, string, Action, bool)> PageRows(PageData page, FrameworkElement at)
    {
        var rows = new List<(int, string, string, Action, bool)>();
        if (page.Tracks.Count > 0)
        {
            rows.Add((32, "오프라인 저장", Icons.Download, () => TrackMenu.SaveOffline(page.Tracks), false));
            rows.Add((33, "다운로드", Icons.Download, () => TrackMenu.DownloadAll(page.Tracks), false));
        }
        if (page.Tracks.Count > 1) rows.Add((31, "모두 좋아요 · 싫어요", Icons.ThumbUp, () => RateAllMenu(at, page), true));
        return rows;
    }

    /// <summary>pear album-actions — 이 목록의 곡 전부에 좋아요 · 싫어요를 누르거나 취소한다</summary>
    private static void RateAllMenu(FrameworkElement at, PageData page)
    {
        var m = new OneMenu(width: 280);
        m.Header($"곡 {page.Tracks.Count}개 모두");
        m.Item("모두 좋아요", Icons.ThumbUp, () => RateAll(page, true, "좋아요"));
        m.Item("모두 좋아요 취소", Icons.ThumbUp, () => RateAll(page, null, "좋아요 취소"));
        m.Item("모두 싫어요", Icons.ThumbDown, () => RateAll(page, false, "싫어요"));
        m.Item("모두 싫어요 취소", Icons.ThumbDown, () => RateAll(page, null, "싫어요 취소"));
        m.Show(at);
    }

    private static async void RateAll(PageData page, bool? like, string what)
    {
        if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
        var list = page.Tracks.ToList();
        Toast.Show($"{list.Count}곡 {what} 중…");
        var ok = 0;
        foreach (var t in list)
        {
            try { if (await InnerTube.Instance.Rate(t.VideoId, like)) ok++; }
            catch (Exception e) { Log.Warn($"모두 {what} 실패: {t.Title} {e.Message}"); }
            await Task.Delay(120); // 너무 빨리 보내면 YT 가 막는다
        }
        Toast.Show(ok == list.Count ? $"{ok}곡 모두 {what} 했어요" : $"{ok}/{list.Count}곡 {what} 했어요");
    }

    /// <summary>이 화면을 카드처럼 (⋮ 메뉴 · 공유 · 재생에 쓴다)</summary>
    private Card AsCard(PageData page) =>
        new(page.Title, page.Subtitle ?? "", page.Thumb, false, null, page.AudioPlaylistId, _browseId,
            page.Kind == PageKind.Album ? "MUSIC_PAGE_TYPE_ALBUM" : page.Kind == PageKind.Artist ? "MUSIC_PAGE_TYPE_ARTIST" : "MUSIC_PAGE_TYPE_PLAYLIST")
        { Menu = page.Menu };

    /// <summary>'보관함에 저장' 동그라미 — 누르면 YT 동작을 보내고 + ↔ ✓ 로 바뀐다</summary>
    private FrameworkElement LibraryButton(PageData page, YtMenuItem lib)
    {
        var on = lib.Toggled;
        var b = new IconButton(40);
        void Draw()
        {
            var g = new Grid();
            g.Children.Add(new XEllipse { Width = 40, Height = 40, Fill = Theme.B(Theme.Chip) });
            g.Children.Add(Theme.Icon(on ? Icons.LibrarySaved : Icons.LibraryAdd, 20, Colors.White, stroke: true, strokeWidth: 2));
            b.Content = g;
        }
        Draw();
        Tip.Set(b, on ? (lib.ToggledText ?? "보관함에서 삭제") : lib.Text);
        b.Click += async () =>
        {
            var ep = on ? lib.ToggledEndpoint ?? lib.Endpoint : lib.Endpoint;
            var ok = await TrackMenu.RunEndpoint(ep, on ? "보관함에서 삭제" : "보관함에 추가", b, AsCard(page));
            if (!ok) return;
            on = !on;
            Draw();
            Anim.V(b.Content!).Scale = new Vector3(0.7f, 0.7f, 1);
            Anim.Scale(b.Content!, 1f);
        };
        return b;
    }

    private static FrameworkElement Circle(double size, bool primary, string icon, bool stroke, string tip, Action click)
    {
        var b = new IconButton(size);
        var grid = new Grid();
        grid.Children.Add(new XEllipse { Width = size, Height = size, Fill = Theme.B(primary ? Colors.White : Theme.Chip) });
        grid.Children.Add(Theme.Icon(icon, primary ? 30 : 20, primary ? Colors.Black : Colors.White, stroke: stroke, strokeWidth: 2));
        b.Content = grid;
        if (primary) b.HoverBrush = new SolidColorBrush(Colors.Transparent);
        Tip.Set(b, tip);
        b.Click += click;
        return b;
    }

    /// <summary>구독 알약 — 구독 전엔 흰 바탕, 구독 중이면 반투명 칩 (YT Music 아티스트 머리)</summary>
    private FrameworkElement SubscribeButton(string channelId, bool subscribed)
    {
        var holder = new Grid();
        void Draw()
        {
            holder.Children.Clear();
            holder.Children.Add(Pill(subscribed ? "구독중" : "구독", !subscribed, async () =>
            {
                if (!Auth.IsLoggedIn) { Toast.Show("로그인이 필요해요"); return; }
                var ok = await InnerTube.Instance.Subscribe(channelId, !subscribed);
                if (!ok) { Toast.Show("하지 못했어요"); return; }
                subscribed = !subscribed;
                Toast.Show(subscribed ? "구독했어요" : "구독을 취소했어요");
                Draw();
            }, c => Theme.Icon(subscribed ? Icons.LibrarySaved : Icons.Person, 20, c, stroke: true)));
        }
        Draw();
        return holder;
    }

    /// <summary>알약 버튼 44px — 주 버튼은 흰 바탕 검은 글씨, 나머지는 반투명 칩</summary>
    private static FrameworkElement Pill(string text, bool primary, Action click, Func<Color, FrameworkElement> icon)
    {
        var fg = primary ? Colors.Black : Colors.White;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon(fg));
        var label = Theme.Text(text, 15, FontWeights.Bold, fg);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var idle = primary ? Colors.White : Theme.Chip;
        var hover = primary ? Color.FromArgb(255, 230, 230, 232) : Theme.ChipHover;
        var b = new Border
        {
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(20, 0, 24, 0),
            Background = Theme.B(idle),
            Child = row,
        };
        Anim.FadeBg(b);
        b.PointerEntered += (_, _) => b.Background = Theme.B(hover);
        b.PointerExited += (_, _) => b.Background = Theme.B(idle);
        Anim.Pressable(b);
        b.Tapped += (_, _) => click();
        return b;
    }

    /// <summary>곡 목록 — 제목(아티스트 페이지의 '노래' · 모두 보기) + 유리 카드 안의 줄들</summary>
    private FrameworkElement BuildTracks(PageData page)
    {
        var box = new StackPanel { Margin = new Thickness(Left, 24, Right, 0), Spacing = 12, MaxWidth = 1100 + Left, HorizontalAlignment = HorizontalAlignment.Left };
        if (page.Kind == PageKind.Artist)
        {
            var head = new Grid();
            head.Children.Add(Theme.Text(page.TracksTitle ?? "노래", 22, FontWeights.ExtraBold, Theme.OnSurface));
            if (page.TracksMoreId is { } more)
            {
                var all = new Border
                {
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(14, 0, 14, 0),
                    Background = Theme.B(Theme.Chip),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Child = Theme.Text("모두 보기", 14, FontWeights.SemiBold, Theme.OnSurface),
                };
                ((TextBlock)all.Child).VerticalAlignment = VerticalAlignment.Center;
                Anim.FadeBg(all);
                all.PointerEntered += (_, _) => all.Background = Theme.B(Theme.ChipHover);
                all.PointerExited += (_, _) => all.Background = Theme.B(Theme.Chip);
                Anim.Pressable(all);
                all.Tapped += (_, _) => App.MainWin?.OpenBrowse(more);
                head.Children.Add(all);
            }
            box.Children.Add(head);
        }
        var list = new StackPanel();
        for (var i = 0; i < page.Tracks.Count; i++)
        {
            var idx = i;
            var r = new TrackRow(page.Tracks[i], divider: i < page.Tracks.Count - 1, subtitle: SubtitleFor(page, page.Tracks[i]));
            r.Click += row => PlayFrom(page, page.Tracks, IndexOf(page, row.Track, idx));
            _rows.Add(r);
            list.Children.Add(r);
        }
        box.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Theme.RadiusCard),
            Background = Theme.B(Color.FromArgb(14, 255, 255, 255)),
            Padding = new Thickness(8),
            Child = list,
        });
        return box;
    }

    private void PlayAll(bool shuffle)
    {
        var page = _page;
        if (page == null) return;
        // 아티스트 '재생' 은 인기곡 몇 개가 아니라 YT Music 이 주는 그 아티스트 곡 목록 전체
        if (!shuffle && page.Kind == PageKind.Artist && page.PlayPlaylistId != null)
        {
            PlayPlaylist(page.PlayPlaylistId, page.Title);
            return;
        }
        if (page.Tracks.Count == 0) return;
        var list = shuffle ? page.Tracks.OrderBy(_ => Random.Shared.Next()).ToList() : page.Tracks;
        PlayFrom(page, list, 0, shuffle);
    }

    /// <summary>
    /// 앨범 · 플레이리스트를 튼다 — 끝나면 YT Music 처럼 그 목록의 자동 믹스(RDAMPL + 목록 ID)가 이어진다
    /// (아티스트 인기곡 목록은 자동 믹스 대신 마지막 곡 라디오)
    /// </summary>
    private void PlayFrom(PageData page, List<Track> list, int index, bool shuffled = false)
    {
        var automix = page.Kind != PageKind.Artist && page.AudioPlaylistId is { } pl ? "RDAMPL" + pl : null;
        _ = PlayerService.Instance.PlayList(list, index, page.Title, null, automix, null, _pager, shuffled);
    }

    /// <summary>곡의 지금 자리 (곡을 빼서 자리가 밀렸을 수 있어서 처음 자리 대신 찾는다)</summary>
    private static int IndexOf(PageData page, Track t, int hint) =>
        hint < page.Tracks.Count && page.Tracks[hint].VideoId == t.VideoId ? hint : Math.Max(0, page.Tracks.FindIndex(x => x.VideoId == t.VideoId));

    private bool Shows(string playlistId) =>
        _page?.AudioPlaylistId == playlistId || _browseId == "VL" + playlistId;

    /// <summary>이 재생목록에서 곡을 뺐으면 그 줄을 접으며 지운다</summary>
    private async void OnPlaylistEdited(string playlistId, string? removedVideoId)
    {
        if (removedVideoId == null || !Shows(playlistId) || _page is not { } page) return;
        var row = _rows.FirstOrDefault(r => r.Track.VideoId == removedVideoId);
        if (row == null) return;
        _rows.Remove(row);
        page.Tracks.RemoveAll(t => t.VideoId == removedVideoId);
        _known.Remove(removedVideoId);
        Anim.Opacity(row, 0, Anim.Fast);
        await Task.Delay(Anim.Fast);
        (row.Parent as Panel)?.Children.Remove(row);
        if (_rows.Count > 0) _rows[^1].Divider = false;
    }

    private void OnPlaylistDeleted(string playlistId)
    {
        if (Shows(playlistId)) BackRequested?.Invoke();
    }

    /// <summary>나머지 묶음이 오면 곡 목록 카드 끝에 줄을 붙인다 (앨범은 번호 이어서)</summary>
    /// <summary>
    /// 곡 줄 부제 — 화면 머리에 이미 있는 가수는 빼고 다른 정보만: 앨범 화면은 앨범 가수와 같으면 비움,
    /// 아티스트 화면은 앨범 이름, 플레이리스트는 '가수 • 앨범'
    /// </summary>
    private static string? SubtitleFor(PageData page, Track t) => page.Kind switch
    {
        PageKind.Album => t.Artist == page.Strapline ? "" : null,
        PageKind.Artist => t.Album ?? (t.Artist == page.Title ? "" : null),
        _ => t.Album == null ? null : $"{t.Artist} • {t.Album}",
    };

    private void OnMoreTracks(List<Track> add)
    {
        if (_page is not { } page || _trackList == null) return;
        var album = page.Kind == PageKind.Album;
        foreach (var t in add)
        {
            if (!_known.Add(t.VideoId)) continue;
            if (_rows.Count > 0) _rows[^1].Divider = true;
            page.Tracks.Add(t);
            var idx = page.Tracks.Count - 1;
            var r = new TrackRow(t, divider: false, subtitle: SubtitleFor(page, t),
                number: album ? idx + 1 : null);
            r.Click += row => PlayFrom(page, page.Tracks, IndexOf(page, row.Track, idx));
            _rows.Add(r);
            var at = _sentinel != null && _trackList.Children.Contains(_sentinel) ? _trackList.Children.Count - 1 : _trackList.Children.Count;
            _trackList.Children.Insert(at, r);
        }
        if (_pager?.HasMore != true && _sentinel != null) _trackList.Children.Remove(_sentinel);
        MarkCurrent();
    }

    private static async void PlayPlaylist(string playlistId, string title)
    {
        try
        {
            var q = await InnerTube.Instance.GetQueue(null, playlistId);
            if (q.Tracks.Count > 0) await PlayerService.Instance.PlayList(q.Tracks, 0, q.Title ?? title, q.RelatedBrowseId, q.AutomixPlaylistId, q.AutomixParams);
        }
        catch (Exception e) { Log.Error("목록 재생 실패", e); }
    }

    private void MarkCurrent()
    {
        var id = PlayerService.Instance.Current?.VideoId;
        foreach (var r in _rows) r.SetCurrent(r.Track.VideoId == id);
    }
}
