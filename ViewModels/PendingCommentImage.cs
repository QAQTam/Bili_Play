using BiliPlay.Core;
using Microsoft.UI.Xaml.Media;

namespace BiliPlay.ViewModels;

public sealed class PendingCommentImage(CommentImagePayload image, ImageSource preview)
{
    public CommentImagePayload Image { get; } = image;
    public ImageSource Preview { get; } = preview;
    public double PreviewWidth => ImageDisplaySize.Fit(Image.Width, Image.Height, 96, 72).Width;
    public double PreviewHeight => ImageDisplaySize.Fit(Image.Width, Image.Height, 96, 72).Height;
    public CommentPicture? Uploaded { get; set; }
    public string Hash { get; } = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(image.Bytes));
}
