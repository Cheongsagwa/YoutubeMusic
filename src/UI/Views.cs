using Microsoft.UI.Input;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>카드 · 곡을 눌렀을 때 무엇을 틀지</summary>
public static class Play
{
    public static async void Card(Card c, Shelf? shelf = null)
    {
        try
        {
            var p = PlayerService.Instance;
            if (DetailView.Opens(c))
            {
                App.MainWin?.OpenBrowse(c.BrowseId!);
                return;
            }
            if (c.VideoId != null && c.PlaylistId == null)
            {
                var t = shelf?.Tracks?.FirstOrDefault(x => x.VideoId == c.VideoId)
                        ?? new Track(c.VideoId, c.Title, InnerTube.StripType(c.Subtitle).Split(" • ")[0], null, c.Thumb, 0);
                await p.PlaySong(t);
                return;
            }
            // 믹스 · 라디오(RD…)는 끝없는 라디오로 — 칩이 목록 위, 자동재생 토글 없음 (YT Music 과 같게)
            if (c.PlaylistId?.StartsWith("RD") == true)
            {
                await p.PlayRadio(c.VideoId, c.PlaylistId, null, c.Title);
                return;
            }
            if (c.PlaylistId != null)
            {
                var q = await InnerTube.Instance.GetQueue(c.VideoId, c.PlaylistId);
                if (q.Tracks.Count > 0) await p.PlayList(q.Tracks, 0, q.Title ?? c.Title, q.RelatedBrowseId, q.AutomixPlaylistId, q.AutomixParams);
                return;
            }
            if (c.BrowseId != null)
            {
                var list = await InnerTube.Instance.BrowseTracks(c.BrowseId);
                if (list.Count > 0) await p.PlayList(list, 0, c.Title);
            }
        }
        catch (Exception e) { Log.Error("카드 재생 실패", e); }
    }
}

/// <summary>세로로 스크롤하는 화면의 공통 틀 — 스크롤바 숨김, 위 · 아래 끝 그라데이션 (pear 테마: 위 44 · 아래 56)</summary>
public class ScrollPage : Grid
{
    /// <summary>화면은 창 맨 위부터 그려지고, 이만큼 밑에서 내용이 시작한다 (창 끌기 32 + 상단 바 90)</summary>
    public const double TopInset = 122;

    protected readonly ScrollViewer Scroller = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Disabled,
    };

    /// <summary>스크롤되어 있는지 바뀌면 (상단 바 유리를 켜고 끈다)</summary>
    public static event Action<ScrollPage, bool>? ScrolledChanged;
    public bool IsScrolled => _topShown;
    protected readonly StackPanel Body = new();
    private bool _topShown;

    /// <summary>내용 왼쪽 · 오른쪽 여백 — 홈 · 둘러보기 · 보관함 · 검색 · 상세 화면 모두 같게 (너무 붙으면 답답해서 다시 넓힘)</summary>
    public const double Left = 48, Right = 48;

    public ScrollPage()
    {
        // 위 여백은 스크롤 상자가 아니라 내용 쪽에 — 스크롤 상자에 여백을 주면 그 위로 내용이 잘려서
        // 상단 바 뒤(유리 아래)로 내용이 흘러 들어가지 않았다
        Scroller.Content = new Border { Padding = new Thickness(0, TopInset, 0, 0), Child = Body };
        Children.Add(Scroller);
        // 아래 끝은 픽셀 마스크로 흐린다 (재생 바 높이 + 여백 + 40) — 내용이 앨범아트 배경 속으로 사라진다.
        // 위는 흐리지 않는다 — 내용이 상단 바 유리 밑으로 흘러 들어가며 흐리게 비치는 것으로 대신
        EdgeMask.Attach(Scroller, 0, Theme.BarHeight + Theme.FloatGap * 2 + 40);
        Scroller.ViewChanged += (_, _) => UpdateScrolled();
    }

    private void UpdateScrolled()
    {
        var top = Scroller.VerticalOffset > 1;
        if (top == _topShown) return;
        _topShown = top;
        ScrolledChanged?.Invoke(this, top);
    }

    protected static Border BottomSpace() => new() { Height = Theme.BarHeight + Theme.FloatGap * 2 + 24 };

    /// <summary>맨 위로 부드럽게 (지금 탭을 다시 누르면 — YT Music 과 같게)</summary>
    public void ScrollToTop() => Scroller.ChangeView(null, 0, null, false);
    public void ScrollTo(double y) => Scroller.ChangeView(null, y, null, true);
}

/// <summary>셸프 화면 (홈 · 둘러보기 · 보관함) — pear 의 One UI 셸프 모양 그대로</summary>
public sealed class ShelvesView : ScrollPage
{
    private readonly string _browseId;
    private readonly bool _withChips;
    private bool _loaded;
    private string? _chipParams;
    /// <summary>보관함 칩(재생목록 · 노래 · 앨범 …)은 다른 화면(browseId)으로 간다</summary>
    private string? _chipBrowse;
    private readonly TextBlock _status = Theme.Text("불러오는 중…", 15, FontWeights.SemiBold, Theme.Secondary);

    private string? _more;
    private bool _loadingMore;
    private Border? _bottom;
    private int _gen;

    private bool IsLibrary => _browseId == "FEmusic_library_landing";
    /// <summary>보관함 위 One UI 알약 탭 — 0 보관함 · 1 기록 · 2 오프라인 저장</summary>
    private PillTabs? _libTabs;
    private int _libTab;
    private const int HistoryTab = 1, OfflineTab = 2;

    /// <summary>내 이름 — 셸프 머리 · 카드 부제에서 나 자신을 가리키는 반복 글씨를 뺄 때 쓴다</summary>
    internal static string Me = "";

    public ShelvesView(string browseId, bool withChips)
    {
        _browseId = browseId;
        _withChips = withChips;
        if (IsLibrary)
        {
            _libTabs = new PillTabs("보관함", "기록", "오프라인 저장") { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(Left, 16, 0, 4) };
            _libTabs.Changed += async i =>
            {
                _libTab = i;
                // 알약이 미끄러진 뒤에 내용을 바꾼다
                await Task.Delay(200);
                Reload();
            };
            Offline.Instance.Changed += () => DispatcherQueue.TryEnqueue(() => { if (_libTab == OfflineTab && _loaded) Reload(); });
        }
        Body.Padding = new Thickness(0, 0, 0, 0);
        // 끝 가까이 내리면 다음 셸프들을 이어서 (YT Music 홈은 몇 개씩 나눠 준다)
        Scroller.ViewChanged += (_, _) =>
        {
            if (Scroller.VerticalOffset > Scroller.ScrollableHeight - 900) LoadMore();
        };
    }

