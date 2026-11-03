using System.Net;
using System.Text.RegularExpressions;

namespace BiliPlay.Core;

public static class MusicPage
{
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern,
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Read rendered cards only. Do not evaluate the page's JavaScript or include banners.
    public static IReadOnlyList<Track> Parse(string html)
    {
        var cards = Regex.Split(html, "<div class=\"feed-card[^\"]*\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var tracks = new List<Track>();
        foreach (var card in cards.Skip(1))
        {
            var bvid = Match(card, @"bilibili\.com/video/(BV[0-9A-Za-z]{10})").Groups[1].Value;
            var title = WebUtility.HtmlDecode(Match(card, "class=\"bili-video-card__title\"[^>]*title=\"([^\"]*)\"").Groups[1].Value);
            var cover = WebUtility.HtmlDecode(Match(card, "<img[^>]*src=\"([^\"]+)\"").Groups[1].Value).Split('@')[0];
            var author = Match(card, "<a(?=[^>]*class=\"[^\"]*bili-video-card__author)(?=[^>]*href=\"(?:https:)?//space.bilibili.com/(\\d+)[^\"]*\")[^>]*>(.*?)</a>");
            var name = Text.Clean(Match(author.Groups[2].Value, "<span[^>]*title=\"([^\"]*)\"").Groups[1].Value);
            if (string.IsNullOrWhiteSpace(name)) name = Text.Clean(author.Groups[2].Value);
            // Channel cards append a publication date to the uploader name.
            var dateSeparator = name.LastIndexOf(" · ", StringComparison.Ordinal);
            if (dateSeparator >= 0) name = name[..dateSeparator];
            var time = Match(card, @"<span[^>]*>(\d{1,3}:\d{2}(?::\d{2})?)</span>").Groups[1].Value;
            if (bvid.Length == 0 || title.Length == 0) continue;
            if (cover.StartsWith("//")) cover = "https:" + cover;
            if (cover.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) cover = "https://" + cover[7..];
            if (!Uri.TryCreate(cover, UriKind.Absolute, out var image) || !Text.IsMediaHost(image)) continue;
            long.TryParse(author.Groups[1].Value, out var owner);
            tracks.Add(new(0, bvid, title, name, cover, Text.ParseDuration(time), UploaderId: owner));
        }
        return tracks.DistinctBy(t => t.Bvid).ToArray();
    }
}
