namespace BiliPlay.Services;

/// <summary>Dev-only cold-start trace: one line per marker with the absolute tick count.
/// Gaps between markers measure each startup phase; the first marker compared against
/// Process.StartTime estimates the pre-managed loader cost.</summary>
public static class StartupTrace
{
    public static void Log(string marker)
    {
#if DEVELOPMENT_BUILD
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppProfile.DataDirectoryName);
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "startup-trace.log"),
                $"{marker} tick={Environment.TickCount64} @{DateTimeOffset.Now:HH:mm:ss.fff}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
#endif
    }
}
