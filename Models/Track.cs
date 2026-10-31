using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace TuneVault.Models;

/// <summary>
/// Plain domain object for a track.
/// </summary>
public sealed class Track
{
    private Bitmap? _coverArt;

    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    public string Artist { get; init; } = "Unknown artist";

    /// <summary>Local audio file. Null for tracks created without a file.</summary>
    public string? FilePath { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Embedded raw image bytes from audio tags.</summary>
    public byte[]? PictureData { get; init; }

    public bool HasCoverArt => PictureData is { Length: > 0 };

    /// <summary>Decoded bitmap for UI binding.</summary>
    public Bitmap? CoverArt
    {
        get
        {
            if (_coverArt is null && PictureData is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(PictureData);
                    _coverArt = new Bitmap(ms);
                }
                catch
                {
                    // Invalid/unsupported image payload
                }
            }
            return _coverArt;
        }
        init => _coverArt = value;
    }

    public string DurationDisplay =>
        Duration > TimeSpan.Zero
            ? Duration.TotalHours >= 1
                ? Duration.ToString(@"h\:mm\:ss")
                : Duration.ToString(@"m\:ss")
            : "—";
}

