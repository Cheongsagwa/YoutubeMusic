using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace OneMusic.Core;

/// <summary>
/// 앱 이름 · 아이콘을 윈도우에 알린다 — 패키지 없는 앱이라 그냥 두면 작업 표시줄 미디어 창(SMTC)에 '알 수 없는 앱' 으로 나왔다.
///  1) 프로세스 AppUserModelID 를 정하고 (창 · 미디어 컨트롤이 이 ID 로 묶인다)
///  2) 시작 메뉴에 같은 ID 를 단 바로가기(이름 · 아이콘)를 둔다 — 윈도우는 이 바로가기에서 이름 · 아이콘을 찾는다
///  3) 알림용 레지스트리(AppUserModelId\ID: DisplayName · IconUri)도 맞춰 둔다
/// </summary>
public static class AppIdentity
{
    public const string Name = "YouTube Music";
    public const string Aumid = "Cheongsagwa.OneMusic.YouTubeMusic";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    /// <summary>창을 만들기 전에 부른다</summary>
    public static void Apply()
    {
        try { SetCurrentProcessExplicitAppUserModelID(Aumid); }
        catch (Exception e) { Log.Warn($"앱 ID 설정 실패: {e.Message}"); }
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        var icon = IOPath.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}");
            key.SetValue("DisplayName", Name);
            if (File.Exists(icon)) key.SetValue("IconUri", icon);
        }
        catch (Exception e) { Log.Warn($"앱 ID 레지스트리 실패: {e.Message}"); }
        // 설치 프로그램으로 깔았으면(install.json) 바로가기는 설치 때 고른 대로 — 앱이 마음대로 만들지 않는다
        if (File.Exists(IOPath.Combine(AppContext.BaseDirectory, "install.json"))) return;
        // 개발 중 실행 등: 바로가기는 뒤에서 (시작이 늦어지지 않게)
        _ = Task.Run(() => EnsureShortcut(exe, File.Exists(icon) ? icon : exe));
    }

    private static void EnsureShortcut(string exe, string icon)
    {
        try
        {
            var dir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            var path = IOPath.Combine(dir, Name + ".lnk");
            var stamp = IOPath.Combine(Log.AppDataDir, "shortcut.txt");
            // 같은 실행 파일로 이미 만들어 뒀으면 건너뛴다 (실행 파일 위치가 바뀌면 다시)
            if (File.Exists(path) && File.Exists(stamp) && File.ReadAllText(stamp) == exe) return;
            var link = (IShellLinkW)new CShellLink();
            link.SetPath(exe);
            link.SetWorkingDirectory(IOPath.GetDirectoryName(exe)!);
            link.SetIconLocation(icon, 0);
            link.SetDescription(Name);
            var store = (IPropertyStore)link;
            var keyId = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5); // System.AppUserModel.ID
            var value = new PropVariant(Aumid);
            store.SetValue(ref keyId, ref value);
            store.Commit();
            value.Dispose();
            ((IPersistFile)link).Save(path, true);
            File.WriteAllText(stamp, exe);
            Log.Info($"시작 메뉴 바로가기 만듦: {path}");
        }
        catch (Exception e) { Log.Warn($"바로가기 만들기 실패: {e.Message}"); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
        public PropertyKey(Guid f, int p) { FormatId = f; PropertyId = p; }
    }

    /// <summary>문자열 하나만 담는 PROPVARIANT (VT_LPWSTR)</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)] private ushort _vt;
        [FieldOffset(8)] private IntPtr _ptr;
        [FieldOffset(16)] private IntPtr _pad; // PROPVARIANT 은 x64 에서 24바이트

        public PropVariant(string value)
        {
            _vt = 31; // VT_LPWSTR
            _ptr = Marshal.StringToCoTaskMemUni(value);
        }

        public void Dispose()
        {
            if (_ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(_ptr);
            _ptr = IntPtr.Zero;
        }
    }
}
