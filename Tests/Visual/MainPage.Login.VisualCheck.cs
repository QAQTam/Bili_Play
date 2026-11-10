using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyLoginLayoutAsync(string directory)
    {
        Check(App.Window.Title == "Bili Play", "开发版窗口不应显示 Dev 后缀");
        ViewModel.IsLoggedIn = false;
        ViewModel.IsInitializing = false;
        ViewModel.LoginLoading = false;
        ViewModel.LoginStatus = "使用哔哩哔哩手机客户端扫码登录";
        using var code = QRCodeGenerator.GenerateQrCode("https://www.bilibili.com/", QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(code);
        using var stream = new MemoryStream(qr.GetGraphic(8));
        var image = new BitmapImage();
        await image.SetSourceAsync(stream.AsRandomAccessStream());
        ViewModel.QrImage = image;
        var size = App.Window.AppWindow.Size;
        try
        {
            foreach (var width in new[] { 840, 1240 })
            {
                var dpi = XamlRoot.RasterizationScale;
                App.Window.AppWindow.Resize(new((int)(width * dpi), (int)(820 * dpi)));
                await Task.Delay(120);
                UpdateLayout();
                Check(LoginSurface.Visibility == Visibility.Visible && LoginLogo.Source is not null, "登录页未显示应用 Logo");
                Check(LoginContent.Children.Count == 5 && LoginHeading.Text == "登录 Bili Play", "登录页存在多余介绍或标题错误");
                var elements = new FrameworkElement[] { LoginLogo, LoginHeading, LoginQrImage, LoginPrompt, LoginRefreshButton };
                for (var i = 1; i < elements.Length; i++)
                    Check(elements[i].TransformToVisual(LoginSurface).TransformPoint(new(0, 0)).Y
                        > elements[i-1].TransformToVisual(LoginSurface).TransformPoint(new(0, 0)).Y, "登录页内容顺序不正确");
                Check(LoginPrompt.Text == "使用哔哩哔哩手机客户端扫码登录", "登录提示不正确");
                Check(string.Concat(LoginVersion.Inlines.OfType<Run>().Select(run => run.Text)) == "版本：" + ViewModel.AppVersion, "首次登录版本不正确");
                var version = LoginVersion.TransformToVisual(LoginSurface).TransformPoint(new(0, 0));
                Check(Math.Abs(version.X + LoginVersion.ActualWidth - LoginSurface.ActualWidth + 24) < 2
                    && Math.Abs(version.Y + LoginVersion.ActualHeight - LoginSurface.ActualHeight + 20) < 2, "版本号没有固定在右下角");
                await SaveVisualAsync(Path.Combine(directory, $"login-{width}.png"));
            }
        }
        finally
        {
            ViewModel.QrImage = null;
            App.Window.AppWindow.Resize(size);
        }
    }
}
