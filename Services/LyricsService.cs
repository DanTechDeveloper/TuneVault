using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TuneVault.Models;

namespace TuneVault.Services;

/// <summary>
/// Reads a same-folder .lrc sidecar first (Poweramp convention), then plain
/// embedded lyrics via TagLibSharp. Missing or malformed data is never fatal.
/// </summary>
public sealed class LyricsService : ILyricsService
{
    public Task<LyricsResult> LoadAsync(Track track, CancellationToken cancellationToken = default)
    {
        var path = track.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(LyricsResult.Empty);
        }

        return Task.Run(() =>
        {
            foreach (var candidate in new[]
                     {
                         Path.ChangeExtension(path, ".lrc"),
                         Path.ChangeExtension(path, ".LRC"),
                     })
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }
                try
                {
                    // UTF-8 is the de facto LRC encoding; BOM is tolerated by File.ReadAllText.
                    return LrcParser.Parse(File.ReadAllText(candidate));
                }
                catch
                {
                    // Try the other casing / embedded tag.
                }
            }

            try
            {
                using var tagFile = TagLib.File.Create(path);
                var lyrics = tagFile.Tag.Lyrics;
                if (!string.IsNullOrWhiteSpace(lyrics))
                {
                    var result = LrcParser.Parse(lyrics);
                    return result with { Source = "embedded" };
                }
            }
            catch
            {
                // No embedded lyrics, or the file cannot be read as a tagged file.
            }

            return LyricsResult.Empty;
        }, cancellationToken);
    }
}
