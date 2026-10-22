using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using OneMusic.Core;
using Windows.Graphics.Imaging;

namespace OneMusic.UI;

/// <summary>
/// ⚠️ 개발용 — 소스 폴더에서 실행했을 때만 켜진다 (dev-watch.ps1 이 있는 폴더).
/// .dev\app-cmd.txt 에 정해진 명령이 적히면 실행하고 .dev\app-out.txt 에 결과를 남긴다.
/// shot: 화면을 .dev\shot.png 로 / state: 지금 상태 / nav:home|player|lyrics|close / search:검색어 / play:first|곡ID / mem
/// </summary>
public sealed class DevBridge
{
    private readonly MainWindow _w;
    private readonly string _cmd, _out, _shot;
    private DateTime _last;
    private readonly DispatcherQueueTimer _timer; // 필드로 붙잡아 둬야 GC 에 수거되지 않는다

    public DevBridge(MainWindow w)
    {
        _w = w;
        var dev = IOPath.Combine(Log.DevRoot!, ".dev");
        _cmd = IOPath.Combine(dev, "app-cmd.txt");
        _out = IOPath.Combine(dev, "app-out.txt");
        _shot = IOPath.Combine(dev, "shot.png");
        if (!File.Exists(_cmd)) File.WriteAllText(_cmd, "");
        _last = File.GetLastWriteTimeUtc(_cmd);
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(400);
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        Log.Info("개발용 통로 켜짐");
    }

    private async void Check()
    {
        try
        {
            var m = File.GetLastWriteTimeUtc(_cmd);
            if (m == _last) return;
            _last = m;
            var cmd = (await File.ReadAllTextAsync(_cmd)).Trim();
            if (cmd.Length == 0) return;
            // 읽은 명령은 지운다 — 쓰는 쪽이 파일을 두 번 건드리면(PowerShell Set-Content) 같은 명령이 두 번 돌았다 (fs 가 켰다 껐다)
            File.WriteAllText(_cmd, "");
            _last = File.GetLastWriteTimeUtc(_cmd);
            string result;
            try { result = await Run(cmd); }
            catch (Exception e) { result = "ERROR " + e; }
            await File.WriteAllTextAsync(_out, $"#done {DateTime.Now:O}\n{result}");
        }
        catch (Exception e) { Log.Warn("DevBridge: " + e.Message); }
    }

