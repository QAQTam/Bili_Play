using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BiliPlay;

public sealed partial class MainWindow
{
    private bool _artworkBackdropVisible;
    private int _backdropTransition;
    private bool _backdropDrifting;

    private void AtmosphereSizeChanged(object sender, SizeChangedEventArgs args) =>
        AtmosphereHost.Clip = new RectangleGeometry { Rect = new(0, 0, args.NewSize.Width, args.NewSize.Height) };

    internal void SetArtworkBackdropVisible(bool visible, bool animate)
    {
        if (_artworkBackdropVisible == visible && animate) return;
        _artworkBackdropVisible = visible;
        var version = ++_backdropTransition;
        var visual = ElementCompositionPreview.GetElementVisual(AtmosphereHost);
        if (!animate)
        {
            visual.StopAnimation("Opacity"); visual.Opacity = visible ? 1 : 0;
            AtmosphereHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        // An implicit starting value continues from the current opacity on rapid reversals.
        if (AtmosphereHost.Visibility == Visibility.Collapsed) visual.Opacity = 0;
        AtmosphereHost.Visibility = Visibility.Visible;
        var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, visible ? 1 : 0,
            visual.Compositor.CreateCubicBezierEasingFunction(new(.16f, .85f), new(.22f, 1)));
        fade.Duration = TimeSpan.FromMilliseconds(visible ? 320 : 200);
        visual.StartAnimation("Opacity", fade);
        batch.Completed += (_, _) =>
        {
            if (version == _backdropTransition)
            {
                visual.StopAnimation("Opacity"); visual.Opacity = visible ? 1 : 0;
                if (!visible) AtmosphereHost.Visibility = Visibility.Collapsed;
            }
            batch.Dispose();
        };
        batch.End();
    }

    internal void ResetArtworkBackdrop()
    {
        AmbientBackdrop.Source = null; AmbientIncoming.Source = null;
        var incoming = ElementCompositionPreview.GetElementVisual(AmbientIncoming);
        incoming.StopAnimation("Opacity"); incoming.Opacity = 1;
        SetArtworkBackdropMotion(false);
    }

    internal async Task PresentArtworkBackdropAsync(WriteableBitmap bitmap, bool animate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var visual = ElementCompositionPreview.GetElementVisual(AmbientIncoming);
        visual.StopAnimation("Opacity"); visual.Opacity = 1; AmbientIncoming.Source = bitmap;
        if (animate)
        {
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1); fade.Duration = TimeSpan.FromMilliseconds(700);
            visual.StartAnimation("Opacity", fade);
            await Task.Delay(720, token);
        }
        token.ThrowIfCancellationRequested();
        AmbientBackdrop.Source = bitmap; AmbientIncoming.Source = null;
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
    }

    internal void SetArtworkBackdropMotion(bool playing)
    {
        // Do not restart a running cycle on every playback update.
        if (_backdropDrifting == playing) return;
        _backdropDrifting = playing;
        ElementCompositionPreview.SetIsTranslationEnabled(AmbientBackdrop, true);
        var visual = ElementCompositionPreview.GetElementVisual(AmbientBackdrop);
        visual.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero);
        if (!playing) return;
        var drift = visual.Compositor.CreateVector3KeyFrameAnimation();
        var ease = visual.Compositor.CreateCubicBezierEasingFunction(new(.42f, 0), new(.58f, 1));
        drift.InsertKeyFrame(0, Vector3.Zero); drift.InsertKeyFrame(.5f, new(10, -6, 0), ease); drift.InsertKeyFrame(1, Vector3.Zero, ease);
        drift.Duration = TimeSpan.FromSeconds(24); drift.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation", drift);
    }
}
