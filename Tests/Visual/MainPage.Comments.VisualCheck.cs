using BiliPlay.Core;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyCommentsAndSpeedAsync(string directory)
    {
        static async Task WaitUntil(Func<bool> ready)
        {
            for(var i=0;i<80&&!ready();i++) await Task.Delay(25);
            Check(ready(),"评论操作未完成");
        }
        static bool ContainsText(DependencyObject node,string value)
        {
            if(node is TextBlock text&&text.Text==value) return true;
            if(node is CommentBody body&&body.Comment?.Content==value) return true;
            for(var child=0;child<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);child++)
                if(ContainsText(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node,child),value)) return true;
            return false;
        }
        var savedRate=ViewModel.PlaybackRate;
        var calls=new List<(string Video,CommentSort Sort,int Page)>();
        var posted=false; var headId=0L; var posts=0;
        var song=new Track(71,"BV7xx411c7mD","评论面板验证","离线 UP","",240,71);
        var other=song with { Aid=72,Bvid="BV8xx411c7mD",Title="新的评论上下文" };
        try
        {
            ViewModel.Queue.Add(song); ViewModel.Navigate("playlist");
            ViewModel.CommentsReader=(track,sort,page,token)=>
            {
                calls.Add((track.Bvid,sort,page));
                var start=sort==CommentSort.Hot?1000:2000;
                var rows=Enumerable.Range(page==1?0:19,page==1?20:2).Select(i=>new BiliComment(start+i,123,$"用户 {i}","","这是一条离线评论，包含多行文本以检查侧栏排版。",3,1700000000+i,ReplyCount:21)).ToList();
                if(page==1&&headId>0) {rows.Insert(0,new(headId,123,"新评论","","自动刷新新增评论",0,1700000100));rows=rows.Take(20).ToList();}
                if(page==1&&posted) {rows.Insert(0,new(9999,123,"测试账号","","界面发送验证",0,1700000200));rows=rows.Take(20).ToList();}
                return Task.FromResult(new CommentPage(rows,21+(headId>0?1:0)+(posted?1:0),page==1));
            };
            ViewModel.CommentSortIndex=0; ViewModel.ShowComments=true;
            await WaitUntil(()=>!ViewModel.IsCommentsLoading&&ViewModel.Comments.Count==20); await Task.Delay(250); UpdateLayout();
            Check(QueueHost.IsPaneOpen&&ViewModel.IsCommentsPane&&!ViewModel.ShowQueue,"评论没有作为独立悬浮面板打开");
            Check(CommentList.ContainerFromIndex(0) is ListViewItem item&&FindVisual<PersonPicture>(item) is not null,"评论作者头像未渲染");
            Check(!SendCommentButton.IsEnabled,"空评论允许发送");
            await ViewModel.Comments.LoadPageAsync(); Check(ViewModel.Comments.Count==21,"评论分页没有去重");
            headId=6000; await ViewModel.RefreshCommentHeadAsync();
            Check(ViewModel.Comments[0].Id==6000&&ViewModel.Comments.Any(comment=>comment.Id==1020),"实时刷新丢失了已加载的后续评论");
            CommentSortPicker.SelectedIndex=1;
            await WaitUntil(()=>!ViewModel.IsCommentsLoading&&ViewModel.Comments.Any(comment=>comment.Id==2000));
            Check(calls.Last().Sort==CommentSort.Latest&&calls.Last().Page==1,"切换最新没有重新请求第一页");
            var root=ViewModel.Comments.First(comment=>comment.HasReplies);
            var votes=0;var hold=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ViewModel.CommentReactionWriter=async (_,id,reaction,enabled,token)=>{votes++;await hold.Task.WaitAsync(token);};
            var voting=ViewModel.ReactToCommentAsync(root,CommentReaction.Like);
            Check(root.IsReacting&&!root.CanReact&&!root.IsLiked,"点赞尚未确认就改变状态或没有禁用");
            await ViewModel.ReactToCommentAsync(root,CommentReaction.Like);Check(votes==1,"重复提交了点赞");
            hold.TrySetResult();await voting;Check(root.IsLiked&&root.Data.Likes==4,"点赞没有更新计数");
            await ViewModel.ReactToCommentAsync(root,CommentReaction.Dislike);Check(root.IsDisliked&&!root.IsLiked&&root.Data.Likes==3,"点踩没有取消点赞");
            await ViewModel.ReactToCommentAsync(root,CommentReaction.Dislike);Check(!root.IsDisliked&&root.Data.Reaction==CommentReaction.None,"无法取消点踩");
            ViewModel.CommentReactionWriter=(_,_,_,_,_)=>throw new HttpRequestException("模拟投票失败");
            await ViewModel.ReactToCommentAsync(root,CommentReaction.Like);Check(!root.IsLiked&&!root.IsReacting,"投票失败仍显示成功");
            ViewModel.CommentReactionWriter=(_,_,_,_,_)=>Task.CompletedTask;
            var replyPages=new List<int>();
            ViewModel.CommentRepliesReader=(_,id,page,_)=>
            {
                Check(id==root.Id,"二级评论请求了错误的根评论");replyPages.Add(page);
                var rows=Enumerable.Range(page==1?1:20,page==1?20:2).Select(i=>new BiliComment(3000+i,123,"回复用户","",$"第 {i} 条二级评论",0,1700000000+i)).ToArray();
                return Task.FromResult(new CommentPage(rows,21,page==1));
            };
            await ViewModel.ToggleCommentRepliesAsync(root);Check(root.IsExpanded&&root.Replies.Count==20,"二级评论未展开和加载");
            CommentList.ScrollIntoView(root);await Task.Delay(120);UpdateLayout();
            Check(CommentList.ContainerFromItem(root) is ListViewItem replyContainer&&ContainsText(replyContainer,"第 1 条二级评论"),"二级回复没有实际显示在父评论下方");
            ViewModel.Notifications.Clear();await SaveVisualAsync(System.IO.Path.Combine(directory,"comment-replies.png"));
            await ViewModel.LoadCommentRepliesAsync(root);Check(root.Replies.Count==21&&!root.CanLoadReplies&&replyPages.SequenceEqual(new[]{1,2}),"二级评论分页去重或结束判断错误");
            await ViewModel.ReactToCommentAsync(root.Replies[0],CommentReaction.Like);Check(root.Replies[0].IsLiked,"二级评论无法点赞");
            var child=root.Replies[0];Check(child.RootId==root.Id,"二级回复丢失根评论关联");
            ViewModel.BeginCommentReply(root);Check(ViewModel.CommentReplyTarget==root&&ViewModel.CommentEditorPlaceholder.Contains(root.Author),"一级评论无法选为回复对象");
            ViewModel.CancelCommentReply();Check(!ViewModel.HasCommentReplyTarget,"取消回复没有恢复普通评论");
            ViewModel.BeginCommentReply(child);ViewModel.CommentDraft="跟评验证";
            ViewModel.CommentReplySender=(track,message,rootId,parentId,token)=>
            {Check(rootId==root.Id&&parentId==child.Id&&track.Bvid==song.Bvid,"跟评提交目标错误");return Task.FromResult<BiliComment?>(new(9000,123,"测试用户","",message,0,1700000200));};
            await ViewModel.SendCommentCommand.ExecuteAsync(null);
            Check(!ViewModel.HasCommentReplyTarget&&root.Replies.Any(reply=>reply.Id==9000)&&!ViewModel.Comments.Any(reply=>reply.Id==9000),"跟评没有插入原评论下或错误插入顶层");
            ViewModel.CommentReplySender=null;

            await ViewModel.ToggleCommentRepliesAsync(root);Check(!root.IsExpanded,"二级评论未收起");
            await ViewModel.ToggleCommentRepliesAsync(root);Check(root.Replies.Count==22&&replyPages.Count==2,"展开已载入回复重复请求");
            await ViewModel.ToggleCommentRepliesAsync(root);
            // A polling result started before a successful vote must not undo that vote.
            var normalReader=ViewModel.CommentsReader;var pendingHead=new TaskCompletionSource<CommentPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stale=new CommentPage(ViewModel.Comments.Select(item=>item.Data).ToArray(),21,false);
            ViewModel.CommentsReader=(_,_,_,_)=>pendingHead.Task;
            var refreshDuringVote=ViewModel.RefreshCommentHeadAsync();
            await ViewModel.ReactToCommentAsync(root,CommentReaction.Like);pendingHead.TrySetResult(stale);await refreshDuringVote;
            Check(root.IsLiked,"较早的轮询结果覆盖了已成功的点赞");ViewModel.CommentsReader=normalReader;
            // Closing the pane cancels reply reads, without leaking a stale result into another song.
            var replyRoot=ViewModel.Comments.First(item=>item.HasReplies&&item.Id!=root.Id);
            var replyStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var replyCanceled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var normalReplies=ViewModel.CommentRepliesReader;
            ViewModel.CommentRepliesReader=async (_,_,_,token)=>{replyStarted.TrySetResult();try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){replyCanceled.TrySetResult();throw;}return new CommentPage([],0,false);};
            var pendingReplies=ViewModel.ToggleCommentRepliesAsync(replyRoot);await replyStarted.Task;
            ViewModel.ShowComments=false;await replyCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));await pendingReplies;
            Check(!replyRoot.IsRepliesLoading&&replyRoot.Replies.Count==0,"关闭后回复请求未停止或写入了过期内容");
            ViewModel.CommentRepliesReader=normalReplies;ViewModel.ShowComments=true;await WaitUntil(()=>!ViewModel.IsCommentsLoading);
            ViewModel.CommentEmotesReader=_=>Task.FromResult<IReadOnlyList<CommentEmotePackage>>([new("测试表情",[new("[doge]","")])]);
            await ViewModel.LoadCommentEmotesAsync();Check(ViewModel.CommentEmotePackages.Count==2,"未载入账号表情及常用表情");
            CommentEditor.Text="听歌";CommentEditor.Select(1,0);InsertCommentEmote(new("[doge]",""));
            Check(ViewModel.CommentDraft=="听[doge]歌"&&CommentEditor.SelectionStart==7,"表情未在光标位置插入正确的 B 站标记");
            CommentEmoteFlyout.ShowAt(CommentEmoteButton);await Task.Delay(120);UpdateLayout();
            Check(CommentEmoteGrid.Items.Count==16&&CommentEmotePackagePicker.Items.Count==2,"表情包未在原生选择面板显示");
            CommentEmotePackagePicker.SelectedIndex=1;Check(CommentEmoteGrid.Items.Count==1,"无法切换 B 站表情包");
            CommentEmoteFlyout.Hide();
            Check(CommentsButton.BorderThickness==new Thickness(0)&&CommentsButton.Padding==new Thickness(0),"评论按钮仍有边框或裁切用的内边距");
            Check(FooterActions.Children[2]==CommentsButton&&FooterActions.Children[3]==DownloadCurrentButton&&FooterActions.Children[5]==PlaybackRateButton&&FooterActions.Children.Last()==QueueButton,"底栏顺序不符合要求");
            CommentEditor.Text="界面发送验证"; await Task.Delay(30);
            Check(ViewModel.CommentDraft=="界面发送验证"&&SendCommentButton.IsEnabled,$"评论输入状态异常：草稿 {ViewModel.CommentDraft.Length}，载入 {ViewModel.IsCommentsLoading}，发送中 {ViewModel.IsPostingComment}，按钮 {SendCommentButton.IsEnabled}");
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ViewModel.CommentSender=async (track,message,token)=>
            {posts++;Check(track.Bvid==song.Bvid&&message=="界面发送验证","发送到了错误的视频或使用了错误内容");await release.Task.WaitAsync(token);posted=true;return new BiliComment(9999,123,"测试账号","",message,0,1700000200);};
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(SendCommentButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await WaitUntil(()=>ViewModel.IsPostingComment&&posts==1);
            Check(!SendCommentButton.IsEnabled&&!CommentEditor.IsEnabled,"发送期间未阻止重复提交");
            await ViewModel.SendCommentCommand.ExecuteAsync(null);Check(posts==1,"重复点击触发了二次发布");
            release.TrySetResult();await WaitUntil(()=>!ViewModel.IsPostingComment);
            Check(ViewModel.CommentDraft.Length==0&&ViewModel.Comments.Any(comment=>comment.Id==9999),"发送成功后未清空草稿或刷新评论");
            ViewModel.CommentDraft="失败时应保留的草稿";
            ViewModel.CommentSender=(_,_,_)=>throw new HttpRequestException("模拟不确定发布");
            await ViewModel.SendCommentCommand.ExecuteAsync(null);
            Check(ViewModel.CommentDraft=="失败时应保留的草稿","发送失败丢失草稿");
            var count=ViewModel.Comments.Count;var reader=ViewModel.CommentsReader;
            ViewModel.CommentsReader=(_,_,_,_)=>throw new HttpRequestException("模拟读取失败");
            await ViewModel.RefreshCommentsCommand.ExecuteAsync(null);
            Check(ViewModel.Comments.Count==count&&ViewModel.CommentsStatus.Length>0,"读取失败破坏已有评论");
            ViewModel.CommentsReader=reader;await ViewModel.RefreshCommentsCommand.ExecuteAsync(null);
            CommentList.ScrollIntoView(ViewModel.Comments[0]);await Task.Delay(120); UpdateLayout();
            Check(CommentList.ContainerFromIndex(0) is ListViewItem refreshed && ContainsText(refreshed,ViewModel.Comments[0].Content),"刷新后评论未实际渲染");
            ViewModel.Notifications.Clear();await SaveVisualAsync(System.IO.Path.Combine(directory,"comments-panel.png"));
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ViewModel.CommentsReader=async (_,_,_,token)=>
            {started.TrySetResult();try {await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){canceled.TrySetResult();throw;}return new CommentPage([],0,false);};
            var pending=ViewModel.RefreshCommentsCommand.ExecuteAsync(null);await started.Task;
            ViewModel.ShowComments=false;await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));await pending;
            Check(!ViewModel.IsCommentsLoading,"关闭评论后加载状态残留");
            ViewModel.CommentsReader=reader;ViewModel.ShowQueue=true;
            Check(!ViewModel.ShowComments&&!ViewModel.IsCommentsPane,"播放列表与评论面板同时显示");
            ViewModel.ShowComments=true;await WaitUntil(()=>!ViewModel.IsCommentsLoading);
            ViewModel.CommentDraft="旧视频草稿";ViewModel.CurrentTrack=other;
            await WaitUntil(()=>!ViewModel.IsCommentsLoading&&calls.Last().Video==other.Bvid);
            Check(ViewModel.CommentDraft.Length==0&&ViewModel.CommentsTargetTitle==other.Title,"切歌串用了上首歌的草稿或标题");
            ViewModel.CurrentTrack=song;await WaitUntil(()=>!ViewModel.IsCommentsLoading);
            Check(ViewModel.CommentDraft=="旧视频草稿","返回歌曲后未恢复草稿");
            ViewModel.ShowComments=false;ViewModel.ShowQueue=false;
            ViewModel.PlaybackRate=1.5;
            Check(ViewModel.PlaybackRateText=="1.5×"&&Math.Abs(ViewModel.Player.PlaybackRate-1.5)<.001,"倍速选择未立即显示或传到播放器");
            var menu=(MenuFlyout)PlaybackRateButton.Flyout;PlaybackRateOpening(menu,new object());
            Check(menu.Items.OfType<RadioMenuFlyoutItem>().Single(option=>option.IsChecked).Tag as string=="1.5","倍速菜单未选中当前速度");
            using(var restored=new PlayerViewModel(new BilibiliClient(new DeferredVideoHandler()))) Check(Math.Abs(restored.PlaybackRate-1.5)<.001,"倍速未持久化");
        }
        finally
        {
            ViewModel.ShowComments=false;ViewModel.ShowQueue=false;ViewModel.CommentsReader=null;ViewModel.CommentSender=null;
            ViewModel.CommentReplySender=null;ViewModel.CommentReactionWriter=null;ViewModel.CommentRepliesReader=null;ViewModel.CommentEmotesReader=null;
            ViewModel.PlaybackRate=savedRate;ViewModel.Store.SetSetting("playbackRate",savedRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ViewModel.ClearQueueCommand.Execute(null);ViewModel.Notifications.Clear();
        }
    }
}
