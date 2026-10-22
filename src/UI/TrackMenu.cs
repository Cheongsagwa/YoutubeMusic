using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// ⋮ 메뉴 (곡 · 카드 · 재생 목록 줄 · 재생 바). 맨 위 좋아요 | 싫어요, 그 아래는 YT Music 이 그 곡에 주는
/// 항목(글자 그대로) + 우리 항목(다운로드 · 동영상 연결 · 전문 통계)을 자주 쓰는 순서로 묶어 늘어놓는다 —
/// 재생 · 대기열 → 이동 → 보관 · 공유 → 드문 것(삭제 · 고정 · 동영상 연결 · 신고 · 통계). 묶음 사이에 구분선.
/// 각 항목은 YT 가 준 동작(엔드포인트)을 그대로 실행한다.
/// </summary>
public static class TrackMenu
{
    /// <param name="queueIndex">다음 트랙 목록 안의 자리 ('현재 재생목록에서 삭제')</param>
    /// <param name="pos">우클릭이면 누른 자리 (창 기준)</param>
    /// <param name="fromBar">재생 바 ⋮ — '현재 재생목록 닫기' · '전문 통계' 가 붙는다</param>
    public static async void Show(FrameworkElement at, Track t, int? queueIndex = null, Point? pos = null, bool fromBar = false)
    {
        try
        {
            var items = t.Menu;
            if (items == null && Auth.IsLoggedIn)
            {
                try { items = await InnerTube.Instance.TrackMenu(t.VideoId); }
                catch (Exception e) { Log.Warn($"곡 메뉴 받기 실패: {e.Message}"); }
            }
            var m = new OneMenu();
            LikeRow(m, at, t);
            var rows = new Rows();
            rows.Add(33, x => x.Item("다운로드", Icons.Download, () => DownloadAll(new[] { t })));
            // 음원인데 YT 가 짝 영상을 모르면 직접 이어 준다 (누가 영상 소리를 음원으로 올린 곡 등)
            if (!PlayerService.IsVideo(t))
            {
                var linked = VideoLinks.Get(t.VideoId) != null;
                rows.Add(44, x => x.Item(linked ? "연결한 동영상 바꾸기" : "동영상 연결", Icons.Link, () => VideoLinkDialog.Show(t)));
                if (linked) rows.Add(44, x => x.Item("동영상 연결 해제", Icons.Link, () => { PlayerService.Instance.LinkVideo(t, null); Toast.Show("동영상 연결을 풀었어요"); }));
            }
            var ctx = new Ctx(at, pos, t, null, queueIndex);
            if (items != null) AddYt(rows, items, ctx);
            else AddFallback(rows, ctx);
            if (queueIndex is { } qi && qi != PlayerService.Instance.Index && items?.Any(x => IsRemoveFromQueue(x)) != true)
                rows.Add(40, x => x.Item("현재 재생목록에서 삭제", Icons.MinusCircle, () => PlayerService.Instance.RemoveFromQueue(qi)));
            if (fromBar)
            {
                rows.Add(47, x => x.Item("현재 재생목록 닫기", Icons.QueueClose, () => PlayerService.Instance.CloseQueue()));
                rows.Add(48, x => x.Item("전문 통계", Icons.Stats, () => ShowStats(at, t), keepOpen: true));
            }
            rows.Fill(m, separatorFirst: true);
            m.Show(at, pos);
        }
        catch (Exception e)
        {
            Log.Error("메뉴 만들기 실패", e);
            Toast.Show("메뉴를 열지 못했어요: " + e.Message);
        }
    }

    /// <summary>카드 우클릭 · ⋮ — 곡이면 곡 메뉴, 앨범 · 플레이리스트 · 아티스트면 YT 카드 메뉴(셔플 재생 · 믹스 시작 · 보관함에 저장 …)</summary>
    public static void ShowCard(FrameworkElement at, Card c, Point pos) => ShowCard(at, c, pos, null);

