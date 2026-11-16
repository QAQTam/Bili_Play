using System.Globalization;

namespace BiliPlay.Core;

public static class StorageSize
{
    public static string Format(long bytes)
    {
        double value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        // Promote rounded boundaries too, so 1024 KB is displayed as 1 MB.
        if (unit < units.Length - 1 && Math.Round(value, 1) >= 1024) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
