using System.Runtime.InteropServices.WindowsRuntime;
using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BiliPlay;

public sealed partial class MainPage
{
    private ArtworkPalette _ambientPalette = ArtworkAtmosphere.Neutral;
    private string? _ambientSource;
    private int _ambientVersion;
    private CancellationTokenSource? _ambientLoad;
    private bool _presentationActive = true;
    private bool _ambientReady;
    private bool? _ambientDark;
    public void SetPresentationActive(bool active)
    {
        _presentationActive=active; ViewModel.SetCommentsPresentationActive(active); UpdateAmbientMotion();
        if(active && ViewModel.IsLyricsOpen) { StyleVisibleLyrics(false); _ = RefreshAtmosphereAsync(); }
    }
    private async Task RefreshAtmosphereAsync()
    {
        if(!ViewModel.IsLyricsOpen || XamlRoot is null) return;
        var version=++_ambientVersion;
        _ambientLoad?.Cancel(); _ambientLoad?.Dispose();
        var load=_ambientLoad=new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var window = (MainWindow)App.Window;
        try
        {
            if(_accessibility.HighContrast)
            { window.ResetArtworkBackdrop(); window.SetArtworkBackdropVisible(false, false); _ambientReady=false; UpdateAmbientMotion(); return; }
            window.SetArtworkBackdropVisible(true, MotionEnabled);
            var dark=ActualTheme==ElementTheme.Dark;
            if(_ambientDark!=dark) { window.ResetArtworkBackdrop(); _ambientReady=false; _ambientDark=dark; }
            var uri=(ViewModel.LyricsCover as BitmapImage)?.UriSource;
            if(uri is not null && _ambientSource!=uri.AbsoluteUri)
            {
                try
                {
                    if(!uri.IsFile && !Text.IsMediaHost(uri)) throw new InvalidDataException("Unexpected artwork source");
                    using IRandomAccessStream stream=uri.IsFile
                        ? await (await StorageFile.GetFileFromPathAsync(uri.LocalPath)).OpenReadAsync().AsTask(load.Token)
                        : await RandomAccessStreamReference.CreateFromUri(new Uri(Text.CoverImage(uri.AbsoluteUri,96))).OpenReadAsync().AsTask(load.Token);
                    var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(load.Token);
                    var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,
                        new BitmapTransform { ScaledWidth=48,ScaledHeight=48 },ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage).AsTask(load.Token);
                    _ambientPalette=ArtworkAtmosphere.Sample(data.DetachPixelData());
                    _ambientSource=uri.AbsoluteUri;
                }
                catch(Exception error) when(error is not OperationCanceledException)
                { _ambientPalette=ArtworkAtmosphere.Neutral; _ambientSource=uri.AbsoluteUri; }
            }
            if(uri is null) { _ambientPalette=ArtworkAtmosphere.Neutral; _ambientSource=null; }
            var pixels=await Task.Run(()=>ArtworkAtmosphere.Render(_ambientPalette,dark),load.Token);
            if(version!=_ambientVersion || !ViewModel.IsLyricsOpen) return;
            var bitmap=new WriteableBitmap(192,120);
            using(var output=bitmap.PixelBuffer.AsStream()) await output.WriteAsync(pixels,load.Token);
            bitmap.Invalidate(); _ambientReady=true;
            await window.PresentArtworkBackdropAsync(bitmap, MotionEnabled, load.Token);
            if(version!=_ambientVersion) return;
            UpdateAmbientMotion();
        }
        catch(OperationCanceledException) { }
        catch(Exception) { /* Decorative effects never prevent playback or opening details. */ }
    }
    private void UpdateAmbientMotion() => ((MainWindow)App.Window).SetArtworkBackdropMotion(
        _presentationActive && _ambientReady && ViewModel.IsLyricsOpen && ViewModel.Player.Playing && MotionEnabled && !_accessibility.HighContrast);
}
