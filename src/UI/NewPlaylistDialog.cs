using OneMusic.Core;
using Windows.ApplicationModel.DataTransfer;

namespace OneMusic.UI;

/// <summary>
/// 새 재생목록 대화상자 (YT Music '새 재생목록' 과 같은 항목, 모양은 One UI) —
/// 제목 · 설명 · 공개 범위(공개 · 일부 공개 · 비공개 알약 탭) · 공동작업(One UI 스위치).
/// 사이드바 '새 재생목록', 곡 ⋮ 메뉴, 다음 트랙 '저장' 이 같이 쓴다
/// </summary>
public static class NewPlaylistDialog
{
    private static readonly (string Label, string Value)[] Privacies =
    [
        ("공개", "PUBLIC"),
        ("일부 공개", "UNLISTED"),
        ("비공개", "PRIVATE"),
    ];

    /// <summary>대화상자를 띄우고, 확인하면 재생목록을 만들어 곡을 넣는다. 만든 재생목록 ID (취소 · 실패면 null)</summary>
    public static Task<string?> Create(IReadOnlyList<string> videoIds, string? suggestedTitle = null) =>
        Create(() => Task.FromResult(videoIds), suggestedTitle);

    /// <param name="videoIds">넣을 곡 — 확인을 누른 뒤에 받는다 (앨범 통째로 넣을 때 대화상자가 늦게 뜨지 않게)</param>
    public static async Task<string?> Create(Func<Task<IReadOnlyList<string>>> videoIds, string? suggestedTitle = null)
    {
        // 칸 위에 '제목' · '설명' 이름이 있으니 안쪽 안내 글자는 두지 않는다
        var title = Field(suggestedTitle ?? "", "", multiline: false);
        var desc = Field("", "", multiline: true);
        var privacy = new PillTabs(Privacies.Select(p => p.Label).ToArray()) { HorizontalAlignment = HorizontalAlignment.Stretch };
        privacy.Select(2, false); // 기본은 비공개 (나중에 바꿀 수 있다)
        var collab = new OneToggle(false) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Label("제목"));
        body.Children.Add(title);
        body.Children.Add(Gap());
        body.Children.Add(Label("설명"));
        body.Children.Add(desc);
        body.Children.Add(Gap());
        body.Children.Add(Label("공개 범위"));
        body.Children.Add(privacy);
        body.Children.Add(Gap());
        // 공동작업: 이름 + 오른쪽 스위치 (One UI 설정 줄)
        var collabRow = new Grid { Padding = new Thickness(16, 12, 14, 12), CornerRadius = new CornerRadius(18), Background = Theme.B(Color.FromArgb(14, 255, 255, 255)) };
        var collabText = Theme.Text("공동작업", 15, FontWeights.Bold, Theme.OnSurface);
        collabText.Margin = new Thickness(0, 0, 64, 0);
        collabText.VerticalAlignment = VerticalAlignment.Center;
        collabRow.Children.Add(collabText);
        collabRow.Children.Add(collab);
        body.Children.Add(collabRow);

        var ok = await OneDialog.Show("새 재생목록", body, "만들기", width: 480, ready: confirm =>
        {
            title.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter) return;
                e.Handled = true;
                confirm();
            };
            title.Loaded += (_, _) => { title.Focus(FocusState.Programmatic); title.SelectAll(); };
        });
        if (!ok) return null;
        var name = title.Text.Trim();
        if (name.Length == 0)
        {
            Toast.Show("재생목록 제목을 적어 주세요");
            return null;
        }
        var value = Privacies[privacy.Selected].Value;
        string? id;
        try { id = await InnerTube.Instance.CreatePlaylist(name, await videoIds(), value, desc.Text.Trim()); }
        catch (Exception e)
        {
            Log.Warn($"재생목록 만들기 실패: {e.Message}");
            id = null;
        }
        if (id == null)
        {
            Toast.Show("만들지 못했어요");
            return null;
        }
        if (collab.IsOn)
        {
            try
            {
                var link = await InnerTube.Instance.EnableCollaboration(id);
                if (!string.IsNullOrEmpty(link))
                {
                    var pkg = new DataPackage();
                    pkg.SetText(link);
                    Clipboard.SetContent(pkg);
                    Toast.Show($"'{name}' 을(를) 만들고 공동작업 초대 링크를 복사했어요");
                    return id;
                }
                Toast.Show(link == null ? $"'{name}' 을(를) 만들었지만 공동작업은 켜지 못했어요" : $"'{name}' 공동작업 재생목록을 만들었어요");
                return id;
            }
            catch (Exception e)
            {
                Log.Warn($"공동작업 켜기 실패: {e.Message}");
                Toast.Show($"'{name}' 을(를) 만들었지만 공동작업은 켜지 못했어요");
                return id;
            }
        }
        Toast.Show($"'{name}' 재생목록을 만들었어요");
        return id;
    }

    private static TextBlock Label(string text) => Theme.Text(text, 13, FontWeights.SemiBold, Theme.Secondary);

    private static FrameworkElement Gap() => new Border { Height = 4 };

    /// <summary>One UI 입력 칸 — 옅은 채움 둥근 칸, 누르면 흰 테두리</summary>
    internal static TextBox Field(string text, string placeholder, bool multiline)
    {
        var box = new TextBox
        {
            Text = text,
            PlaceholderText = placeholder,
            FontFamily = Theme.Font,
            FontSize = 15,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10, 14, 10),
        };
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.Height = 92;
        }
        box.Resources["TextControlBackground"] = Theme.B(Color.FromArgb(20, 255, 255, 255));
        box.Resources["TextControlBackgroundPointerOver"] = Theme.B(Color.FromArgb(28, 255, 255, 255));
        box.Resources["TextControlBackgroundFocused"] = Theme.B(Color.FromArgb(28, 255, 255, 255));
        box.Resources["TextControlBorderBrush"] = new SolidColorBrush(Colors.Transparent);
        box.Resources["TextControlBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
        box.Resources["TextControlBorderBrushFocused"] = Theme.B(Colors.White);
        box.Resources["TextControlForegroundFocused"] = Theme.B(Colors.White);
        box.Resources["TextControlPlaceholderForeground"] = Theme.B(Theme.Secondary);
        box.Resources["TextControlPlaceholderForegroundFocused"] = Theme.B(Theme.Secondary);
        return box;
    }
}
