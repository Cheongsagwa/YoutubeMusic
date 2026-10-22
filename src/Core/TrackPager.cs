namespace OneMusic.Core;

/// <summary>
/// 긴 곡 목록(100곡 넘는 플레이리스트 · 보관함 노래)을 묶음씩 이어 받는다.
/// 상세 화면(스크롤)과 재생기(목록 끝에 다다를 때)가 같은 것을 함께 써서 같은 묶음을 두 번 받지 않는다
/// </summary>
public sealed class TrackPager
{
    private readonly string? _thumb;
    private Task<List<Track>>? _running;
    public string? Token { get; private set; }
    public bool HasMore => Token != null;
    /// <summary>새 묶음을 받으면 (화면 줄 · 재생 목록에 붙인다)</summary>
    public event Action<List<Track>>? Loaded;

    public TrackPager(string? token, string? thumb = null)
    {
        Token = token;
        _thumb = thumb;
    }

    /// <summary>다음 묶음 — 받는 중이면 그 결과를 같이 기다린다</summary>
    public Task<List<Track>> Next()
    {
        if (_running is { IsCompleted: false } running) return running;
        return _running = Load();
    }

    private async Task<List<Track>> Load()
    {
        if (Token is not { } token) return new();
        try
        {
            var (list, next) = await InnerTube.Instance.MoreTracks(token, _thumb);
            Token = list.Count > 0 ? next : null;
            if (list.Count > 0) Loaded?.Invoke(list);
            return list;
        }
        catch (Exception e)
        {
            Log.Warn($"곡 목록 이어 받기 실패: {e.Message}");
            Token = null;
            return new();
        }
    }
}
