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
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""，。！？；、]+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryWebUri(string value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri)
        && uri.Scheme is "http" or "https" && uri.IsWellFormedOriginalString() && uri.Host.Length > 0
        && uri.UserInfo.Length == 0 && !uri.IsLoopback;

    public static IReadOnlyList<RichRun> Parse(string text)
    {
        var runs = new List<RichRun>();
        var position = 0;
        foreach (Match match in UrlPattern.Matches(text))
        {
            var url = match.Value.TrimEnd('。', '，', ',', '！', '!', '.');
            while (url.EndsWith(')') && url.Count(character => character == ')') > url.Count(character => character == '(')) url = url[..^1];
            if (!TryWebUri(url, out _)) continue;
            if (match.Index > position) runs.Add(new(text[position..match.Index], ""));
            runs.Add(new(url, url));
            position = match.Index + url.Length;
        }
        if (position < text.Length) runs.Add(new(text[position..], ""));
        return runs;
    }
}
