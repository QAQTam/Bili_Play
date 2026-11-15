namespace BiliPlay.Core;

public static class AudioQualityPreference
{
    public static bool IsHiRes(string quality) => quality.StartsWith("Hi-Res", StringComparison.Ordinal);
    public static AudioStream Choose(AudioStream available, bool preferHiRes, bool isVip) =>
        IsHiRes(available.Quality) && (!preferHiRes || !isVip) ? available.Fallback ?? available : available;
}
