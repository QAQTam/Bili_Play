using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BiliPlay;
public sealed partial class MainPage
{
    private void TrackContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    { if (!args.InRecycleQueue) { ApplyTrackSelection(args.ItemContainer, ReferenceEquals(sender, TrackList)); if (args.Phase == 0) args.RegisterUpdateCallback(TrackContainerChanging); } }
    private void UpdateTrackSelectionPresentation()
    {
        foreach (var list in new ListViewBase[] { TrackList, RecommendationGrid })
            if (list.ItemsPanelRoot is { } panel)
                foreach (var row in panel.Children.OfType<SelectorItem>()) ApplyTrackSelection(row, ReferenceEquals(list, TrackList));
    }
    private void ApplyTrackSelection(SelectorItem row, bool isList)
    {
        if (row.ContentTemplateRoot is not FrameworkElement content) return;
        var body = content is Grid surface && HoverSurface.GetEnabled(surface) ? surface.Children.OfType<FrameworkElement>().FirstOrDefault() : content;
        if (body is not null) body.Margin = isList && ViewModel.IsTrackSelectionMode ? new Thickness(40, 0, 0, 0) : new Thickness(0);
        void Visit(DependencyObject element)
        {
            if (element is ButtonBase button) button.IsHitTestVisible = !ViewModel.IsTrackSelectionMode;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) Visit(VisualTreeHelper.GetChild(element, index));
        }
        Visit(content);
    }
    private async Task DownloadEntireFolderAsync(Folder folder)
    {
        if (_batchDownloadDialogOpen || ViewModel.Client.Account is not { } account) return;
        _batchDownloadDialogOpen = true;
        try
        {
            if (await Dialog("下载全部", $"下载收藏夹“{folder.Title}”中的全部可播放内容？已下载和重复任务将跳过。", "下载全部").ShowAsync() != ContentDialogResult.Primary) return;
            if (ViewModel.Client.Account?.Id != account.Id) return;
            await ViewModel.OpenFolderCommand.ExecuteAsync(folder);
            if (ViewModel.Client.Account?.Id != account.Id || ViewModel.SelectedFolder?.Id != folder.Id) return;
            await ViewModel.DownloadAllFolderAsync(folder, account.Id);
        }
        finally { _batchDownloadDialogOpen = false; }
    }
}
