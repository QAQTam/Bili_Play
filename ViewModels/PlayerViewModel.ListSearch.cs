using System.Collections.ObjectModel;
using BiliPlay.Core;
using BiliPlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListSearchLabel))]
    public partial string ListSearchText { get; set; } = "";
    [ObservableProperty] public partial bool IsListSearching { get; set; }
    [ObservableProperty] public partial string ListSearchStatus { get; set; } = "";
    public bool CanSearchWithinList => IsFolderView || IsSpaceView || IsPlaylistView;
    public string ListSearchLabel => "搜索";
    public string ListSearchPlaceholder => IsFolderView ? "搜索此收藏夹的歌曲、UP 主或 BV 号"
        : IsSpaceView ? "搜索此空间的作品、标题或 BV 号" : "搜索播放列表";
    public ObservableCollection<Track> PlaylistTracks { get; } = [];
    private CancellationTokenSource? _listSearch;
    private Track[]? _listSearchOriginal;
    private IReadOnlyList<Track>? _listSearchCatalog;
    private bool _listOriginalMore;
    private string _listOriginalEmpty = "";
    private bool _resettingListSearch;
    private bool _playlistRefreshScheduled;
    private void SchedulePlaylistRefresh()
    {
        if (_playlistRefreshScheduled) return;
        _playlistRefreshScheduled = true;
        App.DispatcherQueue.TryEnqueue(() =>
        {
            _playlistRefreshScheduled = false;
            if (!_disposed) RefreshPlaylistSearch();
        });
    }
    internal Task ListSearchCompletion { get; private set; } = Task.CompletedTask;

    partial void OnListSearchTextChanged(string value)
    {
        if (_resettingListSearch || _disposed) return;
        ListSearchCompletion = SafeAsync(() => SearchWithinListAsync(value), false);
    }
    private void RefreshPlaylistSearch()
    {
        Replace(PlaylistTracks, Queue.Where(track => TrackSearch.Matches(track, ListSearchText)));
        if (IsPlaylistView) { IsEmpty = PlaylistTracks.Count == 0; EmptyText = ListSearchText.Length > 0 ? "没有找到匹配的内容。" : "播放列表为空。"; }
        OnPropertyChanged(nameof(HasTracks));
    }
    private void ResetListSearch()
    {
        _listSearch?.Cancel(); _listSearch = null; _listSearchOriginal = null; _listSearchCatalog = null;
        _resettingListSearch = true;
        try { ListSearchText = ""; }
        finally { _resettingListSearch = false; }
        IsListSearching = false; ListSearchStatus = "";
        IsFolderSelectionMode = false;
        RefreshPlaylistSearch();
    }
    private Track[] BrowseSnapshot() => _listSearchOriginal ?? Tracks.ToArray();
    private async Task SearchWithinListAsync(string text)
    {
        _listSearch?.Cancel();
        using var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _listSearch = load;
        var query = text.Trim();
        IsListSearching = false; ListSearchStatus = "";
        IsFolderSelectionMode = false;
        if (IsPlaylistView) { RefreshPlaylistSearch(); _listSearch = null; return; }
        if (!CanSearchWithinList) { _listSearch = null; return; }
        if (query.Length == 0)
        {
            _listSearchCatalog = null;
            if (_listSearchOriginal is { } original)
            { Replace(Tracks, original); HasMore = _listOriginalMore; EmptyText = _listOriginalEmpty; _listSearchOriginal = null; IsEmpty = Tracks.Count == 0; }
            _listSearch = null; return;
        }
        var version = _browseVersion; var folder = SelectedFolder; var user = _spaceUserId;
        try
        {
            await Task.Delay(300, load.Token);
            // Wait for the initial page rather than taking an incomplete restoration snapshot.
            while (ShowPageProgress) { await Task.Delay(30, load.Token); if (version != _browseVersion) return; }
            if (version != _browseVersion || !ReferenceEquals(_listSearch, load)) return;
            if (_listSearchOriginal is null)
            { _listSearchOriginal = Tracks.ToArray(); _listOriginalMore = HasMore; _listOriginalEmpty = EmptyText; }
            HasMore = false; IsListSearching = true; ListSearchStatus = "正在搜索全部分页内容…";
            Func<int, CancellationToken, Task<PageResult<Track>>> readPage = (page, token) => folder is not null
                ? Client.GetFolderTracksAsync(folder.Id, page, token) : Client.GetUserWorksAsync(user, page, token);
#if VISUAL_CHECKS
            readPage = ListPageReader ?? readPage;
#endif
            var catalog = _listSearchCatalog ?? (DebugSession.IsActive ? _homeCache.ToArray()
                : await Task.Run(() => TrackSearch.FindAsync(readPage, "", load.Token), load.Token));
            var result = await Task.Run(() => catalog.Where(track => TrackSearch.Matches(track, query)).ToArray(), load.Token);
            load.Token.ThrowIfCancellationRequested();
            if (version != _browseVersion || !ReferenceEquals(_listSearch, load)) return;
            _listSearchCatalog = catalog;
            Tracks.Clear();
            for (var index = 0; index < result.Length; index++)
            {
                if (version != _browseVersion || load.IsCancellationRequested) return;
                Tracks.Add(result[index]);
                if (index % 12 == 11) await Task.Yield();
            }
            IsEmpty = Tracks.Count == 0; EmptyText = "没有找到匹配的内容。";
            ListSearchStatus = $"找到 {Tracks.Count} 首音乐";
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch { if (ReferenceEquals(_listSearch, load)) ListSearchStatus = "搜索未完成，请更换关键词或清空后重试。"; throw; }
        finally { if (ReferenceEquals(_listSearch, load)) { _listSearch = null; IsListSearching = false; } }
    }
}
