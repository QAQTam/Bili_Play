using System.Text.RegularExpressions;

namespace BiliPlay.Core;

public sealed record RichRun(string Text, string Url)
{
    public bool IsLink => Url.Length > 0;
}

// Mirrors the official web whisper rich text: plain spans keep line breaks and URLs become
// link runs that the view renders as hyperlinks showing the address. Separators and 【headings】 stay plain.
public static class RichMessage
{
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""]+", RegexOptions.Compiled);

    public static IReadOnlyList<RichRun> Parse(string text)
    {
        var runs = new List<RichRun>();
        var position = 0;
        foreach (Match match in UrlPattern.Matches(text))
        {
            var url = match.Value.TrimEnd('>', ')', '？', '?', '。', '，', ',', '！', '!');
            if (url.Length <= "https://".Length) continue;
            if (match.Index > position) runs.Add(new(text[position..match.Index], ""));
            runs.Add(new("", url));
            position = match.Index + match.Value.Length;
        }
        if (position < text.Length) runs.Add(new(text[position..], ""));
        return runs;
    }
}
