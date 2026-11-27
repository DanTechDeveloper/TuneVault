using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneVault.Models;

namespace TuneVault.Services;

/// <summary>Parsed lyrics for one track.</summary>
/// <param name="Lines">Ordered lines; empty when nothing was found.</param>
/// <param name="IsSynced">True when the source had LRC timestamps.</param>
/// <param name="Source">"lrc", "embedded", or empty.</param>
public sealed record LyricsResult(
    IReadOnlyList<LyricLine> Lines,
    bool IsSynced,
    string Source)
{
    public static LyricsResult Empty { get; } = new(Array.Empty<LyricLine>(), false, "");
}

/// <summary>Loads local lyrics without touching the library database or the network.</summary>
public interface ILyricsService
{
    Task<LyricsResult> LoadAsync(Track track, CancellationToken cancellationToken = default);
}
