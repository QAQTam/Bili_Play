using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyNotificationActionsAsync()
    {
        var original = ViewModel.AccountNotifications.ToArray();
        var account = ViewModel.Client.Account!;
        var category = ViewModel.NotificationCategoryIndex;
        try
        {
            ViewModel.Navigate("notifications"); ViewModel.NotificationCategoryIndex = 0; CloseConversation();
            UpdateLayout(); await Task.Delay(60);
            Check(ConversationPlaceholder.Text == "请选择一个聊天" && ConversationPlaceholder.Visibility == Visibility.Visible
                && ConversationHeader.Visibility == Visibility.Collapsed, "私信未选择聊天时没有居中提示");
            var center = ConversationPlaceholder.TransformToVisual(ConversationPane).TransformPoint(new(ConversationPlaceholder.ActualWidth / 2, ConversationPlaceholder.ActualHeight / 2));
            Check(Math.Abs(center.X - ConversationPane.ActualWidth / 2) < 2 && Math.Abs(center.Y - ConversationPane.ActualHeight / 2) < 2, "请选择一个聊天没有位于右侧窗格中间");
            foreach (var entry in new[] { (0, "私信", false), (1, "回复与@", true), (2, "回复与@", false), (3, "系统通知", false) })
            {
                var item = new AccountNotification(entry.Item2, "合成删除测试", "离线数据", "", Id: entry.Item1 switch { 0 => "session:42", 1 => "like:100", 2 => "reply:101", _ => "system:102" }, IsLike: entry.Item3, TalkerId: entry.Item1 == 0 ? 42 : 0);
                ViewModel.AccountNotifications.Clear(); ViewModel.AccountNotifications.Add(item);
                ViewModel.NotificationCategoryIndex = entry.Item1; UpdateLayout();
                Check(ConversationPane.Visibility == (entry.Item1 == 0 ? Visibility.Visible : Visibility.Collapsed), "非私信通知仍显示聊天布局");
                var action = BuildNotificationMenu(item, account.Id).Items.OfType<MenuFlyoutItem>().Single();
                Check(action.Text == "删除" && action.IsEnabled == (entry.Item1 != 3), "云端删除入口没有按接口支持情况显示");
                if (entry.Item1 == 3) continue;
                var acknowledgement = new TaskCompletionSource(); var deleteCalls = 0;
                ViewModel.NotificationDeleteWriter = (_, _) => { deleteCalls++; return acknowledgement.Task; };
                Check(!await ViewModel.DeleteNotificationAsync(item, account.Id + 1) && deleteCalls == 0, "旧账号菜单发出了云端写入");
                var deleting = ViewModel.DeleteNotificationAsync(item, account.Id);
                Check(ViewModel.VisibleNotifications.Contains(item) && deleteCalls == 1, "云端确认前提前隐藏了通知");
                acknowledgement.SetResult(); Check(await deleting && !ViewModel.VisibleNotifications.Any(), "云端确认后没有移除通知");
                ViewModel.AccountNotifications.Add(item); Check(ViewModel.VisibleNotifications.Contains(item), "仍然使用本地墓碑隐藏云端结果");
                ViewModel.NotificationDeleteWriter = (_, _) => throw new HttpRequestException("synthetic failure");
                Check(!await ViewModel.DeleteNotificationAsync(item, account.Id) && ViewModel.VisibleNotifications.Contains(item), "失败后仍移除了通知");
            }
            var chat = new AccountNotification("私信", "合成分页会话", "", "", TalkerId: 42);
            ViewModel.NotificationCategoryIndex = 0;
            var calls = 0;
            await ShowConversationAsync(chat, (cursor, _) =>
            {
                calls++;
                return Task.FromResult(cursor == 0
                    ? new ConversationPage(Enumerable.Range(101, 30).Select(index => new DirectMessage($"page:{index}", 42, $"离线历史消息 {index}", "", index, "", false)).ToArray(), true, 101)
                    : new ConversationPage(Enumerable.Range(71, 31).Select(index => new DirectMessage($"page:{index}", 42, $"离线历史消息 {index}", "", index, "", false)).ToArray(), false, 71));
            });
            await Task.Delay(100); UpdateLayout();
            var scroll = _conversationScroll!;
            Check(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 24, "私信首次加载没有显示最新消息");
            scroll.ChangeView(null, 0, null, true);
            for (var attempt = 0; attempt < 30 && (calls < 2 || _conversationBusy); attempt++) await Task.Delay(20);
            UpdateLayout();
            Check(calls == 2 && _conversationMessages.Count == 60 && _conversationMessages.First().Sequence == 71, "滑到顶部未自动加载历史、重复加载或没有去重");
            Check(scroll.VerticalOffset > 24 && CaptureConversationAnchor()?.Message.Sequence == 101, $"加载历史后阅读位置跳到了最早消息：offset={scroll.VerticalOffset}, first={CaptureConversationAnchor()?.Message.Sequence}");
            scroll.ChangeView(null, 0, null, true); await Task.Delay(80);
            Check(calls == 2, "没有更多消息时仍重复请求历史");            Check(ConversationComposer.Visibility == Visibility.Visible && ConversationStatusRow.Visibility == Visibility.Collapsed, "普通会话没有输入栏或仍残留空状态行");
            ConversationDraft.Text = "合成草稿"; var attempts = 0;
            ConversationWriter = (_, _, _) => { attempts++; throw new HttpRequestException("synthetic uncertain write"); };
            await SendConversationAsync();
            Check(attempts == 1 && ConversationDraft.Text == "合成草稿", "发送失败后自动重试或丢失草稿");
            ConversationWriter = null;

        }
        finally
        {
            ViewModel.NotificationDeleteWriter = null; ConversationWriter = null; CloseConversation();
            ViewModel.Client.RestoreCachedAccount(account);
            ViewModel.AccountNotifications.Clear(); foreach (var item in original) ViewModel.AccountNotifications.Add(item);
            ViewModel.NotificationCategoryIndex = category; ViewModel.Navigate("home");
        }
    }
}
