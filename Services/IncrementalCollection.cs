using BiliPlay.Core;
using Microsoft.UI.Xaml.Data;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;

namespace BiliPlay.Services;

/// <summary>WinUI ListView/GridView request the next page from this source automatically.</summary>
public sealed class IncrementalCollection<T> : PagedCollection<T>, ISupportIncrementalLoading
{
    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count) => AsyncInfo.Run(async ct =>
        new LoadMoreItemsResult { Count = await LoadPageAsync(ct) });
}
