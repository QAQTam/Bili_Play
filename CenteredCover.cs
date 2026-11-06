using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BiliPlay;

/// <summary>Uniformly fills a cover using the exact geometric center on both axes.</summary>
public sealed class CenteredCover : Grid
{
    private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
    private BitmapImage? _pending;
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(CenteredCover),
        new PropertyMetadata(null, (sender, args) => ((CenteredCover)sender).SetImage((ImageSource?)args.NewValue)));
    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public CenteredCover() { Background = _brush; CornerRadius = new CornerRadius(8); IsHitTestVisible = false; }
    private void SetImage(ImageSource? source)
    {
        DetachPending();
        if (source is not BitmapImage bitmap || bitmap.PixelWidth > 0 || _brush.ImageSource is null)
        { _brush.ImageSource = source; return; }
        // Keep the existing cover until its replacement is decoded. Rebinding a
        // folder preview must not show an empty ImageBrush between network frames.
        _pending = bitmap;
        bitmap.ImageOpened += ReplacementOpened;
        bitmap.ImageFailed += ReplacementFailed;
    }
    private void ReplacementOpened(object sender, RoutedEventArgs args)
    { if (_pending is { } ready) { DetachPending(); _brush.ImageSource = ready; } }
    private void ReplacementFailed(object sender, ExceptionRoutedEventArgs args) => DetachPending();
    private void DetachPending()
    {
        if (_pending is null) return;
        _pending.ImageOpened -= ReplacementOpened; _pending.ImageFailed -= ReplacementFailed; _pending = null;
    }
}
