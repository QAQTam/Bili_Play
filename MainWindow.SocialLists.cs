using BiliPlay.Core;
using BiliPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace BiliPlay;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _socialCountsLoad;
    private void AccountFlyoutClosed(object sender, object args) => _socialCountsLoad?.Cancel();
    private async void AccountFlyoutOpening(object sender, object args) => await RefreshSocialCountsAsync();
    private async Task RefreshSocialCountsAsync(Func<CancellationToken, Task<(long Following, long Followers)>>? read = null)
    {
        _socialCountsLoad?.Cancel();
        if (ViewModel.Client.Account is not { } account) return;
        var saved = ViewModel.Store.GetSetting($"socialCounts:{account.Id}", "").Split(',');
        ViewModel.FollowingLabel = saved.Length == 2 ? "关注 " + saved[0] : "关注 …";
        ViewModel.FollowersLabel = saved.Length == 2 ? "粉丝 " + saved[1] : "粉丝 …";
        using var load = new CancellationTokenSource(TimeSpan.FromSeconds(8)); _socialCountsLoad = load;
        try
        {
            var counts = await (read is null ? ViewModel.Client.GetSocialCountsAsync(load.Token) : read(load.Token));
            if (load.IsCancellationRequested || ViewModel.Client.Account?.Id != account.Id) return;
            ViewModel.FollowingLabel = $"关注 {counts.Following:N0}"; ViewModel.FollowersLabel = $"粉丝 {counts.Followers:N0}";
            ViewModel.Store.SetSetting($"socialCounts:{account.Id}", $"{counts.Following},{counts.Followers}");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (saved.Length != 2) { ViewModel.FollowingLabel = "关注 —"; ViewModel.FollowersLabel = "粉丝 —"; }
            ViewModel.Notify(error.Message, NoticeLevel.Warn);
        }
        finally { if (ReferenceEquals(_socialCountsLoad, load)) _socialCountsLoad = null; }
    }
    private bool _socialDialogOpen;
    private Func<SocialUser,Task>? _unfollowSocialUser;
    private async void UnfollowSocialUserClick(object sender,RoutedEventArgs args)
    {
        if(sender is not Button { Tag:SocialUser user } button || _unfollowSocialUser is null) return;
        button.IsEnabled=false;
        try { await _unfollowSocialUser(user); }
        finally { button.IsEnabled=true; }
    }
    private async void SocialListClick(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string kind })
            await ShowSocialListAsync(kind == "followers");
    }
    private async Task ShowSocialListAsync(bool followers, Func<bool, int, CancellationToken, Task<PageResult<SocialUser>>>? readPage = null, Func<long,CancellationToken,Task>? unfollow = null)
    {
        if (_socialDialogOpen || ViewModel.Client.Account is not { } account) return;
        _socialDialogOpen = true; AccountFlyout.Hide(); CloseSearchHistory();
        using var lifetime = new CancellationTokenSource();
        var users = new IncrementalCollection<SocialUser>();
        var unfollowed = new HashSet<long>();
        var page = 1; var more = true; var busy = false; var changing = false;
        var title = followers ? "粉丝" : "关注";
        var content = new Grid { Width = Math.Min(460, Math.Max(240, WindowRoot.ActualWidth - 96)), Height = Math.Clamp(WindowRoot.ActualHeight - 240, 180, 480) };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var refresh = new Button { Content = "刷新", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        var list = new ListView
        {
            ItemsSource = users, ItemTemplate = (DataTemplate)WindowRoot.Resources[followers ? "SocialUserTemplate" : "FollowingUserTemplate"],
            SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true,
            IncrementalLoadingTrigger = IncrementalLoadingTrigger.Edge, IncrementalLoadingThreshold = 1
        };
        Grid.SetRow(list, 1); content.Children.Add(refresh); content.Children.Add(list);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16), IsHitTestVisible = false };
        Grid.SetRow(status, 1); content.Children.Add(status);
        var ring = new ProgressRing { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        Grid.SetRow(ring, 1); content.Children.Add(ring);
        var dialog = new ContentDialog { XamlRoot = WindowRoot.XamlRoot, RequestedTheme = WindowRoot.ActualTheme, Title = title, Content = content, CloseButtonText = "关闭" };
        dialog.Closing += (_, _) => lifetime.Cancel();
        void AccountChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (ViewModel.Client.Account?.Id != account.Id || !ViewModel.IsLoggedIn) { lifetime.Cancel(); dialog.Hide(); } }
        ViewModel.PropertyChanged += AccountChanged;
        _unfollowSocialUser = async user =>
        {
            if(followers || changing || busy || !users.Contains(user) || ViewModel.Client.Account?.Id != account.Id) return;
            changing=true; refresh.IsEnabled=false;
            using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            request.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                if(unfollow is null) await ViewModel.Client.FollowAsync(user.Id,false,request.Token);
                else await unfollow(user.Id,request.Token);
                if(lifetime.IsCancellationRequested || ViewModel.Client.Account?.Id != account.Id) return;
                unfollowed.Add(user.Id); users.Remove(user); ViewModel.ApplyUnfollowedUploader(user.Id);
                var counts=ViewModel.Store.GetSetting($"socialCounts:{account.Id}","").Split(',');
                if(counts.Length==2 && long.TryParse(counts[0],out var following))
                {
                    following=Math.Max(0,following-1); ViewModel.FollowingLabel=$"关注 {following:N0}";
                    ViewModel.Store.SetSetting($"socialCounts:{account.Id}",$"{following},{counts[1]}");
                }
                // Deleting a relation shifts server page offsets. Resume at page 1 and deduplicate.
                page=1; more=true; users.ResetLoading();
                status.Text=users.Count==0 ? "暂时没有关注的人" : "";
            }
            catch(Exception error)
            {
                if(ViewModel.Client.Account?.Id == account.Id)
                    ViewModel.Notify(error is OperationCanceledException or System.Net.Http.HttpRequestException
                        ? "取消关注结果尚未确认，请刷新列表核对。" : error.Message,NoticeLevel.Warn);
            }
            finally {changing=false;refresh.IsEnabled=!busy;users.NotifyHasMore();}
        };
        users.CanLoadMore = () => more && !changing && !lifetime.IsCancellationRequested;
        users.FetchPage = async token =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            request.CancelAfter(TimeSpan.FromSeconds(20)); busy = true; ring.IsActive = true; status.Text = ""; refresh.IsEnabled = false;
            try
            {
                var result = await (readPage is null ? ViewModel.Client.GetSocialUsersAsync(followers, page, request.Token) : readPage(followers, page, request.Token));
                request.Token.ThrowIfCancellationRequested();
                if (ViewModel.Client.Account?.Id != account.Id) return;
                foreach (var user in result.Items) if (!unfollowed.Contains(user.Id) && !users.Any(existing => existing.Id == user.Id)) users.Add(user);
                page++; more = result.HasMore;
                if (users.Count == 0) status.Text = followers ? "暂时没有粉丝" : "暂时没有关注的人";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                more = false; status.Text = users.Count == 0 ? "列表暂时无法加载，请点击刷新重试。" : "";
                ViewModel.Notify(Feedback.WithRequestHint(error is OperationCanceledException ? "列表加载超时，请稍后重试。" : error.Message));
            }
            finally { busy = false; ring.IsActive = false; refresh.IsEnabled = true; }
        };
        refresh.Click += async (_, _) =>
        {
            if (busy || changing) return;
            page = 1; more = true; unfollowed.Clear(); users.Clear(); users.ResetLoading(); await users.LoadPageAsync(lifetime.Token);
        };
        list.ItemClick += async (_, args) =>
        { if (args.ClickedItem is SocialUser user) await ViewModel.RunAsync(async () => { await Launcher.LaunchUriAsync(new Uri(user.Url)); }); };
        try
        {
            var shown = dialog.ShowAsync();
            await users.LoadPageAsync(lifetime.Token);
            await shown;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ViewModel.Notify(error.Message); }
        finally { lifetime.Cancel(); _unfollowSocialUser=null; ViewModel.PropertyChanged -= AccountChanged; _socialDialogOpen = false; }
    }
}
