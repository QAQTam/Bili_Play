namespace BiliPlay.Core;

// One atomic snapshot shared by URL generation, prefetching and the disk cache.
// Before the asynchronous system probe finishes, use built-in image formats.
public static class ArtworkFormats
{
    private static int _flags;
    public static bool WebpSupported => (Volatile.Read(ref _flags) & 1) != 0;
    public static bool CompatibilityMode => (Volatile.Read(ref _flags) & 2) != 0;
    public static bool UseWebp => Volatile.Read(ref _flags) == 1;
    public static void Configure(bool webpSupported, bool compatibilityMode) =>
        Volatile.Write(ref _flags, (webpSupported ? 1 : 0) | (compatibilityMode ? 2 : 0));
}