    private async void LoadMore()
    {
        if (_more == null || _loadingMore || _bottom == null) return;
        _loadingMore = true;
        var gen = _gen;
        var token = _more;
        try
        {
            var data = await InnerTube.Instance.HomeMore(token);
            if (gen != _gen) return;
            _more = data.Continuation;
            var at = Body.Children.IndexOf(_bottom);
            var delay = 0;
            foreach (var s in data.Shelves)
            {
                var shelf = BuildShelf(s);
                Body.Children.Insert(at++, shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 300);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"셸프 이어 받기 실패: {e.Message}");
            _more = null;
        }
        finally { _loadingMore = false; }
        // 화면이 아직 안 찼으면 한 번 더
        if (gen == _gen && Scroller.ScrollableHeight < 900) LoadMore();
    }

    public void Reload()
    {
        _loaded = false;
        LoadOnce();
    }

    private FrameworkElement LoginCard()
    {
        var card = new StackPanel { Spacing = 14, Padding = new Thickness(28) };
        var border = new Border { CornerRadius = new CornerRadius(Theme.RadiusCard), Background = Theme.Glass(0.4), Child = card, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 520, Margin = new Thickness(Left, 32, 0, 0) };
        card.Children.Add(Theme.Text("YouTube Music 로그인", 22, FontWeights.ExtraBold, Theme.OnSurface));
        card.Children.Add(Theme.Text("한국에서는 로그인해야 YouTube Music 을 쓸 수 있어요. 구글 계정으로 로그인하면 홈 추천 · 검색 · 재생이 열려요.", 15, FontWeights.Normal, Theme.Secondary, 3));
        var btn = new Border { CornerRadius = new CornerRadius(22), Background = Theme.B(Colors.White), Padding = new Thickness(24, 12, 24, 12), HorizontalAlignment = HorizontalAlignment.Left };
        btn.Child = Theme.Text("구글 계정으로 로그인", 15, FontWeights.Bold, Colors.Black);
        Anim.Pressable(btn);
        btn.Tapped += async (_, _) =>
        {
            var w = new LoginWindow();
            w.Activate();
            if (await w.Result) App.MainWin?.ReloadAll();
        };
        card.Children.Add(btn);
        return border;
    }

    public async void LoadOnce()
    {
        if (_loaded) return;
        _loaded = true;
        _gen++;
        _more = null;
        _bottom = null;
        Body.Children.Clear();
        if (_libTabs != null) Body.Children.Add(_libTabs);
        if (IsLibrary && _libTab == OfflineTab)
        {
            ShowOffline();
            return;
        }
        var history = IsLibrary && _libTab == HistoryTab;
        // 계정 이름은 셸프와 같이 받는다 (이미 받았으면 바로 끝난다)
        var me = InnerTube.Instance.MyName();
        if (!Auth.IsLoggedIn)
        {
            var lc = LoginCard();
            Body.Children.Add(lc);
            Anim.Appear(lc);
            _loaded = false;
            return;
        }
        _status.Text = "불러오는 중…";
        _status.Margin = new Thickness(Left, 32, 0, 0);
        // '불러오는 중…' 글자 대신 회색 셸프 자리 (스켈레톤) — 내용이 오면 진짜 셸프로 바뀐다
        var skel = Skeleton.Shelves(Left);
        Body.Children.Add(skel);
        try
        {
            List<Shelf> shelves;
            List<Chip> chips = new();
            List<NavButton> buttons = new();
            if (_withChips && !history)
            {
                var home = await InnerTube.Instance.Home(_chipBrowse ?? _browseId, _chipParams);
                shelves = home.Shelves;
                chips = home.Chips;
                buttons = home.Buttons;
                _more = home.Continuation;
            }
            else shelves = await InnerTube.Instance.BrowseShelves(history ? "FEmusic_history" : _browseId);
            Me = await me;
            Body.Children.Clear();
            if (_libTabs != null) Body.Children.Add(_libTabs);
            if (chips.Count > 0)
            {
                var chipRow = BuildChips(chips);
                if (_browseId == "FEmusic_library_landing")
                {
                    // 보관함: 오른쪽에 정렬 (최근 활동 · 가나다순 · 가나다 역순)
                    var row = new Grid();
                    row.Children.Add(chipRow);
                    var sort = SortPill();
                    sort.HorizontalAlignment = HorizontalAlignment.Right;
                    sort.VerticalAlignment = VerticalAlignment.Center;
                    sort.Margin = new Thickness(0, 12, Right, 0);
                    row.Children.Add(sort);
                    Body.Children.Add(row);
                }
                else Body.Children.Add(chipRow);
            }
            if (_sort != 0)
                shelves = shelves.Select(sh => sh.Grid ? sh with { Items = _sort == 1 ? sh.Items.OrderBy(c => c.Title, StringComparer.CurrentCulture).ToList() : sh.Items.OrderByDescending(c => c.Title, StringComparer.CurrentCulture).ToList() } : sh).ToList();
            if (buttons.Count > 0) Body.Children.Add(BuildButtons(buttons));
            if (shelves.Count == 0 && buttons.Count == 0)
            {
                _status.Text = "보여 줄 내용이 없어요.";
                Body.Children.Add(_status);
                _loaded = false;
                return;
            }
            var delay = 0;
            foreach (var s in shelves)
            {
                var shelf = BuildShelf(s);
                Body.Children.Add(shelf);
                Anim.Appear(shelf, delay);
                delay = Math.Min(delay + 60, 360);
            }
            Body.Children.Add(_bottom = BottomSpace());
            DispatcherQueue.TryEnqueue(LoadMore);
        }
        catch (Exception e)
        {
            Log.Error("셸프 불러오기 실패", e);
            Body.Children.Remove(skel);
            if (!Body.Children.Contains(_status)) Body.Children.Add(_status);
            _status.Text = "불러오지 못했어요. 인터넷 연결을 확인해 주세요.";
            _loaded = false;
        }
    }

