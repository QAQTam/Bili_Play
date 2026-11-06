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

    public IEnumerable<AccountNotification> VisibleNotifications => NotificationCategoryIndex switch
    {
        1 => AccountNotifications.Where(item => item.IsLike),
        2 => AccountNotifications.Where(item => item.Category == "回复与@" && !item.IsLike),
        3 => AccountNotifications.Where(item => item.Category == "系统通知"),
        _ => AccountNotifications.Where(item => item.Category == "私信")
    };
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
