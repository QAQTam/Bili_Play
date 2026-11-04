using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BiliPlay;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _suggestionsLoad;
    private int _suggestionsVersion;
    private IReadOnlyList<string> _keywordSuggestions=[];
    private IReadOnlyList<Track> _songSuggestions=[];
    private readonly Dictionary<string,(DateTimeOffset At,IReadOnlyList<string> Keywords,IReadOnlyList<Track> Songs)> _suggestionsCache=[];
    private void CancelSearchRecommendations()
    {++_suggestionsVersion;_suggestionsLoad?.Cancel();_suggestionsLoad=null;}
    private void SetSearchRecommendationItems()
    {
        SearchKeywordItems.ItemsSource=_keywordSuggestions;SearchSongItems.ItemsSource=_songSuggestions;
        SearchKeywordHeading.Visibility=_keywordSuggestions.Count>0?Visibility.Visible:Visibility.Collapsed;
        SearchSongHeading.Visibility=_songSuggestions.Count>0?Visibility.Visible:Visibility.Collapsed;
        SearchHistoryPopup.IsOpen=!_historySuppressed && (SearchHistoryItems.Items.Count>0||_keywordSuggestions.Count>0||_songSuggestions.Count>0);
    }
    private void RequestSearchRecommendations(string query)
    {
        CancelSearchRecommendations();_keywordSuggestions=[];_songSuggestions=[];SetSearchRecommendationItems();
        if(query.Length==0||App.IsVisualCheck)return;
        var key=$"{ViewModel.Client.Account?.Id}:{query}";
        if(_suggestionsCache.TryGetValue(key,out var cached)&&DateTimeOffset.UtcNow-cached.At<TimeSpan.FromMinutes(1))
        {_keywordSuggestions=cached.Keywords;_songSuggestions=cached.Songs;SetSearchRecommendationItems();return;}
        _=LoadSearchRecommendationsAsync(query,key,_suggestionsVersion);
    }
    private async Task LoadSearchRecommendationsAsync(string query,string key,int version)
    {
        using var load=new CancellationTokenSource(TimeSpan.FromSeconds(9));_suggestionsLoad=load;
        bool Current()=>version==_suggestionsVersion&&!load.IsCancellationRequested&&!_historySuppressed&&TitleSearch.Text.Trim()==query&&$"{ViewModel.Client.Account?.Id}:{query}"==key&&ViewModel.IsLoggedIn;
        async Task KeywordsAsync()
        {
            try {var items=await ViewModel.Client.GetSearchSuggestionsAsync(query,load.Token);if(Current()){_keywordSuggestions=items;SetSearchRecommendationItems();}}
            catch(Exception error) when(error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or System.IO.InvalidDataException) { }
        }
        async Task SongsAsync()
        {
            try {var items=await ViewModel.Client.SearchAsync(query,1,load.Token);if(Current()){_songSuggestions=items.Items.Take(5).ToArray();SetSearchRecommendationItems();}}
            catch(Exception error) when(error is HttpRequestException or OperationCanceledException or BilibiliException or System.Text.Json.JsonException or System.IO.InvalidDataException) { }
        }
        try
        {
            await Task.Delay(500,load.Token);
            if(!Current())return;
            await Task.WhenAll(KeywordsAsync(),SongsAsync());
            if(Current()&&(_keywordSuggestions.Count>0||_songSuggestions.Count>0))
            {if(_suggestionsCache.Count>=32)_suggestionsCache.Clear();_suggestionsCache[key]=(DateTimeOffset.UtcNow,_keywordSuggestions,_songSuggestions);}
        }
        catch(OperationCanceledException) { }
        finally {if(ReferenceEquals(_suggestionsLoad,load))_suggestionsLoad=null;}
    }
    private async void SearchSuggestedSongClick(object sender,RoutedEventArgs args)
    {
        if(sender is not FrameworkElement {Tag:Track track})return;
        CloseSearchHistory();await ViewModel.PlayQueueAsync(track);
    }
}
