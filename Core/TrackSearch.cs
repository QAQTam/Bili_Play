namespace BiliPlay.Core;

public static class TrackSearch
{
    public static bool Matches(Track track, string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .All(word => new[] { track.Title, track.Uploader, track.Part, track.Bvid }
            .Any(text => text.Contains(word, StringComparison.OrdinalIgnoreCase)));

    public static async Task<IReadOnlyList<Track>> FindAsync(
        Func<int, CancellationToken, Task<PageResult<Track>>> readPage, string query, CancellationToken token)
    {
        var found = new List<Track>();
        var known = new HashSet<(string, int)>();
        for (var page = 1; ; page++)
        {
            token.ThrowIfCancellationRequested();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            request.CancelAfter(TimeSpan.FromSeconds(25));
            var result = await readPage(page, request.Token).ConfigureAwait(false);
            var added = 0;
            foreach (var track in result.Items)
            {
                token.ThrowIfCancellationRequested();
                if (!known.Add((track.Bvid.Length > 0 ? track.Bvid : "aid:" + track.Aid, track.Page))) continue;
                added++;
                if (Matches(track, query)) found.Add(track);
            }
            // Some endpoints repeat the last page while still reporting has_more.
            if (!result.HasMore || added == 0) return found;
        }
    }
}
