using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BiliPlay.Core;

/// <summary>Serializes a native incremental source's requests and stops zero-result retry loops.</summary>
public class PagedCollection<T> : ObservableCollection<T>
{
    public Func<bool> CanLoadMore { get; set; } = () => false;
    public Func<CancellationToken, Task> FetchPage { get; set; } = _ => Task.CompletedTask;
    private Task<uint>? _activeLoad;
    private bool _paused;
    private int _generation;
    public bool HasMoreItems => !_paused && CanLoadMore();
    public void NotifyHasMore() => OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasMoreItems)));
    public void ResetLoading() { _generation++; _paused = false; NotifyHasMore(); }
    public Task<uint> LoadPageAsync(CancellationToken ct = default)
    {
        if (_activeLoad is not null) return _activeLoad;
        var result = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeLoad = result.Task;
        _ = LoadCoreAsync(ct, result);
        return result.Task;
    }
    private async Task LoadCoreAsync(CancellationToken ct, TaskCompletionSource<uint> result)
    {
        var generation = _generation;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!HasMoreItems) { result.TrySetResult(0); return; }
            var before = Count;
            await FetchPage(ct);
            if (generation != _generation) { result.TrySetResult(0); return; }
            var added = (uint)Math.Max(0, Count - before);
            if (added == 0 && CanLoadMore()) _paused = true;
            result.TrySetResult(added);
        }
        catch (OperationCanceledException) { result.TrySetCanceled(ct); }
        catch (Exception error) { result.TrySetException(error); }
        finally { _activeLoad = null; NotifyHasMore(); }
    }
}
