using System.Text.Json;

namespace OneMusic.Core;

/// <summary>
/// 다운로드 · 오프라인 저장 (pear 의 downloader 플러그인 · YT Music 오프라인 저장).
/// - 다운로드: 음악 폴더\YouTube Music\가수 - 제목.m4a 로 파일을 남긴다
/// - 오프라인 저장: %LocalAppData%\OneMusic\offline\곡ID.m4a + 목록(index.json). 재생할 때 인터넷 대신 이 파일을 쓴다
/// </summary>
public sealed class Offline
{
    private static readonly string Dir = IOPath.Combine(Log.AppDataDir, "offline");
    private static readonly string IndexPath = IOPath.Combine(Dir, "index.json");
    public static string DownloadDir { get; } = MoveOldDownloads(IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), AppIdentity.Name));

    /// <summary>예전 다운로드 폴더(음악\OneMusic)가 있으면 새 이름(음악\YouTube Music)으로 옮겨 둔다</summary>
    private static string MoveOldDownloads(string dir)
    {
        var old = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "OneMusic");
        try
        {
            if (!Directory.Exists(old)) return dir;
            if (!Directory.Exists(dir)) { Directory.Move(old, dir); return dir; }
            foreach (var f in Directory.GetFiles(old))
            {
                var dest = IOPath.Combine(dir, IOPath.GetFileName(f));
                if (!File.Exists(dest)) File.Move(f, dest);
            }
            if (!Directory.EnumerateFileSystemEntries(old).Any()) Directory.Delete(old);
        }
        catch (Exception e) { Log.Warn($"예전 다운로드 폴더 옮기기 실패: {e.Message}"); }
        return dir;
    }

    // ⚠️ 정적 필드는 위에서부터 차례로 만들어진다 — Instance 를 위 경로들보다 먼저 만들면
    // 생성자 안에서 Dir 이 아직 null 이라 '형식 초기화 오류'로 ⋮ 메뉴 · 재생이 통째로 막혔다
    public static Offline Instance { get; } = new();

    private readonly List<Track> _tracks;
    /// <summary>저장한 곡 ID (재생할 때마다 물어보므로 목록을 훑지 않게)</summary>
    private readonly HashSet<string> _ids = new();
    private readonly HashSet<string> _busy = new();
    public event Action? Changed;

    private Offline()
    {
        Directory.CreateDirectory(Dir);
        try
        {
            _tracks = File.Exists(IndexPath) ? JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(IndexPath)) ?? new() : new();
            _tracks.RemoveAll(t => !File.Exists(PathOf(t.VideoId)));
            foreach (var t in _tracks) _ids.Add(t.VideoId);
        }
        catch (Exception e)
        {
            Log.Warn($"오프라인 목록 읽기 실패: {e.Message}");
            _tracks = new();
        }
    }

    /// <summary>최근에 저장한 것이 앞</summary>
    public IReadOnlyList<Track> Tracks { get { lock (_tracks) return _tracks.ToList(); } }

    public bool Has(string videoId) { lock (_tracks) return _ids.Contains(videoId); }
    public bool IsBusy(string videoId) { lock (_busy) return _busy.Contains(videoId); }

    private static string PathOf(string videoId) => IOPath.Combine(Dir, videoId + ".m4a");

    /// <summary>저장된 오디오 (없으면 null)</summary>
    public async Task<byte[]?> Read(string videoId)
    {
        if (!Has(videoId)) return null;
        try { return await File.ReadAllBytesAsync(PathOf(videoId)); }
        catch { return null; }
    }

    public async Task<bool> Save(Track t)
    {
        if (Has(t.VideoId)) return true;
        lock (_busy) if (!_busy.Add(t.VideoId)) return false;
        try
        {
            var bytes = await StreamResolver.Instance.Audio(t.VideoId, false, CancellationToken.None);
            await File.WriteAllBytesAsync(PathOf(t.VideoId), bytes);
            lock (_tracks)
            {
                _tracks.RemoveAll(x => x.VideoId == t.VideoId);
                _tracks.Insert(0, t with { Menu = null });
                _ids.Add(t.VideoId);
            }
            WriteIndex();
            Log.Info($"오프라인 저장: {t.Artist} - {t.Title}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"오프라인 저장 실패: {t.Title}", e);
            return false;
        }
        finally
        {
            lock (_busy) _busy.Remove(t.VideoId);
            Changed?.Invoke();
        }
    }

    public void Remove(string videoId)
    {
        lock (_tracks)
        {
            _tracks.RemoveAll(x => x.VideoId == videoId);
            _ids.Remove(videoId);
        }
        try { File.Delete(PathOf(videoId)); } catch { }
        WriteIndex();
        Changed?.Invoke();
    }

    private void WriteIndex()
    {
        try { lock (_tracks) File.WriteAllText(IndexPath, JsonSerializer.Serialize(_tracks)); }
        catch (Exception e) { Log.Warn($"오프라인 목록 저장 실패: {e.Message}"); }
    }

    /// <summary>음악 폴더에 파일로 내려받는다. 저장한 파일 경로를 돌려준다</summary>
    public static async Task<string> Download(Track t)
    {
        var bytes = await Instance.Read(t.VideoId) ?? await StreamResolver.Instance.Audio(t.VideoId, false, CancellationToken.None);
        Directory.CreateDirectory(DownloadDir);
        var name = $"{t.Artist} - {t.Title}";
        foreach (var c in IOPath.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (name.Length > 150) name = name[..150];
        var path = IOPath.Combine(DownloadDir, name + ".m4a");
        await File.WriteAllBytesAsync(path, bytes);
        // 탐색기 · 다른 음악 앱에서 보이게 곡 정보(제목 · 가수 · 앨범 · 앨범아트)를 파일에 적는다 (pear downloader 처럼)
        byte[]? cover = null;
        try
        {
            var url = Fmt.Thumb(t.Thumb, 600);
            if (url != null) cover = await SquareJpeg(await ImageCache.Get(url));
        }
        catch (Exception e) { Log.Warn($"앨범아트 받기 실패: {e.Message}"); }
        try
        {
            using var f = TagLib.File.Create(path, "audio/mp4", TagLib.ReadStyle.Average);
            f.Tag.Title = t.Title;
            f.Tag.Performers = new[] { t.Artist };
            f.Tag.AlbumArtists = new[] { t.Artist };
            if (t.Album != null) f.Tag.Album = t.Album;
            if (cover != null)
                f.Tag.Pictures = new TagLib.IPicture[]
                {
                    new TagLib.Picture(new TagLib.ByteVector(cover)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/jpeg", Description = "Cover" },
                };
            f.Save();
        }
        catch (Exception e) { Log.Warn($"곡 정보 쓰기 실패: {e.Message}"); }
        Log.Info($"다운로드: {path}");
        return path;
    }

    /// <summary>썸네일을 가운데 정사각형으로 잘라 JPEG 로 (영상 썸네일은 16:9 · webp 일 수도 있어서)</summary>
    private static async Task<byte[]> SquareJpeg(byte[] src)
    {
        using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await ms.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(src));
        ms.Seek(0);
        var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms);
        var w = dec.PixelWidth; var h = dec.PixelHeight;
        var side = Math.Min(w, h);
        var tr = new Windows.Graphics.Imaging.BitmapTransform
        {
            Bounds = new Windows.Graphics.Imaging.BitmapBounds { X = (w - side) / 2, Y = (h - side) / 2, Width = side, Height = side },
        };
        var px = await dec.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, tr,
            Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        using var outMs = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var enc = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, outMs);
        enc.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, side, side, 96, 96, px.DetachPixelData());
        await enc.FlushAsync();
        var bytes = new byte[outMs.Size];
        outMs.Seek(0);
        using var rd = new Windows.Storage.Streams.DataReader(outMs.GetInputStreamAt(0));
        await rd.LoadAsync((uint)outMs.Size);
        rd.ReadBytes(bytes);
        return bytes;
    }
}
