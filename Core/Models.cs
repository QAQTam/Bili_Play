using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BiliPlay.Core;

public sealed record Account(long Id, string Name, string Avatar, int Level = 0, bool IsVip = false);
public sealed record Uploader(long Id, string Name, string Avatar, string Signature, long Fans, long Videos)
{
    public string Detail => $"{Fans:N0} 粉丝 · {Videos:N0} 个视频";
    public string Url => $"https://space.bilibili.com/{Id}";
}
public sealed record SocialUser(long Id, string Name, string Avatar, string Signature)
{
    public string Url => $"https://space.bilibili.com/{Id}";
}
public sealed record LoginCode(string Url, string Key);
public sealed record Folder(long Id, string Title, int Count, bool Owned, bool IsPrivate = false,
    string Intro = "", string Cover = "", string CoverBack = "", string CoverMiddle = "")
{
    public string Detail => $"{Count} 个视频 · {(Owned ? IsPrivate ? "私密" : "公开" : "订阅 · 只读")}";
    public bool HasContents => Count > 0;
    public string FrontImage => HasContents ? Thumbnail(Cover) : "";
    public string MiddleImage => Thumbnail(string.IsNullOrEmpty(CoverMiddle) ? Cover : CoverMiddle);
    public string BackImage => Thumbnail(string.IsNullOrEmpty(CoverBack) ? Cover : CoverBack);
    private static string Thumbnail(string image) => string.IsNullOrEmpty(image) ? "" : Text.CoverImage(image, 640);
    public static bool IsPrivateAttribute(long attributes) => (attributes & 1) != 0;
    public override string ToString() => Title;
}
public sealed record Track(long Aid, string Bvid, string Title, string Uploader, string Cover,
    double Duration, long Cid = 0, int Page = 1, string Part = "", int PartCount = 1, long UploaderId = 0, string LocalFile = "")
{
    public string UploaderDisplay => string.IsNullOrWhiteSpace(Uploader) ? "UP 主信息暂不可用" : Uploader;
    public string DurationText => Text.FormatTime(Duration);
    public string Thumbnail => string.IsNullOrEmpty(Cover) ? "" : Text.CoverImage(Cover, 128);
    public string CardCover => string.IsNullOrEmpty(Cover) ? "" : Text.CoverImage(Cover, 480);
    public string Artwork => string.IsNullOrEmpty(Cover) ? "" : Text.CoverImage(Cover, 960);
    public string Detail => PartCount <= 1 || string.IsNullOrEmpty(Part) ? Uploader : $"{Uploader} · P{Page} {Part}";
    public string Key => $"{Bvid}:{Cid}";
    public override string ToString() => Title;
}
public sealed record Video(Track Track, IReadOnlyList<Track> Parts, int CategoryId = 0, string Category = "", IReadOnlyList<Track>? Collection = null, long CollectionId = 0, string CollectionTitle = "");
public sealed record PageResult<T>(IReadOnlyList<T> Items, bool HasMore);
public sealed record AudioStream(Uri Url, string MimeType, IReadOnlyList<Uri> Backups, string Quality = "标准", AudioStream? Fallback = null, bool HasHiRes = false);
public sealed record SubtitleTrack(string Language, string Label, Uri Url, bool IsAuto, int? AiType = null)
{
    private bool GeneratedOrigin => Language.StartsWith("ai-", StringComparison.OrdinalIgnoreCase) || AiType is > 0
        || AiType is null && IsAuto && Regex.IsMatch(Label, "AI|自动|生成|机器|语音识别", RegexOptions.IgnoreCase);
    public SubtitleTrack NormalizeOrigin() => this with { IsAuto = GeneratedOrigin };
    public string DisplayName => $"{Label}{(GeneratedOrigin ? " · AI" : "")}";
    public override string ToString() => DisplayName;
}
public sealed record SubtitleLine(double From, double To, string Content)
{
    public string TimeText => Text.FormatTime(From);
    public override string ToString() => Content;
}
public sealed record SavedCookie(string Name, string Value, string Domain, string Path, DateTime Expires);

public static partial class Text
{
    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
    [GeneratedRegex(@"^BV[0-9A-Za-z]{10}$", RegexOptions.IgnoreCase)]
    private static partial Regex BvidPattern();

    public static string CoverImage(string value, int width)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (Uri.TryCreate(value,UriKind.Absolute,out var local) && local.IsFile) return value;
        return value.Split('@')[0] + $"@{width}w.webp";
    }
    public static string Clean(string value) => WebUtility.HtmlDecode(Tags().Replace(value, "")).Trim();
    public static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
    public static double ParseDuration(string value)
    {
        double result = 0;
        foreach (var piece in value.Split(':'))
        {
            if (!int.TryParse(piece, out var n) || n < 0) return 0;
            result = result * 60 + n;
        }
        return result;
    }
    public static (string Bvid, int Page)? ParseVideoInput(string input)
    {
        input = input.Trim();
        if (BvidPattern().IsMatch(input)) return ("BV" + input[2..], 1);
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !(uri.Host == "bilibili.com" || uri.Host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase))) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[0] != "video" || !BvidPattern().IsMatch(segments[1])) return null;
        var query = ParseQuery(uri.Query);
        var page = query.TryGetValue("p", out var p) && int.TryParse(p, out var n) ? Math.Max(1, n) : 1;
        return ("BV" + segments[1][2..], page);
    }
    public static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).GroupBy(p => Uri.UnescapeDataString(p[0]))
        .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.Last().ElementAtOrDefault(1) ?? ""));
    public static Uri Https(string value)
    {
        if (value.StartsWith("//")) value = "https:" + value;
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value[7..];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("服务器返回了无效的资源地址。");
        return uri;
    }
    public static bool IsMediaHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        new[] { "bilivideo.com", "bilivideo.cn", "akamaized.net", "bilibili.com", "hdslb.com" }
        .Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
}

internal static class Json
{
    public static string S(this JsonElement e, string name, string fallback = "") =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : fallback;
    public static long N(this JsonElement e, string name) => long.TryParse(e.S(name), out var v) ? v : 0;
    public static JsonElement O(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    public static IEnumerable<JsonElement> A(this JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];
    public static bool B(this JsonElement e, string name) => e.O(name).ValueKind == JsonValueKind.True || e.N(name) == 1;
}
