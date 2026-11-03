using System.Text;
using System.Text.RegularExpressions;

namespace BiliPlay.Core;

public sealed record PreferredUploader(long Id, string Name, string VerifiedVideo);
public static class PreferredUploaders
{
    // Checked against videoData.owner / staff in public Bilibili pages on 2026-11-02.
    // The requested spelling JRLS-LeoFM refers to JLRS-LeoFM.
    public static IReadOnlyList<PreferredUploader> All { get; } = Array.AsReadOnly(new[]
    {
        new PreferredUploader(383152785, "云端音乐铺", "BV1o14y1R7Ad"),
        new PreferredUploader(1873801216, "Sean_Dream", "BV1C84y1x75d"),
        new PreferredUploader(88082801, "挽风丶Sama", "BV1ZDSoBEEvK"),
        new PreferredUploader(3546707482643138, "南风未满", "BV1eCAoeDEi8"),
        new PreferredUploader(490518248, "橘子不是青柠", "BV1Cy421e7n8"),
        new PreferredUploader(9666167, "VV音乐局", "BV17LVN6yEWd"),
        new PreferredUploader(3493093607213343, "JLRS-LeoFM", "BV1ch4y1j7fa"),
        new PreferredUploader(440121192, "JLRS日落fm", "BV1ch4y1j7fa"),
        new PreferredUploader(3493082817366845, "JLRS-jayfm", "BV1ch4y1j7fa")
    });
    private static readonly HashSet<long> Ids = All.Select(user => user.Id).ToHashSet();
    public static bool Contains(long id) => Ids.Contains(id);
    private static string Normalize(string text) => Text.Clean(text).Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    public static bool Matches(Track track, string query)
    {
        var tokens = Regex.Matches(Normalize(query), @"[\p{L}\p{N}]+").Select(match => match.Value).ToArray();
        var haystack = Normalize(track.Title + " " + track.Part + " " + track.Uploader);
        return tokens.Length > 0 && tokens.All(token => haystack.Contains(token, StringComparison.Ordinal));
    }
    public static IReadOnlyList<Track> RankSearch(IEnumerable<Track> tracks, string query) => tracks
        .DistinctBy(track => (track.Bvid, track.Page))
        .OrderByDescending(track => Contains(track.UploaderId) && Matches(track, query)).ToArray();
    public static IReadOnlyList<Track> BlendHome(IEnumerable<Track> preferred, IEnumerable<Track> channel)
    {
        var favorites = new Queue<Track>(preferred.Where(track => Contains(track.UploaderId)));
        var discovery = new Queue<Track>(channel);
        var result = new List<Track>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(Track track) { if (seen.Add(track.Bvid)) result.Add(track); }
        while (favorites.Count > 0 || discovery.Count > 0)
        {
            for (var i = 0; i < 2 && favorites.TryDequeue(out var track); i++) Add(track);
            if (discovery.TryDequeue(out var other)) Add(other);
        }
        return result;
    }
}
