using BiliPlay.Core;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    private Task? _homeLoadTask;
    private int _homeLoadVersion=-1;
    private Task LoadHomeAsync(bool refreshFirstPage=false)
    {
        if(_homeLoadVersion==_browseVersion&&_homeLoadTask is { IsCompleted:false }) return _homeLoadTask;
        _homeLoadVersion=_browseVersion;
        return _homeLoadTask=LoadHomePageAsync(refreshFirstPage);
    }
    private async Task LoadHomePageAsync(bool refreshFirstPage)
    {
        if(_disposed||!IsHomeView||Client.Account is not { } account) return;
        _browse??=new();var version=_browseVersion;var token=_browse.Token;
        var page=refreshFirstPage?1:_regionPage+1;
        bool Current()=>!_disposed&&IsHomeView&&version==_browseVersion&&Client.Account?.Id==account.Id;
        PageResult<Track> result;
        try
        {
            // Network continuations, HTML parsing and recommendation ranking never run on the UI thread.
#if VISUAL_CHECKS
            var read=HomeReader;
            result=await Task.Run(()=>read is null?Client.GetPersonalizedMusicAsync(page,token):read(page,token),token);
#else
            result=await Task.Run(()=>Client.GetPersonalizedMusicAsync(page,token),token);
#endif
        }
        catch
        {
            if(Current()) {IsEmpty=Tracks.Count==0;EmptyText="音乐频道暂时无法连接，请稍后刷新。";}
            throw;
        }
        if(!Current()) return;
        token.ThrowIfCancellationRequested();
        var known=refreshFirstPage?new HashSet<string>(StringComparer.Ordinal):_homeCache.Select(item=>item.Bvid).ToHashSet(StringComparer.Ordinal);
        var fresh=Store.ApplyMetadataBatch(account.Id,result.Items.Where(item=>known.Add(item.Bvid)).ToArray());
        if(refreshFirstPage) _homeCache.Clear();
        _homeCache.AddRange(fresh);_regionPage=page;_hasLoadedHome=true;_homeMore=result.HasMore;
        Store.SaveMusicCache(account.Id,_homeCache);
        if(refreshFirstPage) Tracks.Clear();
        for(var offset=0;offset<fresh.Count;offset+=12)
        {
            if(!Current()) return;
            token.ThrowIfCancellationRequested();
            foreach(var item in fresh.Skip(offset).Take(12)) Tracks.Add(item);
            IsEmpty=Tracks.Count==0;
            if(offset+12<fresh.Count) await Task.Delay(1,token);
        }
        if(!Current()) return;
        HasMore=_homeMore;IsEmpty=Tracks.Count==0;EmptyText="暂时没有音乐内容，请稍后刷新。";
    }
}
