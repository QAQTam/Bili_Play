using System.Numerics;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace BiliPlay;

public sealed partial class MainPage
{
    private readonly Dictionary<ToastNotice, Border> _noticeBorders = new();
    private void NoticeLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Border { Tag: ToastNotice notice } border) return;
        _noticeBorders[notice] = border;
        if (!MotionEnabled) return;
        ElementCompositionPreview.SetIsTranslationEnabled(border, true);
        var visual = ElementCompositionPreview.GetElementVisual(border);
        var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new(400, 0, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, visual.Compositor.CreateCubicBezierEasingFunction(new(.12f,.8f),new(.2f,1)));
        slide.Duration = TimeSpan.FromMilliseconds(280); visual.StartAnimation("Translation", slide);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(0,0); fade.InsertKeyFrame(1,1);
        fade.Duration = TimeSpan.FromMilliseconds(180); visual.StartAnimation("Opacity", fade);
    }
    private void NoticeUnloaded(object sender, RoutedEventArgs args)
    { if (sender is Border { Tag: ToastNotice notice }) _noticeBorders.Remove(notice); }
    private async Task AnimateNoticeOutAsync(ToastNotice notice)
    {
        if (!MotionEnabled || !_noticeBorders.TryGetValue(notice, out var border)) return;
        border.IsHitTestVisible = false;
        ElementCompositionPreview.SetIsTranslationEnabled(border, true);
        var visual = ElementCompositionPreview.GetElementVisual(border);
        var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1, new(400,0,0), visual.Compositor.CreateCubicBezierEasingFunction(new(.4f,0),new(1,1)));
        slide.Duration = TimeSpan.FromMilliseconds(220); visual.StartAnimation("Translation", slide);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(1,0);
        fade.Duration = slide.Duration; visual.StartAnimation("Opacity",fade);
        await Task.Delay(240);
    }
}
