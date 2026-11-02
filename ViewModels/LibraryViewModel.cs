using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TuneVault.Models;
using TuneVault.Services;

namespace TuneVault.ViewModels;

/// <summary>
/// Owns the in-memory track collection, selection, imports, and duplicate handling.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject, IDisposable
{
    private readonly IAudioMetadataReader _metadataReader;
    private readonly IFilePickerService _filePicker;
    private readonly ILibraryRepository _repository;
    private Track? _pendingRemoval;
    private bool _libraryLoaded;
    private readonly SynchronizationContext? _uiContext;
    private Timer? _searchDebounce;

    private readonly List<Track> _allTracks = new();

    public LibraryViewModel(
        IAudioMetadataReader metadataReader,
        IFilePickerService filePicker,
        ILibraryRepository repository)
    {
        _metadataReader = metadataReader;
        _filePicker = filePicker;
        _repository = repository;
        _uiContext = SynchronizationContext.Current;

        Tracks = new ObservableCollection<Track>();
        Tracks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTracks));
            OnPropertyChanged(nameof(ShowEmptyLibrary));
            OnPropertyChanged(nameof(ShowNoResults));
        };

        Albums = new ObservableCollection<AlbumSummary>();
        Albums.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAlbums));
            OnPropertyChanged(nameof(ShowAlbumsEmpty));
        };

        AlbumTracks = new ObservableCollection<Track>();
        AlbumTracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAlbumTracks));
    }

    public ObservableCollection<Track> Tracks { get; }

    public bool HasTracks => Tracks.Count > 0;

    public ObservableCollection<AlbumSummary> Albums { get; }

    public bool HasAlbums => Albums.Count > 0;

    public ObservableCollection<Track> AlbumTracks { get; }

    public bool HasAlbumTracks => AlbumTracks.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlbumDetailView))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumsEmpty))]
    private AlbumSummary? _selectedAlbum;

    partial void OnSelectedAlbumChanged(AlbumSummary? value)
    {
        if (value is null)
        {
            AlbumTracks.Clear();
        }
        else
        {
            RefreshAlbumTracksView();
        }
    }

    public bool IsAlbumDetailView => SelectedAlbum is not null;

    public bool ShowAlbumsEmpty => !IsAlbumDetailView && !HasAlbums;

    public IReadOnlyList<TrackSortField> SortFields { get; } = Enum.GetValues<TrackSortField>();

    [ObservableProperty]
    private TrackSortField _sortField = TrackSortField.Title;

    [ObservableProperty]
    private bool _isSortDescending;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyLibrary))]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private bool _isSearchActive;

    public bool ShowEmptyLibrary => !HasTracks && !IsSearchActive;

    public bool ShowNoResults => IsSearchActive && !HasTracks;

    public string SortDirectionLabel => IsSortDescending ? "DESC ↓" : "ASC ↑";

    [ObservableProperty]
    private Track? _selectedTrack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingStatusText = "";

    [ObservableProperty]
    private double _loadingProgress;

    [ObservableProperty]
    private bool _showDuplicateModal;

    [ObservableProperty]
    private string _duplicateModalTitle = "Audio Already Exists";

    [ObservableProperty]
    private string _duplicateModalMessage = "";

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private string _deleteConfirmationMessage = "";

    /// <summary>Raised after a track has been removed from the collection.</summary>
    public event Action<Track>? TrackRemoved;

    /// <summary>Raised after a search reload completes, so the shell can switch to the results view.</summary>
    public event Action? SearchCompleted;

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
        catch
        {
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
                IsLoading = false;
                return;
            }

            await ImportPathsAsync(files);
        }
        catch
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads the persisted library once. Missing files are flagged, never removed,
    /// so an unmounted drive cannot wipe the database.
    /// </summary>
    public async Task LoadLibraryAsync()
    {
        if (_libraryLoaded)
        {
            return;
        }

        _libraryLoaded = true;

        try
        {
            IsLoading = true;
            LoadingProgress = 0;
            LoadingStatusText = "Loading your library...";

            var tracks = await _repository.LoadAllAsync();
            var missing = 0;

            foreach (var track in tracks)
            {
                if (track.FilePath is null || !File.Exists(track.FilePath))
                {
                    track.IsMissing = true;
                    missing++;
                }

                _allTracks.Add(track);
            }

            RefreshTracksView();
            RefreshAlbumsView();

            if (SelectedTrack is null && Tracks.Count > 0)
            {
                SelectedTrack = Tracks[0];
            }

        }
        catch
        {
        }
        finally
        {
            IsLoading = false;
            LoadingStatusText = "";
            LoadingProgress = 0;
        }
    }

    /// <summary>
    /// Re-queries the database in the selected order. Existing Track instances are
    /// reused by Id so PlaybackViewModel's loaded-track reference stays valid.
    /// </summary>
    public Task ReloadSortedAsync()
    {
        if (!_libraryLoaded || IsLoading)
        {
            return Task.CompletedTask;
        }

        var selectedId = SelectedTrack?.Id;

        RefreshTracksView();
        if (SelectedAlbum is not null)
        {
            RefreshAlbumTracksView();
        }
        RefreshAlbumsView();

        SelectedTrack = Tracks.FirstOrDefault(t => t.Id == selectedId) ?? Tracks.FirstOrDefault();
        return Task.CompletedTask;
    }

    private static string NormalizeAlbum(string? album) =>
        string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album.Trim();

    private static bool MatchesSearch(Track t, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;
        var q = term.Trim();
        return (t.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (t.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (t.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (t.Genre?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private IOrderedEnumerable<Track> SortTracks(IEnumerable<Track> source) =>
        IsSortDescending
            ? source.OrderByDescending(t => SortKey(t)).ThenByDescending(t => t.Title)
            : source.OrderBy(t => SortKey(t)).ThenBy(t => t.Title);

    private object? SortKey(Track t) => SortField switch
    {
        TrackSortField.Artist => t.Artist,
        TrackSortField.Album => t.Album,
        TrackSortField.Genre => t.Genre,
        TrackSortField.Year => t.Year,
        _ => t.Title,
    };

    private void RefreshTracksView()
    {
        var term = SearchText;
        var filtered = _allTracks.Where(t => MatchesSearch(t, term));
        var ordered = SortTracks(filtered).ToList();

        var existing = Tracks.ToDictionary(t => t.Id);
        Tracks.Clear();
        foreach (var track in ordered)
        {
            Tracks.Add(existing.TryGetValue(track.Id, out var kept) ? kept : track);
        }
    }

    private void RefreshAlbumsView()
    {
        var term = SearchText;
        var groups = _allTracks
            .GroupBy(t => NormalizeAlbum(t.Album))
            .Select(g => new AlbumSummary(
                g.Key,
                g.Count(),
                g.OrderBy(t => t.Title).FirstOrDefault()?.PictureData))
            .Where(a => string.IsNullOrWhiteSpace(term)
                        || a.Name.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Albums.Clear();
        foreach (var a in groups)
        {
            Albums.Add(a);
        }
    }

    private void RefreshAlbumTracksView()
    {
        if (SelectedAlbum is null) return;
        var albumName = SelectedAlbum.Name;
        var term = SearchText;
        var filtered = _allTracks
            .Where(t => NormalizeAlbum(t.Album) == albumName)
            .Where(t => MatchesSearch(t, term));
        var ordered = SortTracks(filtered).ToList();

        var existing = AlbumTracks.ToDictionary(t => t.Id);
        AlbumTracks.Clear();
        foreach (var track in ordered)
        {
            AlbumTracks.Add(existing.TryGetValue(track.Id, out var kept) ? kept : track);
        }
    }

    partial void OnSortFieldChanged(TrackSortField value)
    {
        _ = ReloadSortedAsync();
    }

    partial void OnIsSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SortDirectionLabel));
        _ = ReloadSortedAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        IsSearchActive = !string.IsNullOrWhiteSpace(value);

        // Debounce: wait ~350ms of typing silence before hitting the database.
        _searchDebounce?.Dispose();
        _searchDebounce = new Timer(_ =>
        {
            _uiContext?.Post(async __ =>
            {
                await ReloadSortedAsync();
                if (IsSearchActive)
                {
                    SearchCompleted?.Invoke();
                }
            }, null);
        }, null, 350, Timeout.Infinite);
    }

    [RelayCommand]
    private void ToggleSortDirection() => IsSortDescending = !IsSortDescending;

    [RelayCommand]
    private void SelectAlbum(AlbumSummary album)
    {
        SelectedAlbum = album;
        RefreshAlbumTracksView();
    }

    [RelayCommand]
    private void BackToAlbums()
    {
        SelectedAlbum = null;
        AlbumTracks.Clear();
    }

    public void Dispose()
    {
        _searchDebounce?.Dispose();
        _searchDebounce = null;
    }

    /// <summary>Writes an edited track back to the database.</summary>
    public async Task<bool> PersistTrackAsync(Track track)
    {
        if (track.FilePath is null)
        {
            return false;
        }

        await _repository.UpdateAsync(track);
        return true;
    }

    /// <summary>True when another stored track already uses this title/artist.</summary>
    public Task<bool> IsIdentityTakenAsync(string? title, string? artist, Guid excludeTrackId) =>
        _repository.IdentityExistsAsync(title, artist, excludeTrackId);

    private async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        IsLoading = true;
        LoadingProgress = 0;
        LoadingStatusText = $"Preparing {paths.Count} file(s)...";

        IReadOnlyDictionary<string, string> pathIndex;
        try
        {
            pathIndex = await _repository.LoadPathIndexAsync();
        }
        catch
        {
            IsLoading = false;
            return;
        }

        var duplicates = new List<string>();
        var failed = 0;

        // Batch duplicate: the same path twice in one import, or already stored.
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                failed++;
                continue;
            }

            var pathKey = LibraryKeys.ForPath(fullPath);
            if (pending.ContainsKey(pathKey))
            {
                continue;
            }

            if (pathIndex.TryGetValue(pathKey, out var existingTitle))
            {
                duplicates.Add(existingTitle);
                continue;
            }

            pending[pathKey] = fullPath;
        }

        var tracks = new List<Track>(pending.Count);
        var total = pending.Count;
        var index = 0;

        foreach (var fullPath in pending.Values)
        {
            index++;
            LoadingStatusText = $"Importing ({index}/{total}): {Path.GetFileName(fullPath)}";
            LoadingProgress = ((double)index / total) * 100.0;

            try
            {
                var metadata = await Task.Run(() => _metadataReader.Read(fullPath));
                tracks.Add(new Track
                {
                    FilePath = fullPath,
                    Title = metadata.Title ?? TitleFromPath(fullPath),
                    Artist = metadata.Artist ?? "Unknown artist",
                    Album = metadata.Album,
                    Genre = metadata.Genre,
                    Year = metadata.Year,
                    Duration = metadata.Duration,
                    PictureData = metadata.PictureData,
                });
            }
            catch
            {
                failed++;
            }
        }

        var added = 0;

        if (tracks.Count > 0)
        {
            LoadingStatusText = $"Saving {tracks.Count} track(s) to your library...";

            try
            {
                var results = await _repository.AddRangeAsync(tracks);
                foreach (var result in results)
                {
                    switch (result.Outcome)
                    {
                        case AddOutcome.Added:
                            Tracks.Add(result.Track);
                            added++;
                            break;
                        case AddOutcome.DuplicatePath:
                        case AddOutcome.DuplicateIdentity:
                            duplicates.Add($"{result.Track.Title} — {result.Track.Artist}");
                            break;
                        default:
                            failed++;
                            break;
                    }
                }
            }
            catch
            {
                IsLoading = false;
                LoadingStatusText = "";
                return;
            }
        }

        if (SelectedTrack is null && Tracks.Count > 0)
        {
            SelectedTrack = Tracks[0];
        }

        IsLoading = false;
        LoadingStatusText = "";
        LoadingProgress = 0;

        // Keep the visible order consistent with the selected sort after an import.
        if (added > 0)
        {
            await ReloadSortedAsync();
        }

        if (duplicates.Count > 0)
        {
            var dupList = duplicates.Distinct().ToList();
            DuplicateModalTitle = dupList.Count == 1 ? "Audio Already Exists" : "Duplicate Audio Detected";
            if (dupList.Count == 1)
            {
                DuplicateModalMessage = $"\"{dupList[0]}\" is already in your library.";
            }
            else
            {
                var preview = string.Join("\n", dupList.Take(8).Select(d => $"• {d}"));
                var extra = dupList.Count > 8 ? $"\n... and {dupList.Count - 8} more" : "";
                DuplicateModalMessage = $"{dupList.Count} track(s) already exist in your library and were skipped:\n\n{preview}{extra}";
            }
            ShowDuplicateModal = true;
        }
    }

    [RelayCommand]
    private void CloseDuplicateModal()
    {
        ShowDuplicateModal = false;
    }

    [RelayCommand]
    private void RequestRemoveTrack(Track? track)
    {
        track ??= SelectedTrack;
        if (track is null)
        {
            return;
        }

        _pendingRemoval = track;
        DeleteConfirmationMessage =
            $"Are you sure you want to remove \"{track.Title}\" from your library?\n\n" +
            "The audio file will stay on disk.";
        ShowDeleteConfirmation = true;
    }

    [RelayCommand]
    private void CancelRemoveTrack()
    {
        ShowDeleteConfirmation = false;
        _pendingRemoval = null;
    }

    [RelayCommand]
    private async Task ConfirmRemoveTrackAsync()
    {
        var track = _pendingRemoval ?? SelectedTrack;
        ShowDeleteConfirmation = false;
        _pendingRemoval = null;

        if (track is null)
        {
            return;
        }

        // Delete from the database first: a failed delete must not leave the UI
        // showing a track that will reappear on the next launch.
        try
        {
            await _repository.DeleteAsync(track.Id);
        }
        catch
        {
            return;
        }

        Remove(track);
    }

    /// <summary>Removes a track from the library and raises <see cref="TrackRemoved"/>.</summary>
    public void Remove(Track track)
    {
        var index = Tracks.IndexOf(track);
        var wasSelected = ReferenceEquals(SelectedTrack, track);

        if (index < 0)
        {
            return;
        }

        Tracks.Remove(track);

        if (wasSelected || SelectedTrack is null)
        {
            SelectedTrack = Tracks.Count == 0
                ? null
                : Tracks[Math.Min(index, Tracks.Count - 1)];
        }

        TrackRemoved?.Invoke(track);
    }

    private static string TitleFromPath(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(title) ? "Untitled track" : title;
    }
}
