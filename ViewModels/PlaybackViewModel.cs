using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly ILibraryRepository? _repository;
    private readonly ILyricsService? _lyricsService;
    private int _activeLyricIndex = -1;
    private double _volume = 0.8;
    private Track? _loadedTrack;
    // Queue captured when a track is loaded, so Prev/Next follow the playing
    // track even after the user switches views. Null falls back to the view queue.
    private List<Track>? _loadedQueue;
    // Pre-shuffle order of the captured queue, restored when shuffle is turned off.
    private List<Track>? _unshuffledQueue;
    private string _loadedQueueLabel = "All Songs";
    private string _loadedQueueSource = "AllSongs";
    // CurrentTab only changes on an explicit tab switch (unlike the queue source,
    // which search-clear reselects can change even without user navigation).
    private NavigationTab _loadedQueueTab = NavigationTab.AllSongs;
    private bool _suppressSeek;
    private bool _disposed;

    public PlaybackViewModel(
        IAudioPlayer audioPlayer,
        LibraryViewModel library,
        ILibraryRepository? repository = null,
        ILyricsService? lyricsService = null)
    {
        _audioPlayer = audioPlayer;
        _library = library;
        _repository = repository;
        _lyricsService = lyricsService;
        _audioPlayer.Volume = (float)_volume;

        _library.PropertyChanged += OnLibraryPropertyChanged;
        _library.TrackRemoved += OnTrackRemoved;
        _library.SearchCleared += OnSearchCleared;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingTitle))]
    [NotifyPropertyChangedFor(nameof(NowPlayingArtist))]
    [NotifyPropertyChangedFor(nameof(NowPlayingCoverArt))]
    [NotifyPropertyChangedFor(nameof(HasNowPlayingCoverArt))]
    [NotifyPropertyChangedFor(nameof(HasNowPlayingTrack))]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingFavorite))]
    private Track? _nowPlayingTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseIconKind))]
    [NotifyPropertyChangedFor(nameof(PlayPauseToolTip))]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isShuffle;

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

    public bool HasNowPlayingTrack => NowPlayingTrack is not null;

    public bool IsNowPlayingFavorite => NowPlayingTrack?.IsFavorite ?? false;

    [ObservableProperty]
    private bool _isLyricsView;

    [ObservableProperty]
    private string _lyricsStatusText = "";

    [ObservableProperty]
    private ObservableCollection<LyricLine> _lyrics = new();

    [ObservableProperty]
    private LyricLine? _activeLyricLine;

    public bool HasLyrics => Lyrics.Count > 0;

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
        UpdateActiveLyric(PositionSeconds);

        if (wasPlaying &&
            !isPlaying &&
            DurationSeconds > 0 &&
            _audioPlayer.Position >= _audioPlayer.Duration - TimeSpan.FromMilliseconds(500))
        {
            // Track finished: auto-advance within the captured queue (the same
            // order Next uses). Next() no-ops at the boundary, so fall through.
            var before = _loadedTrack;
            Next();
            // Next() no-ops at the boundary (or on a missing file): only skip the
            // cleanup when it actually started playback.
            if (!ReferenceEquals(_loadedTrack, before) || _audioPlayer.IsPlaying)
            {
                return;
            }

            // No next track: clean up so the next click goes through the
            // full load branch instead of the (now stale) "same track" toggle.
            // Keep NowPlayingTrack and DurationSeconds for the completed display.
            ResetPlaybackState(unloadTrack: true, clearDisplay: false);
        }
    }

    /// <summary>
    /// Restores the last saved session into the paused state. Runs once after the
    /// library has loaded; never starts playback.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        if (_repository is null || _loadedTrack is not null)
        {
            return;
        }

        PlaybackSessionState? state;
        try
        {
            state = await _repository.LoadPlaybackStateAsync();
        }
        catch
        {
            return;
        }

        if (state is null)
        {
            return;
        }

        var track = _library.Tracks.FirstOrDefault(t => t.Id == state.TrackId);
        if (track?.FilePath is null || !File.Exists(track.FilePath))
        {
            return;
        }

        // Set the mode before capturing the queue so a restored shuffle session
        // gets a shuffled queue.
        IsShuffle = state.IsShuffle;
        if (!TryLoadTrack(track, captureQueue: true, autoPlay: false))
        {
            return;
        }

        var position = Math.Clamp(state.PositionSeconds, 0, DurationSeconds);
        if (position > 0)
        {
            try { _audioPlayer.Seek(TimeSpan.FromSeconds(position)); } catch { }
            SetPosition(position);
        }

        _library.SelectedTrack = track;
    }

    /// <summary>
    /// Persists the last displayed track, position, and shuffle for the next
    /// start. No-ops when nothing is loaded so a close during startup cannot
    /// wipe the previous session. Blocking so it completes during window close.
    /// </summary>
    public void SaveSessionState()
    {
        var track = _loadedTrack ?? NowPlayingTrack;
        if (_repository is null || track is null)
        {
            return;
        }

        var trackId = track.Id;
        var position = PositionSeconds;
        var shuffle = IsShuffle;

        try
        {
            // Task.Run avoids a UI-thread deadlock: the repository awaits without
            // ConfigureAwait(false) and window close blocks the UI thread.
            Task.Run(() => _repository.SavePlaybackStateAsync(trackId, position, shuffle))
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
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
        _library.SearchCleared -= OnSearchCleared;

        if (NowPlayingTrack is not null)
        {
            NowPlayingTrack.PropertyChanged -= OnNowPlayingTrackPropertyChanged;
        }

        _audioPlayer.Dispose();
    }

    [RelayCommand]
    private void PlayTrack(Track? track) => PlayTrackCore(track, true);

    private void PlayTrackCore(Track? track, bool captureQueue)
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
                if (!TryLoadTrack(track, captureQueue, autoPlay: true))
                {
                    return;
                }
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

    /// <summary>
    /// Loads <paramref name="track"/> into the audio player, capturing the queue
    /// when requested. Returns false (after clearing state) on failure.
    /// </summary>
    private bool TryLoadTrack(Track track, bool captureQueue, bool autoPlay)
    {
        try
        {
            _audioPlayer.Stop();
            _library.ClearAllPlayingState();
            if (_loadedTrack is not null)
            {
                _library.SetTrackPlayingState(_loadedTrack.Id, false);
            }

            var duration = _audioPlayer.Load(track.FilePath!);
            var previousId = NowPlayingTrack?.Id;

            _loadedTrack = track;
            if (captureQueue)
            {
                _unshuffledQueue = CurrentQueue.ToList();
                _loadedQueue = IsShuffle ? ShuffleQueue(_unshuffledQueue, track) : _unshuffledQueue;
                _loadedQueueLabel = CurrentQueueLabel;
                _loadedQueueSource = CurrentQueueSource;
                _loadedQueueTab = _library.CurrentTab;
            }
            NowPlayingTrack = track;
            if (previousId != track.Id)
            {
                ClearLyrics();
                if (IsLyricsView && _lyricsService is not null)
                {
                    _ = OpenLyricsAsync();
                }
            }
            StopCommand.NotifyCanExecuteChanged();
            // Reset the slider before the duration changes: shrinking the
            // slider Maximum while its Value sits at the old track's end
            // makes the slider write that end position back as a seek, so
            // the new track instantly "finishes" and skips a song.
            SetPosition(0);
            DurationSeconds = duration.TotalSeconds;
            if (autoPlay)
            {
                _audioPlayer.Play();
            }
            return true;
        }
        catch
        {
            ClearPlaybackState();
            return false;
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
                if (!TryLoadTrack(track, captureQueue: true, autoPlay: false))
                {
                    return;
                }
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

        ResetPlaybackState(unloadTrack: false, clearDisplay: false);
    }

    private bool CanStop() => _loadedTrack is not null;

    /// <summary>
    /// The track list the Next/Previous buttons navigate. Picks the list the user
    /// is currently viewing (All Songs, album detail, or artist songs) so
    /// navigation stays within the filtered context.
    /// </summary>
    private System.Collections.ObjectModel.ObservableCollection<Track> CurrentQueue =>
        _library.CurrentTab == NavigationTab.Favorites ? _library.FavoriteTracks :
        _library.IsAlbumDetailView ? _library.AlbumTracks :
        _library.IsArtistDetailView ? _library.ArtistSongs :
        _library.IsGenreSongsView ? _library.GenreSongs :
        _library.Tracks; // AllSongs (default, incl. fallback for list views)

    private string CurrentQueueLabel =>
        _library.CurrentTab == NavigationTab.Favorites ? "Favorites" :
        _library.IsAlbumDetailView ? "Album" :
        _library.IsArtistDetailView ? (_library.IsAlbumArtistsTab ? "Album Artist Songs" : "Artist Songs") :
        _library.IsGenreSongsView ? "Genre" :
        "All Songs";

    private string CurrentQueueSource =>
        _library.CurrentTab == NavigationTab.Favorites ? "Favorites" :
        _library.IsAlbumDetailView ? "Album" :
        _library.IsArtistDetailView ? "Artist" :
        _library.IsGenreSongsView ? "Genre" :
        "AllSongs";

    private string ActiveQueueLabel =>
        _loadedTrack is not null && _loadedQueue is { Count: > 0 }
            ? _loadedQueueLabel
            : CurrentQueueLabel;

    /// <summary>
    /// The queue Prev/Next actually navigate. While a track is loaded this is the
    /// list it was started from, so switching views does not hijack navigation.
    /// With nothing loaded, fall back to the view the user is currently browsing.
    /// </summary>
    private List<Track> ActiveQueue =>
        _loadedTrack is not null && _loadedQueue is { Count: > 0 } &&
        _loadedQueue.Any(t => t.Id == _loadedTrack.Id)
            ? _loadedQueue
            : CurrentQueue.ToList();

    /// <summary>
    /// Shuffles a copy of <paramref name="source"/> and moves the current track
    /// first, so Next visits every remaining track once and Previous restarts at
    /// the top (no wrap-around).
    /// </summary>
    private static List<Track> ShuffleQueue(IEnumerable<Track> source, Track? current)
    {
        var queue = source.ToList();
        for (var i = queue.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }

        if (current is not null)
        {
            var idx = queue.FindIndex(t => t.Id == current.Id);
            if (idx > 0)
            {
                var track = queue[idx];
                queue.RemoveAt(idx);
                queue.Insert(0, track);
            }
        }

        return queue;
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
        ShowPlaybackToast(
            IsShuffle ? "Shuffle on" : "Shuffle off",
            IsShuffle ? "Tracks play in random order." : "Tracks play in their original order.");

        // With nothing loaded the flag applies when the next queue is captured.
        if (_loadedTrack is null)
        {
            return;
        }

        if (IsShuffle)
        {
            _loadedQueue = ShuffleQueue(_unshuffledQueue ?? ActiveQueue, _loadedTrack);
        }
        else if (_unshuffledQueue is not null)
        {
            _loadedQueue = _unshuffledQueue;
        }
    }

    [RelayCommand]
    private async Task OpenLyricsAsync()
    {
        if (_lyricsService is null || NowPlayingTrack is null)
        {
            return;
        }

        IsLyricsView = true;
        LyricsStatusText = "Loading lyrics…";
        try
        {
            ApplyLyrics(await _lyricsService.LoadAsync(NowPlayingTrack));
        }
        catch
        {
            ApplyLyrics(LyricsResult.Empty);
        }
    }

    [RelayCommand]
    private void CloseLyrics() => IsLyricsView = false;

    private void ApplyLyrics(LyricsResult result)
    {
        ClearLyrics();
        foreach (var line in result.Lines)
        {
            Lyrics.Add(line);
        }
        OnPropertyChanged(nameof(HasLyrics));
        LyricsStatusText = result.Lines.Count == 0
            ? "No lyrics for this track. Add a .lrc file next to the audio file."
            : result.IsSynced
                ? (result.Source == "embedded" ? "Embedded synced lyrics" : "Sidecar .lrc")
                : "Unsynced lyrics";
        UpdateActiveLyric(PositionSeconds);
    }

    private void ClearLyrics()
    {
        foreach (var line in Lyrics)
        {
            line.IsActive = false;
        }
        Lyrics.Clear();
        _activeLyricIndex = -1;
        ActiveLyricLine = null;
        OnPropertyChanged(nameof(HasLyrics));
    }

    private void UpdateActiveLyric(double seconds)
    {
        if (Lyrics.Count == 0)
        {
            return;
        }

        var lastIndex = Lyrics.Count - 1;
        var firstTime = Lyrics[0].Time.TotalSeconds;
        var lastTime = Lyrics[lastIndex].Time.TotalSeconds;
        var lineTurn = lastIndex > 0 ? (lastTime - firstTime) / lastIndex : 0;

        var index = -1;
        if (lineTurn > 0 && seconds > lastTime)
        {
            // Past the last timestamp, the next line is the first again.
            var steps = (int)((seconds - lastTime) / lineTurn) + 1;
            index = (lastIndex + steps) % Lyrics.Count;
        }
        else
        {
            for (var i = 0; i < Lyrics.Count; i++)
            {
                if (Lyrics[i].Time.TotalSeconds > seconds + 0.05)
                {
                    break;
                }
                index = i;
            }
        }

        if (index == _activeLyricIndex)
        {
            return;
        }

        if (_activeLyricIndex >= 0 && _activeLyricIndex < Lyrics.Count)
        {
            Lyrics[_activeLyricIndex].IsActive = false;
        }
        _activeLyricIndex = index;
        if (index >= 0)
        {
            Lyrics[index].IsActive = true;
            ActiveLyricLine = Lyrics[index];
        }
        else
        {
            ActiveLyricLine = null;
        }
    }

    [RelayCommand]
    private void Next()
    {
        var queue = ActiveQueue;
        if (queue.Count == 0)
        {
            ShowPlaybackToast("No tracks", "Add files or a folder to start playback.");
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
            ShowPlaybackToast("Last track", $"No next track in {ActiveQueueLabel}.");
            return; // no next track (no wrap at the boundary)
        }

        PlayTrackCore(queue[currentIdx + 1], false);
    }

    [RelayCommand]
    private void Previous()
    {
        var queue = ActiveQueue;
        if (queue.Count == 0)
        {
            ShowPlaybackToast("No tracks", "Add files or a folder to start playback.");
            return;
        }
        if (_loadedTrack is null)
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
        if (idx < 0)
        {
            ShowPlaybackToast("No previous track", $"This track is not in the playing {ActiveQueueLabel} queue.");
            return;
        }

        if (idx > 0)
        {
            PlayTrackCore(queue[idx - 1], false);
            return;
        }

        ShowPlaybackToast("First track", $"Already at the first track of {ActiveQueueLabel}.");

        // At the first track: restart the current track.
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

        UpdateActiveLyric(value);
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
            case nameof(Track.IsFavorite):
                OnPropertyChanged(nameof(IsNowPlayingFavorite));
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

        // Re-sort the captured queue when the sort changes in the same view the
        // track was played from, so Next/Prev follow the new order. Do not touch
        // the captured queue for other views (avoids hijacking playback).
        if (e.PropertyName == nameof(LibraryViewModel.SortField) ||
            e.PropertyName == nameof(LibraryViewModel.IsSortDescending))
        {
            if (_loadedTrack is not null && _loadedQueue is not null &&
                _loadedQueueSource == CurrentQueueSource)
            {
                // Only rebuild if the loaded track is still present in the view.
                var stillPresent = false;
                foreach (var t in CurrentQueue)
                {
                    if (t.Id == _loadedTrack.Id)
                    {
                        stillPresent = true;
                        break;
                    }
                }
                if (stillPresent)
                {
                    _unshuffledQueue = CurrentQueue.ToList();
                    // Sorting must not reshuffle the active order; it only
                    // changes what shuffle-off restores.
                    if (!IsShuffle)
                    {
                        _loadedQueue = _unshuffledQueue;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Replaces the queue captured from the filtered search results with the
    /// restored, unfiltered collection of the same view. Pure no-op (no player,
    /// track, or queue change) when the view switched or the track is absent.
    /// </summary>
    private void OnSearchCleared()
    {
        if (_loadedTrack is null) return;
        if (_library.CurrentTab != _loadedQueueTab) return;

        var restored = CurrentQueue.ToList();
        // The track is outside this view now. Keep the current queue and player
        // untouched: never interrupt playback or silently replace the track.
        if (!restored.Any(t => t.Id == _loadedTrack.Id)) return;

        _unshuffledQueue = restored;
        _loadedQueue = IsShuffle ? ShuffleQueue(restored, _loadedTrack) : restored;
        _loadedQueueLabel = CurrentQueueLabel;
    }

    private void OnTrackRemoved(Track track)
    {
        if (_loadedQueue is not null)
        {
            _loadedQueue.RemoveAll(t => t.Id == track.Id);
            if (_loadedQueue.Count == 0)
            {
                _loadedQueue = null;
                _loadedQueueLabel = "All Songs";
                _loadedQueueSource = "AllSongs";
            }
        }

        if (_unshuffledQueue is not null)
        {
            _unshuffledQueue.RemoveAll(t => t.Id == track.Id);
            if (_unshuffledQueue.Count == 0)
            {
                _unshuffledQueue = null;
            }
        }

        // Match by Id, not reference: collection rebuilds can replace the
        // instance while the logical track (and its Id) stays the same.
        if (_loadedTrack is null || _loadedTrack.Id != track.Id)
        {
            return;
        }

        ResetPlaybackState(unloadTrack: true, clearDisplay: true);
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

    private void ClearPlaybackState() =>
        ResetPlaybackState(unloadTrack: true, clearDisplay: true);

    /// <summary>
    /// Single teardown path for "stop + clear playing state". <paramref name="unloadTrack"/>
    /// drops the loaded track/queue (so the next play goes through the full load
    /// branch); otherwise the loaded track is kept (Stop). <paramref name="clearDisplay"/>
    /// clears NowPlaying/Duration (full reset); otherwise they are kept (completed
    /// display after a track ends).
    /// </summary>
    private void ResetPlaybackState(bool unloadTrack, bool clearDisplay)
    {
        try { _audioPlayer.Stop(); } catch { }

        _library.ClearAllPlayingState();
        if (_loadedTrack is not null)
        {
            _library.SetTrackPlayingState(_loadedTrack.Id, false);
        }

        IsPlaying = false;

        if (unloadTrack)
        {
            ClearLyrics();
            _loadedTrack = null;
            _loadedQueue = null;
            _unshuffledQueue = null;
            _loadedQueueLabel = "All Songs";
            _loadedQueueSource = "AllSongs";
            _loadedQueueTab = _library.CurrentTab;
        }

        if (clearDisplay)
        {
            NowPlayingTrack = null;
            DurationSeconds = 0;
        }

        // Reset slider last; _suppressSeek prevents the slider from seeking.
        SetPosition(0);

        StopCommand.NotifyCanExecuteChanged();
    }

    private void ShowPlaybackToast(string title, string message)
    {
        _library.ToastTitle = title;
        _library.ToastSubtitle = "";
        _library.ToastDetail = "";
        _library.ToastMessage = message;
        _library.ToastCover = null;
        // Bounce the flag so a toast already on screen restarts the 6 s timer.
        _library.ShowToast = false;
        _library.ShowToast = true;
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");
}
