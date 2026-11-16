using BiliPlay.Core;
using BiliPlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackSelectionMode), nameof(GridTrackSelectionMode), nameof(CanClickTrack), nameof(IsHomeGridView), nameof(IsTrackListVisible), nameof(IsTrackSelectionMode), nameof(FolderSelectionLabel), nameof(CanDownloadFolderSelection), nameof(FolderSelectionActionOpacity), nameof(CanManageFolderSelection), nameof(CanRemoveFolderSelection))]
    public partial bool IsFolderSelectionMode { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadFolderSelection), nameof(FolderSelectionActionOpacity), nameof(CanManageFolderSelection), nameof(CanRemoveFolderSelection))]
    public partial int SelectedFolderTrackCount { get; set; }
    private int _cachedCount;
    public string DownloadedTabLabel => _cachedCount > 0 ? $"已下载 ({_cachedCount})" : "已下载";
    public string DownloadingTabLabel => Downloads.Count > 0 ? $"正在下载 ({Downloads.Count})" : "正在下载";
    private void RefreshDownloadCounts()
    {
        if (_disposed) return;
        _cachedCount = Client.Account is { } account ? Store.ReadTracks($"downloads:{account.Id}").Count(track => DownloadService.IsCached(account.Id, track)) : 0;
        OnPropertyChanged(nameof(DownloadedTabLabel)); OnPropertyChanged(nameof(DownloadingTabLabel));
    }
    public Microsoft.UI.Xaml.Controls.ListViewSelectionMode GridTrackSelectionMode => IsFolderSelectionMode ? Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Multiple : Microsoft.UI.Xaml.Controls.ListViewSelectionMode.None;
    public bool IsTrackSelectionMode => IsCacheSelectionMode || IsFolderSelectionMode;
    public string FolderSelectionLabel => IsFolderSelectionMode ? "取消选择" : "选择";
    public double FolderSelectionActionOpacity => SelectedFolderTrackCount > 0 ? 1 : .35;
    public bool CanDownloadFolderSelection => IsFolderView && IsFolderSelectionMode && SelectedFolderTrackCount > 0;
    public bool CanManageFolderSelection => IsFolderView && IsFolderSelectionMode && SelectedFolder is { Owned: true };
    public bool CanRemoveFolderSelection => CanManageFolderSelection && SelectedFolderTrackCount > 0 && !IsRemovingFolderSelection;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveFolderSelection))]
    public partial bool IsRemovingFolderSelection { get; set; }

    public Task RemoveFolderSelectionAsync(IReadOnlyList<Track> tracks) => SafeAsync(async () =>
    {
        if (!CanRemoveFolderSelection || DebugSession.IsActive || Client.Account is not { } account || SelectedFolder is not { Owned: true } folder) return;
        var version = _browseVersion;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _accountDownloads.Token, _browse?.Token ?? CancellationToken.None);
        var confirmed = 0; var uncertain = false;
        IsRemovingFolderSelection = true;
        bool Current() => !_disposed && Client.Account?.Id == account.Id && _browseVersion == version && SelectedFolder?.Id == folder.Id;
        try
        {
            foreach (var track in tracks.Where(track => track.Aid > 0).DistinctBy(track => track.Aid))
            {
                if (!Current()) break;
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
                operation.CancelAfter(TimeSpan.FromSeconds(20));
#if VISUAL_CHECKS
                if (FolderSelectionRemover is { } remove) await remove(track.Aid, folder, operation.Token);
                else
#endif
                    await Client.SetFavoriteAsync(track.Aid, folder, false, operation.Token);
                confirmed++;
            }
        }
        catch (Exception) { uncertain = true; }
        finally
        {
            IsRemovingFolderSelection = false;
            if (Current())
            {
                // Fetch authoritative state after writes; pagination offsets and search snapshots are now stale.
                IsFolderSelectionMode = false;
                try { await BrowseFolderAsync(folder); }
                catch (Exception) { Notify("收藏夹刷新失败，请手动刷新查看最新状态。", NoticeLevel.Warn); }
                if (Client.Account?.Id == account.Id)
                    Notify(uncertain ? $"已确认解除收藏 {confirmed} 个视频；后续请求未能确认，已停止操作，请刷新核对后再试。" : $"已解除收藏 {confirmed} 个视频。", uncertain ? NoticeLevel.Warn : NoticeLevel.Info);
            }
        }
    }, false);

    partial void OnIsFolderSelectionModeChanged(bool value) => SelectedFolderTrackCount = 0;

    public Task QueueFolderDownloadsAsync(IReadOnlyList<Track> tracks, bool preferHiRes) => SafeAsync(async () =>
    {
        if (!IsFolderView || DebugSession.IsActive || Client.Account is not { } account) return;
        var existing = Store.ReadTracks($"downloads:{account.Id}").Where(track => DownloadService.IsCached(account.Id, track)).ToArray();
        var known = existing.Select(TrackKey).Concat(Downloads.Where(job => job.Account == account.Id).Select(job => TrackKey(job.Track))).ToHashSet();
        var accountToken = _accountDownloads.Token; var directory = CacheDirectory;
        var jobs = new List<DownloadJob>();
        foreach (var track in tracks)
        {
            if (_disposed || accountToken.IsCancellationRequested || Client.Account?.Id != account.Id) break;
            if (!known.Add(TrackKey(track))) continue;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, accountToken);
            var job = new DownloadJob(track, account.Id, directory, cancellation) { PreferredHiRes = preferHiRes };
            Downloads.Add(job); jobs.Add(job);
            job.Completion = ObserveBatchDownloadAsync(job);
            if (jobs.Count % 24 == 0) await Task.Yield();
        }
        if (_disposed || Client.Account?.Id != account.Id) return;
        IsFolderSelectionMode = false;
        Notify(jobs.Count > 0 ? $"已加入 {jobs.Count} 个下载任务，已下载和重复内容已跳过。" : "所选内容已下载或已在任务列表中。", NoticeLevel.Info);
    }, false);

    public Task DownloadAllFolderAsync(Folder folder, long account) => SafeAsync(async () =>
    {
        var version = _browseVersion;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _accountDownloads.Token, _browse?.Token ?? CancellationToken.None);
        request.CancelAfter(TimeSpan.FromMinutes(3));
        var tracks = await TrackSearch.FindAsync((page, token) => Client.GetFolderTracksAsync(folder.Id, page, token), "", request.Token);
        if (!_disposed && Client.Account?.Id == account && SelectedFolder?.Id == folder.Id && version == _browseVersion)
            await QueueFolderDownloadsAsync(tracks, PreferHiRes);
    }, false);

    private async Task ObserveBatchDownloadAsync(DownloadJob job)
    {
        try
        {
#if VISUAL_CHECKS
            if (BatchDownloadRunner is { } run)
            {
                try { await run(job); }
                finally { Downloads.Remove(job); job.Cancellation.Dispose(); }
                return;
            }
#endif
            await RunDownloadAsync(job);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed) Notify($"下载失败：{job.Track.Title} · {Friendly(error)}", NoticeLevel.Warn); }
    }
}
