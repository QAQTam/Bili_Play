using System.Runtime.InteropServices.WindowsRuntime;
using BiliPlay.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BiliPlay.Services;

public static class CommentImagePreparation
{
    public static Task<CommentImagePayload> PrepareAsync(StorageFile file, CancellationToken token) => Task.Run(async () =>
    {
        var properties = await file.GetBasicPropertiesAsync().AsTask(token).ConfigureAwait(false);
        if (properties.Size is 0 or > 20 * 1024 * 1024) throw new InvalidDataException("原始图片不能超过 20 MB。");
        using var input = await file.OpenReadAsync().AsTask(token).ConfigureAwait(false);
        if (input.Size is 0 or > 20 * 1024 * 1024) throw new InvalidDataException("原始图片不能超过 20 MB。");
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(token).ConfigureAwait(false);
        if (decoder.PixelWidth is 0 or > CommentImagePayload.MaxDimension || decoder.PixelHeight is 0 or > CommentImagePayload.MaxDimension
            || (long)decoder.PixelWidth * decoder.PixelHeight > CommentImagePayload.MaxPixels)
            throw new InvalidDataException("图片尺寸过大，请缩小图片后添加。");
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token).ConfigureAwait(false);
        using var output = new InMemoryRandomAccessStream();
        var jpeg = decoder.DecoderInformation.CodecId == BitmapDecoder.JpegDecoderId;
        var options = new BitmapPropertySet();
        if (jpeg) options.Add("ImageQuality", new BitmapTypedValue(0.9f, Windows.Foundation.PropertyType.Single));
        var encoder = await BitmapEncoder.CreateAsync(jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId, output, options).AsTask(token).ConfigureAwait(false);
        // Encode pixels into a fresh container; never copy EXIF, GPS, comments or the source filename.
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(token).ConfigureAwait(false);
        if (output.Size > CommentImagePayload.MaxBytes) throw new InvalidDataException("处理后的图片超过 5 MB，请缩小图片后添加。");
        output.Seek(0);
        var bytes = new byte[(int)output.Size];
        using var stream = output.AsStreamForRead();
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var result = new CommentImagePayload(bytes, bitmap.PixelWidth, bitmap.PixelHeight, jpeg ? "image/jpeg" : "image/png"); result.Validate(); return result;
    }, token);
}
