using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace TuneVault.Models;

/// <summary>
/// Read-model for a genre tile in the Genres view: name, track count, and a
/// representative cover image.
/// </summary>
public sealed record GenreSummary(string Name, int TrackCount, byte[]? PictureData, TimeSpan TotalDuration = default)
{
    public bool HasCoverArt => PictureData is { Length: > 0 };

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