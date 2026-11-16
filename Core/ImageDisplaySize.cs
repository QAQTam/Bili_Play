namespace BiliPlay.Core;

public static class ImageDisplaySize
{
    // Layout changes only: never crop, resample the source, or enlarge a small image.
    public static (double Width, double Height) Fit(double width, double height, double availableWidth, double availableHeight)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0
            || !double.IsFinite(availableWidth) || !double.IsFinite(availableHeight)) return (0, 0);
        var scale = Math.Min(1, Math.Min(Math.Max(0, availableWidth) / width, Math.Max(0, availableHeight) / height));
        return (width * scale, height * scale);
    }
}
