using System.Runtime.InteropServices;
using BiliPlay.Core;

namespace BiliPlay.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int FirstId = 0x4250;
    private const nuint SubclassId = 0x42504B;
    private readonly nint _window;
    private readonly Action<int> _pressed;
    private readonly SubclassProc _procedure;
    private readonly Dictionary<int, HotkeyBinding> _registered = [];
    private bool _disposed;
    public GlobalHotkeyService(nint window, Action<int> pressed)
    {
        _window = window; _pressed = pressed; _procedure = WindowProc;
        if (!SetWindowSubclass(window, _procedure, SubclassId, 0)) throw new InvalidOperationException("无法启用全局快捷键。");
    }
    public string? Configure(bool enabled, IReadOnlyList<HotkeyBinding> bindings)
    {
        if (_disposed) return "全局快捷键已停止。";
        if (!enabled) { Clear(); return null; }
        if (bindings.Count != 3 || bindings.Any(binding => !binding.IsValid) || bindings.Distinct().Count() != 3)
            return "快捷键需包含 Ctrl 或 Alt，且三个操作不能使用相同组合。";
        var previous = _registered.ToArray();
        Clear();
        for (var index = 0; index < bindings.Count; index++)
        {
            if (RegisterHotKey(_window, FirstId + index, bindings[index].Modifiers | 0x4000, bindings[index].Key))
            { _registered[index] = bindings[index]; continue; }
            Clear();
            foreach (var (id, binding) in previous)
                if (RegisterHotKey(_window, FirstId + id, binding.Modifiers | 0x4000, binding.Key)) _registered[id] = binding;
            return $"{bindings[index].Label} 已被占用或由系统保留，此组合未启用，请设置其他组合。";
        }
        return null;
    }
    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var action = (int)wParam - FirstId;
        if (message == 0x0312 && _registered.ContainsKey(action)) { _pressed(action); return 0; }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private void Clear() { foreach (var id in _registered.Keys) UnregisterHotKey(_window, FirstId + id); _registered.Clear(); }
    public void Dispose() { if (_disposed) return; _disposed = true; Clear(); RemoveWindowSubclass(_window, _procedure, SubclassId); }
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProc procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProc procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}
