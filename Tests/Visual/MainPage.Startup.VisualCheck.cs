using BiliPlay.Core;
using BiliPlay.Services;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyRapidAudioSwitchingAsync()
    {
        // Exercise native decoder teardown on the real UI context, with synthetic audio only.
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8); writer.Write(16036); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(16000); writer.Write(new byte[16000]);
        }
        using var deck = new AudioPlayback(new RapidAudioHandler(bytes.ToArray()));
        var stream = new AudioStream(new("https://upos.bilivideo.com/ui-stress.wav"), "audio/wav", []);
        for (var index = 0; index < 20; index++)
        {
            deck.SetSource(stream); deck.Player.Play();
            ViewModel.Navigate(index % 2 == 0 ? "settings" : "home");
            var pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            App.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () => pulse.TrySetResult());
            await pulse.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await Task.Delay(12);
        }
        deck.Proxy.Clear(); deck.Player.Pause(); deck.Player.Source = null;
        Check(App.DispatcherQueue.HasThreadAccess, "连续切歌测试未在界面线程执行");
    }
    private sealed class RapidAudioHandler(byte[] wave) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(8, token).ConfigureAwait(false);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(wave) };
            response.Content.Headers.ContentType = new("audio/wav");
            return response;
        }
    }
    private async Task VerifyStartupResponsivenessAsync()
    {
        using (var restored = new PlayerViewModel(new BilibiliClient(new StartupMetadataHandler()), () =>
            [new("DedeUserID", "123", ".bilibili.com", "/", DateTime.UtcNow.AddDays(1))]))
        {
            var loginFlashed = restored.LoginVisibility == Visibility.Visible;
            restored.PropertyChanged += (_, args) =>
            { if (args.PropertyName == nameof(restored.LoginVisibility) && restored.LoginVisibility == Visibility.Visible) loginFlashed = true; };
            await restored.InitializeAsync();
            Check(restored.IsLoggedIn && !restored.IsInitializing && !loginFlashed && restored.QrImage is null,
                "恢复已保存登录时经过了二维码界面");
        }
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
            await BiliPlay.Services.ArtworkCache.ClearAsync();Check(ClearImageCacheButton.Content as string=="清除缓存","图片缓存入口缺失");
            Check(SettingsOptions.Children.SequenceEqual(new UIElement[]{GeneralSettings,HistorySettings,PlaybackSettings,StorageSettings,CompatibilitySettings}),"设置分类不完整");
            Check(HistorySettings.Children.OfType<TextBlock>().Single().Text == "隐私", "隐私分类名称未更新");
            Check(GeneralSettings.Children.Contains(AppThemeBox) && GeneralSettings.Children.Contains(CloseBehaviorBox)
                && HistorySettings.Children.Contains(RecordHistorySwitch) && HistorySettings.Children.Contains(RecordSearchHistorySwitch)
                && PlaybackSettings.Children.Contains(SingleClickBehaviorBox) && PlaybackSettings.Children.Contains(DoubleClickBehaviorBox)
                && StorageSettings.Children.Contains(DownloadFolderSetting) && StorageSettings.Children.Contains(ImageCacheSetting)
                && CompatibilitySettings.Children.Contains(ImageCompatibilityOption) && ImageCompatibilityOption.Children.Contains(ImageCompatibilitySwitch), "设置项没有归入正确分类");
            Check(ImageCompatibilityInfo.Glyph == "\uE946" && ToolTipService.GetToolTip(ImageCompatibilityInfo) is string tip && tip.Contains("JPEG") && tip.Contains("PNG"), "兼容模式没有信息图标和格式说明");
            for (DependencyObject? node = ImageCompatibilityInfo; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                Check(node is not UIElement element || element.IsHitTestVisible, "兼容模式说明位于不接收鼠标的原生标题区域，无法悬浮");
            var compatibility = ViewModel.ImageCompatibilityMode;
            var systemWebp = ArtworkFormats.WebpSupported;
            var imageDirectory = Path.Combine(directory, "data", "cache", "images");
            Directory.CreateDirectory(imageDirectory);
            var oldCache = Path.Combine(imageDirectory, new string('d',64) + ".img");
            var keep = Path.Combine(imageDirectory, "keep.txt");
            await File.WriteAllTextAsync(oldCache, "old artwork fixture");
            await File.WriteAllTextAsync(keep, "unrelated fixture");
            var before = ArtworkCache.Create(new Uri(Path.Combine(directory,"cover.png")),48);
            var fixture = new Track(71,"BV7xx411c7mD","图片刷新验证","合成用户",new Uri(Path.Combine(directory,"cover.png")).AbsoluteUri,30);
            ViewModel.Tracks.Add(fixture);
            ImageCompatibilitySwitch.IsOn = true;
            await ViewModel.ImageCompatibilityUpdate;
            Check(ViewModel.ImageCompatibilityMode && !ArtworkFormats.UseWebp && ViewModel.Store.GetSetting("imageCompatibilityMode", "") == "true", "兼容模式没有立即保存或仍在请求 WebP");
            Check(!File.Exists(oldCache) && File.Exists(keep), "兼容模式没有清理图片缓存或误删了其他文件");
            Check(!ReferenceEquals(before,ArtworkCache.Create(new Uri(Path.Combine(directory,"cover.png")),48))
                && !ReferenceEquals(fixture,ViewModel.Tracks.Last()) && !ViewModel.IsUpdatingImageCompatibility, "兼容模式未重建图片和模板绑定");
            using (var restored = new PlayerViewModel(new BilibiliClient(new StartupMetadataHandler())))
                Check(restored.ImageCompatibilityMode, "重新创建配置后丢失兼容模式设置");
            ImageCompatibilitySwitch.IsOn = false;
            await ViewModel.ImageCompatibilityUpdate;
            Check(ArtworkFormats.UseWebp == systemWebp, "关闭兼容模式后没有遵循实际解码能力");
            ImageCompatibilitySwitch.IsOn = compatibility;
            await ViewModel.ImageCompatibilityUpdate;
            File.Delete(keep); ViewModel.Tracks.Clear();
            Check(RecordHistorySwitch.Header as string=="记录历史"&&ViewModel.AppVersion==AboutVersion.Text&&Version.TryParse(AboutVersion.Text,out _),"关于信息版本或记录历史文案不正确");
            Check(AuthorGitHubLink.NavigateUri.AbsoluteUri=="https://github.com/soda-rx"&&ProjectGitHubLink.NavigateUri.AbsoluteUri=="https://github.com/soda-rx/Bili_Play","关于中的 GitHub 链接不正确");
            Check(ViewAgreementButton.Content as string=="查看用户协议与隐私政策"&&AgreementBody().Content is TextBlock agreement&&agreement.Text==BiliPlay.Services.UserAgreement.Text,"关于下面没有完整协议查看入口");
            var searchPreference=ViewModel.RecordSearchHistory;
            RecordSearchHistorySwitch.IsOn=false;
            Check(!ViewModel.RecordSearchHistory&&!ViewModel.Store.RecordSearchHistory&&ViewModel.SearchHistory.Count==0,"关闭搜索历史没有生效");
            RecordSearchHistorySwitch.IsOn=searchPreference;
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
                foreach (var category in SettingsOptions.Children.OfType<StackPanel>())
                {
                    var heading = category.Children.OfType<TextBlock>().First();
                    var left = heading.TransformToVisual(SettingsLayout).TransformPoint(new(0,0)).X;
                    Check(Math.Abs(left-options.X)<1, "设置分类标题未左对齐");
                    foreach (var element in category.Children.OfType<FrameworkElement>())
                        Check(Math.Abs(element.TransformToVisual(SettingsLayout).TransformPoint(new(0,0)).X-left)<1, "设置控件未与分类标题左对齐");
                }
                foreach (var control in new Control[]{AppThemeBox,CloseBehaviorBox,RecordHistorySwitch,RecordSearchHistorySwitch,SingleClickBehaviorBox,DoubleClickBehaviorBox,ClearImageCacheButton,ImageCompatibilitySwitch})
                    Check(control.HorizontalAlignment == HorizontalAlignment.Left, "设置控件仍然居中或拉伸");
                if(width==1280)
                {
                    Check(Grid.GetColumn(AboutPanel)==1&&Grid.GetRow(AboutPanel)==0&&card.X>=options.X+SettingsOptions.ActualWidth+24,"关于没有显示在设置内容右侧");
                    Check(Math.Abs(AboutCard.ActualWidth-280)<1&&Math.Abs(card.Y-options.Y)<1,"右侧关于卡片宽度或顶部对齐不正确");
                }
                else
                {
                    Check(Grid.GetColumn(AboutPanel)==0&&Grid.GetRow(AboutPanel)==1,"窄窗口没有将关于卡片放在设置项下方");
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
