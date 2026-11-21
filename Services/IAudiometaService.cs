using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace TuneVault.Services;

/// <summary>Artist metadata retrieved from Audiometa (genres only; other fields
/// are intentionally not surfaced to keep the payload small).</summary>
public sealed record AudiometaArtist(
    string Name,
    IReadOnlyList<string> Genres);

/// <summary>Track metadata retrieved from Audiometa. Only the fields that map to
/// TuneVault's Track model are exposed.</summary>
public sealed record AudiometaTrack(
    string Id,
    string Name,
    string Artist,
    string? Album,
    string? Genre,
    int? Year,
    int? DurationMs,
    string? ImageUrl,
    Bitmap? CoverArt = null)
{
    public string DurationDisplay => DurationMs is > 0
        ? TimeSpan.FromMilliseconds(DurationMs.Value).ToString(@"m\:ss")
        : string.Empty;
    public string ArtistAlbum
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Album)) return Artist;
            if (string.IsNullOrWhiteSpace(Artist)) return Album!;
            return $"{Artist} • {Album}";
        }
    }
    public string GenreDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Genre) && Year is > 0) return $"{Genre} • {Year}";
            if (!string.IsNullOrWhiteSpace(Genre)) return Genre!;
            if (Year is > 0) return Year.Value.ToString();
            return "Unknown Genre";
        }
    }
}

/// <summary>Optional online lookup for track/artist metadata via audiometa.io.
/// All methods degrade to empty/null on failure so offline editing keeps working.</summary>
public interface IAudiometaService
{
    /// <summary>Reason for the last failed/empty lookup (null on success with results).</summary>
    string? LastFailureReason { get; }

    Task<IReadOnlyList<AudiometaTrack>> SearchTracksAsync(string title, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AudiometaArtist>> SearchArtistsAsync(string name, CancellationToken cancellationToken = default);
    Task<byte[]?> DownloadImageAsync(string url, CancellationToken cancellationToken = default);
}