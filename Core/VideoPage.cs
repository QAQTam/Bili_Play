using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BiliPlay.Core;

/// <summary>Reads embedded JSON only; never executes JavaScript from a remote page.</summary>
public static class VideoPage
{
    public static JsonElement ReadJson(string html, string variable)
    {
        var match = Regex.Match(html, @"(?:window\s*\.\s*)?" + Regex.Escape(variable) + @"\s*=\s*(?=\{)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException("视频网页没有提供可用的播放数据。");
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(html[(match.Index + match.Length)..]));
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }
}
