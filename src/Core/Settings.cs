using System.Text.Json;

namespace OneMusic.Core;

/// <summary>설정 (볼륨 · 반복 모드 · 마지막 창 크기) — %LocalAppData%\OneMusic\settings.json</summary>
public sealed class Settings
{
    public double Volume { get; set; } = 0.6;
    public bool Muted { get; set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;
    public bool Autoplay { get; set; } = true;
    /// <summary>가사를 먼저 찾을 곳 (☆ 로 바꾼다)</summary>
    public LyricsSource LyricsSource { get; set; } = LyricsSource.LRCLib;
    /// <summary>창이 뒤에 있을 때 곡이 바뀌면 윈도우 알림 (pear notifications)</summary>
    public bool Notifications { get; set; } = true;
    /// <summary>앱을 다시 켜면 듣던 곡 · 위치 그대로</summary>
    public bool ResumeSession { get; set; } = true;
    /// <summary>사이드바(≡)를 펼쳐 둘지</summary>
    public bool GuideOpen { get; set; }
    /// <summary>최근 검색어 (YT 기록을 못 받을 때 쓰는 이 PC 의 기록, 최근 것이 앞)</summary>
    public List<string> RecentSearches { get; set; } = new();
    /// <summary>소리를 낼 장치 (null = 윈도우 기본 장치)</summary>
    public string? AudioDevice { get; set; }
    /// <summary>지수 볼륨 곡선 (pear exponential-volume)</summary>
    public bool ExponentialVolume { get; set; }
    /// <summary>음량 표준화 — 곡마다 다른 소리 크기를 맞춘다 (YT loudnessDb 로 볼륨 배율만 바꿈, 압축 · 리미터 없음)</summary>
    public bool Normalize { get; set; } = true;
    /// <summary>높은 음질 — 프리미엄 계정으로 AAC 256k(itag 141) 를 받는다 (로그인 + Node.js/Deno 필요, 없으면 128k)</summary>
    public bool HighQuality { get; set; } = true;
    /// <summary>앱을 켜면 마지막으로 듣던 곡을 바로 이어서 재생 (이어 듣기가 켜져 있을 때)</summary>
    public bool PlayOnStart { get; set; } = true;
    /// <summary>들었던 곡 디스크 캐시 상한 (MB, 0 = 끔)</summary>
    public int CacheLimitMB { get; set; } = 2048;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }

    private static readonly string FilePath = IOPath.Combine(Log.AppDataDir, "settings.json");
    public static Settings Current { get; } = Load();

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) { Log.Warn($"설정 읽기 실패: {e.Message}"); }
        return new Settings();
    }

    private Timer? _saveTimer;

    /// <summary>잠시(0.5초) 뒤 한 번 저장 — 볼륨 슬라이더처럼 연달아 바뀌는 값을 매번 쓰지 않게 모아서.
    /// 창을 닫을 때만 저장하면 강제 종료 · 꺼짐 때 마지막 볼륨이 사라져 예전 볼륨으로 켜졌다</summary>
    public void SaveSoon()
    {
        _saveTimer ??= new Timer(_ => Save());
        _saveTimer.Change(500, Timeout.Infinite);
    }

    private readonly object _saveLock = new();

    public void Save()
    {
        lock (_saveLock) SaveCore();
    }

    private void SaveCore()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch (Exception e) { Log.Warn($"설정 저장 실패: {e.Message}"); }
    }
}
