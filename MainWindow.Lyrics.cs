using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LyricsLine = NovaIsland.Services.TimedLyric;

namespace NovaIsland;

public partial class MainWindow
{
    private sealed class LyricsResponse
    {
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; init; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; init; }
    }

    private static readonly HttpClient LyricsClient = new()
    {
        Timeout = TimeSpan.FromSeconds(7)
    };
    private readonly object lyricsLock = new();
    private CancellationTokenSource? lyricsRequestCancellation;
    private IReadOnlyList<LyricsLine> lyricsLines = Array.Empty<LyricsLine>();
    private string lyricsKey = string.Empty;
    private bool lyricsSynced;
    private int currentLyricIndex = -1;
    private string currentLyric = "Lyrics will appear here when available";
    private string lyricsStatus = "Lyrics are looked up when a track changes.";

    private async Task LoadLyricsAsync(string artist, string title, string mediaKey)
    {
        CancellationTokenSource request;
        lock (lyricsLock)
        {
            if (lyricsKey == mediaKey) return;
            lyricsRequestCancellation?.Cancel();
            request = new CancellationTokenSource();
            lyricsRequestCancellation = request;
            lyricsKey = mediaKey;
            lyricsLines = Array.Empty<LyricsLine>();
            lyricsSynced = false;
            currentLyricIndex = -1;
            currentLyric = "Looking for lyrics…";
            lyricsStatus = "Searching LRCLIB";
        }
        PublishWebState();
        CancellationToken cancellationToken = request.Token;

        try
        {
            string url = Services.LyricsParser.LookupUri(artist, title);
            using var firstResponse = await LyricsClient.GetAsync(url, cancellationToken);
            string normalizedTitle = Services.LyricsParser.NormalizeTitle(title);
            using var retryResponse = firstResponse.StatusCode == HttpStatusCode.NotFound && normalizedTitle != title
                ? await LyricsClient.GetAsync(Services.LyricsParser.LookupUri(artist, normalizedTitle), cancellationToken) : null;
            var response = retryResponse ?? firstResponse;
            cancellationToken.ThrowIfCancellationRequested();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                SetLyrics(request, mediaKey, Array.Empty<LyricsLine>(), "No lyrics found for this track.");
                return;
            }
            response.EnsureSuccessStatusCode();
            LyricsResponse? result = await response.Content.ReadFromJsonAsync<LyricsResponse>(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result == null)
            {
                SetLyrics(request, mediaKey, Array.Empty<LyricsLine>(), "Lyrics service returned no result.");
                return;
            }

            var synced = ParseSyncedLyrics(result.SyncedLyrics);
            if (synced.Count > 0)
            {
                SetLyrics(request, mediaKey, synced, "Synced lyrics · LRCLIB");
                return;
            }

            var plain = (result.PlainLyrics ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => new LyricsLine(-1, line))
                .ToArray();
            SetLyrics(request, mediaKey, plain, plain.Length == 0 ? "No lyrics found for this track." : "Lyrics · timing unavailable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Lyrics lookup: " + ex.Message);
            SetLyrics(request, mediaKey, Array.Empty<LyricsLine>(), "Lyrics unavailable right now.");
        }
        finally
        {
            bool publish = false;
            lock (lyricsLock)
            {
                if (ReferenceEquals(lyricsRequestCancellation, request))
                {
                    lyricsRequestCancellation = null;
                    publish = true;
                }
            }
            request.Dispose();
            if (publish) PublishWebState();
        }
    }

    private void SetLyrics(CancellationTokenSource request, string mediaKey, IReadOnlyList<LyricsLine> lines, string status)
    {
        bool changed;
        lock (lyricsLock)
        {
            if (!ReferenceEquals(lyricsRequestCancellation, request) || lyricsKey != mediaKey || request.IsCancellationRequested)
                return;
            lyricsLines = lines;
            lyricsSynced = lines.Count > 0 && lines.All(line => line.Seconds >= 0);
            currentLyricIndex = -1;
            lyricsStatus = status;
            currentLyric = lines.FirstOrDefault()?.Text ?? status;
            changed = true;
        }
        if (changed)
        {
            CurrentLyricText.Text = currentLyric;
            PublishWebState();
        }
    }

    private static IReadOnlyList<LyricsLine> ParseSyncedLyrics(string? source)
    {
        return Services.LyricsParser.Parse(source);
    }

    private void UpdateCurrentLyric(TimeSpan position)
    {
        lock (lyricsLock)
        {
            if (lyricsLines.Count == 0 || !lyricsSynced) return;
            currentLyricIndex = -1;
            for (int i = 0; i < lyricsLines.Count && lyricsLines[i].Seconds <= position.TotalSeconds; i++) currentLyricIndex = i;
            LyricsLine? line = lyricsLines.LastOrDefault(item => item.Seconds <= position.TotalSeconds);
            if (line != null) currentLyric = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;
        }
        CurrentLyricText.Text = currentLyric;
    }

    private void ClearLyrics()
    {
        lock (lyricsLock)
        {
            lyricsRequestCancellation?.Cancel();
            lyricsRequestCancellation = null;
            lyricsKey = string.Empty;
            lyricsLines = Array.Empty<LyricsLine>();
            lyricsSynced = false;
            currentLyricIndex = -1;
            currentLyric = "Lyrics will appear here when available";
            lyricsStatus = "Play a track to look up lyrics.";
        }
        CurrentLyricText.Text = currentLyric;
        PublishWebState();
    }
}
