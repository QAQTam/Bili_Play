using System.Numerics;
using BiliPlay.Core;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace BiliPlay;

public sealed partial class MainPage
{
    private sealed record CoverFlightState(TimedTween Tween)
    {
        public Rect Current()
        { var rect=Tween.Sample().Position;return new(rect.X,rect.Y,Math.Max(1,rect.Z),Math.Max(1,rect.W)); }
        public Vector4 Velocity => Tween.Sample().Velocity;
    }
    private CoverFlightState? _coverFlight;
    private (Rect Bounds,Vector4 Velocity)? _pendingCoverSource;
    private CancellationTokenSource? _lyricsTransitionWait;
    private Rect ElementBounds(FrameworkElement element)
    {
        var point = element.TransformToVisual(this).TransformPoint(new(0, 0));
        return new(point.X, point.Y, element.ActualWidth, element.ActualHeight);
    }
    private void CancelCoverFlight()
    {
        _coverFlight = null;
        if (CoverFlightLayer is null) return;
        ElementCompositionPreview.SetIsTranslationEnabled(FlyingCover, true);
        var visual = ElementCompositionPreview.GetElementVisual(FlyingCover);
        visual.StopAnimation("Translation"); visual.StopAnimation("Scale");
        CoverFlightLayer.Visibility = Visibility.Collapsed;
        FooterCover.Opacity = CoverFrame.Opacity = 1;
    }
    private void PrepareCoverTransfer(bool opening)
    {
        ++_lyricsTransition;
        var source = opening ? FooterCover : CoverFrame;
        var rect = _coverFlight?.Current() ?? ElementBounds(source);
        var velocity = _coverFlight?.Velocity ?? Vector4.Zero;
        CancelCoverFlight(); SettleArtwork();
        _pendingCoverSource = MotionEnabled && ViewModel.HasCurrentTrack && rect.Width > 0 && rect.Height > 0 ? (rect,velocity) : null;
    }
    private async Task TransitionLyricsAsync(bool opening)
    {
        var version = ++_lyricsTransition;
        _lyricsTransitionWait?.Cancel();
        using var wait=new CancellationTokenSource(); _lyricsTransitionWait=wait;
        var source = _pendingCoverSource; _pendingCoverSource = null;
        var metadataBefore = LayoutInformation.GetLayoutSlot(FooterMetadata).X;
        var metadataMotion=_footerMetadataTween?.Sample() ?? (Vector4.Zero,Vector4.Zero);
        var fresh=LyricsView.Visibility==Visibility.Collapsed;
        var opacity=_lyricsOpacityTween?.Sample() ?? (new Vector4(fresh?0:1,0,0,0),Vector4.Zero);
        Navigation.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        ((MainWindow)App.Window).SetArtworkBackdropVisible(opening && !_accessibility.HighContrast, MotionEnabled);
        LyricsView.Visibility = Visibility.Visible; LyricsView.IsHitTestVisible = opening; LyricsView.Opacity = 1;
        var root = ElementCompositionPreview.GetElementVisual(LyricsView);
        NowPlayingButton.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        FooterArtworkWidth.Width = new GridLength(opening ? 0 : 80);
        FooterTrackInfo.ColumnSpacing = opening ? 0 : 16;
        UpdateLayout(); ResizeArtwork(); UpdateLayout(); SettleArtwork();
        var destination = opening ? CoverFrame : FooterCover;
        var target = ElementBounds(destination);
#if VISUAL_CHECKS
        _coverTargetAtStart = target;
#endif
        if (opening) _ = RefreshAtmosphereAsync(); else { _ambientLoad?.Cancel(); UpdateAmbientMotion(); }
        var metadataAfter = LayoutInformation.GetLayoutSlot(FooterMetadata).X;
        if (!MotionEnabled)
        {
            CancelCoverFlight(); ResetMotion(LyricsColumn); ResetMotion(FooterMetadata); ResetMotion(LyricsView);
            _footerMetadataTween=_lyricsColumnTween=_lyricsOpacityTween=null;
            LyricsView.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
            if (opening) FocusLyric(true);
            if(ReferenceEquals(_lyricsTransitionWait,wait)) _lyricsTransitionWait=null;
            return;
        }
        AnimateFooterMetadata(new((float)(metadataBefore-metadataAfter+metadataMotion.Item1.X),0,0),metadataMotion.Item2);
        _lyricsOpacityTween=AnimateTween(LyricsView,"Opacity",opacity.Item1,new(opening?1:0,0,0,0),opacity.Item2,opening?220:180);
        if (opening) LyricsEntrance(fresh);
        else
        {
            var column=_lyricsColumnTween?.Sample() ?? (Vector4.Zero,Vector4.Zero);
            _lyricsColumnTween=AnimateTween(LyricsColumn,"Translation",column.Item1,new(0,32,0,0),column.Item2,200);
        }
        var milliseconds = opening ? 360 : 240;
        if (source is { } transfer && target.Width > 0 && target.Height > 0)
        {
            var from=transfer.Bounds;
            var duration=milliseconds/1000d;
            var initial=new Vector4((float)from.X,(float)from.Y,(float)from.Width,(float)from.Height);
            var final=new Vector4((float)target.X,(float)target.Y,(float)target.Width,(float)target.Height);
            var velocity=transfer.Velocity;
            if(velocity==Vector4.Zero) velocity=(final-initial)*(float)(3/duration);
            var tween=new MotionTween(initial,final,velocity,duration);
            FlyingArtwork.Source = opening ? ViewModel.CurrentCover : ViewModel.LyricsCover;
            FlyingCover.Width = target.Width; FlyingCover.Height = target.Height;
            Canvas.SetLeft(FlyingCover, target.X); Canvas.SetTop(FlyingCover, target.Y);
            CoverFlightLayer.Visibility = Visibility.Visible; CoverFlightLayer.UpdateLayout();
            FooterCover.Opacity = CoverFrame.Opacity = 0;
            ElementCompositionPreview.SetIsTranslationEnabled(FlyingCover, true);
            var visual = ElementCompositionPreview.GetElementVisual(FlyingCover);
            visual.CenterPoint = Vector3.Zero;
            var slide=visual.Compositor.CreateVector3KeyFrameAnimation();slide.Duration=TimeSpan.FromMilliseconds(milliseconds);
            var resize=visual.Compositor.CreateVector3KeyFrameAnimation();resize.Duration=slide.Duration;
            for(var frame=0;frame<=40;frame++)
            {
                var sample=tween.At(duration*frame/40).Position;
                slide.InsertKeyFrame(frame/40f,new(sample.X-(float)target.X,sample.Y-(float)target.Y,0));
                resize.InsertKeyFrame(frame/40f,new(Math.Max(1,sample.Z)/(float)target.Width,Math.Max(1,sample.W)/(float)target.Height,1));
            }
            _coverFlight=new(new(tween,MotionTime));
            visual.StartAnimation("Translation", slide); visual.StartAnimation("Scale", resize);
        }
        if (opening) { _followPausedUntil = default; _lastAnimatedLyric = ViewModel.ActiveSubtitleIndex; FocusLyric(true); }
        try { await Task.Delay(milliseconds+20,wait.Token); }
        catch(OperationCanceledException) when(wait.IsCancellationRequested) { return; }
        if(ReferenceEquals(_lyricsTransitionWait,wait)) _lyricsTransitionWait=null;
        if (version != _lyricsTransition) return;
        CancelCoverFlight(); SettleArtwork();
        if (!opening)
        {
            LyricsView.Visibility = Visibility.Collapsed;
            root.StopAnimation("Opacity"); root.Opacity = 1; _lyricsOpacityTween=_lyricsColumnTween=null; ResetMotion(LyricsColumn); _resumeLyricsTimer.Stop();
        }
    }
}
