using BiliPlay.Core;
using BiliPlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackSelectionMode), nameof(CanClickTrack), nameof(IsHomeGridView), nameof(IsTrackListVisible), nameof(IsTrackSelectionMode), nameof(FolderSelectionLabel), nameof(CanDownloadFolderSelection))]
    public partial bool IsFolderSelectionMode { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadFolderSelection))]
    public partial int SelectedFolderTrackCount { get; set; }
    public bool IsTrackSelectionMode => IsCacheSelectionMode || IsFolderSelectionMode;
    public string FolderSelectionLabel => IsFolderSelectionMode ? "取消选择" : "批量下载";
    public bool CanDownloadFolderSelection => IsFolderView && IsFolderSelectionMode && SelectedFolderTrackCount > 0;
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
