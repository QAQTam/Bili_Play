using System.Text.Json.Serialization;

namespace BiliPlay.Core;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Account))]
[JsonSerializable(typeof(Track))]
[JsonSerializable(typeof(Track[]))]
[JsonSerializable(typeof(SavedCookie))]
[JsonSerializable(typeof(SavedCookie[]))]
[JsonSerializable(typeof(long[]))]
[JsonSerializable(typeof(SubtitleTrack))]
[JsonSerializable(typeof(SubtitleTrack[]))]
[JsonSerializable(typeof(SubtitleLine))]
[JsonSerializable(typeof(SubtitleLine[]))]
[JsonSerializable(typeof(PageResult<Track>))]
[JsonSerializable(typeof(ResolvedMusicTrack))]
[JsonSerializable(typeof(ResolverRequest))]
[JsonSerializable(typeof(ResolverResponse))]
public partial class BiliPlayJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(MessageContentPayload))]
[JsonSerializable(typeof(CommentPictureRequest))]
[JsonSerializable(typeof(CommentPictureRequest[]))]
internal partial class InternalCoreJsonContext : JsonSerializerContext;
