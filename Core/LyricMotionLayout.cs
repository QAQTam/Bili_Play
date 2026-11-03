namespace BiliPlay.Core;

// Shared by native composition expressions and geometry regression checks.
public static class LyricMotionLayout
{
    public const float MinScale = .92f;
    public const float MaxScale = 1.16f;
    public const float RippleLimit = 8;
    public const double BasePadding = 18;
    public static double Padding(double textHeight) => BasePadding + Math.Max(0, textHeight) * (MaxScale - 1) / 2;
    public static (double Top, double Bottom) Bounds(double textTop, double height, double scale, double ripple, double scroll)
    {
        scale = Math.Clamp(scale, MinScale, MaxScale); ripple = Math.Clamp(ripple, -RippleLimit, RippleLimit);
        var top = textTop + height * (1 - scale) / 2 + ripple + scroll;
        return (top, top + height * scale);
    }
}
