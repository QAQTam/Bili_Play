using System.Runtime.InteropServices;
using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BiliPlay;

public sealed partial class MainPage
{
    private bool _batchDownloadDialogOpen;
    private void ClearListSearchClick(object sender, RoutedEventArgs args) => ViewModel.ListSearchText = "";
    private void SelectAllFolderTracksClick(object sender, RoutedEventArgs args) => TrackList.SelectAll();
    private async void DownloadFolderSelectionClick(object sender, RoutedEventArgs args)
    {
        var tracks = TrackList.SelectedItems.OfType<Track>().ToArray();
        if (tracks.Length == 0 || _batchDownloadDialogOpen) return;
        _batchDownloadDialogOpen = true;
        try
        {
            var content = new StackPanel { Spacing = 12, MaxWidth = 420 };
            content.Children.Add(new TextBlock { Text = $"下载所选 {tracks.Length} 首音乐，已下载和重复任务会自动跳过。", TextWrapping = TextWrapping.Wrap });
            var high = new CheckBox { Content = "优先使用 Hi-Res 音源", IsChecked = ViewModel.IsVip && ViewModel.PreferHiRes, IsEnabled = ViewModel.IsVip };
            content.Children.Add(high);
            content.Children.Add(new TextBlock { Text = "保存为 MP3：标准音源 192 kbps，Hi-Res 音源 320 kbps。无 Hi-Res 或账号无权限时使用标准音源。", TextWrapping = TextWrapping.Wrap });
            if (await Dialog("批量下载", content, "加入下载任务").ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.QueueFolderDownloadsAsync(tracks, high.IsChecked == true);
        }
        finally { _batchDownloadDialogOpen = false; }
    }
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    private void HotkeyCaptureGotFocus(object sender, RoutedEventArgs args) => ViewModel.BeginHotkeyCapture();
    private void HotkeyCaptureLostFocus(object sender, RoutedEventArgs args) => ViewModel.EndHotkeyCapture();
    private void HotkeyCaptureKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Tab) return;
        args.Handled = true;
        if (args.Key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
        if (sender is not TextBox box || !int.TryParse(box.Tag?.ToString(), out var action)) return;
        var modifiers = (GetKeyState(0x11) < 0 ? 2u : 0) | (GetKeyState(0x12) < 0 ? 1u : 0) | (GetKeyState(0x10) < 0 ? 4u : 0);
        if (GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0) modifiers |= 8;
        ViewModel.SetHotkey(action, new HotkeyBinding(modifiers, (uint)args.Key));
    }
}
