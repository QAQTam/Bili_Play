using BiliPlay.Core;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace BiliPlay;

public sealed partial class MainPage
{
    private void ReplyToCommentClick(object sender,RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag:CommentEntry entry })
        { ViewModel.BeginCommentReply(entry); CommentEditor.Focus(FocusState.Programmatic); }
    }
    private void CancelCommentReplyClick(object sender,RoutedEventArgs args) => ViewModel.CancelCommentReply();
    private void ToggleCommentsClick(object sender,RoutedEventArgs args) => ViewModel.ShowComments=!ViewModel.ShowComments;
    private async void LikeCommentClick(object sender,RoutedEventArgs args) => await ReactCommentAsync(sender,CommentReaction.Like);
    private async void DislikeCommentClick(object sender,RoutedEventArgs args) => await ReactCommentAsync(sender,CommentReaction.Dislike);
    private async Task ReactCommentAsync(object sender,CommentReaction reaction)
    {
        if(sender is not ToggleButton { Tag:CommentEntry entry } button) return;
        // The native toggle changes before Click. Restore server state until the request succeeds.
        button.IsChecked=reaction==CommentReaction.Like?entry.IsLiked:entry.IsDisliked;
        await ViewModel.ReactToCommentAsync(entry,reaction);
    }
    private async void ToggleCommentRepliesClick(object sender,RoutedEventArgs args)
    { if(sender is FrameworkElement { Tag:CommentEntry entry }) await ViewModel.ToggleCommentRepliesAsync(entry); }
    private async void MoreCommentRepliesClick(object sender,RoutedEventArgs args)
    { if(sender is FrameworkElement { Tag:CommentEntry entry }) await ViewModel.LoadCommentRepliesAsync(entry); }
    private async void CommentEmoteOpening(object sender,object args)
    {
        var pending=ViewModel.LoadCommentEmotesAsync();
        if(CommentEmotePackagePicker.SelectedIndex<0&&ViewModel.CommentEmotePackages.Count>0) CommentEmotePackagePicker.SelectedIndex=0;
        await pending;
        if(CommentEmotePackagePicker.SelectedIndex<0&&ViewModel.CommentEmotePackages.Count>0) CommentEmotePackagePicker.SelectedIndex=0;
    }
    private void CommentEmotePackageChanged(object sender,SelectionChangedEventArgs args)
    { if(CommentEmoteGrid is not null) CommentEmoteGrid.ItemsSource=(CommentEmotePackagePicker.SelectedItem as CommentEmotePackage)?.Items; }
    private void CommentEmoteClick(object sender,ItemClickEventArgs args)
    { if(args.ClickedItem is CommentEmote emote) InsertCommentEmote(emote); }
    private void InsertCommentEmote(CommentEmote emote)
    {
        if(!ViewModel.CanEditComment) return;
        var text=CommentEditor.Text; var start=Math.Clamp(CommentEditor.SelectionStart,0,text.Length);
        var length=Math.Clamp(CommentEditor.SelectionLength,0,text.Length-start);
        if(text.Length-length+emote.Text.Length>CommentEditor.MaxLength) { ViewModel.Notify("评论最多 1000 个字符。",NoticeLevel.Warn); return; }
        CommentEditor.Text=text[..start]+emote.Text+text[(start+length)..];
        CommentEditor.Select(start+emote.Text.Length,0); CommentEmoteFlyout.Hide(); CommentEditor.Focus(FocusState.Programmatic);
    }
}
