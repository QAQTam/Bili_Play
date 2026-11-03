namespace BiliPlay.Core;

public enum QueueChange { Cancel, Append, Replace }
public enum TrackPlayBehavior { Append, Replace, Ask }

public static class QueuePolicy
{
    public static QueueChange? AutomaticChange(TrackPlayBehavior behavior) => behavior switch
    {
        TrackPlayBehavior.Replace => QueueChange.Replace,
        TrackPlayBehavior.Ask => null,
        _ => QueueChange.Append
    };
    public static IReadOnlyList<Track> Apply(IReadOnlyList<Track> current, IReadOnlyList<Track> incoming, bool replace, Track selected)
    {
        var result = replace ? new List<Track>() : current.ToList();
        foreach (var track in incoming) if (!result.Any(item => QueueSelection.Same(item, track))) result.Add(track);
        var index = result.FindIndex(item => QueueSelection.Same(item, selected));
        if (index >= 0) result[index] = selected; else result.Add(selected);
        return result;
    }
}
