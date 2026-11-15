using BiliPlay.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    private HotkeyBinding[] _hotkeys = HotkeyBinding.Defaults.ToArray();
    private bool _capturingHotkey;
    [ObservableProperty] public partial bool GlobalHotkeysEnabled { get; set; } = true;
    [ObservableProperty] public partial string HotkeyStatus { get; set; } = "";
    public string PreviousHotkeyLabel => _hotkeys[0].Label;
    public string NextHotkeyLabel => _hotkeys[1].Label;
    public string ToggleHotkeyLabel => _hotkeys[2].Label;
    public Func<bool, IReadOnlyList<HotkeyBinding>, string?>? ConfigureGlobalHotkeys { get; set; }
    private void LoadHotkeys()
    {
        _hotkeys = HotkeyBinding.Defaults.Select((binding, index) => HotkeyBinding.Parse(Store.GetSetting("hotkey:" + index, binding.ToString()), binding)).ToArray();
        GlobalHotkeysEnabled = Store.GetSetting("globalHotkeys", "true") != "false";
    }
    partial void OnGlobalHotkeysEnabledChanged(bool value)
    {
        if (ConfigureGlobalHotkeys is null) return;
        var error = ConfigureGlobalHotkeys(value, _hotkeys);
        HotkeyStatus = error ?? (value ? "全局快捷键已启用。" : "全局快捷键已关闭，系统媒体键仍可使用。");
        Store.SetSetting("globalHotkeys", value ? "true" : "false");
    }
    public void ApplyHotkeys()
    {
        if (_disposed) return;
        HotkeyStatus = ConfigureGlobalHotkeys?.Invoke(GlobalHotkeysEnabled, _hotkeys)
            ?? (GlobalHotkeysEnabled ? "全局快捷键已启用。" : "全局快捷键已关闭。");
    }
    public void BeginHotkeyCapture() { _capturingHotkey = true; ConfigureGlobalHotkeys?.Invoke(false, _hotkeys); }
    public void EndHotkeyCapture() { _capturingHotkey = false; ApplyHotkeys(); }
    public void SetHotkey(int action, HotkeyBinding binding)
    {
        if (action is < 0 or > 2 || !binding.IsValid) { HotkeyStatus = "请按 Ctrl 或 Alt 加字母、数字、方向键或功能键；Windows 键和 F12 保留给系统。"; return; }
        var updated = _hotkeys.ToArray(); updated[action] = binding;
        if (updated.Distinct().Count() != 3) { HotkeyStatus = "三个操作不能使用相同的快捷键。"; return; }
        var error = ConfigureGlobalHotkeys?.Invoke(GlobalHotkeysEnabled, updated);
        if (_capturingHotkey) ConfigureGlobalHotkeys?.Invoke(false, _hotkeys);
        if (error is not null) { HotkeyStatus = error; return; }
        _hotkeys = updated; Store.SetSetting("hotkey:" + action, binding.ToString());
        OnPropertyChanged(nameof(PreviousHotkeyLabel)); OnPropertyChanged(nameof(NextHotkeyLabel)); OnPropertyChanged(nameof(ToggleHotkeyLabel));
        HotkeyStatus = GlobalHotkeysEnabled ? "快捷键已保存并生效。" : "快捷键已保存，开启开关后生效。";
    }
}
