using System;

namespace TuneVault.Models;

/// <summary>
/// Plain domain object for a track. It has no knowledge of the UI.
/// </summary>
public sealed class Track
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    public string Artist { get; init; } = "Unknown artist";

    /// <summary>Local audio file. Null for tracks created without a file.</summary>
    public string? FilePath { get; init; }

    public TimeSpan Duration { get; init; }

    public string DurationDisplay =>
        Duration > TimeSpan.Zero
            ? Duration.TotalHours >= 1
                ? Duration.ToString(@"h\:mm\:ss")
                : Duration.ToString(@"m\:ss")
            : "—";
}
