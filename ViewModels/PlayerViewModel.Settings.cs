using BiliPlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    private bool _loadingSettings = true;
    private int _imageSettingsVersion;
    internal Task ImageCompatibilityUpdate { get; private set; } = Task.CompletedTask;
    [ObservableProperty] public partial bool ImageCompatibilityMode { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeImageCompatibility))]
    public partial bool IsUpdatingImageCompatibility { get; set; }
    public bool CanChangeImageCompatibility => !IsUpdatingImageCompatibility;

    partial void OnImageCompatibilityModeChanged(bool value)
    {
        Store.SetSetting("imageCompatibilityMode", value ? "true" : "false");
        ArtworkCache.SetCompatibilityMode(value);
        if (_loadingSettings) return;
        ImageCompatibilityUpdate = ApplyImageCompatibilityAsync(value, ++_imageSettingsVersion);
    }
    private async Task ApplyImageCompatibilityAsync(bool enabled, int version)
    {
        IsUpdatingImageCompatibility = true;
        try
        {
            if (enabled) await ArtworkCache.ClearAsync();
            if (_disposed || version != _imageSettingsVersion) return;
            ReloadArtwork();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        { if (!_disposed && version == _imageSettingsVersion) Notify("图片缓存清理失败：" + error.Message); }
        finally { if (!_disposed && version == _imageSettingsVersion) IsUpdatingImageCompatibility = false; }
    }
    private void ReloadArtwork()
    {
        var restoring = _restoringQueue; _restoringQueue = true;
        try
        {
            // New record instances also refresh one-time bindings in recycled templates.
            for (var index = 0; index < Tracks.Count; index++) Tracks[index] = Tracks[index] with { };
            for (var index = 0; index < Queue.Count; index++) Queue[index] = Queue[index] with { };
            foreach (var folder in Folders) folder.RefreshImages();
            for (var index = 0; index < Uploaders.Count; index++) Uploaders[index] = Uploaders[index] with { };
            for (var index = 0; index < AccountNotifications.Count; index++) AccountNotifications[index] = AccountNotifications[index] with { };
            foreach (var download in Downloads) download.RefreshArtwork();
            foreach (var comment in Comments)
            {
                comment.Update(comment.Data with { });
                foreach (var reply in comment.Replies) reply.Update(reply.Data with { });
            }
        }
        finally { _restoringQueue = restoring; }
        RefreshCurrentArtwork(); RefreshAccountArtwork(); OnPropertyChanged(nameof(SpaceAvatar));
    }
}
