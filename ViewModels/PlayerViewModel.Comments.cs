using BiliPlay.Core;
using BiliPlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommentReplyTarget), nameof(CommentReplyLabel), nameof(CommentEditorPlaceholder))]
    public partial CommentEntry? CommentReplyTarget { get; set; }
    public bool HasCommentReplyTarget => CommentReplyTarget is not null;
    public string CommentReplyLabel => CommentReplyTarget is { } target ? $"回复 @{target.Author}" : "";
    public string CommentEditorPlaceholder => CommentReplyTarget is null ? "发表评论" : CommentReplyLabel;
    public void BeginCommentReply(CommentEntry entry)
    { if (CanEditComment && ContainsComment(entry)) CommentReplyTarget = entry; }
    public void CancelCommentReply() { if (!IsPostingComment) CommentReplyTarget = null; }

    public IncrementalCollection<CommentEntry> Comments { get; } = [];
    private readonly DispatcherTimer _commentsTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _commentsLoad, _commentsPost, _emotesLoad;
    private CancellationTokenSource _commentThreads = new();
    private int _commentsVersion, _commentsNextPage = 1;
    private bool _commentsMore, _commentsActive = true;
    private string _commentsContext = "";
    private Track? _commentsTarget;
    private DateTimeOffset _commentsRetryAfter;
    private readonly Dictionary<string,string> _commentDrafts = [];

    public System.Collections.ObjectModel.ObservableCollection<CommentEmotePackage> CommentEmotePackages { get; } = [];
    [ObservableProperty] public partial bool IsEmotesLoading { get; set; }
    [ObservableProperty] public partial string EmotesStatus { get; set; } = "";
    private long _emotesAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidePaneOpen))]
    public partial bool ShowComments { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidePaneWidth))]
    public partial bool IsCommentsPane { get; set; }
    public double SidePaneWidth => IsCommentsPane ? 440 : 360;
    public bool IsSidePaneOpen
    {
        get => ShowQueue || ShowComments;
        set { if (!value) { ShowQueue = false; ShowComments = false; } else if (!IsSidePaneOpen) ShowQueue = true; }
    }
    [ObservableProperty] public partial int CommentSortIndex { get; set; }
    [ObservableProperty] public partial bool IsCommentsLoading { get; set; }
    [ObservableProperty] public partial bool IsPostingComment { get; set; }
    [ObservableProperty] public partial string CommentDraft { get; set; } = "";
    [ObservableProperty] public partial string CommentsStatus { get; set; } = "";
    [ObservableProperty] public partial string CommentsTargetTitle { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommentsTitle))]
    public partial long CommentsTotal { get; set; }
    public string CommentsTitle => CommentsTotal > 0 ? $"评论 {CommentsTotal:N0}" : "评论";
    public bool CanSendComment => ShowComments && IsLoggedIn && !NeedsLogin && HasCurrentTrack && !IsCommentsLoading && !IsPostingComment && !string.IsNullOrWhiteSpace(CommentDraft);
    public bool CanRefreshComments => ShowComments && !IsCommentsLoading && !IsPostingComment;
    public bool CanEditComment => IsLoggedIn && !NeedsLogin && HasCurrentTrack && !IsPostingComment;

    private void UpdateCommentAvailability()
    {
        OnPropertyChanged(nameof(CanSendComment)); OnPropertyChanged(nameof(CanEditComment)); OnPropertyChanged(nameof(CanRefreshComments));
        RefreshCommentsCommand.NotifyCanExecuteChanged();
        SendCommentCommand.NotifyCanExecuteChanged();
    }
    private void InitializeComments()
    {
        Comments.CanLoadMore = () => ShowComments && _commentsMore && !IsCommentsLoading;
        Comments.FetchPage = token => LoadCommentsAsync(false,false,token);
        _commentsTimer.Tick += async (_, _) =>
        {
            if (ShowComments && _commentsActive && !IsCommentsLoading && DateTimeOffset.UtcNow >= _commentsRetryAfter)
                await RefreshCommentHeadAsync();
        };
    }
    partial void OnShowCommentsChanged(bool value)
    {
        if (value)
        {
            if (!IsLoggedIn || CurrentTrack is null) { ShowComments = false; return; }
            IsCommentsPane = true; ShowQueue = false; ChangeCommentsContext(CurrentTrack);
            if (_commentsActive && !App.IsVisualCheck) _commentsTimer.Start();
            _ = LoadCommentsAsync(true,false);
        }
        else { _commentsTimer.Stop(); CancelCommentsRead(); CancelCommentThreads(); _emotesLoad?.Cancel(); }
        UpdateCommentAvailability();
    }
    partial void OnCurrentTrackChanged(Track? value)
    {
        UpdateCommentAvailability();
        if (!ShowComments) return;
        if (value is null) { ShowComments = false; return; }
        if (ChangeCommentsContext(value)) _ = LoadCommentsAsync(true,false);
    }
    partial void OnIsLoggedInChanged(bool value)
    {
        if (!value)
        {
            ShowComments = false; _commentsPost?.Cancel(); _commentDrafts.Clear();
            CommentReplyTarget = null; _commentsContext = ""; _commentsTarget = null; Comments.Clear(); CommentDraft = "";
            _emotesAccount=0; CommentEmotePackages.Clear();
        }
        UpdateCommentAvailability();
    }
    partial void OnNeedsLoginChanged(bool value) => UpdateCommentAvailability();
    partial void OnCommentSortIndexChanged(int value)
    { if (ShowComments) _ = LoadCommentsAsync(true,false); }
    partial void OnCommentDraftChanged(string value)
    {
        if (_commentsContext.Length > 0) _commentDrafts[_commentsContext] = value;
        UpdateCommentAvailability();
    }
    partial void OnIsCommentsLoadingChanged(bool value) { UpdateCommentAvailability(); Comments.NotifyHasMore(); }
    partial void OnIsPostingCommentChanged(bool value) => UpdateCommentAvailability();
    public void SetCommentsPresentationActive(bool active)
    {
        _commentsActive = active;
        if (active && ShowComments && !App.IsVisualCheck) _commentsTimer.Start(); else _commentsTimer.Stop();
    }
    private bool ChangeCommentsContext(Track track)
    {
        var key = $"{Client.Account?.Id}:{track.Bvid}";
        CommentsTargetTitle = track.Title;
        if (_commentsContext == key) { if (track.Aid > 0) _commentsTarget = track; return false; }
        CommentReplyTarget = null;
        CancelCommentsRead(); CancelCommentThreads(); _commentsContext = key; _commentsTarget = track;
        Comments.Clear(); CommentsTotal = 0; CommentsStatus = "";
        CommentDraft = _commentDrafts.GetValueOrDefault(key, "");
        _commentsNextPage = 1; _commentsMore = false; Comments.ResetLoading();
        return true;
    }
    private void CancelCommentsRead()
    {
        _commentsVersion++; _commentsLoad?.Cancel(); _commentsLoad = null;
        IsCommentsLoading = false;
    }
    internal Task RefreshCommentHeadAsync() => LoadCommentsAsync(false,true);
    [RelayCommand(CanExecute = nameof(CanRefreshComments))]
    private Task RefreshCommentsAsync() => LoadCommentsAsync(true,false);
    private async Task<Track> ResolveCommentTargetAsync(Track track, CancellationToken token)
    {
        if (track.Aid > 0) return track;
        return (await Client.GetVideoAsync(track.Bvid,track.Aid,token)).Track;
    }
    private async Task LoadCommentsAsync(bool reset, bool headOnly, CancellationToken token = default)
    {
        if (_disposed || !ShowComments || _commentsTarget is not { } track || Client.Account is not { } account) return;
        if (reset) { CancelCommentsRead(); CancelCommentThreads(); _commentsNextPage = 1; _commentsMore = false; Comments.ResetLoading(); }
        else if (IsCommentsLoading || !headOnly && !_commentsMore) return;
        var version = _commentsVersion; var context = _commentsContext;
        var previousEntries = Comments.ToDictionary(item=>item.Id);
        var reactions = Comments.ToDictionary(item=>item.Id,item=>item.InteractionVersion);
        var sort = CommentSortIndex == 1 ? CommentSort.Latest : CommentSort.Hot;
        var page = headOnly ? 1 : _commentsNextPage;
        using var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token,token);
        load.CancelAfter(TimeSpan.FromSeconds(20)); _commentsLoad = load;
        IsCommentsLoading = true; CommentsStatus = "";
        try
        {
            var target=await ResolveCommentTargetAsync(track,load.Token);
#if VISUAL_CHECKS
            var result=await (CommentsReader is null?Client.GetCommentsAsync(target.Aid,sort,page,load.Token):CommentsReader(target,sort,page,load.Token));
#else
            var result=await Client.GetCommentsAsync(target.Aid,sort,page,load.Token);
#endif
            load.Token.ThrowIfCancellationRequested();
            if (version != _commentsVersion || context != _commentsContext || Client.Account?.Id != account.Id || !ShowComments) return;
            _commentsTarget = target; CommentsTotal = result.Total;
            if (reset) Comments.Clear();
            if (headOnly)
            {
                for (var i = 0; i < result.Items.Count; i++)
                {
                    var comment = result.Items[i]; var existing = Comments.FirstOrDefault(item=>item.Id==comment.Id);
                    if (existing is null) Comments.Insert(Math.Min(i,Comments.Count),new(comment));
                    else
                    {
                        var old = Comments.IndexOf(existing);
                        if (old != i) Comments.Move(old,Math.Min(i,Comments.Count-1));
                        existing.Update(comment,existing.InteractionVersion != reactions.GetValueOrDefault(existing.Id));
                    }
                }
            }
            else foreach (var comment in result.Items)
            {
                if (Comments.Any(item=>item.Id==comment.Id)) continue;
                if (previousEntries.TryGetValue(comment.Id,out var entry))
                {
                    entry.Update(comment,entry.InteractionVersion!=reactions.GetValueOrDefault(entry.Id)); Comments.Add(entry);
                }
                else Comments.Add(new(comment));
            }
            if (!headOnly || _commentsNextPage == 1) { _commentsNextPage = page + 1; _commentsMore = result.HasMore; }
            CommentsStatus = Comments.Count == 0 ? "暂无评论" : "";
            _commentsRetryAfter = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested && (version != _commentsVersion || _disposed || !ShowComments)) { }
        catch (Exception error)
        {
            if (version != _commentsVersion || _disposed) return;
            if (error is BilibiliException { RequiresLogin:true }) NeedsLogin = true;
            _commentsMore = false; _commentsRetryAfter = DateTimeOffset.UtcNow.AddMinutes(2);
            CommentsStatus = Feedback.WithRequestHint(error is OperationCanceledException ? "评论加载超时，请刷新重试。" : error.Message);
        }
        finally
        {
            if (ReferenceEquals(_commentsLoad,load)) { _commentsLoad = null; IsCommentsLoading = false; Comments.NotifyHasMore(); }
        }
    }
    [RelayCommand(CanExecute = nameof(CanSendComment))]
    private async Task SendCommentAsync()
    {
        if (!CanSendComment || _commentsTarget is not { } track || Client.Account is not { } account) return;
        var message = CommentDraft.Trim(); var context = _commentsContext; var replying = CommentReplyTarget;
        var rootId = replying?.RootId ?? 0; var parentId = replying?.Id ?? 0;
        using var post = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        post.CancelAfter(TimeSpan.FromSeconds(20)); _commentsPost = post; IsPostingComment = true;
        try
        {
            var target=await ResolveCommentTargetAsync(track,post.Token);
#if VISUAL_CHECKS
            var posted=await (replying is not null && CommentReplySender is not null ? CommentReplySender(target,message,rootId,parentId,post.Token) : CommentSender is null?Client.PostCommentAsync(target.Aid,message,post.Token,rootId,parentId):CommentSender(target,message,post.Token));
#else
            var posted=await Client.PostCommentAsync(target.Aid,message,post.Token,rootId,parentId);
#endif
            if (_disposed || Client.Account?.Id != account.Id) return;
            _commentDrafts.Remove(context);
            if (_commentsContext == context)
            {
                CommentDraft = ""; CommentReplyTarget = null;
                if (posted is not null)
                {
                    if (rootId > 0 && Comments.FirstOrDefault(item=>item.Id==rootId) is { } root)
                    {
                        if (!root.Replies.Any(item=>item.Id==posted.Id)) root.Replies.Add(new(posted,true,rootId));
                        root.Update(root.Data with { ReplyCount=root.Data.ReplyCount+1 }); root.IsExpanded=true;
                    }
                    else if (rootId == 0 && !Comments.Any(item=>item.Id==posted.Id)) Comments.Insert(0,new(posted));
                    CommentsTotal++;
                }
                if (ShowComments) await LoadCommentsAsync(false,true);
            }
            Notify("评论已提交。",NoticeLevel.Info);
        }
        catch (Exception error)
        {
            if (_disposed || Client.Account?.Id != account.Id) return;
            if (error is BilibiliException { RequiresLogin:true }) NeedsLogin = true;
            Notify(error is OperationCanceledException or HttpRequestException
                ? "评论提交结果尚未确认，请刷新评论核对后再试。" : error.Message,NoticeLevel.Warn);
        }
        finally { if (ReferenceEquals(_commentsPost,post)) _commentsPost = null; IsPostingComment = false; }
    }
    private void CancelCommentThreads()
    {
        _commentThreads.Cancel(); _commentThreads.Dispose(); _commentThreads = new();
    }
    private bool ContainsComment(CommentEntry entry) => Comments.Contains(entry) || Comments.Any(root=>root.Replies.Contains(entry));
    public async Task ReactToCommentAsync(CommentEntry entry, CommentReaction reaction)
    {
        if (_disposed || !ShowComments || NeedsLogin || !ContainsComment(entry) || entry.IsReacting || _commentsTarget is not { } track || Client.Account is not { } account) return;
        var context=_commentsContext; var enabled=entry.Data.Reaction != reaction;
        using var post=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); post.CancelAfter(TimeSpan.FromSeconds(20));
        entry.IsReacting=true;
        try
        {
            var target=await ResolveCommentTargetAsync(track,post.Token);
#if VISUAL_CHECKS
            if(CommentReactionWriter is not null) await CommentReactionWriter(target,entry.Id,reaction,enabled,post.Token);
            else
#endif
                await Client.SetCommentReactionAsync(target.Aid,entry.Id,reaction,enabled,post.Token);
            if (!_disposed && Client.Account?.Id==account.Id && context==_commentsContext && ContainsComment(entry))
                entry.ApplyReaction(enabled?reaction:CommentReaction.None);
        }
        catch (Exception error)
        {
            if (_disposed || Client.Account?.Id!=account.Id) return;
            if (error is BilibiliException { RequiresLogin:true }) NeedsLogin=true;
            Notify(error is OperationCanceledException or HttpRequestException ? "操作结果尚未确认，请刷新评论核对后再试。" : error.Message,NoticeLevel.Warn);
        }
        finally { entry.IsReacting=false; }
    }
    public async Task ToggleCommentRepliesAsync(CommentEntry root)
    {
        if (!root.HasReplies || !Comments.Contains(root)) return;
        root.IsExpanded=!root.IsExpanded;
        if (root.IsExpanded && root.Replies.Count==0) await LoadCommentRepliesAsync(root);
    }
    public async Task LoadCommentRepliesAsync(CommentEntry root)
    {
        if (_disposed || !ShowComments || !root.IsExpanded || !Comments.Contains(root) || !root.CanLoadReplies || _commentsTarget is not { } track || Client.Account is not { } account) return;
        var context=_commentsContext; var page=root.NextReplyPage;
        var threadToken=_commentThreads.Token;
        using var load=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token,threadToken);
        load.CancelAfter(TimeSpan.FromSeconds(20)); root.IsRepliesLoading=true; root.RepliesStatus="";
        try
        {
            var target=await ResolveCommentTargetAsync(track,load.Token);
#if VISUAL_CHECKS
            var result=await (CommentRepliesReader is null?Client.GetCommentRepliesAsync(target.Aid,root.Id,page,load.Token):CommentRepliesReader(target,root.Id,page,load.Token));
#else
            var result=await Client.GetCommentRepliesAsync(target.Aid,root.Id,page,load.Token);
#endif
            load.Token.ThrowIfCancellationRequested();
            if (_disposed || context!=_commentsContext || Client.Account?.Id!=account.Id || !Comments.Contains(root)) return;
            var before=root.Replies.Count;
            foreach(var reply in result.Items) if(!root.Replies.Any(item=>item.Id==reply.Id)) root.Replies.Add(new(reply,true,root.Id));
            root.NextReplyPage=page+1; root.HasMoreReplies=result.HasMore&&root.Replies.Count>before;
            root.RepliesStatus=root.Replies.Count==0?"暂无回复":"";
        }
        catch(OperationCanceledException) when(_disposed || !ShowComments || context!=_commentsContext || threadToken.IsCancellationRequested) { }
        catch(Exception error)
        {
            if (!ContainsComment(root)||context!=_commentsContext||_disposed) return;
            if(error is BilibiliException { RequiresLogin:true }) NeedsLogin=true;
            root.RepliesStatus=Feedback.WithRequestHint(error is OperationCanceledException?"回复加载超时，请重试。":error.Message);
        }
        finally { root.IsRepliesLoading=false; }
    }
    public async Task LoadCommentEmotesAsync()
    {
        if (_disposed || !ShowComments || NeedsLogin || IsEmotesLoading || Client.Account is not { } account) return;
        if (_emotesAccount==account.Id) return;
        if (CommentEmotePackages.Count==0) CommentEmotePackages.Add(new("常用",new[] { "😀","😊","😂","🥰","😍","😎","😭","👍","👏","❤️","🎵","🎶","🎉","🙏","✨","🤔" }.Select(text=>new CommentEmote(text,"")).ToArray()));
        using var load=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); load.CancelAfter(TimeSpan.FromSeconds(15));
        _emotesLoad=load; IsEmotesLoading=true; EmotesStatus="";
        try
        {
#if VISUAL_CHECKS
            var packages=await (CommentEmotesReader is null?Client.GetCommentEmotesAsync(load.Token):CommentEmotesReader(load.Token));
#else
            var packages=await Client.GetCommentEmotesAsync(load.Token);
#endif
            load.Token.ThrowIfCancellationRequested();
            if (_disposed || Client.Account?.Id!=account.Id) return;
            foreach(var package in packages) CommentEmotePackages.Add(package);
            _emotesAccount=account.Id;
        }
        catch(OperationCanceledException) when(_disposed||!ShowComments) { }
        catch(Exception error)
        {
            if (_disposed || Client.Account?.Id!=account.Id) return;
            if(error is BilibiliException { RequiresLogin:true }) NeedsLogin=true;
            EmotesStatus="B 站表情暂时无法加载，可使用常用表情。";
        }
        finally { if(ReferenceEquals(_emotesLoad,load)) _emotesLoad=null; IsEmotesLoading=false; }
    }
    private void DisposeComments()
    { _commentsTimer.Stop(); CancelCommentsRead(); _commentsPost?.Cancel(); _emotesLoad?.Cancel(); _commentThreads.Cancel(); _commentThreads.Dispose(); }
}
