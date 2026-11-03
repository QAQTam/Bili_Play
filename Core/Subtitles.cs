using System.Text.Json;

namespace BiliPlay.Core;

public static class SubtitleSelection
{
    public static bool IsTranslation(SubtitleTrack track) => System.Text.RegularExpressions.Regex.IsMatch(
        track.Label, "翻译|译文|translated|translation", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static string Language(SubtitleTrack track)
    {
        var code = track.Language.ToLowerInvariant();
        if (code.StartsWith("ai-")) code = code[3..];
        return code.Split('-', '_')[0] switch { "jp" => "ja", "kr" => "ko", var language => language };
    }
    private static bool IsOriginalLabel(SubtitleTrack track) => !IsTranslation(track) &&
        System.Text.RegularExpressions.Regex.IsMatch(track.Label, "原文|原声|original", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // The original ASR track is evidence of the source language. A translated track is not.
    // When platform evidence is absent, use the song name instead of the uploader's marketing prose.
    public static SubtitleTrack? Choose(IReadOnlyList<SubtitleTrack> tracks, string title)
    {
        if (tracks.Count == 0) return null;
        var usable = tracks.Where(track => !IsTranslation(track)).ToArray();
        if (usable.Length == 0) usable = tracks.ToArray();
        var explicitOriginal = usable.FirstOrDefault(IsOriginalLabel);
        var sourceLanguages = usable.Where(track => track.IsAuto).Select(Language).Distinct().ToArray();
        var preferred = explicitOriginal is not null ? Language(explicitOriginal)
            : sourceLanguages.Length == 1 ? sourceLanguages[0] : GuessSongLanguage(title);
        var matching = usable.Where(track => Language(track) == preferred).ToArray();
        if (matching.Length == 0 && preferred == "yue") matching = usable.Where(track => Language(track) == "zh").ToArray();
        return matching.OrderByDescending(IsOriginalLabel).ThenBy(track => track.IsAuto).FirstOrDefault()
            ?? explicitOriginal ?? usable.FirstOrDefault(track => track.IsAuto) ?? usable[0];
    }

    private static string? GuessSongLanguage(string title)
    {
        var hints = new (string Language, string Pattern)[]
        {
            ("ja", "日语|日文|Japanese"), ("ko", "韩语|韩文|Korean"), ("yue", "粤语|Cantonese"),
            ("fr", "法语|法文|French"), ("de", "德语|德文|German"), ("es", "西班牙语|Spanish"),
            ("ru", "俄语|俄文|Russian"), ("en", "英语|英文|English"), ("zh", "国语|中文歌|华语|Mandarin")
        };
        foreach (var (language, pattern) in hints)
            if (System.Text.RegularExpressions.Regex.IsMatch(title, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return language;
        var songNames = System.Text.RegularExpressions.Regex.Matches(title, "[《〈「『]([^》〉」』]+)[》〉」』]");
        var text = songNames.Count > 0 ? songNames[^1].Groups[1].Value : title;
        if (text.Any(c => c is >= '\u3040' and <= '\u30ff')) return "ja";
        if (text.Any(c => c is >= '\uac00' and <= '\ud7af')) return "ko";
        if (text.Any(c => c is >= '\u0400' and <= '\u04ff')) return "ru";
        if (text.Any(c => c is >= '\u0e00' and <= '\u0e7f')) return "th";
        if (text.Any(c => c is >= '\u0600' and <= '\u06ff')) return "ar";
        var chinese = text.Count(c => c is >= '\u4e00' and <= '\u9fff');
        var english = text.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        return english >= 2 && english > chinese * 2 ? "en" : chinese > 0 ? "zh" : null;
    }
}

public static class Subtitles
{
    public static int FollowIndex(IReadOnlyList<SubtitleLine> lines, double position)
    {
        for (var index = 0; index < lines.Count; index++)
            if (lines[index].From > position && lines[index].From <= position + .1 + 1e-9) return index;
        return ActiveIndex(lines, position);
    }

    public static IReadOnlyList<SubtitleLine> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<SubtitleLine>();
        foreach (var entry in document.RootElement.O("body").A())
        {
            if (!entry.O("from").TryGetDoubleSafe(out var start) || !entry.O("to").TryGetDoubleSafe(out var end) ||
                !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start) continue;
            var content = CleanContent(entry.S("content"));
            if (content.Length > 0) result.Add(new(start, end, content));
        }
        return result.OrderBy(l => l.From).ToArray();
    }

    public static string CleanContent(string content)
    {
        var text = Text.Clean(content).Replace("♪", "").Replace("♫", "").Replace("♬", "").Replace("♩", "")
            .Replace("🎵", "").Replace("🎶", "").Trim(' ', '\t', '\r', '\n', '\uFE0F');
        var letters = text.ToCharArray();
        var first = true;
        for (var i = 0; i < letters.Length; i++)
        {
            if (letters[i] is '\r' or '\n') { first = true; continue; }
            if (!first || !char.IsLetter(letters[i])) continue;
            if (letters[i] is >= 'a' and <= 'z') letters[i] = char.ToUpperInvariant(letters[i]);
            first = false;
        }
        return new string(letters);
    }

    // An upper-bound search selects the latest cue that has started; gaps remain unhighlighted.
    public static int ActiveIndex(IReadOnlyList<SubtitleLine> lines, double time, bool previewFirst = false)
    {
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (lines[mid].From <= time) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        if (found >= 0 && time < lines[found].To) return found;
        // Preview only: once actual playback starts, intros and gaps retain their real timing.
        return previewFirst && time == 0 && lines.Count > 0 ? 0 : -1;
    }
    private static bool TryGetDoubleSafe(this JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number);
    }
}
