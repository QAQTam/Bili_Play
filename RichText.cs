using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace BiliPlay;

// Attached rich-text source for whisper bubbles, mirroring the official web: prose keeps
// line breaks, URLs render as inline chain-link chips opening in the system browser.
// RichTextBlock is sealed in WinUI, so the behavior lives in an attached property.
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
            if (run.IsLink)
            {
                var link = new Hyperlink { NavigateUri = new Uri(run.Url) };
                link.Inlines.Add(new Run { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets") });
                link.Click += async (linkSender, _) =>
                {
                    if (linkSender is Hyperlink { NavigateUri: { } uri }) await Windows.System.Launcher.LaunchUriAsync(uri);
                };
                paragraph.Inlines.Add(link);
                paragraph.Inlines.Add(new Run { Text = " " });
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
}
