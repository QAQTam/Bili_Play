using BiliPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace BiliPlay;
public sealed partial class MainPage
{
    private bool _debugConfirmationOpen;
    private async void LoginVersionDoubleTapped(object sender,DoubleTappedRoutedEventArgs args)
    {args.Handled=true;await ConfirmDebugModeAsync();}
    private async Task ConfirmDebugModeAsync()
    {
        if(ViewModel.IsLoggedIn||DebugSession.IsActive||_debugConfirmationOpen)return;
        _debugConfirmationOpen=true;
        try
        {
            var dialog=new ContentDialog {XamlRoot=XamlRoot,RequestedTheme=ActualTheme,Title="进入离线调试模式？",
                Content="无需登录，将使用示例歌曲和临时数据。退出调试或关闭软件后清理本次数据，下次启动恢复正常模式。",
                PrimaryButtonText="进入调试",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close};
            if(await dialog.ShowAsync()==ContentDialogResult.Primary&&!ViewModel.IsLoggedIn)
                ((MainWindow)App.Window).EnterDebugMode();
        }
        finally {_debugConfirmationOpen=false;}
    }
}
