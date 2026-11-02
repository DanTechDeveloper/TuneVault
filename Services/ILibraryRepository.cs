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
}

/// <summary>Result of persisting one track, used to report duplicates and failures.</summary>
public sealed record AddResult(Track Track, AddOutcome Outcome);

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

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
