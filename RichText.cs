using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace BiliPlay;

// Attached rich-text source for whisper bubbles: prose keeps line breaks and URLs render
// as inline hyperlinks that show the actual address (not a bare link icon) and open in the
// system browser. RichTextBlock is sealed in WinUI, so the behavior lives in an attached property.
public static class RichText
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(RichText), new PropertyMetadata("", OnSourceChanged));

    public static void SetSource(DependencyObject element, string value) => element.SetValue(SourceProperty, value);
    public static string GetSource(DependencyObject element) => (string)element.GetValue(SourceProperty);

    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not RichTextBlock block) return;
        block.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var run in RichMessage.Parse(GetSource(block) ?? ""))
        {
            if (run.IsLink && RichMessage.TryWebUri(run.Url, out var uri))
            {
                var link = new Hyperlink();
                link.Inlines.Add(new Run { Text = run.Url });
                link.Click += async (_, _) =>
                {
                    try
                    {
                        if (!await Windows.System.Launcher.LaunchUriAsync(uri!)) NotifyLaunchFailure();
                    }
                    catch (Exception) { NotifyLaunchFailure(); }
                };
                paragraph.Inlines.Add(link);
            }
            else
            {
                var lines = run.Text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0) paragraph.Inlines.Add(new LineBreak());
                    if (lines[i].Length > 0) paragraph.Inlines.Add(new Run { Text = lines[i] });
                }
            }
        }
        block.Blocks.Add(paragraph);
    }
    private static void NotifyLaunchFailure()
    {
        if (App.Window is MainWindow window) window.ViewModel.Notify("无法打开链接，可复制地址后在浏览器中访问。", NoticeLevel.Warn);
    }
}
