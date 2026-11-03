namespace BiliPlay.Core;

public enum NoticeLevel { Info, Warn, Error }
public static class Feedback
{
    public static TimeSpan Lifetime(NoticeLevel level) => TimeSpan.FromSeconds(level == NoticeLevel.Info ? 5 : 7);
    public const string RequestHint = "可能请求频繁被 B 站风控，请稍后再试。";
    public static string WithRequestHint(string message)
    {
        if (message.Contains(RequestHint, StringComparison.Ordinal)) return message;
        return new[] { "音源", "音频", "资源地址", "CDN", "风控", "限制了访问", "请求被拦截", "412", "429", "-352" }
            .Any(term => message.Contains(term, StringComparison.OrdinalIgnoreCase)) ? message + " " + RequestHint : message;
    }
}

public static class QueueSelection
{
    public static bool Same(Track? left, Track? right) => left is not null && right is not null && left.Bvid == right.Bvid && left.Page == right.Page;
    public static Track? Resolve(IReadOnlyList<Track> queue, Track? current, int fallbackIndex = 0) =>
        queue.FirstOrDefault(track => Same(track, current)) ?? (queue.Count == 0 ? null : queue[Math.Clamp(fallbackIndex, 0, queue.Count - 1)]);
}
