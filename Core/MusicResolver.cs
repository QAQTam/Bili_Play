using System.Text.Json;

namespace BiliPlay.Core;

public sealed record ResolvedMusicTrack(
    Track Track,
    string Quality,
    string MimeType,
    bool HasHiRes,
    string StreamUrl,
    string? ProxyUrl,
    string? SelectedSubtitleLanguage,
    IReadOnlyList<SubtitleTrack> SubtitleTracks,
    IReadOnlyList<SubtitleLine> Lyrics);

public sealed record ResolverRequest(
    string Op,
    string? Input = null,
    string? Keyword = null,
    int Page = 1,
    bool PreferHiRes = true,
    bool Proxy = true,
    bool Lyrics = true,
    IReadOnlyList<SavedCookie>? Cookies = null);

public sealed record ResolverResponse(
    bool Ok,
    string Op,
    string? Error = null,
    Account? Account = null,
    ResolvedMusicTrack? Resolved = null,
    PageResult<Track>? Search = null);

/// <summary>Headless, Native-AOT-compatible music resolver and local stream relay controller.</summary>
public sealed class MusicResolver : IDisposable
{
    public BilibiliClient Client { get; }
    private readonly bool _ownsClient;
    private readonly Func<AudioProxy> _createProxy;
    private AudioProxy? _proxy;
    private bool _disposed;

    public MusicResolver(BilibiliClient? client = null, Func<AudioProxy>? createProxy = null)
    {
        _ownsClient = client is null;
        Client = client ?? new BilibiliClient();
        _createProxy = createProxy ?? (() => new AudioProxy());
    }

