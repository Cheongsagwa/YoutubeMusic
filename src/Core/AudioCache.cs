namespace OneMusic.Core;

/// <summary>
/// 들었던 곡 오디오를 디스크에 남겨 두는 캐시 — %LocalAppData%\OneMusic\cache\audio\곡ID.m4a.
/// 같은 곡을 다시 틀면 yt-dlp · 인터넷 없이 바로 연다. 상한(설정 CacheLimitMB)을 넘으면 오래 안 들은 곡부터 지운다.
/// 오프라인 저장(Offline)과는 따로 — 오프라인 곡은 지우지 않고, 이 캐시는 언제든 지워져도 된다
/// </summary>
public static class AudioCache
{
    private static readonly string Dir = IOPath.Combine(Log.AppDataDir, "cache", "audio");
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>고를 수 있는 상한 (MB, 0 = 끔)</summary>
    public static readonly int[] Limits = [0, 512, 1024, 2048, 5120, 10240];

    public static string LimitLabel(int mb) => mb <= 0 ? "끔" : mb >= 1024 ? $"{mb / 1024.0:0.#}GB" : $"{mb}MB";

    /// <summary>높은 음질(256k)로 받은 곡은 따로 (.hq.m4a) — 예전 128k 캐시가 높은 음질을 가로막지 않게</summary>
    private static string PathOf(string videoId, bool hq) => IOPath.Combine(Dir, videoId + (hq ? ".hq.m4a" : ".m4a"));

    /// <summary>캐시에 있으면 오디오 (최근에 쓴 것으로 표시), 없으면 null. hqOnly 면 256k 캐시만 본다</summary>
    public static async Task<(byte[] Bytes, bool Hq)?> Read(string videoId, bool hqOnly)
    {
        if (Settings.Current.CacheLimitMB <= 0) return null;
        var hq = File.Exists(PathOf(videoId, true));
        if (!hq && hqOnly) return null;
        var path = PathOf(videoId, hq);
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            // 오래 안 들은 곡부터 지우려고 마지막으로 쓴 때를 남긴다
            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }
            return (bytes, hq);
        }
        catch (Exception e)
        {
            Log.Warn($"캐시 읽기 실패 {videoId}: {e.Message}");
            return null;
        }
    }

    /// <summary>다 받은 곡을 뒤에서 저장하고 상한에 맞춰 정리한다</summary>
    public static void Write(string videoId, byte[] bytes, bool hq) => _ = Task.Run(async () =>
    {
        var limit = (long)Settings.Current.CacheLimitMB << 20;
        if (limit <= 0 || bytes.Length > limit) return;
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Dir);
            var path = PathOf(videoId, hq);
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, path, true);
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            Trim(limit);
        }
        catch (Exception e) { Log.Warn($"캐시 저장 실패 {videoId}: {e.Message}"); }
        finally { Gate.Release(); }
    });

    /// <summary>상한을 넘으면 오래 안 들은 곡부터 지운다</summary>
    private static void Trim(long limit)
    {
        var files = new DirectoryInfo(Dir).GetFiles("*.m4a").OrderBy(f => f.LastAccessTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var f in files)
        {
            if (total <= limit) break;
            try { total -= f.Length; f.Delete(); } catch { }
        }
    }

    /// <summary>지금 캐시 크기 (바이트)</summary>
    public static long Size()
    {
        try { return Directory.Exists(Dir) ? new DirectoryInfo(Dir).GetFiles("*.m4a").Sum(f => f.Length) : 0; }
        catch { return 0; }
    }

    public static async Task SetLimit(int mb)
    {
        Settings.Current.CacheLimitMB = mb;
        Settings.Current.Save();
        await Gate.WaitAsync();
        try
        {
            if (!Directory.Exists(Dir)) return;
            if (mb <= 0) Clear();
            else Trim((long)mb << 20);
        }
        finally { Gate.Release(); }
    }

    public static void Clear()
    {
        try { if (Directory.Exists(Dir)) foreach (var f in Directory.GetFiles(Dir)) File.Delete(f); }
        catch (Exception e) { Log.Warn($"캐시 비우기 실패: {e.Message}"); }
    }

    public static async Task ClearAsync()
    {
        await Gate.WaitAsync();
        try { await Task.Run(Clear); }
        finally { Gate.Release(); }
    }

    public static string SizeLabel(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0}GB" : $"{bytes / (double)(1 << 20):0}MB";
}
