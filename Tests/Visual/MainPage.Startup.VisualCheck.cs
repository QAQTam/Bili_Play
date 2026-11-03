using BiliPlay.Core;
using BiliPlay.Services;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyStartupResponsivenessAsync()
    {
        using var model=new PlayerViewModel(new BilibiliClient(new StartupMetadataHandler()));
        model.Client.RestoreCachedAccount(new(123,"启动性能隔离验证",""));model.IsLoggedIn=true;
        await model.EnterAccountAsync(false);
        var song=new Track(71,"BV7xx411c7mD","首页异步验证","UP","",240,71);
        var calls=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.HomeReader=async(_,token)=>
        {
            Interlocked.Increment(ref calls);Check(!App.DispatcherQueue.HasThreadAccess,"首页网络和解析仍在界面线程执行");
            started.TrySetResult();await release.Task.WaitAsync(token);return new PageResult<Track>([song],true);
        };
        var load=model.OpenHomeCommand.ExecuteAsync(null);await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var refresh=model.RefreshCommand.ExecuteAsync(null);await Task.Delay(30);
        Check(calls==1,"启动时重复启动了首页请求");
        var pulse=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        App.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High,()=>pulse.TrySetResult());await pulse.Task.WaitAsync(TimeSpan.FromSeconds(1));
        model.Navigate("cache");release.TrySetResult();await Task.WhenAll(load,refresh);
        Check(model.IsCacheView&&model.PageTitle=="下载"&&model.Tracks.Count==0&&!model.ShowPageProgress,"首页旧结果覆盖了用户切换后的下载页面");
        // A synchronous parsing workload also runs off-thread, and a large page yields between UI batches.
        model.Navigate("home");var parsed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.HomeReader=(_,_)=>
        {
            Interlocked.Increment(ref calls);Check(!App.DispatcherQueue.HasThreadAccess,"解析工作负载在 UI 线程执行");Thread.Sleep(160);parsed.TrySetResult();
            return Task.FromResult(new PageResult<Track>(Enumerable.Range(1,240).Select(index=>song with {Bvid=$"BV{index:0000000000}"}).ToArray(),true));
        };
        var navigated=false;
        void OnBatch(object? sender,System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        {
            if(model.Tracks.Count!=12||navigated) return;
            App.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High,()=>{navigated=true;model.Navigate("settings");});
        }
        model.Tracks.CollectionChanged+=OnBatch;
        var large=model.OpenHomeCommand.ExecuteAsync(null);
        Check(!large.IsCompleted,"同步解析阻塞了调用方，未将工作送到后台");
        await parsed.Task.WaitAsync(TimeSpan.FromSeconds(3));await large;model.Tracks.CollectionChanged-=OnBatch;
        Check(navigated&&model.IsSettingsView&&model.Tracks.Count==12,"首页没有分批让出界面线程或继续向其他页面写入推荐");
        // Verifying the same account must preserve navigation, the restored queue and the loaded feed.
        var resets=0;model.Tracks.CollectionChanged+=(_,args)=>{if(args.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;};
        model.Queue.Add(song);var current=model.CurrentTrack;await model.EnterAccountAsync();
        Check(model.IsSettingsView&&resets==0&&QueueSelection.Same(model.CurrentTrack,current),"后台验证账号后重新初始化了页面或播放列表");
        model.Navigate("home");var before=calls;await model.OpenHomeCommand.ExecuteAsync(null);
        Check(calls==before&&model.Tracks.Count==240,"返回已载入首页又发起了初始化请求");
    }
    private sealed class StartupMetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK) {Content=new StringContent("""{"code":0,"data":{"list":[],"has_more":false}}""",System.Text.Encoding.UTF8,"application/json")});
    }
    private async Task VerifyDownloadLabelsAndAboutAsync(string directory)
    {
        var saved=ViewModel.Store.GetSetting("downloads:123","[]");var file=System.IO.Path.Combine(directory,"download-menu.mp3");
        var track=new Track(71,"BV7xx411c7mD","下载状态验证","UP","",240,71);
        try
        {
            System.IO.File.WriteAllBytes(file,new byte[16]);ViewModel.Store.WriteTracks("downloads:123",[track with {LocalFile=file}]);
            ViewModel.Navigate("home");var menu=BuildTrackMenu(track);
            Check(menu.Items.OfType<MenuFlyoutItem>().Any(item=>item.Text=="已下载"&&!item.IsEnabled)&&!menu.Items.OfType<MenuFlyoutItem>().Any(item=>item.Text=="下载音乐"),"已下载音乐仍然显示可重复下载操作");
            ViewModel.Navigate("cache");menu=BuildTrackMenu(track);
            Check(ViewModel.PageTitle=="下载"&&Navigation.MenuItems.OfType<NavigationViewItem>().Single(item=>item.Tag as string=="cache").Content as string=="下载","下载页面和侧栏名称不一致");
            Check(menu.Items.OfType<MenuFlyoutItem>().Any(item=>item.Text=="已下载"&&!item.IsEnabled)&&menu.Items.OfType<MenuFlyoutItem>().Any(item=>item.Text=="删除音乐"&&item.IsEnabled),"下载页面缺少状态或删除入口");
            System.IO.File.Delete(file);ViewModel.Navigate("home");menu=BuildTrackMenu(track);
            Check(menu.Items.OfType<MenuFlyoutItem>().Any(item=>item.Text=="下载音乐"&&item.IsEnabled),"本地文件消失后仍显示已下载");
            ViewModel.Navigate("settings");await Task.Delay(650);UpdateLayout();
            Check(CloseBehaviorBox.Items.Count==3 && RecordHistorySwitch.OnContent as string=="开" && RecordHistorySwitch.OffContent as string=="关", "关闭行为或历史开关文案缺失");
            var priorBehavior=ViewModel.CloseBehaviorIndex; ViewModel.CloseBehaviorIndex=2;
            Check(ViewModel.CloseToTray && ViewModel.Store.GetSetting("closeBehavior","")=="2", "暂停并到托盘设置未保存"); ViewModel.CloseBehaviorIndex=priorBehavior;
            ViewModel.Navigate("search"); ViewModel.SearchGridMode=true; UpdateLayout();
            Check(ViewModel.CanChooseView && ViewModel.IsHomeGridView && !ViewModel.IsTrackListVisible,"搜索图标视图切换失败");
            ViewModel.SearchGridMode=false;Check(ViewModel.IsTrackListVisible&&!ViewModel.IsHomeGridView,"搜索列表视图切换失败");
            ViewModel.SearchCategoryIndex=1;ViewModel.SearchGridMode=true;UpdateLayout();
            Check(ViewModel.CanChooseView&&ViewModel.IsUploaderGridView&&!ViewModel.IsUploaderListView,"UP 主图标视图切换失败");
            ViewModel.SearchGridMode=false;Check(ViewModel.IsUploaderListView&&!ViewModel.IsUploaderGridView,"UP 主列表视图切换失败");ViewModel.SearchCategoryIndex=0;
            ViewModel.Navigate("settings");
            Check(SettingsOptions.Children.SequenceEqual(new UIElement[]{AppThemeBox,CloseBehaviorBox,RecordHistorySwitch,SingleClickBehaviorBox,DoubleClickBehaviorBox,DownloadFolderSetting}),"设置顺序不符合要求");
            Check(RecordHistorySwitch.Header as string=="记录历史"&&ViewModel.AppVersion==AboutVersion.Text&&Version.TryParse(AboutVersion.Text,out _),"关于信息版本或记录历史文案不正确");
            Check(AuthorGitHubLink.NavigateUri.AbsoluteUri=="https://github.com/soda-rx"&&ProjectGitHubLink.NavigateUri.AbsoluteUri=="https://github.com/soda-rx/Bili_Play","关于中的 GitHub 链接不正确");
            SettingsScroller.ChangeView(null,SettingsScroller.ScrollableHeight,null,true);await Task.Delay(80);UpdateLayout();
            await SaveVisualAsync(System.IO.Path.Combine(directory,"settings-about.png"));
            await VerifyAboutSidebarLayoutAsync(directory);
        }
        finally
        {
            if(System.IO.File.Exists(file)) System.IO.File.Delete(file);
            ViewModel.Store.SetSetting("downloads:123",saved);ViewModel.Navigate("home");
        }
    }
    private async Task VerifyAboutSidebarLayoutAsync(string directory)
    {
        var size=App.Window.AppWindow.Size;var scale=XamlRoot.RasterizationScale;
        var root=(FrameworkElement)App.Window.Content;var theme=root.RequestedTheme;
        try
        {
            root.RequestedTheme=ElementTheme.Dark;
            foreach(var width in new[]{1280,840})
            {
                App.Window.AppWindow.Resize(new((int)(width*scale),(int)(820*scale)));
                for(var attempt=0;attempt<30;attempt++) {await Task.Delay(25);if(Math.Abs(ActualWidth-width)<24) break;}
                SettingsScroller.ChangeView(null,0,null,true);await Task.Delay(650);UpdateLayout();
                var card=AboutCard.TransformToVisual(SettingsLayout).TransformPoint(new(0,0));
                var options=SettingsOptions.TransformToVisual(SettingsLayout).TransformPoint(new(0,0));
                if(width==1280)
                {
                    Check(Grid.GetColumn(AboutCard)==1&&Grid.GetRow(AboutCard)==0&&card.X>=options.X+SettingsOptions.ActualWidth+24,"关于没有显示在设置内容右侧");
                    Check(Math.Abs(AboutCard.ActualWidth-280)<1&&Math.Abs(card.Y-options.Y)<1,"右侧关于卡片宽度或顶部对齐不正确");
                }
                else
                {
                    Check(Grid.GetColumn(AboutCard)==0&&Grid.GetRow(AboutCard)==1,"窄窗口没有将关于卡片放在设置项下方");
                    Check(card.Y>=options.Y+SettingsOptions.ActualHeight+20&&AboutCard.ActualWidth<=SettingsLayout.ActualWidth+1,"窄窗口关于卡片覆盖设置项或超出边界");
                    SettingsScroller.ChangeView(null,SettingsScroller.ScrollableHeight,null,true);await Task.Delay(80);
                }
                var project=ProjectGitHubLink.TransformToVisual(AboutCard).TransformPoint(new(0,0));
                var author=AuthorGitHubLink.TransformToVisual(AboutCard).TransformPoint(new(0,0));
                Check(project.Y<author.Y,"项目没有排在作者上面");
                Check(ProjectGitHubLink.Content is StackPanel projectContent&&projectContent.Children.OfType<TextBlock>().Single().Text=="GitHub仓库","仓库链接文字没有更新");
                foreach(var link in new[]{ProjectGitHubLink,AuthorGitHubLink})
                    Check(link.Content is StackPanel content&&content.Children.OfType<FontIcon>().Single().Glyph=="\uE8A7","GitHub 链接缺少原生外部链接图标");
                Check(_browseMotionElements.Contains(AboutCard),"右侧关于卡片缺少入场动画");
                await SaveVisualAsync(System.IO.Path.Combine(directory,$"settings-about-{width}.png"));
            }
        }
        finally {root.RequestedTheme=theme;App.Window.AppWindow.Resize(size);await Task.Delay(100);}
    }

}
