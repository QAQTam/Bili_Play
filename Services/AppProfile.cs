namespace BiliPlay.Services;

/// <summary>Build-time boundaries between everyday use and development data.</summary>
public static class AppProfile
{
#if DEVELOPMENT_BUILD
    public static bool IsDevelopment => true;
    public static string DisplayName => "Bili Play";
    public static string DataDirectoryName => "BiliPlay.Dev";
    public static string SessionResource => "BiliPlay.BilibiliSession.Development";
    public static string DownloadDirectoryName => "Bili_Play_Dev";
    public static string InstanceScope => "Development";
#else
    public static bool IsDevelopment => false;
    public static string DisplayName => "Bili Play";
    public static string DataDirectoryName => "BiliPlay";
    public static string SessionResource => "BiliPlay.BilibiliSession";
    public static string DownloadDirectoryName => "Bili_Play";
    public static string InstanceScope => "Main";
#endif
}
