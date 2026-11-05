using System.Globalization;
using System.Text.Json;

namespace BiliPlay.Core;

public sealed partial class BilibiliClient
{
    /// <summary>读取私信、点赞、回复/@ 和系统通知。接口结构变化时仍尽量保留可识别的嵌套消息。</summary>
    public async Task<IReadOnlyList<AccountNotification>> GetNotificationsAsync(CancellationToken ct = default)
    {
        RequireAccount();
        var requests = new (string Category, string Path, Dictionary<string, string>? Args)[]
        {
            ("私信", "x/msgfeed/session", new() { ["session_type"] = "1", ["group_id"] = "0", ["sort_rule"] = "2" }),
            ("收到的赞", "x/msgfeed/like", new() { ["build"] = "0", ["mobi_app"] = "web" }),
            ("回复与@", "x/msgfeed/reply", new() { ["build"] = "0", ["mobi_app"] = "web" }),
            ("系统通知", "x/sys-msg/query", new() { ["mobi_app"] = "web", ["build"] = "0" })
        };
        var result = new List<AccountNotification>();
        Exception? last = null;
        foreach (var request in requests)
        {
            try
            {
                for (var page = 1; page <= 3; page++)
                {
                    var args = request.Args is null ? new Dictionary<string, string>() : new(request.Args);
                    args["pn"] = page.ToString(CultureInfo.InvariantCulture);
                    args["ps"] = "20";
                    var data = await GetAsync(request.Path, args, false, ct);
                    var pageItems = ParseNotifications(request.Category, data).ToArray();
                    result.AddRange(pageItems);
                    if (!HasMore(data) || pageItems.Length < 20) break;
                }
            }
            catch (Exception error) when (error is BilibiliException or HttpRequestException or InvalidDataException or JsonException or TaskCanceledException)
            { last = error; }
        }
        if (result.Count == 0 && last is not null && !ct.IsCancellationRequested) throw last;
        return result
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) || !string.IsNullOrWhiteSpace(item.Content))
            .GroupBy(item => $"{item.Category}\u001f{item.Title}\u001f{item.Content}\u001f{item.Time}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => ParseTime(item.Time))
            .Take(400)
            .ToArray();
    }

    private static IEnumerable<AccountNotification> ParseNotifications(string category, JsonElement data)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in EnumerateMessageObjects(data))
        {
            var user = FirstObject(item, "user", "sender", "author", "upper");
            var nested = FirstObject(item, "item", "last_msg", "message", "reply");
            var name = FirstText(item, "uname", "name", "username") is { Length: > 0 } direct ? direct : FirstText(user, "uname", "name", "username");
            if (name.Length == 0) name = FirstText(nested, "uname", "name");
            var title = category == "私信" ? (name.Length > 0 ? name : "私信") :
                FirstText(item, "title", "desc", "type_name", "action") is { Length: > 0 } heading ? heading : category;
            var content = FirstText(item, "content", "msg", "text", "message", "desc", "summary");
            if (content.Length == 0) content = FirstText(nested, "title", "content", "desc", "text", "summary");
            var avatar = FirstText(item, "face", "avatar", "upic") is { Length: > 0 } image ? image : FirstText(user, "face", "avatar", "upic");
            if (avatar.Length == 0) avatar = FirstText(nested, "face", "avatar");
            var time = FirstText(item, "ctime", "timestamp", "pub_time", "mtime", "time", "create_time");
            if (time.Length == 0) time = FirstText(nested, "ctime", "timestamp", "pub_time", "mtime", "time");
            var url = FirstText(item, "uri", "url", "link");
            if (url.Length == 0) url = FirstText(nested, "uri", "url", "link");
            if (title.Length == 0 && content.Length == 0) continue;
            var key = $"{category}\u001f{title}\u001f{content}\u001f{time}";
            if (seen.Add(key)) yield return new(category, title, Text.Clean(content), FormatNotificationTime(time), avatar, url);
        }
    }

    private static IEnumerable<JsonElement> EnumerateMessageObjects(JsonElement value, int depth = 0)
    {
        if (depth > 6) yield break;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
                foreach (var item in EnumerateMessageObjects(child, depth + 1)) yield return item;
            yield break;
        }
        if (value.ValueKind != JsonValueKind.Object) yield break;
        if (LooksLikeMessage(value)) yield return value;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                foreach (var item in EnumerateMessageObjects(property.Value, depth + 1)) yield return item;
        }
    }

    private static bool LooksLikeMessage(JsonElement value)
        => new[] { "content", "msg", "text", "title", "desc", "ctime", "timestamp", "pub_time", "mtime", "uname", "user" }
            .Any(name => value.TryGetProperty(name, out var child) && child.ValueKind != JsonValueKind.Null);

    private static JsonElement FirstObject(JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            var child = value.O(name);
            if (child.ValueKind == JsonValueKind.Object) return child;
        }
        return default;
    }

    private static string FirstText(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return "";
        foreach (var name in names)
        {
            if (!value.TryGetProperty(name, out var child) || child.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (child.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                var text = child.ToString();
                if (!string.IsNullOrWhiteSpace(text)) return Text.Clean(text);
            }
            if (child.ValueKind == JsonValueKind.Object)
            {
                var nested = FirstText(child, "text", "content", "title", "desc", "name", "uname");
                if (nested.Length > 0) return nested;
            }
        }
        return "";
    }

    private static bool HasMore(JsonElement data) => data.B("has_more") || data.B("hasMore") || data.N("next_page") > 0;

    private static string FormatNotificationTime(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            if (epoch > 10_000_000_000) epoch /= 1000;
            try { return DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); } catch { }
        }
        return value;
    }

    private static DateTimeOffset ParseTime(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var time) ? time : DateTimeOffset.MinValue;
}
