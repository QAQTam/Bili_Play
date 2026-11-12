using System.Numerics;
using BiliPlay.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private readonly HashSet<object> _enteredBrowseItems=[];
    private readonly HashSet<UIElement> _preparedBrowseItems=[];
    private bool _browseContainerPassPending, _browseMotionUnloaded;
    private ListViewBase? _enteringBrowseList;
    private int _browseRowVersion;
#if VISUAL_CHECKS
    private bool _browseRowsEntered;
    private int _browseEntranceRows;
#endif
    [System.Diagnostics.Conditional("VISUAL_CHECKS")]
    private void RecordBrowseRowsEntered(bool value)
    {
#if VISUAL_CHECKS
        _browseRowsEntered=value;
#endif
    }
    private void InitializeBrowseMotion()
    {
        RecommendationGrid.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        NotificationList.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        Loaded+=(_,_)=>_browseMotionUnloaded=false;
        Unloaded+=(_,_)=>{_browseMotionUnloaded=true;_browseTransition++;FinishBrowseEntrance();};
        BrowseSurface.LayoutUpdated += (_, _) => { if (!_browseMotionUnloaded && IsLoaded) QueueBrowseRows(); };
        foreach(var list in new ListViewBase[]{RecommendationGrid,TrackList,PlaylistList,FolderGrid,FolderList,UploaderList,UploaderGrid,DownloadsList,NotificationList})
        {
            list.SizeChanged += (_, _) => list.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new(0,0,list.ActualWidth,list.ActualHeight) };
            list.ContainerContentChanging+=(_,args)=>
            {
                if (args.InRecycleQueue)
                {
                    if (args.ItemContainer.ContentTemplateRoot is UIElement content)
                    { _preparedBrowseItems.Remove(content); _browseMotionElements.Remove(content); ResetMotion(content); }
                    return;
                }
                PrepareBrowseItem(list, args.ItemContainer, args.Item);
                QueueBrowseRows();
                if(args.Phase==0) args.RegisterUpdateCallback((_,phase)=>
                { if(!phase.InRecycleQueue) { PrepareBrowseItem(list,phase.ItemContainer,phase.Item); QueueBrowseRows(); } });
            };
        }
    }
    private static object BrowseItemKey(object item) => item switch
    {
        Track track => $"track:{track.Bvid}:{track.Page}",
        Folder folder => $"folder:{folder.Id}",
        Uploader uploader => $"uploader:{uploader.Id}",
        AccountNotification notice when notice.Id.Length > 0 => $"notice:{notice.Id}",
        _ => item
    };
    private void PrepareBrowseItem(ListViewBase list, SelectorItem container, object item)
    {
        if (_browseMotionUnloaded || !MotionEnabled || !ViewModel.IsLoggedIn || ViewModel.IsLyricsOpen || list != CurrentBrowseList()
            || _enteredBrowseItems.Contains(BrowseItemKey(item)) || container.ContentTemplateRoot is not UIElement content || !_preparedBrowseItems.Add(content)) return;
        ResetMotion(content); _browseMotionElements.Add(content);
        ElementCompositionPreview.GetElementVisual(content).Opacity=0;
    }
    private ListViewBase CurrentBrowseList() => ViewModel.IsNotificationsView ? NotificationList : ViewModel.IsLibraryView ? (ViewModel.FolderGridMode?FolderGrid:FolderList)
        : ViewModel.IsPlaylistView ? PlaylistList : ViewModel.IsSearchUploaders ? (ViewModel.SearchGridMode ? UploaderGrid : UploaderList)
        : ViewModel.IsDownloadingView ? DownloadsList : ViewModel.IsHomeGridView?RecommendationGrid:TrackList;
    private void QueueBrowseEntrance()
    {
        var version=++_browseTransition;
        FinishBrowseEntrance();
        RecordBrowseRowsEntered(false);_browseContainerPassPending=false;
#if VISUAL_CHECKS
        _browseEntranceRows=0;
#endif
        // Arm the first frame synchronously, before bindings/layout can present the destination page.
        if(_browseMotionUnloaded||!ViewModel.IsLoggedIn||ViewModel.IsLyricsOpen||!MotionEnabled)
        {ResetMotion(BrowseSurface);RecordBrowseRowsEntered(true);return;}
        // Hide the destination synchronously so the compositor never exposes a
        // fully-rendered frame before the first entrance keyframe is committed.
        ElementCompositionPreview.SetIsTranslationEnabled(BrowseSurface,true);
        var surface=ElementCompositionPreview.GetElementVisual(BrowseSurface);
        surface.StopAnimation("Opacity");surface.StopAnimation("Translation");
        surface.Opacity=0;surface.Properties.InsertVector3("Translation",new(0,24,0));
        if (!ViewModel.IsSettingsView && !ViewModel.IsNotificationsView)
        {
            _enteringBrowseList = CurrentBrowseList();
            ElementCompositionPreview.GetElementVisual(_enteringBrowseList).Opacity = 0;
        }
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal,()=>
        {
            if(version!=_browseTransition) return;
            if(_browseMotionUnloaded||!ViewModel.IsLoggedIn||ViewModel.IsLyricsOpen)
            {ResetMotion(BrowseSurface);RecordBrowseRowsEntered(true);return;}
            AnimateBrowseElement(BrowseSurface,24,0);
            if(ViewModel.IsSettingsView)
            {
                var order=0;
                foreach(var element in SettingsOptions.Children.OfType<FrameworkElement>()) AnimateBrowseElement(element,32,order++*32);
                AnimateBrowseElement(AboutCard,32,64);
                AnimateBrowseElement(ViewAgreementButton,24,96);
                RecordBrowseRowsEntered(true);
            }
            else if (ViewModel.IsNotificationsView)
            {
                AnimateBrowseElement(NotificationCategoryPicker,24,0);
                QueueBrowseRows();
            }
            else { if (ViewModel.IsSpaceView) AnimateBrowseElement(SpaceHeader,32,0); QueueBrowseRows(); }
        });
    }
    private void FinishBrowseEntrance()
    {
        RecordBrowseRowsEntered(true);
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= BrowseRowsFrame;
        _browseContainerPassPending=false;
        foreach (var element in _browseMotionElements) ResetMotion(element);
        _browseMotionElements.Clear();
        _enteredBrowseItems.Clear(); _preparedBrowseItems.Clear();
        if (_enteringBrowseList is { } list) ElementCompositionPreview.GetElementVisual(list).Opacity = 1;
        _enteringBrowseList = null;
        ResetMotion(BrowseSurface);
    }
    private void QueueBrowseRows()
    {
        if(_browseContainerPassPending || !MotionEnabled || _browseMotionUnloaded || !ViewModel.IsLoggedIn || ViewModel.IsSettingsView || ViewModel.IsLyricsOpen) return;
        _browseContainerPassPending=true;
        _browseRowVersion=_browseTransition;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += BrowseRowsFrame;
    }
    private void BrowseRowsFrame(object? sender, object args)
    {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= BrowseRowsFrame;
            _browseContainerPassPending=false;
            if(_browseRowVersion!=_browseTransition) return;
            if(_browseMotionUnloaded||!ViewModel.IsLoggedIn||ViewModel.IsSettingsView||ViewModel.IsLyricsOpen) return;
            var list=CurrentBrowseList();
            if(!list.IsLoaded||list.Visibility != Visibility.Visible || list.ActualWidth<=0||list.ActualHeight<=0) return;
            var rows=new List<FrameworkElement>();
            var first=list.ItemsPanelRoot switch { ItemsStackPanel panel=>Math.Max(0,panel.FirstCacheIndex),ItemsWrapGrid panel=>Math.Max(0,panel.FirstCacheIndex),_=>0 };
            var last=list.ItemsPanelRoot switch { ItemsStackPanel panel=>panel.LastCacheIndex,ItemsWrapGrid panel=>panel.LastCacheIndex,_=>Math.Min(31,list.Items.Count-1) };
            for(var index=first;index<=last&&index<list.Items.Count;index++)
            {
                if(list.ContainerFromIndex(index) is not SelectorItem container || container.ContentTemplateRoot is not FrameworkElement row || row.ActualHeight <= 0) continue;
                var key=BrowseItemKey(list.Items[index]);
                if(_enteredBrowseItems.Contains(key)) continue;
                FrameworkElement viewport = list == NotificationList ? NotificationsScroller : list;
                var point=container.TransformToVisual(viewport).TransformPoint(new(0,0));
                if(point.Y+container.ActualHeight>0&&point.Y<viewport.ActualHeight)
                { PrepareBrowseItem(list,container,list.Items[index]); rows.Add(row); _enteredBrowseItems.Add(key); }
                // Offscreen rows stay hidden until they enter the viewport. Never
                // expose later loading batches in their final state for one frame.
            }
            if(rows.Count==0) return;
            RecordBrowseRowsEntered(true);
#if VISUAL_CHECKS
            _browseEntranceRows+=rows.Count;
#endif
            // Grid cards enter one after another for a clear cover reveal. List rows
            // use the same compact stagger as the playlist so switching views feels
            // consistent without making the page wait on every row.
            var grid = list is GridView;
            var distance = grid ? 80f : 32f;
            var stagger = grid ? 40 : 24;
            for(var index=0;index<rows.Count;index++) { _preparedBrowseItems.Remove(rows[index]); AnimateBrowseElement(rows[index],distance,Math.Min(index,7)*stagger); }
            ElementCompositionPreview.GetElementVisual(list).Opacity=1;
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
        var scale=visual.Compositor.CreateVector3KeyFrameAnimation();
        // Keep the scale inside the layout slot. Grid cards use a deeper settle to
        // make the larger entrance motion visible without touching neighbours.
        var gridMotion = distance > 40;
        scale.InsertKeyFrame(0,new Vector3(gridMotion ? .95f : .985f));
        scale.InsertKeyFrame(.78f,new Vector3(1.0f),ease);
        scale.InsertKeyFrame(1,Vector3.One,ease);
        scale.Duration=TimeSpan.FromMilliseconds(400);scale.DelayTime=slide.DelayTime;scale.DelayBehavior=slide.DelayBehavior;
        visual.CenterPoint = new Vector3((float)(element.RenderSize.Width / 2), (float)(element.RenderSize.Height / 2), 0);
        visual.StartAnimation("Translation",slide);visual.StartAnimation("Opacity",fade);visual.StartAnimation("Scale",scale);
    }
}
