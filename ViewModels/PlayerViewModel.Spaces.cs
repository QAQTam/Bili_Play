using BiliPlay.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliPlay.ViewModels;

public sealed partial class PlayerViewModel
{
    [ObservableProperty] public partial bool IsSpaceView { get; set; }
    [ObservableProperty] public partial bool IsSpaceLoading { get; set; }
    private long _spaceUserId;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpaceName), nameof(SpaceAvatar), nameof(SpaceDetails), nameof(SpaceSignature), nameof(CanFollowSpace), nameof(CanToggleSpaceFollow))]
    public partial UserSpace? SpaceProfile { get; set; }
    public string SpaceName => SpaceProfile?.Name ?? "正在读取个人空间…";
    public string SpaceAvatar => SpaceProfile?.Avatar ?? "";
    public string SpaceDetails => SpaceProfile is { } user ? $"{user.LevelText}  ·  {user.VipText}  ·  {user.UidText}" : "";
    public string SpaceSignature => SpaceProfile?.Signature ?? "";
    public bool CanFollowSpace => IsSpaceView && SpaceProfile is { } profile && profile.Id != Client.Account?.Id;
    public bool CanToggleSpaceFollow => CanFollowSpace && !IsSpaceFollowBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SpaceFollowLabel))]
    public partial bool IsSpaceFollowing { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SpaceFollowLabel), nameof(CanToggleSpaceFollow))]
    public partial bool IsSpaceFollowBusy { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SpaceFollowLabel))]
    public partial bool SpaceRelationReady { get; set; }
    public string SpaceFollowLabel => IsSpaceFollowBusy ? "正在处理…" : !SpaceRelationReady ? "重试关注状态" : IsSpaceFollowing ? "取消关注" : "关注";
    internal Func<long, CancellationToken, Task<bool>>? SpaceRelationReader { get; set; }
    private CancellationTokenSource? _spaceRelationLoad;
    private int _spaceRelationVersion;
    partial void OnSpaceProfileChanged(UserSpace? value)
    {
        var previous = _spaceRelationLoad; _spaceRelationLoad = null; previous?.Cancel(); _spaceRelationVersion++;
        IsSpaceFollowing = false; SpaceRelationReady = false; IsSpaceFollowBusy = false;
        if (CanFollowSpace) _ = RefreshSpaceRelationAsync();
    }
    private async Task RefreshSpaceRelationAsync()
    {
        if (!CanFollowSpace || SpaceProfile is not { } profile || Client.Account is not { } account) return;
        var version = ++_spaceRelationVersion;
        using var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        load.CancelAfter(TimeSpan.FromSeconds(10)); _spaceRelationLoad = load; IsSpaceFollowBusy = true;
        try
        {
            var followed = SpaceRelationReader is { } read ? await read(profile.Id, load.Token)
                : App.IsVisualCheck ? false : await Client.IsFollowingAsync(profile.Id, load.Token);
            if (!_disposed && version == _spaceRelationVersion && SpaceProfile?.Id == profile.Id && Client.Account?.Id == account.Id)
            { IsSpaceFollowing = followed; SpaceRelationReady = true; }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Leave a retry action rather than guessing the relation. */ }
        finally { if (version == _spaceRelationVersion) { IsSpaceFollowBusy = false; _spaceRelationLoad = null; } }
    }
    [RelayCommand]
    private Task ToggleSpaceFollowAsync() => SafeAsync(async () =>
    {
        if (!CanToggleSpaceFollow || SpaceProfile is not { } profile || Client.Account is not { } account) return;
        if (!SpaceRelationReady) { await RefreshSpaceRelationAsync(); return; }
        var version = _spaceRelationVersion; var follow = !IsSpaceFollowing;
        IsSpaceFollowBusy = true;
        try
        {
            await Client.FollowAsync(profile.Id, follow, _lifetime.Token);
            if (!_disposed && Client.Account?.Id == account.Id)
            {
                if (_currentUploader == profile.Id) IsFollowingUploader = follow;
                if (version == _spaceRelationVersion && SpaceProfile?.Id == profile.Id) IsSpaceFollowing = follow;
            }
        }
        catch { if (version == _spaceRelationVersion) SpaceRelationReady = false; throw; }
        finally { if (version == _spaceRelationVersion) IsSpaceFollowBusy = false; }
    }, false);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeGridView), nameof(IsTrackListVisible), nameof(BrowseGridMode), nameof(BrowseListMode))]
    public partial bool SpaceGridMode { get; set; }
    partial void OnSpaceGridModeChanged(bool value) => Store.SetSetting("spaceGrid", value ? "true" : "false");

    private void ClearAccountPages()
    {
        _browse?.Cancel(); _browseVersion++;
        DisposeNotifications(); IsNotificationsLoading = false; NotificationsStatus = "";
        AccountNotifications.Clear();
        OnPropertyChanged(nameof(VisibleNotifications)); OnPropertyChanged(nameof(NotificationsEmpty));
        SpaceProfile = null; _spaceUserId = 0; IsSpaceLoading = false;
    }

    public Task OpenSpaceAsync(long user) => SafeAsync(async () =>
    {
        if (user <= 0 || Client.Account is null) return;
        if (IsSpaceView && SpaceProfile?.Id != user)
            _backStack.Push(new(_view, PageTitle, SelectedFolder, Tracks.ToArray(), Uploaders.ToArray(), _query, _page, HasMore, SearchCategoryIndex, EmptyText, SpaceProfile, _spaceUserId));
        IsLyricsOpen = false; ShowComments = false; ShowQueue = false;
        Navigate("space"); _spaceUserId = user; SpaceProfile = null; Tracks.Clear(); _page = 1;
        _browse = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_browse.Token);
        request.CancelAfter(TimeSpan.FromSeconds(25));
        var version = _browseVersion; var token = request.Token;
        PageTitle = "个人空间"; EmptyText = "正在读取作品…";
        IsSpaceLoading = true;
        try
        {
            var profile = await Client.GetUserSpaceAsync(user, token);
            if (version != _browseVersion || _disposed) return;
            SpaceProfile = profile;
            var works = await Client.GetUserWorksAsync(user, 1, token);
            if (version != _browseVersion || _disposed) return;
            Replace(Tracks, works.Items); HasMore = works.HasMore;
            IsEmpty = Tracks.Count == 0; EmptyText = "暂无可查看的公开作品。";
        }
        catch { if (version == _browseVersion) { IsEmpty = Tracks.Count == 0; EmptyText = "个人空间暂时无法加载，请点击刷新重试。"; } throw; }
        finally { if (version == _browseVersion) IsSpaceLoading = false; }
    });
}
