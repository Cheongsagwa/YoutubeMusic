using System.Diagnostics;

namespace OneMusic.Core;

/// <summary>
/// 곡 ID → 실제 오디오 스트림 주소. yt-dlp 로 뽑는다
/// (YouTube 가 막는 방식을 바꿔도 가장 빨리 대응하는 도구. 처음 필요할 때 한 번 내려받는다).
/// YoutubeExplode 도 써 봤지만 주소는 빨리 주는데 재생 몇 초 만에 끊기는 주소(서명 처리 문제)라 뺐다.
/// 대신 다음 곡 주소를 미리 받아 둬서 곡이 바뀔 때는 바로 시작된다.
/// </summary>
public sealed class StreamResolver
{
    public static StreamResolver Instance { get; } = new();
    public string LastSource { get; private set; } = "";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Dictionary<string, (string Url, DateTime At)> _cache = new();
    /// <summary>
    /// yt-dlp 는 '폴더판'(yt-dlp_win.zip)을 쓴다 — 한 파일판(yt-dlp.exe)은 실행할 때마다 자기 자신을 임시 폴더에 풀어서 1초 넘게 더 걸린다.
    /// 폴더판을 못 받으면 한 파일판으로
    /// </summary>
    private static readonly string YtDlpDirExe = IOPath.Combine(Log.AppDataDir, "yt-dlp", "yt-dlp.exe");
    private static readonly string YtDlpOneFile = IOPath.Combine(Log.AppDataDir, "yt-dlp.exe");
    private static string YtDlpPath => File.Exists(YtDlpDirExe) ? YtDlpDirExe : YtDlpOneFile;
    private readonly SemaphoreSlim _ytdlpGate = new(1, 1);

    private readonly Dictionary<string, Task<string>> _prefetch = new();
    private readonly HttpClient _media = new(Net.Handler(TimeSpan.FromMinutes(5))) { Timeout = TimeSpan.FromMinutes(2) };
    /// <summary>오디오 파일 자체 (곡 하나 4~6MB, 30분짜리는 30MB 정도) — 최근 3곡만 들고 있는다 (지금 곡 · 다음 곡 · 마우스로 미리 받은 곡)</summary>
    private readonly Dictionary<string, Task<byte[]>> _audio = new();

    /// <summary>
    /// 곡 오디오를 통째로 메모리에 받아 온다 (YouTube 서버가 빨라서 4MB 가 0.1초 정도).
    /// Windows 재생기가 YouTube 주소를 직접 열면 곡에 따라 열자마자 끝나 버려서, 받아 둔 데이터를 재생기에 넘긴다.
    /// 덤으로 넘기기(시크)가 즉시 된다.
    /// </summary>
    public Task<byte[]> Audio(string videoId, bool fresh, CancellationToken ct)
    {
        lock (_audio)
        {
            if (!fresh && _audio.TryGetValue(videoId, out var t) && !t.IsFaulted && !t.IsCanceled) return t;
            var task = Download(videoId, fresh);
            _audio[videoId] = task;
            // 오래된 것은 버린다 (최근 3곡만 — 그 앞은 디스크 캐시에서 바로 다시 읽는다)
            while (_audio.Count > 3) _audio.Remove(_audio.Keys.First(k => k != videoId));
            return task.WaitAsync(ct);
        }
    }

    private readonly SemaphoreSlim _hoverGate = new(2, 2);

