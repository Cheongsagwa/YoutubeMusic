using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// '동영상 연결' — 음원에 유튜브 영상을 직접 이어 준다. 곡 이름으로 유튜브를 찾아 보여 주고(길이가 비슷한 영상을 맨 위로),
/// 하나 고르고 [연결] 하면 그 뒤로 동영상 전환이 그 영상으로 된다
/// </summary>
public static class VideoLinkDialog
{
    public static async void Show(Track t)
    {
        VideoHit? chosen = null;
        // 앨범 이름이 들어가야 원본 영상이 잘 나온다 (예: 'OWCS KOREA 2026 Main Theme')
        var query = !string.IsNullOrWhiteSpace(t.Album) && t.Album != t.Title ? $"{t.Album} {t.Title}" : $"{t.Title} {t.Artist}".Trim();
        var box = NewPlaylistDialog.Field(query, "유튜브에서 찾기", false);
        var status = Theme.Text("", 13, FontWeights.SemiBold, Theme.Secondary);
        var list = new StackPanel { Spacing = 2 };
        var scroll = new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(box);
        content.Children.Add(status);
        content.Children.Add(scroll);
        Border? picked = null;
        var gen = 0;

        async void Search()
        {
            var my = ++gen;
            var q = box.Text.Trim();
            if (q.Length == 0) return;
            list.Children.Clear();
            chosen = null;
            picked = null;
            status.Text = "찾는 중…";
            try
            {
                var hits = await StreamResolver.Instance.SearchVideos(q);
                if (my != gen) return;
                // 음원과 길이가 비슷한(±3초) 영상이 원본일 가능성이 높다 → 위로
                var dur = t.DurationSec;
                hits = hits.Where(h => h.Id != t.VideoId).OrderBy(h => Same(h, dur) ? 0 : 1).ToList();
                status.Text = hits.Count == 0 ? "찾은 영상이 없어요" : "연결할 영상을 고르세요";
                var i = 0;
                foreach (var h in hits)
                {
                    var row = Row(h, dur);
                    row.Tapped += (_, _) =>
                    {
                        if (picked != null) picked.Background = new SolidColorBrush(Colors.Transparent);
                        picked = row;
                        chosen = h;
                        row.Background = Theme.B(Theme.Selected);
                    };
                    list.Children.Add(row);
                    Anim.Appear(row, i++ * 30, 8);
                }
            }
            catch (Exception e)
            {
                if (my != gen) return;
                Log.Warn($"동영상 찾기 실패: {e.Message}");
                status.Text = "찾지 못했어요";
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            Search();
        };
        Search();
        var ok = await OneDialog.Show("동영상 연결", content, "연결", width: 560);
        if (!ok) return;
        if (chosen == null) { Toast.Show("연결할 영상을 고르지 않았어요"); return; }
        VideoMeta.Set(chosen);
        if (!string.IsNullOrWhiteSpace(t.Album) && chosen.Channel.Length > 0) VideoMeta.SetAlbumChannel(t.Album, chosen.Channel);
        PlayerService.Instance.LinkVideo(t, chosen.Id);
        Toast.Show("동영상을 연결했어요");
    }

    private static bool Same(VideoHit h, int dur) => dur > 0 && h.Duration > 0 && Math.Abs(h.Duration - dur) <= 3;

    private static Border Row(VideoHit h, int dur)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border
        {
            Width = 96,
            Height = 54,
            CornerRadius = new CornerRadius(8),
            Background = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri($"https://i.ytimg.com/vi/{h.Id}/mqdefault.jpg")) { DecodePixelWidth = 192 },
                Stretch = Stretch.UniformToFill,
            },
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Theme.Text(h.Title, 14, FontWeights.SemiBold, Theme.OnSurface, 2));
        var meta = new List<string>();
        if (h.Channel.Length > 0) meta.Add(h.Channel);
        if (h.Duration > 0) meta.Add(Fmt.Time(h.Duration));
        if (Same(h, dur)) meta.Add("길이 같음");
        text.Children.Add(Theme.Text(string.Join(" · ", meta), 12, FontWeights.Normal, Theme.Secondary));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var row = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Colors.Transparent), Child = g };
        Anim.FadeBg(row);
        Anim.Pressable(row, 0.97f);
        return row;
    }
}
