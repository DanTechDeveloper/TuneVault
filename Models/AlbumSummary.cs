using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace TuneVault.Models;

/// <summary>
/// Read-model for an album tile in the Albums view: name, track count, and a
/// representative cover image. Kept separate from <see cref="Track"/> so the
/// album list can be grouped in memory without pulling full track rows.
/// </summary>
public sealed record AlbumSummary(string Name, int TrackCount, byte[]? PictureData)
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