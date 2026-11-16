using System.Collections.ObjectModel;
using BiliPlay.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public sealed partial class PlayerViewModel
{
    public ObservableCollection<AccountNotification> AccountNotifications { get; } = [];

    [ObservableProperty]
    public partial bool IsNotificationsView { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleNotifications), nameof(NotificationsEmpty), nameof(NotificationEmptyText), nameof(HasNotificationStatus))]
    public partial int NotificationCategoryIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsEmpty), nameof(HasNotificationStatus))]
    public partial bool IsNotificationsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsEmpty), nameof(NotificationEmptyText), nameof(HasNotificationStatus))]
    public partial string NotificationsStatus { get; set; } = "";
    public string NotificationEmptyText => NotificationsStatus.Length > 0 ? NotificationsStatus : NotificationCategoryIndex switch { 1 => "暂无点赞通知", 2 => "暂无回复与 @", 3 => "暂无系统通知", _ => "暂无私信" };
    public bool HasNotificationStatus => !IsNotificationsLoading && !NotificationsEmpty && NotificationsStatus.Length > 0;

    // Filtered rows are materialized once per data or category change (see Core
    // NotificationFeed) so rebinding reads a stable snapshot; the dirty flag is
    // set by category changes and after the feed is repopulated.
    private readonly NotificationFeed _notificationFeed = new();
    public IEnumerable<AccountNotification> VisibleNotifications => _notificationFeed.Visible(AccountNotifications, NotificationCategoryIndex);
    private readonly HashSet<(long Account, string Id)> _notificationDeletes = [];
    public async Task<bool> DeleteNotificationAsync(AccountNotification item, long account)
    {
        if (_disposed || account <= 0 || Client.Account?.Id != account || !AccountNotifications.Contains(item) || !BilibiliClient.CanDeleteNotification(item)) return false;
        if (BiliPlay.Services.DebugSession.IsActive) return false;
        var key = (account, item.Category == "私信" ? "session:" + item.TalkerId : item.Id);
        if (!_notificationDeletes.Add(key)) return false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _accountDownloads.Token);
        request.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
#if VISUAL_CHECKS
            if (NotificationDeleteWriter is { } writer) await writer(item, request.Token);
            else
#endif
                await Client.DeleteNotificationAsync(item, request.Token);
            if (_disposed || Client.Account?.Id != account) return false;
            // Only the acknowledged cloud write changes the displayed list. Do not persist local tombstones.
            _notificationsVersion++; DisposeNotifications(); IsNotificationsLoading = false; NotificationsStatus = "";
            foreach (var current in AccountNotifications.Where(row => row.Id == item.Id).ToArray()) AccountNotifications.Remove(current);
            _notificationFeed.Invalidate();
            OnPropertyChanged(nameof(VisibleNotifications)); OnPropertyChanged(nameof(NotificationsEmpty));
            return true;
        }
        catch (Exception)
        {
            if (!_disposed && Client.Account?.Id == account) Notify("云端删除未能确认，通知已保留。请刷新核对后再试。", NoticeLevel.Warn);
            return false;
        }
        finally { _notificationDeletes.Remove(key); }
    }
    partial void OnNotificationCategoryIndexChanged(int value) => _notificationFeed.Invalidate();
    public bool NotificationsEmpty => !IsNotificationsLoading && !VisibleNotifications.Any();

    private CancellationTokenSource? _notificationsLoad;
    private int _notificationsVersion;

    public async Task RefreshNotificationsAsync()
    {
        if (_disposed || !IsNotificationsView || Client.Account is null) return;
        if (App.IsVisualCheck || BiliPlay.Services.DebugSession.IsActive)
        { NotificationsStatus = ""; return; }
        var version = ++_notificationsVersion;
        _notificationsLoad?.Cancel();
        _notificationsLoad?.Dispose();
        using var load = _notificationsLoad = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        load.CancelAfter(TimeSpan.FromSeconds(25));
        IsNotificationsLoading = true;
        NotificationsStatus = "正在读取通知…";
        try
        {
            var items = await Client.GetNotificationsAsync(load.Token);
            if (_disposed || version != _notificationsVersion || !IsNotificationsView) return;
            _notificationFeed.Invalidate();
            AccountNotifications.Clear();
            foreach (var item in items) AccountNotifications.Add(item);
            _ = BiliPlay.Services.ArtworkCache.WarmAsync(items.SelectMany(item=>item.Avatars.Append(item.TargetImage)), _lifetime.Token);
            NotificationsStatus = Client.NotificationReadWarnings.Count > 0
                ? string.Join("、", Client.NotificationReadWarnings.Distinct()) + "暂时无法读取，点击刷新重试。"
                : "";
            OnPropertyChanged(nameof(VisibleNotifications));
            OnPropertyChanged(nameof(NotificationsEmpty));
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            if (!_disposed && IsNotificationsView && version == _notificationsVersion) NotificationsStatus = "通知读取超时，请刷新重试。";
        }
        catch (Exception error)
        {
            if (!_disposed && version == _notificationsVersion)
            {
                NotificationsStatus = Feedback.WithRequestHint(error.Message);
                OnPropertyChanged(nameof(VisibleNotifications));
                OnPropertyChanged(nameof(NotificationsEmpty));
            }
        }
        finally
        {
            if (ReferenceEquals(_notificationsLoad, load))
            {
                _notificationsLoad = null;
                IsNotificationsLoading = false;
            }
        }
    }

    private void DisposeNotifications()
    {
        _notificationsLoad?.Cancel();
        _notificationsLoad?.Dispose();
        _notificationsLoad = null;
    }
}
