using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using BiliPlay.Core;
using BiliPlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace BiliPlay.ViewModels;

public partial class PlayerViewModel
{
    public ObservableCollection<PendingCommentImage> CommentImages { get; } = [];
    private readonly Dictionary<string, List<PendingCommentImage>> _commentImageDrafts = [];
    private CancellationTokenSource? _commentImagePreparation;
    [ObservableProperty] public partial bool IsPreparingCommentImages { get; set; }
    [ObservableProperty] public partial string CommentSendStatus { get; set; } = "";
    public bool HasCommentImages => CommentImages.Count > 0;
    public bool CanAddCommentImages => CanEditComment && !IsPreparingCommentImages && CommentImages.Count < CommentImagePayload.MaxPictures;
    public bool CanRemoveCommentImages => CanEditComment && !IsPreparingCommentImages;
    internal string CommentImageContext => _commentsContext;

    partial void OnIsPreparingCommentImagesChanged(bool value) => UpdateCommentAvailability();
    private void InitializeCommentImages()
    {
        CommentImages.CollectionChanged += (_, _) =>
        {
            if (_commentsContext.Length > 0) _commentImageDrafts[_commentsContext] = CommentImages.ToList();
            OnPropertyChanged(nameof(HasCommentImages)); UpdateCommentAvailability();
        };
    }
    private void RestoreCommentImages(string context)
    {
        _commentImagePreparation?.Cancel();
        var images = _commentImageDrafts.GetValueOrDefault(context)?.ToArray() ?? [];
        CommentImages.Clear(); foreach (var image in images) CommentImages.Add(image);
        CommentSendStatus = "";
    }
    private void ClearCommentImageDrafts()
    {
        _commentImagePreparation?.Cancel(); CommentImages.Clear(); _commentImageDrafts.Clear(); CommentSendStatus = "";
    }
    public void RemoveCommentImage(PendingCommentImage image)
    { if (CanRemoveCommentImages) { CommentImages.Remove(image); CommentSendStatus = ""; } }

    public async Task AddCommentImagesAsync(IReadOnlyList<StorageFile> files, string expectedContext)
    {
        if (!CanAddCommentImages || expectedContext != _commentsContext || Client.Account is not { } account || files.Count == 0) return;
        if (files.Count + CommentImages.Count > CommentImagePayload.MaxPictures) { Notify("单条评论最多选择 9 张图片。", NoticeLevel.Warn); return; }
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        preparation.CancelAfter(TimeSpan.FromSeconds(90)); _commentImagePreparation = preparation; IsPreparingCommentImages = true;
        bool Current() => !_disposed && expectedContext == _commentsContext && Client.Account?.Id == account.Id && !preparation.IsCancellationRequested;
        try
        {
            foreach (var file in files)
            {
                if (!Current()) return;
                CommentSendStatus = "正在处理图片…";
                var data = await CommentImagePreparation.PrepareAsync(file, preparation.Token);
                if (!Current()) return;
                var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data.Bytes));
                if (CommentImages.Any(image => image.Hash == hash)) continue;
                if (_commentImageDrafts.Values.SelectMany(images => images).Sum(image => (long)image.Image.Bytes.Length) + data.Bytes.Length > 64L * 1024 * 1024)
                    throw new InvalidDataException("待发送图片过多，请先发送或移除部分图片。");
                using var input = new MemoryStream(data.Bytes);
                using var random = input.AsRandomAccessStream();
                var preview = new BitmapImage { DecodePixelWidth = 96, DecodePixelHeight = 96 };
                await preview.SetSourceAsync(random).AsTask(preparation.Token);
                if (Current()) CommentImages.Add(new(data, preview));
            }
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch (Exception error)
        {
            if (!_disposed && Client.Account?.Id == account.Id && expectedContext == _commentsContext)
                Notify(error is InvalidDataException ? error.Message : "无法读取这张图片，请选择有效的 JPG、PNG、BMP 或 WebP 图片。", NoticeLevel.Warn);
        }
        finally
        {
            if (ReferenceEquals(_commentImagePreparation, preparation)) { _commentImagePreparation = null; IsPreparingCommentImages = false; }
            if (!_disposed && expectedContext == _commentsContext) CommentSendStatus = "";
        }
    }
}
