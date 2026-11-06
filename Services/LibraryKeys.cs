using System;

namespace TuneVault.Services;

/// <summary>
/// Normalizes the two keys the library database uses to decide whether a file is
/// already in the library.
/// </summary>
public static class LibraryKeys
{
    private const char Separator = '\u001F';
    private const string UntitledTrack = "Untitled track";

    /// <summary>
    /// Stable key for a file path: absolute, and case-folded on Windows so the
    /// same file imported with different casing is still a duplicate.
    /// </summary>
    public static string ForPath(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? fullPath.ToLowerInvariant() : fullPath;
    }

    /// <summary>
    /// Key for the title/artist duplicate rule. Returns null for untitled
    /// fallbacks, which must never be treated as duplicates of each other.
    /// </summary>
    public static string? ForIdentity(string? title, string? artist)
    {
        var normalizedTitle = title?.Trim() ?? "";
        if (normalizedTitle.Length == 0 ||
            normalizedTitle.Equals(UntitledTrack, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalizedArtist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        return normalizedTitle.ToLowerInvariant() + Separator + normalizedArtist.ToLowerInvariant();
    }
}
