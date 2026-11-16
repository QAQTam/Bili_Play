using BiliPlay.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using BiliPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace BiliPlay;

public sealed partial class MainPage
{
    private async Task VerifyCommentImagesAsync(string directory)
    {
        var song = new Track(71, "BV0000000071", "合成图片评论验证", "离线 UP", "", 60, 71);
        var other = song with { Aid = 72, Bvid = "BV0000000072", Title = "另一首合成曲目" };
        var picture = new CommentPicture("https://i0.hdslb.com/bfs/new_dyn/synthetic.png", 96, 64, 1);
        var rows = new List<BiliComment> { new(42, 123, "合成用户", "", "包含图片的合成评论", 0, 1700000000, ReplyCount: 1, Pictures: [picture]) };
        var file = await StorageFile.GetFileFromPathAsync(Path.Combine(directory, "cover.png"));
        var uploads = 0; var posts = 0;
        try
        {
            ViewModel.CommentsReader = (_, _, _, _) => Task.FromResult(new CommentPage(rows.ToArray(), rows.Count, false));
            ViewModel.CurrentTrack = song; ViewModel.ShowComments = true;
            await Task.Delay(150); UpdateLayout();
            CommentList.ScrollIntoView(ViewModel.Comments[0]); await Task.Delay(100); UpdateLayout();
            var gallery = FindVisual<CommentPictures>((DependencyObject)CommentList.ContainerFromIndex(0));
            Check(gallery is not null && gallery.Visibility == Visibility.Visible && FindVisual<Image>(gallery)?.Source is BitmapImage, "评论原图未显示");
            await Task.Delay(100); UpdateLayout();
            var inline = FindVisual<Image>(gallery!)!;
            Check(inline.Stretch == Stretch.Uniform && inline.Source is BitmapImage { DecodePixelWidth: 0, DecodePixelHeight: 0 }, "评论原图被裁剪或限制了解码尺寸");
            Check(inline.ActualWidth <= 240 && inline.ActualHeight <= 180, "评论内联图片没有缩为紧凑预览");
            var previewPictures = new[] { picture with { Width = 2400, Height = 600 }, picture with { Width = 600, Height = 2400 }, picture with { Width = 40, Height = 20 } };
            foreach (var item in previewPictures)
            {
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                var pixels = new byte[item.Width * item.Height * 4];
                for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 180; pixels[i + 1] = 100; pixels[i + 2] = 40; pixels[i + 3] = 255; }
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
                    (uint)item.Width, (uint)item.Height, 96, 96, pixels);
                await encoder.FlushAsync(); stream.Seek(0);
                using var output = File.Create(Path.Combine(directory, $"image-{item.Width}x{item.Height}.png"));
                await stream.AsStreamForRead().CopyToAsync(output);
            }
            var showing = ShowCommentPictureAsync(previewPictures[0], previewPictures); await Task.Delay(150); UpdateLayout();
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Single(value => value.Child is Grid { Name: "PicturePreviewOverlay" });
            var overlay = (Grid)popup.Child;
            for (var index = 0; index < previewPictures.Length; index++)
            {
                await Task.Delay(80); UpdateLayout();
                var displayed = FindVisual<Image>(overlay)!;
                var original = previewPictures[index];
                var expected = ImageDisplaySize.Fit(original.Width, original.Height, XamlRoot.Size.Width - 116, XamlRoot.Size.Height - 64);
                Check(displayed.Source is BitmapImage { DecodePixelWidth: 0, DecodePixelHeight: 0 }
                    && Math.Abs(displayed.Width - expected.Width) < 1 && Math.Abs(displayed.Height - expected.Height) < 1, "横图、竖图或小图没有按原尺寸适配预览");
                var corner = displayed.TransformToVisual(overlay).TransformPoint(new(0, 0));
                Check(corner.X >= 0 && corner.Y >= 0 && corner.X + displayed.ActualWidth <= overlay.ActualWidth
                    && corner.Y + displayed.ActualHeight <= overlay.ActualHeight, "图片预览超出窗口边界");
                foreach (var name in new[] { "ClosePicturePreview", "DownloadPicturePreview", "NextPicturePreview" })
                {
                    var tool = FindNamedButton(overlay, name)!;
                    var location = tool.TransformToVisual(overlay).TransformPoint(new(0, 0));
                    Check(location.X >= corner.X + displayed.ActualWidth + 10 && location.X + tool.ActualWidth <= overlay.ActualWidth, "预览工具栏遮挡原图或超出窗口");
                }
                if (index == 0) await SaveVisualAsync(Path.Combine(directory, "picture-preview-wide.png"), overlay);
                if (index == 1) await SaveVisualAsync(Path.Combine(directory, "picture-preview-tall.png"), overlay);
                if (index < previewPictures.Length - 1)
                    ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(FindNamedButton(overlay, "NextPicturePreview")!).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            }
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(FindNamedButton(overlay, "ClosePicturePreview")!).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await showing;

