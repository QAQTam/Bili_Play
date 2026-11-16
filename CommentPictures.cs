using BiliPlay.Core;
using BiliPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BiliPlay;

public sealed class CommentPictureEventArgs(CommentPicture picture, IReadOnlyList<CommentPicture>? pictures = null) : EventArgs
{ public CommentPicture Picture { get; } = picture; public IReadOnlyList<CommentPicture> Pictures { get; } = pictures ?? [picture]; }

public sealed class CommentPictures : UserControl
{
    public static readonly DependencyProperty CommentProperty = DependencyProperty.Register(nameof(Comment), typeof(BiliComment), typeof(CommentPictures), new PropertyMetadata(null, Changed));
    public BiliComment? Comment { get => (BiliComment?)GetValue(CommentProperty); set => SetValue(CommentProperty, value); }
    public event EventHandler<CommentPictureEventArgs>? PictureClicked;
    private readonly Grid _pictures = new() { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<Action> _resize = [];
    private XamlRoot? _observedRoot;
    public CommentPictures()
    {
        Content = _pictures; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SizeChanged += (_, _) => ResizePictures();
        Loaded += (_, _) =>
        {
            ReleaseRoot(); _observedRoot = XamlRoot;
            if (_observedRoot is not null) _observedRoot.Changed += RootChanged;
            ResizePictures();
        };
        Unloaded += (_, _) => ReleaseRoot();
    }
    private void ReleaseRoot()
    {
        // XamlRoot can already be null during Unloaded; detach from the exact root we subscribed to.
        if (_observedRoot is not null) _observedRoot.Changed -= RootChanged;
        _observedRoot = null;
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizePictures();
    private void ResizePictures() { foreach (var resize in _resize) resize(); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((CommentPictures)sender).Render();
    internal static BitmapImage Source(CommentPicture picture)
    {
#if VISUAL_CHECKS
        if (App.IsVisualCheck)
        {
            var fixture = Path.Combine(App.VisualCheckDirectory!, $"image-{picture.Width}x{picture.Height}.png");
            return new BitmapImage(new Uri(File.Exists(fixture) ? fixture : Path.Combine(App.VisualCheckDirectory!, "cover.png")));
        }
#endif
        // Zero decode dimensions retain the original image; only the visual layout is scaled.
        return ArtworkCache.Create(new Uri(picture.OriginalUrl), 0);
    }
    private void Render()
    {
        _pictures.Children.Clear(); _pictures.ColumnDefinitions.Clear(); _resize.Clear();
        var pictures = Comment?.Pictures?.Where(picture => picture.IsValid).Take(CommentImagePayload.MaxPictures).ToArray() ?? [];
        Visibility = pictures.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        for (var col = 0; col < Math.Min(3, pictures.Length); col++) _pictures.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        foreach (var picture in pictures.Take(3))
        {
            var source = Source(picture);
            var image = new Image { Source = source, Stretch = pictures.Length == 1 ? Stretch.Uniform : Stretch.UniformToFill };
            var placeholder = new TextBlock { Text = "正在加载图片…", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var frame = new Grid(); frame.Children.Add(image); frame.Children.Add(placeholder);
            var button = new Button { Content = frame, Tag = picture, Padding = new(0), BorderThickness = new(0), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(4) };
            void Resize()
            {
                var width = source.PixelWidth > 0 ? source.PixelWidth : picture.Width;
                var height = source.PixelHeight > 0 ? source.PixelHeight : picture.Height;
                var side = Math.Min(128, Math.Max(1, (ActualWidth - 8) / Math.Min(3, pictures.Length)));
                var size = pictures.Length == 1 ? ImageDisplaySize.Fit(width, height, Math.Min(240, Math.Max(1, ActualWidth)), 180) : (Width: side, Height: side);
                image.Width = frame.Width = size.Width; image.Height = frame.Height = size.Height;
            }
            _resize.Add(Resize);
            image.ImageOpened += (_, _) => { placeholder.Visibility = Visibility.Collapsed; Resize(); };
            image.ImageFailed += (_, _) => placeholder.Text = "图片加载失败";
            if (source.PixelWidth > 0) placeholder.Visibility = Visibility.Collapsed;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"查看评论图片 {_pictures.Children.Count + 1}");
            button.Click += (_, _) => PictureClicked?.Invoke(this, new(picture, pictures));
            var index = _pictures.Children.Count;
            if (index == 2 && pictures.Length > 3)
            {
                var badge = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(190, 0, 0, 0)), CornerRadius = new(4), Padding = new(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
                    Child = new TextBlock { Text = $"+{pictures.Length - 3}", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } };
                frame.Children.Add(badge);
            }
            Grid.SetColumn(button, index); _pictures.Children.Add(button); Resize();
        }
    }
}
