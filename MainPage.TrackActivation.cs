using System.Runtime.InteropServices;
using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BiliPlay;

public sealed partial class MainPage
{
    private CancellationTokenSource? _pendingTrackClick;
#if VISUAL_CHECKS
    private Task _trackActivationTask = Task.CompletedTask;
#endif
    private ListViewBase? _ignoredClickOwner;
    private Track? _ignoredClickTrack;
    private DateTimeOffset _ignoreClickUntil;
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    private void InitializeTrackActivation()
    {
        foreach (var list in new ListViewBase[] { RecommendationGrid, TrackList, PlaylistList, QueueList })
        {
            list.IsDoubleTapEnabled = true;
            list.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler(TrackDoubleTapped), true);
        }
        // A new press starts a new gesture and also cancels a pending click before a drag or another control action.
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            _ignoredClickOwner = null; _ignoredClickTrack = null; CancelPendingTrackClick();
        }), true);
        ViewModel.BrowseChanged += CancelPendingTrackClick;
    }

    private void CancelPendingTrackClick()
    {
        _pendingTrackClick?.Cancel(); _pendingTrackClick = null;
    }
    private void TrackItemClicked(object sender, ItemClickEventArgs args)
    {
        if (sender is not ListViewBase list || args.ClickedItem is not Track track || ViewModel.IsCacheSelectionMode) return;
        ScheduleTrackClick(list, track);
    }
    private void ScheduleTrackClick(ListViewBase list, Track track)
    {
        if (_ignoredClickOwner == list && QueueSelection.Same(_ignoredClickTrack, track) && DateTimeOffset.UtcNow < _ignoreClickUntil) return;
        CancelPendingTrackClick();
        var pending = _pendingTrackClick = new CancellationTokenSource();
#if VISUAL_CHECKS
        _trackActivationTask = CompleteSingleTrackClickAsync(list, track, pending);
#else
        _ = CompleteSingleTrackClickAsync(list, track, pending);
#endif
    }
    private async Task CompleteSingleTrackClickAsync(ListViewBase list, Track track, CancellationTokenSource pending)
    {
        try
        {
            // WinUI emits the first click before it knows whether it will become a double tap.
            // Wait for the user's system interval so a double tap performs exactly one queue operation.
            await Task.Delay(TimeSpan.FromMilliseconds(GetDoubleClickTime() + 30), pending.Token);
            if (!ReferenceEquals(_pendingTrackClick, pending)) return;
            _pendingTrackClick = null;
            await PlayHomeCoverLaunchAsync(list, track);
            await ViewModel.ActivateTrackAsync(track, false, list == PlaylistList || list == QueueList);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        finally { pending.Dispose(); }
    }
    private void TrackDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is not ListViewBase list || ViewModel.IsCacheSelectionMode) return;
        for (var node = args.OriginalSource as DependencyObject; node is not null && node != list; node = VisualTreeHelper.GetParent(node))
        {
            // Buttons, UP links and inline playback controls keep their own native behavior.
            if (node is ButtonBase or RangeBase or TextBox) return;
            if (node is SelectorItem item && list.ItemFromContainer(item) is Track track)
            {
                args.Handled = true;
                ActivateDoubleTrackClick(list, track);
                return;
            }
        }
    }
    private void ActivateDoubleTrackClick(ListViewBase list, Track track)
    {
        CancelPendingTrackClick();
        _ignoredClickOwner = list; _ignoredClickTrack = track;
        _ignoreClickUntil = DateTimeOffset.UtcNow.AddMilliseconds(GetDoubleClickTime());
#if VISUAL_CHECKS
        _trackActivationTask = ActivateDoubleTrackAsync(list, track);
#else
        _ = ActivateDoubleTrackAsync(list, track);
#endif
    }
    private async Task ActivateDoubleTrackAsync(ListViewBase list, Track track)
    {
        await PlayHomeCoverLaunchAsync(list, track);
        await ViewModel.ActivateTrackAsync(track, true, list == PlaylistList || list == QueueList);
    }
    private async Task PlayHomeCoverLaunchAsync(ListViewBase list, Track track)
    {
        if (list != RecommendationGrid || !MotionEnabled || list.ContainerFromItem(track) is not FrameworkElement row || FindVisual<CenteredCover>(row) is not { } cover) return;
        var source = ElementBounds(cover); var target = ElementBounds(FooterCover);
        if (source.Width <= 0 || target.Width <= 0) return;
        FlyingArtwork.Source = cover.Source; FlyingCover.Width = target.Width; FlyingCover.Height = target.Height;
        Canvas.SetLeft(FlyingCover, target.X); Canvas.SetTop(FlyingCover, target.Y); CoverFlightLayer.Visibility = Visibility.Visible;
        CoverFlightLayer.UpdateLayout();
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FlyingCover);
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(FlyingCover, true);
        var slide = visual.Compositor.CreateVector3KeyFrameAnimation(); slide.Duration = TimeSpan.FromMilliseconds(260);
        var scale = visual.Compositor.CreateVector3KeyFrameAnimation(); scale.Duration = slide.Duration;
        var start = new System.Numerics.Vector3((float)(source.X - target.X), (float)(source.Y - target.Y), 0);
        var ratio = new System.Numerics.Vector3((float)(source.Width / target.Width), (float)(source.Height / target.Height), 1);
        slide.InsertKeyFrame(0, start); slide.InsertKeyFrame(.72f, new((float)(-18), -8, 0)); slide.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        scale.InsertKeyFrame(0, ratio); scale.InsertKeyFrame(.72f, new(1.06f, 1.06f, 1)); scale.InsertKeyFrame(1, System.Numerics.Vector3.One);
        visual.StartAnimation("Translation", slide); visual.StartAnimation("Scale", scale); cover.Opacity = .25;
        await Task.Delay(280);
        visual.StopAnimation("Translation"); visual.StopAnimation("Scale"); CoverFlightLayer.Visibility = Visibility.Collapsed; cover.Opacity = 1;
    }
}
