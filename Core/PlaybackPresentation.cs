namespace BiliPlay.Core;

public static class PlaybackPresentation
{
    public static string VolumeGlyph(double volume) => !double.IsFinite(volume) || volume <= 0 ? "\uE74F"
        : volume <= 33 ? "\uE993" : volume <= 66 ? "\uE994" : "\uE995";
    public static DetailLayout Measure(double width, double height)
    {
        width = Math.Max(0, width); height = Math.Max(0, height);
        var padding = width >= 1280 ? 56 : width >= 960 ? 40 : 24;
        var gap = width >= 1280 ? 72 : width >= 960 ? 48 : 28;
        var content = Math.Max(0, width - padding * 2);
        var left = Math.Clamp((content - gap) * .46, 200, 540);
        left = Math.Min(left, Math.Max(0, content - gap - 300));
        var cover = Math.Max(0, Math.Min(Math.Min(left - 16, height - 80), 500));
        return new(padding, gap, left, cover, width < 960 ? 26 : 32);
    }
}
public sealed record DetailLayout(double Padding, double Gap, double LeftWidth, double CoverSize, double LyricFontSize);