    public AudioProxy Proxy
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _proxy ??= _createProxy();
        }
    }

    public void ImportSession(IEnumerable<SavedCookie> cookies)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Client.ImportSession(cookies);
    }

    public async Task<ResolvedMusicTrack> ResolveAsync(
        string input,
        bool preferHiRes = true,
        bool startProxy = true,
        bool includeLyrics = true,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var parsed = Text.ParseVideoInput(input ?? "")
            ?? throw new InvalidDataException("请输入有效的 BV 号或 bilibili.com/video/BV… 链接。");
        var video = await Client.GetVideoAsync(parsed.Bvid, ct: ct).ConfigureAwait(false);
        var part = video.Parts.FirstOrDefault(p => p.Page == parsed.Page)
            ?? throw new InvalidOperationException("没有找到这个分 P。");
        var available = await Client.GetAudioAsync(part, ct).ConfigureAwait(false);
        var chosen = AudioQualityPreference.Choose(available, preferHiRes, Client.Account?.IsVip == true);
        var proxyUrl = startProxy ? Proxy.SetSource(chosen).AbsoluteUri : null;
        IReadOnlyList<SubtitleTrack> subtitleTracks = [];
        IReadOnlyList<SubtitleLine> lyrics = [];
        string? selectedLanguage = null;
        if (includeLyrics)
        {
            try
            {
                subtitleTracks = await Client.GetSubtitleTracksAsync(part, ct).ConfigureAwait(false);
                if (SubtitleSelection.Choose(subtitleTracks, part.Title) is { } selected)
                {
                    selectedLanguage = selected.Language;
                    lyrics = await Client.GetSubtitlesAsync(selected, ct).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Subtitle failures must never block audio resolution or stream relay.
            }
        }
        return new(
            part,
            chosen.Quality,
            chosen.MimeType,
            available.HasHiRes || AudioQualityPreference.IsHiRes(available.Quality),
            chosen.Url.AbsoluteUri,
            proxyUrl,
            selectedLanguage,
            subtitleTracks,
            lyrics);
    }

    public void StopProxy() => _proxy?.Clear();

    public async Task<ResolverResponse> ExecuteAsync(ResolverRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var op = (request.Op ?? "").Trim().ToLowerInvariant();
        try
        {
            switch (op)
            {
                case "session":
                    if (request.Cookies is { Count: > 0 }) Client.ImportSession(request.Cookies);
                    var account = await Client.GetAccountAsync(ct).ConfigureAwait(false);
                    return new(true, op, Account: account);
                case "resolve":
                    var resolved = await ResolveAsync(
                        request.Input ?? "",
                        request.PreferHiRes,
                        request.Proxy,
                        request.Lyrics,
                        ct).ConfigureAwait(false);
                    return new(true, op, Resolved: resolved);
                case "search":
                    var keyword = (request.Keyword ?? request.Input ?? "").Trim();
                    if (keyword.Length == 0) throw new InvalidDataException("搜索关键词不能为空。");
                    var page = Math.Max(1, request.Page);
                    var search = await Client.SearchPreferredAsync(keyword, page, ct).ConfigureAwait(false);
                    return new(true, op, Search: search);
                case "stop":
                    StopProxy();
                    return new(true, op);
                case "quit":
                    StopProxy();
                    return new(true, op);
                default:
                    return new(false, op, Error: $"未知操作：{request.Op}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            return new(false, op, Error: error.Message);
        }
    }

    public async Task RunStdioAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!ct.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            line = line.Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            ResolverResponse response;
            try
            {
                var request = JsonSerializer.Deserialize(line, BiliPlayJsonContext.Default.ResolverRequest)
                    ?? throw new JsonException("无效的请求格式。");
                response = await ExecuteAsync(request, ct).ConfigureAwait(false);
                await output.WriteLineAsync(JsonSerializer.Serialize(response, BiliPlayJsonContext.Default.ResolverResponse)).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                if (response.Op == "quit") break;
            }
            catch (JsonException)
            {
                response = new(false, "invalid", Error: "请求 JSON 格式无效。");
                await output.WriteLineAsync(JsonSerializer.Serialize(response, BiliPlayJsonContext.Default.ResolverResponse)).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
        }
    }

    public async Task<int> RunCliAsync(
        IReadOnlyList<string> args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default)
    {
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == "--cookies" && i + 1 < list.Count)
            {
                var cookiesPath = list[i + 1];
                list.RemoveRange(i, 2);
                var cookies = JsonSerializer.Deserialize(await File.ReadAllTextAsync(cookiesPath, ct).ConfigureAwait(false), BiliPlayJsonContext.Default.SavedCookieArray) ?? [];
                ImportSession(cookies);
                if (Client.Account is null) await Client.GetAccountAsync(ct).ConfigureAwait(false);
                break;
            }
        }

        if (list.Count == 0 || list[0] is "--stdio" or "daemon")
        {
            await RunStdioAsync(input, output, ct).ConfigureAwait(false);
            return 0;
        }

        var command = list[0].ToLowerInvariant();
        var preferHiRes = !list.Remove("--no-hires");
        var includeLyrics = !list.Remove("--no-lyrics");
        var page = 1;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == "--page" && i + 1 < list.Count && int.TryParse(list[i + 1], out var parsedPage))
            {
                page = Math.Max(1, parsedPage);
                list.RemoveRange(i, 2);
                break;
            }
        }

        if (command is "resolve" or "proxy")
        {
            if (list.Count < 2)
            {
                await error.WriteLineAsync("用法：BiliPlay.Resolver resolve|proxy <BV号或链接> [--cookies cookies.json] [--no-hires] [--no-lyrics]").ConfigureAwait(false);
                return 2;
            }
            var startProxy = command == "proxy";
            var response = await ExecuteAsync(new("resolve", Input: list[1], PreferHiRes: preferHiRes, Proxy: startProxy, Lyrics: includeLyrics), ct).ConfigureAwait(false);
            await output.WriteLineAsync(JsonSerializer.Serialize(response, BiliPlayJsonContext.Default.ResolverResponse)).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            if (!response.Ok) return 1;
            if (startProxy)
            {
                // Keep the local loopback stream alive until EOF or cancellation.
                try { await input.ReadLineAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            return 0;
        }

        if (command == "search")
        {
            if (list.Count < 2)
            {
                await error.WriteLineAsync("用法：BiliPlay.Resolver search <关键词> [--page N] [--cookies cookies.json]").ConfigureAwait(false);
                return 2;
            }
            var response = await ExecuteAsync(new("search", Keyword: list[1], Page: page), ct).ConfigureAwait(false);
            await output.WriteLineAsync(JsonSerializer.Serialize(response, BiliPlayJsonContext.Default.ResolverResponse)).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            return response.Ok ? 0 : 1;
        }

        await error.WriteLineAsync($"未知命令：{list[0]}").ConfigureAwait(false);
        return 2;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _proxy?.Dispose();
        if (_ownsClient) Client.Dispose();
    }
}
