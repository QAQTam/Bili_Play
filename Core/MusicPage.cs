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
            var cover = ReadCover(card, bvid);
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

    private static IEnumerable<Match> Matches(string text, string pattern) => Regex.Matches(text, pattern,
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>();

    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(
        Match(tag, @"\s" + name + @"\s*=\s*([""'])(.*?)\1").Groups[2].Value);

    private static string ReadCover(string card, string bvid)
    {
        // Prefer the semantic cover container and the selected video's own link.
        // The first image in a card can be an avatar, badge or preview decoration.
        var pictures = Matches(card, @"<picture\b[^>]*>.*?</picture>")
            .Where(picture => Attribute(Match(picture.Value, @"^<picture\b[^>]*>").Value, "class")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("bili-video-card__cover")).ToArray();
        foreach (var picture in pictures)
            if (ImageIn(picture.Value) is { Length: > 0 } cover) return cover;
        if (pictures.Length > 0) return "";

        var coverImages = Matches(card, @"<img\b[^>]*>").Where(image =>
            Attribute(image.Value, "class").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("bili-video-card__cover")).ToArray();
        foreach (var image in coverImages)
            if (ImageIn(image.Value) is { Length: > 0 } cover) return cover;
        if (coverImages.Length > 0) return "";

        foreach (var link in Matches(card, @"<a\b[^>]*>.*?</a>"))
        {
            var href = Attribute(Match(link.Value, @"^<a\b[^>]*>").Value, "href");
            if (href.StartsWith("//")) href = "https:" + href;
            if (Text.ParseVideoInput(href)?.Bvid == bvid && ImageIn(link.Value) is { Length: > 0 } cover) return cover;
        }

        // Older cards have a bare image. Accept only an unambiguous candidate,
        // excluding known avatar/badge paths rather than guessing another image.
        var candidates = Matches(card, @"<img\b[^>]*>").Select(image => ImageIn(image.Value))
            .Where(image => image.Length > 0 && !image.Contains("/bfs/face/", StringComparison.OrdinalIgnoreCase)
                && !image.Contains("/bfs/garb/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : "";
    }

    private static string ImageIn(string markup)
    {
        foreach (var image in Matches(markup, @"<img\b[^>]*>"))
        foreach (var name in new[] { "data-src", "src" })
        {
            var value = Attribute(image.Value, name).Split('@')[0];
            if (value.StartsWith("//")) value = "https:" + value;
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value[7..];
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && Text.IsMediaHost(uri)) return value;
        }
        return "";
    }
}
