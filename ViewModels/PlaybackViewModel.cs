using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TuneVault.Models;
using TuneVault.Services;
using Material.Icons;

namespace TuneVault.ViewModels;

/// <summary>
/// Owns the loaded track, transport commands, progress, and volume. Reads the
/// selected track from <see cref="LibraryViewModel"/>.
/// </summary>
public sealed partial class PlaybackViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlayer _audioPlayer;
    private readonly LibraryViewModel _library;
    private double _volume = 0.8;
    private Track? _loadedTrack;
    private bool _suppressSeek;
    private bool _disposed;

    public PlaybackViewModel(
        IAudioPlayer audioPlayer,
        LibraryViewModel library)
    {
        _audioPlayer = audioPlayer;
        _library = library;
        _audioPlayer.Volume = (float)_volume;

        _library.PropertyChanged += OnLibraryPropertyChanged;
        _library.TrackRemoved += OnTrackRemoved;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingTitle))]
    [NotifyPropertyChangedFor(nameof(NowPlayingArtist))]
    [NotifyPropertyChangedFor(nameof(NowPlayingCoverArt))]
    [NotifyPropertyChangedFor(nameof(HasNowPlayingCoverArt))]
    private Track? _nowPlayingTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIconKind))]
    [NotifyPropertyChangedFor(nameof(PlayPauseToolTip))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private double _durationSeconds;

    public string NowPlayingTitle => NowPlayingTrack?.Title ?? "Nothing playing";

    public string NowPlayingArtist =>
        NowPlayingTrack?.ArtistAlbum ??
        (_library.HasTracks
            ? "Select a track, then press Play."
            : "Add Files Or Add Folder then press play");

    public Avalonia.Media.Imaging.Bitmap? NowPlayingCoverArt => NowPlayingTrack?.CoverArt;

    public bool HasNowPlayingCoverArt => NowPlayingTrack?.HasCoverArt ?? false;

    public MaterialIconKind PlayPauseIconKind => IsPlaying ? MaterialIconKind.PauseCircle : MaterialIconKind.PlayCircle;

    public string PlayPauseToolTip => IsPlaying ? "Pause track" : "Play track";

    public string PositionText => FormatTime(TimeSpan.FromSeconds(PositionSeconds));

    public string DurationText => FormatTime(TimeSpan.FromSeconds(DurationSeconds));

    /// <summary>
    /// Kept manual because the toolkit's generated setter cannot clamp the value
    /// before it is stored.
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, Math.Clamp(value, 0, 1)))
            {
                return;
            }

            _audioPlayer.Volume = (float)_volume;
        }
    }

    /// <summary>
    /// Called by the View's timer so the progress slider follows playback.
    /// </summary>
    public void RefreshPlayback()
    {
        if (_loadedTrack is null)
        {
            return;
        }

        var wasPlaying = IsPlaying;
        var isPlaying = _audioPlayer.IsPlaying;
        IsPlaying = isPlaying;
        if (_loadedTrack is not null) _library.SetTrackPlayingState(_loadedTrack.Id, isPlaying);

        var position = _audioPlayer.Position.TotalSeconds;
        if (Math.Abs(position - PositionSeconds) >= 0.1)
        {
            _suppressSeek = true;
            PositionSeconds = position;
            _suppressSeek = false;
        }

        if (wasPlaying &&
            !isPlaying &&
            DurationSeconds > 0 &&
            _audioPlayer.Position >= _audioPlayer.Duration - TimeSpan.FromMilliseconds(500))
        {
            // track finished — clean up so the next click goes through the
            // full load branch instead of the (now stale) "same track" toggle
            try { _audioPlayer.Stop(); } catch { }
            _library.ClearAllPlayingState();
            if (_loadedTrack is not null)
            {
                _library.SetTrackPlayingState(_loadedTrack.Id, false);
            }
            _loadedTrack = null;
            IsPlaying = false;
            // Reset slider to 0 so the state is "fresh", not stale at end.
            _suppressSeek = true;
            PositionSeconds = 0;
            _suppressSeek = false;
            StopCommand.NotifyCanExecuteChanged();
            // keep NowPlayingTrack and DurationSeconds for display (completed state)
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _library.PropertyChanged -= OnLibraryPropertyChanged;
        _library.TrackRemoved -= OnTrackRemoved;

        if (NowPlayingTrack is not null)
        {
            NowPlayingTrack.PropertyChanged -= OnNowPlayingTrackPropertyChanged;
        }

        _audioPlayer.Dispose();
    }

    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        // With no explicit row parameter the header button targets the loaded
        // track first, so its Play/Pause label always matches the action.
        track ??= _loadedTrack ?? _library.SelectedTrack ?? _library.Tracks.FirstOrDefault();
        if (track?.FilePath is null)
        {
            return;
        }

        if (!File.Exists(track.FilePath))
        {
            track.IsMissing = true;
            return;
        }

        _library.SelectedTrack = track;

        try
        {
            if (!ReferenceEquals(_loadedTrack, track))
            {
                _audioPlayer.Stop();
                _library.ClearAllPlayingState();
                if (_loadedTrack is not null)
                {
                    _library.SetTrackPlayingState(_loadedTrack.Id, false);
                }
                var duration = _audioPlayer.Load(track.FilePath);

                _loadedTrack = track;
                NowPlayingTrack = track;
                StopCommand.NotifyCanExecuteChanged();
                DurationSeconds = duration.TotalSeconds;
                SetPosition(0);
                _audioPlayer.Play();
            }
            else
            {
                if (_audioPlayer.IsPlaying)
                {
                    _audioPlayer.Pause();
                }
                else
                {
                    // If the track already finished (position at end), restart
                    // from the beginning instead of playing from the end.
                    if (_audioPlayer.Position >= _audioPlayer.Duration - TimeSpan.FromMilliseconds(500))
                    {
                        try { _audioPlayer.Seek(TimeSpan.Zero); } catch { }
                    }
                    _audioPlayer.Play();
                }
            }

            UpdatePlaybackState();
        }
        catch
        {
            ClearPlaybackState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        var track = _library.SelectedTrack;
        if (track?.FilePath is null)
        {
            return;
        }

        if (!File.Exists(track.FilePath))
        {
            track.IsMissing = true;
            return;
        }

        try
        {
            if (!ReferenceEquals(_loadedTrack, track))
            {
                _audioPlayer.Stop();
                _library.ClearAllPlayingState();
                if (_loadedTrack is not null)
                {
                    _library.SetTrackPlayingState(_loadedTrack.Id, false);
                }
                var duration = _audioPlayer.Load(track.FilePath);

                _loadedTrack = track;
                NowPlayingTrack = track;
                StopCommand.NotifyCanExecuteChanged();
                DurationSeconds = duration.TotalSeconds;
                SetPosition(0);
            }

            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Pause();
            }
            else
            {
                _audioPlayer.Play();
            }

            UpdatePlaybackState();
        }
        catch
        {
            ClearPlaybackState();
        }
    }

    private bool CanPlayPause() => _library.SelectedTrack?.FilePath is not null;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (_loadedTrack is null)
        {
            return;
        }

        _audioPlayer.Stop();
        IsPlaying = false;
        _library.ClearAllPlayingState();
        if (_loadedTrack is not null) _library.SetTrackPlayingState(_loadedTrack.Id, false);
        SetPosition(0);
    }

    private bool CanStop() => _loadedTrack is not null;

    /// <summary>
    /// The track list the Next/Previous buttons navigate. Picks the list the user
    /// is currently viewing (All Songs, album detail, or artist songs) so
    /// navigation stays within the filtered context.
    /// </summary>
    private System.Collections.ObjectModel.ObservableCollection<Track> CurrentQueue =>
        _library.IsAlbumDetailView ? _library.AlbumTracks :
        _library.IsArtistDetailView ? _library.ArtistSongs :
        _library.IsGenreSongsView ? _library.GenreSongs :
        _library.Tracks; // AllSongs (default, incl. fallback for list views)

    [RelayCommand]
    private void Next()
    {
        var queue = CurrentQueue;
        if (queue.Count == 0)
        {
            return;
        }

        // Nothing loaded yet: start playback at the selected track (or the first).
        if (_loadedTrack is null)
        {
            var target = _library.SelectedTrack ?? (queue.Count > 0 ? queue[0] : null);
            if (target is not null)
            {
                PlayTrack(target);
            }
            return;
        }

        // Match by Id (not reference) so a rebuilt collection still resolves.
        // Fall back to the selected track, then to the first track, so Next
        // always advances when a queue is present.
        // Prefer the loaded track; fall back to the selected track, then to
        // the first track, so Next always advances when a queue is present.
        var currentIdx = -1;
        for (var i = 0; i < queue.Count; i++)
        {
            if (_loadedTrack is not null && queue[i].Id == _loadedTrack.Id)
            {
                currentIdx = i;
                break;
            }
        }
        if (currentIdx < 0 && _library.SelectedTrack is not null)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                if (queue[i].Id == _library.SelectedTrack.Id)
                {
                    currentIdx = i;
                    break;
                }
            }
        }
        if (currentIdx < 0 && queue.Count > 0)
        {
            currentIdx = 0;
        }
        if (currentIdx < 0 || currentIdx + 1 >= queue.Count)
        {
            return; // no next track (no wrap at the boundary)
        }

        PlayTrack(queue[currentIdx + 1]);
    }

    [RelayCommand]
    private void Previous()
    {
        var queue = CurrentQueue;
        if (queue.Count == 0 || _loadedTrack is null)
        {
            return;
        }

        var current = _loadedTrack;
        var idx = -1;
        if (current is not null)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                if (queue[i].Id == current.Id)
                {
                    idx = i;
                    break;
                }
            }
        }
        if (idx < 0 && _library.SelectedTrack is not null)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                if (queue[i].Id == _library.SelectedTrack.Id)
                {
                    idx = i;
                    break;
                }
            }
        }
        if (idx > 0)
        {
            PlayTrack(queue[idx - 1]);
            return;
        }

        // At the first track (or not found): restart the current track.
        try
        {
            _audioPlayer.Seek(TimeSpan.Zero);
        }
        catch
        {
        }
        SetPosition(0);
        if (!_audioPlayer.IsPlaying)
        {
            _audioPlayer.Play();
        }
        UpdatePlaybackState();
    }

    partial void OnPositionSecondsChanged(double value)
    {
        if (_suppressSeek || _loadedTrack is null)
        {
            return;
        }

        try
        {
            _audioPlayer.Seek(TimeSpan.FromSeconds(value));
        }
        catch
        {
        }
    }

    partial void OnNowPlayingTrackChanged(Track? oldValue, Track? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnNowPlayingTrackPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnNowPlayingTrackPropertyChanged;
        }
    }

    private void OnNowPlayingTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Track.Title):
                OnPropertyChanged(nameof(NowPlayingTitle));
                break;
            case nameof(Track.Artist):
            case nameof(Track.Album):
            case nameof(Track.ArtistAlbum):
                OnPropertyChanged(nameof(NowPlayingArtist));
                break;
            case nameof(Track.CoverArt):
            case nameof(Track.PictureData):
            case nameof(Track.HasCoverArt):
                OnPropertyChanged(nameof(NowPlayingCoverArt));
                OnPropertyChanged(nameof(HasNowPlayingCoverArt));
                break;
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedTrack))
        {
            PlayPauseCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(LibraryViewModel.HasTracks))
        {
            OnPropertyChanged(nameof(NowPlayingArtist));
        }
    }

    private void OnTrackRemoved(Track track)
    {
        if (!ReferenceEquals(_loadedTrack, track))
        {
            return;
        }

        _audioPlayer.Stop();
        ClearPlaybackState();
    }

    private void UpdatePlaybackState()
    {
        IsPlaying = _audioPlayer.IsPlaying;
        if (_loadedTrack is not null)
        {
            _library.SetTrackPlayingState(_loadedTrack.Id, IsPlaying);
        }
        SetPosition(_audioPlayer.Position.TotalSeconds);
    }

    private void SetPosition(double seconds)
    {
        _suppressSeek = true;
        PositionSeconds = Math.Max(0, seconds);
        _suppressSeek = false;
    }

    private void ClearPlaybackState()
    {
        try { _audioPlayer.Stop(); } catch { }

        if (_loadedTrack is not null)
        {
            _library.SetTrackPlayingState(_loadedTrack.Id, false);
        }
        _library.ClearAllPlayingState();

        _loadedTrack = null;
        NowPlayingTrack = null;
        IsPlaying = false;
        DurationSeconds = 0;
        SetPosition(0);
        StopCommand.NotifyCanExecuteChanged();
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");
}
