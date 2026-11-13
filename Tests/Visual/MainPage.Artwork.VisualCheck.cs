using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using BiliPlay.Core;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyArtworkReplacementAsync(string directory)
    {
        var parent = (Grid)Content;
        var cover = new CenteredCover { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        parent.Children.Add(cover);
        try
        {
            await Task.Delay(20); UpdateLayout();
            var brush = (ImageBrush)cover.Background;
            var first = new WriteableBitmap(8, 8);
            using (var pixelStream = first.PixelBuffer.AsStream())
                await pixelStream.WriteAsync(Enumerable.Range(0, 64).SelectMany(_ => new byte[] { 0, 0, 255, 255 }).ToArray());
            first.Invalidate();
            cover.Source = first;
            Check(ReferenceEquals(brush.ImageSource, first), "初始封面没有显示");
            await AssertColorAsync(cover, 2, "初始红色封面实际未显示");

            // Match the real disk-cache path: UriSource arrives later, and WinUI
            // must perform its own decode. SetSourceAsync masks a detached brush.
            var cachedPath = Path.Combine(directory, "uri-cover.img");
            using (var output = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 8, 8, 96, 96,
                    Enumerable.Range(0, 64).SelectMany(_ => new byte[] { 0, 255, 0, 255 }).ToArray());
                await encoder.FlushAsync(); output.Seek(0);
                using var file = File.Create(cachedPath); await output.AsStreamForRead().CopyToAsync(file);
            }
            var automatic = new BitmapImage { DecodePixelWidth = 48 };
            cover.Source = automatic;
            await Task.Delay(20);
            var placeholder = new RenderTargetBitmap(); await placeholder.RenderAsync(cover);
            var emptyPixels = (await placeholder.GetPixelsAsync()).ToArray();
            var emptyCenter = ((placeholder.PixelHeight / 2) * placeholder.PixelWidth + placeholder.PixelWidth / 2) * 4;
            Check(emptyPixels[emptyCenter + 3] == 0, "新封面尚未加载时仍渲染了上一首的红色封面");
            automatic.UriSource = new Uri(cachedPath);
            for (var attempt = 0; attempt < 100 && automatic.PixelWidth == 0; attempt++) await Task.Delay(20);
            Check(automatic.PixelWidth > 0 && ReferenceEquals(brush.ImageSource, automatic), "缓存 URI 封面没有自然解码；等待 ImageOpened 前必须先挂到画刷");
            await AssertColorAsync(cover, 1, "封面对象已绑定但实际渲染像素仍为空白或错误图片");
            var shared = new CenteredCover { Width = 48, Height = 48, Source = automatic };
            parent.Children.Add(shared);
            Check(ReferenceEquals(((ImageBrush)shared.Background).ImageSource, automatic), "同一缓存图片在第二个控件未显示");
            parent.Children.Remove(shared);
            var cacheHit = BiliPlay.Services.ArtworkCache.Create(new Uri(cachedPath), 48);
            cover.Source = cacheHit;
            for (var attempt = 0; attempt < 100 && cacheHit.PixelWidth == 0; attempt++) await Task.Delay(20);
            Check(cacheHit.PixelWidth > 0 && ReferenceEquals(brush.ImageSource, cacheHit), "预先设置 URI 的实际缓存图片未解码");

            // The replacement CDN format must also render through the production
            // .img disk-cache path, without relying on a WebP extension.
            var jpegPath = Path.Combine(directory, "native-jpeg-cover.img");
            using (var output = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 8, 8, 96, 96,
                    Enumerable.Range(0, 64).SelectMany(_ => new byte[] { 255, 0, 0, 255 }).ToArray());
                await encoder.FlushAsync(); output.Seek(0);
                using var file = File.Create(jpegPath); await output.AsStreamForRead().CopyToAsync(file);
            }
            var jpeg = BiliPlay.Services.ArtworkCache.Create(new Uri(jpegPath), 48);
            cover.Source = jpeg;
            await AssertColorAsync(cover, 0, "JPEG 缓存封面没有实际渲染");
            cover.Source = BiliPlay.Services.ArtworkCache.CreateAvatar(new Uri(cachedPath), 48);
            await AssertColorAsync(cover, 1, "PNG 头像缓存没有实际渲染");
            var supportsWebp = await BiliPlay.Services.ImageCodecSupport.SupportsWebpAsync();
            Check(supportsWebp == ArtworkFormats.WebpSupported, "启动未应用系统的实际 WebP 解码检测结果");
            if (supportsWebp)
            {
                var webpPath = Path.Combine(directory, "native-webp-cover.img");
                await File.WriteAllBytesAsync(webpPath, Convert.FromBase64String(BiliPlay.Services.ImageCodecSupport.WebpSample));
                var webp = BiliPlay.Services.ArtworkCache.Create(new Uri(webpPath), 48);
                cover.Source = webp;
                for (var attempt = 0; attempt < 100 && webp.PixelWidth == 0; attempt++) await Task.Delay(20);
                Check(webp.PixelWidth == 1 && webp.PixelHeight == 1, "WebP 探测通过但真实封面路径未解码");
            }

            var slow = new BitmapImage(); var selected = new BitmapImage();
            cover.Source = slow;
            Check(ReferenceEquals(brush.ImageSource, slow), "新封面没有接入画刷或仍显示上一首歌曲的封面");
            cover.Source = selected;
            await DecodeAsync(slow);
            Check(ReferenceEquals(brush.ImageSource, selected), "旧封面延迟加载后覆盖了新曲目");
            await DecodeAsync(selected);
            Check(ReferenceEquals(brush.ImageSource, selected), "当前曲目的封面解码后没有显示");

            var broken = new BitmapImage(); cover.Source = broken;
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            broken.ImageFailed += (_, _) => failed.TrySetResult();
            broken.UriSource = new Uri(Path.Combine(directory, "missing-cover.png"));
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(brush.ImageSource is null, "封面加载失败后仍残留其他歌曲的封面");

            var canceled = new BitmapImage(); cover.Source = canceled; cover.Source = null;
            await DecodeAsync(canceled);
            Check(brush.ImageSource is null, "清空歌曲后旧请求重新显示了封面");

            var recycled = new BitmapImage(); cover.Source = recycled;
            parent.Children.Remove(cover); await Task.Delay(20);
            await DecodeAsync(recycled);
            Check(brush.ImageSource is null, "已经回收的封面控件仍然处理旧图片回调");
            parent.Children.Add(cover); await Task.Delay(20); UpdateLayout();
            Check(ReferenceEquals(brush.ImageSource, recycled), "回收控件重新加载后未恢复当前封面");
        }
        finally { parent.Children.Remove(cover); }

        async Task DecodeAsync(BitmapImage bitmap)
        {
            using var stream = await (await StorageFile.GetFileFromPathAsync(Path.Combine(directory, "cover.png"))).OpenReadAsync();
            await bitmap.SetSourceAsync(stream);
            await Task.Delay(20);
        }
        async Task AssertColorAsync(CenteredCover target, int channel, string message)
        {
            byte[] sample = [];
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(20); UpdateLayout();
                var rendered = new RenderTargetBitmap(); await rendered.RenderAsync(target);
                var pixels = (await rendered.GetPixelsAsync()).ToArray();
                var center = ((rendered.PixelHeight / 2) * rendered.PixelWidth + rendered.PixelWidth / 2) * 4;
                sample = pixels.Skip(center).Take(4).ToArray();
                if (sample.Length == 4 && sample[channel] > 200 && sample[3] > 200 &&
                    Enumerable.Range(0, 3).Where(index => index != channel).All(index => sample[index] < 20)) return;
            }
            Check(false, $"{message}: BGRA={string.Join(',', sample)}, size={target.ActualWidth}x{target.ActualHeight}, visible={target.Visibility}");
        }
    }

    // Capacity eviction must drop only the least-recently-used cover, never the whole
    // cache: wiping everything at 256 made fast scrolling re-decode every cover (blank flashes).
    private void VerifyArtworkCacheLru(string directory)
    {
        Services.ArtworkCache.ResetScope();
        try
        {
            Uri Key(int i) => new Uri(Path.Combine(directory, $"lru-probe-{i}.png"));
            var hot = Services.ArtworkCache.Create(Key(0), 48);
            var cold = Services.ArtworkCache.Create(Key(1), 48);
            for (var i = 2; i <= 255; i++) Services.ArtworkCache.Create(Key(i), 48);
            Services.ArtworkCache.Create(Key(0), 48);   // touch hot -> most recently used
            Services.ArtworkCache.Create(Key(256), 48); // overflow evicts the LRU entry (cold), keeps hot
            Check(ReferenceEquals(hot, Services.ArtworkCache.Create(Key(0), 48)), "满容量淘汰整表清空了最近使用的封面");
            Check(!ReferenceEquals(cold, Services.ArtworkCache.Create(Key(1), 48)), "LRU 未淘汰最久未用的封面");
        }
        finally { Services.ArtworkCache.ResetScope(); }
    }
}
