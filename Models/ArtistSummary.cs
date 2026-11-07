using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace TuneVault.Models;

/// <summary>
/// Read-model for an artist tile in the Album Artists view: name, track count,
/// and a representative cover image.
/// </summary>
public sealed record ArtistSummary(string Name, int TrackCount, byte[]? PictureData, TimeSpan TotalDuration = default, int Year = 0, int AlbumCount = 0)
{
    public bool HasCoverArt => PictureData is { Length: > 0 };
    public bool HasYear => Year > 0;
    public string AlbumCountDisplay => AlbumCount == 1 ? "1 Album" : $"{AlbumCount} Albums";

    public Bitmap? CoverArt
    {
        get
        {
            if (PictureData is not { Length: > 0 })
            {
                return null;
            }

            try
            {
                using var ms = new MemoryStream(PictureData);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }
    }
}