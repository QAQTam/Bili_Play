using System.Runtime.InteropServices;

namespace BiliPlay.Services;

/// <summary>Native notification-area integration without WinForms or a resident helper process.</summary>
public sealed class TrayService : IDisposable
{
    private const uint Callback = 0x8120;
    private readonly nint _window;
    private readonly SubclassProc _procedure;
    private readonly Action _show, _exit;
    private readonly uint _taskbarCreated;
    private IconData _data;
    private bool _disposed;
    private static readonly Guid StableIconGuid = new("8C2F2D8B-8F3D-4D53-9A12-7E6F5B4B2C10");
    public bool Available { get; private set; }

    public TrayService(nint window, Action show, Action exit)
    {
        _window = window; _show = show; _exit = exit;
        _procedure = WindowProc;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), 1, 0, 0, 0x10 | 0x40);
        _data = new()
        {
            Size = (uint)Marshal.SizeOf<IconData>(), Window = window, Id = 1,
            Flags = 1 | 2 | 4 | 0x20, Message = Callback, Icon = icon, Guid = StableIconGuid, Tip = AppProfile.DisplayName,
            Info = "", InfoTitle = ""
        };
        if (SetWindowSubclass(window, _procedure, 1, 0))
        {
            // Remove a stale instance with the same stable identity before adding the current process icon.
            ShellNotifyIcon(2, ref _data);
            Available = ShellNotifyIcon(0, ref _data);
        }
    }
    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x24)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var scale = GetDpiForWindow(hwnd) / 96.0;
            info.MinTrackSize = new() { X = (int)(840 * scale), Y = (int)(560 * scale) };
            Marshal.StructureToPtr(info, lParam, false);
            return 0;
        }
        if (message == Callback)
        {
            if ((uint)lParam is 0x202 or 0x203) _show();
            if ((uint)lParam == 0x205) ShowMenu();
            return 0;
        }
        if (message == _taskbarCreated) Available = ShellNotifyIcon(0, ref _data);
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, 3, "退出");
            GetCursorPos(out var point); SetForegroundWindow(_window);
            var action = TrackPopupMenu(menu, 0x100 | 2, point.X, point.Y, 0, _window, 0);
            if (action == 3) _exit();
            PostMessage(_window, 0, 0, 0);
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ShellNotifyIcon(2, ref _data); RemoveWindowSubclass(_window, _procedure, 1);
        if (_data.Icon != 0) DestroyIcon(_data.Icon);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint Balloon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint action, ref IconData data);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint hwnd, uint msg, nuint wParam, nint lParam);
}
