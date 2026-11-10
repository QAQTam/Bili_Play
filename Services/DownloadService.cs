using BiliPlay.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace BiliPlay.Services;

public sealed record DownloadOptions(Track Track, AudioStream Source, int Bitrate);

public sealed class DownloadService(Func<AudioProxy>? createProxy = null)
{
    public static string CacheDirectory(long account) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic, Environment.SpecialFolderOption.DoNotVerify), AppProfile.DownloadDirectoryName);
    public static bool IsCached(long account, Track track)
    {
        if (string.IsNullOrEmpty(track.LocalFile)) return false;
        return string.Equals(Path.GetExtension(track.LocalFile), ".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(track.LocalFile);
    }
    public async Task<Track> DownloadAsync(BilibiliClient client, long account, Track track, IProgress<double>? progress, CancellationToken ct, string? destinationDirectory = null, Action<Track>? committed = null, DownloadOptions? options = null)
    {
        ct.ThrowIfCancellationRequested();
        if (track.Cid == 0) track = (await client.GetVideoAsync(track.Bvid, track.Aid, ct)).Parts.FirstOrDefault(t => t.Page == track.Page)
            ?? throw new InvalidOperationException("没有找到这个分 P。");
        if (Text.ParseVideoInput(track.Bvid) is null) throw new InvalidDataException("视频编号无效。");
        var root = Path.GetFullPath(destinationDirectory ?? CacheDirectory(account)); Directory.CreateDirectory(root);
        var target = Path.Combine(root, $"{track.Bvid}_p{track.Page}.mp3");
        ct.ThrowIfCancellationRequested();
        if (File.Exists(target)) return track with { LocalFile = target };
        var source = Path.Combine(root, Guid.NewGuid().ToString("N") + ".biliplay.part.m4a");
        var staging = Path.Combine(root, Guid.NewGuid().ToString("N") + ".biliplay.part.mp3");
        try
        {
            var audio = options?.Source ?? await client.GetAudioAsync(track, ct);
            if (options is null) audio = audio.Fallback ?? audio;
            if (audio.Quality.StartsWith("Hi-Res", StringComparison.Ordinal) && client.Account?.IsVip != true)
                throw new InvalidOperationException("Hi-Res 音源需要哔哩哔哩大会员。");
            using var proxy = createProxy?.Invoke() ?? new AudioProxy();
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await http.GetAsync(proxy.SetSource(audio), HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(source))
            {
                var total = response.Content.Headers.ContentLength ?? 0;
                var buffer = new byte[81920]; long copied = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                { await output.WriteAsync(buffer.AsMemory(0, read), ct); copied += read; if (total > 0) progress?.Report(70d * copied / total); }
            }
            ct.ThrowIfCancellationRequested();
            await ConvertToMp3Async(source, staging, new Progress<double>(value => progress?.Report(70 + value * .3)), ct, options?.Bitrate ?? 192000);
            ct.ThrowIfCancellationRequested();
            File.Move(staging, target);
            var downloaded = track with { LocalFile = target }; committed?.Invoke(downloaded); return downloaded;
        }
        finally
        {
            await DeleteFileAsync(source); await DeleteFileAsync(staging);
        }
    }
    public static async Task DeleteFileAsync(string file)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(file);
                // Windows can leave a delete-pending directory entry until the
                // decoder or scanner releases its last handle. Wait for removal.
                var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
                if (!Directory.EnumerateFiles(folder,Path.GetFileName(file)).Any()) return;
                if (attempt >= 8) throw new IOException("文件仍在被使用，暂时无法完成删除。");
            }
            catch (DirectoryNotFoundException) { return; }
            catch (IOException) when (attempt < 8) { }
            await Task.Delay(100);
        }
    }
    public static async Task DeleteCachedFilesAsync(Track track)
    {
        if (string.IsNullOrEmpty(track.LocalFile) || !string.Equals(Path.GetExtension(track.LocalFile), ".mp3", StringComparison.OrdinalIgnoreCase)) return;
        await DeleteFileAsync(track.LocalFile);
        await DeleteFileAsync(Path.ChangeExtension(track.LocalFile, ".subtitles.json"));
    }
    public static async Task ConvertToMp3Async(string source, string destination, IProgress<double>? progress, CancellationToken ct, int bitrate = 192000)
    {
        ct.ThrowIfCancellationRequested();
        if (bitrate is not (192000 or 320000)) throw new ArgumentOutOfRangeException(nameof(bitrate));
        var inputFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source));
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        ct.ThrowIfCancellationRequested();
        var outputFile = await folder.CreateFileAsync(Path.GetFileName(destination), CreationCollisionOption.FailIfExists);
        var profile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
        profile.Audio.Bitrate = (uint)bitrate;
        var prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(inputFile, outputFile, profile).AsTask(ct);
        if (!prepared.CanTranscode) throw new InvalidOperationException("系统无法将这个音轨转换为 MP3（" + prepared.FailureReason + "）。");
        await prepared.TranscodeAsync().AsTask(ct, progress ?? new Progress<double>());
    }

}
