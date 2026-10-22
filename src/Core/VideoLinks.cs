using System.Text.Json;

namespace OneMusic.Core;

/// <summary>
/// 사용자가 직접 이어 준 '음원 → 동영상' 짝 (%LocalAppData%\OneMusic\video-links.json).
/// YT 가 짝(CounterpartId)을 모르는 곡 — 예: 누가 유튜브 영상 소리를 음원으로 따로 올린 곡 — 도 원래 영상으로 볼 수 있게
/// </summary>
public static class VideoLinks
{
    private static readonly string FilePath = IOPath.Combine(Log.AppDataDir, "video-links.json");
    private static Dictionary<string, string>? _map;

    private static Dictionary<string, string> Map
    {
        get
        {
            if (_map != null) return _map;
            try { _map = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) : null; }
            catch (Exception e) { Log.Warn($"동영상 연결 읽기 실패: {e.Message}"); }
            return _map ??= new();
        }
    }

    /// <summary>이어 준 영상 ("" = 찾아봤는데 없었음 → null)</summary>
    public static string? Get(string trackId) => Map.GetValueOrDefault(trackId) is { Length: > 0 } v ? v : null;

    /// <summary>자동으로든 직접이든 한 번 정해진 곡인지 (없음으로 정해진 것도 포함 — 다시 찾지 않게)</summary>
    public static bool Known(string trackId) => Map.ContainsKey(trackId);

    /// <param name="videoId">null 이면 연결을 푼다, "" 이면 '원본 영상 없음' 으로 기억</param>
    public static void Set(string trackId, string? videoId)
    {
        if (videoId == null) Map.Remove(trackId);
        else Map[trackId] = videoId;
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Map)); }
        catch (Exception e) { Log.Warn($"동영상 연결 저장 실패: {e.Message}"); }
    }
}

/// <summary>이어 준 영상의 제목 · 채널 (video-meta.json) — 동영상으로 볼 땐 음원 대신 이걸 보여 준다</summary>
public static class VideoMeta
{
    private static readonly string FilePath = IOPath.Combine(Log.AppDataDir, "video-meta.json");
    private static Dictionary<string, VideoHit>? _map;

    private static Dictionary<string, VideoHit> Map
    {
        get
        {
            if (_map != null) return _map;
            try { _map = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, VideoHit>>(File.ReadAllText(FilePath)) : null; }
            catch (Exception e) { Log.Warn($"영상 정보 읽기 실패: {e.Message}"); }
            return _map ??= new();
        }
    }

    public static VideoHit? Get(string videoId) => Map.GetValueOrDefault(videoId);

    private static readonly string AlbumPath = IOPath.Combine(Log.AppDataDir, "video-album-channels.json");
    private static Dictionary<string, string>? _albums;

    private static Dictionary<string, string> Albums
    {
        get
        {
            if (_albums != null) return _albums;
            try { _albums = File.Exists(AlbumPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AlbumPath)) : null; }
            catch (Exception e) { Log.Warn($"앨범 채널 읽기 실패: {e.Message}"); }
            return _albums ??= new();
        }
    }

    /// <summary>
    /// 이 앨범 곡의 원본 영상을 올린 채널 — 같은 앨범의 다른 곡도 그 채널에 있을 가능성이 높다.
    /// ('OWCS KOREA 2026 Track.04' 로는 안 나오고 '호람썬 Track.04' 로는 나온다)
    /// </summary>
    public static string? AlbumChannel(string album) => Albums.GetValueOrDefault(album);

    public static void SetAlbumChannel(string album, string channel)
    {
        if (Albums.GetValueOrDefault(album) == channel) return;
        Albums[album] = channel;
        try { File.WriteAllText(AlbumPath, JsonSerializer.Serialize(Albums)); }
        catch (Exception e) { Log.Warn($"앨범 채널 저장 실패: {e.Message}"); }
    }

    public static void Set(VideoHit h)
    {
        Map[h.Id] = h;
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Map)); }
        catch (Exception e) { Log.Warn($"영상 정보 저장 실패: {e.Message}"); }
    }
}

/// <summary>유튜브 동영상 검색 결과 한 줄 (동영상 연결 고르기)</summary>
public sealed record VideoHit(string Id, string Title, string Channel, double Duration);
