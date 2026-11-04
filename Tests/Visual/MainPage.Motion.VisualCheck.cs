using System.Collections.Specialized;
using System.Numerics;
using BiliPlay.Core;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace BiliPlay;

public sealed partial class MainPage
{
    private void VerifyFooterGeometry()
    {
        foreach(var group in new[]{FooterActions,TransportGroup})
        {
            double? right=null;double? top=null;
            foreach(var child in group.Children.OfType<FrameworkElement>())
            {
                var point=child.TransformToVisual(group).TransformPoint(new(0,0));
                Check(Math.Abs(child.ActualWidth-48)<.1&&Math.Abs(child.ActualHeight-48)<.1,"底栏按钮与点击区域不是统一的 48×48");
                if(right is { } edge) Check(Math.Abs(point.X-edge-8)<.2,"底栏按钮间距不相等");
                if(top is { } y) Check(Math.Abs(point.Y-y)<.2,"底栏按钮高度位置不一致");
                right=point.X+child.ActualWidth;top=point.Y;
            }
        }
    }
    private async Task VerifyPageEntrancesAsync()
    {
        var song=new Track(71,"BV7xx411c7mD","页面动效验证","UP","",240,71);
        var oldHome=ViewModel.HomeGridMode;var oldFolders=ViewModel.FolderGridMode;var oldFolderTracks=ViewModel.FolderTracksGridMode;
        ViewModel.Queue.Add(song);ViewModel.Folders.Add(new(71,"页面验证收藏夹",0,true));
        try
        {
            foreach(var view in new[]{"home","library","history","playlist","cache","search","folder","settings"})
            foreach(var grid in new[]{true,false})
            {
                ViewModel.HomeGridMode=grid;ViewModel.FolderGridMode=grid;ViewModel.FolderTracksGridMode=grid;
                ViewModel.Navigate(view);
                Check(ElementCompositionPreview.GetElementVisual(BrowseSurface).Opacity==0,$"{view} 切换后在动画排队前暴露了完成态");
                if(view is not ("settings" or "library" or "playlist")) {ViewModel.Tracks.Clear();ViewModel.Tracks.Add(song);}
                ViewModel.IsEmpty=false;await Task.Delay(80);UpdateLayout();await Task.Delay(40);
                Check(_browseMotionElements.Contains(BrowseSurface),$"{view} 没有页面入场动画");
                if(view=="settings") Check(SettingsOptions.Children.All(child=>_browseMotionElements.Contains(child)),"设置项没有逐项入场");
                else Check(_browseRowsEntered&&_browseEntranceRows>0,$"{view} 的列表／图标没有逐项入场");
                var version=_browseTransition;var count=_browseMotionElements.Count;
                ViewModel.CurrentTrack=song;
                if(view=="home") ViewModel.Tracks.Add(song with {Bvid="BV8xx411c7mD"});
                await Task.Delay(25);
                Check(version==_browseTransition&&count==_browseMotionElements.Count,"切歌或增量加载重播了整页动画");
            }
            ViewModel.Navigate("settings");ViewModel.Navigate("library");ViewModel.Navigate("playlist");
            Check(ElementCompositionPreview.GetElementVisual(BrowseSurface).Opacity==0,"连续切页暴露了未初始化的新页面");
            await Task.Delay(650);
            Check(ViewModel.IsPlaylistView&&ElementCompositionPreview.GetElementVisual(BrowseSurface).Opacity==1,"旧入场回调覆盖新页面或页面未恢复显示");
            _forceReducedMotion=true;ViewModel.Navigate("settings");
            Check(ElementCompositionPreview.GetElementVisual(BrowseSurface).Opacity==1,"减少动画模式短暂隐藏了页面");await Task.Delay(60);
            Check(ElementCompositionPreview.GetElementVisual(BrowseSurface).Opacity==1,"减少动画模式仍隐藏页面");
        }
        finally
        {
            _forceReducedMotion=false;ViewModel.HomeGridMode=oldHome;ViewModel.FolderGridMode=oldFolders;ViewModel.FolderTracksGridMode=oldFolderTracks;
            ViewModel.ClearQueueCommand.Execute(null);ViewModel.Folders.Clear();ViewModel.Tracks.Clear();ViewModel.Navigate("home");
        }
    }
    private async Task VerifyStableQueueActivationAsync()
    {
        foreach(var playlistView in new[]{true,false})
        foreach(var behavior in new[]{0,1,2})
        foreach(var twice in new[]{false,true})
        {
            using var handler=new DeferredVideoHandler();using var model=new PlayerViewModel(new BilibiliClient(handler));
            model.Client.RestoreCachedAccount(new(123,"播放列表稳定性验证",""));model.IsLoggedIn=true;
            model.SingleClickBehaviorIndex=model.DoubleClickBehaviorIndex=behavior;
            var first=new Track(1,"BV1xx411c7mD","第一首","UP","",120,111);
            var second=new Track(2,"BV2xx411c7mD","第二首","UP","",120);
            model.Queue.Add(first);model.Queue.Add(second);
            model.Navigate(playlistView?"playlist":"home");
            var changes=new List<NotifyCollectionChangedAction>();model.Queue.CollectionChanged+=(_,args)=>changes.Add(args.Action);
            model.ConfirmQueueChange=_=>throw new InvalidOperationException("选择队列内已有曲目不应询问");
            var task=model.ActivateTrackAsync(second,twice,!playlistView);
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(changes.Count==0&&model.Queue.Count==2&&ReferenceEquals(model.Queue[0],first)&&ReferenceEquals(model.Queue[1],second),"播放列表切歌重建或替换了列表");
            Check(QueueSelection.Same(model.CurrentTrack,second),"播放列表切歌没有立即切换当前项");
            model.ClearQueueCommand.Execute(null);handler.Release.TrySetResult();await task;
        }
        ViewModel.Store.SetSetting("singleClickBehavior",ViewModel.SingleClickBehaviorIndex.ToString());
        ViewModel.Store.SetSetting("doubleClickBehavior",ViewModel.DoubleClickBehaviorIndex.ToString());
    }
    private async Task VerifyInterruptibleFooterAsync()
    {
        ViewModel.IsLyricsOpen=false;await _lyricsMotionTask;
        _footerInputSerial++;_footerInputAt=MotionTime;var first=ToggleLyricsFromInputAsync();
        Check(ViewModel.IsLyricsOpen&&!first.IsCompleted,"点击没有立即打开详情");
        await ToggleLyricsFromInputAsync();Check(ViewModel.IsLyricsOpen,"同一次按下／点击／双击事件被重复处理");
        await Task.Delay(48);
        var before=_coverFlight!.Current();var opacity=_lyricsOpacityTween!.Sample().Position.X;
        _footerInputSerial++;_footerInputAt=MotionTime;var second=ToggleLyricsFromInputAsync();
        Check(!ViewModel.IsLyricsOpen&&!second.IsCompleted,"动画期间的新点击被忽略");
        var initial=_coverFlight!.Tween.Motion.Initial;
        Check(Math.Abs(initial.X-before.X)<24&&Math.Abs(initial.Y-before.Y)<24&&Math.Abs(initial.Z-before.Width)<24,"打断动画时封面跳回了端点");
        Check(Math.Abs(_lyricsOpacityTween!.Motion.Initial.X-opacity)<.12,"反向时透明度闪回端点");
        await first.WaitAsync(TimeSpan.FromMilliseconds(80));
        await Task.Delay(42);
        _footerInputSerial++;_footerInputAt=MotionTime;var third=ToggleLyricsFromInputAsync();
        Check(ViewModel.IsLyricsOpen,"第二次打断没有响应最新点击");
        await second.WaitAsync(TimeSpan.FromMilliseconds(80));await third;
        Check(_coverFlight is null&&LyricsView.Visibility==Visibility.Visible,"旧动画结束覆盖了最新展开状态");
        _footerInputSerial++;_footerInputAt=MotionTime;await ToggleLyricsFromInputAsync();
        Check(!ViewModel.IsLyricsOpen&&LyricsView.Visibility==Visibility.Collapsed,"最后一次点击未正确收起");
        _footerInputSerial=0;_footerConsumedSerial=-1;
    }
}
