using System.Runtime.InteropServices;
using OneMusic.Core;

namespace OneMusic.UI;

/// <summary>
/// 작업 표시줄 미리보기 버튼 (pear taskbar-mediacontrol) — 작업 표시줄의 앱 아이콘에 마우스를 올리면 뜨는
/// 미리보기 밑에 이전 · 재생/일시정지 · 다음. 아이콘은 재생 바와 같은 모양을 직접 그려 만든다
/// </summary>
public static class TaskbarButtons
{
    private const int WM_COMMAND = 0x0111;
    private const int THBN_CLICKED = 0x1800;
    private const uint THB_ICON = 0x2, THB_TOOLTIP = 0x4, THB_FLAGS = 0x8;
    private const uint THBF_ENABLED = 0, THBF_DISABLED = 0x1;
    private const int GWLP_WNDPROC = -4;
    private const int SM_CXSMICON = 49;

    private static IntPtr _hwnd, _oldProc;
    private static WndProc? _proc; // GC 가 걷어 가지 않게 붙잡아 둔다
    private static ITaskbarList3? _list;
    private static uint _createdMsg;
    private static IntPtr _iPrev, _iNext, _iPlay, _iPause;
    private static bool _added, _playing, _enabled;

    public static void Attach(Window w)
    {
        try
        {
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
            _createdMsg = RegisterWindowMessage("TaskbarButtonCreated");
            var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow(_hwnd));
            if (size <= 0) size = 16;
            _iPrev = MakeIcon(size, (x, y) => InRect(x, y, 4.8f, 5.2f, 7f, 18.8f) || InTri(x, y, 19.2f, 6.2f, 9.4f, 12f, 19.2f, 17.8f));
            _iNext = MakeIcon(size, (x, y) => InRect(x, y, 17f, 5.2f, 19.2f, 18.8f) || InTri(x, y, 4.8f, 6.2f, 14.6f, 12f, 4.8f, 17.8f));
            _iPlay = MakeIcon(size, (x, y) => InTri(x, y, 7.5f, 4.8f, 19.2f, 12f, 7.5f, 19.2f));
            _iPause = MakeIcon(size, (x, y) => InRect(x, y, 6.5f, 5f, 10.3f, 19f) || InRect(x, y, 13.7f, 5f, 17.5f, 19f));
            _proc = Proc;
            _oldProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _proc);
            _list = (ITaskbarList3)new TaskbarList();
            _list.HrInit();
            Add();
            var p = PlayerService.Instance;
            p.StateChanged += Update;
            p.TrackChanged += Update;
        }
        catch (Exception e) { Log.Warn($"작업 표시줄 버튼 실패: {e.Message}"); }
    }

    private static THUMBBUTTON[] Buttons()
    {
        var flags = _enabled ? THBF_ENABLED : THBF_DISABLED;
        THUMBBUTTON B(uint id, IntPtr icon, string tip) => new()
        {
            dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS,
            iId = id,
            hIcon = icon,
            szTip = tip,
            dwFlags = flags,
        };
        return
        [
            B(1, _iPrev, "이전"),
            B(2, _playing ? _iPause : _iPlay, _playing ? "일시정지" : "재생"),
            B(3, _iNext, "다음"),
        ];
    }

    private static void Add()
    {
        if (_added || _list == null) return;
        try
        {
            _enabled = PlayerService.Instance.Current != null;
            _playing = PlayerService.Instance.IsPlaying;
            _list.ThumbBarAddButtons(_hwnd, 3, Buttons());
            _added = true;
        }
        catch (Exception e) { Log.Info($"작업 표시줄 버튼은 아이콘이 생기면 다시: {e.Message}"); }
    }

    private static void Update()
    {
        if (!_added || _list == null) return;
        var p = PlayerService.Instance;
        var playing = p.IsPlaying;
        var enabled = p.Current != null;
        if (playing == _playing && enabled == _enabled) return;
        _playing = playing;
        _enabled = enabled;
        try { _list.ThumbBarUpdateButtons(_hwnd, 3, Buttons()); }
        catch (Exception e) { Log.Warn($"작업 표시줄 버튼 갱신 실패: {e.Message}"); }
    }

    private static IntPtr Proc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == _createdMsg && _createdMsg != 0)
            {
                // 탐색기가 다시 켜지면 버튼을 새로 붙인다
                _added = false;
                Add();
            }
            else if (msg == WM_COMMAND && ((wParam.ToInt64() >> 16) & 0xFFFF) == THBN_CLICKED)
            {
                var p = PlayerService.Instance;
                switch (wParam.ToInt64() & 0xFFFF)
                {
                    case 1: _ = p.Previous(); break;
                    case 2: p.TogglePlay(); break;
                    case 3: _ = p.Next(); break;
                }
                return IntPtr.Zero;
            }
        }
        catch (Exception e) { Log.Warn($"작업 표시줄 버튼 처리 실패: {e.Message}"); }
        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }

    // ── 아이콘 그리기 (24×24 좌표의 도형을 4×4 표본으로 부드럽게) ──

    private static bool InRect(float x, float y, float x0, float y0, float x1, float y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1;

    private static bool InTri(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float S(float px, float py, float qx, float qy, float rx, float ry) => (px - rx) * (qy - ry) - (qx - rx) * (py - ry);
        var d1 = S(x, y, ax, ay, bx, by);
        var d2 = S(x, y, bx, by, cx, cy);
        var d3 = S(x, y, cx, cy, ax, ay);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static IntPtr MakeIcon(int size, Func<float, float, bool> inside)
    {
        const int ss = 4;
        var px = new int[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var hit = 0;
                for (var j = 0; j < ss; j++)
                    for (var i = 0; i < ss; i++)
                        if (inside((x + (i + 0.5f) / ss) / size * 24f, (y + (j + 0.5f) / ss) / size * 24f)) hit++;
                var a = hit * 255 / (ss * ss);
                px[y * size + x] = (a << 24) | 0x00FFFFFF;
            }
        var color = CreateBitmap(size, size, 1, 32, px);
        var mask = CreateMaskBitmap(size, size, 1, 1, new byte[(size + 15) / 16 * 2 * size]);
        var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        var icon = CreateIconIndirect(ref info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    // ── Win32 · COM ──

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProc newProc);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, int[] bits);

    [DllImport("gdi32.dll", EntryPoint = "CreateBitmap")]
    private static extern IntPtr CreateMaskBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public uint dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szTip;
        public uint dwFlags;
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList { }

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr clip);
    }
}
