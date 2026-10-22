using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace OneMusic.Core;

/// <summary>
/// YouTube Music 웹이 쓰는 내부 API(youtubei, 클라이언트 이름 WEB_REMIX) 를 직접 부른다.
/// 로그인 없이: 검색 · 검색어 추천 · 홈 · 다음 트랙(라디오) · 앨범/플레이리스트 곡 목록.
/// </summary>
public sealed class InnerTube
{
    private const string Base = "https://music.youtube.com/youtubei/v1/";
    private const string ClientVersion = "1.20250929.03.00";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static InnerTube Instance { get; } = new();

    private HttpClient _http = Create();
    private CookieContainer? _jar;

    private static HttpClient Create()
    {
        var h = Net.Handler();
        h.CookieContainer = Auth.Cookies;
        h.UseCookies = true;
        return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private static HttpClient CreateOld() => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        CookieContainer = Auth.Cookies,
        UseCookies = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    /// <summary>로그인 상태가 바뀌면(쿠키 통째로 교체) 새 쿠키로 다시 만든다</summary>
    private HttpClient Http
    {
        get
        {
            if (!ReferenceEquals(_jar, Auth.Cookies))
            {
                _jar = Auth.Cookies;
                _http = Create();
                _visitorData = null;
            }
            return _http;
        }
    }

    private string? _visitorData;
    /// <summary>방문자 식별값 — 재생 주소 요청(StreamResolver)도 같이 보내야 '로그인 필요'로 막히지 않는다</summary>
    public string? VisitorData => _visitorData;

    private const string WebVersion = "2.20250929.01.00";

    /// <param name="web">true 면 www.youtube.com 의 WEB 클라이언트로 (댓글은 YT Music 에 없어서 YouTube 것을 쓴다)</param>
    private async Task<JsonNode?> Post(string endpoint, JsonObject body, CancellationToken ct = default, bool web = false)
    {
        var origin = web ? "https://www.youtube.com" : "https://music.youtube.com";
        var client = new JsonObject
        {
            ["clientName"] = web ? "WEB" : "WEB_REMIX",
            ["clientVersion"] = web ? WebVersion : ClientVersion,
            ["hl"] = "ko",
            ["gl"] = "KR",
        };
        if (_visitorData != null) client["visitorData"] = _visitorData;
        body["context"] = new JsonObject { ["client"] = client, ["user"] = new JsonObject() };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{(web ? "https://www.youtube.com/youtubei/v1/" : Base)}{endpoint}{(endpoint.Contains('?') ? '&' : '?')}prettyPrint=false")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.TryAddWithoutValidation("Origin", origin);
        req.Headers.Referrer = new Uri(origin + "/");
        req.Headers.TryAddWithoutValidation("X-Youtube-Client-Name", web ? "1" : "67");
        req.Headers.TryAddWithoutValidation("X-Youtube-Client-Version", web ? WebVersion : ClientVersion);
        req.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("ko-KR"));
        if (_visitorData != null) req.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", _visitorData);
        var auth = Auth.AuthorizationHeader(origin);
        if (auth != null)
        {
            req.Headers.TryAddWithoutValidation("Authorization", auth);
            req.Headers.TryAddWithoutValidation("X-Origin", origin);
            req.Headers.TryAddWithoutValidation("X-Goog-AuthUser", "0");
        }

        using var res = await Http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            Log.Warn($"InnerTube {endpoint} HTTP {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
            return null;
        }
        if (Log.DevRoot != null)
        {
            // 개발 중엔 마지막 응답을 남겨 두고 구조를 확인한다
            try { File.WriteAllText(IOPath.Combine(Log.DevRoot, ".dev", $"last-{(web ? "web-" : "")}{endpoint.Split('?')[0].Replace('/', '_')}.json"), text); } catch { }
        }
        var json = JsonNode.Parse(text);
        if (!web) _visitorData ??= json.P("responseContext", "visitorData").Str();
        return json;
    }

    // ───────────────────────── 검색 ─────────────────────────

    /// <summary>노래만 검색 (YT Music 검색의 '노래' 필터)</summary>
    public async Task<List<Track>> SearchSongs(string query, CancellationToken ct = default)
    {
        var json = await Post("search", new JsonObject
        {
            ["query"] = query,
            ["params"] = "EgWKAQIIAWoMEA4QChADEAQQCRAF",
        }, ct);
        return json.FindAll("musicResponsiveListItemRenderer").Select(ParseResponsive).OfType<Track>()
            .DistinctBy(t => t.VideoId).ToList();
    }

    /// <summary>
    /// YT Music 검색 그대로 — 필터 없이 부르면 맨 위 결과 카드 + 노래 · 동영상 · 앨범 … 갈래,
    /// 칩(필터)의 params 로 부르면 그 갈래만
    /// </summary>
    public async Task<SearchData> Search(string query, string? @params = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["query"] = query };
        if (@params != null) body["params"] = @params;
        var json = await Post("search", body, ct);
        var chips = json.FindAll("chipCloudChipRenderer").Select(c => new Chip(
            c.P("text").Text(), null,
            c.P("navigationEndpoint", "searchEndpoint", "params").Str(),
            c.P("isSelected")?.GetValue<bool>() == true)).Where(c => c.Text.Length > 0).ToList();

        SearchItem? top = null;
        var topMore = new List<SearchItem>();
        if (json.FindFirst("musicCardShelfRenderer") is { } card)
        {
            var titleRun = card.P("title", "runs", 0);
            var nav = titleRun.P("navigationEndpoint");
            var sub = card.P("subtitle");
            var subText = sub.Text();
            var thumb = card.P("thumbnail").BestThumb();
            var videoId = nav.P("watchEndpoint", "videoId").Str();
            Track? track = null;
            if (videoId != null)
            {
                var segs = subText.Split(" • ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (segs.Count > 0 && TypeLabels.Contains(segs[0])) segs.RemoveAt(0);
                var time = segs.FirstOrDefault(x => TimeRx.IsMatch(x));
                if (time != null) segs.Remove(time);
                track = new Track(videoId, card.P("title").Text(), segs.FirstOrDefault() ?? "", null, thumb, Fmt.ParseTime(time), ArtistOf(sub), AlbumOf(sub));
            }
            top = new SearchItem(card.P("title").Text(), subText, thumb,
                nav.P("browseEndpoint", "browseId").Str(), PageTypeOf(nav.P("browseEndpoint")), track)
            {
                Menu = ParseMenu(card.P("menu", "menuRenderer")),
                Buttons = ParseCardButtons(card.P("buttons")),
            };
            if (card.P("contents") is JsonArray cc)
                topMore = cc.Select(x => x.P("musicResponsiveListItemRenderer")).OfType<JsonNode>().Select(ParseSearchItem).OfType<SearchItem>().ToList();
        }
        var sections = new List<SearchSection>();
        foreach (var shelf in json.FindAll("musicShelfRenderer"))
        {
            var items = (shelf.P("contents") as JsonArray ?? new JsonArray()).Select(x => x.P("musicResponsiveListItemRenderer"))
                .OfType<JsonNode>().Select(ParseSearchItem).OfType<SearchItem>().ToList();
            if (items.Count > 0) sections.Add(new SearchSection(shelf.P("title").Text(), items));
        }
        // 새 형식: 갈래(노래 · 동영상 …) 머리 없이 itemSectionRenderer 마다 결과 한 줄씩 → 한 덩어리로
        var loose = json.FindAll("itemSectionRenderer")
            .SelectMany(s => s.P("contents") as JsonArray ?? new JsonArray())
            .Select(x => x.P("musicResponsiveListItemRenderer")).OfType<JsonNode>()
            .Select(ParseSearchItem).OfType<SearchItem>().ToList();
        if (loose.Count > 0) sections.Add(new SearchSection("검색 결과", loose));
        var corrected = json.FindFirst("showingResultsForRenderer").P("correctedQuery").Text();
        return new SearchData(top, topMore, sections, chips, corrected.Length > 0 ? corrected : null);
    }

