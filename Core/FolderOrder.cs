namespace BiliPlay.Core;

// The first server snapshot seeds the order; later snapshots update data, not existing positions.
public static class FolderOrder
{
    public static IReadOnlyList<Folder> Merge(IEnumerable<Folder> latest, IEnumerable<long> saved)
    {
        var folders = latest.DistinctBy(folder => folder.Id).ToArray();
        var byId = folders.ToDictionary(folder => folder.Id);
        var result = new List<Folder>();
        foreach (var id in saved.Distinct()) if (byId.Remove(id, out var folder)) result.Add(folder);
        foreach (var folder in folders) if (byId.Remove(folder.Id)) result.Add(folder);
        return result;
    }
}