    /// <summary>개발용: n 번째 가로 줄을 › 한 번 넘긴다</summary>
    public void PageShelf(int n)
    {
        var svs = new List<ScrollViewer>();
        void Walk(DependencyObject o)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var c = VisualTreeHelper.GetChild(o, i);
                if (c is ScrollViewer { HorizontalScrollBarVisibility: ScrollBarVisibility.Hidden, VerticalScrollMode: ScrollMode.Disabled } h) svs.Add(h);
                else Walk(c);
            }
        }
        Walk(Body);
        if (n < svs.Count) svs[n].ChangeView(SnapTarget(svs[n], svs[n].HorizontalOffset + svs[n].ViewportWidth * 0.8), null, null);
    }

    /// <summary>개발용: 보관함 탭 고르기</summary>
    public void SelectLibTab(int i) => _libTabs?.Select(i);

    /// <summary>보관함 '오프라인 저장' 탭 — 이 PC 에 저장한 곡 (인터넷 없이 재생)</summary>
    private void ShowOffline()
    {
        var saved = Offline.Instance.Tracks;
        if (saved.Count == 0)
        {
            var empty = Theme.Text("오프라인 저장한 곡이 없어요. 곡의 ⋮ 메뉴에서 '오프라인 저장'을 누르면 여기에 모여요.", 15, FontWeights.SemiBold, Theme.Secondary, 3);
            empty.Margin = new Thickness(Left, 32, Right, 0);
            Body.Children.Add(empty);
            Anim.Appear(empty);
            return;
        }
        // 탭 이름이 이미 '오프라인 저장' — 셸프 머리는 두지 않는다
        var shelf = BuildShelf(new Shelf("", saved.Select(t => new Card(t.Title, t.Artist, t.Thumb, false, t.VideoId, null, null)).ToList(), saved.ToList()) { List = true });
        Body.Children.Add(shelf);
        Anim.Appear(shelf);
        Body.Children.Add(_bottom = BottomSpace());
    }

    private int _sort;
    private static readonly string[] SortNames = ["최근 활동", "가나다순", "가나다 역순"];

    /// <summary>보관함 정렬 알약 — 'Recent activity ⌄' 처럼 테두리 알약, 누르면 메뉴</summary>
    private FrameworkElement SortPill()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var label = Theme.Text(SortNames[_sort], 14, FontWeights.SemiBold, Theme.OnSurface);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        row.Children.Add(Theme.Icon(Icons.ChevronDown, 16, Colors.White, stroke: true, strokeWidth: 2));
        var pill = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 0, 12, 0),
            Background = Theme.B(Theme.Chip),
            Child = row,
        };
        Anim.FadeBg(pill);
        pill.PointerEntered += (_, _) => pill.Background = Theme.B(Theme.Selected);
        pill.PointerExited += (_, _) => pill.Background = Theme.B(Theme.Chip);
        Anim.Pressable(pill);
        pill.Tapped += (_, _) =>
        {
            var m = new OneMenu();
            for (var i = 0; i < SortNames.Length; i++)
            {
                var k = i;
                m.Item(SortNames[i], Icons.Stats, () => { _sort = k; Reload(); }, check: i == _sort);
            }
            m.Show(pill);
        };
        return pill;
    }

    /// <summary>
    /// 큰 이동 버튼 — 둘러보기 맨 위 3개(최신 음악 · 차트 · 분위기 및 장르)는 한 줄에 같은 폭으로,
    /// 분위기 및 장르 화면의 많은 버튼은 왼쪽 색 띠가 있는 격자로
    /// </summary>
    internal static FrameworkElement BuildButtons(List<NavButton> buttons)
    {
        if (buttons.Count <= 4)
        {
            var g = new Grid { Margin = new Thickness(Left, 24, Right, 8), ColumnSpacing = 24 };
            for (var i = 0; i < buttons.Count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var b = NavButtonView(buttons[i], big: true);
                Grid.SetColumn(b, i);
                g.Children.Add(b);
            }
            return g;
        }
        var wrap = new WrapPanel { Margin = new Thickness(Left, 24, Right, 8), HSpacing = 16, VSpacing = 16 };
        foreach (var b in buttons) wrap.Children.Add(NavButtonView(b, big: false));
        return wrap;
    }

    private static FrameworkElement NavButtonView(NavButton nb, bool big)
    {
        var g = new Grid { Height = big ? 56 : 48, Width = big ? double.NaN : 196, CornerRadius = new CornerRadius(big ? 12 : 8), Background = Theme.B(Theme.Chip) };
        if (nb.Stripe is { } c)
            g.Children.Add(new XRect { Width = 6, Fill = Theme.B(c), HorizontalAlignment = HorizontalAlignment.Left });
        var t = Theme.Text(nb.Text, big ? 22 : 15, FontWeights.Bold, Theme.OnSurface);
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(big ? 24 : 18, 0, 12, 0);
        g.Children.Add(t);
        Anim.FadeBg(g);
        g.PointerEntered += (_, _) => g.Background = Theme.B(Theme.ChipHover);
        g.PointerExited += (_, _) => g.Background = Theme.B(Theme.Chip);
        Anim.Pressable(g);
        g.Tapped += (_, _) => App.MainWin?.OpenBrowse(nb.BrowseId, nb.Params);
        return g;
    }

    /// <summary>홈 맨 위 분위기 칩 — 36px 알약, 고르면 흰 칩</summary>
    private FrameworkElement BuildChips(List<Chip> chips)
    {
        var sv = HorizontalRow(out var row);
        row.Spacing = 12;
        // 스크롤 칸 자체를 좌우 여백 안쪽으로 (넘겨도 내용이 여백으로 밀려 나가지 않게)
        sv.Margin = new Thickness(Left, 0, Right, 0);
        row.Padding = new Thickness(0, 24, 0, 12);
        foreach (var c in chips)
        {
            var selected = c.Selected || (c.Params != null && c.Params == _chipParams)
                           || (_chipBrowse != null && c.BrowseId == _chipBrowse && c.Params == _chipParams);
            var chip = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 0, 16, 0),
                Background = Theme.B(selected ? Colors.White : Theme.Chip),
                Child = Theme.Text(c.Text, 14, FontWeights.SemiBold, selected ? Colors.Black : Theme.OnSurface),
            };
            ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
            Anim.FadeBg(chip);
            chip.PointerEntered += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.ChipHover); };
            chip.PointerExited += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.Chip); };
            Anim.Pressable(chip);
            chip.Tapped += (_, _) =>
            {
                _chipParams = selected ? null : c.Params;
                _chipBrowse = selected || c.BrowseId == _browseId ? null : c.BrowseId;
                Reload();
            };
            row.Children.Add(chip);
        }
        return sv;
    }

    /// <param name="pixelFade">메인 화면 줄은 픽셀 마스크(EdgeMask). 재생 화면 패널 안은 이미 EdgeMask 목록 안이라 겹치지 않게 옛 방식(ListFade)</param>
    internal static ScrollViewer HorizontalRow(out StackPanel row, bool pixelFade = true)
    {
        var sv = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            // 휠이 가로 목록에 먹히지 않게 사용자 스크롤은 끄고, 넘기기는 ChangeView 로만
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        row = new StackPanel { Orientation = Orientation.Horizontal };
        sv.Content = row;
        // 더 넘길 내용이 있는 쪽 끝을 흐린다 (pear: 메인 가로 목록 56px)
        if (pixelFade) EdgeMask.Attach(sv, 40, 40, horizontal: true);
        else ListFade.Attach(sv, horizontal: true, start: 56, end: 56);
        // 마우스 휠은 가로로 넘기지 않고 화면을 위아래로 (가로는 ‹ › 버튼으로)
        return sv;
    }

    internal static FrameworkElement BuildShelf(Shelf s)
    {
        var box = new StackPanel { Padding = new Thickness(0, 32, 0, 0) };
        // 머리: (프로필 사진) + 작은 글씨 + 제목 22px ExtraBold
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Left, 0, 0, 16), Spacing = 16 };
        // 작은 글씨가 내 이름이면 (다시 듣기 등) 나를 다시 알려 줄 뿐 — 이름 · 내 사진 둘 다 뺀다
        var mine = s.Strapline != null && Me.Length > 0 && s.Strapline == Me;
        if (s.Avatar != null && !mine)
        {
            var av = new Cover(s.Avatar, 56, 56, 56, 28, shimmer: false);
            head.Children.Add(av);
        }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        if (s.Strapline != null && !mine) titles.Children.Add(Theme.Text(s.Strapline, 14, FontWeights.Normal, Theme.Secondary));
        // 더 볼 수 있는 셸프는 제목 자체가 링크 (› 하나로 표시) — 따로 '더보기' 알약을 두지 않는다
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        titleLine.Children.Add(Theme.Text(s.Title, 22, FontWeights.ExtraBold, Theme.OnSurface));
        if (s.MoreBrowseId is { } moreId && !s.List && !s.Grid)
        {
            var chev = Theme.Icon(Icons.ChevronRight, 22, Colors.White, stroke: true, strokeWidth: 2.4);
            chev.VerticalAlignment = VerticalAlignment.Center;
            chev.Margin = new Thickness(0, 2, 0, 0);
            titleLine.Children.Add(chev);
            titleLine.Background = new SolidColorBrush(Colors.Transparent);
            Anim.Pressable(titleLine);
            Tip.Set(titleLine, "모두 보기");
            titleLine.Tapped += (_, _) => App.MainWin?.OpenBrowse(moreId, s.MoreParams);
        }
        titles.Children.Add(titleLine);
        head.Children.Add(titles);
        if (s.List && s.Tracks is { Count: > 0 } listTracks)
        {
            // 세로 곡 목록 (기록 · 보관함 노래): 둥근 카드 안의 줄들 (pear 14 · 48)
            if (s.Title.Length > 0) box.Children.Add(head);
            var rows = new StackPanel();
            TrackRow? last = null;
            var known = new HashSet<string>();
            void AddRows(IEnumerable<Track> tracks)
            {
                foreach (var t in tracks)
                {
                    if (!known.Add(t.VideoId)) continue;
                    if (last != null) last.Divider = true;
                    var tr = new TrackRow(t, divider: false, subtitle: t.Album != null ? $"{t.Artist} • {t.Album}" : null);
                    tr.Click += r => _ = PlayerService.Instance.PlaySong(r.Track);
                    rows.Children.Insert(rows.Children.Count - (rows.Children.LastOrDefault() is MoreSentinel ? 1 : 0), tr);
                    last = tr;
                }
            }
            AddRows(listTracks);
            // 더 있으면 끝에 다다를 때 이어 받는다 (YT Music 보관함 노래 · 기록처럼 스크롤로)
            if (s.ListContinuation != null)
            {
                var pager = new TrackPager(s.ListContinuation);
                var sentinel = new MoreSentinel(pager);
                rows.Children.Add(sentinel);
                pager.Loaded += add =>
                {
                    AddRows(add);
                    if (!pager.HasMore) rows.Children.Remove(sentinel);
                };
            }
            box.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(Theme.RadiusCard),
                Background = Theme.B(Color.FromArgb(14, 255, 255, 255)),
                Padding = new Thickness(8),
                Child = rows,
                Margin = new Thickness(Left, 0, Right, 0),
                MaxWidth = 1100,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            return box;
        }
        if (s.Grid && s.Buttons is { } gridButtons)
        {
            // 분위기 및 장르 화면의 구역(맞춤 · 분위기 및 상황 · 장르): 4칸 격자, 칸 폭은 화면에 맞춰 늘어난다 (pear)
            if (s.Title.Length > 0) box.Children.Add(head);
            var grid = new Grid { Margin = new Thickness(Left, 0, Right, 8), ColumnSpacing = 16, RowSpacing = 16 };
            for (var c = 0; c < 4; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < gridButtons.Count; i++)
            {
                if (i % 4 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var b = NavButtonView(gridButtons[i], big: false);
                b.Width = double.NaN;
                Grid.SetRow(b, i / 4);
                Grid.SetColumn(b, i % 4);
                grid.Children.Add(b);
            }
            box.Children.Add(grid);
            return box;
        }
        if (s.Grid)
        {
            // 보관함: 줄바꿈 격자 (pear 의 보관함 화면)
            if (s.Title.Length > 0) box.Children.Add(head);
            var wrap = new WrapPanel { Margin = new Thickness(Left, 0, Right, 8), HSpacing = 16, VSpacing = 24 };
            foreach (var c in Tidy(s.Items))
            {
                var card = new CardView(c, 166, square: true, fixedHeight: true);
                card.Click += cc => Play.Card(cc, s);
                wrap.Children.Add(card);
            }
            box.Children.Add(wrap);
            return box;
        }
        var headRow = new Grid();
        headRow.Children.Add(head);
        box.Children.Add(headRow);

        var sv = HorizontalRow(out var row);
        // ‹ › 는 셸프에 마우스를 올렸을 때만 (넘길 때만 필요한 버튼)
        var arrows = ShelfArrows(sv);
        Anim.V(arrows).Opacity = 0;
        arrows.IsHitTestVisible = false;
        box.Background = new SolidColorBrush(Colors.Transparent);
        box.PointerEntered += (_, _) => { arrows.IsHitTestVisible = true; Anim.Opacity(arrows, 1, Anim.Fast); };
        box.PointerExited += (_, _) => { arrows.IsHitTestVisible = false; Anim.Opacity(arrows, 0, Anim.Medium); };
        headRow.Children.Add(arrows);
        // 스크롤 칸 자체를 좌우 여백 안쪽으로 — 넘겨도 카드가 여백까지 밀려 나가지 않는다
        sv.Margin = new Thickness(Left, 0, Right, 0);
        row.Padding = new Thickness(0, 0, 0, 8);
        var twoRows = s.Strapline != null || s.Title is "다시 듣기" or "Listen again";
        if (s.Buttons is { } navButtons)
        {
            // 둘러보기 '분위기 및 장르': 4행 가로 격자 — ‹ › 로 넘긴다 (pear). 전부 펼치지 않아 화면이 깔끔하다
            row.Spacing = 16;
            for (var i = 0; i < navButtons.Count; i += 4)
            {
                var col = new StackPanel { Spacing = 16 };
                foreach (var b in navButtons.Skip(i).Take(4)) col.Children.Add(NavButtonView(b, big: false));
                row.Children.Add(col);
            }
        }
        else if (s.Tracks != null && s.Tracks.Count >= 4 && s.Tracks.Count == s.Items.Count)
        {
            // 빠른 선곡: 4줄 × 여러 칸, 줄 436×48 · 48px 아트
            row.Spacing = 24;
            for (var i = 0; i < s.Tracks.Count; i += 4)
            {
                var col = new StackPanel { Width = 436, Spacing = 16 };
                foreach (var t in s.Tracks.Skip(i).Take(4)) col.Children.Add(QuickRow(t));
                row.Children.Add(col);
            }
        }
        else if (twoRows)
        {
            // 다시 듣기: 164px 정사각 카드 2줄
            row.Spacing = 24;
            for (var i = 0; i < s.Items.Count; i += 2)
            {
                var col = new StackPanel { Spacing = 16 };
                foreach (var c in s.Items.Skip(i).Take(2))
                {
                    var card = new CardView(c, 164, square: true, fixedHeight: true, playButton: false);
                    card.Click += cc => Play.Card(cc, s);
                    col.Children.Add(card);
                }
                row.Children.Add(col);
            }
        }
        else
        {
            row.Spacing = 24;
            foreach (var c in s.Items)
            {
                var card = new CardView(c, 196);
                card.Click += cc => Play.Card(cc, s);
                row.Children.Add(card);
            }
        }
        box.Children.Add(sv);
        return box;
    }

    /// <summary>
    /// 긴 곡 목록 끝 표시 — 화면 끝에서 900px 안으로 들어오면 다음 묶음을 받는다 (받는 동안 '더 불러오는 중…')
    /// </summary>
    internal sealed class MoreSentinel : Grid
    {
        public MoreSentinel(TrackPager pager)
        {
            Height = 56;
            var t = Theme.Text("더 불러오는 중…", 14, FontWeights.SemiBold, Theme.Secondary);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(t);
            EffectiveViewportChanged += (s, e) =>
            {
                if (e.BringIntoViewDistanceY < 900 && pager.HasMore) _ = pager.Next();
            };
        }
    }

    /// <summary>셸프 머리 오른쪽 ‹ › — 테두리만 있는 36px 동그라미, 끝에 닿으면 흐려진다 (pear 셸프 버튼)</summary>
    /// <summary>넘길 자리를 카드 왼쪽 끝에 맞춘다 — 멈췄을 때 첫 카드가 여백 선에 딱 붙게 (반쯤 잘린 카드 없이)</summary>
    private static double SnapTarget(ScrollViewer sv, double target)
    {
        target = Math.Clamp(target, 0, sv.ScrollableWidth);
        if (sv.Content is not Panel row || target >= sv.ScrollableWidth - 1) return target;
        double best = 0;
        foreach (var c in row.Children.OfType<FrameworkElement>())
        {
            double x;
            try { x = c.TransformToVisual(row).TransformPoint(new Point(0, 0)).X - row.Margin.Left; }
            catch { continue; }
            if (x <= target + 1) best = x; else break;
        }
        return Math.Clamp(best, 0, sv.ScrollableWidth);
    }

    /// <summary>
    /// 보관함 격자 카드 부제에서 반복되는 조각을 뺀다 — 내 이름(전부 내 것), 모든 카드가 같은 첫 조각
    /// (칩으로 '재생목록' 을 고르면 전부 '재생목록 • …'). 남는 건 카드마다 다른 정보뿐
    /// </summary>
    private static IEnumerable<Card> Tidy(List<Card> items)
    {
        var parts = items.Select(c => c.Subtitle.Split('•', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Me.Length == 0 || p != Me).ToList()).ToList();
        var first = parts.Count > 1 && parts.All(p => p.Count > 1) ? parts[0][0] : null;
        if (first != null && parts.All(p => p[0] == first)) foreach (var p in parts) p.RemoveAt(0);
        return items.Select((c, i) => c with { Subtitle = string.Join(" • ", parts[i]) });
    }

    /// <summary>부제에서 내 이름 조각을 뺀다 (내 재생목록마다 붙는 '잘익은청사과 •')</summary>
    internal static string WithoutMe(string subtitle) =>
        Me.Length == 0 || !subtitle.Contains(Me) ? subtitle
            : string.Join(" • ", subtitle.Split('•', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(p => p != Me));

    internal static FrameworkElement ShelfArrows(ScrollViewer sv)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, Right, 16) };
        IconButton Arrow(string icon, int dir)
        {
            var b = new IconButton(36) { Content = Theme.Icon(icon, 20, Colors.White, stroke: true, strokeWidth: 2) };
            b.Children.Insert(0, new XEllipse { Stroke = Theme.B(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1 });
            b.Click += () => sv.ChangeView(SnapTarget(sv, sv.HorizontalOffset + dir * sv.ViewportWidth * 0.8), null, null);
            return b;
        }
        var prev = Arrow(Icons.ChevronLeft, -1);
        var next = Arrow(Icons.ChevronRight, 1);
        panel.Children.Add(prev);
        panel.Children.Add(next);
        void Update()
        {
            var canPrev = sv.HorizontalOffset > 1;
            var canNext = sv.HorizontalOffset < sv.ScrollableWidth - 1;
            prev.Visibility = next.Visibility = sv.ScrollableWidth > 1 ? Visibility.Visible : Visibility.Collapsed;
            prev.Opacity = canPrev ? 1 : 0.35;
            next.Opacity = canNext ? 1 : 0.35;
            prev.IsHitTestVisible = canPrev;
            next.IsHitTestVisible = canNext;
        }
        sv.ViewChanged += (_, _) => Update();
        sv.SizeChanged += (_, _) => Update();
        if (sv.Content is FrameworkElement content) content.SizeChanged += (_, _) => Update();
        return panel;
    }

    /// <summary>빠른 선곡 한 줄 (436×48) — 48px 둥근 아트 · 곡명 · 가수</summary>
    internal static FrameworkElement QuickRow(Track t, double width = 436)
    {
        var g = new Grid { Height = 48, Width = width, Background = new SolidColorBrush(Colors.Transparent) };
        StreamResolver.HoverPrefetch(g, () => t.VideoId);
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Cover(t.Thumb, 48, 48, 86, Theme.RadiusThumb) { HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };
        if (t.Rank != null)
        {
            // 차트: 오르내림(▲ 초록 · ▼ 빨강 · • 그대로) + 순위
            g.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = new GridLength(52) });
            var rank = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var (mark, color) = t.Trend switch
            {
                "ARROW_DROP_UP" => ("▲", Color.FromArgb(255, 61, 220, 132)),
                "ARROW_DROP_DOWN" => ("▼", Color.FromArgb(255, 255, 82, 82)),
                _ => ("•", Theme.Secondary),
            };
            rank.Children.Add(Theme.Text(mark, 11, FontWeights.Bold, color));
            rank.Children.Add(Theme.Text(t.Rank, 15, FontWeights.Bold, Theme.OnSurface));
            Grid.SetColumn(rank, 1);
            g.Children.Add(rank);
        }
        text.Children.Add(Theme.Text(t.Title, 14, FontWeights.Bold, Theme.OnSurface));
        text.Children.Add(Theme.Text(t.Artist, 14, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, t.Rank != null ? 2 : 1);
        g.Children.Add(text);
        Anim.Pressable(art);
        g.Tapped += (_, _) => _ = PlayerService.Instance.PlaySong(t);
        g.RightTapped += (_, e) => { e.Handled = true; TrackMenu.Show(g, t, null, e.GetPosition(null)); };
        return g;
    }
}

