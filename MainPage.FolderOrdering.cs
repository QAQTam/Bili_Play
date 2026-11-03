using BiliPlay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace BiliPlay;

public sealed partial class MainPage
{
    private long[]? _folderOrderBefore;
    private long _folderOrderAccount;
    private void FolderDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (args.Items.Count == 0 || args.Items.Any(item => item is not Folder)) { args.Cancel = true; return; }
        ViewModel.PauseFolderPreviews();
        _folderOrderBefore = ViewModel.Folders.Select(folder => folder.Id).ToArray();
        _folderOrderAccount = ViewModel.Client.Account?.Id ?? 0;
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }
    private void FolderDragOver(object sender, DragEventArgs args)
    {
        if (_folderOrderBefore is null || ViewModel.Client.Account?.Id != _folderOrderAccount)
        { args.AcceptedOperation = DataPackageOperation.None; args.Handled = true; }
        // Internal moves are handled by the native control's insertion/reorder logic.
    }
    private void FolderDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var before = _folderOrderBefore; _folderOrderBefore = null;
        if (before is null || ViewModel.Client.Account?.Id != _folderOrderAccount) return;
        var ids = args.DropResult == DataPackageOperation.Move ? ViewModel.Folders.Select(folder => folder.Id).ToArray() : before;
        var ordered = FolderOrder.Merge(ViewModel.Folders, ids);
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ViewModel.Folders.ToList().FindIndex(folder => folder.Id == ordered[i].Id);
            if (current != i && current >= 0) ViewModel.Folders.Move(current, i);
        }
        ViewModel.SaveFolderOrder();
    }
}