    /// <summary>
    /// 앨범 · 플레이리스트 화면 머리의 ⋮ — 카드 메뉴 + 그 화면에서만 쓰는 줄(순위, 글씨, 아이콘, 동작, 메뉴 유지).
    /// 화면 자체가 열려 있으니 '재생'(큰 ▶ 가 있다) · '열기' 는 뺀다
    /// </summary>
    public static void ShowCard(FrameworkElement at, Card c, Point pos, IEnumerable<(int Rank, string Text, string Icon, Action Click, bool KeepOpen)>? page)
    {
        if (c.VideoId != null && c.PlaylistId == null)
        {
            var artist = InnerTube.StripType(c.Subtitle).Split(" • ")[0];
            Show(at, new Track(c.VideoId, c.Title, artist, null, c.Thumb, 0) { Menu = c.Menu }, null, pos);
            return;
        }
        var m = new OneMenu();
        var ctx = new Ctx(at, pos, null, c, null);
        var rows = new Rows();
        if (page == null) rows.Add(10, x => x.Item("재생", Icons.Play, () => Play.Card(c with { PageType = "play" })));
        if (c.Menu != null) AddYt(rows, c.Menu, ctx);
        else
        {
            if (c.BrowseId != null && page == null) rows.Add(20, x => x.Item("열기", Icons.Album, () => App.MainWin?.OpenBrowse(c.BrowseId)));
            if (LinkOf(ctx) != null) rows.Add(34, x => x.Item("공유", Icons.Share, () => Share(ctx)));
        }
        foreach (var (rank, text, icon, click, keep) in page ?? [])
            rows.Add(rank, x => x.Item(text, icon, click, keepOpen: keep));
        rows.Fill(m, separatorFirst: false);
        m.Show(at, pos);
    }

    /// <summary>YT 동작 하나 실행 (머리 버튼 · 토글) — 성공하면 true</summary>
    public static async Task<bool> RunEndpoint(string? endpoint, string text, FrameworkElement at, Card? card)
    {
        try
        {
            if (endpoint == null) return false;
            return await RunNode(JsonNode.Parse(endpoint), text, new Ctx(at, null, null, card, null));
        }
        catch (Exception e)
        {
            Log.Error($"동작 실패: {text}", e);
            return false;
        }
    }

    // ───────────────────────── 만들기 ─────────────────────────

    private sealed record Ctx(FrameworkElement At, Point? Pos, Track? Track, Card? Card, int? QueueIndex);

