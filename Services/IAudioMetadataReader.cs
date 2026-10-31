using System;
using System.IO;
using System.Linq;
using NAudio.SoundFile;

namespace TuneVault.Services;

public sealed record AudioFileMetadata(
    string? Title,
    string? Artist,
    TimeSpan Duration,
    byte[]? PictureData = null);

public interface IAudioMetadataReader
{
    /// <summary>
    /// Reads embedded tags and duration. Throws when the file cannot be decoded.
    /// </summary>
    AudioFileMetadata Read(string filePath);
}

/// <summary>
/// Reads metadata through libsndfile and TagLibSharp, which supports WAV, AIFF, FLAC, OGG,
/// Opus and MP3 on Windows, Linux and macOS.
/// </summary>
public sealed class SoundFileMetadataReader : IAudioMetadataReader
{
    public AudioFileMetadata Read(string filePath)
    {
        using var reader = new SoundFileReader(filePath);
        var tags = reader.Tags;

        var title = Normalize(tags.Title);
        var artist = Normalize(tags.Artist);
        byte[]? pictureData = null;

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = Normalize(tagFile.Tag.Title);
            }
            if (string.IsNullOrWhiteSpace(artist))
            {
                artist = Normalize(tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist);
            }

            var pic = tagFile.Tag.Pictures.FirstOrDefault();
            if (pic?.Data.Data is { Length: > 0 } bytes)
            {
                pictureData = bytes;
            }
        }
        catch
        {
            // libsndfile basic metadata is sufficient fallback
        }

        return new AudioFileMetadata(
            title,
            artist,
            reader.TotalTime,
            pictureData);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
