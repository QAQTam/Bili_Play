using System.Numerics;
using BiliPlay.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using DispatcherQueuePriority=Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace BiliPlay;

public sealed partial class MainPage
{
    private sealed record TimedTween(MotionTween Motion,double Started)
    {
        public (Vector4 Position,Vector4 Velocity) Sample() => Motion.At(MotionTime-Started);
    }
    private TimedTween AnimateTween(UIElement element,string property,Vector4 initial,Vector4 target,Vector4 velocity,int milliseconds)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element,true);
        var visual=ElementCompositionPreview.GetElementVisual(element);
        var tween=new MotionTween(initial,target,velocity,milliseconds/1000d);
        if(property=="Opacity")
        {
            var animation=visual.Compositor.CreateScalarKeyFrameAnimation();animation.Duration=TimeSpan.FromMilliseconds(milliseconds);
            for(var frame=0;frame<=40;frame++) animation.InsertKeyFrame(frame/40f,Math.Clamp(tween.At(milliseconds/1000d*frame/40).Position.X,0,1));
            visual.StartAnimation(property,animation);
        }
        else
        {
            var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.Duration=TimeSpan.FromMilliseconds(milliseconds);
            for(var frame=0;frame<=40;frame++) { var sample=tween.At(milliseconds/1000d*frame/40).Position;animation.InsertKeyFrame(frame/40f,new(sample.X,sample.Y,sample.Z)); }
            visual.StartAnimation(property,animation);
        }
        return new(tween,MotionTime);
    }
    private readonly HashSet<UIElement> _browseMotionElements=[];
    private bool _browseRowsEntered, _browseContainerPassPending, _browseMotionUnloaded;
#if VISUAL_CHECKS
    private int _browseEntranceRows;
#endif
    private void InitializeBrowseMotion()
    {
        Loaded+=(_,_)=>_browseMotionUnloaded=false;
        Unloaded+=(_,_)=>{_browseMotionUnloaded=true;_browseTransition++;_browseRowsEntered=true;};
        foreach(var list in new ListViewBase[]{RecommendationGrid,TrackList,PlaylistList,FolderGrid,FolderList,UploaderList,UploaderGrid,DownloadsList})
        {
            list.ContainerContentChanging+=(_,args)=>
            {
                if(args.InRecycleQueue||_browseRowsEntered) return;
                QueueBrowseRows();
                if(args.Phase==0) args.RegisterUpdateCallback((_,phase)=>{if(!phase.InRecycleQueue&&!_browseRowsEntered) QueueBrowseRows();});
            };
        }
    }
    private ListViewBase CurrentBrowseList() => ViewModel.IsLibraryView ? (ViewModel.FolderGridMode?FolderGrid:FolderList)
        : ViewModel.IsPlaylistView ? PlaylistList : ViewModel.IsSearchUploaders ? (ViewModel.SearchGridMode ? UploaderGrid : UploaderList)
        : ViewModel.IsDownloadingView ? DownloadsList : ViewModel.IsHomeGridView?RecommendationGrid:TrackList;
    private void QueueBrowseEntrance()
    {
        var version=++_browseTransition;
        foreach(var element in _browseMotionElements) ResetMotion(element);
        _browseMotionElements.Clear();_browseRowsEntered=false;
#if VISUAL_CHECKS
        _browseEntranceRows=0;
#endif
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=>
        {
            if(_browseMotionUnloaded||version!=_browseTransition||!ViewModel.IsLoggedIn||ViewModel.IsLyricsOpen) return;
            AnimateBrowseElement(BrowseSurface,24,0);
            if(ViewModel.IsSettingsView)
            {
                var order=0;
                foreach(var element in SettingsOptions.Children.OfType<FrameworkElement>()) AnimateBrowseElement(element,32,order++*32);
                AnimateBrowseElement(AboutCard,32,64);
                _browseRowsEntered=true;
            }
            else QueueBrowseRows();
        });
    }
    private void QueueBrowseRows()
    {
        if(_browseRowsEntered||_browseContainerPassPending) return;
        _browseContainerPassPending=true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,()=>
        {
            _browseContainerPassPending=false;
            if(_browseMotionUnloaded||_browseRowsEntered||!ViewModel.IsLoggedIn||ViewModel.IsSettingsView||ViewModel.IsLyricsOpen) return;
            var list=CurrentBrowseList();
            if(!list.IsLoaded||list.ActualWidth<=0||list.ActualHeight<=0) return;
            var rows=new List<FrameworkElement>();
            var first=list.ItemsPanelRoot switch { ItemsStackPanel panel=>Math.Max(0,panel.FirstCacheIndex),ItemsWrapGrid panel=>Math.Max(0,panel.FirstCacheIndex),_=>0 };
            var last=list.ItemsPanelRoot switch { ItemsStackPanel panel=>panel.LastCacheIndex,ItemsWrapGrid panel=>panel.LastCacheIndex,_=>Math.Min(31,list.Items.Count-1) };
            for(var index=first;index<=last&&index<list.Items.Count;index++)
            {
                if(list.ContainerFromIndex(index) is not FrameworkElement row) continue;
                var point=row.TransformToVisual(list).TransformPoint(new(0,0));
                if(point.Y+row.ActualHeight>0&&point.Y<list.ActualHeight) rows.Add(row);
                if(rows.Count>=16) break;
            }
            if(rows.Count==0) return;
            _browseRowsEntered=true;
#if VISUAL_CHECKS
            _browseEntranceRows=rows.Count;
#endif
            for(var index=0;index<rows.Count;index++) AnimateBrowseElement(rows[index],32,Math.Min(index,7)*24);
        });
    }
    private void AnimateBrowseElement(UIElement element,float distance,int delay)
    {
        ResetMotion(element);_browseMotionElements.Add(element);
        if(!MotionEnabled) return;
        var visual=ElementCompositionPreview.GetElementVisual(element);
        var ease=visual.Compositor.CreateCubicBezierEasingFunction(new(.12f,.85f),new(.18f,1));
        var slide=visual.Compositor.CreateVector3KeyFrameAnimation();slide.InsertKeyFrame(0,new(0,distance,0));slide.InsertKeyFrame(1,Vector3.Zero,ease);
        slide.Duration=TimeSpan.FromMilliseconds(400);slide.DelayTime=TimeSpan.FromMilliseconds(delay);slide.DelayBehavior=AnimationDelayBehavior.SetInitialValueBeforeDelay;
        var fade=visual.Compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(1,1,ease);fade.Duration=TimeSpan.FromMilliseconds(220);fade.DelayTime=slide.DelayTime;fade.DelayBehavior=slide.DelayBehavior;
        visual.StartAnimation("Translation",slide);visual.StartAnimation("Opacity",fade);
    }
}