    /// <summary>
    /// 마우스를 곡 위에 잠깐(0.35초) 올려 두면 미리 받기 시작 — 누를 때 yt-dlp 를 기다리지 않게.
    /// 동시에 2곡까지만 (훑고 지나가며 yt-dlp 를 여러 개 띄우지 않게)
    /// </summary>
    public static void HoverPrefetch(UIElement el, Func<string?> videoId)
    {
        DispatcherQueueTimer? timer = null;
        el.PointerEntered += (_, _) =>
        {
            timer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(350);
            timer.IsRepeating = false;
            timer.Tick -= Fire;
            timer.Tick += Fire;
            timer.Start();
        };
        el.PointerExited += (_, _) => timer?.Stop();
        void Fire(DispatcherQueueTimer t, object o)
        {
            var id = videoId();
            if (string.IsNullOrEmpty(id)) return;
            lock (Instance._audio) if (Instance._audio.ContainsKey(id)) return;
            if (!Instance._hoverGate.Wait(0)) return;
            _ = Instance.Audio(id, false, CancellationToken.None)
                .ContinueWith(_ => Instance._hoverGate.Release(), TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    /// <summary>다음 곡을 미리 받아 둔다 (곡이 바뀔 때 바로 시작되게)</summary>
    public void Prefetch(string videoId)
    {
        _ = Audio(videoId, false, CancellationToken.None).ContinueWith(_ => { });
        if (Settings.Current.Normalize) _ = Loudness(videoId).ContinueWith(_ => { });
    }

    /// <summary>
    /// 2MB 조각으로 나눠 동시에 6개씩 받는다.
    /// YouTube 는 큰 파일을 한 번에 달라고 하면 재생 속도 정도로 느리게 줘서, 30분 넘는 곡은 시간 안에 다 못 받았다
    /// </summary>
    private async Task<byte[]> Download(string videoId, bool fresh)
    {
        // 오프라인 저장한 곡이면 인터넷 없이
        if (!fresh && await Offline.Instance.Read(videoId) is { } saved)
        {
            lock (_quality) _quality[videoId] = "오프라인 저장본 (저장할 때 받은 음질)";
            return saved;
        }
        // 전에 들었던 곡이면 디스크 캐시에서 (yt-dlp · 인터넷 없이 바로)
        if (!fresh && await AudioCache.Read(videoId, PremiumReady) is { } cached)
        {
            Log.Info($"오디오 캐시에서 {videoId}: {cached.Bytes.Length / 1024} KB{(cached.Hq ? " (256k)" : "")}");
            SetQuality(videoId, cached.Hq ? "141" : "140", "디스크 캐시");
            return cached.Bytes;
        }
        var url = await Resolve(videoId, fresh, CancellationToken.None);
        var sw = Stopwatch.StartNew();
        const int chunk = 2 << 20;
        (byte[] Data, long? Total) first;
        try { first = await Range(url, 0, chunk - 1); }
        catch (Exception e) when (!fresh)
        {
            // 빠른 길로 받은 주소가 막혔으면 yt-dlp 주소로 한 번 더
            Log.Warn($"오디오 첫 조각 실패 ({e.Message}) → 새 주소로");
            url = await Resolve(videoId, true, CancellationToken.None);
            first = await Range(url, 0, chunk - 1);
        }
        var firstMs = sw.ElapsedMilliseconds;
        var total = first.Total ?? first.Data.Length;
        var bytes = new byte[total];
        first.Data.CopyTo(bytes, 0);
        if (total > first.Data.Length)
        {
            using var gate = new SemaphoreSlim(6);
            var tasks = new List<Task>();
            for (long start = first.Data.Length; start < total; start += chunk)
            {
                var from = start;
                var to = Math.Min(start + chunk, total) - 1;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        // 조각 하나가 실패하면 두 번까지 다시
                        for (var attempt = 0; ; attempt++)
                        {
                            try
                            {
                                var part = await Range(url, from, to);
                                part.Data.CopyTo(bytes, from);
                                return;
                            }
                            catch when (attempt < 2) { await Task.Delay(300 * (attempt + 1)); }
                        }
                    }
                    finally { gate.Release(); }
                }));
            }
            await Task.WhenAll(tasks);
        }
        var fixedCount = Mp4Fix.NeutralizeEditLists(bytes);
        var client = System.Text.RegularExpressions.Regex.Match(url, @"[?&]c=([^&]+)").Groups[1].Value;
        Log.Info($"오디오 받음 {videoId}: {bytes.Length / 1024} KB / {sw.ElapsedMilliseconds} ms (첫 조각 {firstMs} ms, 클라이언트 {client}){(fixedCount > 0 ? " (편집 목록 정리)" : "")}");
        var itag = System.Text.RegularExpressions.Regex.Match(url, @"[?&]itag=(\d+)").Groups[1].Value;
        SetQuality(videoId, itag, null);
        AudioCache.Write(videoId, bytes, itag == "141");
        return bytes;
    }

    private async Task<(byte[] Data, long? Total)> Range(string url, long from, long to)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Range", $"bytes={from}-{to}");
        // ANDROID_VR 로 받은 주소는 같은 앱인 척 받아야 느려지지 않는다
        if (url.Contains("c=ANDROID_VR")) req.Headers.TryAddWithoutValidation("User-Agent", VrAgent);
        using var res = await _media.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadAsByteArrayAsync();
        // 범위를 무시하고 전체(200)를 주면 그게 곧 전부
        long? total = res.StatusCode == System.Net.HttpStatusCode.PartialContent ? res.Content.Headers.ContentRange?.Length : data.Length;
        if (from > 0 && data.Length != to - from + 1) throw new IOException($"조각 크기 이상 {data.Length}/{to - from + 1}");
        return (data, total);
    }

    /// <summary>전문 통계용 — 받은 주소의 형식(itag · mime) · 크기 · 출처</summary>
    public List<(string, string)> Info(string videoId)
    {
        var list = new List<(string, string)>();
        string? url = null;
        lock (_cache) if (_cache.TryGetValue(videoId, out var c)) url = c.Url;
        if (Offline.Instance.Has(videoId)) list.Add(("출처", "오프라인 저장"));
        else list.Add(("출처", url != null ? LastSource switch { "innertube" => "YouTube (직접)", "premium" => "yt-dlp (YouTube Music 프리미엄)", _ => "yt-dlp (YouTube)" } : "-"));
        lock (_quality) if (_quality.TryGetValue(videoId, out var qa)) list.Insert(0, ("음질", qa));
        if (url != null)
        {
            var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
            if (q["itag"] is { } itag) list.Add(("itag", itag));
            if (q["mime"] is { } mime) list.Add(("형식", mime));
            if (q["clen"] is { } clen && long.TryParse(clen, out var n)) list.Add(("크기", $"{n / 1024.0 / 1024.0:0.0} MB"));
            if (q["dur"] is { } dur) list.Add(("길이", $"{dur} 초"));
            if (q["expire"] is { } exp && long.TryParse(exp, out var e))
                list.Add(("주소 만료", DateTimeOffset.FromUnixTimeSeconds(e).ToLocalTime().ToString("HH:mm")));
        }
        lock (_audio)
            if (_audio.TryGetValue(videoId, out var a) && a.IsCompletedSuccessfully)
                list.Add(("메모리", $"{a.Result.Length / 1024.0 / 1024.0:0.0} MB"));
        return list;
    }

    public async Task<string> Resolve(string videoId, bool useFallback, CancellationToken ct)
    {
        Task<string>? pending = null;
        lock (_cache)
        {
            if (!useFallback && _prefetch.Remove(videoId, out var pf)) pending = pf;
        }
        if (pending != null)
        {
            try { return await pending.WaitAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch { /* 미리 받기 실패 → 아래에서 다시 */ }
        }
        lock (_cache)
        {
            // 스트림 주소는 몇 시간 유효 — 1시간 안이면 다시 쓴다
            if (!useFallback && _cache.TryGetValue(videoId, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromHours(1))
                return c.Url;
        }
        string url;
        // 1차: YouTube 내부 API 를 직접 (0.2~0.5초). yt-dlp 는 실행할 때마다 압축을 풀고 페이지를 받느라 2~3초 걸린다
        // ⚠️ 꺼 둠: 주소는 0.1초에 오지만 YouTube 가 그 주소의 첫 바이트를 3.8초쯤 붙잡고 있어서 결국 더 느렸다
        if (!useFallback && UseDirectPlayerApi)
        {
            try
            {
                var fast = await ViaInnerTube(videoId, ct);
                if (fast != null)
                {
                    Remember(videoId, fast);
                    LastSource = "innertube";
                    return fast;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { Log.Warn($"빠른 주소 받기 실패 ({videoId}): {e.Message} → yt-dlp"); }
        }
        // 프리미엄: 로그인 쿠키 + web_music 클라이언트로 AAC 256k(141). 안 되면 아래 보통 길(128k)
        if (!useFallback && PremiumReady)
        {
            try
            {
                var hq = await ViaPremium(videoId, ct);
                Remember(videoId, hq);
                LastSource = "premium";
                return hq;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { Log.Warn($"높은 음질 주소 실패 ({videoId}): {e.Message} → 보통 음질"); }
        }
        try { url = await ViaYtDlp(videoId, ct); }
        catch (Exception e) when (e is not OperationCanceledException and not UnplayableException && !_updatedAfterFail)
        {
            // YouTube 가 바뀌어서 옛 yt-dlp 가 실패하는 경우가 많다 → 한 번 새 버전으로 올리고 다시
            _updatedAfterFail = true;
            Log.Warn($"yt-dlp 실패 → 업데이트 후 다시: {e.Message}");
            await UpdateYtDlp();
            url = await ViaYtDlp(videoId, ct);
        }
        Remember(videoId, url);
        LastSource = "yt-dlp";
        return url;
    }

    private readonly HttpClient _player = new(Net.Handler()) { Timeout = TimeSpan.FromSeconds(6) };

    // ── 음질 ──
    private readonly Dictionary<string, string> _quality = new();

    /// <summary>전문 통계의 '음질' 줄 (itag 로 코덱 · 비트레이트)</summary>
    private void SetQuality(string videoId, string itag, string? from)
    {
        var q = itag switch
        {
            "141" => "높음 · AAC 256kbps 44.1kHz (프리미엄)",
            "140" => "보통 · AAC 128kbps 44.1kHz",
            "139" => "낮음 · HE-AAC 48kbps",
            "" => "알 수 없음",
            _ => $"itag {itag}",
        };
        lock (_quality) _quality[videoId] = from != null ? $"{q} · {from}" : q;
    }

    /// <summary>높은 음질을 쓸 수 있나 — 설정 켜짐 + 로그인 + JS 실행기 (yt-dlp 가 web_music 서명을 풀 때 필요) + 이번 실행에서 막히지 않음</summary>
    public bool PremiumReady => Settings.Current.HighQuality && Auth.IsLoggedIn && !_premiumOff && JsRuntime != null;

    private bool _premiumOff;
    private int _premiumMiss;
    private static string? _js;
    private static bool _jsChecked;

    /// <summary>yt-dlp 가 쓸 JS 실행기 ("node:경로" / "deno:경로"), 없으면 null</summary>
    private static string? JsRuntime
    {
        get
        {
            if (_jsChecked) return _js;
            _jsChecked = true;
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Append(IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"))
                .Append(IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".deno", "bin"))
                .ToList();
            foreach (var (name, exe) in new[] { ("deno", "deno.exe"), ("node", "node.exe") })
                foreach (var d in dirs)
                {
                    try
                    {
                        var path = IOPath.Combine(d.Trim('"'), exe);
                        if (File.Exists(path)) return _js = $"{name}:{path}";
                    }
                    catch { /* 이상한 PATH 항목 */ }
                }
            Log.Info("JS 실행기(node · deno) 없음 → 높은 음질 못 씀");
            return null;
        }
    }

    /// <summary>
    /// 프리미엄 256k 주소 — 로그인 쿠키를 잠깐 파일로 써서 yt-dlp web_music 클라이언트로 받는다 (쿠키 파일은 바로 지움).
    /// 141 이 아닌 게 3번 연속 오면 프리미엄이 아닌 계정으로 보고 이번 실행 동안 끈다
    /// </summary>
    private async Task<string> ViaPremium(string videoId, CancellationToken ct)
    {
        await EnsureYtDlp(ct);
        var cookies = IOPath.Combine(Log.AppDataDir, $"yt-cookies-{Guid.NewGuid():N}.txt");
        try
        {
            Auth.WriteNetscape(cookies);
            var url = await RunYtDlp(videoId,
                $"-f \"141/bestaudio[ext=m4a]\" -g --no-warnings --no-playlist --js-runtimes \"{JsRuntime}\" --cookies \"{cookies}\" " +
                $"--extractor-args \"youtube:player_client=web_music\" \"https://music.youtube.com/watch?v={videoId}\"", ct);
            if (url.Contains("itag=141")) _premiumMiss = 0;
            else if (++_premiumMiss >= 3)
            {
                _premiumOff = true;
                Log.Warn("256k 가 3번 연속 안 나옴 → 프리미엄 아닌 계정으로 보고 이번 실행 동안 보통 길로");
            }
            return url;
        }
        finally
        {
            try { File.Delete(cookies); } catch { }
        }
    }

    private const bool UseDirectPlayerApi = false;
    private const string VrVersion = "1.65.10";
    private const string VrAgent = "com.google.android.apps.youtube.vr.oculus/" + VrVersion +
                                   " (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip";

    /// <summary>
    /// YouTube 내부 player API 를 ANDROID_VR 클라이언트로 불러 오디오 주소를 바로 받는다
    /// (이 클라이언트는 서명 풀기 · PoToken 이 필요 없는 주소를 준다 — yt-dlp 도 같은 방법을 쓴다).
    /// 재생할 수 없는 응답이면 null → yt-dlp 로
    /// </summary>
    /// <summary>ANDROID_VR player 응답 (주소 · 소리 크기). 실패하면 null</summary>
    private async Task<JsonNode?> VrPlayer(string videoId, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["context"] = new JsonObject
            {
                ["client"] = new JsonObject
                {
                    ["clientName"] = "ANDROID_VR",
                    ["clientVersion"] = VrVersion,
                    ["deviceMake"] = "Oculus",
                    ["deviceModel"] = "Quest 3",
                    ["androidSdkVersion"] = 32,
                    ["osName"] = "Android",
                    ["osVersion"] = "12L",
                    ["hl"] = "ko",
                    ["gl"] = "KR",
                },
            },
            ["videoId"] = videoId,
            ["contentCheckOk"] = true,
            ["racyCheckOk"] = true,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/player?prettyPrint=false")
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("User-Agent", VrAgent);
        req.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", "28");
        req.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", VrVersion);
        // 방문자 식별값이 없으면 YouTube 가 '로그인 필요(봇 확인)'로 막는다
        var visitor = InnerTube.Instance.VisitorData;
        if (visitor != null)
        {
            ((JsonObject)body["context"]!["client"]!)["visitorData"] = visitor;
            req.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            req.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", visitor);
        }
        using var res = await _player.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    private readonly Dictionary<string, double?> _loudness = new();

    /// <summary>
    /// 곡의 소리 크기 (YouTube 가 재 둔 loudnessDb — 기준보다 몇 dB 큰지, 음수면 작은 곡). 음량 표준화에 쓴다.
    /// 못 받으면 null (그 곡은 그대로 튼다)
    /// </summary>
    public async Task<double?> Loudness(string videoId, CancellationToken ct = default)
    {
        lock (_loudness) if (_loudness.TryGetValue(videoId, out var known)) return known;
        double? db = null;
        try
        {
            var json = await VrPlayer(videoId, ct);
            var n = json.P("playerConfig", "audioConfig", "loudnessDb") ?? json.P("streamingData", "adaptiveFormats").FindFirst("loudnessDb");
            if (n != null && double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) db = v;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { Log.Warn($"소리 크기 받기 실패 ({videoId}): {e.Message}"); }
        lock (_loudness) _loudness[videoId] = db;
        return db;
    }

    private async Task<string?> ViaInnerTube(string videoId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var json = await VrPlayer(videoId, ct);
        if (json == null) return null;
        var status = json.P("playabilityStatus", "status").Str();
        if (status != "OK")
        {
            Log.Info($"빠른 주소: {videoId} 재생 불가 ({status}, 방문자 {(InnerTube.Instance.VisitorData != null ? "있음" : "없음")}) → yt-dlp");
            return null;
        }
        if (json.P("streamingData", "adaptiveFormats") is not JsonArray formats) return null;
        // 서명 없이 바로 쓸 수 있는 주소가 있는 오디오만, Windows 재생기가 확실히 여는 AAC(mp4) 우선
        var audio = formats
            .Where(f => (f.P("mimeType").Str() ?? "").StartsWith("audio/") && f.P("url").Str() != null)
            .OrderByDescending(f => (f.P("mimeType").Str() ?? "").StartsWith("audio/mp4") ? 1 : 0)
            .ThenByDescending(f => (long?)f?["bitrate"]?.GetValue<double>() ?? 0)
            .FirstOrDefault();
        var url = audio.P("url").Str();
        if (url == null || !(audio.P("mimeType").Str() ?? "").StartsWith("audio/mp4")) return null;
        Log.Info($"빠른 주소 받음 {videoId}: itag {audio.P("itag").Str()} / {sw.ElapsedMilliseconds} ms");
        return url;
    }

    private void Remember(string id, string url)
    {
        lock (_cache)
        {
            // 한 시간 지난 주소는 어차피 다시 받으니 버린다
            if (_cache.Count > 200)
                foreach (var k in _cache.Where(x => DateTime.UtcNow - x.Value.At > TimeSpan.FromHours(1)).Select(x => x.Key).ToList())
                    _cache.Remove(k);
            _cache[id] = (url, DateTime.UtcNow);
        }
    }

    private bool _updateChecked, _updatedAfterFail;

    /// <summary>yt-dlp 가 사흘 넘게 묵었으면 뒤에서 조용히 새 버전으로 (YouTube 가 막는 방식을 바꿔도 계속 재생되게)</summary>
    private void MaybeUpdate()
    {
        if (_updateChecked) return;
        _updateChecked = true;
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(YtDlpPath) < TimeSpan.FromDays(3)) return;
        }
        catch { return; }
        _ = Task.Run(UpdateYtDlp);
    }

    private async Task UpdateYtDlp()
    {
        await _ytdlpGate.WaitAsync();
        try
        {
            var psi = new ProcessStartInfo(YtDlpPath, "-U --no-warnings")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            // 이미 최신이어도 다음 확인은 사흘 뒤
            try { File.SetLastWriteTimeUtc(YtDlpPath, DateTime.UtcNow); } catch { }
            Log.Info($"yt-dlp 업데이트 확인: {output.Trim().Split('\n').LastOrDefault()}");
        }
        catch (Exception e) { Log.Warn($"yt-dlp 업데이트 실패: {e.Message}"); }
        finally { _ytdlpGate.Release(); }
    }

    /// <summary>
    /// 뮤직비디오 재생용 DASH 목록(MPD). 소리 · 화면이 한 파일에 든 mp4(18)는 이제 주소가 막혀서(403)
    /// 화면(avc1 mp4, 1080p 까지)과 소리(m4a)를 따로 두고 Windows 재생기(AdaptiveMediaSource)가 맞춰 흘려 받는다.
    /// (HLS 는 YouTube 소리 조각을 Windows 가 못 열어서 안 된다)
    /// </summary>
    public async Task<string> VideoManifest(string videoId, CancellationToken ct)
    {
        var key = "v:" + videoId;
        lock (_cache)
            if (_cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromHours(1)) return c.Url;
        var json = JsonNode.Parse(await ViaYtDlp(videoId, ct, "-f \"bestvideo[ext=mp4][vcodec^=avc1][height<=1080]+bestaudio[ext=m4a]\" -j"));
        if (json.P("requested_formats") is not JsonArray formats || formats.Count != 2)
            throw new InvalidOperationException("영상 · 소리 포맷을 못 찾음");
        var ranges = await Task.WhenAll(formats.Select(f => BoxRanges(f.P("url").Str()!)));
        var sets = new System.Text.StringBuilder();
        for (var i = 0; i < formats.Count; i++)
        {
            var f = formats[i]!;
            var video = f.P("vcodec").Str() is { } vc && vc != "none";
            var (initEnd, idxStart, idxEnd) = ranges[i];
            var size = video ? $" width=\"{f.P("width").Str()}\" height=\"{f.P("height").Str()}\"" : "";
            var bandwidth = (long)((f["tbr"]?.GetValue<double>() ?? 128) * 1000);
            sets.Append($"<AdaptationSet mimeType=\"{(video ? "video" : "audio")}/mp4\" startWithSAP=\"1\" segmentAlignment=\"true\">")
                .Append($"<Representation id=\"{f.P("format_id").Str()}\" codecs=\"{(video ? f.P("vcodec").Str() : f.P("acodec").Str())}\" bandwidth=\"{bandwidth}\"{size}>")
                .Append($"<BaseURL>{System.Net.WebUtility.HtmlEncode(f.P("url").Str())}</BaseURL>")
                .Append($"<SegmentBase indexRange=\"{idxStart}-{idxEnd}\"><Initialization range=\"0-{initEnd}\"/></SegmentBase>")
                .Append("</Representation></AdaptationSet>");
        }
        var duration = json!["duration"]?.GetValue<double>() ?? 0;
        var mpd = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" profiles=\"urn:mpeg:dash:profile:isoff-on-demand:2011\" " +
                  $"type=\"static\" minBufferTime=\"PT1.5S\" mediaPresentationDuration=\"PT{duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}S\"><Period>{sets}</Period></MPD>";
        Remember(key, mpd);
        return mpd;
    }

    /// <summary>mp4 앞부분의 상자 위치 — moov 끝까지가 Initialization, sidx 가 indexRange</summary>
    private async Task<(long InitEnd, long IdxStart, long IdxEnd)> BoxRanges(string url)
    {
        var b = (await Range(url, 0, 32767)).Data;
        long pos = 0, initEnd = -1;
        while (pos + 8 <= b.Length)
        {
            long size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan((int)pos));
            if (size < 8) break;
            var type = System.Text.Encoding.ASCII.GetString(b, (int)pos + 4, 4);
            if (type == "moov") initEnd = pos + size - 1;
            else if (type == "sidx" && initEnd > 0) return (initEnd, pos, pos + size - 1);
            pos += size;
        }
        throw new InvalidOperationException("영상 색인(sidx)을 못 찾음");
    }

    /// <param name="what">yt-dlp 에 넘길 포맷 · 출력 (-g = 주소, -j = 정보 JSON)</param>
    private async Task<string> ViaYtDlp(string videoId, CancellationToken ct, string what = "-f \"bestaudio[ext=m4a]/bestaudio\" -g")
    {
        await EnsureYtDlp(ct);
        var url = $"\"https://www.youtube.com/watch?v={videoId}\"";
        var common = $"{what} --no-warnings --no-playlist";
        // 빠른 길: 웹페이지 · 설정 받기를 건너뛰고 방문자 식별값을 직접 넘긴다 (요청 2~3개 줄어듦)
        var visitor = InnerTube.Instance.VisitorData;
        if (visitor != null && !_skipFastArgs)
        {
            try
            {
                return await RunYtDlp(videoId, $"{common} --extractor-args \"youtube:player_skip=webpage,configs;visitor_data={visitor}\" {url}", ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (UnplayableException) { /* 빠른 길에서만 '없는 영상'이라 할 수도 있어서 원래 방식으로 한 번 더 확인 */ }
            catch (Exception e)
            {
                _skipFastArgs = true; // 이번 실행 동안은 원래 방식으로
                Log.Warn($"yt-dlp 빠른 길 실패 → 원래 방식: {e.Message}");
            }
        }
        return await RunYtDlp(videoId, $"{common} {url}", ct);
    }

    private bool _skipFastArgs;

    /// <summary>
    /// 짝 영상이 없는 음원의 원본 영상 찾기 — 누가 유튜브 영상 소리를 음원으로 따로 올린 곡(예: 'Main Theme' ← 'Playlist) 오버워치 2026 OWCS KOREA Main Theme').
    /// '앨범 제목' · '(같은 앨범 원본을 올린 채널) 제목' · '제목 가수' 로 찾아서, 제목이 들어 있고 길이가 ±3초 안이고 '- Topic'(음원 채널)이 아닌 첫 영상
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex NotOriginal = new(
        @"lyric|가사|cover|커버|reaction|리액션|karaoke|노래방|MR|inst(rumental)?|sped ?up|slowed|nightcore|8d audio|1 ?hour|1시간|loop|live|라이브|stage|무대|교차|fancam|직캠|piano|피아노|remix",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public async Task<string?> FindOriginalVideo(Track t, CancellationToken ct = default)
    {
        var queries = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.Album) && t.Album != t.Title) queries.Add($"{t.Album} {t.Title}");
        if (!string.IsNullOrWhiteSpace(t.Album) && VideoMeta.AlbumChannel(t.Album) is { } ch) queries.Add($"{ch} {t.Title}");
        queries.Add($"{t.Title} {t.Artist}".Trim());
        var artist = t.Artist?.Split(',', '&')[0].Trim() ?? "";
        var keepAll = NotOriginal.IsMatch(t.Title); // 곡 이름부터 'Live' · 'Remix' 면 거르지 않는다
        foreach (var q in queries.Distinct())
        {
            // 가사 · 커버 · 라이브 같은 남의 영상은 빼고, 가수 이름 채널(공식)을 먼저
            var hit = (await SearchVideos(q, ct)).Where(h =>
                h.Id != t.VideoId && !h.Channel.EndsWith("- Topic") && (keepAll || !NotOriginal.IsMatch(h.Title)) &&
                h.Duration > 0 && Math.Abs(h.Duration - t.DurationSec) <= 3 &&
                h.Title.Contains(t.Title, StringComparison.OrdinalIgnoreCase))
                .OrderBy(h => artist.Length > 0 && h.Channel.Contains(artist, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
            if (hit != null)
            {
                VideoMeta.Set(hit);
                if (!string.IsNullOrWhiteSpace(t.Album) && hit.Channel.Length > 0) VideoMeta.SetAlbumChannel(t.Album, hit.Channel);
                Log.Info($"원본 영상 찾음: {t.Title} → {hit.Id} ({hit.Title})");
                return hit.Id;
            }
        }
        return null;
    }

    /// <summary>영상 하나의 제목 · 채널 (yt-dlp, 받지는 않음) — 기억해 둔 게 없을 때만</summary>
    public async Task<VideoHit?> VideoInfo(string videoId, CancellationToken ct = default)
    {
        if (VideoMeta.Get(videoId) is { } known) return known;
        await EnsureYtDlp(ct);
        var psi = new ProcessStartInfo(YtDlpPath, $"--skip-download --no-warnings --encoding utf-8 --print \"%(title)s	%(duration)s	%(channel)s\" \"https://www.youtube.com/watch?v={videoId}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp 실행 실패");
        _ = p.StandardError.ReadToEndAsync(ct);
        var f = (await p.StandardOutput.ReadToEndAsync(ct)).Trim().Split('	');
        await p.WaitForExitAsync(ct);
        if (f.Length < 3 || f[0].Length == 0) return null;
        double.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dur);
        var h = new VideoHit(videoId, f[0], f[2] == "NA" ? "" : f[2], dur);
        VideoMeta.Set(h);
        return h;
    }

    /// <summary>유튜브 동영상 검색 (yt-dlp ytsearch, 목록만 — 2~4초) — 동영상 연결 고르기</summary>
    public async Task<List<VideoHit>> SearchVideos(string query, CancellationToken ct = default)
    {
        await EnsureYtDlp(ct);
        var q = query.Replace("\"", " ");
        var psi = new ProcessStartInfo(YtDlpPath, $"--flat-playlist --no-warnings --encoding utf-8 --print \"%(id)s\t%(title)s\t%(duration)s\t%(channel)s\" \"ytsearch10:{q}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp 실행 실패");
        _ = p.StandardError.ReadToEndAsync(ct);
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var list = new List<VideoHit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split('\t');
            if (f.Length < 4 || f[0].Length != 11) continue;
            double.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dur);
            list.Add(new VideoHit(f[0], f[1], f[3] == "NA" ? "" : f[3], dur));
        }
        return list;
    }

    private async Task<string> RunYtDlp(string videoId, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(YtDlpPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var swYt = Stopwatch.StartNew();
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp 실행 실패");
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var result = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(result) || !(result.StartsWith("http") || result.StartsWith('{')))
        {
            // 지역 제한 · 삭제된 곡 — 어떤 클라이언트 · 로그인으로도 안 열린다 (yt-dlp 를 올려도 소용없음)
            if (err.Contains("Video unavailable") || err.Contains("This video is not available") || err.Contains("content isn't available"))
                throw new UnplayableException(err.Trim());
            throw new InvalidOperationException($"yt-dlp 실패: {err.Trim()}");
        }
        Log.Info($"yt-dlp 주소 {videoId}: {swYt.ElapsedMilliseconds} ms{(args.Contains("player_skip") ? " (빠른 길)" : "")}{(YtDlpPath == YtDlpDirExe ? " · 폴더판" : "")}");
        return result;
    }

    private async Task EnsureYtDlp(CancellationToken ct)
    {
        if (File.Exists(YtDlpDirExe))
        {
            MaybeUpdate();
            return;
        }
        await _ytdlpGate.WaitAsync(ct);
        try
        {
            if (File.Exists(YtDlpDirExe)) return;
            try
            {
                Log.Info("yt-dlp 폴더판 내려받는 중…");
                var zip = await _http.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_win.zip", ct);
                var dir = IOPath.GetDirectoryName(YtDlpDirExe)!;
                var tmp = dir + ".part";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                using (var ms = new MemoryStream(zip))
                    System.IO.Compression.ZipFile.ExtractToDirectory(ms, tmp, true);
                // 압축 안에 폴더가 하나 더 있으면 그 안의 것을 쓴다
                var exe = Directory.GetFiles(tmp, "yt-dlp.exe", SearchOption.AllDirectories).FirstOrDefault()
                          ?? throw new InvalidOperationException("압축 안에 yt-dlp.exe 없음");
                var root = IOPath.GetDirectoryName(exe)!;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.Move(root, dir);
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Log.Info($"yt-dlp 폴더판 준비됨 ({zip.Length / 1024 / 1024} MB)");
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Log.Warn($"yt-dlp 폴더판 실패 → 한 파일판: {e.Message}"); }
            if (File.Exists(YtDlpOneFile)) return;
            Log.Info("yt-dlp 내려받는 중…");
            var bytes = await _http.GetByteArrayAsync(
                "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", ct);
            var part = YtDlpOneFile + ".part";
            await File.WriteAllBytesAsync(part, bytes, ct);
            File.Move(part, YtDlpOneFile, true);
            Log.Info($"yt-dlp 준비됨 ({bytes.Length / 1024 / 1024} MB)");
        }
        finally
        {
            _ytdlpGate.Release();
        }
    }
}

/// <summary>이 지역에서 막혔거나 지워진 곡 — 다시 해도 안 열리니 건너뛴다</summary>
public sealed class UnplayableException(string message) : Exception(message);
