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
    [NotifyPropertyChangedFor(nameof(VisibleNotifications), nameof(NotificationsEmpty))]
    public partial int NotificationCategoryIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsEmpty))]
    public partial bool IsNotificationsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsEmpty))]
    public partial string NotificationsStatus { get; set; } = "";

    public IEnumerable<AccountNotification> VisibleNotifications => NotificationCategoryIndex switch
    {
        1 => AccountNotifications.Where(item => item.Category == "私信"),
        2 => AccountNotifications.Where(item => item.Category == "收到的赞"),
        3 => AccountNotifications.Where(item => item.Category == "回复与@"),
        4 => AccountNotifications.Where(item => item.Category == "系统通知"),
        _ => AccountNotifications
    };
    public bool NotificationsEmpty => !IsNotificationsLoading && !VisibleNotifications.Any();

    private CancellationTokenSource? _notificationsLoad;
    private int _notificationsVersion;

    public async Task RefreshNotificationsAsync()
    {
        if (_disposed || !IsNotificationsView || Client.Account is null) return;
        var version = ++_notificationsVersion;
        _notificationsLoad?.Cancel();
        _notificationsLoad?.Dispose();
        var load = _notificationsLoad = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        load.CancelAfter(TimeSpan.FromSeconds(25));
        IsNotificationsLoading = true;
        NotificationsStatus = "正在读取通知…";
        try
        {
            var items = await Client.GetNotificationsAsync(load.Token);
            if (_disposed || version != _notificationsVersion || !IsNotificationsView) return;
            AccountNotifications.Clear();
            foreach (var item in items) AccountNotifications.Add(item);
            NotificationsStatus = AccountNotifications.Count == 0 ? "暂无新通知" : "";
            OnPropertyChanged(nameof(VisibleNotifications));
            OnPropertyChanged(nameof(NotificationsEmpty));
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_disposed && version == _notificationsVersion)
            {
                AccountNotifications.Clear();
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
