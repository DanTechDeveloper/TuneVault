using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace TuneVault.Models;

/// <summary>
/// Domain model for a track with observable properties for dynamic updates.
/// </summary>
public sealed partial class Track : ObservableObject
{
    private Bitmap? _coverArt;

    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArtistAlbum))]
    private string _artist = "Unknown artist";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArtistAlbum))]
    private string? _album;

    [ObservableProperty]
    private string? _genre;

    [ObservableProperty]
    private uint _year;

    /// <summary>Local audio file. Null for tracks created without a file.</summary>
    public string? FilePath { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Embedded raw image bytes from audio tags.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCoverArt))]
    [NotifyPropertyChangedFor(nameof(CoverArt))]
    private byte[]? _pictureData;

    public bool HasCoverArt => PictureData is { Length: > 0 };

    /// <summary>
    /// True when the persisted file could not be found at load time. Computed from
    /// disk on startup and never stored: an unmounted drive must not lose library rows.
    /// </summary>
    [ObservableProperty]
    private bool _isMissing;

    /// <summary>
    /// True while this track is the one currently producing audio. Kept on the
    /// model so the per-row play button can react to playback state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIconKind))]
    [NotifyPropertyChangedFor(nameof(PlayPauseToolTip))]
    private bool _isPlaying;

    public MaterialIconKind PlayPauseIconKind => IsPlaying ? MaterialIconKind.PauseCircle : MaterialIconKind.PlayCircle;

    public string PlayPauseToolTip => IsPlaying ? "Pause track" : "Play track";

    /// <summary>Combined artist/album label for the track row.</summary>
    public string ArtistAlbum => string.IsNullOrWhiteSpace(Album)
        ? Artist
        : $"{Artist} - {Album}";

    /// <summary>Decoded bitmap for UI binding.</summary>
    public Bitmap? CoverArt
    {
        get
        {
            if (_coverArt is null && PictureData is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(PictureData);
                    _coverArt = new Bitmap(ms);
                }
                catch
                {
                    // Invalid/unsupported image payload
                }
            }
            return _coverArt;
        }
        set
        {
            _coverArt = value;
            OnPropertyChanged();
        }
    }

    partial void OnPictureDataChanged(byte[]? value)
    {
        _coverArt = null;
        OnPropertyChanged(nameof(CoverArt));
    }

    public string DurationDisplay =>
        Duration > TimeSpan.Zero
            ? Duration.TotalHours >= 1
                ? Duration.ToString(@"h\:mm\:ss")
                : Duration.ToString(@"m\:ss")
            : "—";
}

