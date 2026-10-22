using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;

namespace OneMusic.Core;

/// <summary>
/// 재생 담당. Windows 기본 재생기(MediaPlayer)를 쓰므로 키보드 미디어 키 · 작업 표시줄 · 잠금 화면 미디어 컨트롤이 그냥 된다.
/// 모든 이벤트는 화면(UI) 스레드에서 알린다.
/// </summary>
public sealed class PlayerService
{
    public static PlayerService Instance { get; private set; } = null!;

    private readonly MediaPlayer _mp = new() { AudioCategory = MediaPlayerAudioCategory.Media, AutoPlay = false };
    private readonly DispatcherQueue _ui;
    private CancellationTokenSource? _loadCts;
    private bool _triedFallback;
    private bool _extending;
    private DateTime _openedAt;

    public List<Track> Queue { get; private set; } = new();
    public int Index { get; private set; } = -1;
    public Track? Current => Index >= 0 && Index < Queue.Count
        ? _shownAs is { } s && s.VideoId == Queue[Index].VideoId ? s : Queue[Index]
        : null;

    /// <summary>
    /// 이어 준 영상(VideoLinks)으로 보는 동안 보여 줄 모습 — 제목 · 채널 · 썸네일을 영상 것으로 (목록 · 이어 듣기에는 원래 곡 그대로).
    /// 무단 음원의 엉뚱한 가수 이름 · 아트 대신 원본 영상 그대로 보이게
    /// </summary>
    private Track? _shownAs;
    public RepeatMode Repeat { get; private set; } = Settings.Current.Repeat;
    /// <summary>'재생 중인 트랙 출처' 에 보일 이름 (예: NEW YORK 믹스)</summary>
    public string? QueueTitle { get; private set; }
    public string? RelatedBrowseId { get; private set; }
    /// <summary>
    /// 자동재생 구간이 시작하는 자리. Queue[..AutoStart] 는 사용자가 고른 곡(곡 하나 · 앨범 · 플레이리스트),
    /// Queue[AutoStart..] 는 '자동재생 사용 중' 아래에 보이는 비슷한 곡들 (칩으로 고른 알고리즘에 따라 바뀜)
    /// </summary>
    public int AutoStart { get; private set; }
    /// <summary>자동재생 칩 (전체 · 친숙한 곡 · 인기 · 처음 듣는 곡 · 숨은 명곡 · 2020년대 …)</summary>
    public List<QueueChip> AutoChips { get; private set; } = new();
    public bool AutoLoading { get; private set; }
    /// <summary>
    /// 곡 하나로 시작한 라디오(믹스)인지. YT Music 처럼 라디오는 목록 전체가 하나로 이어지고
    /// 칩이 목록 맨 위에 붙는다 (자동재생 줄 · '자동재생 사용 중' 구분 없음)
    /// </summary>
    public bool Radio { get; private set; }
    /// <summary>
    /// 자동재생 토글이 보이는지 — 검색에서 곡을 틀었을 때만 (pear 와 같게).
    /// 나머지(홈 카드 · 앨범 · 플레이리스트 · 라디오)는 토글 대신 알고리즘 칩이 맨 위에 오고, 목록은 늘 이어서 튼다
    /// </summary>
    public bool ShowAutoplayToggle { get; private set; }
    private int _autoVersion;
    /// <summary>지금 목록(긴 플레이리스트)의 나머지를 이어 받는 것 — 고른 곡들 끝에 다다르기 전에 붙인다</summary>
    private TrackPager? _pager;
    private bool _pagerShuffle;

    private void SetPager(TrackPager? pager, bool shuffle = false)
    {
        if (_pager != null) _pager.Loaded -= OnPagerLoaded;
        _pager = pager?.HasMore == true ? pager : null;
        _pagerShuffle = shuffle;
        if (_pager != null) _pager.Loaded += OnPagerLoaded;
    }

    /// <summary>이어 받은 묶음을 고른 곡들 끝(자동재생 구간 앞)에 붙인다 (셔플이면 묶음도 섞어서)</summary>
    private void OnPagerLoaded(List<Track> add)
    {
        var known = Queue.Take(AutoStart).Select(t => t.VideoId).ToHashSet();
        var list = add.Where(t => known.Add(t.VideoId)).ToList();
        if (_pagerShuffle) list = list.OrderBy(_ => Random.Shared.Next()).ToList();
        if (list.Count == 0) return;
        // 자동재생 구간에 같은 곡이 있으면 거기선 뺀다
        var ids = list.Select(t => t.VideoId).ToHashSet();
        for (var i = Queue.Count - 1; i >= AutoStart; i--)
            if (ids.Contains(Queue[i].VideoId) && i != Index)
            {
                Queue.RemoveAt(i);
                if (i < Index) Index--;
            }
        var at = Math.Min(AutoStart, Queue.Count);
        Queue.InsertRange(at, list);
        if (at <= Index) Index += list.Count;
        AutoStart = at + list.Count;
        QueueChanged?.Invoke();
    }

    /// <summary>고른 곡들 끝이 가까우면 나머지 묶음을 미리 받는다</summary>
    private void MaybePage()
    {
        if (_pager is { HasMore: true } pg && Index < AutoStart && Index + 5 >= AutoStart) _ = pg.Next();
    }

