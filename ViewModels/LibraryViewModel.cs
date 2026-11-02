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
/// Owns the in-memory track collection, selection, imports, and duplicate handling.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IAudioMetadataReader _metadataReader;
    private readonly IFilePickerService _filePicker;
    private readonly ILibraryRepository _repository;
    private readonly StatusViewModel _status;
    private Track? _pendingRemoval;
    private bool _libraryLoaded;
    private int _sortVersion;

    public LibraryViewModel(
        IAudioMetadataReader metadataReader,
        IFilePickerService filePicker,
        ILibraryRepository repository,
        StatusViewModel status)
    {
        _metadataReader = metadataReader;
        _filePicker = filePicker;
        _repository = repository;
        _status = status;

        Tracks = new ObservableCollection<Track>();
        Tracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTracks));
    }

    public ObservableCollection<Track> Tracks { get; }

    public bool HasTracks => Tracks.Count > 0;

    public IReadOnlyList<TrackSortField> SortFields { get; } = Enum.GetValues<TrackSortField>();

    [ObservableProperty]
    private TrackSortField _sortField = TrackSortField.Title;

    [ObservableProperty]
    private bool _isSortDescending;

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
            _status.Message = $"Could not open the file picker: {ex.Message}";
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
                _status.Message = "No supported audio files found in selected folder.";
                IsLoading = false;
                return;
            }

            await ImportPathsAsync(files);
        }
        catch (Exception ex)
        {
            _status.Message = $"Could not open folder picker: {ex.Message}";
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

            var tracks = await _repository.LoadAllAsync(SortField, IsSortDescending);
            var missing = 0;

            foreach (var track in tracks)
            {
                if (track.FilePath is null || !File.Exists(track.FilePath))
                {
                    track.IsMissing = true;
                    missing++;
                }

                Tracks.Add(track);
            }

            if (SelectedTrack is null && Tracks.Count > 0)
            {
                SelectedTrack = Tracks[0];
            }

            _status.Message = tracks.Count == 0
                ? "Add local audio files to start your library."
                : missing == 0
                    ? $"Loaded {tracks.Count} track(s)."
                    : $"Loaded {tracks.Count} track(s); {missing} file(s) not found on disk.";
        }
        catch (Exception ex)
        {
            _status.Message = $"Could not load the library database: {ex.Message}";
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
    public async Task ReloadSortedAsync()
    {
        if (!_libraryLoaded || IsLoading)
        {
            return;
        }

        var version = ++_sortVersion;
        var selectedId = SelectedTrack?.Id;

        try
        {
            var ordered = await _repository.LoadAllAsync(SortField, IsSortDescending);
            if (version != _sortVersion)
            {
                return;
            }

            var existing = Tracks.ToDictionary(t => t.Id);
            Tracks.Clear();

            foreach (var track in ordered)
            {
                if (existing.TryGetValue(track.Id, out var kept))
                {
                    Tracks.Add(kept);
                    continue;
                }

                if (track.FilePath is null || !File.Exists(track.FilePath))
                {
                    track.IsMissing = true;
                }

                Tracks.Add(track);
            }

            SelectedTrack = Tracks.FirstOrDefault(t => t.Id == selectedId) ?? Tracks.FirstOrDefault();
            _status.Message = $"Sorted by {SortField} ({(IsSortDescending ? "descending" : "ascending")}).";
        }
        catch (Exception ex)
        {
            _status.Message = $"Could not sort the library: {ex.Message}";
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

    [RelayCommand]
    private void ToggleSortDirection() => IsSortDescending = !IsSortDescending;

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
        catch (Exception ex)
        {
            _status.Message = $"Could not read the library database: {ex.Message}";
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
            catch (Exception ex)
            {
                _status.Message = $"Could not save to the library database: {ex.Message}";
                IsLoading = false;
                LoadingStatusText = "";
                return;
            }
        }

        if (SelectedTrack is null && Tracks.Count > 0)
        {
            SelectedTrack = Tracks[0];
        }

        _status.Message = (added, failed) switch
        {
            (0, 0) => "Those files are already in your library.",
            (0, _) => $"Could not read {failed} file(s).",
            (_, 0) => $"Added {added} track(s).",
            _ => $"Added {added} track(s); {failed} could not be read.",
        };

        IsLoading = false;
        LoadingStatusText = "";
        LoadingProgress = 0;

        // Keep the visible order consistent with the selected sort after an import.
        if (added > 0)
        {
            var message = _status.Message;
            await ReloadSortedAsync();
            _status.Message = message;
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
        catch (Exception ex)
        {
            _status.Message = $"Could not remove \"{track.Title}\" from the library: {ex.Message}";
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
        _status.Message = $"Removed \"{track.Title}\".";
    }

    private static string TitleFromPath(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(title) ? "Untitled track" : title;
    }
}