/// <summary>
/// 검색 결과 (pear 의 YT Music 검색과 같은 배치) — 가운데 860px 칸에
/// 필터 칩(노래 · 동영상 · 아티스트 · 앨범 …) / 고친 검색어 / 맨 위 결과 카드(재생 · 저장) / 갈래별 목록
/// </summary>
public sealed class SearchView : ScrollPage
{
    private readonly StackPanel _col = new() { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 0 };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly StackPanel _results = new();
    private CancellationTokenSource? _cts;
    private string _query = "";
    private string? _filter;

    public SearchView()
    {
        Body.Padding = new Thickness(Left, 24, Right, 0);
        var chipScroll = ShelvesView.HorizontalRow(out var _);
        chipScroll.Content = _chips;
        chipScroll.Margin = new Thickness(0, 0, 0, 16);
        _col.Children.Add(chipScroll);
        _col.Children.Add(_results);
        Body.Children.Add(_col);
        Body.Children.Add(BottomSpace());
        SizeChanged += (_, _) => _col.Width = Math.Min(860, Math.Max(0, ActualWidth - Left - Right));
    }

    public void Search(string q)
    {
        _query = q;
        _filter = null;
        Run();
    }

    private async void Run()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _results.Children.Clear();
        var status = Theme.Text($"'{_query}' 검색 중…", 15, FontWeights.SemiBold, Theme.Secondary);
        status.Margin = new Thickness(8, 8, 0, 0);
        _results.Children.Add(status);
        try
        {
            var data = await InnerTube.Instance.Search(_query, _filter, cts.Token);
            if (cts.IsCancellationRequested) return;
            _results.Children.Clear();
            BuildChips(data.Chips);
            if (data.Corrected != null)
            {
                var c = Theme.Text($"수정된 검색어에 대한 결과: {data.Corrected}", 14, FontWeights.SemiBold, Theme.Secondary);
                c.Margin = new Thickness(8, 0, 0, 16);
                _results.Children.Add(c);
            }
            if (data.Top == null && data.Sections.Count == 0)
            {
                status.Text = $"'{_query}' 검색 결과가 없어요";
                _results.Children.Add(status);
                return;
            }
            var delay = 0;
            void Add(FrameworkElement el)
            {
                _results.Children.Add(el);
                if (delay < 400) Anim.Appear(el, delay, 8);
                delay += 30;
            }
            if (data.Top != null) Add(TopCard(data.Top));
            foreach (var it in data.TopMore) Add(Row(it, data.TopMore));
            foreach (var sec in data.Sections)
            {
                if (sec.Title.Length > 0 && (_filter == null || data.Sections.Count > 1))
                {
                    var head = new Grid { Margin = new Thickness(8, 32, 0, 12) };
                    head.Children.Add(Theme.Text(sec.Title, 22, FontWeights.ExtraBold, Theme.OnSurface));
                    // 갈래마다 '모두 보기' — 그 갈래의 필터 칩을 고른 것과 같다 (YT Music 검색)
                    if (_filter == null && data.Chips.FirstOrDefault(c => c.Text == sec.Title && c.Params != null) is { } chip)
                    {
                        var all = new Border
                        {
                            Height = 32,
                            CornerRadius = new CornerRadius(16),
                            Padding = new Thickness(14, 0, 14, 0),
                            BorderBrush = Theme.B(Color.FromArgb(70, 255, 255, 255)),
                            BorderThickness = new Thickness(1),
                            Background = new SolidColorBrush(Colors.Transparent),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Child = Theme.Text("모두 보기", 13, FontWeights.SemiBold, Theme.OnSurface),
                        };
                        ((TextBlock)all.Child).VerticalAlignment = VerticalAlignment.Center;
                        Anim.FadeBg(all);
                        all.PointerEntered += (_, _) => all.Background = Theme.B(Theme.ChipHover);
                        all.PointerExited += (_, _) => all.Background = new SolidColorBrush(Colors.Transparent);
                        Anim.Pressable(all);
                        all.Tapped += (_, _) => { _filter = chip.Params; Run(); };
                        head.Children.Add(all);
                    }
                    Add(head);
                }
                foreach (var it in sec.Items) Add(Row(it, sec.Items));
            }
            Scroller.ChangeView(null, 0, null, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Error("검색 실패", e);
            status.Text = "검색하지 못했어요. 인터넷 연결을 확인해 주세요.";
        }
    }

