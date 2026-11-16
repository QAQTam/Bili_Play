using BiliPlay.Core;
using BiliPlay.ViewModels;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;

namespace BiliPlay;

public sealed partial class MainPage
{
    private bool _commentImagePickerOpen, _commentImageDialogOpen;
    private async void AddCommentImagesClick(object sender, RoutedEventArgs args)
    {
        if (_commentImagePickerOpen || !ViewModel.CanAddCommentImages) return;
        _commentImagePickerOpen = true;
        var context = ViewModel.CommentImageContext;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var files = await picker.PickMultipleFilesAsync();
            if (files.Count > 0) await ViewModel.AddCommentImagesAsync(files, context);
        }
        catch (Exception) { ViewModel.Notify("无法打开图片选择器，请稍后重试。", NoticeLevel.Warn); }
        finally { _commentImagePickerOpen = false; }
    }
    private void RemoveCommentImageClick(object sender, RoutedEventArgs args)
    { if (sender is FrameworkElement { Tag: PendingCommentImage image }) ViewModel.RemoveCommentImage(image); }
    private async void CommentPictureClicked(object? sender, CommentPictureEventArgs args) => await ShowCommentPictureAsync(args.Picture, args.Pictures);
    private async Task ShowCommentPictureAsync(CommentPicture picture, IReadOnlyList<CommentPicture>? pictures = null)
    {
        if (_commentImageDialogOpen || !picture.IsValid) return;
        _commentImageDialogOpen = true;
        using var lifetime = new CancellationTokenSource();
        var root = XamlRoot;
        var focused = FocusManager.GetFocusedElement(root) as Control;
        var popup = new Popup { XamlRoot = root, IsLightDismissEnabled = false };
        try
        {
            var gallery = pictures?.Where(item => item.IsValid).ToArray() ?? [picture];
            if (gallery.Length == 0) gallery = [picture];
            var selected = Math.Max(0, Array.IndexOf(gallery, picture));
            var source = CommentPictures.Source(picture);
            var image = new Image { Name = "PicturePreviewImage", Source = source, Stretch = Stretch.Uniform };
            var scroll = new ScrollViewer { Content = image, ZoomMode = ZoomMode.Enabled, MinZoomFactor = 1, MaxZoomFactor = 4,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            var frame = new Grid { CornerRadius = new(8), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            frame.Children.Add(scroll);
            var status = new TextBlock { Text = "正在加载图片…", TextWrapping = TextWrapping.Wrap, MaxWidth = 240,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            frame.Children.Add(status);
            var overlay = new Grid { Name = "PicturePreviewOverlay", RequestedTheme = ElementTheme.Dark,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(185, 0, 0, 0)) };
            overlay.TabFocusNavigation = KeyboardNavigationMode.Cycle;
            var layout = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(40) });
            layout.Children.Add(frame);
            var controls = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(controls, 1); layout.Children.Add(controls); overlay.Children.Add(layout); popup.Child = overlay;
            Button Tool(string name, string glyph)
            {
                var button = new Button { Name = name, Content = new FontIcon { Glyph = glyph, FontSize = 16 },
                    Width = 40, Height = 40, Padding = new(0), CornerRadius = new(8) };
                controls.Children.Add(button); return button;
            }
            var close = Tool("ClosePicturePreview", "\uE711");
            var download = Tool("DownloadPicturePreview", "\uE896");
            void Label(Button button, string label)
            { Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label); }
            Label(close, "关闭图片 (Esc)"); Label(download, "下载原图");
            var previous = Tool("PreviousPicturePreview", "\uE76B");
            var next = Tool("NextPicturePreview", "\uE76C");
            Label(previous, "上一张 (←)"); Label(next, "下一张 (→)");
            var counter = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            controls.Children.Add(counter);
            previous.Visibility = next.Visibility = counter.Visibility = gallery.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            void Resize()
            {
                overlay.Width = root.Size.Width; overlay.Height = root.Size.Height;
                // The image and its separate tool rail fit both axes. Never enlarge small originals.
                var size = ImageDisplaySize.Fit(source.PixelWidth > 0 ? source.PixelWidth : picture.Width,
                    source.PixelHeight > 0 ? source.PixelHeight : picture.Height,
                    Math.Max(1, root.Size.Width - 116), Math.Max(1, root.Size.Height - 64));
                image.Width = scroll.Width = frame.Width = size.Width;
                image.Height = scroll.Height = frame.Height = size.Height;
            }
            void Select(int index)
            {
                selected = (index + gallery.Length) % gallery.Length; picture = gallery[selected];
                source = CommentPictures.Source(picture); image.Source = source;
                counter.Text = $"{selected + 1}/{gallery.Length}"; scroll.ChangeView(0, 0, 1);
                status.Text = "正在加载图片…"; status.Visibility = source.PixelWidth > 0 ? Visibility.Collapsed : Visibility.Visible;
                Resize();
            }
            previous.Click += (_, _) => Select(selected - 1); next.Click += (_, _) => Select(selected + 1);
            close.Click += (_, _) => popup.IsOpen = false;
            overlay.Tapped += (_, args) => { if (ReferenceEquals(args.OriginalSource, overlay)) popup.IsOpen = false; };
            overlay.KeyDown += (_, args) =>
            {
                if (args.Key == VirtualKey.Escape) { popup.IsOpen = false; args.Handled = true; }
                else if (gallery.Length > 1 && args.Key is VirtualKey.Left or VirtualKey.Right)
                { Select(selected + (args.Key == VirtualKey.Left ? -1 : 1)); args.Handled = true; }
            };
            download.Click += async (_, _) =>
            {
                if (!download.IsEnabled) return;
                var target = picture; download.IsEnabled = false;
                try { await SaveCommentPictureAsync(target, lifetime.Token); }
                catch (OperationCanceledException) { }
                catch (Exception) { ViewModel.Notify("图片未能保存，请稍后重试。", NoticeLevel.Warn); }
                finally { download.IsEnabled = true; }
            };
            void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
            void PageUnloaded(object sender, RoutedEventArgs args) => popup.IsOpen = false;
            image.ImageOpened += (_, _) => { status.Visibility = Visibility.Collapsed; Resize(); };
            image.ImageFailed += (_, _) => status.Text = "图片暂时无法加载，请关闭后重试。";
            Select(selected);
            var closed = new TaskCompletionSource();
            popup.Closed += (_, _) => { lifetime.Cancel(); closed.TrySetResult(); };
            root.Changed += RootChanged; Unloaded += PageUnloaded;
            try { popup.IsOpen = true; close.Focus(FocusState.Programmatic); await closed.Task; }
            finally { root.Changed -= RootChanged; Unloaded -= PageUnloaded; }
        }
        catch (Exception) { ViewModel.Notify("图片暂时无法显示，请稍后重试。", NoticeLevel.Warn); }
        finally
        {
            popup.IsOpen = false; popup.Child = null; lifetime.Cancel(); _commentImageDialogOpen = false;
            if (IsLoaded && focused?.XamlRoot == root) focused.Focus(FocusState.Programmatic);
        }
    }
    private static async Task SaveCommentPictureAsync(CommentPicture picture, CancellationToken ct)
    {
        if (!picture.IsValid) return;
        // Fetch the original, with no account credentials or image transformations, and save its exact bytes.
        var uri = new Uri(picture.OriginalUrl);
#if VISUAL_CHECKS
        if (App.IsVisualCheck) uri = new Uri(Path.Combine(App.VisualCheckDirectory!, "cover.png"));
#endif
        using var original = await RandomAccessStreamReference.CreateFromUri(uri).OpenReadAsync().AsTask(ct);
        const int maxBytes = 20 * 1024 * 1024;
        if (original.Size is 0 or > maxBytes) throw new IOException("Invalid image size");
        var decoder = await BitmapDecoder.CreateAsync(original).AsTask(ct);
        var extension = decoder.DecoderInformation.FileExtensions.FirstOrDefault() ?? ".png";
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, SuggestedFileName = "BiliPlay_image" };
        picker.FileTypeChoices.Add("图片", new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        ct.ThrowIfCancellationRequested();
        original.Seek(0);
        using var input = original.AsStreamForRead();
        var bytes = new byte[checked((int)original.Size)];
        await input.ReadExactlyAsync(bytes, ct);
        await FileIO.WriteBytesAsync(file, bytes);
    }
}
