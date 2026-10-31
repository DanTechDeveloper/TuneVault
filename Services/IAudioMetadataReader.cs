using System;
using NAudio.SoundFile;

namespace TuneVault.Services;

public sealed record AudioFileMetadata(string? Title, string? Artist, TimeSpan Duration);

public interface IAudioMetadataReader
{
    /// <summary>
    /// Reads embedded tags and duration. Throws when the file cannot be decoded.
    /// </summary>
    AudioFileMetadata Read(string filePath);
}

/// <summary>
/// Reads metadata through libsndfile, which supports WAV, AIFF, FLAC, OGG,
/// Opus and MP3 on Windows, Linux and macOS.
/// </summary>
public sealed class SoundFileMetadataReader : IAudioMetadataReader
{
    public AudioFileMetadata Read(string filePath)
    {
        using var reader = new SoundFileReader(filePath);
        var tags = reader.Tags;

        return new AudioFileMetadata(
            Normalize(tags.Title),
            Normalize(tags.Artist),
            reader.TotalTime);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