    /// <summary>검색 결과 한 줄 — 곡이면 Track 도 같이, 앨범 · 아티스트 · 플레이리스트면 browseId</summary>
    private static SearchItem? ParseSearchItem(JsonNode item)
    {
        var cols = item.P("flexColumns") as JsonArray;
        var title = cols.P(0, "musicResponsiveListItemFlexColumnRenderer", "text").Text();
        if (string.IsNullOrWhiteSpace(title)) return null;
        var sub = string.Join(" • ", (cols ?? new JsonArray()).Skip(1)
            .Select(c => c.P("musicResponsiveListItemFlexColumnRenderer", "text").Text()).Where(x => x.Length > 0));
        var be = item.P("navigationEndpoint", "browseEndpoint");
        var track = be == null ? ParseResponsive(item) : null;
        if (track == null && be == null) return null;
        return new SearchItem(title, sub, item.P("thumbnail").BestThumb(), be.P("browseId").Str(), PageTypeOf(be), track)
        {
            Menu = ParseMenu(item.P("menu", "menuRenderer")),
            PlaylistId = item.FindFirst("watchPlaylistEndpoint").P("playlistId").Str() ?? item.FindFirst("watchEndpoint").P("playlistId").Str(),
        };
    }

    /// <summary>카드 버튼 (buttonRenderer · toggleButtonRenderer · subscribeButtonRenderer) — 글자 · 아이콘 · 동작 그대로</summary>
    private static List<YtMenuItem>? ParseCardButtons(JsonNode? buttons)
    {
        if (buttons is not JsonArray arr) return null;
        var list = new List<YtMenuItem>();
        foreach (var b in arr)
        {
            if (b.P("buttonRenderer") is { } br)
            {
                var ep = br.P("command") ?? br.P("navigationEndpoint") ?? br.P("serviceEndpoint");
                var text = br.P("text").Text();
                if (text.Length == 0) text = br.P("accessibility", "label").Str() ?? "";
                list.Add(new YtMenuItem(text, br.P("icon", "iconType").Str(), ep?.ToJsonString()));
            }
            else if (b.P("toggleButtonRenderer") is { } tg && ParseToggle(tg) is { } t) list.Add(t);
            else if (b.P("subscribeButtonRenderer") is { } sub && sub.P("channelId").Str() is { } ch)
            {
                var on = sub.P("subscribed")?.GetValue<bool>() == true;
                list.Add(new YtMenuItem("구독", "SUBSCRIBE", new JsonObject { ["subscribeEndpoint"] = new JsonObject { ["channelIds"] = new JsonArray(ch) } }.ToJsonString())
                {
                    Toggle = true,
                    Toggled = on,
                    ToggledText = "구독중",
                    ToggledEndpoint = new JsonObject { ["unsubscribeEndpoint"] = new JsonObject { ["channelIds"] = new JsonArray(ch) } }.ToJsonString(),
                });
            }
        }
        list.RemoveAll(x => x.Text.Length == 0);
        return list.Count > 0 ? list : null;
    }

    // ───────────────────────── 가사 (YT Music) ─────────────────────────

    /// <summary>
    /// pear synced-lyrics 의 YTMusic 출처: next 의 '가사' 탭 browseId 를 iOS 앱(clientName 26, 7.01.05) 으로 browse 하면
    /// 시간 있는 가사(timedLyricsModel — 줄마다 시작 · 끝 ms)가 온다. 없으면 글자 가사(설명 칸 · 안내 문구)
    /// </summary>
    public async Task<(List<(int StartMs, int EndMs, string Text)>? Timed, string? Plain)> YtLyricsFull(string videoId, CancellationToken ct = default)
    {
        var next = await Post("next", new JsonObject { ["videoId"] = videoId, ["isAudioOnly"] = true }, ct);
        string? id = null;
        foreach (var tab in next.FindAll("tabRenderer"))
            if (tab.P("endpoint", "browseEndpoint", "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType").Str() == "MUSIC_PAGE_TYPE_TRACK_LYRICS")
                id = tab.P("endpoint", "browseEndpoint", "browseId").Str();
        if (id == null) return (null, null);

        JsonNode? json = null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}browse?prettyPrint=false")
            {
                Content = new StringContent(new JsonObject
                {
                    ["browseId"] = id,
                    ["context"] = new JsonObject { ["client"] = new JsonObject { ["clientName"] = "26", ["clientVersion"] = "7.01.05", ["hl"] = "ko", ["gl"] = "KR" } },
                }.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Accept.ParseAdd("application/json");
            using var res = await _lyricsHttp.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
            else Log.Warn($"YT 시간 가사 HTTP {(int)res.StatusCode}");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) { Log.Warn($"YT 시간 가사 실패: {e.Message}"); }

        if (json.FindFirst("timedLyricsData") is JsonArray timed && timed.Count > 0 && timed[0].P("cueRange") != null)
            return (timed.Select(x => (
                int.Parse(x.P("cueRange", "startTimeMilliseconds").Str() ?? "0"),
                int.Parse(x.P("cueRange", "endTimeMilliseconds").Str() ?? "0"),
                x.P("lyricLine").Str() ?? "")).ToList(), null);
        if (json.FindFirst("timedLyricsData") is JsonArray onlyText && onlyText.Count > 0)
            return (null, string.Join("\n", onlyText.Select(x => x.P("lyricLine").Str() ?? "")));
        var plain = json.FindFirst("messageRenderer").P("text").Text();
        if (plain.Length == 0) plain = json.FindFirst("musicDescriptionShelfRenderer").P("description").Text();
        if (plain.Length == 0)
        {
            // 웹 클라이언트로 한 번 더 (글자 가사)
            var web = await Post("browse", new JsonObject { ["browseId"] = id }, ct);
            plain = web.FindFirst("musicDescriptionShelfRenderer").P("description").Text();
        }
        return (null, plain.Length > 0 ? plain : null);
    }

    /// <summary>시간 가사용 — 쿠키 없이 (iOS 앱 요청에 웹 로그인 쿠키를 붙이면 거절될 수 있다)</summary>
    private readonly HttpClient _lyricsHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ───────────────────────── 댓글 ─────────────────────────

