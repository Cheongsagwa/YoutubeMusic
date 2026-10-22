using System.Net;
using System.Text.RegularExpressions;

namespace OneMusic.Core;

/// <summary>
/// 가사 — pear synced-lyrics 플러그인의 출처 4개를 같은 방식으로:
///  · LRCLib     : track_name + artist_name 검색 → 가수 이름 Jaro-Winkler 0.9 초과 → 길이 차 15초 이하 중 가장 가까운 것 (시간 가사, 없으면 글자)
///  · YTMusic    : next 의 '가사' 탭 → iOS 앱(clientName 26) 으로 browse 하면 시간 있는 가사(timedLyricsModel), 없으면 글자
///  · Musixmatch : 데스크톱 앱 토큰 + macro.subtitles.get (LRC), 없으면 글자 가사
///  · Genius     : 검색 → 곡 페이지의 가사 (글자)
/// 한국 · 일본 곡에 로마자 · 번역본이 올라와 있는 LRCLib 결과는 걸러 낸다. 곡 · 출처별로 기억해 둔다
/// </summary>
public sealed class LyricsService
{
    public static LyricsService Instance { get; } = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<(string, LyricsSource), LyricsResult?> _bySource = new();

    public LyricsService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OneMusic v1.0 (desktop music player)");
    }

    /// <summary>찾는 순서: 고른 출처(☆) → LRCLib → YT Music → Musixmatch → Genius</summary>
    public static IEnumerable<LyricsSource> Order(LyricsSource preferred) =>
        new[] { preferred, LyricsSource.LRCLib, LyricsSource.YTMusic, LyricsSource.Musixmatch, LyricsSource.Genius }.Distinct().Where(Enabled);

    /// <summary>
    /// 꺼 둔 출처 — Musixmatch 는 지금 토큰으로 받으면 진짜 가사 대신 맛보기(더미) 가사만 와서 잠시 끈다 (사용자 요청).
    /// 다시 켜려면 여기서 빼면 된다
    /// </summary>
    public static readonly HashSet<LyricsSource> Disabled = new() { LyricsSource.Musixmatch };
    public static bool Enabled(LyricsSource s) => !Disabled.Contains(s);

    public async Task<LyricsResult?> Get(Track t, LyricsSource source, CancellationToken ct)
    {
        if (!Enabled(source)) return null;
        lock (_bySource) if (_bySource.TryGetValue((t.VideoId, source), out var c)) return c;
        LyricsResult? r = null;
        try
        {
            r = source switch
            {
                LyricsSource.LRCLib => await Lrclib(t, ct),
                LyricsSource.YTMusic => await YtMusic(t, ct),
                LyricsSource.Musixmatch => await Musixmatch(t, ct),
                _ => await Genius(t, ct),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { Log.Warn($"가사({source}) 실패: {e.Message}"); }
        Log.Info($"가사 {source}: {(r == null ? "없음" : $"{r.Lines.Count}줄 {(r.Synced ? "시간 있음" : "글자만")}")} — {t.Artist} - {t.Title}");
        lock (_bySource)
        {
            // 오래 들으면 끝없이 쌓이지 않게 (곡 수백 개쯤이면 비운다)
            if (_bySource.Count > 600) _bySource.Clear();
            _bySource[(t.VideoId, source)] = r;
        }
        return r;
    }

    private static LyricsResult? Plain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = text.Replace("\r", "").Split('\n').Select(x => new LyricLine(-1, x.Trim())).ToList();
        while (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.Count > 0 ? new LyricsResult(lines, false) : null;
    }

    /// <summary>pear LRC 파서처럼 첫 줄이 0.3초보다 늦게 시작하면 맨 앞에 빈 줄을 넣는다 (전주 동안 ♪)</summary>
    private static LyricsResult? Synced(List<LyricLine>? lines)
    {
        if (lines == null || lines.Count == 0) return null;
        if (lines[0].TimeMs > 300) lines.Insert(0, new LyricLine(0, ""));
        return new LyricsResult(lines, true);
    }

    // ───────────────────────── YT Music ─────────────────────────

    private async Task<LyricsResult?> YtMusic(Track t, CancellationToken ct)
    {
        var (timed, plain) = await InnerTube.Instance.YtLyricsFull(t.VideoId, ct);
        if (timed is { Count: > 0 })
            return Synced(timed.Select(x => new LyricLine(x.StartMs, x.Text.Trim() == "♪" ? "" : x.Text.Trim())).ToList());
        if (plain == null || plain.Trim() == "Lyrics not available") return null;
        return Plain(plain);
    }

    // ───────────────────────── LRCLib ─────────────────────────

    /// <summary>
    /// pear LRCLib.ts 와 같은 순서:
    ///  1) search?track_name=제목&artist_name=가수(&album_name=앨범)  — 없으면 앨범 없이, feat 뗀 제목으로도
    ///  2) 그래도 없으면 search?q=제목
    ///  3) 가수 이름(쉼표 · & 로 나눈 것끼리) Jaro-Winkler 가 0.9 를 넘는 것만
    ///  4) 곡 길이와 가장 가까운 것, 차이가 15초를 넘으면 없음 · 연주곡이면 없음
    ///  + 한국 · 일본 곡이면 원어 가사여야 함 (로마자 · 번역본 거르기)
    /// </summary>
    private async Task<LyricsResult?> Lrclib(Track t, CancellationToken ct)
    {
        var full = t.Title.Trim();
        var title = Clean(full);
        var artists = ArtistNames(t.Artist);
        var all = new Dictionary<long, JsonNode>();

        async Task Search(string query)
        {
            try
            {
                using var res = await _http.GetAsync($"https://lrclib.net/api/search?{query}", ct);
                if (!res.IsSuccessStatusCode) return;
                if (JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) is JsonArray arr)
                    foreach (var x in arr)
                        if (x?["id"] is { } id) all.TryAdd(id.GetValue<long>(), x);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } // 요청 시간 초과
        }
        string Q(params (string K, string? V)[] kv) =>
            string.Join("&", kv.Where(x => !string.IsNullOrEmpty(x.V)).Select(x => $"{x.K}={Uri.EscapeDataString(x.V!)}"));

        bool Enough() => Pick(t, all.Values, artists, synced: true) != null;

        var mainArtist = t.Artist.Trim();
        if (t.Album != null) await Search(Q(("artist_name", mainArtist), ("track_name", full), ("album_name", t.Album)));
        if (!Enough()) await Search(Q(("artist_name", mainArtist), ("track_name", full)));
        if (!Enough() && title != full) await Search(Q(("artist_name", mainArtist), ("track_name", title)));
        foreach (var a in artists.Where(a => a != mainArtist))
            if (!Enough()) await Search(Q(("artist_name", a), ("track_name", title)));
        if (!Enough()) await Search(Q(("q", title)));
        if (!Enough()) await Search(Q(("q", $"{artists[0]} {title}")));
        var feat = Regex.Match(full, @"[\(\[](?:feat\.?|ft\.?|with)\s*([^\)\]]+)[\)\]]", RegexOptions.IgnoreCase);
        if (!Enough() && feat.Success) await Search(Q(("q", $"{title} {feat.Groups[1].Value.Trim()}")));

        Log.Info($"LRCLib 후보 {all.Count}개: " + string.Join(" / ", all.Values.Take(8).Select(x =>
            $"#{x["id"]} {x.P("artistName").Str()} - {x.P("trackName").Str()} {Dur(x):0}s {(x.P("syncedLyrics").Str() != null ? "synced" : x.P("plainLyrics").Str() != null ? "plain" : "-")} " +
            $"가수:{ArtistScore(artists, x.P("artistName").Str() ?? ""):0.00} 원어:{ScriptMatches(t, x.P("syncedLyrics").Str() ?? x.P("plainLyrics").Str())}")));

        if (Pick(t, all.Values, artists, synced: true) is { } best)
            return Synced(Parse(best.P("syncedLyrics").Str()));
        if (Pick(t, all.Values, artists, synced: false) is { } plain)
            return Plain(plain.P("plainLyrics").Str());
        return null;
    }

    private static JsonNode? Pick(Track t, IEnumerable<JsonNode> all, List<string> artists, bool synced) =>
        all.Where(x => x["instrumental"]?.GetValue<bool>() != true)
           .Where(x => ArtistScore(artists, x.P("artistName").Str() ?? "") > 0.9)
           .Where(x => t.DurationSec <= 0 || Math.Abs(Dur(x) - t.DurationSec) <= 15)
           .Where(x => synced ? x.P("syncedLyrics").Str() is { } s && ScriptMatches(t, s)
                              : x.P("plainLyrics").Str() is { } p && ScriptMatches(t, p))
           .OrderBy(x => Math.Abs(Dur(x) - t.DurationSec))
           .FirstOrDefault();

    /// <summary>가수 이름 후보: 'izna (이즈나)' → 'izna (이즈나)' · 'izna' · '이즈나', 'A, B 및 C' → 각각</summary>
    private static List<string> ArtistNames(string artist)
    {
        var list = new List<string> { artist.Trim() };
        foreach (var part in Regex.Split(artist, @",| 및 | & | x |&"))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            list.Add(p);
            var m = Regex.Match(p, @"^(.*?)\s*\((.+?)\)\s*$");
            if (m.Success)
            {
                list.Add(m.Groups[1].Value.Trim());
                list.Add(m.Groups[2].Value.Trim());
            }
        }
        return list.Where(a => a.Length > 0).Distinct().ToList();
    }

    /// <summary>pear 처럼 양쪽 가수 이름을 쉼표 · & 로 나눠 모든 짝의 Jaro-Winkler 중 가장 높은 값</summary>
    private static double ArtistScore(List<string> ours, string theirs)
    {
        var them = ArtistNames(theirs);
        var best = 0.0;
        foreach (var a in ours)
            foreach (var b in them)
                best = Math.Max(best, Math.Max(JaroWinkler(a.ToLowerInvariant(), b.ToLowerInvariant()), JaroWinkler(b.ToLowerInvariant(), a.ToLowerInvariant())));
        return best;
    }

    private static double JaroWinkler(string s1, string s2)
    {
        if (s1 == s2) return 1;
        if (s1.Length == 0 || s2.Length == 0) return 0;
        var range = Math.Max(0, Math.Max(s1.Length, s2.Length) / 2 - 1);
        var m1 = new bool[s1.Length];
        var m2 = new bool[s2.Length];
        var matches = 0;
        for (var i = 0; i < s1.Length; i++)
        {
            for (int j = Math.Max(0, i - range), end = Math.Min(s2.Length, i + range + 1); j < end; j++)
            {
                if (m2[j] || s1[i] != s2[j]) continue;
                m1[i] = m2[j] = true;
                matches++;
                break;
            }
        }
        if (matches == 0) return 0;
        var t = 0;
        for (int i = 0, k = 0; i < s1.Length; i++)
        {
            if (!m1[i]) continue;
            while (!m2[k]) k++;
            if (s1[i] != s2[k]) t++;
            k++;
        }
        var jaro = (matches / (double)s1.Length + matches / (double)s2.Length + (matches - t / 2.0) / matches) / 3;
        var prefix = 0;
        for (var i = 0; i < Math.Min(4, Math.Min(s1.Length, s2.Length)) && s1[i] == s2[i]; i++) prefix++;
        return jaro + prefix * 0.1 * (1 - jaro);
    }

    private static double Dur(JsonNode x) => x["duration"]?.GetValue<double>() ?? 0;

    private static bool HasHangul(string s) => s.Any(c => c is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏');
    private static bool HasKana(string s) => s.Any(c => c is >= '぀' and <= 'ヿ');

    /// <summary>
    /// 가사가 곡의 원어인지 — 제목 · 가수 이름에 한글이 있으면 가사에도 한글이 꽤 있어야 하고(일본어 가나도 같게).
    /// LRCLib 에는 한국 곡의 로마자 · 영어 번역 가사가 원본처럼 올라와 있는 경우가 많다
    /// </summary>
    private static bool ScriptMatches(Track t, string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return false;
        var meta = $"{t.Title} {t.Artist} {t.Album}";
        var text = Regex.Replace(lyrics, @"\[\d+:\d+(?:\.\d+)?\]", "");
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return true;
        if (HasHangul(meta))
        {
            var ko = letters.Count(c => c is >= '가' and <= '힣');
            // 로마자 · 번역본은 한글이 0 이다. 영어가 많은 K-pop 도 살리려고 기준은 낮게
            return ko >= Math.Max(5, letters.Count * 0.03);
        }
        if (HasKana(meta))
        {
            var ja = letters.Count(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿');
            return ja >= Math.Max(5, letters.Count * 0.03);
        }
        return true;
    }

    // ───────────────────────── Musixmatch ─────────────────────────

    private const string MxmBase = "https://apic-desktop.musixmatch.com/ws/1.1/";
    private const string MxmApp = "web-desktop-app-v1.0";
    private readonly CookieContainer _mxmCookies = new();
    private HttpClient? _mxm;
    private string? _mxmToken;
    private DateTime _mxmTokenAt;

    private HttpClient Mxm()
    {
        if (_mxm != null) return _mxm;
        _mxmCookies.Add(new Uri(MxmBase), new Cookie("x-mxm-user-id", ""));
        _mxmCookies.Add(new Uri(MxmBase), new Cookie("AWSELB", "0"));
        _mxmCookies.Add(new Uri(MxmBase), new Cookie("AWSELBCORS", "0"));
        _mxm = new HttpClient(new HttpClientHandler { CookieContainer = _mxmCookies, UseCookies = true }) { Timeout = TimeSpan.FromSeconds(15) };
        _mxm.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Musixmatch/3.14.4564 Chrome/96.0.4664.45 Electron/16.0.0 Safari/537.36");
        return _mxm;
    }

    /// <summary>
    /// pear MusixMatch.ts 와 같게: 토큰(token.get) → macro.subtitles.get (namespace lyrics_richsynched, subtitle_format lrc).
    /// 곡을 못 찾았거나 엉뚱한 곡(track_id 115264642 'Coldplay - Paradise')이면 없음. 401 이면 토큰을 새로 받는다
    /// </summary>
    private async Task<LyricsResult?> Musixmatch(Track t, CancellationToken ct)
    {
        foreach (var title in new[] { t.Title, Clean(t.Title) }.Distinct())
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (_mxmToken == null || DateTime.UtcNow - _mxmTokenAt > TimeSpan.FromMinutes(10))
                {
                    _mxmToken = await MxmToken(ct);
                    _mxmTokenAt = DateTime.UtcNow;
                }
                if (_mxmToken == null) return null;
                var url = $"{MxmBase}macro.subtitles.get?format=json&namespace=lyrics_richsynched&subtitle_format=lrc&app_id={MxmApp}" +
                          $"&q_track={Uri.EscapeDataString(title)}&q_artist={Uri.EscapeDataString(t.Artist)}" +
                          (t.DurationSec > 0 ? $"&q_duration={t.DurationSec}" : "") +
                          (t.Album != null ? $"&q_album={Uri.EscapeDataString(t.Album)}" : "") +
                          $"&usertoken={Uri.EscapeDataString(_mxmToken)}";
                var json = JsonNode.Parse(await Mxm().GetStringAsync(url, ct));
                if (json.P("message", "header", "status_code")?.GetValue<int>() == 401) { _mxmToken = null; continue; }
                var calls = json.P("message", "body", "macro_calls");
                var track = calls.P("matcher.track.get", "message", "body", "track");
                if (track == null || track["track_id"]?.GetValue<long>() == 115264642) break;
                var sub = calls.P("track.subtitles.get", "message", "body", "subtitle_list", 0, "subtitle", "subtitle_body").Str();
                if (Synced(Parse(sub)) is { } synced) return synced;
                var plain = calls.P("track.lyrics.get", "message", "body", "lyrics", "lyrics_body").Str();
                // 무료 응답 끝의 '******* This Lyrics is NOT for Commercial use' 같은 줄은 뺀다
                if (plain != null) plain = string.Join('\n', plain.Split('\n').TakeWhile(l => !l.StartsWith("*******")));
                return Plain(plain);
            }
        }
        return null;
    }

    private async Task<string?> MxmToken(CancellationToken ct)
    {
        var json = JsonNode.Parse(await Mxm().GetStringAsync($"{MxmBase}token.get?app_id={MxmApp}", ct));
        return json.P("message", "body", "user_token").Str() is { Length: > 0 } tok && !tok.StartsWith("UpgradeOnly") ? tok : null;
    }

    // ───────────────────────── Genius ─────────────────────────

    private static readonly Regex PreloadedState = new(@"__PRELOADED_STATE__ = JSON\.parse\('(.*?)'\);", RegexOptions.Singleline);
    private static readonly Regex PreloadHtml = new(@"body"":{""html"":""(.*?)"",""children""", RegexOptions.Singleline);

    /// <summary>pear LyricsGenius.ts — 검색에서 제목 · 가수가 맞는 곡을 고르고, 곡 페이지에 실린 가사(글자)를 꺼낸다</summary>
    private async Task<LyricsResult?> Genius(Track t, CancellationToken ct)
    {
        var title = Clean(t.Title);
        var q = Uri.EscapeDataString($"{t.Artist} {title}");
        var search = JsonNode.Parse(await _http.GetStringAsync($"https://genius.com/api/search/song?q={q}&page=1&per_page=10", ct));
        if (search.P("response", "sections", 0, "hits") is not JsonArray hits || hits.Count == 0) return null;
        var best = hits.OrderByDescending(h =>
            (h.P("result", "title").Str() == title ? 1 : 0) +
            ((h.P("result", "primary_artist", "name").Str() ?? "").Contains(t.Artist) ? 1 : 0)).First();
        if (best.P("result", "primary_artist", "url").Str() == "https://genius.com/artists/Deleted-artist") return null;
        var path = best.P("result", "path").Str();
        if (path == null) return null;
        var html = await _http.GetStringAsync("https://genius.com" + path, ct);
        var state = PreloadedState.Match(html).Groups[1].Value.Replace("\\\"", "\"");
        var body = PreloadHtml.Match(state).Groups[1].Value;
        if (body.Length == 0) return null;
        body = body.Replace("\\/", "/").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\'", "'").Replace("\\\"", "\"");
        body = Regex.Replace(body, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        var text = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]+>", ""));
        if (text.Trim().Trim('[', ']').Equals("instrumental", StringComparison.OrdinalIgnoreCase)) return null;
        return Plain(text);
    }

    // ───────────────────────── 공통 ─────────────────────────

    /// <summary>제목의 (feat. …), [MV] 같은 꼬리를 떼서 검색이 잘 되게</summary>
    private static string Clean(string s)
    {
        s = Regex.Replace(s, @"\s*[\(\[](feat|ft|prod|with|official|mv|m/v|lyrics?)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        return s.Trim();
    }

    /// <summary>LRC → 줄 목록. 한 줄에 시간이 여럿이면 그만큼, [offset:] 태그 반영</summary>
    public static List<LyricLine>? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return null;
        var lines = new List<LyricLine>();
        var offset = 0;
        var timeRx = new Regex(@"^\[(\d+):(\d+(?:\.\d+)?)\]");
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.Trim();
            var off = Regex.Match(line, @"^\[offset:\s*([+-]?\d+)\]", RegexOptions.IgnoreCase);
            if (off.Success) { offset = int.Parse(off.Groups[1].Value); continue; }
            var times = new List<int>();
            Match m;
            while ((m = timeRx.Match(line)).Success)
            {
                times.Add(int.Parse(m.Groups[1].Value) * 60000 + (int)(double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * 1000));
                line = line[m.Length..];
            }
            // 단어별 시간 <mm:ss.xx> 는 뺀다
            var text = Regex.Replace(line, @"<\d+:\d+\.\d+>\s*", "").Trim();
            foreach (var ms in times) lines.Add(new LyricLine(ms + offset, text));
        }
        return lines.Count > 0 ? lines.OrderBy(l => l.TimeMs).ToList() : null;
    }
}
