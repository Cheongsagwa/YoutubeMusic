using System.Net;
using System.Net.Sockets;

namespace OneMusic.Core;

/// <summary>
/// 네트워크 공통 — IPv4 먼저 연결.
/// IPv6 경로가 막힌 네트워크에서는 새 서버에 연결할 때마다 IPv6 를 먼저 시도하다 3~4초 기다린 뒤 IPv4 로 넘어간다.
/// YouTube 는 곡마다 다른 서버(googlevideo)에서 오디오를 주기 때문에 곡을 틀 때마다 그만큼 늦어졌다.
/// </summary>
public static class Net
{
    public static SocketsHttpHandler Handler(TimeSpan? lifetime = null) => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = lifetime ?? TimeSpan.FromMinutes(10),
        MaxConnectionsPerServer = 8,
        ConnectTimeout = TimeSpan.FromSeconds(8),
        ConnectCallback = ConnectIpv4First,
    };

    private static async ValueTask<Stream> ConnectIpv4First(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var addrs = await Dns.GetHostAddressesAsync(host, ct);
        var ordered = addrs.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
        Exception? last = null;
        foreach (var addr in ordered)
        {
            var s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // IPv4 는 넉넉히, IPv6 는 짧게 기다린다
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(addr.AddressFamily == AddressFamily.InterNetwork ? 6000 : 1500);
                await s.ConnectAsync(new IPEndPoint(addr, ctx.DnsEndPoint.Port), timeout.Token);
                return new NetworkStream(s, ownsSocket: true);
            }
            catch (Exception e)
            {
                s.Dispose();
                last = e;
                if (ct.IsCancellationRequested) throw;
            }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }
}
