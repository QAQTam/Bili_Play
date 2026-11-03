using System.Security.Cryptography;
using System.Text;

namespace BiliPlay.Core;

public static class WbiSigner
{
    private static readonly int[] Order = [46,47,18,2,53,8,23,32,15,50,10,31,58,3,45,35,27,43,5,49,33,9,42,19,29,28,14,39,12,38,41,13,37,48,7,16,24,55,40,61,26,17,0,1,60,51,30,4,22,25,54,21,56,59,6,63,57,62,11,36,20,34,44,52];

    public static string Sign(IReadOnlyDictionary<string, string> parameters, string imageKey, string subKey, long timestamp)
    {
        var raw = imageKey + subKey;
        if (raw.Length != 64) throw new InvalidDataException("B 站签名密钥无效，请稍后重试。");
        var key = new string(Order.Take(32).Select(i => raw[i]).ToArray());
        var values = new SortedDictionary<string, string>(parameters.ToDictionary(k => k.Key, v => v.Value), StringComparer.Ordinal)
        { ["wts"] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        values.Remove("w_rid");
        var query = string.Join("&", values.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(new string(p.Value.Where(c => !"!'()*".Contains(c)).ToArray()))}"));
        var digest = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(query + key)));
        return query + "&w_rid=" + digest;
    }
}