    private async Task<string> Run(string cmd)
    {
        var p = _w.Player;
        if (cmd == "shot") return await Shot();
        if (cmd == "state")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"track: {p.Current?.Artist} - {p.Current?.Title} ({p.Current?.VideoId}) artist {p.Current?.ArtistId} album {p.Current?.AlbumId}");
            sb.AppendLine($"playing: {p.IsPlaying} buffering: {p.IsBuffering} pos: {p.Position:0.0}/{p.Duration:0.0}");
            sb.AppendLine($"queue: {p.Queue.Count} index: {p.Index} repeat: {p.Repeat} volume: {p.Volume:0.00} norm: {p.Normalize} gain: {p.GainDb:0.0}dB muted: {p.Muted}");
            sb.AppendLine($"lyrics: {LyricsState.Instance.Lines?.Count.ToString() ?? "none"} loading: {LyricsState.Instance.Loading}");
            sb.AppendLine($"player page: {_w.PlayerPage.IsOpen}");
            sb.AppendLine($"fullscreen: {_w.PlayerPage.IsFullScreen}");
            sb.AppendLine($"chips: {string.Join(" | ", p.AutoChips.Select(c => c.Selected ? $"[{c.Text}]" : c.Text))}");
            sb.AppendLine($"video: mode {p.VideoMode} can {p.CanVideo} playing {p.PlayingVideo} size {p.Media.PlaybackSession.NaturalVideoWidth}x{p.Media.PlaybackSession.NaturalVideoHeight} id {PlayerService.VideoIdFor(p.Queue.ElementAtOrDefault(p.Index))}");
            return sb.ToString();
        }
        if (cmd == "fs") { _w.PlayerPage.ToggleFullScreen(); return $"fullscreen {_w.PlayerPage.IsFullScreen}"; }
        if (cmd.StartsWith("video:")) { p.SetVideoMode(cmd[6..] == "1"); return "ok"; }
        if (cmd == "nav:home") { _w.ShowHome(); return "ok"; }
        if (cmd == "nav:explore") { _w.SelectTab(1); return "ok"; }
        if (cmd == "nav:library") { _w.SelectTab(2); return "ok"; }
        if (cmd.StartsWith("scroll:")) { _w.ScrollCurrent(double.Parse(cmd[7..])); return "ok"; }
        if (cmd == "login") { var lw = new LoginWindow(); lw.Activate(); _ = lw.Result.ContinueWith(t => { if (t.Result) _w.DispatcherQueue.TryEnqueue(() => _w.ReloadAll()); }); return "login window opened"; }
        if (cmd.StartsWith("ytdlp:"))
        {
            // 개발용 진단: yt-dlp 를 정해진 인자 + 고른 클라이언트로 실행해 결과와 주소 응답을 본다
            var parts = cmd[6..].Split('|');
            var id = parts[0];
            var client = parts.Length > 1 ? parts[1] : "";
            var exe = IOPath.Combine(Log.AppDataDir, "yt-dlp.exe");
            var args = $"-v -f \"bestaudio[ext=m4a]/bestaudio\" -g --no-playlist " +
                       (client.Length > 0 ? $"--extractor-args \"youtube:player_client={client}\" " : "") +
                       $"\"https://www.youtube.com/watch?v={id}\"";
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var pr = System.Diagnostics.Process.Start(psi)!;
            var o = await pr.StandardOutput.ReadToEndAsync();
            var er = await pr.StandardError.ReadToEndAsync();
            await pr.WaitForExitAsync();
            var url = o.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            var probe = "";
            if (url != null && url.StartsWith("http"))
            {
                using var http = new HttpClient();
                foreach (var range in new[] { "bytes=0-65535", "bytes=1500000-1565535", "bytes=0-", "bytes=0-20000000" })
                {
                    using var rq = new HttpRequestMessage(HttpMethod.Get, url);
                    rq.Headers.TryAddWithoutValidation("Range", range);
                    using var rs = await http.SendAsync(rq, HttpCompletionOption.ResponseHeadersRead);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var body = await rs.Content.ReadAsByteArrayAsync();
                    probe += $"{range}: {(int)rs.StatusCode} {rs.Content.Headers.ContentLength} got {body.Length} in {sw.ElapsedMilliseconds}ms type {rs.Content.Headers.ContentType}\n";
                    if (range == "bytes=0-") probe += "head: " + Convert.ToHexString(body.AsSpan(0, Math.Min(48, body.Length))) + "\n";
                }
            }
            var errTail = string.Join("\n", er.Split('\n').Where(l => l.Contains("[youtube]") || l.Contains("WARNING") || l.Contains("ERROR") || l.Contains("[info]")).TakeLast(25));
            return $"{probe}\nurl: {url?[..Math.Min(160, url.Length)]}\n{errTail}";
        }
        if (cmd.StartsWith("dump:"))
        {
            var id = cmd[5..];
            var bytes = await StreamResolver.Instance.Audio(id, false, CancellationToken.None);
            await File.WriteAllBytesAsync(IOPath.Combine(Log.DevRoot!, ".dev", id + ".m4a"), bytes);
            return $"dumped {bytes.Length}";
        }
        if (cmd.StartsWith("ptab:")) { _w.OpenPlayer(); _w.PlayerPage.SelectTab(int.Parse(cmd[5..])); return "ok"; }
        if (cmd.StartsWith("hit:"))
        {
            var xy = cmd[4..].Split(',');
            var pt = new Point(double.Parse(xy[0]), double.Parse(xy[1]));
            var sb = new StringBuilder();
            foreach (var el in VisualTreeHelper.FindElementsInHostCoordinates(pt, _w.Root, true).Take(25))
            {
                var fe = el as FrameworkElement;
                var txt = el is TextBlock tb ? " text=" + tb.Text : el is TextBox tx ? " textbox=" + tx.Text : "";
                var par = (VisualTreeHelper.GetParent(el) as FrameworkElement)?.GetType().Name;
                sb.AppendLine($"{el.GetType().Name} parent={par} vis={el.Visibility} op={el.Opacity:0.##} gpuop={Anim.V(el).Opacity:0.##} size={fe?.ActualWidth:0}x{fe?.ActualHeight:0}{txt}");
            }
            return sb.ToString();
        }
        if (cmd == "guide") { _w.ToggleGuide(); return "ok"; }
        if (cmd == "mute") { p.Muted = !p.Muted; return $"muted {p.Muted}"; }
        if (cmd == "expandfirst")
        {
            // 진단: 처음 보이는 '자세히 보기' 알약을 누른다
            Border? found = null;
            void Walk(DependencyObject o)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o) && found == null; i++)
                {
                    var c = VisualTreeHelper.GetChild(o, i);
                    if (c is Border { Child: TextBlock { Text: "자세히 보기" or "간략히" } } b && b.Visibility == Visibility.Visible) found = b;
                    else Walk(c);
                }
            }
            Walk(_w.PlayerPage);
            if (found == null) return "none";
            if (found.Tag is Action toggle) { toggle(); return "tapped"; }
            return "no action";
        }
        if (cmd == "accountmenu") { AccountMenu.Show(_w, _w.Root); return "ok"; }
        if (cmd == "about") { AboutDialog.Show(); return "ok"; }
        if (cmd == "trackmenu") { if (PlayerService.Instance.Current is { } cur) TrackMenu.Show(_w.Bar, cur, PlayerService.Instance.Index, null, fromBar: true); return "ok"; }
        if (cmd == "newpl") { _ = NewPlaylistDialog.Create(Array.Empty<string>()); return "ok"; }
        if (cmd == "dialog:cancel") { OneDialog.Cancel(); return "ok"; }
        if (cmd.StartsWith("shelfnext:")) { _w.Home.PageShelf(int.Parse(cmd[10..])); return "ok"; }
        if (cmd.StartsWith("libtab:")) { _w.Library.SelectLibTab(int.Parse(cmd[7..])); return "ok"; }
        if (cmd == "cards")
        {
            // 진단: 화면의 카드 아트 — 주소 · Wide · Image 크기 · 받은 그림 크기
            var sb = new StringBuilder();
            void Walk(DependencyObject o)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
                {
                    var ch = VisualTreeHelper.GetChild(o, i);
                    if (ch is CardView cv)
                    {
                        var img = Find<Image>(cv);
                        var bmp = img?.Source as BitmapImage;
                        sb.AppendLine($"{cv.Card.Title[..Math.Min(20, cv.Card.Title.Length)]} wide={cv.Card.Wide} img={img?.ActualWidth:0}x{img?.ActualHeight:0} px={bmp?.PixelWidth}x{bmp?.PixelHeight} src={cv.Card.Thumb} -> {bmp?.UriSource}");
                        if (sb.Length > 6000) return;
                    }
                    else Walk(ch);
                }
            }
            Walk(_w.Root);
            return sb.ToString();
        }
        if (cmd == "notify" && p.Current != null) { Notifier.TestShow(p.Current); return "ok"; }
        if (cmd == "comments" && p.Current != null)
        {
            var cp = await InnerTube.Instance.Comments(p.Current.VideoId);
            return $"count={cp.Count} list={cp.Items.Count} next={cp.Next != null} first={cp.Items.FirstOrDefault()?.Text}";
        }
        if (cmd == "menutest")
        {
            // 진단: 메뉴를 열고, 메뉴 한가운데 · 첫 줄 위치에서 클릭될 수 있는 요소들을 위에서부터 나열
            var m = new OneMenu();
            for (var i = 0; i < 6; i++) m.Item($"항목 {i}", Icons.QueueAdd, () => Log.Info("[메뉴진단] 항목 동작"));
            m.Show(_w.Bar, new Point(300, 200));
            await Task.Delay(700);
            var sb = new StringBuilder();
            foreach (var pt in new[] { new Point(420, 230), new Point(420, 300) })
            {
                sb.AppendLine($"@{pt}:");
                foreach (var el in VisualTreeHelper.FindElementsInHostCoordinates(pt, _w.Root).Take(12))
                {
                    var fe = el as FrameworkElement;
                    sb.AppendLine($"  {el.GetType().Name} name={fe?.Name} hit={el.IsHitTestVisible} vis={el.Visibility} op={el.Opacity:0.##} size={fe?.ActualWidth:0}x{fe?.ActualHeight:0}");
                }
            }
            OneMenu.CloseAll();
            return sb.ToString();
        }
        if (cmd == "download" && p.Current != null)
        {
            var path = await Offline.Download(p.Current);
            try { File.Copy(path, IOPath.Combine(AppContext.BaseDirectory, "last-download.m4a"), true); } catch { }
            using var f = TagLib.File.Create(path);
            return $"{path} | title={f.Tag.Title} artist={f.Tag.FirstPerformer} album={f.Tag.Album} pics={f.Tag.Pictures.Length} {(f.Tag.Pictures.Length > 0 ? f.Tag.Pictures[0].MimeType + " " + f.Tag.Pictures[0].Data.Count + "B" : "")}";
        }
        if (cmd == "tiptest") return await _w.Bar.TipTest();
        if (cmd.StartsWith("suggest:")) return await _w.TestSuggest(cmd[8..]);
        if (cmd == "auth") return $"logged in: {Auth.IsLoggedIn}, cookies: {Auth.AllCookies().Count}";
        if (cmd == "nav:player") { _w.OpenPlayer(); return "ok"; }
        if (cmd == "nav:close") { _w.ClosePlayer(); return "ok"; }
        if (cmd == "nav:lyrics") { _w.OpenPlayer(); _w.PlayerPage.ShowLyricsTab(); return "ok"; }
        if (cmd == "toggle") { p.TogglePlay(); return "ok"; }
        // 아트가 곡을 따라오는지: 다음 곡으로 넘기며 GC 를 거듭 돌린다 (예전엔 불러오던 그림이 거둬져 아트가 멈췄다)
        if (cmd.StartsWith("artgc:"))
        {
            var n = int.Parse(cmd[6..]);
            var miss = 0;
            for (var i = 0; i < n; i++)
            {
                await p.Next();
                for (var k = 0; k < 6; k++) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(50); }
                await Task.Delay(1500);
                var art = _w.Bar.Art;
                if (art.ShownGen != art.RequestedGen) miss++;
            }
            return $"{n}곡 중 아트가 안 바뀐 곡 {miss}";
        }
        if (cmd == "next") { await p.Next(); return "ok"; }
        if (cmd.StartsWith("open:")) { _w.OpenBrowse(cmd[5..]); return "ok"; }
        if (cmd == "back") { _w.Back(); return "ok"; }
        if (cmd.StartsWith("search:")) { _w.ShowSearch(cmd[7..]); return "ok"; }
        if (cmd.StartsWith("seek:")) { p.Seek(double.Parse(cmd[5..])); return "ok"; }
        if (cmd == "play:first")
        {
            var list = await InnerTube.Instance.SearchSongs("아이유 밤편지");
            if (list.Count == 0) return "no results";
            await p.PlaySong(list[0]);
            return $"playing {list[0].Title}";
        }
        if (cmd is "mem" or "memgc")
        {
            if (cmd == "memgc")
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
            var gi = GC.GetGCMemoryInfo();
            using var pr = System.Diagnostics.Process.GetCurrentProcess();
            return $"ws {pr.WorkingSet64 >> 20} MB · private {pr.PrivateMemorySize64 >> 20} MB\n" +
                   $"gc heap {GC.GetTotalMemory(false) >> 20} MB · heap size {gi.HeapSizeBytes >> 20} MB · committed {gi.TotalCommittedBytes >> 20} MB · frag {gi.FragmentedBytes >> 20} MB\n" +
                   $"gens {string.Join("/", gi.GenerationInfo.ToArray().Select(g => g.SizeAfterBytes >> 20))} MB · gc count {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}";
        }
        if (cmd == "stats") return p.Current is { } st ? string.Join("\n", StreamResolver.Instance.Info(st.VideoId).Select(x => $"{x.Item1}: {x.Item2}")) : "no track";
        if (cmd.StartsWith("pair:")) return $"pair {await InnerTube.Instance.Counterpart(cmd[5..])}";
        if (cmd.StartsWith("findvideo:"))
        {
            // findvideo:videoId|제목|가수|앨범|초
            var f = cmd[10..].Split('|');
            var t = new Track(f[0], f[1], f[2], f[3], null, int.Parse(f[4]));
            return "found: " + (await StreamResolver.Instance.FindOriginalVideo(t) ?? "none");
        }
        if (cmd.StartsWith("play:"))
        {
            var parts = cmd[5..].Split('|', 2);
            await p.PlaySong(new Track(parts[0], parts[0], "", null, parts.Length > 1 ? parts[1] : null, 0));
            return "ok";
        }
        return "unknown: " + cmd;
    }

    private static T? Find<T>(DependencyObject o) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            if (c is T t) return t;
            if (Find<T>(c) is { } r) return r;
        }
        return null;
    }

    private async Task<string> Shot()
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(_w.Root);
        var pixels = await rtb.GetPixelsAsync();
        using var fs = new FileStream(_shot, FileMode.Create, FileAccess.ReadWrite);
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs.AsRandomAccessStream());
        var dpi = (float)(_w.Root.XamlRoot?.RasterizationScale ?? 1) * 96;
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, dpi, dpi, pixels.ToArray());
        await enc.FlushAsync();
        return $"saved {rtb.PixelWidth}x{rtb.PixelHeight}";
    }
}
