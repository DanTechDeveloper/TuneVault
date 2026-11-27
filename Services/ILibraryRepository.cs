using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneVault.Models;

namespace TuneVault.Services;

public enum AddOutcome
{
    Added,
    DuplicatePath,
    DuplicateIdentity,
    Failed,
}

/// <summary>Fields the library list can be ordered by. Mapped to fixed SQL columns.</summary>
public enum TrackSortField
{
    Title,
    Artist,
    Album,
    Genre,
    Year,
    TotalDuration,
    TotalTracks,
}

/// <summary>Result of persisting one track, used to report duplicates and failures.</summary>
public sealed record AddResult(Track Track, AddOutcome Outcome);

/// <summary>Last playback session, restored on startup in the paused state.</summary>
public sealed record PlaybackSessionState(Guid TrackId, double PositionSeconds, bool IsShuffle);

/// <summary>
/// Persistence boundary for the music library. Keeps SQLite types out of the
/// ViewModels, mirroring <see cref="IAudioMetadataReader"/> and <see cref="IAudioPlayer"/>.
/// </summary>
public interface ILibraryRepository
{
    /// <summary>Creates or migrates the schema. Safe to call on every start.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads every track, ordered by the database.</summary>
    Task<IReadOnlyList<Track>> LoadAllAsync(
        TrackSortField sortBy = TrackSortField.Title,
        bool descending = false,
        CancellationToken cancellationToken = default);

    /// <summary>Map of normalized file path to the stored title.</summary>
    Task<IReadOnlyDictionary<string, string>> LoadPathIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>True when another stored track already uses this title/artist.</summary>
    Task<bool> IdentityExistsAsync(
        string? title,
        string? artist,
        Guid? excludeTrackId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts the batch in a single transaction; duplicate rows are reported, not thrown.</summary>
    Task<IReadOnlyList<AddResult>> AddRangeAsync(
        IReadOnlyList<Track> tracks,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>Sets the favorite flag without touching any metadata columns.</summary>
    Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Last saved track/position/shuffle, or null when nothing was saved.</summary>
    Task<PlaybackSessionState?> LoadPlaybackStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the resume point; a null track id clears it.</summary>
    Task SavePlaybackStateAsync(
        Guid? trackId,
        double positionSeconds,
        bool isShuffle,
        CancellationToken cancellationToken = default);
}
