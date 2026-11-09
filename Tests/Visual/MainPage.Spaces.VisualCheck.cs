using System.Net;
using System.Text;
using BiliPlay.Core;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifySpacesAndConversationsAsync(string directory)
    {
        var handler = new SpaceFixtureHandler();
        using (var model = new PlayerViewModel(new BilibiliClient(handler)))
        {
            model.Client.RestoreCachedAccount(new(123, "离线空间测试", "")); model.IsLoggedIn = true;
            model.Client.ImportSession([new("bili_jct","fixture-csrf",".bilibili.com","/",DateTime.UtcNow.AddDays(1))]);
            model.SpaceRelationReader = model.Client.IsFollowingAsync;
            await model.OpenSpaceAsync(42);
            Check(model.IsSpaceView && model.SpaceProfile is { Id:42, Level:6, IsVip:true, VipType:2 } && model.Tracks.Count >= 1, "个人空间资料或作品缺失");
            Check(model.SpaceRelationReady && model.IsSpaceFollowing && model.SpaceFollowLabel == "取消关注", "未读取已关注状态");
            await model.ToggleSpaceFollowCommand.ExecuteAsync(null);
            Check(!model.IsSpaceFollowing && model.SpaceFollowLabel == "关注" && handler.RelationWrites == 1, "取消关注后按钮或服务状态未更新");
            await model.ToggleSpaceFollowCommand.ExecuteAsync(null);
            Check(model.IsSpaceFollowing && model.SpaceFollowLabel == "取消关注" && handler.RelationWrites == 2, "关注后按钮未切换为取消关注");
            var loaded = model.Tracks.Count;
            if (model.HasMore) await model.Tracks.LoadPageAsync();
            Check(model.Tracks.Count > loaded && !model.HasMore, "个人空间作品没有继续分页");
            await model.OpenSpaceAsync(43); model.NavigateBack();
            Check(model.IsSpaceView && model.SpaceProfile?.Id == 42 && model.Tracks.Count >= 2, "返回个人空间后资料或作品错配");
            var old = model.OpenSpaceAsync(44);
            await model.OpenSpaceAsync(43); await old;
            Check(model.SpaceProfile?.Id == 43 && model.Tracks.All(track => track.UploaderId == 43), "迟到的空间请求覆盖了新用户");
            await model.OpenSpaceAsync(123);
            Check(!model.CanFollowSpace && !model.CanToggleSpaceFollow, "自己的主页仍然可以关注自己");
            await model.ToggleSpaceFollowCommand.ExecuteAsync(null);
            Check(handler.RelationWrites == 2, "自己的主页意外提交了关注请求");
        }
        ViewModel.Navigate("space");
        ViewModel.SpaceProfile = new(42, "离线个人空间", "", 6, true, 2, "离线界面验证");
        ViewModel.Tracks.Clear(); ViewModel.Tracks.Add(new(71,"BV1xx411c7mD","全部分区的公开作品","离线个人空间","",30,UploaderId:42));
        ViewModel.IsEmpty = false;
        await Task.Delay(450); UpdateLayout();
        Check(SpaceHeader.Visibility == Visibility.Visible && SpaceHeader.ActualHeight >= 88 && TrackList.Visibility == Visibility.Visible, "个人空间头部或作品列表未显示");
        Check(ViewModel.SpaceDetails.Contains("年度大会员") && ViewModel.SpaceDetails.Contains("UID 42"), "个人空间会员等级或 UID 缺失");
        Check(SpaceFollowButton.Visibility == Visibility.Visible && (string)SpaceFollowButton.Content == "关注", "主页关注按钮缺失");
        var row = TrackList.ContainerFromIndex(0) as DependencyObject;
        var artwork = row is null ? null : FindVisual<CenteredCover>(row);
        Check(artwork is not null && Math.Abs(artwork.TransformToVisual(BrowseSurface).TransformPoint(new(0,0)).X - SpaceHeader.Margin.Left) <= 12, "横条封面没有与个人空间页头左侧对齐");
        SetBrowseMode(true); await Task.Delay(450); UpdateLayout();
        Check(ViewModel.CanChooseView && RecommendationGrid.Visibility == Visibility.Visible && !ViewModel.IsTrackListVisible, "主页不能切换图标视图");
        await SaveVisualAsync(Path.Combine(directory, "personal-space-grid.png"));
        SetBrowseMode(false); await Task.Delay(450); UpdateLayout();
        Check(TrackList.Visibility == Visibility.Visible && ViewModel.Store.GetSetting("spaceGrid", "true") == "false", "主页列表视图未恢复或未保存选择");
        await SaveVisualAsync(Path.Combine(directory, "personal-space.png"));
        var notification = new AccountNotification("私信", "离线私信", "", "", TalkerId:42);
        var conversation = ShowConversationAsync(notification, (before, token) => Task.FromResult(new ConversationPage([
            new("one",42,"对话中的 <原文> 不应丢失","",1,"2026-11-09 12:00",false),
            new("two",123,"自己的消息","",2,"2026-11-09 12:01",true)],false,1)));
        await Task.Delay(180);
        var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(popup => FindVisual<ContentDialog>(popup.Child)).FirstOrDefault(value=>value is not null);
        Check(dialog is not null && FindVisual<ListView>(dialog!)?.Items.Count == 2, "点击私信未载入对话");
        await SaveVisualAsync(Path.Combine(directory, "private-conversation.png"));
        dialog!.Hide(); await conversation;
        Check(!_conversationOpen, "关闭私信后会话没有释放");
        var pending = ShowConversationAsync(notification, async (_, token) => { await Task.Delay(10000, token); return new([],false,0); });
        await Task.Delay(80);
        dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(popup=>FindVisual<ContentDialog>(popup.Child)).FirstOrDefault(value=>value is not null);
        Check(dialog is not null, "加载期间没有显示私信窗口"); dialog!.Hide();
        await pending.WaitAsync(TimeSpan.FromSeconds(2)); Check(!_conversationOpen, "取消私信加载未终止请求");
        ViewModel.Navigate("home");
    }

    private sealed class SpaceFixtureHandler : HttpMessageHandler
    {
        public int RelationWrites { get; private set; }
        private readonly HashSet<long> _following = [42];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post)
            {
                var form = Text.ParseQuery(await request.Content!.ReadAsStringAsync(token));
                if (form["csrf"] != "fixture-csrf") throw new InvalidOperationException("关注请求缺少 CSRF");
                var userId = long.Parse(form["fid"]); RelationWrites++;
                if (form["act"] == "1") _following.Add(userId); else _following.Remove(userId);
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"code\":0,\"data\":{}}", Encoding.UTF8, "application/json") };
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/relation"))
            {
                var id = long.Parse(Text.ParseQuery(request.RequestUri.Query)["fid"]);
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"code\":0,\"data\":{\"attribute\":" + (_following.Contains(id) ? "2" : "0") + "}}", Encoding.UTF8, "application/json") };
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/nav"))
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"code":0,"data":{"isLogin":true,"mid":123,"uname":"离线空间测试","wbi_img":{"img_url":"https://i0.hdslb.com/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png","sub_url":"https://i0.hdslb.com/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.png"}}}""",Encoding.UTF8,"application/json") };
            var query = Text.ParseQuery(request.RequestUri!.Query); var user = query.GetValueOrDefault("mid", "42");
            if (user == "44") await Task.Delay(100, token);
            var json = request.RequestUri.AbsolutePath.EndsWith("/info")
                ? """{"code":0,"data":{"mid":USER,"name":"离线用户","level":6,"vip":{"status":1,"type":2}}}""".Replace("USER", user)
                : """{"code":0,"data":{"page":{"count":31},"list":{"vlist":[{"aid":71,"bvid":"BVID","title":"公开作品","mid":USER,"author":"离线用户","length":"1:00"}]}}}""".Replace("USER",user).Replace("BVID",query.GetValueOrDefault("pn")=="2"?"BV2xx411c7mD":"BV1xx411c7mD");
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
