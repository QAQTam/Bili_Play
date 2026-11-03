using System.Globalization;

namespace BiliPlay.Core;

public static class PlaybackSpeed
{
    public static IReadOnlyList<double> Presets { get; } = Array.AsReadOnly(new[] { .5, .75, 1, 1.25, 1.5, 1.75, 2 });
    public static double Validate(double value) => double.IsFinite(value) && value is >= .5 and <= 2 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public static string Label(double value) => Validate(value).ToString("0.##",CultureInfo.InvariantCulture) + "×";
}
