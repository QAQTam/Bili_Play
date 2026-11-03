using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BiliPlay;

/// <summary>Uniformly fills a cover using the exact geometric center on both axes.</summary>
public sealed class CenteredCover : Grid
{
    private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(CenteredCover),
        new PropertyMetadata(null, (sender, args) => ((CenteredCover)sender)._brush.ImageSource = (ImageSource?)args.NewValue));
    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public CenteredCover() { Background = _brush; CornerRadius = new CornerRadius(8); IsHitTestVisible = false; }
}
