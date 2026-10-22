using Microsoft.Web.WebView2.Core;
using OneMusic.Core;
using Windows.Graphics;

namespace OneMusic.UI;

/// <summary>
/// 구글 로그인 창 — 사용자가 직접 로그인하면 YouTube Music 쿠키만 가져오고 창을 닫는다.
/// (웹 엔진은 로그인하는 동안에만 떠 있고, 닫으면 메모리에서 사라진다)
/// </summary>
public sealed class LoginWindow : Window
{
    private readonly WebView2 _web = new();
    private readonly TaskCompletionSource<bool> _done = new();
    private bool _finished;

    public Task<bool> Result => _done.Task;

    public LoginWindow()
    {
        Title = "YouTube Music 로그인";
        AppWindow.Resize(new SizeInt32(560, 760));
        var root = new Grid { Background = Theme.B(Theme.Bg) };
        root.Children.Add(_web);
        Content = root;
        Closed += (_, _) =>
        {
            _done.TrySetResult(_finished);
            try { _web.Close(); } catch { }
        };
        _ = Start();
    }

    private async Task Start()
    {
        try
        {
            var folder = IOPath.Combine(Log.AppDataDir, "WebView2");
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, folder, new CoreWebView2EnvironmentOptions());
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.NavigationCompleted += async (_, _) => await Check();
            _web.CoreWebView2.Navigate("https://accounts.google.com/ServiceLogin?service=youtube&hl=ko&continue=https%3A%2F%2Fmusic.youtube.com%2F");
        }
        catch (Exception e)
        {
            Log.Error("로그인 창을 띄우지 못함", e);
            _done.TrySetResult(false);
            Close();
        }
    }

    private async Task Check()
    {
        if (_finished) return;
        var url = _web.CoreWebView2.Source ?? "";
        if (!url.StartsWith("https://music.youtube.com")) return;
        var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://music.youtube.com");
        var list = cookies.Select(c => new System.Net.Cookie(c.Name, c.Value, c.Path, c.Domain)
        {
            Secure = c.IsSecure,
            HttpOnly = c.IsHttpOnly,
            Expires = c.IsSession ? DateTime.MinValue : DateTimeOffset.FromUnixTimeSeconds((long)c.Expires).LocalDateTime,
        }).ToList();
        if (!list.Any(c => c.Name is "SAPISID" or "__Secure-3PAPISID")) return; // 아직 로그인 전
        _finished = true;
        Auth.SetCookies(list);
        Log.Info($"로그인 완료 (쿠키 {list.Count}개)");
        _done.TrySetResult(true);
        Close();
    }
}
