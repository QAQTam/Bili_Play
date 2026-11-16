using BiliPlay.Core;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyLibraryToolsAsync()
    {
        using var model = new PlayerViewModel(new BilibiliClient(new PagedTrackFixtureHandler()));
        model.Client.RestoreCachedAccount(new(123, "合成功能验证", "", IsVip: true)); model.IsLoggedIn = true;
        var first = new Track(1, "BV0000000001", "第一首", "甲", "", 60, 1);
        var second = first with { Aid = 2, Bvid = "BV0000000002", Title = "月光", Uploader = "乙" };
        model.Queue.Add(first); model.Queue.Add(second); model.Navigate("playlist");
        model.ListSearchText = "月光"; await model.ListSearchCompletion;
        Check(model.PlaylistTracks.Count == 1 && model.Queue.Count == 2, "播放列表过滤改写了实际队列");
        model.Queue.Remove(second);
        Check(model.PlaylistTracks.Count == 0 && model.IsEmpty, "播放列表变更未同步过滤结果");
        model.ListSearchText = ""; await model.ListSearchCompletion;
        Check(model.PlaylistTracks.Single() == first, "清空播放列表搜索未恢复队列");

        await model.OpenFolderCommand.ExecuteAsync(new Folder(71, "合成收藏夹", 2, true));
        var original = model.Tracks.ToArray(); var more = model.HasMore;
        var pages = 0;
        model.ListPageReader = (page, token) =>
        {
            Interlocked.Increment(ref pages);
            return Task.FromResult(new PageResult<Track>(page == 1 ? [first] : [second], true));
        };
        model.ListSearchText = "月光"; await model.ListSearchCompletion;
        Check(pages == 3 && model.Tracks.Single() == second && !model.HasMore, "收藏夹搜索遗漏后续分页或重复页未终止");
        model.ListSearchText = ""; await model.ListSearchCompletion;
        Check(model.Tracks.SequenceEqual(original) && model.HasMore == more, "清空收藏夹搜索未恢复分页状态");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.ListPageReader = async (_, token) => { started.TrySetResult(); await Task.Delay(10000, token); return new PageResult<Track>([second], false); };
        model.ListSearchText = "旧请求"; var old = model.ListSearchCompletion; await started.Task;
        model.ListPageReader = (_, _) => Task.FromResult(new PageResult<Track>([first], false));
        model.ListSearchText = "第一"; await model.ListSearchCompletion; await old;
        Check(model.Tracks.Single() == first, "旧的搜索覆盖了较新的搜索");
        model.ListSearchText = "切换前"; var pending = model.ListSearchCompletion; model.Navigate("settings"); await pending;
        Check(model.IsSettingsView && model.ListSearchText.Length == 0 && !model.IsListSearching, "导航没有取消列表搜索");

        await model.OpenFolderCommand.ExecuteAsync(new Folder(71, "合成收藏夹", 2, true));
        model.FolderTracksGridMode = true; model.IsFolderSelectionMode = true; model.SelectedFolderTrackCount = 2;
        Check(model.CanDownloadFolderSelection && !model.CanClickTrack && model.IsHomeGridView && !model.IsTrackListVisible, "收藏夹选择没有保留图标视图");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BatchDownloadRunner = job => release.Task.WaitAsync(job.Cancellation.Token);
        await model.QueueFolderDownloadsAsync([first, first, second], true);
        Check(model.Downloads.Count == 2 && model.Downloads.All(job => job.PreferredHiRes == true), "批量任务未去重或未保留音质选择");
        model.Navigate("cache");
        Check(model.CacheTabIndex == 1 && model.DownloadingTabLabel == "正在下载 (2)", "进入下载页未优先显示进行中的任务或数量不正确");
        await model.OpenFolderCommand.ExecuteAsync(new Folder(71, "合成收藏夹", 2, true));
        await model.QueueFolderDownloadsAsync([first, second], true);
        Check(model.Downloads.Count == 2, "重复提交新增了重复下载任务");
        var jobs = model.Downloads.ToArray();
        await model.CancelDownloadAsync(jobs[0]);
        Check(model.Downloads.Count == 1, "取消批量任务影响了其他任务");
        release.TrySetResult(); await jobs[1].Completion;
        Check(model.Downloads.Count == 0 && model.DownloadingTabLabel == "正在下载", "批量任务完成后未清理任务状态或仍显示零数量");
        var removed = new List<long>();
        model.FolderSelectionRemover = (aid, folder, _) => { Check(folder.Id == 71 && folder.Owned, "解除收藏目标不正确"); removed.Add(aid); return Task.CompletedTask; };
        model.IsFolderSelectionMode = true; model.SelectedFolderTrackCount = 3;
        await model.RemoveFolderSelectionAsync([first, first, second]);
        Check(removed.SequenceEqual(new long[] { 1, 2 }) && !model.IsRemovingFolderSelection && !model.IsFolderSelectionMode, "解除收藏没有去重或完成后未退出选择");
        removed.Clear(); model.IsFolderSelectionMode = true; model.SelectedFolderTrackCount = 2;
        model.FolderSelectionRemover = (aid, _, _) => { removed.Add(aid); throw new HttpRequestException("synthetic uncertain response"); };
        await model.RemoveFolderSelectionAsync([first, second]);
        Check(removed.SequenceEqual(new long[] { 1 }), "解除收藏失败后仍继续写入或自动重试");
        await model.OpenFolderCommand.ExecuteAsync(new Folder(72, "合成订阅收藏夹", 2, false));
        model.IsFolderSelectionMode = true; model.SelectedFolderTrackCount = 2;
        Check(!model.CanRemoveFolderSelection && model.CanDownloadFolderSelection, "订阅收藏夹开放了解除收藏或禁止了下载");
        model.ConfigureGlobalHotkeys = (_, bindings) => bindings.Any(binding => binding.Key == 0x78) ? "合成快捷键冲突" : null;
        model.SetHotkey(0, new(0, 0x77));
        Check(model.PreviousHotkeyLabel == "F8" && model.HotkeyStatus.Length == 0, "单个功能键不能保存或仍显示普通状态文案");
        model.SetHotkey(1, new(0, 0x78));
        Check(model.NextHotkeyLabel != "F9" && model.HotkeyStatus == "合成快捷键冲突", "冲突的快捷键覆盖了原设置或没有提示");
        model.SetHotkey(1, new(4, 0x77));
        Check(model.NextHotkeyLabel == "Shift + F8" && model.HotkeyStatus.Length == 0, "仅带 Shift 的快捷键不能保存");
        ViewModel.Navigate("playlist"); UpdateLayout();
        Check(ListSearchBox.XamlRoot is null, "列表搜索没有默认折叠");
        ListSearchFlyout.ShowAt(ListSearchButton); await Task.Delay(80); UpdateLayout();
        var sizes = new[] { App.Window.AppWindow.Size, new Windows.Graphics.SizeInt32((int)(840 * XamlRoot.RasterizationScale), (int)(820 * XamlRoot.RasterizationScale)) };
        try
        {
            foreach (var size in sizes)
            {
                ListSearchFlyout.Hide(); await Task.Delay(200); App.Window.AppWindow.Resize(size); await Task.Delay(200);
                UpdateLayout(); ListSearchFlyout.ShowAt(ListSearchButton); await Task.Delay(200); UpdateLayout();
                var presenter = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(popup => FindVisual<FlyoutPresenter>(popup.Child)).FirstOrDefault(value => value is not null);
                Check(presenter is not null && ListSearchBox.ActualWidth > 100 && ListSearchBox.ActualWidth <= presenter.ActualWidth - presenter.Padding.Left - presenter.Padding.Right + 1, $"搜索框超出弹层可用宽度：input={ListSearchBox.ActualWidth}, presenter={presenter?.ActualWidth}, padding={presenter?.Padding}");
                var scroll = FindVisual<ScrollViewer>(presenter!);
                Check(scroll is null || scroll.ScrollableWidth < 1, "搜索框仍需水平滚动");
            }
        }
        finally { ListSearchFlyout.Hide(); App.Window.AppWindow.Resize(sizes[0]); }
        await Task.Delay(250); ListSearchFlyout.ShowAt(ListSearchButton); await Task.Delay(200); UpdateLayout();
        var originalSearchHeight = ListSearchContent.ActualHeight;
        ListSearchBox.Text = "合成搜索"; await ViewModel.ListSearchCompletion;
        Check(ViewModel.ListSearchText == "合成搜索" && ListSearchBox.QueryIcon is not null, "搜索框输入没有应用过滤或缺少搜索图标");
        Check(Math.Abs(ListSearchContent.ActualHeight - originalSearchHeight) < 1, "输入搜索内容后弹层高度发生跳动");
        Check(ViewModel.EmptyText == "没有找到匹配的内容。", "无匹配搜索文案不正确");
        var clear = FindNamedButton(ListSearchBox, "DeleteButton");
        Check(clear is not null && clear.Visibility == Visibility.Visible, "搜索框缺少内置清空按钮");
        ((IInvokeProvider)new ButtonAutomationPeer(clear!).GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(60); await ViewModel.ListSearchCompletion;
        Check(ViewModel.ListSearchText.Length == 0, "搜索框内 × 没有清空过滤");
        await SaveVisualAsync(Path.Combine(App.VisualCheckDirectory!, "search-flyout.png"));
        ListSearchFlyout.Hide(); ViewModel.Navigate("settings"); UpdateLayout();
        Check(HotkeySettings.Children.OfType<TextBox>().Count() == 0, "快捷键输入框仍常驻设置页");
        EditHotkeysButton.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Task.Delay(250); UpdateLayout();
        var editing = ShowHotkeyEditorAsync(); await Task.Delay(200); UpdateLayout();
        var dialog = _hotkeyDialog;
        Check(dialog?.Content is StackPanel editor && editor.Children.OfType<TextBox>().Count() == 3 && editor.ActualWidth > 260, "编辑快捷键没有弹出三个完整操作");
        dialog!.Hide(); await editing; ViewModel.Navigate("home");
        ViewModel.Navigate("folder"); ViewModel.SelectedFolder = new(71, "合成多选收藏夹", 2, true); ViewModel.PageTitle = "合成多选收藏夹";
        ViewModel.FolderTracksGridMode = true; ViewModel.Tracks.Clear(); ViewModel.Tracks.Add(first); ViewModel.Tracks.Add(second); ViewModel.IsEmpty = false;
        ViewModel.IsFolderSelectionMode = true;
        Check(!ViewModel.CanDownloadFolderSelection && !ViewModel.CanRemoveFolderSelection && ViewModel.FolderSelectionActionOpacity < 1, "未选内容时操作没有灰显");
        await Task.Delay(120); UpdateLayout();
        Check(RecommendationGrid.Visibility == Visibility.Visible && RecommendationGrid.SelectionMode == ListViewSelectionMode.Multiple, "图标多选没有保留原视图");
        RecommendationGrid.SelectedItems.Add(first);
        Check(ViewModel.SelectedFolderTrackCount == 1 && SelectedFolderTracks().Single() == first, "图标勾选没有同步下载与删除操作");
        var tile = (GridViewItem)RecommendationGrid.ContainerFromIndex(0);
        Check(tile.Margin.Right >= 20 && tile.Margin.Bottom >= 24, "首页与收藏夹封面卡片间距不足");
        Check(tile.ContentTemplateRoot is Grid frame && HoverSurface.GetEnabled(frame)
            && Math.Abs(frame.ActualWidth - tile.ActualWidth) < 1 && Math.Abs(frame.ActualHeight - tile.ActualHeight) < 1,
            "图标悬浮边界与卡片尺寸不一致");
        var check = FindVisual<CheckBox>(tile)!;
        var point = check.TransformToVisual(tile).TransformPoint(new(0,0));
        Check(point.X >= 7 && point.Y >= 7, "选择框没有向卡片内部留出边距");
        DownloadFolderSelectionClick(EditHotkeysButton, new RoutedEventArgs()); await Task.Delay(120);
        var downloading = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(popup => FindVisual<ContentDialog>(popup.Child)).FirstOrDefault(item => item?.Title as string == "批量下载 (1)");
        Check(downloading?.PrimaryButtonText == "开始下载" && downloading.Content is StackPanel options
            && options.Children.Count == 2 && options.Children[0] is CheckBox && options.Children[1] is TextBlock, "批量下载对话框没有精简为音质与 MP3 提示");
        downloading!.Hide(); await Task.Delay(80);
        await SaveVisualAsync(Path.Combine(App.VisualCheckDirectory!, "selection-grid.png"));
        ViewModel.IsFolderSelectionMode = false; ViewModel.FolderTracksGridMode = false; ViewModel.IsFolderSelectionMode = true;
        await Task.Delay(120); UpdateLayout();
        var row = TrackList.ContainerFromIndex(0) as ListViewItem;
        Check(row?.ContentTemplateRoot is Grid surface && HoverSurface.GetEnabled(surface) && surface.Children.OfType<FrameworkElement>().First().Margin.Left >= 40, "列表复选框仍与封面重叠");
        await SaveVisualAsync(Path.Combine(App.VisualCheckDirectory!, "selection-list.png"));
        ViewModel.IsFolderSelectionMode = false; ViewModel.Navigate("home");
        model.PreferHiRes = false; Check(!model.Player.PreferHiRes, "音质设置未传递到播放器");
        model.SelectPlaybackQuality("Hi-Res 无损"); Check(model.Player.PreferHiRes && model.Store.GetSetting("preferHiRes", "") == "true", "Hi-Res 偏好未持久化");
        model.AccountNotifications.Add(new("私信", "合成旧通知", "", ""));
        Check(model.VisibleNotifications.Count() == 1, "通知过滤未读取新增数据");
        model.AccountNotifications.Clear();
        Check(!model.VisibleNotifications.Any(), "清空通知后缓存仍暴露旧内容");
        model.AccountNotifications.Add(new("系统通知", "合成系统消息", "", ""));
        model.NotificationCategoryIndex = 3;
        Check(model.VisibleNotifications.Single().Title == "合成系统消息", "通知分类切换未刷新缓存");
    }
}
