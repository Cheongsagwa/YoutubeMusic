using System.Net;

namespace OneMusic.Core;

/// <summary>
/// 앨범아트 바이트 캐시 — 재생 바 · 재생 화면 · 앰비언트 빛이 같은 그림을 각자 다시 받지 않게,
/// 그리고 다음 곡 아트를 미리 받아 둬서 곡이 바뀌면 바로 보이게. 최근 48장만 들고 있는다(대략 10MB 안쪽).
/// </summary>
public static class ImageCache
{
    private static readonly HttpClient Http = new(Net.Handler())
    { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly Dictionary<string, Task<byte[]>> Items = new();
    private static readonly LinkedList<string> Order = new();
    private const int Max = 48;

    public static Task<byte[]> Get(string url)
    {
        lock (Items)
        {
            if (Items.TryGetValue(url, out var t) && !t.IsFaulted && !t.IsCanceled)
            {
                Order.Remove(url);
                Order.AddFirst(url);
                return t;
            }
            var task = Http.GetByteArrayAsync(url);
            Items[url] = task;
            Order.Remove(url);
            Order.AddFirst(url);
            while (Order.Count > Max)
            {
                Items.Remove(Order.Last!.Value);
                Order.RemoveLast();
            }
            return task;
        }
    }

    /// <summary>미리 받아 두기 (실패해도 조용히)</summary>
    public static void Prefetch(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        _ = Get(url).ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
    }
}
