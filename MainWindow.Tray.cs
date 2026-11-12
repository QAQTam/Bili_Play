using BiliPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BiliPlay;

public sealed partial class MainWindow
{
    private int _trayTrimVersion;

    // Closing to the tray keeps playback and the ViewModel alive but releases the
    // interface: the realized XAML tree, its decoded bitmaps and the artwork caches
    // are dropped so idle memory settles near the media pipeline instead of the UI.
    // The placeholder page is intentionally anonymous and never stored; the tray
    // state is derived from what the root frame currently hosts.
    private void EnterTrayUiState()
    {
        if (RootFrame.Content is not MainPage page) return;
        page.PrepareForTrayDetach();
        RootFrame.Content = new Page();
        ArtworkCache.ReleaseMemory();
        ScheduleTrayGcTrim();
    }

    private void ExitTrayUiState()
    {
        if (RootFrame.Content is MainPage) return;
        RootFrame.Content = new MainPage(ViewModel);
    }

    private void ScheduleTrayGcTrim()
    {
        var version = ++_trayTrimVersion;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);
                if (version != _trayTrimVersion || RootFrame.Content is MainPage) return;
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
            catch (ObjectDisposedException) { }
        });
    }
}
