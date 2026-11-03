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
        _trackActivationTask = ViewModel.ActivateTrackAsync(track, true, list == PlaylistList || list == QueueList);
#else
        _ = ViewModel.ActivateTrackAsync(track, true, list == PlaylistList || list == QueueList);
#endif
    }
}
