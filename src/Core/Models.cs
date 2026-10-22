namespace OneMusic.Core;

public sealed record Track(
    string VideoId,
    string Title,
    string Artist,
    string? Album,
    string? Thumb,
    int DurationSec,
    string? ArtistId = null,
    string? AlbumId = null)
{
    public string DurationText => DurationSec > 0 ? Fmt.Time(DurationSec) : "";
    /// <summary>YT Music 이 이 곡에 주는 ⋮ 메뉴 (항목 이름 · 순서 · 동작 그대로)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<YtMenuItem>? Menu { get; init; }
    /// <summary>차트 순위 · 오르내림 (ARROW_DROP_UP · ARROW_DROP_DOWN · ARROW_CHART_NEUTRAL)</summary>
    public string? Rank { get; init; }
    public string? Trend { get; init; }
    /// <summary>뮤직비디오 ↔ 음원 짝 (노래/동영상 전환)</summary>
    public string? CounterpartId { get; init; }
    /// <summary>이 곡이 음원(ATV)인지 뮤직비디오(OMV) · 일반 동영상인지 (MUSIC_VIDEO_TYPE_…)</summary>
    public string? VideoType { get; init; }
}

/// <summary>
/// YT Music ⋮ 메뉴의 항목 하나 — menuNavigationItemRenderer · menuServiceItemRenderer · toggleMenuServiceItemRenderer ·
/// menuServiceItemDownloadRenderer. Endpoint 는 누르면 할 일(JSON 그대로)
/// </summary>
public sealed record YtMenuItem(string Text, string? Icon, string? Endpoint)
{
    public bool Toggle { get; init; }
    public bool Toggled { get; init; }
    public string? ToggledText { get; init; }
    public string? ToggledIcon { get; init; }
    public string? ToggledEndpoint { get; init; }
    /// <summary>오프라인 저장(다운로드 렌더러)</summary>
    public bool Download { get; init; }
}

/// <summary>메인 화면 카드 (곡 · 앨범 · 플레이리스트 · 아티스트)</summary>
public sealed record Card(
    string Title,
    string Subtitle,
    string? Thumb,
    bool Wide,
    string? VideoId,
    string? PlaylistId,
    string? BrowseId,
    string? PageType = null)
{
    public List<YtMenuItem>? Menu { get; init; }
}

public sealed record Shelf(string Title, List<Card> Items, List<Track>? Tracks = null)
{
    /// <summary>세로 곡 목록(보관함 노래 · 기록)이 더 있으면 이어 받는 표</summary>
    public string? ListContinuation { get; init; }
    /// <summary>셸프 머리 '더보기' (예: 새 앨범 및 싱글 → 전체, 아티스트 앨범 → 모두)</summary>
    public string? MoreBrowseId { get; init; }
    public string? MoreParams { get; init; }
    /// <summary>세로 곡 목록 셸프 (musicShelfRenderer — 기록 · 보관함 노래)</summary>
    public bool List { get; init; }
    /// <summary>보관함처럼 줄바꿈 격자로 보여 줄 셸프</summary>
    public bool Grid { get; init; }
    /// <summary>제목 위 작은 글씨 (예: 다시 듣기 위의 내 이름)</summary>
    public string? Strapline { get; init; }
    /// <summary>제목 왼쪽 동그란 사진 (예: 내 프로필)</summary>
    public string? Avatar { get; init; }
    /// <summary>카드 대신 이동 버튼이 든 셸프 (둘러보기 '분위기 및 장르' 가로 4행 격자 · 분위기 화면의 구역별 격자)</summary>
    public List<NavButton>? Buttons { get; init; }
}

/// <summary>홈 맨 위 분위기 칩 (휴식 · 운동 …)</summary>
public sealed record Chip(string Text, string? BrowseId, string? Params, bool Selected);

public sealed record HomeData(List<Chip> Chips, List<Shelf> Shelves, string? Continuation = null)
{
    /// <summary>둘러보기 맨 위 큰 버튼 (최신 음악 · 차트 · 분위기 및 장르) · 분위기 화면의 장르 버튼들</summary>
    public List<NavButton> Buttons { get; init; } = new();
    /// <summary>화면 제목 (분위기 · 장르 하위 화면 등)</summary>
    public string? Title { get; init; }
}

