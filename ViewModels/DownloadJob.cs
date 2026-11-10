using BiliPlay.Core;
using BiliPlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public sealed partial class DownloadJob(Track track, long account, string directory, CancellationTokenSource cancellation, DownloadOptions? options = null) : ObservableObject
{
    public Track Track { get; } = track;
    public string Thumbnail => Track.Thumbnail;
    public void RefreshArtwork() => OnPropertyChanged(nameof(Thumbnail));
    public DownloadOptions? Options { get; } = options;
    public long Account { get; } = account;
    public string Directory { get; } = directory;
    public CancellationTokenSource Cancellation { get; } = cancellation;
    public Task Completion { get; set; } = Task.CompletedTask;
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "等待下载";
    [ObservableProperty] public partial bool IsCancelling { get; set; }
}
