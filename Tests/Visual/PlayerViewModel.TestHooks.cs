using BiliPlay.Core;

namespace BiliPlay.ViewModels;

// Included only by builds that explicitly set EnableVisualChecks=true.
public partial class PlayerViewModel
{
    internal Func<int,CancellationToken,Task<PageResult<Track>>>? HomeReader { get; set; }
    internal Func<Track,CommentSort,int,CancellationToken,Task<CommentPage>>? CommentsReader { get; set; }
    internal Func<Track,string,long,long,CancellationToken,Task<BiliComment?>>? CommentReplySender { get; set; }
    internal Func<Track,string,CancellationToken,Task<BiliComment?>>? CommentSender { get; set; }
    internal Func<Track,long,int,CancellationToken,Task<CommentPage>>? CommentRepliesReader { get; set; }
    internal Func<Track,long,CommentReaction,bool,CancellationToken,Task>? CommentReactionWriter { get; set; }
    internal Func<CancellationToken,Task<IReadOnlyList<CommentEmotePackage>>>? CommentEmotesReader { get; set; }
}
