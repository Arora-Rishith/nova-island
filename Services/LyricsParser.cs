using System.Globalization;
using System.Text.RegularExpressions;

namespace NovaIsland.Services;

internal sealed record TimedLyric(double Seconds, string Text);
internal static class LyricsParser
{
    internal static string LookupUri(string artist, string title) =>
        "https://lrclib.net/api/get?artist_name=" + Uri.EscapeDataString(artist) + "&track_name=" + Uri.EscapeDataString(title);
    internal static string NormalizeTitle(string title) => Regex.Replace(title,
        @"\s*[\(\[][^\)\]]*(?:official\s+(?:music\s+)?video|official\s+audio|lyrics?\s+video|visuali[sz]er)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase).Trim();
    internal static IReadOnlyList<TimedLyric> Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return [];
        var lines = new List<TimedLyric>();
        var offsetMatch = Regex.Match(source, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        double offset = offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var ms) ? ms / 1000 : 0;
        foreach (string raw in source.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var stamps = Regex.Matches(raw, @"\[(\d+):(\d{2})(?:[.:](\d{1,3}))?\]");
            if (stamps.Count == 0) continue;
            var last = stamps[^1];
            string text = raw[(last.Index + last.Length)..].Trim();
            foreach (Match stamp in stamps)
            {
                if (!int.TryParse(stamp.Groups[1].Value, out int minutes) || !int.TryParse(stamp.Groups[2].Value, out int seconds) || seconds >= 60) continue;
                double fraction = stamp.Groups[3].Success ? double.Parse("0." + stamp.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                lines.Add(new(Math.Max(0, minutes * 60.0 + seconds + fraction + offset), text));
            }
        }
        return lines.OrderBy(line => line.Seconds).ToArray();
    }
}
