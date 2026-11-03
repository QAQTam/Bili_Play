using System.Collections.ObjectModel;
using BiliPlay.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public sealed partial class CommentEntry(BiliComment data, bool isReply = false, long rootId = 0) : ObservableObject
{
    public BiliComment Data { get; private set; } = data;
    public long Id => Data.Id;
    public long RootId => rootId > 0 ? rootId : Id;
    public long AuthorId => Data.AuthorId;
    public string Author => Data.Author;
    public string Avatar => Data.Avatar;
    public string Content => Data.Content;
    public string Meta => Data.Meta;
    public string LikeLabel => Data.Likes > 0 ? Data.Likes.ToString("N0") : "赞";
    public bool IsLiked => Data.Reaction == CommentReaction.Like;
    public bool IsDisliked => Data.Reaction == CommentReaction.Dislike;
    public bool CanReact => !IsReacting;
    public bool HasReplies => !isReply && Data.ReplyCount > 0;
    public string RepliesLabel => IsExpanded ? "收起回复" : $"展开 {Data.ReplyCount:N0} 条回复";
    public string MoreRepliesLabel => RepliesStatus.Length > 0 ? "重试" : "加载更多回复";
    public bool CanLoadReplies => !IsRepliesLoading && (HasMoreReplies || RepliesStatus.Length > 0);
    public ObservableCollection<CommentEntry> Replies { get; } = [];
    public int NextReplyPage { get; set; } = 1;
    public long InteractionVersion { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanReact))] public partial bool IsReacting { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RepliesLabel))] public partial bool IsExpanded { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanLoadReplies))] public partial bool IsRepliesLoading { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanLoadReplies))] public partial bool HasMoreReplies { get; set; } = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanLoadReplies),nameof(MoreRepliesLabel))] public partial string RepliesStatus { get; set; } = "";
    public void Update(BiliComment value, bool keepReaction = false)
    {
        Data = keepReaction || IsReacting ? value with { Reaction=Data.Reaction,Likes=Data.Likes } : value;
        OnPropertyChanged(nameof(Data)); OnPropertyChanged(nameof(Author)); OnPropertyChanged(nameof(Avatar));
        OnPropertyChanged(nameof(Content)); OnPropertyChanged(nameof(Meta)); OnPropertyChanged(nameof(LikeLabel));
        OnPropertyChanged(nameof(IsLiked)); OnPropertyChanged(nameof(IsDisliked)); OnPropertyChanged(nameof(HasReplies)); OnPropertyChanged(nameof(RepliesLabel));
    }
    public void ApplyReaction(CommentReaction reaction)
    {
        var likes = Math.Max(0,Data.Likes + (reaction==CommentReaction.Like?1:0) - (Data.Reaction==CommentReaction.Like?1:0));
        InteractionVersion++; Data = Data with { Reaction=reaction,Likes=likes }; Update(Data);
    }
}
