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
    public CenteredCover()
    {
        Background = _brush; CornerRadius = new CornerRadius(8); IsHitTestVisible = false;
        Loaded += (_, _) => SetImage(Source);
        Unloaded += (_, _) => { DetachPending(); _brush.ImageSource = null; };
    }
    private void SetImage(ImageSource? source)
    {
        DetachPending();
        // A recycled cell or newly selected track must never display the previous
        // item's artwork while its own image is loading, or after a load failure.
        _brush.ImageSource = null;
        if (source is BitmapImage bitmap && bitmap.PixelWidth == 0)
        {
            _pending = bitmap;
            bitmap.ImageOpened += ReplacementOpened;
            bitmap.ImageFailed += ReplacementFailed;
        }
        // URI images decode on demand through the live brush. Waiting for
        // ImageOpened before attaching the source prevents that decode entirely.
        _brush.ImageSource = source;
    }
    private void ReplacementOpened(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _pending) && ReferenceEquals(Source, _pending))
            DetachPending();
    }
    private void ReplacementFailed(object sender, ExceptionRoutedEventArgs args)
    { if (ReferenceEquals(sender, _pending)) { DetachPending(); _brush.ImageSource = null; } }
    private void DetachPending()
    {
        if (_pending is null) return;
        _pending.ImageOpened -= ReplacementOpened; _pending.ImageFailed -= ReplacementFailed; _pending = null;
    }
}
