using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace TuneVault.Services;

/// <summary>Last.fm-backed metadata lookup implementing the same contract as the
/// previous Audiometa service. Every method degrades to an empty/null result on
/// failure so offline editing keeps working.</summary>
public sealed class LastFmService : IAudiometaService
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private static readonly Regex YearRegex = new(@"\b(\d{4})\b", RegexOptions.Compiled);

    public string? LastFailureReason { get; private set; }

    public LastFmService(string? apiKey = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("LASTFM_API_KEY");
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://ws.audioscrobbler.com"),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    private string ApiKeyOrEmpty() => _apiKey ?? string.Empty;

    private static string MapError(int code) => code switch
    {
        10 => "invalid_key",
        29 => "rate_limited",
        11 => "service_offline",
        6 or 7 => "no_matches",
        _ => "http_error",
    };

    private async Task<JsonDocument?> GetJsonAsync(string pathAndQuery, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(pathAndQuery, ct);
            var stream = await response.Content.ReadAsStreamAsync(ct);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Number)
            {
                LastFailureReason = MapError(err.GetInt32());
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                LastFailureReason = "http_error";
                return null;
            }
            return doc;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LastFmService request failed ({pathAndQuery}): {ex.GetType().Name}: {ex.Message}");
            LastFailureReason = "network";
            return null;
        }
    }

    public async Task<IReadOnlyList<AudiometaTrack>> SearchTracksAsync(string title, CancellationToken cancellationToken = default)
    {
        var result = new List<AudiometaTrack>();
        LastFailureReason = null;
        if (string.IsNullOrWhiteSpace(title)) { LastFailureReason = "no_matches"; return result; }

        var url = $"/2.0/?method=track.search&track={Uri.EscapeDataString(title.Trim())}&limit=20&api_key={ApiKeyOrEmpty()}&format=json";
        using var doc = await GetJsonAsync(url, cancellationToken);
        if (doc is null) return result;

        if (!doc.RootElement.TryGetProperty("results", out var resultsEl) ||
            !resultsEl.TryGetProperty("trackmatches", out var tm) ||
            !tm.TryGetProperty("track", out var tracksEl))
        {
            LastFailureReason = "no_matches";
            return result;
        }

        var searchItems = new List<JsonElement>();
        if (tracksEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tracksEl.EnumerateArray()) searchItems.Add(t);
        }
        else if (tracksEl.ValueKind == JsonValueKind.Object)
        {
            searchItems.Add(tracksEl);
        }

        if (searchItems.Count == 0) { LastFailureReason = "no_matches"; return result; }

        var sem = new SemaphoreSlim(5);
        var yearCache = new ConcurrentDictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var details = new ConcurrentDictionary<int, (string? Album, int? Duration, string? Genre, string? ImageUrl, int? Year, Bitmap? CoverArt)>();

        var tasks = searchItems.Select(async (item, idx) =>
        {
            await sem.WaitAsync(cancellationToken);
            try
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) return;

                var artist = item.TryGetProperty("artist", out var a) ? a.GetString() ?? "" : "";

                string? imageUrl = null;
                if (item.TryGetProperty("image", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var im in imgs.EnumerateArray())
                    {
                        if (im.TryGetProperty("size", out var sz) &&
                            (sz.GetString() == "extralarge" || sz.GetString() == "large") &&
                            im.TryGetProperty("#text", out var u) && !string.IsNullOrWhiteSpace(u.GetString()))
                        {
                            imageUrl = u.GetString();
                            break;
                        }
                    }
                    if (imageUrl is null && imgs.GetArrayLength() > 0)
                    {
                        var last = imgs[imgs.GetArrayLength() - 1];
                        if (last.TryGetProperty("#text", out var lu) && !string.IsNullOrWhiteSpace(lu.GetString()))
                            imageUrl = lu.GetString();
                    }
                }

                string? album = null;
                int? duration = null;
                string? genre = null;
                string? infoImage = null;

                if (!string.IsNullOrWhiteSpace(artist))
                {
                    var infoUrl = $"/2.0/?method=track.getInfo&artist={Uri.EscapeDataString(artist)}&track={Uri.EscapeDataString(name!)}&api_key={ApiKeyOrEmpty()}&format=json";
                    using var infoDoc = await GetJsonAsync(infoUrl, cancellationToken);
                    if (infoDoc is not null && infoDoc.RootElement.TryGetProperty("track", out var tr))
                    {
                        if (tr.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.String &&
                            int.TryParse(dur.GetString(), out var d) && d > 0)
                        {
                            duration = d;
                        }
                        if (tr.TryGetProperty("album", out var alb) && alb.ValueKind == JsonValueKind.Object)
                        {
                            if (alb.TryGetProperty("title", out var at)) album = at.GetString();
                            if (alb.TryGetProperty("image", out var aimgs) && aimgs.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var im in aimgs.EnumerateArray())
                                {
                                    if (im.TryGetProperty("size", out var sz) &&
                                        (sz.GetString() == "extralarge" || sz.GetString() == "large") &&
                                        im.TryGetProperty("#text", out var u) && !string.IsNullOrWhiteSpace(u.GetString()))
                                    {
                                        infoImage = u.GetString();
                                        break;
                                    }
                                }
                                if (infoImage is null && aimgs.GetArrayLength() > 0)
                                {
                                    var last = aimgs[aimgs.GetArrayLength() - 1];
                                    if (last.TryGetProperty("#text", out var lu) && !string.IsNullOrWhiteSpace(lu.GetString()))
                                        infoImage = lu.GetString();
                                }
                            }
                        }
                        if (tr.TryGetProperty("toptags", out var tags) && tags.TryGetProperty("tag", out var tagEl))
                        {
                            JsonElement firstTag = default;
                            bool hasTag = false;
                            if (tagEl.ValueKind == JsonValueKind.Array && tagEl.GetArrayLength() > 0) { firstTag = tagEl[0]; hasTag = true; }
                            else if (tagEl.ValueKind == JsonValueKind.Object) { firstTag = tagEl; hasTag = true; }
                            if (hasTag && firstTag.TryGetProperty("name", out var tn)) genre = tn.GetString();
                        }
                        // Fallback: artist top tags if track tags empty.
                        if (string.IsNullOrWhiteSpace(genre) && !string.IsNullOrWhiteSpace(artist))
                        {
                            try
                            {
                                var atUrl = $"/2.0/?method=artist.getTopTags&artist={Uri.EscapeDataString(artist)}&api_key={ApiKeyOrEmpty()}&format=json";
                                using var atDoc = await GetJsonAsync(atUrl, cancellationToken);
                                if (atDoc is not null && atDoc.RootElement.TryGetProperty("toptags", out var att) && att.TryGetProperty("tag", out var attag))
                                {
                                    JsonElement ft = default; bool fh = false;
                                    if (attag.ValueKind == JsonValueKind.Array && attag.GetArrayLength() > 0) { ft = attag[0]; fh = true; }
                                    else if (attag.ValueKind == JsonValueKind.Object) { ft = attag; fh = true; }
                                    if (fh && ft.TryGetProperty("name", out var ftn)) genre = ftn.GetString();
                                }
                            }
                            catch { /* best effort */ }
                        }
                    }
                }

                var finalImage = infoImage ?? imageUrl;

                int? year = null;
                if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
                {
                    var cacheKey = $"{artist}|{album}";
                    if (!yearCache.TryGetValue(cacheKey, out year))
                    {
                        try
                        {
                            var albumUrl = $"/2.0/?method=album.getInfo&artist={Uri.EscapeDataString(artist)}&album={Uri.EscapeDataString(album)}&api_key={ApiKeyOrEmpty()}&format=json";
                            using var albumDoc = await GetJsonAsync(albumUrl, cancellationToken);
                            if (albumDoc is not null && albumDoc.RootElement.TryGetProperty("album", out var albEl) &&
                                albEl.TryGetProperty("releasedate", out var rd) && rd.ValueKind == JsonValueKind.String)
                            {
                                var m = YearRegex.Match(rd.GetString() ?? "");
                                if (m.Success && int.TryParse(m.Groups[1].Value, out var y)) year = y;
                            }
                        }
                        catch { /* best effort */ }
                        yearCache[cacheKey] = year;
                    }
                }

                // Download thumbnail for the row (best effort, concurrency-limited by sem).
                Bitmap? coverArt = null;
                var thumbUrl = finalImage;
                if (!string.IsNullOrWhiteSpace(thumbUrl))
                {
                    try
                    {
                        var bytes = await _http.GetByteArrayAsync(thumbUrl, cancellationToken);
                        if (bytes is { Length: > 0 })
                        {
                            using var ms = new System.IO.MemoryStream(bytes);
                            coverArt = new Bitmap(ms);
                        }
                    }
                    catch { /* best effort */ }
                }

                details[idx] = (album, duration, genre, finalImage, year, coverArt);
            }
            finally
            {
                sem.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        int i = 0;
        foreach (var item in searchItems)
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) { i++; continue; }
            var artist = item.TryGetProperty("artist", out var a) ? a.GetString() ?? "" : "";
            details.TryGetValue(i, out var d);
            result.Add(new AudiometaTrack(
                item.TryGetProperty("mbid", out var mb) ? mb.GetString() ?? "" : "",
                name!,
                artist,
                d.Album,
                d.Genre,
                d.Year,
                d.Duration,
                d.ImageUrl,
                d.CoverArt));
            i++;
        }

        if (result.Count == 0 && LastFailureReason is null) LastFailureReason = "no_matches";
        return result;
    }

    public async Task<IReadOnlyList<AudiometaArtist>> SearchArtistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = new List<AudiometaArtist>();
        LastFailureReason = null;
        if (string.IsNullOrWhiteSpace(name)) { LastFailureReason = "no_matches"; return result; }

        var url = $"/2.0/?method=artist.getTopTags&artist={Uri.EscapeDataString(name.Trim())}&api_key={ApiKeyOrEmpty()}&format=json";
        using var doc = await GetJsonAsync(url, cancellationToken);
        if (doc is null) return result;

        var genres = new List<string>();
        if (doc.RootElement.TryGetProperty("toptags", out var tt) && tt.TryGetProperty("tag", out var tagEl))
        {
            if (tagEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tagEl.EnumerateArray())
                {
                    if (t.TryGetProperty("name", out var tn) && !string.IsNullOrWhiteSpace(tn.GetString()))
                        genres.Add(tn.GetString()!);
                }
            }
            else if (tagEl.ValueKind == JsonValueKind.Object && tagEl.TryGetProperty("name", out var tn2) && !string.IsNullOrWhiteSpace(tn2.GetString()))
            {
                genres.Add(tn2.GetString()!);
            }
        }

        if (genres.Count == 0) { LastFailureReason = "no_matches"; return result; }
        result.Add(new AudiometaArtist(name.Trim(), genres));
        return result;
    }

    public async Task<byte[]?> DownloadImageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            return await _http.GetByteArrayAsync(url, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LastFmService.DownloadImageAsync failed: {ex.Message}");
            return null;
        }
    }
}