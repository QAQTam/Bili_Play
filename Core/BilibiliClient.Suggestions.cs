using System.Text.Json;

namespace BiliPlay.Core;

public sealed partial class BilibiliClient
{
    public async Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string keyword,CancellationToken ct=default)
    {
        keyword=keyword.Trim();if(keyword.Length==0)return [];
        using var response=await _http.GetAsync(new Uri("https://s.search.bilibili.com/main/suggest?main_ver=v1&term="+Uri.EscapeDataString(keyword)),ct);
        response.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root=doc.RootElement;if(root.N("code")!=0)return [];
        return root.O("result").O("tag").A().Select(item=>Text.Clean(item.S("value")))
            .Where(text=>text.Length>0&&text.Length<=120).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray();
    }
}
