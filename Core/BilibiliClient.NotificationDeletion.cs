using System.Globalization;
namespace BiliPlay.Core;
public sealed partial class BilibiliClient
{
    public static bool CanDeleteNotification(AccountNotification item)
    {
        if (item.Category == "私信") return item.TalkerId > 0 && item.SessionType is 1 or 2;
        var pieces = item.Id.Split(':');
        return pieces.Length == 2 && pieces[0] is "like" or "reply" or "at" && long.TryParse(pieces[1], out var id) && id > 0;
    }
    public async Task DeleteNotificationAsync(AccountNotification item, CancellationToken ct = default)
    {
        RequireAccount();
        if (!CanDeleteNotification(item)) throw new NotSupportedException("这类通知尚无已核验的云端删除接口。");
        var account = Account!.Id;
        if ((await GetAccountAsync(ct)).Id != account) throw new InvalidOperationException("账号已变化，请重新打开通知中心。");
        if (item.Category == "私信")
            _ = await PostAsync("https://api.vc.bilibili.com/session_svr/v1/session_svr/remove_session", new() {
                ["talker_id"] = item.TalkerId.ToString(CultureInfo.InvariantCulture), ["session_type"] = item.SessionType.ToString(CultureInfo.InvariantCulture), ["csrf_token"] = _cookies.GetCookies(Api)["bili_jct"]?.Value ?? "", ["build"] = "0", ["mobi_app"] = "web" }, ct);
        else
        {
            var pieces = item.Id.Split(':');
            _ = await PostAsync("x/msgfeed/del", new() { ["id"] = pieces[1], ["tp"] = pieces[0] switch { "like" => "0", "reply" => "1", _ => "2" }, ["build"] = "0", ["mobi_app"] = "web" }, ct);
        }
    }
}
