using BiliPlay.Services;
using Microsoft.UI.Xaml;

namespace BiliPlay;
public sealed partial class MainWindow
{
    internal void EnterDebugMode()
    {
        if(ViewModel.IsLoggedIn||DebugSession.IsActive)return;
        CloseSearchHistory();_suggestionsCache.Clear();
        DebugSession.Begin(App.IsVisualCheck ? Path.Combine(App.VisualCheckDirectory!, "debug-sessions") : null);
        ViewModel.BrowseChanged-=CloseSearchHistory;ViewModel.Dispose();ArtworkCache.ResetScope();
        RootFrame.Navigate(typeof(MainPage));ViewModel=((MainPage)RootFrame.Content).ViewModel;
        ViewModel.BrowseChanged+=CloseSearchHistory;
        ViewModel.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(ViewModel.IsLoggedIn)&&!ViewModel.IsLoggedIn)CloseSearchHistory();};
        Title="Bili Play · 离线调试";AppTitleBar.Title=Title;TitleSearch.Text="";Bindings.Update();
    }
    private void ExitDebugModeClick(object sender,RoutedEventArgs args) => Close();
}
