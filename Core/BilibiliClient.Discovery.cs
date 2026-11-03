using System.Collections.Concurrent;
using System.Text.Json;

namespace BiliPlay.Core;

public sealed partial class BilibiliClient
{
    private readonly SemaphoreSlim _discoveryGate = new(2, 2);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, PageResult<Track> Page)> _archiveCache = new();
    private long _archiveRetryAfterTicks;
    private int _homeRotation;

    private async Task<PageResult<Track>> ReadPreferredArchiveAsync(PreferredUploader uploader, int page, string keyword, CancellationToken ct)
    {
        var key = $"{uploader.Id}:{page}:{keyword}";
        if (_archiveCache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(15)) return cached.Page;
        await _discoveryGate.WaitAsync(ct);
        try
        {
            if (_archiveCache.TryGetValue(key, out cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(15)) return cached.Page;
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _archiveRetryAfterTicks)) return new([], false);
            // The space frontend uses this signed archive endpoint; these are read-only requests.
            var data = await GetAsync("x/space/wbi/arc/search", new()
            {
                ["mid"] = uploader.Id.ToString(), ["pn"] = page.ToString(), ["ps"] = "30", ["tid"] = "3",
                ["keyword"] = keyword, ["order"] = "pubdate", ["order_avoided"] = "true", ["platform"] = "web"
            }, true, ct);
            var items = data.O("list").O("vlist").A()
                .Where(item => item.N("mid") == uploader.Id && !string.IsNullOrEmpty(item.S("bvid")))
                .Select(item => new Track(item.N("aid"), item.S("bvid"), Text.Clean(item.S("title")),
                    item.S("author", uploader.Name), ImageUrl(item.S("pic")), Text.ParseDuration(item.S("length")), UploaderId: uploader.Id))
                .Where(track => keyword.Length == 0 || PreferredUploaders.Matches(track, keyword)).ToArray();
            var result = new PageResult<Track>(items, (long)page * 30 < data.O("page").N("count"));
            if (_archiveCache.Count > 128) _archiveCache.Clear();
            _archiveCache[key] = (DateTimeOffset.UtcNow, result);
            return result;
        }
        catch (BilibiliException error) when (error.Code is -352 or -403 or -799)
        { Interlocked.Exchange(ref _archiveRetryAfterTicks, DateTime.UtcNow.AddMinutes(2).Ticks); return new([], false); }
        catch (Exception error) when (error is HttpRequestException or BilibiliException or InvalidDataException or JsonException)
        { return new([], false); }
        finally { _discoveryGate.Release(); }
    }

    private async Task<PageResult<Track>> TryPreferredArchiveAsync(PreferredUploader uploader, int page, string keyword, CancellationToken ct)
    {
        try { return await ReadPreferredArchiveAsync(uploader, page, keyword, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new([], false); }
    }

    public async Task<PageResult<Track>> SearchPreferredAsync(string keyword, int page, CancellationToken ct = default)
    {
        RequireAccount();
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (page != 1) return await SearchAsync(keyword, page, ct);
        using var extra = CancellationTokenSource.CreateLinkedTokenSource(ct);
        extra.CancelAfter(TimeSpan.FromSeconds(6));
        var supplemental = Task.WhenAll(PreferredUploaders.All.Select(user => TryPreferredArchiveAsync(user, 1, keyword, extra.Token)));
        try
        {
            var standard = await SearchAsync(keyword, page, ct);
            var preferred = await supplemental;
            ct.ThrowIfCancellationRequested();
            return new(PreferredUploaders.RankSearch(preferred.SelectMany(result => result.Items).Concat(standard.Items), keyword), standard.HasMore);
        }
        finally { extra.Cancel(); await supplemental; }
    }

    public async Task<PageResult<Track>> GetPersonalizedMusicAsync(int page, CancellationToken ct = default)
    {
        RequireAccount();
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (page == 1) _homeRotation = Random.Shared.Next(15);
        var cursor = page - 1 + _homeRotation;
        var group = cursor % 3; var round = cursor / 3;
        using var extra = CancellationTokenSource.CreateLinkedTokenSource(ct);
        extra.CancelAfter(TimeSpan.FromSeconds(6));
        var supplemental = Task.WhenAll(PreferredUploaders.All.Skip(group * 3).Take(3)
            .Select(user => TryPreferredArchiveAsync(user, round / 5 + 1, "", extra.Token)));
        try
        {
            PageResult<Track> channel;
            Exception? failure = null;
            try { channel = await GetMusicChannelAsync(page, ct); }
            catch (Exception error) when (error is HttpRequestException or BilibiliException or InvalidDataException or InvalidOperationException)
            { channel = new([], false); failure = error; }
            var batches = await supplemental;
            ct.ThrowIfCancellationRequested();
            // Six songs per creator, alternating creators. The cached 30-item page supplies five rounds.
            var slices = batches.Select(batch => batch.Items.Skip(round % 5 * 6).Take(6).ToArray()).ToArray();
            var preferred = Enumerable.Range(0, 6).SelectMany(index => slices.Where(slice => index < slice.Length).Select(slice => slice[index]));
            var items = PreferredUploaders.BlendHome(preferred, channel.Items);
            if (items.Count == 0 && failure is not null) throw failure;
            return new(items, channel.HasMore || batches.Any(batch => batch.HasMore || batch.Items.Count > (round % 5 + 1) * 6));
        }
        finally { extra.Cancel(); await supplemental; }
    }
}