    /// <summary>자동재생 — 목록 끝에 다다르면 비슷한 곡을 이어서 튼다</summary>
    public bool Autoplay
    {
        get => Settings.Current.Autoplay;
        set
        {
            Settings.Current.Autoplay = value;
            QueueChanged?.Invoke();
            if (value && ShowAutoplayToggle && AutoStart >= Queue.Count && Queue.Count > 0) _ = LoadAuto(Queue[^1].VideoId, null, null);
        }
    }
    public bool Loading { get; private set; }

    /// <summary>지금 고른 출력 장치 이름 (null = 윈도우 기본 장치)</summary>
    public string? OutputName { get; private set; }

    /// <summary>
    /// 소리를 낼 장치 (pear custom-output-device) — null 이면 윈도우 기본 장치를 따라간다. 고른 것은 다음에 켤 때도
    /// </summary>
    public async Task SetOutput(string? deviceId)
    {
        try
        {
            if (deviceId == null)
            {
                try { _mp.AudioDevice = null; }
                catch
                {
                    var def = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(Windows.Media.Devices.AudioDeviceRole.Default);
                    if (def != null) _mp.AudioDevice = await Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(def);
                }
                OutputName = null;
            }
            else
            {
                var dev = await Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(deviceId);
                _mp.AudioDevice = dev;
                OutputName = dev.Name;
            }
            Settings.Current.AudioDevice = deviceId;
            Settings.Current.Save();
        }
        catch (Exception e)
        {
            Log.Warn($"출력 장치 바꾸기 실패: {e.Message}");
            // 장치가 빠졌으면 기본 장치로
            if (deviceId != null && Settings.Current.AudioDevice == deviceId)
            {
                Settings.Current.AudioDevice = null;
                Settings.Current.Save();
            }
        }
    }

