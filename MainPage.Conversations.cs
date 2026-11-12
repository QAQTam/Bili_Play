using System.Collections.ObjectModel;
using System.ComponentModel;
using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BiliPlay;

public sealed partial class MainPage
{
    private bool _conversationOpen;
    private async void NotificationClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not AccountNotification { Category: "私信", TalkerId: > 0 } item) return;
        if (item.SessionType != 1) { ViewModel.Notify("暂不支持群聊会话。", NoticeLevel.Info); return; }
        await ShowConversationAsync(item);
    }

    private async Task ShowConversationAsync(AccountNotification item,
        Func<long, CancellationToken, Task<ConversationPage>>? read = null)
    {
        if (_conversationOpen || ViewModel.Client.Account is not { } account) return;
        _conversationOpen = true;
        using var lifetime = new CancellationTokenSource();
        var messages = new ObservableCollection<DirectMessage>();
        var list = new ListView { ItemsSource = messages, ItemTemplate = (DataTemplate)Resources["DirectMessageTemplate"], SelectionMode = ListViewSelectionMode.None };
        var content = new Grid { Width = Math.Max(240, Math.Min(580, ActualWidth - 96)), Height = Math.Clamp(ActualHeight - 220, 180, 580), RowSpacing = 12 };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var earlier = new Button { Content = "更早的消息", IsEnabled = false };
        var refresh = new Button { Content = "刷新" };
        var ring = new ProgressRing { Width = 20, Height = 20 };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220 };
        toolbar.Children.Add(earlier); toolbar.Children.Add(refresh); toolbar.Children.Add(ring);
        content.Children.Add(toolbar); Grid.SetRow(status, 1); content.Children.Add(status); Grid.SetRow(list, 2); content.Children.Add(list);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = item.Title, Content = content, CloseButtonText = "关闭" };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        var busy = false; var more = false; long cursor = 0;
        bool Current() => !lifetime.IsCancellationRequested && ViewModel.IsLoggedIn && ViewModel.Client.Account?.Id == account.Id;
        void Close() { lifetime.Cancel(); dialog.Hide(); }
        void AccountChanged(object? sender, PropertyChangedEventArgs args) { if (!Current()) Close(); }
        void PageUnloaded(object sender, RoutedEventArgs args) => Close();
        ViewModel.PropertyChanged += AccountChanged; Unloaded += PageUnloaded;
        dialog.Closing += (_, _) => lifetime.Cancel();
        async Task LoadAsync(bool reset)
        {
            if (busy || !Current()) return;
            busy = true; earlier.IsEnabled = refresh.IsEnabled = false; ring.IsActive = true; status.Text = "正在读取对话…";
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            request.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var page = await (read is null ? ViewModel.Client.GetConversationAsync(item.TalkerId, reset ? 0 : cursor, request.Token) : read(reset ? 0 : cursor, request.Token));
                if (!Current()) return;
                var anchor = reset ? null : messages.FirstOrDefault();
                var combined = (reset ? page.Items : messages.Concat(page.Items)).DistinctBy(message => message.Id).OrderBy(message => message.Sequence).ToArray();
                messages.Clear(); foreach (var message in combined) messages.Add(message with { SenderAvatar = message.IsOwn ? account.Avatar : item.AvatarOne });
                cursor = page.BeforeSequence; more = page.HasMore;
                status.Text = messages.Count == 0 ? "暂无可读取的对话内容。" : "";
                if ((anchor ?? messages.LastOrDefault()) is { } focus) list.ScrollIntoView(focus);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception) { if (Current()) status.Text = "对话读取失败，请刷新重试。"; }
            finally { busy = false; ring.IsActive = false; earlier.IsEnabled = Current() && more; refresh.IsEnabled = Current(); }
        }
        earlier.Click += async (_, _) => await LoadAsync(false);
        refresh.Click += async (_, _) => await LoadAsync(true);
        try { var shown = dialog.ShowAsync(); await LoadAsync(true); await shown; }
        finally { lifetime.Cancel(); ViewModel.PropertyChanged -= AccountChanged; Unloaded -= PageUnloaded; _conversationOpen = false; }
    }
}
