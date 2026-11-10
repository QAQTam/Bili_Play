using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace BiliPlay.Services;

internal static class ImageCodecSupport
{
    // A 1x1 WebP fixture: capability checks never contact the network or read user files.
    internal const string WebpSample = "UklGRigAAABXRUJQVlA4IBwAAABwAQCdASoBAAEAAgA0JZwCdAFAAAD++KVULvQA";
    private static readonly Lazy<Task<bool>> WebpProbe = new(() => Task.Run(ProbeWebpAsync));
    public static async Task<bool> SupportsWebpAsync()
    {
        try { return await WebpProbe.Value.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
    }
    private static async Task<bool> ProbeWebpAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var bytes = new MemoryStream(Convert.FromBase64String(WebpSample));
            using var stream = bytes.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(timeout.Token).ConfigureAwait(false);
            var pixels = await decoder.GetPixelDataAsync().AsTask(timeout.Token).ConfigureAwait(false);
            return decoder.PixelWidth == 1 && decoder.PixelHeight == 1 && pixels.DetachPixelData().Length > 0;
        }
        catch (Exception) { return false; }
    }
}