    /// <summary>필터 칩 — 고른 칩은 흰 바탕, 다시 누르면 필터 해제</summary>
    private void BuildChips(List<Chip> chips)
    {
        _chips.Children.Clear();
        foreach (var c in chips)
        {
            var selected = c.Selected || (_filter != null && c.Params == _filter);
            var chip = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 0, 16, 0),
                Background = Theme.B(selected ? Colors.White : Theme.Chip),
                Child = Theme.Text(c.Text, 14, FontWeights.SemiBold, selected ? Colors.Black : Theme.OnSurface),
            };
            ((TextBlock)chip.Child).VerticalAlignment = VerticalAlignment.Center;
            Anim.FadeBg(chip);
            chip.PointerEntered += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.Selected); };
            chip.PointerExited += (_, _) => { if (!selected) chip.Background = Theme.B(Theme.Chip); };
            Anim.Pressable(chip);
            chip.Tapped += (_, _) =>
            {
                _filter = selected ? null : c.Params;
                Run();
            };
            _chips.Children.Add(chip);
        }
    }

    private static void Open(SearchItem it, List<SearchItem> siblings)
    {
        if (it.Track is { } t)
        {
            _ = PlayerService.Instance.PlaySong(t, fromSearch: true);
            return;
        }
        if (it.BrowseId != null) App.MainWin?.OpenBrowse(it.BrowseId);
    }

    /// <summary>검색 결과 → 메뉴 · 동작에 쓰는 카드</summary>
    private static Card CardOf(SearchItem it) =>
        new(it.Title, it.Subtitle, it.Thumb, false, it.Track?.VideoId, it.PlaylistId, it.BrowseId, it.PageType) { Menu = it.Menu };

    /// <summary>⋮ · 우클릭 메뉴 — 곡이면 곡 메뉴, 아니면 YT 가 준 카드 메뉴</summary>
    private static void ShowMenu(FrameworkElement at, SearchItem it, Point? pos)
    {
        if (it.Track is { } t) TrackMenu.Show(at, t with { Menu = t.Menu ?? it.Menu }, null, pos);
        else TrackMenu.ShowCard(at, CardOf(it), pos ?? at.TransformToVisual(null).TransformPoint(new Point(-220, 36)));
    }

    /// <summary>
    /// 맨 위 결과 — 100px 아트, 큰 제목, YT 가 준 버튼 그대로 (곡: 재생 · 저장 / 앨범: 재생 · 셔플 · 저장 /
    /// 아티스트: 셔플 · 믹스 · 구독 …), 첫 버튼은 흰 알약. 켜고 끄는 버튼(보관함 · 구독)은 누르면 글자가 바뀐다
    /// </summary>
    private FrameworkElement TopCard(SearchItem it)
    {
        var g = new Grid { Padding = new Thickness(20), ColumnSpacing = 16, RowSpacing = 16 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var art = new Cover(it.Thumb, 100, 100, 178, it.Round ? 50 : 10);
        g.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        text.Children.Add(Theme.Text(it.Title, 24, FontWeights.ExtraBold, Theme.OnSurface));
        text.Children.Add(Theme.Text(it.Subtitle, 15, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var more = new IconButton(40) { Content = Theme.Icon(Icons.More, 22, Colors.White), VerticalAlignment = VerticalAlignment.Top };
        Tip.Set(more, "작업 메뉴");
        more.Click += () => ShowMenu(more, it, null);
        Grid.SetColumn(more, 2);
        g.Children.Add(more);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var card = CardOf(it);
        if (it.Buttons is { Count: > 0 } list)
        {
            for (var i = 0; i < list.Count && i < 4; i++) buttons.Children.Add(YtButton(list[i], i == 0, it, card));
        }
        else
        {
            buttons.Children.Add(Pill(it.Track != null ? "재생" : "열기", true, Icons.Play, out _, _ => Open(it, new())));
            if (it.Track is { } t)
                buttons.Children.Add(Pill("저장", false, Icons.PlaylistAddPath, out _, at => _ = TrackMenu.RunEndpoint(
                    new JsonObject { ["addToPlaylistEndpoint"] = new JsonObject { ["videoId"] = t.VideoId } }.ToJsonString(), "저장", at, card)));
        }
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        g.Children.Add(buttons);
        var box = new Border
        {
            CornerRadius = new CornerRadius(Theme.RadiusCard),
            Background = Theme.B(Color.FromArgb(26, 255, 255, 255)),
            Child = g,
            Margin = new Thickness(0, 0, 0, 24),
        };
        box.Tapped += (_, _) => Open(it, new());
        box.RightTapped += (_, e) => { e.Handled = true; ShowMenu(box, it, e.GetPosition(null)); };
        return box;
    }

    /// <summary>YT 카드 버튼 하나 — 누르면 YT 가 준 동작 그대로. 곡 재생은 '검색에서 튼 곡'(자동재생 토글)으로</summary>
    private static FrameworkElement YtButton(YtMenuItem b, bool primary, SearchItem it, Card card)
    {
        var toggled = b.Toggle && b.Toggled;
        string TextOf() => toggled && !string.IsNullOrEmpty(b.ToggledText) ? b.ToggledText! : b.Text;
        string IconOf() => TrackMenu.IconOf(toggled ? b.ToggledIcon ?? b.Icon : b.Icon, TextOf());
        Action<string, string>? set = null;
        var pill = Pill(TextOf(), primary, IconOf(), out set, async at =>
        {
            var ep = toggled ? b.ToggledEndpoint ?? b.Endpoint : b.Endpoint;
            if (it.Track is { } t && ep != null && ep.Contains("\"watchEndpoint\"") && !ep.Contains("playlistId"))
            {
                _ = PlayerService.Instance.PlaySong(t, fromSearch: true);
                return;
            }
            var ok = await TrackMenu.RunEndpoint(ep, TextOf(), at, card);
            if (!ok)
            {
                if (ep == null) Toast.Show("이 버튼은 지원하지 않아요");
                return;
            }
            if (!b.Toggle) return;
            toggled = !toggled;
            set?.Invoke(TextOf(), IconOf());
        });
        return pill;
    }

    private static FrameworkElement Pill(string text, bool primary, string icon, out Action<string, string> set, Action<FrameworkElement> click)
    {
        var fg = primary ? Colors.Black : Colors.White;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var label = Theme.Text(text, 14, FontWeights.SemiBold, fg);
        label.VerticalAlignment = VerticalAlignment.Center;
        FrameworkElement MakeIcon(string i) => i == Icons.Play ? Theme.Icon(i, 18, fg) : Theme.Icon(i, 18, fg, stroke: true, strokeWidth: 1.8);
        row.Children.Add(MakeIcon(icon));
        row.Children.Add(label);
        var b = new Border
        {
            MinWidth = 112,
            Height = 36,
            Padding = new Thickness(18, 0, 20, 0),
            CornerRadius = new CornerRadius(18),
            Background = primary ? Theme.B(Colors.White) : new SolidColorBrush(Colors.Transparent),
            BorderBrush = primary ? null : Theme.B(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(primary ? 0 : 1),
            Child = row,
        };
        if (!primary)
        {
            Anim.FadeBg(b);
            b.PointerEntered += (_, _) => b.Background = Theme.B(Theme.ChipHover);
            b.PointerExited += (_, _) => b.Background = new SolidColorBrush(Colors.Transparent);
        }
        set = (t, i) =>
        {
            label.Text = t;
            row.Children[0] = MakeIcon(i);
            Anim.Pop(row, 0, new Vector2(0.5f, 0.5f));
        };
        Anim.Pressable(b);
        b.Tapped += (_, e) => { e.Handled = true; click(b); };
        return b;
    }

    /// <summary>결과 한 줄 — 56px 아트(아티스트는 동그라미), 제목 Bold, '노래 • MC몽 • 2175만회 재생'</summary>
    private static FrameworkElement Row(SearchItem it, List<SearchItem> siblings)
    {
        var g = new Grid { Height = 80, Background = new SolidColorBrush(Colors.Transparent) };
        var hover = new Border { CornerRadius = new CornerRadius(14), Background = Theme.B(Theme.ChipHover) };
        Anim.V(hover).Opacity = 0;
        g.Children.Add(hover);
        var inner = new Grid { Margin = new Thickness(8, 0, 8, 0), ColumnSpacing = 16 };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var art = new Cover(it.Thumb, 56, 56, 100, it.Round ? 28 : 6) { VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        text.Children.Add(Theme.Text(it.Title, 15, FontWeights.Bold, Theme.OnSurface));
        text.Children.Add(Theme.Text(it.Subtitle, 14, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        inner.Children.Add(text);
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // 마우스를 올리면 오른쪽에 ⋮ (YT Music 검색 결과 줄)
        var more = new IconButton(40) { Content = Theme.Icon(Icons.More, 22, Colors.White), VerticalAlignment = VerticalAlignment.Center };
        Tip.Set(more, "작업 메뉴");
        more.Click += () => ShowMenu(more, it, null);
        Anim.V(more).Opacity = 0;
        more.IsHitTestVisible = false;
        Grid.SetColumn(more, 2);
        inner.Children.Add(more);
        // 곡이면 아트 위에 재생 덮개
        FrameworkElement? overlay = null;
        if (it.Track != null)
        {
            // 아트 자체를 어둡게 + 재생 아이콘 (둥근 덮개를 따로 얹으면 모서리가 깨져 보였다)
            overlay = Theme.Icon(Icons.Play, 22, Colors.White);
            Anim.V(overlay).Opacity = 0;
            art.Children.Add(overlay);
        }
        g.Children.Add(inner);
        g.PointerEntered += (_, _) =>
        {
            Anim.Opacity(hover, 1, 160);
            Anim.Opacity(more, 1, 160);
            more.IsHitTestVisible = true;
            if (overlay != null) { Anim.Opacity(overlay, 1, 160); art.Dim(true); }
        };
        g.PointerExited += (_, _) =>
        {
            Anim.Opacity(hover, 0, 240);
            Anim.Opacity(more, 0, 200);
            more.IsHitTestVisible = false;
            if (overlay != null) { Anim.Opacity(overlay, 0, 200); art.Dim(false); }
        };
        Anim.Pressable(art);
        g.Tapped += (_, _) => Open(it, siblings);
        g.RightTapped += (_, e) =>
        {
            e.Handled = true;
            ShowMenu(g, it, e.GetPosition(null));
        };
        return g;
    }
}
