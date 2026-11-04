using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BiliPlay;

// Bilibili sends image emotes alongside their literal [token] in the message.
public sealed class CommentBody : UserControl
{
    public static readonly DependencyProperty CommentProperty = DependencyProperty.Register(nameof(Comment),typeof(BiliComment),typeof(CommentBody),new PropertyMetadata(null,Changed));
    public BiliComment? Comment { get => (BiliComment?)GetValue(CommentProperty); set => SetValue(CommentProperty,value); }
    private readonly RichTextBlock _body = new() { TextWrapping=TextWrapping.Wrap, IsTextSelectionEnabled=true };
    public CommentBody() { Content=_body; HorizontalContentAlignment=HorizontalAlignment.Stretch; }
    private static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs args) => ((CommentBody)sender).Render();
    private void Render()
    {
        _body.Blocks.Clear(); if(Comment is not { } comment) return;
        var paragraph=new Paragraph(); _body.Blocks.Add(paragraph);
        var emotes=comment.Emotes?.Where(item=>item.Text.Length>0).GroupBy(item=>item.Text).Select(group=>group.First()).ToArray() ?? [];
        var text=comment.Content; var cursor=0;
        while(cursor<text.Length)
        {
            var match=emotes.Select(item=>(Item:item,Index:text.IndexOf(item.Text,cursor,StringComparison.Ordinal))).Where(item=>item.Index>=0).OrderBy(item=>item.Index).ThenByDescending(item=>item.Item.Text.Length).FirstOrDefault();
            if(match.Item is null) { paragraph.Inlines.Add(new Run { Text=text[cursor..] }); break; }
            if(match.Index>cursor) paragraph.Inlines.Add(new Run { Text=text[cursor..match.Index] });
            if(Uri.TryCreate(match.Item.Url,UriKind.Absolute,out var uri) && uri.Scheme=="https" && Text.IsMediaHost(uri))
            {
                var image=new Image { Width=24,Height=24,Source=BiliPlay.Services.ArtworkCache.Create(uri,64),Margin=new(1,0,1,0),VerticalAlignment=VerticalAlignment.Bottom };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image,match.Item.Text);
                paragraph.Inlines.Add(new InlineUIContainer { Child=image });
            }
            else paragraph.Inlines.Add(new Run { Text=match.Item.Text });
            cursor=match.Index+match.Item.Text.Length;
        }
    }
}
