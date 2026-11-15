using BiliPlay.Core;
using BiliPlay.ViewModels;

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
        model.IsFolderSelectionMode = true; model.SelectedFolderTrackCount = 2;
        Check(model.CanDownloadFolderSelection && !model.CanClickTrack && !model.IsHomeGridView && model.IsTrackListVisible, "收藏夹多选状态未切换到可选择列表");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BatchDownloadRunner = job => release.Task.WaitAsync(job.Cancellation.Token);
        await model.QueueFolderDownloadsAsync([first, first, second], true);
        Check(model.Downloads.Count == 2 && model.Downloads.All(job => job.PreferredHiRes == true), "批量任务未去重或未保留音质选择");
        await model.QueueFolderDownloadsAsync([first, second], true);
        Check(model.Downloads.Count == 2, "重复提交新增了重复下载任务");
        var jobs = model.Downloads.ToArray();
        await model.CancelDownloadAsync(jobs[0]);
        Check(model.Downloads.Count == 1, "取消批量任务影响了其他任务");
        release.TrySetResult(); await jobs[1].Completion;
        Check(model.Downloads.Count == 0, "批量任务完成后未清理任务状态");
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
