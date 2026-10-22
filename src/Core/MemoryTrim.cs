using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace OneMusic.Core;

/// <summary>
/// 창을 최소화하거나 한동안 다른 창(게임)을 쓰는 동안, 당장 안 쓰는 메모리를 Windows 에 돌려준다.
/// .NET 힙을 한 번 압축하고 작업 집합을 비운다 — 화면 · 소리 품질과는 상관없고, 다시 쓰는 부분은 그때 돌아온다
/// </summary>
public static class MemoryTrim
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    private static long _last;
    private static int _ticket;
    private static DispatcherQueueTimer? _timer;

    public static void Now()
    {
        // 너무 자주 하지 않는다 (30초에 한 번)
        var now = Environment.TickCount64;
        if (now - _last < 30_000) return;
        _last = now;
        Task.Run(() =>
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            SetProcessWorkingSetSize(-1, -1, -1); // -1 = 이 프로세스
        });
    }

    /// <summary>1분 뒤에도 still() 이면 (계속 다른 창을 쓰고 있으면) 비운다</summary>
    public static void Later(DispatcherQueue q, Func<bool> still)
    {
        var my = ++_ticket;
        _timer?.Stop();
        var t = _timer = q.CreateTimer();
        t.Interval = TimeSpan.FromMinutes(1);
        t.IsRepeating = false;
        t.Tick += (_, _) => { if (my == _ticket && still()) Now(); };
        t.Start();
    }
}