public sealed record NavButton(string Text, string BrowseId, string? Params, Color? Stripe);

/// <summary>
/// 다음 트랙 목록 + 출처 이름(예: 'NEW YORK 믹스') + 관련 항목 페이지
/// + 자동재생 칩(전체 · 친숙한 곡 · 인기 · 2020년대 …) + 플레이리스트 뒤에 이어 붙일 자동재생 목록 주소
/// </summary>
public sealed record QueueData(List<Track> Tracks, string? Title, string? RelatedBrowseId)
{
    public List<QueueChip> Chips { get; init; } = new();
    public string? AutomixPlaylistId { get; init; }
    public string? AutomixParams { get; init; }
}

/// <summary>자동재생 칩 하나 — 누르면 이 주소(next)로 자동재생 목록을 다시 받는다</summary>
public sealed record QueueChip(string Text, string? VideoId, string? PlaylistId, string? Params, bool Selected)
{
    /// <summary>칩이 가진 watchEndpoint JSON 그대로 (YT 웹이 칩을 누를 때 보내는 값을 그대로 보낸다)</summary>
    public string? Endpoint { get; init; }
}

public enum PageKind { Album, Playlist, Artist }

/// <summary>앨범 · 플레이리스트 · 아티스트 상세 화면 한 장</summary>
public sealed record PageData(
    PageKind Kind,
    string Title,
    string? Subtitle,
    string? Strapline,
    string? StraplineId,
    string? SecondSubtitle,
    string? Description,
    string? Thumb,
    List<Track> Tracks,
    string? TracksTitle,
    string? TracksMoreId,
    List<Shelf> Shelves,
    string? PlayPlaylistId,
    string? RadioPlaylistId)
{
    /// <summary>만든 사람(플레이리스트 주인 · 앨범 가수) 동그란 사진</summary>
    public string? StraplineThumb { get; init; }
    /// <summary>머리 ⋮ 메뉴 (셔플 재생 · 믹스 시작 · 재생목록에 저장 · 공유 …)</summary>
    public List<YtMenuItem>? Menu { get; init; }
    /// <summary>'보관함에 저장' 켜고 끄기</summary>
    public YtMenuItem? LibraryToggle { get; init; }
    /// <summary>아티스트 구독</summary>
    public string? ChannelId { get; init; }
    public bool Subscribed { get; init; }
    /// <summary>앨범 · 플레이리스트의 재생목록 ID (공유 · 재생목록에 저장)</summary>
    public string? AudioPlaylistId { get; init; }
    /// <summary>곡이 100곡을 넘으면 나머지를 이어 받는 표 (스크롤 · 재생하면서 이어 받는다)</summary>
    public string? TracksContinuation { get; init; }
}

/// <summary>검색 결과 한 줄 (노래 · 동영상 · 앨범 · 아티스트 · 플레이리스트 …)</summary>
public sealed record SearchItem(string Title, string Subtitle, string? Thumb, string? BrowseId, string? PageType, Track? Track)
{
    public bool Round => PageType is "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_USER_CHANNEL";
    /// <summary>⋮ 메뉴 (YT 가 준 그대로)</summary>
    public List<YtMenuItem>? Menu { get; init; }
    /// <summary>맨 위 결과 카드의 버튼들 (재생 · 저장 · 셔플 · 믹스 · 구독 …) — 첫 버튼이 흰 알약</summary>
    public List<YtMenuItem>? Buttons { get; init; }
    /// <summary>재생 목록 · 앨범을 바로 틀 때의 playlistId</summary>
    public string? PlaylistId { get; init; }
}

public sealed record SearchSection(string Title, List<SearchItem> Items);

/// <summary>검색창 추천어 — RemoveToken 이 있으면 '최근 검색어'(시계 아이콘 · ✕ 로 지우기). "local" 은 이 PC 에만 남긴 기록</summary>
public sealed record Suggestion(string Text, string? RemoveToken = null)
{
    public bool History => RemoveToken != null;
}

