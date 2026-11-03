using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BiliPlay.Core;

/// <summary>A loopback-only relay that forwards byte ranges without buffering entire files.</summary>
public sealed class AudioProxy : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, AudioStream> _sources = new();
    private readonly SemaphoreSlim _slots = new(8);
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly int _port;
    private bool _disposed;

    public AudioProxy(HttpMessageHandler? handler = null)
    {
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync();
    }
    public Uri SetSource(AudioStream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Text.IsMediaHost(stream.Url)) throw new ArgumentException("Unsupported audio host.", nameof(stream));
        var key = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        _sources.Clear();
        _sources[key] = stream;
        return new($"http://127.0.0.1:{_port}/{key}");
    }
    public void Clear() => _sources.Clear();
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (!_slots.Wait(0)) { client.Dispose(); continue; }
                _clients[client] = 0;
                _ = HandleAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }
    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                var network = client.GetStream();
                using var reader = new StreamReader(network, Encoding.ASCII, false, 1024, leaveOpen: true);
                var first = await reader.ReadLineAsync(requestTimeout.Token);
                var parts = first?.Split(' ');
                if (parts is not { Length: 3 } || parts[0] is not ("GET" or "HEAD"))
                { await ErrorAsync(network, 405, "Method Not Allowed"); return; }
                string? range = null, ifRange = null;
                var headerBytes = first!.Length;
                while (true)
                {
                    var line = await reader.ReadLineAsync(requestTimeout.Token);
                    if (line is null) return;
                    if (line.Length == 0) break;
                    headerBytes += line.Length;
                    if (headerBytes > 16384) { await ErrorAsync(network, 431, "Request Header Fields Too Large"); return; }
                    var colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    var name = line[..colon]; var value = line[(colon + 1)..].Trim();
                    if (name.Equals("Range", StringComparison.OrdinalIgnoreCase)) range = value;
                    if (name.Equals("If-Range", StringComparison.OrdinalIgnoreCase)) ifRange = value;
                    if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                    { await ErrorAsync(network, 403, "Forbidden"); return; }
                }
                if (!_sources.TryGetValue(parts[1].TrimStart('/'), out var source))
                { await ErrorAsync(network, 404, "Not Found"); return; }
                using var streamTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                streamTimeout.CancelAfter(TimeSpan.FromHours(12));
                HttpResponseMessage? upstream = null;
                using var connectionBudget = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                connectionBudget.CancelAfter(TimeSpan.FromSeconds(30));
                foreach (var candidate in new[] { source.Url }.Concat(source.Backups).Distinct())
                {
                    var url = candidate;
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(connectionBudget.Token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(8));
                    try
                    {
                        for (var redirect = 0; redirect < 5 && Text.IsMediaHost(url); redirect++)
                        {
                            using var request = new HttpRequestMessage(parts[0] == "HEAD" ? HttpMethod.Head : HttpMethod.Get, url);
                            request.Headers.UserAgent.ParseAdd(BilibiliClient.UserAgent);
                            request.Headers.Referrer = new("https://www.bilibili.com/");
                            if (range is not null)
                            {
                                if (!System.Net.Http.Headers.RangeHeaderValue.TryParse(range, out var parsed))
                                { await ErrorAsync(network, 400, "Bad Request"); return; }
                                request.Headers.Range = parsed;
                            }
                            if (ifRange is not null && System.Net.Http.Headers.RangeConditionHeaderValue.TryParse(ifRange, out var condition)) request.Headers.IfRange = condition;
                            upstream = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token);
                            if ((int)upstream.StatusCode is 301 or 302 or 303 or 307 or 308 && upstream.Headers.Location is { } location)
                            {
                                var next = new Uri(url, location);
                                upstream.Dispose(); upstream = null;
                                if (!Text.IsMediaHost(next)) break;
                                url = next; continue;
                            }
                            if (!upstream.IsSuccessStatusCode && upstream.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
                            { upstream.Dispose(); upstream = null; }
                            break;
                        }
                    }
                    catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !_stop.IsCancellationRequested)
                    { upstream?.Dispose(); upstream = null; }
                    if (upstream is not null || connectionBudget.IsCancellationRequested) break;
                }
                if (upstream is null) { await ErrorAsync(network, 502, "Bad Gateway"); return; }
                using (upstream)
                {
                    var headers = new StringBuilder($"HTTP/1.1 {(int)upstream.StatusCode} {upstream.ReasonPhrase}\r\nConnection: close\r\nCache-Control: no-store\r\n");
                    foreach (var name in new[] { "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified" })
                    {
                        if (upstream.Content.Headers.TryGetValues(name, out var values) || upstream.Headers.TryGetValues(name, out values))
                            headers.Append(name).Append(": ").Append(string.Join(", ", values)).Append("\r\n");
                    }
                    headers.Append("\r\n");
                    await network.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), streamTimeout.Token);
                    if (parts[0] != "HEAD") await upstream.Content.CopyToAsync(network, streamTimeout.Token);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or HttpRequestException or OperationCanceledException or ObjectDisposedException) { }
            finally { _clients.TryRemove(client, out _); _slots.Release(); }
        }
    }
    private static async Task ErrorAsync(Stream output, int code, string reason) =>
        await output.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel(); _listener.Stop(); _http.Dispose();
        foreach (var client in _clients.Keys) client.Dispose();
        _sources.Clear();
    }
}