    /// <summary>
    /// 곡 댓글 — YouTube(WEB) next 에서 댓글 칸의 이어 받기 토큰을 얻고, 그 토큰으로 댓글을 받는다.
    /// token 을 주면 그 다음 묶음 (인기순 → 끝까지 내리면 이어서)
    /// </summary>
    public async Task<CommentPage> Comments(string videoId, string? token = null, CancellationToken ct = default, bool replies = false)
    {
        if (token == null)
        {
            var watch = await Post("next", new JsonObject { ["videoId"] = videoId }, ct, web: true);
            token = watch.FindAll("itemSectionRenderer")
                .Where(s => s.P("sectionIdentifier").Str() == "comment-item-section")
                .Select(s => s.FindFirst("continuationCommand").P("token").Str()).FirstOrDefault(t => t != null)
                ?? watch.FindAll("continuationCommand").Select(c => c.P("token").Str()).LastOrDefault(t => t != null);
            if (token == null) return new CommentPage(new(), null, null);
        }
        var json = await Post("next", new JsonObject { ["continuation"] = token }, ct, web: true);
        var header = json.FindFirst("commentsHeaderRenderer");
        var count = header.P("countText").Text();

        // 새 형식: 내용은 frameworkUpdates 의 commentEntityPayload (entityKey = commentViewModel.commentKey)
        // 좋아요 · 싫어요 · 답글 동작은 toolbarSurface, 내가 누른 상태는 toolbarState 에 따로 온다
        var map = new Dictionary<string, JsonNode>();
        var surfaces = new Dictionary<string, JsonNode>();
        var states = new Dictionary<string, JsonNode>();
        foreach (var arr in json.FindAll("mutations").OfType<JsonArray>())
            foreach (var m in arr)
            {
                if (m.P("entityKey").Str() is not { } key) continue;
                if (m.P("payload", "commentEntityPayload") is { } pl) map[key] = pl;
                else if (m.P("payload", "engagementToolbarSurfaceEntityPayload") is { } sf) surfaces[key] = sf;
                else if (m.P("payload", "engagementToolbarStateEntityPayload") is { } st) states[key] = st;
            }
        string? Action(JsonNode? surface, string name) =>
            surface.P(name).FindFirst("performCommentActionEndpoint").P("action").Str();
        Comment? FromView(JsonNode vm, string? repliesToken)
        {
            // 요즘은 commentViewModel 안에 commentViewModel 이 한 번 더 감싸져 온다
            while (vm.P("commentKey") == null && vm.P("commentViewModel") is { } inner) vm = inner;
            if (vm.P("commentKey").Str() is not { } key || !map.TryGetValue(key, out var p)) return null;
            var surface = vm.P("toolbarSurfaceKey").Str() is { } sk ? surfaces.GetValueOrDefault(sk) : null;
            var state = vm.P("toolbarStateKey").Str() is { } tk ? states.GetValueOrDefault(tk) : null;
            return new Comment(
                p.P("author", "displayName").Str() ?? "",
                p.P("author", "avatarThumbnailUrl").Str(),
                p.P("properties", "content", "content").Str() ?? "",
                p.P("properties", "publishedTime").Str() ?? "",
                p.P("toolbar", "likeCountNotliked").Str() ?? "",
                p.P("toolbar", "replyCount").Str() ?? "")
            {
                RepliesToken = repliesToken,
                LikesLiked = p.P("toolbar", "likeCountLiked").Str(),
                LikeState = state.P("likeState").Str() switch
                {
                    "TOGGLE_LIKE_STATE_LIKED" => "LIKED",
                    "TOGGLE_LIKE_STATE_DISLIKED" => "DISLIKED",
                    _ => "INDIFFERENT",
                },
                LikeAction = Action(surface, "likeCommand"),
                UnlikeAction = Action(surface, "unlikeCommand"),
                DislikeAction = Action(surface, "dislikeCommand"),
                UndislikeAction = Action(surface, "undislikeCommand"),
                ReplyParams = surface.P("replyCommand").FindFirst("createReplyParams").Str(),
            };
        }

        var list = new List<Comment>();
        if (!replies)
        {
            foreach (var t in json.FindAll("commentThreadRenderer"))
            {
                var rt = t.P("replies").FindFirst("continuationCommand").P("token").Str();
                if (t.FindFirst("commentViewModel") is { } vm && FromView(vm, rt) is { } c) list.Add(c);
            }
        }
        else
        {
            // 답글: 묶음 없이 commentViewModel 이 차례로 온다
            foreach (var vm in json.FindAll("commentViewModel"))
                if (FromView(vm, null) is { } c && list.All(x => x.Text != c.Text || x.Author != c.Author))
                    list.Add(c);
        }
        // 옛 형식
        if (list.Count == 0)
            foreach (var c in json.FindAll("commentRenderer"))
                list.Add(new Comment(c.P("authorText").Text(), c.P("authorThumbnail").BestThumb(), c.P("contentText").Text(),
                    c.P("publishedTimeText").Text(), c.P("voteCount").Text(), c.P("replyCount").Str() ?? ""));
        var next = json.FindAll("continuationItemRenderer")
            .Select(c => c.FindFirst("continuationCommand").P("token").Str()).LastOrDefault(t => t != null);
        // 정렬 (인기 댓글 · 최신순) · 댓글 쓰기 칸
        var sorts = header.FindFirst("sortFilterSubMenuRenderer").P("subMenuItems") is JsonArray items
            ? items.Select(x => (x.P("title").Str() ?? x.P("title").Text(), x.FindFirst("continuationCommand").P("token").Str() ?? "", x.P("selected")?.GetValue<bool>() == true))
                .Where(x => x.Item2.Length > 0).ToList()
            : new List<(string, string, bool)>();
        var box = json.FindFirst("commentSimpleboxRenderer");
        return new CommentPage(list, count is { Length: > 0 } ? count : null, list.Count > 0 ? next : null)
        {
            Sorts = sorts,
            CreateParams = box.FindFirst("createCommentParams").Str(),
            MyAvatar = box.P("authorThumbnail").BestThumb(),
            Placeholder = box.P("placeholderText").Text() is { Length: > 0 } ph ? ph : null,
        };
    }

    /// <summary>댓글 좋아요 · 싫어요 (perform_comment_action) — 성공하면 true</summary>
    public async Task<bool> CommentAction(string action, CancellationToken ct = default)
    {
        var json = await Post("comment/perform_comment_action", new JsonObject { ["actions"] = new JsonArray(action) }, ct, web: true);
        return json != null && json.P("actionResults", 0, "status").Str() is null or "STATUS_SUCCEEDED";
    }

    /// <summary>답글 쓰기 — 성공하면 true</summary>
    public async Task<bool> PostReply(string text, string replyParams, CancellationToken ct = default)
    {
        var json = await Post("comment/create_comment_reply", new JsonObject { ["commentText"] = text, ["createReplyParams"] = replyParams }, ct, web: true);
        return json != null && (json.P("actionResult", "status").Str() is null or "STATUS_SUCCEEDED");
    }

    /// <summary>댓글 쓰기 (YouTube 계정으로) — 성공하면 true</summary>
    public async Task<bool> PostComment(string text, string createParams, CancellationToken ct = default)
    {
        var json = await Post("comment/create_comment", new JsonObject { ["commentText"] = text, ["createCommentParams"] = createParams }, ct, web: true);
        return json != null && (json.P("actionResult", "status").Str() is null or "STATUS_SUCCEEDED");
    }

    // ───────────────────────── 좋아요 · 재생목록에 저장 ─────────────────────────

    /// <summary>좋아요(true) · 싫어요(false) · 지우기(null)</summary>
    public async Task<bool> Rate(string videoId, bool? like, CancellationToken ct = default)
    {
        var ep = like switch { true => "like/like", false => "like/dislike", _ => "like/removelike" };
        var json = await Post(ep, new JsonObject { ["target"] = new JsonObject { ["videoId"] = videoId } }, ct);
        if (json != null) lock (Likes) Likes[videoId] = like switch { true => "LIKE", false => "DISLIKE", _ => "INDIFFERENT" };
        return json != null;
    }

