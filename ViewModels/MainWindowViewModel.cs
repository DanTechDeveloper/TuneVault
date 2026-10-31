using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TuneVault.Models;
using TuneVault.Services;

namespace TuneVault.ViewModels;

/// <summary>
/// State and actions for the main window. Contains no Avalonia or control types,
/// so it can be tested without opening a window.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlayer _audioPlayer;
    private readonly IAudioMetadataReader _metadataReader;
    private readonly IFilePickerService _filePicker;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTrackCommand))]
    private Track? _selectedTrack;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingStatusText = "";

    [ObservableProperty]
    private double _loadingProgress;

    [ObservableProperty]
    private string _statusMessage = "Add local audio files to start your library.";

    [ObservableProperty]
    private NavigationTab _currentTab = NavigationTab.AllSongs;

    public bool IsAllSongsTab => CurrentTab == NavigationTab.AllSongs;
    public bool IsAlbumsTab => CurrentTab == NavigationTab.Albums;
    public bool IsArtistsTab => CurrentTab == NavigationTab.Artists;
    public bool IsGenresTab => CurrentTab == NavigationTab.Genres;

    partial void OnCurrentTabChanged(NavigationTab value)
    {
        OnPropertyChanged(nameof(IsAllSongsTab));
        OnPropertyChanged(nameof(IsAlbumsTab));
        OnPropertyChanged(nameof(IsArtistsTab));
        OnPropertyChanged(nameof(IsGenresTab));
    }

    [RelayCommand]
    private void SelectTab(NavigationTab tab)
    {
        CurrentTab = tab;
    }

    private double _volume = 0.8;
    private Track? _loadedTrack;
    private bool _suppressSeek;
    private bool _disposed;

    public MainWindowViewModel(
        IAudioPlayer audioPlayer,
        IAudioMetadataReader metadataReader,
        IFilePickerService filePicker)
    {
        _audioPlayer = audioPlayer;
        _metadataReader = metadataReader;
        _filePicker = filePicker;
        _audioPlayer.Volume = (float)_volume;

        Tracks = new ObservableCollection<Track>();
        Tracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTracks));
    }

    public ObservableCollection<Track> Tracks { get; }

    public bool HasTracks => Tracks.Count > 0;

    public string NowPlayingTitle => NowPlayingTrack?.Title ?? "Nothing playing";

    public string NowPlayingArtist =>
        NowPlayingTrack?.Artist ?? "Add files, then press Play.";

    public Avalonia.Media.Imaging.Bitmap? NowPlayingCoverArt => NowPlayingTrack?.CoverArt;

    public bool HasNowPlayingCoverArt => NowPlayingTrack?.HasCoverArt ?? false;

    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";

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
            StatusMessage = $"Finished \"{_loadedTrack.Title}\".";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _audioPlayer.Dispose();
    }

    private bool CanImport => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task AddFilesAsync()
    {
        try
        {
            var paths = await _filePicker.PickAudioFilesAsync();
            if (paths.Count == 0)
            {
                return;
            }

            await ImportPathsAsync(paths);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open the file picker: {ex.Message}";
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task AddFolderAsync()
    {
        try
        {
            var folder = await _filePicker.PickAudioFolderAsync();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            IsLoading = true;
            LoadingProgress = 0;
            LoadingStatusText = "Scanning folder for audio files...";

            var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".wav", ".mp3", ".flac", ".ogg", ".opus", ".aiff", ".aif"
            };

            var files = await Task.Run(() =>
            {
                try
                {
                    return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => supportedExts.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f)
                        .ToArray();
                }
                catch
                {
                    return Array.Empty<string>();
                }
            });

            if (files.Length == 0)
            {
                StatusMessage = "No supported audio files found in selected folder.";
                IsLoading = false;
                return;
            }

            await ImportPathsAsync(files);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open folder picker: {ex.Message}";
            IsLoading = false;
        }
    }

    private async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        IsLoading = true;
        LoadingProgress = 0;
        LoadingStatusText = $"Preparing {paths.Count} file(s)...";

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var existingPaths = new HashSet<string>(
            Tracks.Where(t => t.FilePath is not null).Select(t => t.FilePath!),
            pathComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var toProcess = paths.Where(p => !existingPaths.Contains(p)).ToList();
        if (toProcess.Count == 0)
        {
            StatusMessage = "Selected file(s) are already in your library.";
            IsLoading = false;
            return;
        }

        var added = 0;
        var failed = 0;
        Track? lastAdded = null;
        var total = toProcess.Count;

        for (var i = 0; i < total; i++)
        {
            var path = toProcess[i];
            var fileName = Path.GetFileName(path);
            LoadingStatusText = $"Importing ({i + 1}/{total}): {fileName}";
            LoadingProgress = ((double)(i + 1) / total) * 100.0;

            try
            {
                var metadata = await Task.Run(() => _metadataReader.Read(path));
                var track = new Track
                {
                    Title = metadata.Title ?? TitleFromPath(path),
                    Artist = metadata.Artist ?? "Unknown artist",
                    Duration = metadata.Duration,
                    FilePath = path,
                    PictureData = metadata.PictureData,
                };

                Tracks.Add(track);
                lastAdded = track;
                added++;
            }
            catch
            {
                failed++;
            }
        }

        if (SelectedTrack is null && Tracks.Count > 0)
        {
            SelectedTrack = Tracks[0];
        }

        StatusMessage = (added, failed) switch
        {
            (0, 0) => "Those files are already in your library.",
            (0, _) => $"Could not read {failed} file(s).",
            (_, 0) => $"Added {added} track(s).",
            _ => $"Added {added} track(s); {failed} could not be read.",
        };

        IsLoading = false;
    }

    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        track ??= SelectedTrack ?? Tracks.FirstOrDefault();
        if (track?.FilePath is null)
        {
            StatusMessage = "No local audio file for this track.";
            return;
        }

        SelectedTrack = track;

        try
        {
            if (!ReferenceEquals(_loadedTrack, track))
            {
                _audioPlayer.Stop();
                var duration = _audioPlayer.Load(track.FilePath);

                _loadedTrack = track;
                NowPlayingTrack = track;
                StopCommand.NotifyCanExecuteChanged();
                DurationSeconds = duration.TotalSeconds;
                SetPosition(0);
                _audioPlayer.Play();
                StatusMessage = $"Playing \"{track.Title}\".";
            }
            else
            {
                if (_audioPlayer.IsPlaying)
                {
                    _audioPlayer.Pause();
                    StatusMessage = $"Paused \"{track.Title}\".";
                }
                else
                {
                    _audioPlayer.Play();
                    StatusMessage = $"Playing \"{track.Title}\".";
                }
            }

            UpdatePlaybackState();
        }
        catch (Exception ex)
        {
            ClearPlaybackState();
            StatusMessage = $"Could not play \"{track.Title}\": {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        var track = SelectedTrack;
        if (track?.FilePath is null)
        {
            StatusMessage = "No local audio file for this track.";
            return;
        }

        try
        {
            if (!ReferenceEquals(_loadedTrack, track))
            {
                _audioPlayer.Stop();
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
                StatusMessage = $"Paused \"{track.Title}\".";
            }
            else
            {
                _audioPlayer.Play();
                StatusMessage = $"Playing \"{track.Title}\".";
            }

            UpdatePlaybackState();
        }
        catch (Exception ex)
        {
            ClearPlaybackState();
            StatusMessage = $"Could not play \"{track.Title}\": {ex.Message}";
        }
    }

    private bool CanPlayPause() => SelectedTrack?.FilePath is not null;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (_loadedTrack is null)
        {
            return;
        }

        _audioPlayer.Stop();
        IsPlaying = false;
        SetPosition(0);
        StatusMessage = $"Stopped \"{_loadedTrack.Title}\".";
    }

    private bool CanStop() => _loadedTrack is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveTrack))]
    private void RemoveTrack()
    {
        var track = SelectedTrack;
        if (track is null)
        {
            return;
        }

        var index = Tracks.IndexOf(track);

        if (ReferenceEquals(_loadedTrack, track))
        {
            _audioPlayer.Stop();
            ClearPlaybackState();
        }

        Tracks.Remove(track);

        SelectedTrack = Tracks.Count == 0
            ? null
            : Tracks[Math.Min(index, Tracks.Count - 1)];

        StatusMessage = $"Removed \"{track.Title}\".";
    }

    private bool CanRemoveTrack() => SelectedTrack is not null;

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
            StatusMessage = $"Could not seek: {ex.Message}";
        }
    }

    private void UpdatePlaybackState()
    {
        IsPlaying = _audioPlayer.IsPlaying;
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
        _loadedTrack = null;
        NowPlayingTrack = null;
        IsPlaying = false;
        DurationSeconds = 0;
        SetPosition(0);
        StopCommand.NotifyCanExecuteChanged();
    }

    private static string TitleFromPath(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(title) ? "Untitled track" : title;
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");
}
