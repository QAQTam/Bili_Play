using BiliPlay.Services;

namespace BiliPlay;

public sealed partial class MainWindow
{
    private GlobalHotkeyService? _hotkeys;
    private void BindHotkeys()
    {
        if (App.IsVisualCheck || DebugSession.IsActive) { _hotkeys?.Configure(false, BiliPlay.Core.HotkeyBinding.Defaults); return; }
        _hotkeys ??= new(WinRT.Interop.WindowNative.GetWindowHandle(this), action =>
        {
            // Dispatch after returning from the native window callback.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ViewModel.IsLoggedIn || !ViewModel.GlobalHotkeysEnabled) return;
                var command = action == 0 ? ViewModel.PreviousCommand : action == 1 ? ViewModel.NextCommand : ViewModel.TogglePlayCommand;
                if (command.CanExecute(null)) _ = command.ExecuteAsync(null);
            });
        });
        ViewModel.ConfigureGlobalHotkeys = _hotkeys.Configure;
        ViewModel.ApplyHotkeys();
    }
}