    /// <summary>맨 위 좋아요 · 싫어요 (이미 눌렀으면 채운 아이콘 + '취소'). 상태를 모르면 받아 와서 바꿔 그린다</summary>
    private static void LikeRow(OneMenu m, FrameworkElement at, Track t)
    {
        void Pair(string? st) => m.Pair(
            (st == "LIKE" ? "좋아요 취소" : "좋아요", Icons.ThumbUp, st == "LIKE", () => Rate(t, st == "LIKE" ? null : true)),
            (st == "DISLIKE" ? "싫어요 취소" : "싫어요", Icons.ThumbDown, st == "DISLIKE", () => Rate(t, st == "DISLIKE" ? null : false)));
        string? known;
        lock (InnerTube.Likes) known = InnerTube.Likes.GetValueOrDefault(t.VideoId);
        Pair(known);
        if (known == null && Auth.IsLoggedIn)
            _ = InnerTube.Instance.LikeStatus(t.VideoId).ContinueWith(task =>
            {
                if (task.Result is { } st) at.DispatcherQueue.TryEnqueue(() => Pair(st));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    /// <summary>메뉴 줄을 순위와 함께 모았다가 순위대로 (같은 순위는 들어온 순서) — 십의 자리가 바뀌면 구분선</summary>
    private sealed class Rows
    {
        private readonly List<(int Rank, Action<OneMenu> Add)> _rows = new();
        public void Add(int rank, Action<OneMenu> add) => _rows.Add((rank, add));

        public void Fill(OneMenu m, bool separatorFirst)
        {
            var group = -1;
            foreach (var (rank, add) in _rows.OrderBy(r => r.Rank))
            {
                if (group != rank / 10 && (group >= 0 || separatorFirst)) m.Separator();
                group = rank / 10;
                add(m);
            }
        }
    }

    /// <summary>
    /// 자주 쓰는 순서. 1x 재생 · 대기열(재생 · 셔플 · 다음에 재생 · 현재 재생목록에 추가 · 재생목록에 저장 · 믹스),
    /// 2x 이동(앨범 · 아티스트 · 구독), 3x 보관 · 공유, 4x 드문 것(대기열 · 재생목록에서 삭제 · 고정 · 신고 · 재생목록 삭제)
    /// </summary>
    private static int RankOf(string? ep, string? icon)
    {
        ep ??= "";
        // 확인 대화상자 · 팝업에 싸인 동작이 있어서 안쪽 키까지 보는 Contains — 드문 것부터 걸러야 겹쳐도 안 틀린다
        if (ep.Contains("deletePlaylistEndpoint")) return 45;
        if (ep.Contains("getReportFormEndpoint") || ep.Contains("flagEndpoint") || icon == "FLAG") return 46;
        if (ep.Contains("dismissQueueCommand")) return 47;
        if (ep.Contains("removeFromQueueEndpoint")) return 40;
        if (ep.Contains("playlistEditEndpoint")) return 41;
        if (icon is "KEEP" or "KEEP_OFF" or "PIN") return 42;
        if (ep.Contains("feedbackEndpoint")) return icon is "LIBRARY_ADD" or "LIBRARY_MUSIC" or "LIBRARY_SAVED" or "LIBRARY_REMOVE" or "BOOKMARK" ? 30 : 43;
        if (ep.Contains("likeEndpoint")) return 31;
        if (ep.Contains("shareEntityEndpoint")) return 34;
        if (ep.Contains("subscribeEndpoint")) return 23;
        if (ep.Contains("addToPlaylistEndpoint")) return 14;
        if (ep.Contains("queueAddEndpoint")) return ep.Contains("INSERT_AFTER_CURRENT_VIDEO") ? 12 : 13;
        if (icon is "MUSIC_SHUFFLE" or "SHUFFLE") return 11;
        if (ep.Contains("watchEndpoint") || ep.Contains("watchPlaylistEndpoint")) return icon == "PLAY_ARROW" ? 10 : 15;
        if (ep.Contains("browseEndpoint")) return icon is "ARTIST" or "PERSON" or "ACCOUNT_BOX" ? 21 : 20;
        return 35;
    }

    private static void AddYt(Rows rows, List<YtMenuItem> items, Ctx ctx)
    {
        foreach (var it in items)
        {
            if (it.Download)
            {
                if (ctx.Track is not { } t) continue;
                var off = Offline.Instance;
                if (off.Has(t.VideoId)) rows.Add(32, x => x.Item("오프라인 저장 삭제", Icons.Download, () => { off.Remove(t.VideoId); Toast.Show("오프라인 저장에서 뺐어요"); }));
                else rows.Add(32, x => x.Item(it.Text, Icons.Download, () => SaveOffline(new[] { t })));
                continue;
            }
            var toggled = it.Toggle && it.Toggled;
            var text = toggled && !string.IsNullOrEmpty(it.ToggledText) ? it.ToggledText! : it.Text;
            var type = toggled ? it.ToggledIcon ?? it.Icon : it.Icon;
            var icon = IconOf(type, text);
            var ep = toggled ? it.ToggledEndpoint ?? it.Endpoint : it.Endpoint;
            var keep = ep != null && ep.Contains("addToPlaylistEndpoint");
            // 곡 메뉴는 맨 위 좋아요 | 싫어요 줄이 같은 일을 한다 ('좋아요 표시한 노래에 추가')
            if (ctx.Track != null && ep?.Contains("likeEndpoint") == true) continue;
            rows.Add(RankOf(ep, type), x => x.Item(text, icon, () => Run(ep, text, ctx), keepOpen: keep));
        }
    }

    /// <summary>YT 메뉴를 못 받았을 때 (로그인 전 · 오프라인) — YT 곡 메뉴와 같은 항목을 직접 (순위는 RankOf 와 같게)</summary>
    private static void AddFallback(Rows rows, Ctx ctx)
    {
        if (ctx.Track is not { } t) return;
        var p = PlayerService.Instance;
        rows.Add(15, x => x.Item("믹스 시작", Icons.Radio, () => _ = p.PlaySong(t)));
        rows.Add(12, x => x.Item("다음에 재생", Icons.PlayNextIcon, () => { p.PlayNext(t); Toast.Show("다음에 재생할 곡에 추가했어요"); }));
        rows.Add(13, x => x.Item("현재 재생목록에 추가", Icons.QueueAdd, () => { p.AddToQueue(t); Toast.Show("현재 재생목록에 추가했어요"); }));
        var off = Offline.Instance;
        if (off.Has(t.VideoId)) rows.Add(32, x => x.Item("오프라인 저장 삭제", Icons.Download, () => { off.Remove(t.VideoId); Toast.Show("오프라인 저장에서 뺐어요"); }));
        else rows.Add(32, x => x.Item("오프라인 저장", Icons.Download, () => SaveOffline(new[] { t })));
        rows.Add(14, x => x.Item("재생목록에 저장", Icons.PlaylistAddPath, () => SaveToPlaylist(ctx, t.VideoId, null), keepOpen: true));
        if (t.AlbumId != null) rows.Add(20, x => x.Item("앨범으로 이동", Icons.Album, () => App.MainWin?.OpenBrowse(t.AlbumId)));
        if (t.ArtistId != null) rows.Add(21, x => x.Item("아티스트 페이지로 이동", Icons.Person, () => App.MainWin?.OpenBrowse(t.ArtistId)));
        rows.Add(34, x => x.Item("공유", Icons.Share, () => Share(ctx)));
    }

    private static bool IsRemoveFromQueue(YtMenuItem x) =>
        x.Endpoint?.Contains("removeFromQueueEndpoint") == true || x.Text == "현재 재생목록에서 삭제";

    /// <summary>YT 아이콘 이름 → 우리 아이콘 (pear 처럼 같은 선 굵기 · 둥근 끝)</summary>
    internal static string IconOf(string? type, string text) => type switch
    {
        "PLAY_ARROW" => Icons.Play,
        "SUBSCRIBE" or "NOTIFICATIONS" => Icons.Bell,
        "MIX" or "RADIO" => Icons.Radio,
        "QUEUE_PLAY_NEXT" => Icons.PlayNextIcon,
        "ADD_TO_REMOTE_QUEUE" or "QUEUE_MUSIC" => Icons.QueueAdd,
        "LIBRARY_ADD" or "LIBRARY_MUSIC" => Icons.LibraryAdd,
        "LIBRARY_SAVED" or "LIBRARY_REMOVE" or "BOOKMARK" => Icons.LibrarySaved,
        "FAVORITE" or "LIKE" or "THUMB_UP" => Icons.ThumbUp,
        "UNFAVORITE" => Icons.ThumbUp,
        "OFFLINE_DOWNLOAD" or "DOWNLOAD" => Icons.Download,
        "ADD_TO_PLAYLIST" or "PLAYLIST_ADD" => Icons.PlaylistAddPath,
        "REMOVE" or "DELETE" or "REMOVE_FROM_QUEUE" => Icons.MinusCircle,
        "ALBUM" => Icons.Album,
        "ARTIST" or "PERSON" or "ACCOUNT_BOX" => Icons.Person,
        "SHARE" => Icons.Share,
        "FLAG" => Icons.Flag,
        "KEEP" or "KEEP_OFF" or "PIN" => Icons.Pin,
        "MUSIC_SHUFFLE" or "SHUFFLE" => Icons.Shuffle,
        "DISMISS_QUEUE" => Icons.QueueClose,
        _ => text.Contains("공유") ? Icons.Share : text.Contains("신고") ? Icons.Flag : text.Contains("고정") ? Icons.Pin
            : text.Contains("삭제") ? Icons.MinusCircle : text.Contains("재생목록") ? Icons.PlaylistAddPath : Icons.QueueAdd,
    };

    // ───────────────────────── 실행 ─────────────────────────

    /// <summary>YT 가 준 동작을 실행한다. 모르는 동작이면 안쪽에서 아는 동작을 찾아 본다 (확인 대화상자 등)</summary>
    private static async void Run(string? endpoint, string text, Ctx ctx)
    {
        try
        {
            if (endpoint == null) { Toast.Show("이 항목은 지원하지 않아요"); return; }
            var ep = JsonNode.Parse(endpoint);
            if (await RunNode(ep, text, ctx)) return;
            // '재생목록 수정' — 이름 바꾸기 (YT 웹의 수정 대화상자 대신)
            if (text.Contains("수정") && PlaylistIdOf(ctx) is { } pl)
            {
                var name = await Prompt.Ask(ctx.At.XamlRoot, "재생목록 이름 바꾸기", ctx.Card?.Title ?? "새 이름", "바꾸기", ctx.Card?.Title);
                if (string.IsNullOrWhiteSpace(name)) return;
                var ok = await InnerTube.Instance.RenamePlaylist(pl, name.Trim());
                Toast.Show(ok ? $"'{name.Trim()}'(으)로 바꿨어요" : "하지 못했어요");
                return;
            }
            Log.Warn($"메뉴 동작 모름: {text} {endpoint[..Math.Min(200, endpoint.Length)]}");
            Toast.Show($"'{text}'은(는) 아직 지원하지 않아요");
        }
        catch (Exception e)
        {
            Log.Error($"메뉴 동작 실패: {text}", e);
            Toast.Show("하지 못했어요: " + e.Message);
        }
    }

    private static readonly string[] Known =
    [
        "watchEndpoint", "watchPlaylistEndpoint", "queueAddEndpoint", "browseEndpoint", "shareEntityEndpoint",
        "addToPlaylistEndpoint", "feedbackEndpoint", "likeEndpoint", "playlistEditEndpoint", "removeFromQueueEndpoint",
        "dismissQueueCommand", "offlineVideoEndpoint", "getReportFormEndpoint", "flagEndpoint", "urlEndpoint",
        "subscribeEndpoint", "unsubscribeEndpoint", "deletePlaylistEndpoint",
    ];

    /// <summary>내 재생목록이 바뀌었을 때 (재생목록 ID, 뺀 곡 ID) — 상세 화면이 그 줄을 지운다</summary>
    public static event Action<string, string?>? PlaylistEdited;
    /// <summary>재생목록을 지웠을 때 — 그 화면이 열려 있으면 닫는다</summary>
    public static event Action<string>? PlaylistDeleted;

    /// <summary>
    /// YT 확인 대화상자(confirmDialogRenderer) — '재생목록을 삭제할까요?' 처럼 되돌릴 수 없는 동작은 먼저 묻는다.
    /// 확인을 누르면 true
    /// </summary>
    private static async Task<bool> Confirm(JsonNode dialog, FrameworkElement at)
    {
        var title = dialog.P("title").Text();
        var message = dialog.P("dialogMessages") is JsonArray msgs ? string.Join("\n", msgs.Select(m => m.Text()).Where(x => x.Length > 0)) : "";
        var ok = dialog.P("confirmButton").FindFirst("text").Text();
        var cancel = dialog.P("cancelButton").FindFirst("text").Text();
        return await OneDialog.Show(title.Length > 0 ? title : "계속할까요?",
            message.Length > 0 ? Theme.Text(message, 15, FontWeights.Normal, Theme.Secondary, 6) : null,
            ok.Length > 0 ? ok : "확인", cancel.Length > 0 ? cancel : "취소");
    }

    /// <summary>
    /// 끝이 있는 목록(앨범 · 플레이리스트, 셔플 포함)은 YT Music 처럼 그 목록 + 자동재생 토글,
    /// RD… 로 시작하는 믹스 · 라디오만 끝없는 라디오(칩)로
    /// </summary>
    private static async Task PlayPlaylist(string? videoId, string playlistId, string? @params, string? title)
    {
        var p = PlayerService.Instance;
        if (playlistId.StartsWith("RD"))
        {
            await p.PlayRadio(videoId, playlistId, @params, title);
            return;
        }
        var q = await InnerTube.Instance.GetQueue(videoId, playlistId, @params);
        if (q.Tracks.Count == 0) return;
        var start = videoId == null ? 0 : Math.Max(0, q.Tracks.FindIndex(x => x.VideoId == videoId));
        await p.PlayList(q.Tracks, start, q.Title ?? title, q.RelatedBrowseId, q.AutomixPlaylistId, q.AutomixParams);
    }

    private static async Task<bool> RunNode(JsonNode? ep, string text, Ctx ctx)
    {
        if (ep is not JsonObject o) return false;
        var p = PlayerService.Instance;
        var api = InnerTube.Instance;
        // 맨 위 키가 아는 동작이 아니면(예: openPopupAction → 확인 대화상자) 안쪽에서 찾는다
        var key = Known.FirstOrDefault(o.ContainsKey);
        JsonNode? node = key != null ? o[key] : null;
        if (key == null)
        {
            // 확인 대화상자가 감싸고 있으면 먼저 묻는다 (취소하면 아무것도 안 함)
            if (o.FindFirst("confirmDialogRenderer") is { } dialog && !await Confirm(dialog, ctx.At)) return true;
            foreach (var k in Known)
                if (o.FindFirst(k) is { } inner) { key = k; node = inner; break; }
        }
        if (key == null || node == null) return false;

        switch (key)
        {
            case "watchEndpoint":
            {
                var vid = node.P("videoId").Str();
                var pl = node.P("playlistId").Str();
                var prm = node.P("params").Str();
                if (pl != null) await PlayPlaylist(vid, pl, prm, ctx.Card?.Title ?? ctx.Track?.Title);
                else if (ctx.Track is { } t && vid == t.VideoId) await p.PlaySong(t);
                else if (vid != null) await p.PlaySong(new Track(vid, ctx.Card?.Title ?? "", "", null, ctx.Card?.Thumb, 0));
                return true;
            }
            case "watchPlaylistEndpoint":
                if (node.P("playlistId").Str() is not { } wpl) return false;
                await PlayPlaylist(null, wpl, node.P("params").Str(), ctx.Card?.Title);
                return true;
            case "subscribeEndpoint":
            case "unsubscribeEndpoint":
            {
                var on = key == "subscribeEndpoint";
                var ok = node.P("channelIds", 0).Str() is { } ch && await api.Subscribe(ch, on);
                Toast.Show(!ok ? "로그인이 필요해요" : on ? "구독했어요" : "구독을 취소했어요");
                return ok;
            }
            case "queueAddEndpoint":
            {
                var target = node.P("queueTarget");
                var next = node.P("queueInsertPosition").Str() == "INSERT_AFTER_CURRENT_VIDEO";
                List<Track> tracks;
                if (target.P("videoId").Str() is { } vid)
                    tracks = ctx.Track is { } t && t.VideoId == vid ? [t] : [new Track(vid, ctx.Card?.Title ?? "", InnerTube.StripType(ctx.Card?.Subtitle ?? ""), null, ctx.Card?.Thumb, 0)];
                else if (target.P("playlistId").Str() is { } pl)
                    tracks = (await api.GetQueue(null, pl)).Tracks;
                else return false;
                if (next) p.PlayNext(tracks); else p.AddToQueue(tracks);
                Toast.Show(next ? "다음에 재생할 곡에 추가했어요" : "현재 재생목록에 추가했어요");
                return true;
            }
            case "browseEndpoint":
                if (node.P("browseId").Str() is { } bid) App.MainWin?.OpenBrowse(bid, node.P("params").Str());
                return true;
            case "shareEntityEndpoint":
                Share(ctx);
                return true;
            case "addToPlaylistEndpoint":
                SaveToPlaylist(ctx, node.P("videoId").Str(), node.P("playlistId").Str());
                return true;
            case "feedbackEndpoint":
            {
                var ok = node.P("feedbackToken").Str() is { } tok && await api.Feedback(tok);
                Toast.Show(ok ? Done(text) : "로그인이 필요해요");
                return true;
            }
            case "likeEndpoint":
            {
                var ok = await api.RateTarget(node.P("target") ?? new JsonObject(), node.P("status").Str() ?? "INDIFFERENT");
                Toast.Show(ok ? Done(text) : "로그인이 필요해요");
                return true;
            }
            case "playlistEditEndpoint":
            {
                var ok = await api.EditPlaylist(node);
                Toast.Show(ok ? Done(text) : "하지 못했어요");
                if (ok && node.P("playlistId").Str() is { } epl)
                    PlaylistEdited?.Invoke(epl, node.P("actions", 0, "removedVideoId").Str());
                return true;
            }
            case "deletePlaylistEndpoint":
            {
                if (node.P("playlistId").Str() is not { } dpl) return false;
                var ok = await api.DeletePlaylist(dpl);
                Toast.Show(ok ? "재생목록을 삭제했어요" : "하지 못했어요");
                if (ok) PlaylistDeleted?.Invoke(dpl);
                return true;
            }
            case "removeFromQueueEndpoint":
            {
                var idx = ctx.QueueIndex ?? (ctx.Track is { } t ? p.Queue.FindIndex(x => x.VideoId == t.VideoId) : -1);
                p.RemoveFromQueue(idx);
                return true;
            }
            case "dismissQueueCommand":
                p.CloseQueue();
                return true;
            case "offlineVideoEndpoint":
                if (ctx.Track is { } ot) SaveOffline(new[] { ot });
                return true;
            case "getReportFormEndpoint":
            case "flagEndpoint":
            {
                // 신고 양식은 YouTube 화면에서만 열 수 있어서 그 곡 페이지를 연다
                var id = ctx.Track?.VideoId ?? ctx.Card?.VideoId;
                var url = id != null ? $"https://www.youtube.com/watch?v={id}" : LinkOf(ctx) ?? "https://www.youtube.com/";
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                Toast.Show("YouTube 에서 신고할 수 있게 열었어요");
                return true;
            }
            case "urlEndpoint":
                if (node.P("url").Str() is { } u) await Windows.System.Launcher.LaunchUriAsync(new Uri(u.StartsWith("/") ? "https://music.youtube.com" + u : u));
                return true;
        }
        return false;
    }

    /// <summary>'보관함에 추가' → '보관함에 추가했어요'</summary>
    private static string Done(string text)
    {
        foreach (var verb in new[] { "추가", "삭제", "고정", "해제", "저장", "취소" })
            if (text.EndsWith(verb)) return text + "했어요";
        return "완료했어요";
    }

    private static string? PlaylistIdOf(Ctx ctx) =>
        ctx.Card?.PlaylistId ?? (ctx.Card?.BrowseId?.StartsWith("VL") == true ? ctx.Card.BrowseId[2..] : null);

    private static string? LinkOf(Ctx ctx)
    {
        if (ctx.Track is { } t) return $"https://music.youtube.com/watch?v={t.VideoId}";
        if (ctx.Card is not { } c) return null;
        if (c.PlaylistId != null) return $"https://music.youtube.com/playlist?list={c.PlaylistId}";
        if (c.BrowseId?.StartsWith("VL") == true) return $"https://music.youtube.com/playlist?list={c.BrowseId[2..]}";
        if (c.BrowseId != null) return $"https://music.youtube.com/browse/{c.BrowseId}";
        return c.VideoId != null ? $"https://music.youtube.com/watch?v={c.VideoId}" : null;
    }

    private static void Share(Ctx ctx)
    {
        if (LinkOf(ctx) is not { } link) return;
        var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(link);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        Toast.Show("링크를 복사했어요");
    }

    private static async void Rate(Track t, bool? like)
    {
        try
        {
            var ok = await InnerTube.Instance.Rate(t.VideoId, like);
            Toast.Show(!ok ? "로그인이 필요해요" : like switch
            {
                true => "좋아요 표시한 노래에 추가했어요",
                false => "싫어요를 표시했어요",
                _ => "평가를 취소했어요",
            });
            // 싫어요 한 곡이 지금 곡이면 넘긴다 (pear skip-disliked-songs)
            if (ok && like == false && PlayerService.Instance.Current?.VideoId == t.VideoId) _ = PlayerService.Instance.Next();
        }
        catch (Exception e) { Log.Warn($"좋아요 실패: {e.Message}"); }
    }

    /// <summary>재생목록에 저장 — 같은 메뉴 모양으로 내 재생목록을 보여 주고 고르면 넣는다 (곡 또는 재생목록 통째로)</summary>
    private static async void SaveToPlaylist(Ctx ctx, string? videoId, string? sourcePlaylistId)
    {
        try
        {
            var probe = videoId ?? ctx.Track?.VideoId;
            var lists = await InnerTube.Instance.MyPlaylists(probe, sourcePlaylistId);
            OneMenu.CloseAll();
            if (lists.Count == 0) { Toast.Show("재생목록이 없어요"); return; }
            var m = new OneMenu();
            m.Header("재생목록에 저장");
            m.Item("새 재생목록", Icons.QueueAdd, () => NewPlaylist(ctx, videoId, sourcePlaylistId));
            m.Separator();
            foreach (var (title, id, contains) in lists.Take(30))
                m.Item(title, Icons.PlaylistAddPath, check: contains, click: async () =>
                {
                    if (contains) { Toast.Show($"이미 '{title}'에 있어요"); return; }
                    var ok = sourcePlaylistId != null
                        ? await InnerTube.Instance.AddPlaylistToPlaylist(id, sourcePlaylistId)
                        : videoId != null && await InnerTube.Instance.AddToPlaylist(id, videoId);
                    Toast.Show(ok ? $"'{title}'에 저장했어요" : "저장하지 못했어요");
                });
            m.Show(ctx.At, ctx.Pos);
        }
        catch (Exception e)
        {
            Log.Warn($"재생목록 목록 실패: {e.Message}");
            OneMenu.CloseAll();
            Toast.Show("재생목록을 불러오지 못했어요");
        }
    }

    /// <summary>새 재생목록 — 이름을 받아 만들고 곡을 넣는다</summary>
    private static async void NewPlaylist(Ctx ctx, string? videoId, string? sourcePlaylistId)
    {
        await NewPlaylistDialog.Create(async () =>
        {
            var ids = new List<string>();
            if (videoId != null) ids.Add(videoId);
            else if (sourcePlaylistId != null) ids.AddRange((await InnerTube.Instance.GetQueue(null, sourcePlaylistId)).Tracks.Select(t => t.VideoId));
            return ids;
        });
    }

    /// <summary>전문 통계 (YT '전문 통계') — 곡 ID · 형식 · 크기 · 출처 · 재생 상태</summary>
    private static void ShowStats(FrameworkElement at, Track t)
    {
        OneMenu.CloseAll();
        var p = PlayerService.Instance;
        var info = StreamResolver.Instance.Info(t.VideoId);
        var m = new OneMenu(width: 340);
        m.Header("전문 통계");
        m.Info("동영상 ID", t.VideoId);
        m.Info("제목", t.Title);
        m.Info("종류", t.VideoType switch
        {
            "MUSIC_VIDEO_TYPE_ATV" => "음원 (ATV)",
            "MUSIC_VIDEO_TYPE_OMV" => "뮤직비디오 (OMV)",
            "MUSIC_VIDEO_TYPE_UGC" => "동영상 (UGC)",
            null => "-",
            var v => v,
        });
        foreach (var (k, v) in info) m.Info(k, v);
        m.Info("재생 위치", $"{Fmt.Time(p.Position)} / {Fmt.Time(p.Duration)}");
        m.Info("음량 표준화", p.Normalize ? $"{p.GainDb:+0.0;-0.0;0} dB" : "꺼짐");
        m.Info("볼륨", $"{Math.Round(p.Volume * 100)}%{(p.Muted ? " (음소거)" : "")}");
        m.Info("다음 트랙", $"{p.Queue.Count}곡 · {(p.Radio ? "라디오" : p.ShowAutoplayToggle ? (p.Autoplay ? "자동재생 켜짐" : "자동재생 꺼짐") : "목록")}");
        m.Show(at, null);
    }

    // ───────────────────────── 다운로드 · 오프라인 ─────────────────────────

    /// <summary>음악 폴더\OneMusic 에 m4a 로 (여러 곡이면 차례로)</summary>
    public static async void DownloadAll(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        var ok = 0;
        Toast.Show(list.Count == 1 ? $"'{list[0].Title}' 다운로드 중…" : $"{list.Count}곡 다운로드 중…");
        foreach (var t in list)
        {
            try { await Offline.Download(t); ok++; }
            catch (Exception e) { Log.Error($"다운로드 실패: {t.Title}", e); }
        }
        Toast.Show(ok == list.Count ? $"다운로드 완료 — {Offline.DownloadDir}" : $"{ok}/{list.Count}곡 다운로드 (나머지 실패)");
    }

    public static async void SaveOffline(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        Toast.Show(list.Count == 1 ? $"'{list[0].Title}' 오프라인 저장 중…" : $"{list.Count}곡 오프라인 저장 중…");
        var ok = 0;
        foreach (var t in list) if (await Offline.Instance.Save(t)) ok++;
        Toast.Show(ok == list.Count ? "오프라인 저장했어요" : $"{ok}/{list.Count}곡 저장 (나머지 실패)");
    }
}

/// <summary>한 줄 입력 대화상자 (새 재생목록 이름 등) — One UI 유리 대화상자, Enter = 확인</summary>
public static class Prompt
{
    public static async Task<string?> Ask(XamlRoot? root, string title, string placeholder, string primary = "만들기", string? initial = null)
    {
        var box = new TextBox { PlaceholderText = placeholder, FontFamily = Theme.Font, FontSize = 15, Text = initial ?? "", CornerRadius = new CornerRadius(14) };
        box.Resources["TextControlBackground"] = Theme.B(Color.FromArgb(20, 255, 255, 255));
        box.Resources["TextControlBackgroundPointerOver"] = Theme.B(Color.FromArgb(28, 255, 255, 255));
        box.Resources["TextControlBackgroundFocused"] = Theme.B(Color.FromArgb(28, 255, 255, 255));
        box.Resources["TextControlBorderBrush"] = new SolidColorBrush(Colors.Transparent);
        box.Resources["TextControlBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
        box.Resources["TextControlBorderBrushFocused"] = Theme.B(Colors.White);
        box.Resources["TextControlForegroundFocused"] = Theme.B(Colors.White);
        var ok = await OneDialog.Show(title, box, primary, ready: confirm =>
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter) return;
                e.Handled = true;
                confirm();
            };
            box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        });
        return ok ? box.Text : null;
    }
}