    /// <summary>재생 속도 (pear playback-speed) — 곡이 바뀌어도 그대로</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            _speed = Math.Clamp(value, 0.25, 3);
            _mp.PlaybackSession.PlaybackRate = _speed;
        }
    }
    private double _speed = 1;

    /// <summary>재생기 (재생 화면이 동영상을 그릴 때 MediaPlayerElement 에 붙인다)</summary>
    public MediaPlayer Media => _mp;

    /// <summary>노래 / 동영상 전환 — 동영상이면 뮤직비디오(짝 ID)를 화면과 함께 튼다</summary>
    public bool VideoMode { get; private set; }
    /// <summary>지금 실제로 영상을 틀고 있는지 (동영상 모드여도 짝이 없는 곡은 노래로)</summary>
    public bool PlayingVideo { get; private set; }
    public event Action? VideoChanged;
    private double _resumeAt;
    private int _unplayableRun;

    /// <summary>이 곡의 영상 ID — 뮤직비디오 · 일반 동영상이면 자기 자신, 음원이면 짝(뮤직비디오)</summary>
    public static string? VideoIdFor(Track? t) =>
        t == null ? null : IsVideo(t) ? t.VideoId : VideoLinks.Get(t.VideoId) ?? t.CounterpartId ?? Pair(t.VideoId);

    /// <summary>next 로 따로 물어 알아낸 YT 공식 짝 (재생목록 · 앨범 곡은 짝 없이 와서)</summary>
    private static readonly Dictionary<string, string?> _pairs = new();
    private static string? Pair(string id) { lock (_pairs) return _pairs.GetValueOrDefault(id); }

    public static bool IsVideo(Track t) => t.VideoType is "MUSIC_VIDEO_TYPE_OMV" or "MUSIC_VIDEO_TYPE_UGC";

    /// <summary>
    /// 짝 영상이 안 붙어 온 음원이면 뒤에서 찾아 둔다 (실행마다 곡당 한 번).
    /// 먼저 YT 에 그 곡의 공식 짝(뮤직비디오)을 묻고, YT 도 모르는 곡(누가 영상 소리를 음원으로 올린 곡)만 유튜브 검색.
    /// 찾으면 동영상 버튼이 켜지고, 이미 동영상 모드였으면 그 영상으로 다시 연다
    /// </summary>
    private async void AutoLink(Track t)
    {
        if (IsVideo(t) || t.CounterpartId != null || VideoLinks.Known(t.VideoId) || !_autoLinkTried.Add(t.VideoId)) return;
        try
        {
            string? pair = null;
            try { pair = await InnerTube.Instance.Counterpart(t.VideoId); }
            catch (Exception e) { Log.Warn($"공식 짝 묻기 실패 ({t.Title}): {e.Message}"); }
            if (pair != null)
            {
                lock (_pairs) _pairs[t.VideoId] = pair;
                Ui(() => Relink(t));
                return;
            }
            if (t.DurationSec <= 0) return;
            var id = await StreamResolver.Instance.FindOriginalVideo(t);
            Ui(() =>
            {
                // 못 찾은 건 이번 실행 동안만 기억 (같은 앨범 채널을 알게 되면 다음엔 찾을 수 있다). 그새 직접 이어 줬으면 그대로
                if (id == null || VideoLinks.Known(t.VideoId)) return;
                LinkVideo(t, id);
            });
        }
        catch (Exception e) { Log.Warn($"원본 영상 찾기 실패 ({t.Title}): {e.Message}"); }
    }

    private readonly HashSet<string> _autoLinkTried = new();

    private MediaPlaybackItem? _item;

    /// <summary>윈도우 미디어 창 · 작업 표시줄에 보일 곡 정보</summary>
    private static void SetDisplay(MediaPlaybackItem item, Track t)
    {
        var props = item.GetDisplayProperties();
        props.Type = MediaPlaybackType.Music;
        props.MusicProperties.Title = t.Title;
        props.MusicProperties.Artist = t.Artist;
        props.MusicProperties.AlbumTitle = t.Album ?? "";
        if (t.Thumb != null) props.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(Fmt.Thumb(t.Thumb, 300)!));
        item.ApplyDisplayProperties(props);
    }

    /// <summary>이어 준 영상을 트는 중이면 그 영상의 제목 · 채널 · 썸네일로 보이게, 아니면 원래대로</summary>
    private async void ShowAs(string trackId, string? vid)
    {
        Track? shown = null;
        if (vid != null && vid != trackId && VideoLinks.Get(trackId) == vid)
        {
            VideoHit? h = null;
            try { h = await StreamResolver.Instance.VideoInfo(vid); }
            catch (Exception e) { Log.Warn($"영상 정보 받기 실패 ({vid}): {e.Message}"); }
            if (Queue.ElementAtOrDefault(Index) is not { } orig || orig.VideoId != trackId) return;
            shown = orig with
            {
                Title = h?.Title ?? orig.Title,
                Artist = h is { Channel.Length: > 0 } ? h.Channel : orig.Artist,
                Album = null,
                ArtistId = null,
                AlbumId = null,
                Thumb = $"https://i.ytimg.com/vi/{vid}/hqdefault.jpg",
            };
        }
        if (Queue.ElementAtOrDefault(Index)?.VideoId != trackId) return;
        if (shown == null && _shownAs?.VideoId != trackId) return;
        _shownAs = shown;
        if (_item != null && Current is { } now)
        {
            try { SetDisplay(_item, now); }
            catch (Exception e) { Log.Warn($"미디어 정보 바꾸기 실패: {e.Message}"); }
        }
        TrackChanged?.Invoke();
        QueueChanged?.Invoke();
    }

    /// <summary>음원에 동영상을 이어 주거나(null 이면 풂) — 지금 곡이면 바로 반영 (동영상 모드면 그 영상으로 다시 연다)</summary>
    public async void LinkVideo(Track t, string? videoId)
    {
        VideoLinks.Set(t.VideoId, videoId ?? ""); // 풀어도 '없음' 으로 기억해서 자동 찾기가 다시 잇지 않게
        Relink(t);
    }

    /// <summary>지금 곡의 영상이 바뀌었으면 바로 반영 (동영상 모드면 그 영상으로 다시 연다)</summary>
    private async void Relink(Track t)
    {
        if (Current is not { } cur || cur.VideoId != t.VideoId) return;
        VideoChanged?.Invoke();
        if (!VideoMode) return;
        _resumeAt = Position;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        Loading = true;
        StateChanged?.Invoke();
        await Open(cur, false, cts.Token);
    }

    public bool CanVideo => VideoIdFor(Current) != null;

    /// <summary>노래 ↔ 동영상 — 같은 자리에서 이어서 튼다 (YT Music 처럼)</summary>
    public async void SetVideoMode(bool on)
    {
        if (on == VideoMode) return;
        VideoMode = on;
        VideoChanged?.Invoke();
        if (Current is not { } t) return;
        var wanted = on ? VideoIdFor(t) != null : PlayingVideo;
        if (!wanted) return;
        _resumeAt = Position;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        Loading = true;
        StateChanged?.Invoke();
        await Open(t, false, cts.Token);
    }

    public event Action? TrackChanged;
    public event Action? StateChanged;
    public event Action? QueueChanged;
    public event Action? VolumeChanged;
    public event Action? RepeatChanged;

    public PlayerService(DispatcherQueue ui)
    {
        Instance = this;
        _ui = ui;
        ApplyVolume();
        _mp.IsMuted = Settings.Current.Muted;
        _mp.PlaybackSession.PlaybackStateChanged += (_, _) => Ui(() => StateChanged?.Invoke());
        _mp.MediaEnded += (_, _) => Ui(OnEnded);
        _mp.MediaFailed += (_, e) => Ui(() => OnFailed(e.ErrorMessage));
        _mp.MediaOpened += (_, _) => Ui(() =>
        {
            _openedAt = DateTime.UtcNow;
            Loading = false;
            if (Math.Abs(_speed - 1) > 0.001) _mp.PlaybackSession.PlaybackRate = _speed;
            // 노래 ↔ 동영상 전환이면 듣던 자리에서 이어서
            if (_resumeAt > 1)
            {
                _mp.PlaybackSession.Position = TimeSpan.FromSeconds(_resumeAt);
                _resumeAt = 0;
            }
            StateChanged?.Invoke();
        });

        // 미디어 키 · 시스템 미디어 컨트롤의 이전 / 다음
        var cm = _mp.CommandManager;
        cm.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.NextReceived += (s, e) => { e.Handled = true; Ui(() => _ = Next()); };
        cm.PreviousReceived += (s, e) => { e.Handled = true; Ui(() => _ = Previous()); };
    }

    private void Ui(Action a) => _ui.TryEnqueue(() =>
    {
        try { a(); }
        catch (Exception e) { Log.Error("재생 이벤트 처리 중 오류", e); }
    });

    // ───────────────────────── 상태 ─────────────────────────

    public bool IsPlaying => _mp.PlaybackSession.PlaybackState is MediaPlaybackState.Playing;
    public bool IsBuffering => Loading || _mp.PlaybackSession.PlaybackState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering;

    public double Position => _mp.PlaybackSession.Position.TotalSeconds;

    public double Duration
    {
        get
        {
            var d = _mp.PlaybackSession.NaturalDuration.TotalSeconds;
            return d > 1 ? d : Current?.DurationSec ?? 0;
        }
    }

    /// <summary>슬라이더 값(0~1). 실제 소리 크기는 볼륨 곡선을 거친다</summary>
    public double Volume
    {
        get => Settings.Current.Volume;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            Settings.Current.Volume = v;
            ApplyVolume();
            if (_mp.IsMuted && v > 0) _mp.IsMuted = false;
            Settings.Current.Volume = v;
            Settings.Current.Muted = _mp.IsMuted;
            Settings.Current.SaveSoon();
            VolumeChanged?.Invoke();
        }
    }

    /// <summary>
    /// 지수 볼륨 (pear exponential-volume) — 켜면 슬라이더 값의 세제곱. 귀는 소리 크기를 로그로 느껴서
    /// 작은 소리 쪽을 더 세밀하게 고를 수 있다
    /// </summary>
    public bool ExponentialVolume
    {
        get => Settings.Current.ExponentialVolume;
        set
        {
            Settings.Current.ExponentialVolume = value;
            Settings.Current.Save();
            ApplyVolume();
        }
    }

    private static double Curve(double v) => Settings.Current.ExponentialVolume ? v * v * v : v;

    /// <summary>
    /// 음량 표준화 — 곡의 loudnessDb 만큼 볼륨 배율을 반대로 (큰 곡은 줄이고 작은 곡은 키운다).
    /// 소리 자체는 건드리지 않고 재생기 볼륨만 바꾸니 음질은 그대로다. 대신 키우는 쪽은 볼륨 100% 를 넘을 수 없다
    /// </summary>
    public bool Normalize
    {
        get => Settings.Current.Normalize;
        set
        {
            Settings.Current.Normalize = value;
            Settings.Current.Save();
            if (value && Current is { } t) _ = LoadGain(PlayingVideo ? VideoIdFor(t) ?? t.VideoId : t.VideoId, null);
            ApplyVolume();
        }
    }

    private string? _gainFor;

    /// <summary>지금 곡의 표준화 배율 (dB) — 전문 통계에 보인다</summary>
    public double GainDb { get; private set; }

    private void ApplyVolume()
    {
        var gain = Settings.Current.Normalize ? Math.Pow(10, GainDb / 20) : 1;
        _mp.Volume = Math.Min(1, Curve(Settings.Current.Volume) * gain);
    }

    /// <summary>곡의 소리 크기를 받아 배율을 정한다 (+12 ~ -12 dB). 1.5초 안에 못 받으면 0 dB 로 시작하고, 늦게 오면 그때 맞춘다</summary>
    private async Task LoadGain(string videoId, CancellationToken? ct)
    {
        _gainFor = videoId;
        var task = StreamResolver.Instance.Loudness(videoId);
        var first = await Task.WhenAny(task, Task.Delay(1500));
        if (ct?.IsCancellationRequested == true || _gainFor != videoId) return;
        double? db = first == task ? await task : null;
        Ui(() => { GainDb = db is { } d ? Math.Clamp(-d, -12, 12) : 0; ApplyVolume(); });
        if (first == task) return;
        var late = await task;
        if (late == null || _gainFor != videoId) return;
        Ui(() => { if (_gainFor == videoId) { GainDb = Math.Clamp(-late.Value, -12, 12); ApplyVolume(); } });
    }

    public bool Muted
    {
        get => _mp.IsMuted || Settings.Current.Volume <= 0.001;
        set
        {
            _mp.IsMuted = value;
            Settings.Current.Muted = value;
            Settings.Current.SaveSoon();
            VolumeChanged?.Invoke();
        }
    }

    // ───────────────────────── 조작 ─────────────────────────

    /// <summary>곡 하나를 틀고, 그 곡으로 시작하는 라디오를 다음 트랙으로 채운다</summary>
    public async Task PlaySong(Track t, bool fromSearch = false)
    {
        SetPager(null);
        ShowAutoplayToggle = fromSearch;
        Queue = new List<Track> { t };
        Index = 0;
        AutoStart = 1;
        Radio = !fromSearch;
        AutoChips = new();
        QueueTitle = null;
        RelatedBrowseId = null;
        var version = ++_autoVersion;
        AutoLoading = true;
        QueueChanged?.Invoke();
        await Load();
        try
        {
            // YT Music 처럼: 고른 곡 하나 + 그 곡의 라디오가 '자동재생' 구간
            var data = await InnerTube.Instance.GetQueue(t.VideoId, null);
            if (version != _autoVersion) return; // 그 사이 다른 곡을 틀었음
            QueueTitle = data.Title;
            RelatedBrowseId = data.RelatedBrowseId;
            AutoChips = data.Chips;
            Queue = new List<Track> { t };
            Queue.AddRange(data.Tracks.Where(x => x.VideoId != t.VideoId));
            if (Queue.Count > 1) StreamResolver.Instance.Prefetch(Queue[1].VideoId);
        }
        catch (Exception e) { Log.Warn($"라디오 불러오기 실패: {e.Message}"); }
        finally
        {
            if (version == _autoVersion)
            {
                AutoLoading = false;
                QueueChanged?.Invoke();
            }
        }
    }

    /// <summary>앨범 · 플레이리스트를 튼다. automix 가 있으면 그 목록, 없으면 마지막 곡의 라디오를 자동재생 구간으로 붙인다</summary>
    public async Task PlayList(List<Track> list, int start = 0, string? title = null, string? related = null,
        string? automixPlaylistId = null, string? automixParams = null, TrackPager? pager = null, bool shuffled = false)
    {
        if (list.Count == 0) return;
        SetPager(pager, shuffled);
        QueueTitle = title;
        RelatedBrowseId = related;
        Queue = new List<Track>(list);
        AutoStart = Queue.Count;
        AutoChips = new();
        Radio = false;
        // 앨범 · 플레이리스트는 끝이 있는 목록 → YT Music 처럼 자동재생 토글 + 끝에 비슷한 곡
        ShowAutoplayToggle = true;
        Index = Math.Clamp(start, 0, list.Count - 1);
        var version = ++_autoVersion;
        QueueChanged?.Invoke();
        var load = Load();
        // 자동 믹스가 비면 마지막 곡의 라디오로
        if (Autoplay)
            _ = LoadAuto(async ct =>
            {
                if (automixPlaylistId != null)
                {
                    var mix = await InnerTube.Instance.GetQueue(null, automixPlaylistId, automixParams, ct);
                    if (mix.Tracks.Count > 0) return mix;
                }
                return await InnerTube.Instance.GetQueue(list[^1].VideoId, null, null, ct);
            }, version, false);
        await load;
    }

    /// <summary>자동재생 칩을 고른다 — 자동재생 구간을 그 알고리즘의 목록으로 바꾼다 (이미 지나간 곡 · 지금 곡은 그대로)</summary>
    public async Task SelectChip(int i)
    {
        if (i < 0 || i >= AutoChips.Count) return;
        var chip = AutoChips[i];
        AutoChips = AutoChips.Select((c, k) => c with { Selected = k == i }).ToList();
        var seed = Queue.ElementAtOrDefault(Math.Max(0, AutoStart - 1))?.VideoId;
        await LoadAuto(ct => InnerTube.Instance.ChipQueue(chip, seed, ct), null, keepChips: true);
    }

    /// <summary>자동재생 구간을 새로 받아 바꿔 끼운다</summary>
    private Task LoadAuto(string? videoId, string? playlistId, string? @params, int? version = null, bool keepChips = false) =>
        LoadAuto(ct => InnerTube.Instance.GetQueue(videoId, playlistId, @params, ct), version, keepChips);

    private async Task LoadAuto(Func<CancellationToken, Task<QueueData>> fetch, int? version, bool keepChips)
    {
        var v = version ?? ++_autoVersion;
        if (v != _autoVersion) return;
        AutoLoading = true;
        QueueChanged?.Invoke();
        try
        {
            var data = await fetch(CancellationToken.None);
            if (v != _autoVersion) return;
            // 지금 곡이 자동재생 구간에 있으면 그 곡까지는 남긴다 (라디오는 지금 곡 뒤를 통째로 바꾼다)
            // 알고리즘 칩(토글 없는 경우)은 지금 곡 바로 다음부터 통째로 바꾼다 (사용자 요청)
            var fromNext = keepChips && !ShowAutoplayToggle || Radio;
            var keep = Queue.Take(fromNext ? Index + 1 : Math.Max(AutoStart, Index + 1)).ToList();
            if (fromNext) AutoStart = Math.Min(AutoStart, Index + 1);
            var known = keep.Select(t => t.VideoId).ToHashSet();
            Queue = keep.Concat(data.Tracks.Where(t => known.Add(t.VideoId))).ToList();
            if (!keepChips || AutoChips.Count == 0) AutoChips = data.Chips;
            RelatedBrowseId ??= data.RelatedBrowseId;
            if ((Autoplay || !ShowAutoplayToggle) && Index + 1 < Queue.Count) StreamResolver.Instance.Prefetch(Queue[Index + 1].VideoId);
        }
        catch (Exception e) { Log.Warn($"자동재생 목록 불러오기 실패: {e.Message}"); }
        finally
        {
            if (v == _autoVersion)
            {
                AutoLoading = false;
                QueueChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 라디오(믹스)에 곡을 직접 넣으면 YT Music 처럼 '끝이 있는 목록 + 자동재생' 으로 바뀐다:
    /// 지금 곡까지가 고른 곡, 남은 라디오 곡들은 '자동재생 사용 중' 아래로 내려가고 자동재생 토글이 생긴다
    /// </summary>
    private void LeaveRadio()
    {
        if (!Radio) return;
        Radio = false;
        ShowAutoplayToggle = true;
        AutoStart = Math.Min(Queue.Count, Index + 1);
    }

    /// <summary>지금 곡 바로 뒤에 끼운다 (YT Music '다음에 재생')</summary>
    public void PlayNext(Track t) => PlayNext(new[] { t });

    public void PlayNext(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;
        if (Current == null) { _ = PlayList(tracks.ToList()); return; }
        LeaveRadio();
        foreach (var t in tracks) Remove(t.VideoId, keepCurrent: true);
        Queue.InsertRange(Index + 1, tracks.Where(t => t.VideoId != Current?.VideoId));
        var added = tracks.Count(t => t.VideoId != Current?.VideoId);
        if (Index + 1 < AutoStart) AutoStart += added;
        else AutoStart = Index + 1 + added;
        QueueChanged?.Invoke();
        StreamResolver.Instance.Prefetch(tracks[0].VideoId);
    }

    /// <summary>고른 곡들 끝(자동재생 구간 앞)에 붙인다 ('현재 재생목록에 추가')</summary>
    public void AddToQueue(Track t) => AddToQueue(new[] { t });

    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;
        if (Current == null) { _ = PlayList(tracks.ToList()); return; }
        LeaveRadio();
        foreach (var t in tracks) Remove(t.VideoId, keepCurrent: true);
        var list = tracks.Where(t => t.VideoId != Current?.VideoId).ToList();
        var at = Math.Min(AutoStart, Queue.Count);
        Queue.InsertRange(at, list);
        AutoStart = at + list.Count;
        QueueChanged?.Invoke();
    }

    /// <summary>
    /// 라디오(믹스) 시작 — ⋮ '믹스 시작' · 카드의 믹스처럼 YT 가 준 주소(watchEndpoint)로.
    /// 칩이 목록 위에 붙고 자동재생 토글은 없다
    /// </summary>
    public async Task PlayRadio(string? videoId, string? playlistId, string? @params, string? title = null)
    {
        var data = await InnerTube.Instance.GetQueue(videoId, playlistId, @params);
        if (data.Tracks.Count == 0) return;
        SetPager(null);
        var start = videoId == null ? 0 : Math.Max(0, data.Tracks.FindIndex(t => t.VideoId == videoId));
        Queue = data.Tracks.ToList();
        Index = start;
        AutoStart = Queue.Count;
        Radio = true;
        ShowAutoplayToggle = false;
        AutoChips = data.Chips;
        QueueTitle = data.Title ?? title;
        RelatedBrowseId = data.RelatedBrowseId;
        ++_autoVersion;
        AutoLoading = false;
        QueueChanged?.Invoke();
        await Load();
    }

    /// <summary>'현재 재생목록 닫기' — 재생을 멈추고 목록을 비운다 (재생 바도 사라진다)</summary>
    public void CloseQueue()
    {
        SetPager(null);
        _loadCts?.Cancel();
        _mp.Pause();
        _mp.Source = null;
        Queue = new();
        Index = -1;
        AutoStart = 0;
        AutoChips = new();
        Radio = false;
        ShowAutoplayToggle = false;
        QueueTitle = null;
        RelatedBrowseId = null;
        ++_autoVersion;
        Loading = false;
        QueueChanged?.Invoke();
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>다음 트랙 순서 바꾸기 (끌어다 놓기) — 지금 곡 · 자동재생 구간 경계도 따라 옮긴다</summary>
    public void MoveQueue(int from, int to)
    {
        if (from == to || from < 0 || from >= Queue.Count || to < 0 || to >= Queue.Count) return;
        var t = Queue[from];
        Queue.RemoveAt(from);
        Queue.Insert(to, t);
        if (from == Index) Index = to;
        else if (from < Index && to >= Index) Index--;
        else if (from > Index && to <= Index) Index++;
        if (from >= AutoStart && to < AutoStart) AutoStart++;
        else if (from < AutoStart && to >= AutoStart) AutoStart--;
        QueueChanged?.Invoke();
        if (Index + 1 < Queue.Count) StreamResolver.Instance.Prefetch(Queue[Index + 1].VideoId);
    }

    /// <summary>목록에서 뺀다 (지금 곡은 못 뺌)</summary>
    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= Queue.Count || index == Index) return;
        Queue.RemoveAt(index);
        if (index < Index) Index--;
        if (index < AutoStart) AutoStart--;
        QueueChanged?.Invoke();
    }

    private void Remove(string videoId, bool keepCurrent)
    {
        for (var i = Queue.Count - 1; i >= 0; i--)
            if (Queue[i].VideoId == videoId && !(keepCurrent && i == Index))
            {
                Queue.RemoveAt(i);
                if (i < Index) Index--;
                if (i < AutoStart) AutoStart--;
            }
    }

    public async Task PlayAt(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        Index = index;
        await Load();
    }

    public void TogglePlay()
    {
        if (Current == null) return;
        if (IsPlaying) _mp.Pause();
        else _mp.Play();
    }

    public void Seek(double seconds)
    {
        if (Current == null) return;
        _mp.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, Duration - 0.5)));
        StateChanged?.Invoke();
    }

    public async Task Next()
    {
        if (Queue.Count == 0) return;
        // 긴 목록의 끝에 다다랐으면 나머지를 받아서 이어 간다
        if (_pager is { HasMore: true } pg && Index + 1 == AutoStart) await pg.Next();
        // 자동재생을 끄면 자동재생 구간은 건너뛴다 (목록 끝 = 사용자가 고른 곡 끝)
        var end = Autoplay || !ShowAutoplayToggle ? Queue.Count : Math.Min(AutoStart, Queue.Count);
        if (Index + 1 < end) Index++;
        else if (Repeat == RepeatMode.All) Index = 0;
        else
        {
            if (!Autoplay && ShowAutoplayToggle) return;
            await Extend();
            if (Index + 1 >= Queue.Count) return;
            Index++;
        }
        await Load();
    }

    public async Task Previous()
    {
        if (Queue.Count == 0) return;
        if (Position > 3 || Index == 0)
        {
            Seek(0);
            return;
        }
        Index--;
        await Load();
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        Settings.Current.Repeat = Repeat;
        RepeatChanged?.Invoke();
    }

    /// <summary>지금 곡 뒤의 목록을 한 번 섞는다 (YT Music 의 셔플과 같은 '한 번 섞기')</summary>
    public void Shuffle()
    {
        // 사용자가 고른 곡들 안에서만 섞는다 (지금 곡이 자동재생 구간이면 그 뒤를)
        var end = Radio || Index >= AutoStart ? Queue.Count : AutoStart;
        if (end - Index <= 2) return;
        var mid = Queue.Skip(Index + 1).Take(end - Index - 1).OrderBy(_ => Random.Shared.Next());
        Queue = Queue.Take(Index + 1).Concat(mid).Concat(Queue.Skip(end)).ToList();
        QueueChanged?.Invoke();
    }

    // ───────────────────────── 내부 ─────────────────────────

    private async Task Load()
    {
        var t = Current;
        if (t == null) return;
        MaybePage();
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        _triedFallback = false;
        Loading = true;
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
        await Open(t, false, cts.Token);
    }

    private async Task Open(Track t, bool fallback, CancellationToken ct, bool play = true)
    {
        if (!fallback) AutoLink(t);
        var vid = VideoMode && !fallback ? VideoIdFor(t) : null;
        // 소리 크기는 스트림을 받는 동안 같이 받는다 (영상이면 영상 쪽 소리)
        var gain = Settings.Current.Normalize ? LoadGain(vid ?? t.VideoId, ct) : null;
        try
        {
            MediaPlaybackItem item;
            if (vid != null)
            {
                // 뮤직비디오: 화면 · 소리 DASH 목록을 만들어 Windows 재생기가 흘려 받게 (영상은 커서 통째로 받지 않는다)
                var mpd = await StreamResolver.Instance.VideoManifest(vid, ct);
                if (ct.IsCancellationRequested) return;
                var ras = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(mpd)).AsRandomAccessStream();
                var r = await AdaptiveMediaSource.CreateFromStreamAsync(ras, new Uri("https://www.youtube.com/"), "application/dash+xml");
                if (r.Status != AdaptiveMediaSourceCreationStatus.Success)
                    throw new InvalidOperationException($"영상 목록 열기 실패: {r.Status} {r.ExtendedError?.Message}");
                if (ct.IsCancellationRequested) return;
                item = new MediaPlaybackItem(MediaSource.CreateFromAdaptiveMediaSource(r.MediaSource));
            }
            else
            {
                var bytes = await StreamResolver.Instance.Audio(t.VideoId, fallback, ct);
                if (ct.IsCancellationRequested) return;
                // 받아 둔 배열을 그대로 감싸서 넘긴다 (복사하지 않아 곡 하나만큼 메모리를 아낀다)
                var ras = new MemoryStream(bytes, writable: false).AsRandomAccessStream();
                item = new MediaPlaybackItem(MediaSource.CreateFromStream(ras, "audio/mp4"));
            }
            if (PlayingVideo != (vid != null))
            {
                PlayingVideo = vid != null;
                VideoChanged?.Invoke();
            }
            _item = item;
            SetDisplay(item, t);
            // 표시를 먼저 깔고 나서 영상 정보로 덮는다 (거꾸로면 원래 곡 정보가 작업 표시줄에 남는다)
            ShowAs(t.VideoId, vid);
            if (gain != null) await gain;
            if (ct.IsCancellationRequested) return;
            _mp.Source = item;
            if (play) _mp.Play();
            else { Loading = false; StateChanged?.Invoke(); }
            Log.Info($"재생: {t.Artist} - {t.Title} ({(vid != null ? "뮤직비디오 " + vid : StreamResolver.Instance.LastSource)})");
            _unplayableRun = 0;
            // 다음 곡 주소를 미리 받아 둔다
            if (Index + 1 < Queue.Count) StreamResolver.Instance.Prefetch(Queue[Index + 1].VideoId);
        }
        catch (OperationCanceledException) { }
        catch (UnplayableException e) when (vid == null)
        {
            // 지역 제한 · 지워진 곡: 다시 해 봐야 소용없으니 알리고 다음 곡으로 (막힌 곡이 줄줄이 이어지면 5곡째에서 멈춘다)
            Log.Warn($"재생할 수 없는 곡: {t.Artist} - {t.Title} ({e.Message})");
            Loading = false;
            StateChanged?.Invoke();
            if (ct.IsCancellationRequested) return;
            UI.Toast.Show("이 곡은 이 지역에서 재생할 수 없어요");
            if (++_unplayableRun < 5 && Index + 1 < Queue.Count) await Next();
            else _unplayableRun = 0;
        }
        catch (Exception e)
        {
            Log.Error($"스트림 열기 실패: {t.Title}", e);
            if (vid != null && !ct.IsCancellationRequested) UI.Toast.Show("뮤직비디오를 불러오지 못해 노래로 재생할게요");
            if (!fallback && !ct.IsCancellationRequested)
            {
                _triedFallback = true;
                await Open(t, true, ct);
            }
            else
            {
                Loading = false;
                StateChanged?.Invoke();
            }
        }
    }

    private void OnFailed(string message)
    {
        Log.Warn($"재생 실패: {message}");
        var t = Current;
        if (t != null && !_triedFallback && _loadCts != null)
        {
            _triedFallback = true;
            _ = Open(t, true, _loadCts.Token);
            return;
        }
        Loading = false;
        StateChanged?.Invoke();
    }

    /// <summary>취침 타이머 '지금 곡이 끝나면' — 끝나면 멈춘다</summary>
    public bool StopAfterCurrent { get; set; }

    public void Pause()
    {
        _mp.Pause();
        StateChanged?.Invoke();
    }

    private void OnEnded()
    {
        // 열자마자 끝났다 = 끊긴 주소 → 새 주소로 한 번 더
        if (DateTime.UtcNow - _openedAt < TimeSpan.FromSeconds(4) && Duration > 10 && Current is { } cur && !_triedFallback && _loadCts != null)
        {
            Log.Warn("열자마자 끝남 → 새 스트림 주소로 다시 시도");
            _triedFallback = true;
            _ = Open(cur, true, _loadCts.Token);
            return;
        }
        if (StopAfterCurrent)
        {
            StopAfterCurrent = false;
            Seek(0);
            _mp.Pause();
            StateChanged?.Invoke();
            return;
        }
        if (Repeat == RepeatMode.One)
        {
            Seek(0);
            _mp.Play();
            return;
        }
        _ = Next();
    }

    // ───────────────────────── 이어 듣기 ─────────────────────────

    /// <summary>앱을 닫을 때의 목록 · 위치 (다시 켜면 그대로 — 멈춘 채로)</summary>
    public SessionState Snapshot() => new()
    {
        Queue = Queue.Take(500).ToList(),
        Index = Index,
        Position = Position,
        AutoStart = AutoStart,
        Radio = Radio,
        ShowAutoplayToggle = ShowAutoplayToggle,
        AutoChips = AutoChips,
        QueueTitle = QueueTitle,
        RelatedBrowseId = RelatedBrowseId,
    };

    /// <param name="play">켜자마자 이어서 재생 (설정 PlayOnStart) — 아니면 멈춘 채로</param>
    public async void Restore(SessionState s, bool play = false)
    {
        if (s.Queue.Count == 0 || s.Index < 0 || s.Index >= s.Queue.Count) return;
        Queue = s.Queue;
        Index = s.Index;
        AutoStart = Math.Min(s.AutoStart, Queue.Count);
        Radio = s.Radio;
        ShowAutoplayToggle = s.ShowAutoplayToggle;
        AutoChips = s.AutoChips ?? new();
        QueueTitle = s.QueueTitle;
        RelatedBrowseId = s.RelatedBrowseId;
        QueueChanged?.Invoke();
        TrackChanged?.Invoke();
        if (AutoChips.Count == 0) _ = RefetchChips();
        var cts = _loadCts = new CancellationTokenSource();
        _resumeAt = s.Position;
        await Open(Queue[Index], false, cts.Token, play: play);
    }

    /// <summary>칩을 안 남긴 예전 이어 듣기 — 목록은 그대로 두고 칩만 다시 받는다 (자동재생 구간을 시작한 곡 기준)</summary>
    private async Task RefetchChips()
    {
        var v = _autoVersion;
        var seed = Queue.ElementAtOrDefault(Math.Clamp(AutoStart - 1, 0, Queue.Count - 1))?.VideoId;
        if (seed == null) return;
        try
        {
            var data = await InnerTube.Instance.GetQueue(seed, null);
            if (v != _autoVersion || AutoChips.Count > 0 || data.Chips.Count == 0) return;
            AutoChips = data.Chips;
            QueueChanged?.Invoke();
        }
        catch (Exception e) { Log.Warn($"칩 다시 받기 실패: {e.Message}"); }
    }

    /// <summary>목록 끝에 다다르면 마지막 곡으로 라디오를 이어 붙인다</summary>
    private async Task Extend()
    {
        if (_extending || Queue.Count == 0) return;
        _extending = true;
        try
        {
            var last = Queue[^1];
            var more = await InnerTube.Instance.Queue(last.VideoId, null);
            var known = Queue.Select(q => q.VideoId).ToHashSet();
            var add = more.Where(m => !known.Contains(m.VideoId)).ToList();
            if (add.Count > 0)
            {
                Queue.AddRange(add);
                QueueChanged?.Invoke();
            }
        }
        catch (Exception e) { Log.Warn($"다음 트랙 이어 붙이기 실패: {e.Message}"); }
        finally { _extending = false; }
    }
}
