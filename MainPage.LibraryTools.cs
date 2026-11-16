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
    private void ListSearchOpened(object? sender, object args)
    {
        ListSearchBox.Focus(FocusState.Programmatic);
    }
    private ContentDialog? _hotkeyDialog;
    private async void EditHotkeysClick(object sender, RoutedEventArgs args) => await ShowHotkeyEditorAsync();
    private async Task ShowHotkeyEditorAsync()
    {
        if (_hotkeyDialog is not null) return;
        var panel = new StackPanel { Spacing = 12, Width = Math.Min(320, XamlRoot.Size.Width - 96) };
        var headers = new[] { "上一首", "下一首", "播放 / 暂停" };
        var boxes = headers.Select((header, action) => new TextBox { Header = header, Tag = action, IsReadOnly = true, MinWidth = 0 }).ToArray();
        foreach (var box in boxes)
        {
            box.KeyDown += HotkeyCaptureKeyDown; box.GotFocus += HotkeyCaptureGotFocus; box.LostFocus += HotkeyCaptureLostFocus;
            panel.Children.Add(box);
        }
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(status);
        void Update()
        {
            boxes[0].Text = ViewModel.PreviousHotkeyLabel; boxes[1].Text = ViewModel.NextHotkeyLabel; boxes[2].Text = ViewModel.ToggleHotkeyLabel;
            status.Text = ViewModel.HotkeyStatus; status.Visibility = ViewModel.HasHotkeyError ? Visibility.Visible : Visibility.Collapsed;
        }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Update();
        var dialog = _hotkeyDialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "编辑快捷键", Content = panel, CloseButtonText = "完成" };
        ViewModel.PropertyChanged += Changed;
        try { Update(); await dialog.ShowAsync(); }
        finally { ViewModel.PropertyChanged -= Changed; dialog.Content = null; _hotkeyDialog = null; ViewModel.EndHotkeyCapture(); }
    }
    private async void RemoveFolderSelectionClick(object sender, RoutedEventArgs args)
    {
        var tracks = SelectedFolderTracks();
        var folder = ViewModel.SelectedFolder;
        if (!ViewModel.CanRemoveFolderSelection || tracks.Length == 0 || folder is null || _batchDownloadDialogOpen) return;
        var account = ViewModel.Client.Account?.Id;
        _batchDownloadDialogOpen = true;
        try
        {
            if (await Dialog("解除收藏", $"从“{folder.Title}”解除收藏所选 {tracks.Select(track => track.Aid).Distinct().Count()} 个视频？", "解除收藏").ShowAsync() == ContentDialogResult.Primary
                && ViewModel.Client.Account?.Id == account && ViewModel.SelectedFolder?.Id == folder.Id)
                await ViewModel.RemoveFolderSelectionAsync(tracks);
        }
        finally { _batchDownloadDialogOpen = false; }
    }
    private Track[] SelectedFolderTracks() => (ViewModel.IsHomeGridView ? RecommendationGrid.SelectedItems : TrackList.SelectedItems).OfType<Track>().ToArray();
    private void SelectAllFolderTracksClick(object sender, RoutedEventArgs args)
    { if (ViewModel.IsHomeGridView) RecommendationGrid.SelectAll(); else TrackList.SelectAll(); }
    private async void DownloadFolderSelectionClick(object sender, RoutedEventArgs args)
    {
        var tracks = SelectedFolderTracks();
        var account = ViewModel.Client.Account?.Id; var folder = ViewModel.SelectedFolder?.Id;
        if (tracks.Length == 0 || _batchDownloadDialogOpen) return;
        _batchDownloadDialogOpen = true;
        try
        {
            var content = new StackPanel { Spacing = 12, MaxWidth = 420 };
            var high = new CheckBox { Content = "优先使用 Hi-Res 音源", IsChecked = ViewModel.IsVip && ViewModel.PreferHiRes, IsEnabled = ViewModel.IsVip };
            content.Children.Add(high);
            content.Children.Add(new TextBlock { Text = "保存为 MP3：标准音源 192 kbps，Hi-Res 音源 320 kbps。无 Hi-Res 或账号无权限时使用标准音源。", TextWrapping = TextWrapping.Wrap });
            var dialog = Dialog($"批量下载 ({tracks.Length})", content, "开始下载");
            dialog.DefaultButton = ContentDialogButton.Primary;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && ViewModel.Client.Account?.Id == account && ViewModel.SelectedFolder?.Id == folder)
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
