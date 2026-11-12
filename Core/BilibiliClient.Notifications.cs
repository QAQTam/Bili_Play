using System.Globalization;
using System.Text.Json;

namespace BiliPlay.Core;

public sealed partial class BilibiliClient
{
    public IReadOnlyList<string> NotificationReadWarnings { get; private set; } = [];

    // These are read-only endpoints used by message.bilibili.com's message-pc
    // client. Each feed has its own cursor; page numbers are not interchangeable.
    public async Task<IReadOnlyList<AccountNotification>> GetNotificationsAsync(CancellationToken ct = default)
    {
        RequireAccount();
        var sources = new[] { "session", "like", "reply", "at", "system", "user-system" };
        var feeds = await Task.WhenAll(sources.Select(source => ReadNotificationSourceAsync(source, ct)));
        ct.ThrowIfCancellationRequested();
        NotificationReadWarnings = feeds.Where(feed => feed.Error.Length > 0).Select(feed => feed.Error).ToArray();
        return feeds.SelectMany(feed => feed.Items)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Timestamp).ThenByDescending(item => item.ActorCount).First())
            .OrderByDescending(item => item.Timestamp).ToArray();
    }

    private async Task<(IReadOnlyList<AccountNotification> Items, string Error)> ReadNotificationSourceAsync(string source, CancellationToken ct)
    {
        var items = new List<AccountNotification>();
        var args = new Dictionary<string, string> { ["build"] = "0", ["mobi_app"] = "web", ["platform"] = "web" };
        var path = source switch
        {
            "session" => "https://api.vc.bilibili.com/session_svr/v1/session_svr/get_sessions",
            "system" => "https://message.bilibili.com/x/sys-msg/query_unified_notify",
            "user-system" => "https://message.bilibili.com/x/sys-msg/query_user_notify",
            _ => "x/msgfeed/" + source
        };
        if (source == "session")
        { args["session_type"] = "1"; args["sort_rule"] = "2"; args["size"] = "20"; }
        else if (source is "system" or "user-system") args["page_size"] = "20";
        string? previousCursor = null;
        try
        {
            for (var page = 0; page < 10; page++)
            {
                ct.ThrowIfCancellationRequested();
                var data = await GetAsync(path, args, false, ct);
                foreach (var row in NotificationRows(source, data))
                {
                    var notification = ParseNotification(source, row);
                    if (notification is not null) items.Add(notification);
                }
                var cursor = source == "like" ? data.O("total").O("cursor") : data.O("cursor");
                string next;
                if (source == "session")
                {
                    if (!data.B("has_more")) break;
                    next = data.O("session_list").A().LastOrDefault().S("session_ts");
                    args["end_ts"] = next;
                }
                else if (source is "system" or "user-system")
                {
                    var rows = NotificationRows(source, data).ToArray();
                    next = rows.LastOrDefault().S("cursor");
                    if (next.Length == 0) break;
                    path = "https://message.bilibili.com/x/sys-msg/query_notify_list";
                    args["cursor"] = next; args["data_type"] = "1";
                }
                else
                {
                    if (cursor.ValueKind != JsonValueKind.Object || cursor.B("is_end")) break;
                    var id = cursor.S("id"); var time = cursor.S("time");
                    if (id.Length == 0 || time.Length == 0) break;
                    next = id + ":" + time;
                    args["id"] = id; args[source + "_time"] = time;
                }
                if (next.Length == 0 || next == previousCursor) break;
                previousCursor = next;
            }
            if (source == "session") await EnrichSessionIdentitiesAsync(items, ct);
            return (items, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is BilibiliException or HttpRequestException or JsonException or InvalidDataException or OperationCanceledException)
        { return (items, source switch { "session" => "私信", "system" or "user-system" => "系统通知", "like" => "点赞", "at" => "@", _ => "回复" }); }
    }

    // get_sessions omits account_info for stranger/unfollowed talkers; the official web
    // batch-resolves those identities via x/polymer/pc-electron/v1/user/cards. Mirror it.
    private async Task EnrichSessionIdentitiesAsync(List<AccountNotification> items, CancellationToken ct)
    {
        var missing = items.Where(item => item.TalkerId > 0 && (item.Title == "UID " + item.TalkerId || item.AvatarOne.Length == 0))
            .Select(item => item.TalkerId).Distinct().ToArray();
        if (missing.Length == 0) return;
        try
        {
            var cards = await GetUserCardsAsync(missing, ct);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!cards.TryGetValue(item.TalkerId, out var card)) continue;
                if (item.Title == "UID " + item.TalkerId && card.Name.Length > 0) item = item with { Title = card.Name };
                if (item.AvatarOne.Length == 0 && card.Face.Length > 0) item = item with { Avatar = card.Face, ActorAvatars = new[] { card.Face } };
                items[i] = item;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is BilibiliException or HttpRequestException or JsonException or InvalidDataException) { }
    }

    private static IEnumerable<JsonElement> NotificationRows(string source, JsonElement data) => source switch
    {
        "session" => data.O("session_list").A(),
        "like" => data.O("latest").O("items").A().Concat(data.O("total").O("items").A()),
        "system" or "user-system" => data.ValueKind == JsonValueKind.Array ? data.A() : data.O("system_notify_list").A(),
        _ => data.O("items").A()
    };

    private static AccountNotification? ParseNotification(string source, JsonElement row)
    {
        var system = source is "system" or "user-system";
        var category = source == "session" ? "私信" : system ? "系统通知" : "回复与@";
        var target = row.O("item");
        var users = row.O("users").A().ToArray();
        if (users.Length == 0 && row.O("user").ValueKind == JsonValueKind.Object) users = [row.O("user")];
        // Live get_sessions rows carry identity in account_info; talker_info is the legacy shape.
        if (users.Length == 0 && row.O("account_info").ValueKind == JsonValueKind.Object) users = [row.O("account_info")];
        if (users.Length == 0 && row.O("talker_info").ValueKind == JsonValueKind.Object) users = [row.O("talker_info")];
        var names = users.Select(user => NotificationText(user, "nickname", "uname", "name")).Where(name => name.Length > 0).Take(2).ToArray();
        var avatars = users.Select(user => NotificationImage(NotificationText(user, "avatar", "face", "pic_url")))
            .Where(url => url.Length > 0).Take(2).ToArray();
        var title = names.Length > 0 ? string.Join("、", names) : source == "session" ? "UID " + row.S("talker_id") : system ? NotificationText(row, "title", "name") : "用户";
        var kind = NotificationText(target, "business");
        if (kind.Length == 0) kind = target.S("type") switch { "danmu" => "弹幕", "reply" => "评论", "video" => "视频", _ => "内容" };
        var count = (int)Math.Clamp(Math.Max(row.N("counts"), users.Length), 1, int.MaxValue);
        var content = source == "session" ? NotificationText(row.O("last_msg"), "content")
            : system ? NotificationText(row, "content", "content_text", "text") : NotificationText(target, "source_content");
        var action = source == "like" ? (NotificationText(row, "action", "action_name").Contains("收藏", StringComparison.Ordinal) ? "收藏了" : "赞了") : "";
        if (source is "reply" or "at") title += (count > 1 ? $" 等共 {count} 人" : "") + (source == "at" ? $" 在{kind}中 @ 了我" : $" 回复了我的{kind}");
        var time = source switch
        {
            "like" => row.S("like_time"), "reply" => row.S("reply_time"), "at" => row.S("at_time"),
            "session" => row.S("session_ts"), _ => NotificationText(row, "time_at", "notify_time", "ctime", "cursor", "time")
        };
        var stamp = NotificationTimestamp(time);
        var id = source == "session" ? row.S("talker_id") : row.S("id");
        // Do not manufacture messages out of nested user/target objects. A row
        // without both an identity and readable content is not a notification.
        if (id.Length == 0 && content.Length == 0) return null;
        var image = kind is "评论" or "弹幕" ? "" : NotificationImage(NotificationText(target, "image", "cover", "pic"));
        var targetText = NotificationText(target, "title", "target_reply_content", "target_content");
        var url = NotificationText(target, "uri", "url");
        var key = system ? "system" : source;
        return new(category, title, content, FormatNotificationTime(stamp, time), avatars.FirstOrDefault() ?? "", url,
            source == "like", image, targetText, kind, count, avatars, action,
            key + ":" + (id.Length > 0 ? id : title + ":" + time), stamp,
            source == "session" ? row.N("talker_id") : 0, source == "session" && row.N("session_type") > 0 ? (int)row.N("session_type") : 1);
    }

    private static string NotificationImage(string value)
    {
        try { var uri = Text.Https(value); return Text.IsMediaHost(uri) ? uri.AbsoluteUri : ""; }
        catch (InvalidDataException) { return ""; }
    }

    private static string NotificationText(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return "";
        foreach (var name in names)
        {
            var node = value.O(name);
            if (node.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) continue;
            var text = node.ToString();
            if (text.Length == 0) continue;
            if (text.StartsWith('{'))
            {
                try
                {
                    using var json = JsonDocument.Parse(text);
                    return NotificationText(json.RootElement, "content", "text", "title");
                }
                catch (JsonException) { }
            }
            return Text.Clean(text);
        }
        return "";
    }

    private static long NotificationTimestamp(string time)
    {
        if (long.TryParse(time, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stamp))
        { while (stamp > 100_000_000_000) stamp /= 1000; return stamp; }
        return DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed.ToUnixTimeSeconds() : 0;
    }
    private static string FormatNotificationTime(long stamp, string fallback)
    {
        try { return stamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(stamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : fallback; }
        catch (ArgumentOutOfRangeException) { return fallback; }
    }
}
