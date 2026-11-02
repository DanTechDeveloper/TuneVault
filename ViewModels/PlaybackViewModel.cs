using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TuneVault.Models;
using TuneVault.Services;

namespace TuneVault.ViewModels;

/// <summary>
/// Owns the loaded track, transport commands, progress, and volume. Reads the
/// selected track from <see cref="LibraryViewModel"/> and writes status text
/// through <see cref="StatusViewModel"/>.
/// </summary>
public sealed partial class PlaybackViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlayer _audioPlayer;
    private readonly LibraryViewModel _library;
    private readonly StatusViewModel _status;
    private double _volume = 0.8;
    private Track? _loadedTrack;
    private bool _suppressSeek;
    private bool _disposed;

    public PlaybackViewModel(
        IAudioPlayer audioPlayer,
        LibraryViewModel library,
        StatusViewModel status)
    {
        _audioPlayer = audioPlayer;
        _library = library;
        _status = status;
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
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _durationSeconds;

    public string NowPlayingTitle => NowPlayingTrack?.Title ?? "Nothing playing";

    public string NowPlayingArtist =>
        NowPlayingTrack?.Artist ?? "Add files, then press Play.";

    public Avalonia.Media.Imaging.Bitmap? NowPlayingCoverArt => NowPlayingTrack?.CoverArt;

    public bool HasNowPlayingCoverArt => NowPlayingTrack?.HasCoverArt ?? false;

    public string PlayPauseLabel => IsPlaying ? "❚❚ Pause" : "▶ Play";

    public string ProgressText =>
        $"{FormatTime(TimeSpan.FromSeconds(PositionSeconds))} / " +
        $"{FormatTime(TimeSpan.FromSeconds(DurationSeconds))}";

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
        _loadedTrack.IsPlaying = isPlaying;

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
            _status.Message = $"Finished \"{_loadedTrack.Title}\".";
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
            _status.Message = "No local audio file for this track.";
            return;
        }

        if (!File.Exists(track.FilePath))
        {
            track.IsMissing = true;
            _status.Message = $"File not found: {track.FilePath}";
            return;
        }

        _library.SelectedTrack = track;

        try
        {
            if (!ReferenceEquals(_loadedTrack, track))
            {
                _audioPlayer.Stop();
                if (_loadedTrack is not null)
                {
                    _loadedTrack.IsPlaying = false;
                }
                var duration = _audioPlayer.Load(track.FilePath);

                _loadedTrack = track;
                NowPlayingTrack = track;
                StopCommand.NotifyCanExecuteChanged();
                DurationSeconds = duration.TotalSeconds;
                SetPosition(0);
                _audioPlayer.Play();
                _status.Message = $"Playing \"{track.Title}\".";
            }
            else
            {
                if (_audioPlayer.IsPlaying)
                {
                    _audioPlayer.Pause();
                    _status.Message = $"Paused \"{track.Title}\".";
                }
                else
                {
                    _audioPlayer.Play();
                    _status.Message = $"Playing \"{track.Title}\".";
                }
            }

            UpdatePlaybackState();
        }
        catch (Exception ex)
        {
            ClearPlaybackState();
            _status.Message = $"Could not play \"{track.Title}\": {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        var track = _library.SelectedTrack;
        if (track?.FilePath is null)
        {
            _status.Message = "No local audio file for this track.";
            return;
        }

        if (!File.Exists(track.FilePath))
        {
            track.IsMissing = true;
            _status.Message = $"File not found: {track.FilePath}";
            return;
        }

        try
        {
            if (!ReferenceEquals(_loadedTrack, track))
            {
                _audioPlayer.Stop();
                if (_loadedTrack is not null)
                {
                    _loadedTrack.IsPlaying = false;
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
                _status.Message = $"Paused \"{track.Title}\".";
            }
            else
            {
                _audioPlayer.Play();
                _status.Message = $"Playing \"{track.Title}\".";
            }

            UpdatePlaybackState();
        }
        catch (Exception ex)
        {
            ClearPlaybackState();
            _status.Message = $"Could not play \"{track.Title}\": {ex.Message}";
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
        _loadedTrack.IsPlaying = false;
        SetPosition(0);
        _status.Message = $"Stopped \"{_loadedTrack.Title}\".";
    }

    private bool CanStop() => _loadedTrack is not null;

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
        catch (Exception ex)
        {
            _status.Message = $"Could not seek: {ex.Message}";
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
            _loadedTrack.IsPlaying = IsPlaying;
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
        if (_loadedTrack is not null)
        {
            _loadedTrack.IsPlaying = false;
        }

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
