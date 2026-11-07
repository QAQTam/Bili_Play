using System.Net;
using System.Text.Json;
using System.Collections.Concurrent;

namespace BiliPlay.Core;

public sealed class BilibiliException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
    public bool RequiresLogin => Code is -101 or -111;
}

public sealed partial class BilibiliClient : IDisposable
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private sealed record SigningKeys(string Image,string Sub,DateTimeOffset At);
    private SigningKeys? _keys;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, Video Video)> _videoCache = new();
    private static readonly Uri Site = new("https://www.bilibili.com/");
    private static readonly Uri Api = new("https://api.bilibili.com/");
    public Account? Account { get; private set; }

    public BilibiliClient(HttpMessageHandler? handler = null)
    {
        _http = new(handler ?? new HttpClientHandler
        {
            CookieContainer = _cookies,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false
        }) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Referrer = Site;
    }

    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        var uri = Site;
        for (var redirect = 0; redirect < 5; redirect++)
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
            {
                var next = new Uri(uri, location);
                if (next.Scheme != "https" || !(next.Host == "bilibili.com" || next.Host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("B 站首页返回了不受支持的跳转。");
                uri = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            return;
        }
        throw new HttpRequestException("Bilibili homepage redirected too many times.");
    }

    public IReadOnlyList<SavedCookie> ExportSession() => _cookies.GetAllCookies().Cast<Cookie>()
        .Where(c => c.Domain.TrimStart('.').Equals("bilibili.com", StringComparison.OrdinalIgnoreCase) && !c.Expired)
        .Select(c => new SavedCookie(c.Name, c.Value, c.Domain, c.Path, c.Expires)).ToArray();

    public void ImportSession(IEnumerable<SavedCookie> session)
    {
        _commentVerifiedAccount = 0;
        foreach (var item in session)
        {
            if (item.Domain.TrimStart('.') != "bilibili.com") continue;
            if (item.Expires != DateTime.MinValue && item.Expires.ToUniversalTime() <= DateTime.UtcNow) continue;
            try { _cookies.Add(new Cookie(item.Name, item.Value, item.Path, item.Domain) { Secure = true, Expires = item.Expires }); }
            catch (CookieException) { /* Ignore an expired or malformed stored cookie. */ }
        }
    }

    public void ClearSession()
    {
        foreach (Cookie c in _cookies.GetAllCookies()) c.Expired = true;
        Account = null;
        _commentVerifiedAccount = 0;
        Volatile.Write(ref _keys,null);
    }

    public void RestoreCachedAccount(Account account) => Account = account;

    public async Task<Account> GetAccountAsync(CancellationToken ct = default)
    {
        var data = await GetAsync("x/web-interface/nav", null, false, ct);
        if (!data.B("isLogin")) throw new BilibiliException(-101, "登录已过期，请重新扫码登录。");
        RememberKeys(data);
        Account = new(data.N("mid"), data.S("uname"), data.S("face"),
            (int)data.O("level_info").N("current_level"), data.N("vipStatus") == 1 || data.O("vip").N("status") == 1);
        return Account;
    }

    public async Task<LoginCode> CreateLoginCodeAsync(CancellationToken ct = default)
    {
        var data = await GetAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/generate", null, false, ct);
        var url = Text.Https(data.S("url"));
        var key = data.S("qrcode_key");
        if (!(url.Host == "bilibili.com" || url.Host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase)) || key.Length == 0)
            throw new InvalidDataException("B 站返回了无效的登录二维码，请重试。");
        return new(url.AbsoluteUri, key);
    }

    public async Task<int> PollLoginAsync(string key, CancellationToken ct = default)
    {
        var data = await GetAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/poll", new() { ["qrcode_key"] = key }, false, ct);
        var code = (int)data.N("code");
        if (code == 0 && Uri.TryCreate(data.S("url"), UriKind.Absolute, out var url))
        {
            // Some responses expose the session in the cross-domain URL instead of Set-Cookie.
            var query = Text.ParseQuery(url.Query);
            foreach (var name in new[] { "SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5" })
                if (_cookies.GetCookies(Api)[name] is null && query.TryGetValue(name, out var value))
                    _cookies.Add(new Cookie(name, value, "/", ".bilibili.com") { Secure = true, Expires = DateTime.UtcNow.AddMonths(6) });
        }
        return code;
    }

    public async Task<IReadOnlyList<Folder>> GetFoldersAsync(CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/v3/fav/folder/created/list-all", new() { ["up_mid"] = Account!.Id.ToString(), ["type"] = "2" }, false, ct);
        var folders = data.O("list").A().Select(x => ParseFolder(x, true)).ToList();
        // Subscribed folders are displayed too, but are never offered as write targets.
        for (var page = 1; ; page++)
        {
            var collected = await GetAsync("x/v3/fav/folder/collected/list", new()
            { ["up_mid"] = Account.Id.ToString(), ["pn"] = page.ToString(), ["ps"] = "20", ["platform"] = "web" }, false, ct);
            var items = collected.O("list").A().ToArray();
            folders.AddRange(items.Select(x => ParseFolder(x, false)));
            if (!collected.B("has_more") || items.Length == 0) break;
        }
        return folders.DistinctBy(f => f.Id).ToArray();
    }

    public async Task<PageResult<Track>> GetFolderTracksAsync(long id, int page, CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/v3/fav/resource/list", new()
        { ["media_id"] = id.ToString(), ["pn"] = page.ToString(), ["ps"] = "20", ["order"] = "mtime", ["type"] = "0", ["platform"] = "web" }, false, ct);
        var tracks = data.O("medias").A().Where(x => x.N("type") == 2).Select(x => new Track(
            x.N("id"), x.S("bvid"), Text.Clean(x.S("title")), x.O("upper").S("name"),
            ImageUrl(x.S("cover")), x.N("duration"), UploaderId: x.O("upper").N("mid"))).ToArray();
        return new(tracks, data.B("has_more"));
    }

    public async Task<Folder> GetFolderPreviewAsync(Folder folder, CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/v3/fav/resource/list", new()
        { ["media_id"] = folder.Id.ToString(), ["pn"] = "1", ["ps"] = "3", ["order"] = "mtime", ["type"] = "0", ["platform"] = "web" }, false, ct);
        var info = data.O("info").N("id") > 0 ? ParseFolder(data.O("info"), folder.Owned) : folder;
        var covers = data.O("medias").A().Select(x => ImageUrl(x.S("cover"))).Where(s => s.Length > 0).Take(3).ToArray();
        return folder with
        {
            IsPrivate = info.IsPrivate, Count = info.Count,
            Cover = covers.ElementAtOrDefault(0) ?? info.Cover,
            CoverMiddle = covers.ElementAtOrDefault(1) ?? "", CoverBack = covers.ElementAtOrDefault(2) ?? ""
        };
    }

    public async Task<PageResult<Track>> SearchAsync(string keyword, int page, CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/web-interface/wbi/search/type", new()
        { ["search_type"] = "video", ["keyword"] = keyword, ["page"] = page.ToString(), ["page_size"] = "20", ["order"] = "totalrank" }, true, ct);
        var items = data.O("result").A().Select(x => new Track(x.N("aid"), x.S("bvid"), Text.Clean(x.S("title")),
            x.S("author"), ImageUrl(x.S("pic")), Text.ParseDuration(x.S("duration")), UploaderId: x.N("mid"))).ToArray();
        return new(PreferredUploaders.RankSearch(items, keyword), page < data.N("numPages"));
    }

    public async Task<PageResult<Uploader>> SearchUploadersAsync(string keyword, int page, CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/web-interface/wbi/search/type", new()
        { ["search_type"] = "bili_user", ["user_type"] = "1", ["keyword"] = keyword, ["page"] = page.ToString(), ["page_size"] = "20" }, true, ct);
        var items = data.O("result").A().Where(x => x.N("mid") > 0).Select(x => new Uploader(
            x.N("mid"), Text.Clean(x.S("uname")), ImageUrl(x.S("upic")), Text.Clean(x.S("usign")), x.N("fans"), x.N("videos"))).ToArray();
        return new(items.OrderByDescending(user => PreferredUploaders.Contains(user.Id)).ToArray(), page < data.N("numPages"));
    }
    public async Task<bool> IsFavoritedAsync(long aid, CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/web-interface/archive/relation", new() { ["aid"] = aid.ToString() }, false, ct);
        return data.B("favorite");
    }

    public async Task<Video> GetVideoAsync(string bvid, long aid = 0, CancellationToken ct = default)
    {
        RequireAccount();
        var cacheKey = string.IsNullOrEmpty(bvid) ? $"av{aid}" : bvid;
        if (_videoCache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(15))
            return cached.Video;
        var args = string.IsNullOrEmpty(bvid) ? new Dictionary<string, string> { ["aid"] = aid.ToString() } : new() { ["bvid"] = bvid };
        Video video;
        try
        {
            video = ParseVideo(await GetAsync("x/web-interface/wbi/view", args, true, ct));
            if (!string.IsNullOrEmpty(bvid) && video.Track.Bvid != bvid) throw new InvalidDataException("接口返回的视频与所选内容不一致。");
        }
        catch (Exception error) when (error is BilibiliException or HttpRequestException or InvalidDataException or JsonException)
        {
            if (string.IsNullOrEmpty(bvid)) throw;
            video = ParseVideo(VideoPage.ReadJson(await GetVideoPageAsync(bvid, 1, ct), "__INITIAL_STATE__").O("videoData"));
            if (video.Track.Bvid != bvid) throw new InvalidDataException("网页返回的视频与所选内容不一致。");
        }
        if (_videoCache.Count > 256) _videoCache.Clear();
        _videoCache[cacheKey] = (DateTimeOffset.UtcNow, video);
        return video;
    }

    private static Video ParseVideo(JsonElement data)
    {
        var track = new Track(data.N("aid"), data.S("bvid"), Text.Clean(data.S("title")), data.O("owner").S("name"),
            ImageUrl(data.S("pic")), data.N("duration"), UploaderId: data.O("owner").N("mid"));
        var pages = data.O("pages").A().ToArray();
        var parts = pages.Select(x => track with
        { Cid = x.N("cid"), Page = (int)x.N("page"), Part = x.S("part"), Duration = x.N("duration"), PartCount = pages.Length }).ToArray();
        if (parts.Length == 0 || string.IsNullOrEmpty(track.Bvid)) throw new InvalidDataException("这个视频没有可播放的分 P，网页也未提供有效内容。");
        var season = data.O("ugc_season");
        var collection = season.O("sections").A().SelectMany(x => x.O("episodes").A()).Select(x =>
        {
            var arc = x.O("arc"); var page = x.O("page");
            return new Track(x.N("aid"), x.S("bvid", arc.S("bvid")), Text.Clean(x.S("title", arc.S("title"))),
                arc.O("author").S("name", track.Uploader), ImageUrl(arc.S("pic", x.S("cover", x.S("bvid") == track.Bvid ? track.Cover : ""))), arc.N("duration"),
                x.N("cid") > 0 ? x.N("cid") : page.N("cid"), UploaderId: arc.O("author").N("mid") > 0 ? arc.O("author").N("mid") : track.UploaderId);
        }).Where(t => !string.IsNullOrEmpty(t.Bvid)).DistinctBy(t => t.Bvid).ToArray();
        var category = (int)data.N("tid_v2");
        return new(track, parts, category > 0 ? category : (int)data.N("tid"), data.S("tname_v2", data.S("tname")),
            collection, season.N("id"), season.S("title"));
    }
    private async Task<string> GetVideoPageAsync(string bvid, int page, CancellationToken ct)
    {
        if (Text.ParseVideoInput(bvid) is null) throw new InvalidDataException("视频编号无效。");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Site, $"video/{bvid}/?p={Math.Max(1, page)}"));
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
    public async Task<AudioStream> GetPageAudioAsync(Track track, CancellationToken ct = default)
    {
        RequireAccount();
        var root = VideoPage.ReadJson(await GetVideoPageAsync(track.Bvid, track.Page, ct), "__playinfo__");
        if (root.N("code") != 0) throw new InvalidOperationException("视频网页没有提供可用音源。" );
        return ReadAudio(root.O("data"));
    }

    public async Task<PageResult<Track>> GetMusicChannelAsync(int page, CancellationToken ct = default)
    {
        RequireAccount();
        var musicPage = new Uri(Site, "c/music/");
        if (page == 1)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, musicPage);
                    request.Headers.Referrer = musicPage;
                    using var response = await _http.SendAsync(request, timeout.Token);
                    if ((int)response.StatusCode is 412 or 429)
                        throw new BilibiliException(-352, "音乐频道暂时限制了访问，请稍后重试。");
                    response.EnsureSuccessStatusCode();
                    var html = System.Text.Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(timeout.Token));
                    var tracks = MusicPage.Parse(html);
                    if (tracks.Count == 0) throw new InvalidDataException("音乐频道页面暂未返回推荐内容，请稍后刷新。");
                    return new(tracks, true);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (attempt > 0) throw new InvalidOperationException("音乐频道连接超时，请稍后刷新。已加载的推荐会继续保留。");
                }
                catch (HttpRequestException) when (attempt == 0) { }
                await Task.Delay(400, ct);
            }
        }
        // Parameters and archive fields used by bilibili.com/c/music/'s channel bundle.
        var args = new Dictionary<string, string>
        {
            ["display_id"] = page.ToString(), ["request_cnt"] = "15", ["from_region"] = "1003",
            ["device"] = "web", ["plat"] = "30", ["web_location"] = "333.40138"
        };
        var query = string.Join("&", args.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        using var feed = new HttpRequestMessage(HttpMethod.Get, new Uri(Api, "x/web-interface/region/feed/rcmd?" + query));
        feed.Headers.Referrer = musicPage;
        using var result = await _http.SendAsync(feed, ct);
        JsonElement data;
        try { data = await ReadResponseAsync(result, ct); }
        catch (BilibiliException e) when (e.Code == 62013) { return new([], false); }
        var items = data.O("archives").A().Where(x => !string.IsNullOrEmpty(x.S("bvid")))
            .Select(x => new Track(x.N("aid"), x.S("bvid"), Text.Clean(x.S("title")), x.O("author").S("name"),
                ImageUrl(x.S("cover")), x.N("duration"), UploaderId: x.O("author").N("mid"))).ToArray();
        return new(items, items.Length > 0);
    }

    public async Task<(long Following, long Followers)> GetSocialCountsAsync(CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/relation/stat", new() { ["vmid"] = Account!.Id.ToString() }, false, ct);
        return (Math.Max(0, data.N("following")), Math.Max(0, data.N("follower")));
    }
    public async Task<PageResult<SocialUser>> GetSocialUsersAsync(bool followers, int page = 1, CancellationToken ct = default)
    {
        RequireAccount();
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        const int size = 50;
        var data = await GetAsync(followers ? "x/relation/followers" : "x/relation/followings", new()
        {
            ["vmid"] = Account!.Id.ToString(), ["pn"] = page.ToString(), ["ps"] = size.ToString(), ["order"] = "desc"
        }, false, ct);
        var users = data.O("list").A().Where(item => item.N("mid") > 0)
            .Select(item => new SocialUser(item.N("mid"), Text.Clean(item.S("uname")), ImageUrl(item.S("face")), Text.Clean(item.S("sign"))))
            .DistinctBy(user => user.Id).ToArray();
        var total = data.N("total");
        return new(users, users.Length > 0 && (total > 0 ? (long)page * size < total : users.Length == size));
    }

    public async Task<bool> IsFollowingAsync(long user, CancellationToken ct = default)
    {
        RequireAccount();
        var relation = await GetAsync("x/relation", new() { ["fid"] = user.ToString() }, false, ct);
        return relation.N("attribute") is 2 or 6;
    }
    public async Task FollowAsync(long user, bool follow, CancellationToken ct = default)
    {
        if (user <= 0 || user == Account?.Id) throw new InvalidOperationException("请选择其他 UP 主。");
        _ = await PostAsync("x/relation/modify", new()
        { ["fid"] = user.ToString(), ["act"] = follow ? "1" : "2", ["re_src"] = "11" }, ct);
    }

    public async Task<AudioStream> GetAudioAsync(Track track, CancellationToken ct = default)
    {
        RequireAccount();
        try
        {
            var data = await GetAsync("x/player/wbi/playurl", new()
            { ["bvid"] = track.Bvid, ["avid"] = track.Aid.ToString(), ["cid"] = track.Cid.ToString(), ["fnval"] = Account!.IsVip ? "4048" : "16", ["fnver"] = "0", ["qn"] = "80" }, true, ct);
            return ReadAudio(data);
        }
        catch (Exception error) when (error is BilibiliException or HttpRequestException or InvalidDataException or InvalidOperationException or JsonException)
        {
            try { return await GetPageAudioAsync(track, ct); }
            catch (Exception pageError) when (!ct.IsCancellationRequested && pageError is BilibiliException or HttpRequestException or InvalidDataException or InvalidOperationException or JsonException)
            {
                try
                {
                    var data = await GetAsync("x/player/playurl", new()
                    { ["bvid"] = track.Bvid, ["avid"] = track.Aid.ToString(), ["cid"] = track.Cid.ToString(), ["fnval"] = Account!.IsVip ? "4048" : "16", ["fnver"] = "0", ["qn"] = "80" }, false, ct);
                    return ReadAudio(data);
                }
                catch (Exception alternateError) when (!ct.IsCancellationRequested && alternateError is BilibiliException or HttpRequestException or InvalidDataException or InvalidOperationException or JsonException)
                {
                    if (error is InvalidOperationException && pageError is InvalidDataException)
                        throw new InvalidOperationException("接口和视频网页均未提供可用的独立音频流。", new AggregateException(error, pageError, alternateError));
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(pageError).Throw(); throw;
                }

            }
        }
    }
    private AudioStream ReadAudio(JsonElement data)
    {
        AudioStream? standard = null; InvalidDataException? invalidAddress = null;
        foreach (var audio in data.O("dash").O("audio").A()
            .OrderByDescending(x => x.S("codecs").StartsWith("mp4a", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.N("bandwidth")))
        {
            try { standard = ParseAudio(audio, "标准"); break; }
            catch (InvalidDataException error) { invalidAddress = error; }
        }
        if (standard is null && invalidAddress is not null) throw invalidAddress;
        if (standard is null) throw new InvalidOperationException("这个视频未提供可用的独立音频流，或当前账号没有播放权限。");
        var flacInfo = data.O("dash").O("flac"); var flac = flacInfo.O("audio");
        var hasHiRes = flac.N("id") == 30251 || Account!.IsVip == false && flacInfo.B("display");
        if (Account!.IsVip && flac.N("id") == 30251)
        {
            try { return ParseAudio(flac, "Hi-Res 无损") with { Fallback = standard, HasHiRes = true }; }
            catch (InvalidDataException) { } // A broken lossless address must not hide playable AAC.
        }
        return standard with { HasHiRes = hasHiRes };
    }
    private static AudioStream ParseAudio(JsonElement audio, string quality)
    {
        var backup = audio.O("backupUrl");
        if (backup.ValueKind != JsonValueKind.Array) backup = audio.O("backup_url");
        var candidates = new[] { audio.S("baseUrl"), audio.S("base_url") }
            .Concat(backup.A().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? ""));
        var urls = new List<Uri>();
        foreach (var candidate in candidates)
        {
            try { var url = Text.Https(candidate); if (Text.IsMediaHost(url) && !urls.Contains(url)) urls.Add(url); }
            catch (InvalidDataException) { }
        }
        if (urls.Count == 0) throw new InvalidDataException("B 站没有返回可用的受支持 CDN 音源地址。");
        return new(urls[0], audio.S("mimeType", audio.S("mime_type", "audio/mp4")), urls.Skip(1).ToArray(), quality);
    }

    public async Task<IReadOnlyList<SubtitleTrack>> GetSubtitleTracksAsync(Track track, CancellationToken ct = default)
    {
        RequireAccount();
        var data = await GetAsync("x/player/wbi/v2", new() { ["aid"] = track.Aid.ToString(), ["cid"] = track.Cid.ToString() }, true, ct);
        if (data.B("need_login_subtitle")) throw new BilibiliException(-101, "字幕需要有效的 B 站登录状态，请重新登录。");
        return data.O("subtitle").O("subtitles").A().Where(x => !string.IsNullOrEmpty(x.S("subtitle_url")))
            .Select(x => new SubtitleTrack(x.S("lan"), x.S("lan_doc"), Text.Https(x.S("subtitle_url")),
                x.N("ai_type") > 0 || x.S("lan").StartsWith("ai-", StringComparison.OrdinalIgnoreCase),
                x.O("ai_type").ValueKind == JsonValueKind.Number ? (int)x.N("ai_type") : null).NormalizeOrigin())
            .Where(x => Text.IsMediaHost(x.Url)).OrderBy(x => x.IsAuto)
            .ThenByDescending(x => x.Language.Contains("zh", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public async Task<IReadOnlyList<SubtitleLine>> GetSubtitlesAsync(SubtitleTrack track, CancellationToken ct = default)
    {
        if (!Text.IsMediaHost(track.Url)) throw new InvalidDataException("字幕地址无效。");
        // The cookie container only sends account cookies to bilibili.com, never unrelated CDN domains.
        using var response = await _http.GetAsync(track.Url, ct);
        response.EnsureSuccessStatusCode();
        return Subtitles.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task CreateFolderAsync(string title, bool isPrivate, CancellationToken ct = default) =>
        _ = await PostAsync("x/v3/fav/folder/add", new() { ["title"] = title.Trim(), ["privacy"] = isPrivate ? "1" : "0" }, ct);

    public async Task EditFolderAsync(Folder folder, string title, bool isPrivate, CancellationToken ct = default)
    {
        if (!folder.Owned) throw new InvalidOperationException("订阅的收藏夹只能读取。");
        // Fetch complete metadata so a rename cannot silently clear the original intro/cover.
        var info = await GetAsync("x/v3/fav/folder/info", new() { ["media_id"] = folder.Id.ToString() }, false, ct);
        _ = await PostAsync("x/v3/fav/folder/edit", new()
        {
            ["media_id"] = folder.Id.ToString(), ["title"] = title.Trim(), ["privacy"] = isPrivate ? "1" : "0",
            ["intro"] = info.S("intro"), ["cover"] = info.S("cover")
        }, ct);
    }
    public async Task DeleteFolderAsync(Folder folder, CancellationToken ct = default)
    {
        if (!folder.Owned) throw new InvalidOperationException("订阅的收藏夹只能读取。");
        _ = await PostAsync("x/v3/fav/folder/del", new() { ["media_ids"] = folder.Id.ToString() }, ct);
    }
    public async Task SetFavoriteAsync(long aid, Folder folder, bool add, CancellationToken ct = default)
    {
        if (!folder.Owned) throw new InvalidOperationException("订阅的收藏夹只能读取。");
        _ = await PostAsync("x/v3/fav/resource/deal", new()
        { ["rid"] = aid.ToString(), ["type"] = "2", [add ? "add_media_ids" : "del_media_ids"] = folder.Id.ToString() }, ct);
    }
    private static Folder ParseFolder(JsonElement x, bool owned) => new(x.N("id"), Text.Clean(x.S("title")),
        (int)x.N("media_count"), owned, Folder.IsPrivateAttribute(x.N("attr")), x.S("intro"), ImageUrl(x.S("cover")));
    private static string ImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        try { var uri = Text.Https(url); return Text.IsMediaHost(uri) ? uri.AbsoluteUri : ""; }
        catch (InvalidDataException) { return ""; }
    }
    private void RequireAccount()
    {
        if (Account is null) throw new BilibiliException(-101, "请先登录 B 站账号。");
    }
    private void RememberKeys(JsonElement data)
    {
        var wbi = data.O("wbi_img");
        var image = wbi.S("img_url"); var sub = wbi.S("sub_url");
        if (image.Length == 0 || sub.Length == 0) return;
        Volatile.Write(ref _keys,new SigningKeys(Path.GetFileNameWithoutExtension(new Uri(image).AbsolutePath),
            Path.GetFileNameWithoutExtension(new Uri(sub).AbsolutePath),DateTimeOffset.UtcNow));
    }
    private async Task<string> SignedQueryAsync(Dictionary<string, string> args, CancellationToken ct)
    {
        await _keyLock.WaitAsync(ct);
        try
        {
            var keys=Volatile.Read(ref _keys);
            if (keys is null || DateTimeOffset.UtcNow - keys.At > TimeSpan.FromMinutes(15))
            {
                // Anonymous nav responses also contain keys; GetAccountAsync enforces login separately.
                using var response = await _http.GetAsync(new Uri(Api, "x/web-interface/nav"), ct);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                RememberKeys(doc.RootElement.O("data"));
                keys=Volatile.Read(ref _keys);
            }
            if (keys is null) throw new InvalidDataException("暂时无法获取 B 站请求签名，请稍后重试。");
            return WbiSigner.Sign(args, keys.Image, keys.Sub, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        finally { _keyLock.Release(); }
    }
    private async Task<JsonElement> GetAsync(string path, Dictionary<string, string>? args, bool signed, CancellationToken ct)
    {
        args ??= [];
        var query = signed ? await SignedQueryAsync(args, ct) : string.Join("&", args.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        var uri = path.StartsWith("https://", StringComparison.Ordinal) ? new Uri(path) : new Uri(Api, path);
        using var response = await _http.GetAsync(new UriBuilder(uri) { Query = query }.Uri, ct);
        return await ReadResponseAsync(response, ct);
    }
    private async Task<JsonElement> PostAsync(string path, Dictionary<string, string> args, CancellationToken ct)
    {
        RequireAccount();
        var csrf = _cookies.GetCookies(Api)["bili_jct"]?.Value;
        if (string.IsNullOrEmpty(csrf)) throw new BilibiliException(-111, "登录凭据不完整，请重新扫码登录。");
        args["csrf"] = csrf;
        using var response = await _http.PostAsync(new Uri(Api, path), new FormUrlEncodedContent(args), ct);
        // Writes are never retried automatically; a timeout may have happened after server commit.
        return await ReadResponseAsync(response, ct);
    }
    private static async Task<JsonElement> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if ((int)response.StatusCode is 412 or 429)
            throw new BilibiliException(-352, "B 站暂时限制了访问，请稍后重试，或在官网确认账号状态。");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var code = (int)root.N("code");
        if (code != 0)
        {
            var message = code switch
            {
                -101 => "登录已过期，请重新扫码登录。",
                -111 => "登录校验失败，请重新扫码登录。",
                -352 or -403 => "B 站拒绝了请求，请稍后重试，或在官网确认访问权限。",
                _ => $"B 站返回错误 {code}：{Text.Clean(root.S("message", "请求未完成"))}"
            };
            throw new BilibiliException(code, message);
        }
        var data = root.O("data");
        if (data.ValueKind == JsonValueKind.Undefined) return default;
        if (!string.IsNullOrEmpty(data.S("v_voucher"))) throw new BilibiliException(-352, "当前请求需要在 B 站官网完成验证，请验证后重试。");
        return data.Clone();
    }
    public void Dispose() { _http.Dispose(); _keyLock.Dispose(); }
}