            ViewModel.CommentImageUploader = (_, _) => { uploads++; throw new HttpRequestException("synthetic upload failure"); };
            ViewModel.CommentPictureSender = (_, _, _, _, _, _) => { posts++; throw new HttpRequestException("synthetic post failure"); };
            ViewModel.CommentDraft = "图片草稿";
            var preparing = ViewModel.AddCommentImagesAsync([file, file], ViewModel.CommentImageContext);
            Check(ViewModel.IsPreparingCommentImages && !ViewModel.CanSendComment, "图片尚未处理完就允许发送"); await preparing; UpdateLayout();
            Check(ViewModel.CommentImages.Count == 1 && uploads == 0 && CommentImagePreviews.Items.Count == 1, "选图触发了上传、重复添加或没有预览");
            var draft = ViewModel.CommentImages[0];
            Check(draft.Preview is BitmapImage image && image.PixelWidth > 0, "待发送图片没有实际解码");
            ViewModel.CurrentTrack = other;
            Check(ViewModel.CommentImages.Count == 0, "图片串到了另一首歌曲");
            ViewModel.CurrentTrack = song;
            Check(ViewModel.CommentImages.Count == 1 && ViewModel.CommentDraft == "图片草稿", "切歌丢失图片草稿");
            await ViewModel.SendCommentCommand.ExecuteAsync(null);
            Check(uploads == 1 && posts == 0 && ViewModel.CommentImages.Count == 1 && ViewModel.CommentDraft == "图片草稿", "上传失败后仍发布评论或丢失草稿");

            ViewModel.CommentImageUploader = (data, _) => { uploads++; return Task.FromResult(picture with { Width = data.Width, Height = data.Height }); };
            await ViewModel.SendCommentCommand.ExecuteAsync(null);
            Check(uploads == 2 && posts == 1 && ViewModel.CommentImages[0].Uploaded is not null, "不确定发布没有保留已上传图片");
            ViewModel.CommentPictureSender = (target, text, root, parent, pictures, _) =>
            {
                posts++; Check(target.Bvid == song.Bvid && root == 0 && parent == 0 && pictures.Count == 1, "图文评论目标或图片不正确");
                var result = new BiliComment(7001, 123, "合成用户", "", text, 0, 1700000000, Pictures: pictures.ToArray()); rows.Insert(0, result); return Task.FromResult<BiliComment?>(result);
            };
            await ViewModel.SendCommentCommand.ExecuteAsync(null);
            Check(uploads == 2 && posts == 2 && ViewModel.CommentImages.Count == 0 && ViewModel.CommentDraft.Length == 0, "手动重试重复上传或发送成功未清空草稿");

            var rootEntry = ViewModel.Comments.Single(entry => entry.Id == 42);
            ViewModel.BeginCommentReply(rootEntry); ViewModel.CommentDraft = "合成图文回复";
            await ViewModel.AddCommentImagesAsync([file], ViewModel.CommentImageContext);
            ViewModel.CommentPictureSender = (_, text, root, parent, pictures, _) =>
            {
                Check(root == 42 && parent == 42, "图片回复丢失根评论或目标评论");
                return Task.FromResult<BiliComment?>(new(7002, 123, "合成回复用户", "", text, 0, 1700000000, Pictures: pictures.ToArray()));
            };
            await ViewModel.SendCommentCommand.ExecuteAsync(null); await Task.Delay(100); UpdateLayout();
            Check(rootEntry.Replies.Single(entry => entry.Id == 7002).Data.Pictures?.Count == 1 && !ViewModel.Comments.Any(entry => entry.Id == 7002), "图片回复没有留在楼中楼");
            await ViewModel.AddCommentImagesAsync([file], ViewModel.CommentImageContext);
            ViewModel.RemoveCommentImage(ViewModel.CommentImages[0]); Check(!ViewModel.HasCommentImages, "待发送图片无法移除");
            ViewModel.Notifications.Clear(); await SaveVisualAsync(Path.Combine(directory, "comment-images.png"));
        }
        finally
        {
            ViewModel.CommentImageUploader = null; ViewModel.CommentPictureSender = null; ViewModel.CommentsReader = null;
            foreach (var image in ViewModel.CommentImages.ToArray()) ViewModel.RemoveCommentImage(image);
            ViewModel.ShowComments = false; ViewModel.CommentDraft = ""; ViewModel.ClearQueueCommand.Execute(null); ViewModel.Notifications.Clear();
        }
        using var isolated = new PlayerViewModel(new BilibiliClient(new PagedTrackFixtureHandler()));
        isolated.Client.RestoreCachedAccount(new(123, "合成隔离账号", "")); isolated.IsLoggedIn = true;
        isolated.CommentsReader = (_, _, _, _) => Task.FromResult(new CommentPage([], 0, false));
        isolated.CurrentTrack = song; isolated.ShowComments = true;
        await isolated.AddCommentImagesAsync([file], isolated.CommentImageContext);
        Check(isolated.HasCommentImages, "隔离图片草稿未创建");
        isolated.IsLoggedIn = false;
        Check(!isolated.HasCommentImages, "退出账号未清除图片草稿");
    }
}
