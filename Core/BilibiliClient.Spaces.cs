using System.Text.Json;

namespace BiliPlay.Core;

public sealed record UserSpace(long Id, string Name, string Avatar, int Level, bool IsVip, int VipType, string Signature)
{
    public string VipText => !IsVip ? "非大会员" : VipType == 2 ? "年度大会员" : "大会员";
    public string UidText => $"UID {Id}";
}
public sealed record DirectMessage(string Id, long Sender, string Text, string Image, long Sequence, string Time, bool IsOwn, string SenderAvatar = "")
{
    public string SenderLabel => IsOwn ? "我" : "对方";
}
public sealed record ConversationPage(IReadOnlyList<DirectMessage> Items, bool HasMore, long BeforeSequence);

public sealed partial class BilibiliClient
{
    public async Task<UserSpace> GetUserSpaceAsync(long user, CancellationToken ct = default)
    {
        RequireAccount();
        if (user <= 0) throw new ArgumentOutOfRangeException(nameof(user));
        JsonElement data;
        try { data = await GetAsync("x/space/wbi/acc/info", new() { ["mid"] = user.ToString() }, true, ct); }
        catch (Exception error) when (error is BilibiliException or HttpRequestException or InvalidDataException)
        { data = (await GetAsync("x/web-interface/card", new() { ["mid"] = user.ToString(), ["photo"] = "1" }, false, ct)).O("card"); }
        if (data.N("mid") != user) throw new InvalidDataException("个人空间返回的用户与所选用户不一致。");
        var vip = data.O("vip");
        return new(user, Text.Clean(data.S("name")), ImageUrl(data.S("face")),
            (int)(data.O("level_info").N("current_level") > 0 ? data.O("level_info").N("current_level") : data.N("level")),
            vip.N("status") == 1 || vip.N("vipStatus") == 1,
            (int)Math.Max(vip.N("type"), vip.N("vipType")), Text.Clean(data.S("sign")));
    }

    public async Task<PageResult<Track>> GetUserWorksAsync(long user, int page, CancellationToken ct = default)
    {
        RequireAccount();
        if (user <= 0 || page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        var data = await GetAsync("x/space/wbi/arc/search", new() {
            ["mid"] = user.ToString(), ["pn"] = page.ToString(), ["ps"] = "30", ["tid"] = "0",
            ["order"] = "pubdate", ["platform"] = "web", ["order_avoided"] = "true"
        }, true, ct);
        var items = data.O("list").O("vlist").A()
            .Where(item => item.S("bvid").Length > 0 && (item.N("mid") == 0 || item.N("mid") == user))
            .Select(item => new Track(item.N("aid"), item.S("bvid"), Text.Clean(item.S("title")),
                Text.Clean(item.S("author")), ImageUrl(item.S("pic")), Text.ParseDuration(item.S("length")), UploaderId: user)).ToArray();
        var total = data.O("page").N("count");
        return new(items, items.Length > 0 && (total > 0 ? (long)page * 30 < total : items.Length == 30));
    }

    public async Task<IReadOnlyDictionary<long, (string Name, string Face)>> GetUserCardsAsync(IReadOnlyCollection<long> uids, CancellationToken ct = default)
    {
        RequireAccount();
        var cards = new Dictionary<long, (string Name, string Face)>();
        if (uids.Count == 0) return cards;
        var data = await GetAsync("x/polymer/pc-electron/v1/user/cards", new() { ["uids"] = string.Join(",", uids), ["build"] = "0", ["mobi_app"] = "web" }, false, ct);
        foreach (var entry in data.EnumerateObject())
            if (entry.Value.ValueKind == JsonValueKind.Object && long.TryParse(entry.Name, out var uid))
            {
                var name = Text.Clean(entry.Value.S("name"));
                var face = ImageUrl(entry.Value.S("face"));
                if (name.Length > 0 || face.Length > 0) cards[uid] = (name, face);
            }
        return cards;
    }

    public async Task<ConversationPage> GetConversationAsync(long talker, long before = 0, CancellationToken ct = default)
    {
        RequireAccount();
        if (talker <= 0 || before < 0) throw new ArgumentOutOfRangeException(nameof(talker));
        var self = Account!.Id;
        var args = new Dictionary<string, string> { ["talker_id"] = talker.ToString(), ["session_type"] = "1", ["size"] = "20", ["build"] = "0", ["mobi_app"] = "web" };
        if (before > 0) args["begin_seqno"] = before.ToString();
        var data = await GetAsync("https://api.vc.bilibili.com/svr_sync/v1/svr_sync/fetch_session_msgs", args, false, ct);
        var messages = new List<DirectMessage>();
        foreach (var row in data.O("messages").A())
        {
            var sender = row.N("sender_uid"); var receiver = row.N("receiver_id"); var sequence = row.N("msg_seqno");
            if (!((sender == self && receiver == talker) || (sender == talker && receiver == self))) continue;
            if (before > 0 && sequence >= before) continue;
            var raw = row.S("content"); var text = ""; var image = "";
            try
            {
                using var content = JsonDocument.Parse(raw);
                text = content.RootElement.S("content", content.RootElement.S("title", content.RootElement.S("text")));
                if (row.N("msg_type") == 2) image = NotificationImage(content.RootElement.S("url"));
            }
            catch (JsonException) { if (row.N("msg_type") == 1) text = raw; }
            if (row.N("msg_type") == 5) text = "[消息已撤回]";
            if (text.Length == 0 && image.Length == 0) text = $"[暂不支持的消息类型 {row.N("msg_type")}]";
            var id = row.S("msg_key", $"{sender}:{sequence}");
            messages.Add(new(id, sender, text, image, sequence,
                FormatNotificationTime(NotificationTimestamp(row.S("timestamp")), ""), sender == self));
        }
        var cursor = data.N("min_seqno");
        if (cursor <= 0) cursor = messages.Select(message => message.Sequence).Where(seq => seq > 0).DefaultIfEmpty(0).Min();
        return new(messages.DistinctBy(message => message.Id).OrderBy(message => message.Sequence).ToArray(),
            data.B("has_more") && cursor > 0 && (before == 0 || cursor < before), cursor);
    }
}