/// <summary>검색 결과 — 맨 위 결과 카드 + 갈래별 목록 + 필터 칩 (노래 · 동영상 · 아티스트 …) + 고친 검색어</summary>
public sealed record SearchData(SearchItem? Top, List<SearchItem> TopMore, List<SearchSection> Sections, List<Chip> Chips, string? Corrected);

public sealed record LyricLine(int TimeMs, string Text);

/// <summary>가사 출처 — pear synced-lyrics 플러그인처럼 고를 수 있다 (기본 LRCLib, 없으면 YT Music)</summary>
public enum LyricsSource { LRCLib, YTMusic, Musixmatch, Genius }

/// <summary>가사 + 시간 정보가 있는지 (YT Music 가사는 시간 없이 글자만)</summary>
public sealed record LyricsResult(List<LyricLine> Lines, bool Synced);

public enum RepeatMode { Off, All, One }

public static class Fmt
{
    public static string Time(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static int ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var parts = s.Trim().Split(':');
        var total = 0;
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var n)) return 0;
            total = total * 60 + n;
        }
        return total;
    }

    /// <summary>썸네일 주소를 원하는 크기로 (googleusercontent 의 =w60-h60 꼴)</summary>
    public static string? Thumb(string? url, int size)
    {
        if (string.IsNullOrEmpty(url)) return url;
        var m = System.Text.RegularExpressions.Regex.Replace(url, @"=w\d+-h\d+", $"=w{size}-h{size}");
        return m.StartsWith("//") ? "https:" + m : m;
    }
}

public sealed record Comment(string Author, string? Avatar, string Text, string Time, string Likes, string Replies)
{
    /// <summary>답글 묶음을 받아 올 토큰</summary>
    public string? RepliesToken { get; init; }
    /// <summary>좋아요를 눌렀을 때 보일 수 (YT 가 미리 준다)</summary>
    public string? LikesLiked { get; init; }
    /// <summary>LIKED · DISLIKED · INDIFFERENT</summary>
    public string? LikeState { get; init; }
    /// <summary>좋아요 · 좋아요 취소 · 싫어요 · 싫어요 취소 동작 (perform_comment_action 에 그대로)</summary>
    public string? LikeAction { get; init; }
    public string? UnlikeAction { get; init; }
    public string? DislikeAction { get; init; }
    public string? UndislikeAction { get; init; }
    /// <summary>답글 쓰기 (create_comment_reply)</summary>
    public string? ReplyParams { get; init; }
}

/// <summary>댓글 한 묶음 + '댓글 810개' + 다음 묶음 토큰</summary>
public sealed record CommentPage(List<Comment> Items, string? Count, string? Next)
{
    /// <summary>정렬 (인기 댓글 · 최신순) — 제목 · 토큰 · 지금 고른 것</summary>
    public List<(string Title, string Token, bool Selected)> Sorts { get; init; } = new();
    public string? CreateParams { get; init; }
    public string? MyAvatar { get; init; }
    public string? Placeholder { get; init; }
}

/// <summary>이어 듣기 — 닫을 때의 재생 목록 상태 (%LocalAppData%\OneMusic\session.json)</summary>
public sealed class SessionState
{
    public List<Track> Queue { get; set; } = new();
    public int Index { get; set; } = -1;
    public double Position { get; set; }
    public int AutoStart { get; set; }
    public bool Radio { get; set; }
    public bool ShowAutoplayToggle { get; set; }
    /// <summary>알고리즘 칩 (고른 칩 표시 포함) — 예전엔 안 남겨서 다시 켜면 칩 줄이 사라졌다</summary>
    public List<QueueChip>? AutoChips { get; set; }
    public string? QueueTitle { get; set; }
    public string? RelatedBrowseId { get; set; }

    private static readonly string FilePath = IOPath.Combine(Log.AppDataDir, "session.json");

    public static SessionState? Load()
    {
        try
        {
            return File.Exists(FilePath) ? System.Text.Json.JsonSerializer.Deserialize<SessionState>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception e)
        {
            Log.Warn($"이어 듣기 읽기 실패: {e.Message}");
            return null;
        }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this)); }
        catch (Exception e) { Log.Warn($"이어 듣기 저장 실패: {e.Message}"); }
    }
}
