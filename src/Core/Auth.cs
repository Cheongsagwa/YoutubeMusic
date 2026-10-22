using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace OneMusic.Core;

/// <summary>
/// YouTube Music 로그인 상태 (구글 쿠키).
/// 쿠키는 Windows 사용자 계정으로 암호화해서 %LocalAppData%\OneMusic\auth.bin 에 둔다 — 이 PC 의 이 사용자만 풀 수 있다.
/// 요청을 보낼 때마다 YouTube 가 새로 준 쿠키(Set-Cookie)도 반영해서 로그인이 오래 유지된다.
/// </summary>
public static class Auth
{
    private static readonly string FilePath = IOPath.Combine(Log.AppDataDir, "auth.bin");
    public static CookieContainer Cookies { get; private set; } = new();
    public static bool IsLoggedIn => Sapisid != null;
    public static event Action? Changed;

    private static readonly Uri Music = new("https://music.youtube.com/");
    private static readonly Uri Youtube = new("https://www.youtube.com/");

    public static string? Sapisid
    {
        get
        {
            foreach (var name in new[] { "SAPISID", "__Secure-3PAPISID" })
            {
                var c = Cookies.GetCookies(Music)[name] ?? Cookies.GetCookies(Youtube)[name];
                if (c != null && !string.IsNullOrEmpty(c.Value)) return c.Value;
            }
            return null;
        }
    }

    /// <summary>요청에 붙일 Authorization 헤더 (SAPISIDHASH)</summary>
    public static string? AuthorizationHeader(string origin = "https://music.youtube.com")
    {
        var sapisid = Sapisid;
        if (sapisid == null) return null;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{ts} {sapisid} {origin}"))).ToLowerInvariant();
        return $"SAPISIDHASH {ts}_{hash}";
    }

    public static IReadOnlyList<Cookie> AllCookies()
    {
        var list = new List<Cookie>();
        foreach (Cookie c in Cookies.GetAllCookies()) list.Add(c);
        return list;
    }

    public static void SetCookies(IEnumerable<Cookie> cookies)
    {
        var jar = new CookieContainer();
        foreach (var c in cookies)
        {
            try { jar.Add(c); }
            catch (Exception e) { Log.Warn($"쿠키 추가 실패 {c.Name}: {e.Message}"); }
        }
        Cookies = jar;
        _ = Save();
        Changed?.Invoke();
    }

    public static void Logout()
    {
        Cookies = new CookieContainer();
        try { File.Delete(FilePath); } catch { }
        Changed?.Invoke();
    }

    /// <summary>yt-dlp 에 넘길 쿠키 파일 (Netscape 형식) — 로그인한 계정으로 받아야 프리미엄 음질(256k)이 나온다</summary>
    public static void WriteNetscape(string path)
    {
        var sb = new StringBuilder("# Netscape HTTP Cookie File\n");
        foreach (var c in AllCookies())
        {
            var domain = c.Domain.StartsWith('.') ? c.Domain : "." + c.Domain;
            var exp = c.Expires == DateTime.MinValue ? 0 : new DateTimeOffset(c.Expires).ToUnixTimeSeconds();
            sb.Append($"{domain}\tTRUE\t{c.Path}\t{(c.Secure ? "TRUE" : "FALSE")}\t{exp}\t{c.Name}\t{c.Value}\n");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private sealed record Stored(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly, DateTime Expires);

    public static async Task Save()
    {
        try
        {
            var data = AllCookies().Select(c => new Stored(c.Name, c.Value, c.Domain, c.Path, c.Secure, c.HttpOnly, c.Expires)).ToList();
            var json = JsonSerializer.Serialize(data);
            var provider = new DataProtectionProvider("LOCAL=user");
            var buf = await provider.ProtectAsync(CryptographicBuffer.ConvertStringToBinary(json, BinaryStringEncoding.Utf8));
            CryptographicBuffer.CopyToByteArray(buf, out var bytes);
            await File.WriteAllBytesAsync(FilePath, bytes);
        }
        catch (Exception e) { Log.Warn($"로그인 정보 저장 실패: {e.Message}"); }
    }

    public static async Task Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var bytes = await File.ReadAllBytesAsync(FilePath);
            var provider = new DataProtectionProvider();
            var buf = await provider.UnprotectAsync(CryptographicBuffer.CreateFromByteArray(bytes));
            var json = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, buf);
            var data = JsonSerializer.Deserialize<List<Stored>>(json) ?? new();
            var jar = new CookieContainer();
            foreach (var s in data)
            {
                if (s.Expires != DateTime.MinValue && s.Expires < DateTime.Now) continue;
                try
                {
                    jar.Add(new Cookie(s.Name, s.Value, s.Path, s.Domain) { Secure = s.Secure, HttpOnly = s.HttpOnly, Expires = s.Expires });
                }
                catch { /* 무시 */ }
            }
            Cookies = jar;
            Log.Info($"로그인 정보 불러옴 (쿠키 {data.Count}개, 로그인 {(IsLoggedIn ? "됨" : "안 됨")})");
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Warn($"로그인 정보 읽기 실패: {e.Message}"); }
    }
}