    /// <summary>'재생목록에 저장' 에 보일 내 재생목록들 (이름 · ID · 이미 들어 있는지)</summary>
    public async Task<List<(string Title, string PlaylistId, bool Contains)>> MyPlaylists(string? videoId, string? sourcePlaylistId = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["excludeWatchLater"] = false };
        if (videoId != null) body["videoIds"] = new JsonArray(videoId);
        else if (sourcePlaylistId != null) body["playlistId"] = sourcePlaylistId;
        var json = await Post("playlist/get_add_to_playlist", body, ct);
        return json.FindAll("playlistAddToOptionRenderer").Select(r => (
            r.P("title").Text(),
            r.P("playlistId").Str() ?? "",
            r.P("containsSelectedVideos").Str() == "ALL")).Where(x => x.Item2.Length > 0).ToList();
    }

    public async Task<bool> AddToPlaylist(string playlistId, string videoId, CancellationToken ct = default)
    {
        var json = await Post("browse/edit_playlist", new JsonObject
        {
            ["playlistId"] = playlistId,
            ["actions"] = new JsonArray(new JsonObject { ["action"] = "ACTION_ADD_VIDEO", ["addedVideoId"] = videoId }),
        }, ct);
        return json.P("status").Str() == "STATUS_SUCCEEDED";
    }

    /// <summary>재생목록(앨범) 통째로 다른 재생목록에 넣는다</summary>
    public async Task<bool> AddPlaylistToPlaylist(string playlistId, string sourcePlaylistId, CancellationToken ct = default)
    {
        var json = await Post("browse/edit_playlist", new JsonObject
        {
            ["playlistId"] = playlistId,
            ["actions"] = new JsonArray(new JsonObject { ["action"] = "ACTION_ADD_PLAYLIST", ["addedFullListId"] = sourcePlaylistId }),
        }, ct);
        return json.P("status").Str() == "STATUS_SUCCEEDED";
    }

    /// <summary>여러 곡을 한 번에 재생목록에 (다음 트랙 '저장')</summary>
    public async Task<bool> AddVideosToPlaylist(string playlistId, IEnumerable<string> videoIds, CancellationToken ct = default)
    {
        var actions = new JsonArray(videoIds.Distinct().Select(v => (JsonNode)new JsonObject { ["action"] = "ACTION_ADD_VIDEO", ["addedVideoId"] = v, ["dedupeOption"] = "DEDUPE_OPTION_SKIP" }).ToArray());
        var json = await Post("browse/edit_playlist", new JsonObject { ["playlistId"] = playlistId, ["actions"] = actions }, ct);
        return json.P("status").Str() == "STATUS_SUCCEEDED";
    }

    /// <summary>새 재생목록 — privacy: PUBLIC · UNLISTED · PRIVATE. 만든 재생목록 ID</summary>
    public async Task<string?> CreatePlaylist(string title, IEnumerable<string> videoIds, string privacy = "PRIVATE", string description = "", CancellationToken ct = default)
    {
        var json = await Post("playlist/create", new JsonObject
        {
            ["title"] = title,
            ["description"] = description,
            ["privacyStatus"] = privacy,
            ["videoIds"] = new JsonArray(videoIds.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
        }, ct);
        return json.P("playlistId").Str();
    }

    /// <summary>
    /// 공동작업 켜기 — 초대 링크를 만든다(받은 사람이 곡을 추가할 수 있다). 초대 링크를 돌려준다 (ytmusicapi edit_playlist(collaboration=True) 와 같은 동작)
    /// </summary>
    public async Task<string?> EnableCollaboration(string playlistId, CancellationToken ct = default)
    {
        var json = await Post("browse/edit_playlist", new JsonObject
        {
            ["playlistId"] = playlistId.StartsWith("VL") ? playlistId[2..] : playlistId,
            ["actions"] = new JsonArray(new JsonObject { ["action"] = "ACTION_CREATE_COLLABORATION_INVITE_LINK" }),
        }, ct);
        return json.P("collaborationInviteLink").Str() ?? (json.P("status").Str() == "STATUS_SUCCEEDED" ? "" : null);
    }

    /// <summary>로그인한 계정의 프로필 사진</summary>
    private Task<(string? Photo, string Name)>? _account;

    /// <summary>계정 사진 · 이름 — account_menu 는 한 번만 부르고 같이 쓴다 (로그인이 바뀌면 ForgetAccount)</summary>
    public Task<(string? Photo, string Name)> Account()
    {
        if (_account is { IsFaulted: false, IsCanceled: false } a) return a;
        return _account = Load();
        async Task<(string?, string)> Load()
        {
            var json = await Post("account/account_menu", new JsonObject());
            return (json.FindFirst("accountPhoto").BestThumb(), json.FindFirst("accountName").Text());
        }
    }

    public void ForgetAccount() => _account = null;

    /// <summary>내 이름 — 받기 전이거나 실패했으면 ""</summary>
    public async Task<string> MyName()
    {
        if (!Auth.IsLoggedIn) return "";
        try { return (await Account()).Name; }
        catch { return ""; }
    }

    public async Task<string?> AccountPhoto(CancellationToken ct = default) => (await Account()).Photo;

    /// <summary>검색어 추천 + 바로 갈 수 있는 결과(곡 · 동영상 · 아티스트 · 앨범) — pear 검색창 드롭다운처럼</summary>
    public async Task<(List<Suggestion> Words, List<SearchItem> Items)> SuggestionsFull(string input, CancellationToken ct = default)
    {
        var json = await Post("music/get_search_suggestions", new JsonObject { ["input"] = input }, ct);
        // 최근 검색어(historySuggestionRenderer — ✕ 는 feedbackToken 으로 지운다)가 먼저, 그다음 추천어
        var words = json.FindAll("historySuggestionRenderer")
            .Select(s => new Suggestion(s.P("suggestion").Text(), s.FindFirst("feedbackToken").Str() ?? ""))
            .Concat(json.FindAll("searchSuggestionRenderer").Select(s => new Suggestion(s.P("suggestion").Text())))
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .DistinctBy(s => s.Text)
            .Take(input.Length == 0 ? 10 : 7)
            .ToList();
        var items = json.FindAll("musicResponsiveListItemRenderer").Select(ParseSearchItem).OfType<SearchItem>().Take(4).ToList();
        return (words, items);
    }

    // ───────────────────────── 홈 ─────────────────────────

    public async Task<HomeData> Home(string? browseId = null, string? @params = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["browseId"] = browseId ?? "FEmusic_home" };
        if (@params != null) body["params"] = @params;
        var json = await Post("browse", body, ct);
        var chips = json.FindAll("chipCloudChipRenderer").Select(c => new Chip(
            c.P("text").Text(),
            c.P("navigationEndpoint", "browseEndpoint", "browseId").Str(),
            c.P("navigationEndpoint", "browseEndpoint", "params").Str(),
            c.P("isSelected")?.GetValue<bool>() == true)).Where(c => c.Text.Length > 0).ToList();
        // 맨 위 큰 버튼은 제목 없는 격자에 든 것만 (둘러보기: 최신 음악 · 차트 · 분위기 및 장르).
        // 셸프 · 제목 있는 격자 안의 버튼은 그 셸프가 따로 그린다 (전엔 전부 한데 모여 둘러보기가 버튼 50개로 덮였다)
        var top = json.FindAll("gridRenderer").Where(g => g.P("header", "gridHeaderRenderer", "title").Text().Length == 0).SelectMany(g => ParseButtons(g)).ToList();
        return new HomeData(chips, ParseShelves(json), NextToken(json))
        {
            Buttons = top,
            Title = json.FindFirst("musicHeaderRenderer").P("title").Text() is { Length: > 0 } t ? t : null,
        };
    }

    private static List<NavButton> ParseButtons(JsonNode? json) =>
        json.FindAll("musicNavigationButtonRenderer").Select(b =>
        {
            var be = b.P("clickCommand", "browseEndpoint");
            var stripe = b.P("solid", "leftStripeColor")?.GetValue<double>();
            Color? color = null;
            if (stripe is { } v)
            {
                var u = (uint)(long)v;
                color = Color.FromArgb(255, (byte)(u >> 16), (byte)(u >> 8), (byte)u);
            }
            return new NavButton(b.P("buttonText").Text(), be.P("browseId").Str() ?? "", be.P("params").Str(), color);
        }).Where(b => b.Text.Length > 0 && b.BrowseId.Length > 0).ToList();

    /// <summary>홈을 끝까지 내리면 이어지는 셸프들 (YT Music 홈은 6개쯤씩 나눠서 준다)</summary>
    public async Task<HomeData> HomeMore(string token, CancellationToken ct = default)
    {
        var json = await Post($"browse?ctoken={Uri.EscapeDataString(token)}&continuation={Uri.EscapeDataString(token)}&type=next", new JsonObject(), ct);
        return new HomeData(new(), ParseShelves(json), NextToken(json));
    }

    private static string? NextToken(JsonNode? json) =>
        json.FindFirst("sectionListContinuation").P("continuations", 0, "nextContinuationData", "continuation").Str()
        ?? json.FindFirst("sectionListRenderer").P("continuations", 0, "nextContinuationData", "continuation").Str();

    /// <summary>화면(홈 · 둘러보기 · 보관함 · 관련 항목) 응답에서 셸프들을 뽑는다</summary>
    public async Task<List<Shelf>> BrowseShelves(string browseId, CancellationToken ct = default)
    {
        var json = await Post("browse", new JsonObject { ["browseId"] = browseId }, ct);
        return ParseShelves(json);
    }

    private static List<Shelf> ParseShelves(JsonNode? json, bool lists = true)
    {
        var shelves = new List<Shelf>();
        foreach (var shelf in json.FindAll("musicCarouselShelfRenderer"))
        {
            var header = shelf.P("header", "musicCarouselShelfBasicHeaderRenderer");
            var title = header.P("title").Text();
            if (string.IsNullOrWhiteSpace(title)) continue;
            var (cards, tracks) = ParseItems(shelf.P("contents") as JsonArray);
            if (cards.Count == 0 && ParseButtons(shelf.P("contents")) is { Count: > 0 } btns)
                shelves.Add(new Shelf(title, new(), null)
                {
                    Buttons = btns,
                    MoreBrowseId = header.P("moreContentButton").FindFirst("browseEndpoint").P("browseId").Str(),
                    MoreParams = header.P("moreContentButton").FindFirst("browseEndpoint").P("params").Str(),
                });
            if (cards.Count > 0)
                shelves.Add(new Shelf(title, cards, tracks.Count > 0 ? tracks : null)
                {
                    MoreBrowseId = header.P("moreContentButton").FindFirst("browseEndpoint").P("browseId").Str(),
                    MoreParams = header.P("moreContentButton").FindFirst("browseEndpoint").P("params").Str(),
                    Strapline = header.P("strapline").Text() is { Length: > 0 } st ? st : null,
                    Avatar = header.P("thumbnail").BestThumb(),
                });
        }
        if (!lists) return shelves;
        // 보관함처럼 격자로 오는 화면
        foreach (var grid in json.FindAll("gridRenderer"))
        {
            var title = grid.P("header", "gridHeaderRenderer", "title").Text();
            var (cards, tracks) = ParseItems(grid.P("items") as JsonArray);
            if (cards.Count > 0) shelves.Add(new Shelf(title.Length > 0 ? title : "", cards, tracks.Count > 0 ? tracks : null) { Grid = true });
            else if (title.Length > 0 && ParseButtons(grid) is { Count: > 0 } btns)
                shelves.Add(new Shelf(title, new(), null) { Grid = true, Buttons = btns });
        }
        foreach (var ms in json.FindAll("musicShelfRenderer"))
        {
            var title = ms.P("title").Text();
            var (cards, tracks) = ParseItems(ms.P("contents") as JsonArray);
            // 세로 목록(기록 · 보관함 노래 · 좋아요 표시한 음악 …) — 화면에서도 세로로
            if (tracks.Count > 0) shelves.Add(new Shelf(title.Length > 0 ? title : "노래", cards, tracks) { List = true, ListContinuation = TrackToken(ms) });
        }
        return shelves;
    }

    private static (List<Card>, List<Track>) ParseItems(JsonArray? items)
    {
        var cards = new List<Card>();
        var tracks = new List<Track>();
        if (items == null) return (cards, tracks);
        foreach (var item in items)
        {
            if (item.P("musicTwoRowItemRenderer") is { } two)
            {
                var c = ParseTwoRow(two);
                if (c != null) cards.Add(c);
            }
            else if (item.P("musicResponsiveListItemRenderer") is { } resp && ParseResponsive(resp) is { } t)
            {
                tracks.Add(t);
                cards.Add(new Card(t.Title, t.Artist, t.Thumb, false, t.VideoId, null, null));
            }
        }
        return (cards, tracks);
    }

    // ───────────────────────── 재생 목록 ─────────────────────────

    /// <summary>
    /// 다음 트랙 목록. videoId 만 주면 그 곡으로 시작하는 라디오(믹스),
    /// playlistId 를 주면 그 플레이리스트 · 앨범.
    /// </summary>
    public async Task<List<Track>> Queue(string? videoId, string? playlistId, CancellationToken ct = default) =>
        (await GetQueue(videoId, playlistId, ct)).Tracks;

    public async Task<QueueData> GetQueue(string? videoId, string? playlistId, CancellationToken ct = default) =>
        await GetQueue(videoId, playlistId, null, ct);

    /// <summary>params 는 자동재생 칩(친숙한 곡 · 2020년대 …) · 자동재생 목록이 주는 값 그대로</summary>
    public async Task<QueueData> GetQueue(string? videoId, string? playlistId, string? @params, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["isAudioOnly"] = true,
            ["enablePersistentPlaylistPanel"] = true,
            ["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
        };
        if (videoId != null) body["videoId"] = videoId;
        body["playlistId"] = playlistId ?? (videoId != null ? "RDAMVM" + videoId : null);
        if (@params != null) body["params"] = @params;
        var json = await Post("next", body, ct);
        if (videoId != null) RememberLike(videoId, json);
        return ParseQueue(json);
    }

    /// <summary>
    /// 곡의 공식 짝 영상 (음원 ↔ 뮤직비디오). 재생목록 · 앨범에서 받은 곡엔 짝이 안 붙어 와서 next 로 그 곡만 다시 물어본다
    /// </summary>
    public async Task<string?> Counterpart(string videoId, CancellationToken ct = default)
    {
        var q = await GetQueue(videoId, null, null, ct);
        return q.Tracks.FirstOrDefault(t => t.VideoId == videoId)?.CounterpartId;
    }

    /// <summary>곡별 좋아요 상태 (LIKE · DISLIKE · INDIFFERENT) — next 응답에 같이 온다</summary>
    public static Dictionary<string, string> Likes { get; } = new();

    private static void RememberLike(string videoId, JsonNode? json)
    {
        var st = json.FindFirst("likeButtonRenderer").P("likeStatus").Str();
        if (st != null) lock (Likes) Likes[videoId] = st;
    }

    public async Task<string?> LikeStatus(string videoId, CancellationToken ct = default)
    {
        lock (Likes) if (Likes.TryGetValue(videoId, out var s)) return s;
        var json = await Post("next", new JsonObject { ["videoId"] = videoId, ["isAudioOnly"] = true }, ct);
        RememberLike(videoId, json);
        lock (Likes) return Likes.GetValueOrDefault(videoId);
    }

    private static QueueData ParseQueue(JsonNode? json)
    {
        // 자동재생 칩: musicQueueRenderer.subHeaderChipCloud 안의 칩들. 누르면 queueUpdateCommand 의 watchEndpoint 로 다시 부른다
        var chips = json.FindFirst("musicQueueRenderer").P("subHeaderChipCloud").FindAll("chipCloudChipRenderer").Select(c =>
        {
            var ep = c.P("navigationEndpoint").FindFirst("watchEndpoint") ?? c.P("navigationEndpoint").FindFirst("watchPlaylistEndpoint");
            return new QueueChip(c.P("text").Text(), ep.P("videoId").Str(), ep.P("playlistId").Str(), ep.P("params").Str(),
                c.P("isSelected")?.GetValue<bool>() == true) { Endpoint = ep?.ToJsonString() };
        }).Where(c => c.Text.Length > 0 && (c.PlaylistId != null || c.Params != null)).ToList();
        // 플레이리스트 · 앨범 뒤의 '자동재생' 미리보기 → 이어 붙일 목록 주소
        var automix = json.FindFirst("automixPreviewVideoRenderer").FindFirst("watchPlaylistEndpoint");
        var list = new List<Track>();
        var panel = json.FindFirst("playlistPanelRenderer");
        var title = panel.P("title").Text();
        string? related = null;
        foreach (var tab in json.FindAll("tabRenderer"))
            if (tab.P("endpoint", "browseEndpoint", "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType").Str() == "MUSIC_PAGE_TYPE_TRACK_RELATED")
                related = tab.P("endpoint", "browseEndpoint", "browseId").Str();
        var extra = (QueueData q) => q with { Chips = chips, AutomixPlaylistId = automix.P("playlistId").Str(), AutomixParams = automix.P("params").Str() };
        if (panel.P("contents") is not JsonArray items) return extra(new QueueData(list, title, related));
        foreach (var item in items)
        {
            // 한 줄에 '음원(ATV)'과 '뮤직비디오(OMV)'가 함께 들어 있는 경우가 많다 → 음원만 고른다
            JsonNode? chosen = item.P("playlistPanelVideoRenderer");
            string? counterpart = null;
            if (item.P("playlistPanelVideoWrapperRenderer") is { } wrap)
            {
                var versions = new List<JsonNode?> { wrap.P("primaryRenderer", "playlistPanelVideoRenderer") };
                if (wrap.P("counterpart") is JsonArray cps)
                    versions.AddRange(cps.Select(c => c.P("counterpartRenderer", "playlistPanelVideoRenderer")));
                chosen = versions.FirstOrDefault(v => VideoType(v) == "MUSIC_VIDEO_TYPE_ATV") ?? versions[0];
                // 노래/동영상 전환에 쓸 짝 (뮤직비디오)
                counterpart = versions.FirstOrDefault(v => v != chosen).P("videoId").Str();
            }
            if (chosen != null && ParsePanel(chosen) is { } t) list.Add(t with { CounterpartId = counterpart });
        }
        return extra(new QueueData(list.DistinctBy(t => t.VideoId).ToList(), title.Length > 0 ? title : null, related));
    }

    /// <summary>
    /// 자동재생 칩을 눌렀을 때 — 칩의 watchEndpoint 를 그대로 next 본문으로 보낸다 (YT 웹과 같게).
    /// 우리 기본 값(isAudioOnly · tunerSetting · persistent panel)을 섞으면 서버가 칩 params 를 무시하고 같은 목록을 줬다
    /// </summary>
    public async Task<QueueData> ChipQueue(QueueChip chip, string? seedVideoId, CancellationToken ct = default)
    {
        if (chip.Endpoint == null) return await GetQueue(chip.VideoId ?? seedVideoId, chip.PlaylistId, chip.Params, ct);
        var body = (JsonNode.Parse(chip.Endpoint) as JsonObject) ?? new JsonObject();
        body.Remove("watchEndpointMusicSupportedConfigs");
        if (!body.ContainsKey("videoId") && seedVideoId != null) body["videoId"] = seedVideoId;
        body["isAudioOnly"] = true;
        var json = await Post("next", body, ct);
        var q = ParseQueue(json);
        Log.Info($"칩 '{chip.Text}' → {q.Tracks.Count}곡: {string.Join(", ", q.Tracks.Take(4).Select(t => t.Title))}");
        return q;
    }

    /// <summary>앨범 · 플레이리스트 · 아티스트 페이지의 곡들</summary>
    public async Task<List<Track>> BrowseTracks(string browseId, CancellationToken ct = default)
    {
        var json = await Post("browse", new JsonObject { ["browseId"] = browseId }, ct);
        var albumThumb = json.P("header").BestThumb() ?? json.P("contents").FindFirst("musicResponsiveHeaderRenderer").BestThumb();
        var list = json.FindAll("musicResponsiveListItemRenderer").Select(ParseResponsive).OfType<Track>()
            .DistinctBy(t => t.VideoId).ToList();
        // 앨범 수록곡 줄에는 썸네일이 없어서 앨범 표지를 넣어 준다
        if (albumThumb != null)
            list = list.Select(t => t.Thumb == null ? t with { Thumb = albumThumb } : t).ToList();
        return list;
    }

    /// <summary>앨범 · 플레이리스트 · 아티스트 상세 화면 (머리 + 곡 목록 + 아래 셸프들)</summary>
    public async Task<PageData> BrowsePage(string browseId, CancellationToken ct = default)
    {
        var json = await Post("browse", new JsonObject { ["browseId"] = browseId }, ct);
        var artistHeader = json.FindFirst("musicImmersiveHeaderRenderer") ?? json.FindFirst("musicVisualHeaderRenderer");
        var header = artistHeader ?? json.FindFirst("musicResponsiveHeaderRenderer") ?? json.FindFirst("musicDetailHeaderRenderer");
        var kind = artistHeader != null || browseId.StartsWith("UC") ? PageKind.Artist
            : browseId.StartsWith("MPRE") ? PageKind.Album : PageKind.Playlist;

        var title = header.P("title").Text();
        var subtitle = header.P("subtitle").Text();
        var strap = header.P("straplineTextOne");
        var desc = header.P("description", "musicDescriptionShelfRenderer", "description").Text();
        if (desc.Length == 0) desc = header.P("description").Text();
        var second = header.P("secondSubtitle").Text();
        if (kind == PageKind.Artist)
        {
            // 아티스트: 부제목 자리에 구독자 · 월간 청취자 수
            var subs = header.FindFirst("subscriberCountText").Text();
            var monthly = header.P("monthlyListenerCount").Text();
            subtitle = string.Join(" • ", new[] { subs.Length > 0 ? $"구독자 {subs}" : "", monthly }.Where(x => x.Length > 0));
        }
        var thumb = header.P("thumbnail").BestThumb() ?? header.P("foregroundThumbnail").BestThumb();

        // 곡 목록: 앨범 · 아티스트는 musicShelfRenderer, 플레이리스트는 musicPlaylistShelfRenderer
        var list = json.FindFirst("musicPlaylistShelfRenderer") ?? json.FindFirst("musicShelfRenderer");
        var tracks = (list.P("contents") as JsonArray ?? new JsonArray()).Select(i => i.P("musicResponsiveListItemRenderer"))
            .OfType<JsonNode>().Select(ParseResponsive).OfType<Track>().DistinctBy(t => t.VideoId).ToList();
        var albumArtist = strap.Text();
        var albumArtistId = ArtistOf(strap);
        if (kind == PageKind.Album)
            // 앨범 수록곡 줄에는 썸네일 · 가수(한 사람 앨범일 때) · 앨범 이름이 비어 있다
            tracks = tracks.Select(t => t with
            {
                Thumb = t.Thumb ?? thumb,
                Artist = t.Artist.Length > 0 ? t.Artist : albumArtist,
                ArtistId = t.ArtistId ?? albumArtistId,
                Album = t.Album ?? title,
                AlbumId = t.AlbumId ?? browseId,
            }).ToList();
        else if (thumb != null)
            tracks = tracks.Select(t => t.Thumb == null ? t with { Thumb = thumb } : t).ToList();
        var tracksTitle = list.P("title").Text();
        var more = list.P("bottomEndpoint", "browseEndpoint", "browseId").Str()
                   ?? list.P("title", "runs", 0, "navigationEndpoint", "browseEndpoint", "browseId").Str();

        string? play = null, radio = null;
        if (kind == PageKind.Artist)
        {
            play = header.P("playButton").FindFirst("playlistId").Str();
            radio = header.P("startRadioButton").FindFirst("playlistId").Str();
        }

        // 100곡이 넘는 플레이리스트는 이어 받는 표가 목록 끝(또는 continuations)에 온다
        var tracksToken = kind == PageKind.Artist ? null : TrackToken(list);
        return new PageData(kind, title,
            subtitle.Length > 0 ? subtitle : null,
            albumArtist.Length > 0 ? albumArtist : null,
            albumArtistId,
            second.Length > 0 ? second : null,
            desc.Length > 0 ? desc : null,
            thumb, tracks,
            tracksTitle.Length > 0 ? tracksTitle : null,
            more,
            ParseShelves(json, lists: false),
            play, radio)
        {
            StraplineThumb = header.P("straplineThumbnail").BestThumb(),
            Menu = ParseMenu(header.FindFirst("menuRenderer")),
            LibraryToggle = ParseToggle(header.FindFirst("toggleButtonRenderer")),
            ChannelId = header.FindFirst("subscribeButtonRenderer").P("channelId").Str(),
            Subscribed = header.FindFirst("subscribeButtonRenderer").P("subscribed")?.GetValue<bool>() == true,
            AudioPlaylistId = json.FindFirst("musicResponsiveHeaderRenderer").FindFirst("playlistId").Str()
                              ?? (browseId.StartsWith("VL") ? browseId[2..] : null),
            TracksContinuation = tracksToken,
        };
    }

    /// <summary>곡 목록(musicPlaylistShelf · musicShelf)의 다음 묶음 표 — 예전 방식(continuations)과 새 방식(끝의 continuationItemRenderer)</summary>
    private static string? TrackToken(JsonNode? shelf) =>
        shelf.P("continuations", 0, "nextContinuationData", "continuation").Str()
        ?? (shelf.P("contents") as JsonArray)?.LastOrDefault().P("continuationItemRenderer").FindFirst("continuationCommand").P("token").Str();

    /// <summary>
    /// 곡 목록 다음 묶음 (플레이리스트 100곡 너머 · 보관함 노래 …). 새 방식(본문에 continuation)으로 먼저, 비면 예전 방식(ctoken)
    /// </summary>
    public async Task<(List<Track> Tracks, string? Next)> MoreTracks(string token, string? thumb, CancellationToken ct = default)
    {
        var json = await Post("browse", new JsonObject { ["continuation"] = token }, ct);
        var items = json.FindAll("musicResponsiveListItemRenderer").ToList();
        if (items.Count == 0)
        {
            json = await Post($"browse?ctoken={Uri.EscapeDataString(token)}&continuation={Uri.EscapeDataString(token)}&type=next", new JsonObject(), ct);
            items = json.FindAll("musicResponsiveListItemRenderer").ToList();
        }
        var tracks = items.Select(ParseResponsive).OfType<Track>().DistinctBy(t => t.VideoId)
            .Select(t => t.Thumb == null && thumb != null ? t with { Thumb = thumb } : t).ToList();
        var next = json.FindFirst("continuationItemRenderer").FindFirst("continuationCommand").P("token").Str()
                   ?? json.FindFirst("musicPlaylistShelfContinuation").P("continuations", 0, "nextContinuationData", "continuation").Str()
                   ?? json.FindFirst("musicShelfContinuation").P("continuations", 0, "nextContinuationData", "continuation").Str();
        return (tracks, next != token ? next : null);
    }

    /// <summary>머리의 '보관함에 저장' 같은 켜고 끄는 버튼 (toggleButtonRenderer)</summary>
    private static YtMenuItem? ParseToggle(JsonNode? tg)
    {
        if (tg == null) return null;
        var text = tg.P("defaultText").Text();
        if (text.Length == 0) text = tg.P("accessibility", "label").Str() ?? tg.P("defaultTooltip").Str() ?? "보관함에 저장";
        return new YtMenuItem(text, tg.P("defaultIcon", "iconType").Str(), tg.P("defaultServiceEndpoint")?.ToJsonString())
        {
            Toggle = true,
            Toggled = tg.P("isToggled")?.GetValue<bool>() == true,
            ToggledText = tg.P("toggledText").Text() is { Length: > 0 } tt ? tt : tg.P("toggledTooltip").Str() ?? "보관함에서 삭제",
            ToggledIcon = tg.P("toggledIcon", "iconType").Str(),
            ToggledEndpoint = tg.P("toggledServiceEndpoint")?.ToJsonString(),
        };
    }

    /// <summary>아티스트 구독 · 구독 취소</summary>
    public async Task<bool> Subscribe(string channelId, bool on, CancellationToken ct = default)
    {
        var json = await Post(on ? "subscription/subscribe" : "subscription/unsubscribe", new JsonObject { ["channelIds"] = new JsonArray(channelId) }, ct);
        return json != null;
    }

    // ───────────────────────── 파싱 ─────────────────────────

    private static string? PageTypeOf(JsonNode? browseEndpoint) =>
        browseEndpoint.P("browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType").Str();

    /// <summary>글자 조각들(runs) 중 아티스트 · 앨범 페이지로 가는 링크의 browseId</summary>
    private static string? LinkOf(JsonNode? node, string pageType) =>
        node.FindAll("browseEndpoint").FirstOrDefault(b => PageTypeOf(b) == pageType).P("browseId").Str();

    private static string? ArtistOf(JsonNode? node) => LinkOf(node, "MUSIC_PAGE_TYPE_ARTIST");
    private static string? AlbumOf(JsonNode? node) => LinkOf(node, "MUSIC_PAGE_TYPE_ALBUM");

    private static readonly Regex TimeRx = new(@"^\d{1,2}(:\d{2}){1,2}$", RegexOptions.Compiled);
    private static readonly string[] TypeLabels = ["노래", "동영상", "Song", "Video", "에피소드", "Episode"];

    /// <summary>⋮ 메뉴 (menuRenderer.items) — YT 가 주는 순서 · 글자 · 동작 그대로</summary>
    public static List<YtMenuItem>? ParseMenu(JsonNode? menu)
    {
        if (menu.P("items") is not JsonArray items) return null;
        var list = new List<YtMenuItem>();
        foreach (var it in items)
        {
            if (it.P("menuNavigationItemRenderer") is { } nav)
                list.Add(new YtMenuItem(nav.P("text").Text(), nav.P("icon", "iconType").Str(), nav.P("navigationEndpoint")?.ToJsonString()));
            else if (it.P("menuServiceItemRenderer") is { } svc)
                list.Add(new YtMenuItem(svc.P("text").Text(), svc.P("icon", "iconType").Str(), svc.P("serviceEndpoint")?.ToJsonString()));
            else if (it.P("toggleMenuServiceItemRenderer") is { } tg)
                list.Add(new YtMenuItem(tg.P("defaultText").Text(), tg.P("defaultIcon", "iconType").Str(), tg.P("defaultServiceEndpoint")?.ToJsonString())
                {
                    Toggle = true,
                    Toggled = tg.P("isToggled")?.GetValue<bool>() == true,
                    ToggledText = tg.P("toggledText").Text(),
                    ToggledIcon = tg.P("toggledIcon", "iconType").Str(),
                    ToggledEndpoint = tg.P("toggledServiceEndpoint")?.ToJsonString(),
                });
            else if (it.P("menuServiceItemDownloadRenderer") is { } dl)
                list.Add(new YtMenuItem("오프라인 저장", "OFFLINE_DOWNLOAD", dl.P("serviceEndpoint")?.ToJsonString()) { Download = true });
        }
        list.RemoveAll(x => x.Text.Length == 0);
        return list.Count > 0 ? list : null;
    }

    /// <summary>곡 하나의 ⋮ 메뉴를 따로 받아 온다 (메뉴 없이 만들어진 곡용) — next 의 첫 줄 메뉴</summary>
    public async Task<List<YtMenuItem>?> TrackMenu(string videoId, CancellationToken ct = default)
    {
        var json = await Post("next", new JsonObject { ["videoId"] = videoId, ["isAudioOnly"] = true }, ct);
        RememberLike(videoId, json);
        foreach (var r in json.FindAll("playlistPanelVideoRenderer"))
            if (r.P("videoId").Str() == videoId) return ParseMenu(r.P("menu", "menuRenderer"));
        return null;
    }

    /// <summary>YT 쪽 동작 하나를 그대로 보낸다 (feedback · like · edit_playlist …). 성공하면 true</summary>
    public async Task<bool> Feedback(string token, CancellationToken ct = default)
    {
        var json = await Post("feedback", new JsonObject { ["feedbackTokens"] = new JsonArray(token) }, ct);
        return json != null && json.P("feedbackResponses", 0, "isProcessed")?.GetValue<bool>() != false;
    }

    public async Task<bool> RateTarget(JsonNode target, string status, CancellationToken ct = default)
    {
        var ep = status switch { "LIKE" => "like/like", "DISLIKE" => "like/dislike", _ => "like/removelike" };
        var json = await Post(ep, new JsonObject { ["target"] = target.DeepClone() }, ct);
        if (json != null && target.P("videoId").Str() is { } vid) lock (Likes) Likes[vid] = status;
        return json != null;
    }

    /// <summary>내 재생목록 삭제</summary>
    public async Task<bool> DeletePlaylist(string playlistId, CancellationToken ct = default)
    {
        var json = await Post("playlist/delete", new JsonObject { ["playlistId"] = playlistId }, ct);
        return json != null;
    }

    /// <summary>내 재생목록 이름 바꾸기</summary>
    public async Task<bool> RenamePlaylist(string playlistId, string name, CancellationToken ct = default) =>
        await EditPlaylist(new JsonObject
        {
            ["playlistId"] = playlistId,
            ["actions"] = new JsonArray(new JsonObject { ["action"] = "ACTION_SET_PLAYLIST_NAME", ["playlistName"] = name }),
        }, ct);

    public async Task<bool> EditPlaylist(JsonNode endpoint, CancellationToken ct = default)
    {
        var body = (endpoint.DeepClone() as JsonObject) ?? new JsonObject();
        var json = await Post("browse/edit_playlist", body, ct);
        return json.P("status").Str() == "STATUS_SUCCEEDED";
    }

    /// <summary>'노래 • MC몽' 처럼 앞에 붙은 종류 글자를 뗀다</summary>
    public static string StripType(string subtitle)
    {
        foreach (var label in TypeLabels)
            if (subtitle.StartsWith(label + " • ")) return subtitle[(label.Length + 3)..];
        return subtitle;
    }

    private static Track? ParseResponsive(JsonNode item)
    {
        var videoId = item.P("playlistItemData", "videoId").Str()
                      ?? item.P("overlay").FindFirst("videoId").Str()
                      ?? item.P("flexColumns", 0).FindFirst("videoId").Str();
        if (videoId == null) return null;
        var cols = item.P("flexColumns") as JsonArray;
        var title = cols.P(0, "musicResponsiveListItemFlexColumnRenderer", "text").Text();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var segs = new List<string>();
        if (cols != null)
            for (var i = 1; i < cols.Count; i++)
                segs.AddRange(cols.P(i, "musicResponsiveListItemFlexColumnRenderer", "text").Text()
                    .Split(" • ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (segs.Count > 0 && TypeLabels.Contains(segs[0])) segs.RemoveAt(0);

        var duration = 0;
        var fixedText = item.P("fixedColumns", 0, "musicResponsiveListItemFixedColumnRenderer", "text").Text();
        if (TimeRx.IsMatch(fixedText)) duration = Fmt.ParseTime(fixedText);
        var timeSeg = segs.LastOrDefault(s => TimeRx.IsMatch(s));
        if (timeSeg != null)
        {
            if (duration == 0) duration = Fmt.ParseTime(timeSeg);
            segs.Remove(timeSeg);
        }
        // 조회수 같은 꼬리 정보 빼기
        segs.RemoveAll(s => s.Contains("회") && s.Any(char.IsDigit) || s.EndsWith("views"));

        var artist = segs.Count > 0 ? segs[0] : "";
        var album = segs.Count > 1 ? segs[1] : null;
        var thumb = item.P("thumbnail").BestThumb();
        var rest = cols?.Skip(1).ToList() ?? new List<JsonNode?>();
        var idx = item.P("customIndexColumn", "musicCustomIndexColumnRenderer");
        return new Track(videoId, title, artist, album, thumb, duration,
            rest.Select(ArtistOf).FirstOrDefault(x => x != null), rest.Select(AlbumOf).FirstOrDefault(x => x != null))
        {
            Rank = idx.P("text").Text() is { Length: > 0 } rank ? rank : null,
            Trend = idx.P("icon", "iconType").Str(),
            Menu = ParseMenu(item.P("menu", "menuRenderer")),
            VideoType = item.FindFirst("musicVideoType").Str(),
        };
    }

    private static string? VideoType(JsonNode? r) =>
        r.P("navigationEndpoint", "watchEndpoint", "watchEndpointMusicSupportedConfigs", "watchEndpointMusicConfig", "musicVideoType").Str();

    private static Track? ParsePanel(JsonNode r)
    {
        var videoId = r.P("videoId").Str();
        if (videoId == null) return null;
        var title = r.P("title").Text();
        var bylineNode = r.P("longBylineText") ?? r.P("shortBylineText");
        var byline = bylineNode.Text();
        var segs = byline.Split(" • ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var artist = segs.Length > 0 ? segs[0] : "";
        var album = segs.Length > 1 && !Regex.IsMatch(segs[1], @"^\d{4}$") ? segs[1] : null;
        var duration = Fmt.ParseTime(r.P("lengthText").Text());
        return new Track(videoId, title, artist, album, r.P("thumbnail").BestThumb(), duration, ArtistOf(bylineNode), AlbumOf(bylineNode))
        {
            Menu = ParseMenu(r.P("menu", "menuRenderer")),
            VideoType = VideoType(r),
        };
    }

    private static Card? ParseTwoRow(JsonNode two)
    {
        var title = two.P("title").Text();
        if (string.IsNullOrWhiteSpace(title)) return null;
        var subtitle = two.P("subtitle").Text();
        var thumbs = two.P("thumbnailRenderer").FindFirst("thumbnails") as JsonArray;
        var best = thumbs?.LastOrDefault();
        var w = (int?)best?["width"]?.GetValue<double>() ?? 1;
        var h = (int?)best?["height"]?.GetValue<double>() ?? 1;
        var nav = two.P("navigationEndpoint");
        var videoId = nav.P("watchEndpoint", "videoId").Str();
        var playlistId = nav.P("watchEndpoint", "playlistId").Str() ?? nav.P("watchPlaylistEndpoint", "playlistId").Str();
        var browseId = nav.P("browseEndpoint", "browseId").Str();
        return new Card(title, subtitle, best.P("url").Str(), w > h * 1.2, videoId, playlistId, browseId,
            PageTypeOf(nav.P("browseEndpoint")))
        {
            Menu = ParseMenu(two.P("menu", "menuRenderer")),
        };
    }
}
