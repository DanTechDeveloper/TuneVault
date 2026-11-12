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
    private bool _isRefreshingSummaries;

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
            OnPropertyChanged(nameof(ShowAlbumsNoResults));
        };

        AlbumTracks = new ObservableCollection<Track>();
        AlbumTracks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAlbumTracks));
            OnPropertyChanged(nameof(ShowAlbumTracksEmpty));
            OnPropertyChanged(nameof(ShowAlbumTracksNoResults));
        };

        Artists = new ObservableCollection<ArtistSummary>();
        Artists.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasArtists));
            OnPropertyChanged(nameof(ShowArtistsEmpty));
            OnPropertyChanged(nameof(ShowArtistsNoResults));
        };

        ArtistAlbums = new ObservableCollection<AlbumSummary>();
        ArtistAlbums.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasArtistAlbums));
            OnPropertyChanged(nameof(ShowArtistAlbumsEmpty));
            OnPropertyChanged(nameof(ShowArtistAlbumsNoResults));
        };

        ArtistSongs = new ObservableCollection<Track>();
        ArtistSongs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasArtistSongs));
            OnPropertyChanged(nameof(ShowArtistSongsEmpty));
            OnPropertyChanged(nameof(ShowArtistSongsNoResults));
        };

        Genres = new ObservableCollection<GenreSummary>();
        Genres.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGenres));
            OnPropertyChanged(nameof(ShowGenresEmpty));
            OnPropertyChanged(nameof(ShowGenresNoResults));
        };

        GenreSongs = new ObservableCollection<Track>();
        GenreSongs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGenreSongs));
            OnPropertyChanged(nameof(ShowGenreSongsEmpty));
            OnPropertyChanged(nameof(ShowGenreSongsNoResults));
        };
    }

    public ObservableCollection<Track> Tracks { get; }

    public bool HasTracks => Tracks.Count > 0;

    public ObservableCollection<AlbumSummary> Albums { get; }

    public bool HasAlbums => Albums.Count > 0;

    public ObservableCollection<Track> AlbumTracks { get; }

    public bool HasAlbumTracks => AlbumTracks.Count > 0;
    public bool ShowAlbumTracksEmpty => !IsSearchActive && IsAlbumDetailView && !HasAlbumTracks;
    public bool ShowAlbumTracksNoResults => IsSearchActive && IsAlbumDetailView && !HasAlbumTracks;

    public ObservableCollection<ArtistSummary> Artists { get; }

    public bool HasArtists => Artists.Count > 0;

    public bool ShowArtistsEmpty => !IsSearchActive && !IsArtistDetailView && !HasArtists;
    public bool ShowArtistsNoResults => IsSearchActive && !IsArtistDetailView && !HasArtists;

    public ObservableCollection<AlbumSummary> ArtistAlbums { get; }

    public bool HasArtistAlbums => ArtistAlbums.Count > 0;
    public bool ShowArtistAlbumsEmpty => !IsSearchActive && IsArtistDetailView && !IsArtistSongsView && !HasArtistAlbums;
    public bool ShowArtistAlbumsNoResults => IsSearchActive && IsArtistDetailView && !IsArtistSongsView && !HasArtistAlbums;

    public ObservableCollection<Track> ArtistSongs { get; }

    public bool HasArtistSongs => ArtistSongs.Count > 0;
    public bool ShowArtistSongsEmpty => !IsSearchActive && IsArtistSongsView && !HasArtistSongs;
    public bool ShowArtistSongsNoResults => IsSearchActive && IsArtistSongsView && !HasArtistSongs;

    public ObservableCollection<GenreSummary> Genres { get; }

    public bool HasGenres => Genres.Count > 0;

    public ObservableCollection<Track> GenreSongs { get; }

    public bool HasGenreSongs => GenreSongs.Count > 0;

    public bool IsGenreSongsView => SelectedGenre is not null;

    public bool ShowGenresEmpty => !IsSearchActive && !IsGenreSongsView && !HasGenres;
    public bool ShowGenresNoResults => IsSearchActive && !IsGenreSongsView && !HasGenres;
    public bool ShowGenreSongsEmpty => !IsSearchActive && IsGenreSongsView && !HasGenreSongs;
    public bool ShowGenreSongsNoResults => IsSearchActive && IsGenreSongsView && !HasGenreSongs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGenreSongsView))]
    [NotifyPropertyChangedFor(nameof(ShowGenresEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowGenresNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowGenreSongsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowGenreSongsNoResults))]
    private GenreSummary? _selectedGenre;

    [ObservableProperty]
    private string _genreSongsTitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArtistDetailView))]
    [NotifyPropertyChangedFor(nameof(IsArtistSongsView))]
    [NotifyPropertyChangedFor(nameof(ShowArtistsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowArtistAlbumsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistAlbumsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowArtistDetail))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongs))]
    private ArtistSummary? _selectedArtist;

    partial void OnSelectedArtistChanged(ArtistSummary? value)
    {
        if (_isRefreshingSummaries) return;

        IsArtistsTab = CurrentTab == NavigationTab.Artists;
        IsAlbumArtistsTab = CurrentTab == NavigationTab.AlbumArtists;

        // Reset song-view state so a previously selected artist's title/list
        // never leaks into the newly selected artist (stale data).
        IsArtistAllSongsView = false;
        SelectedArtistAlbum = null;
        if (!IsTabSwitching)
        {
            ArtistAlbums.Clear();
            ArtistSongs.Clear();
        }
        ArtistSongsTitle = string.Empty;

        if (value is not null)
        {
            RefreshArtistAlbums();
            // Populate ArtistSongs with ALL artist songs so the navigation
            // queue is defined even in Layer 2 (album list).
            RefreshArtistSongs();

            // Only the Artists (track-artist) tab lands directly on all-songs.
            if (IsArtistsTab)
            {
                IsArtistAllSongsView = true;
            }
        }

        IsArtistsTab = CurrentTab == NavigationTab.Artists;
        IsAlbumArtistsTab = CurrentTab == NavigationTab.AlbumArtists;
        UpdateSortContext();
    }

    partial void OnSelectedArtistAlbumChanged(AlbumSummary? value)
    {
        // When an album is picked from the list (via SelectedItem binding),
        // refresh the song list even though no command was invoked.
        if (value is not null && SelectedArtist is not null)
        {
            ArtistSongsTitle = value.Name;
            RefreshArtistSongs();
        }

        UpdateSortContext();
    }

    partial void OnIsArtistAllSongsViewChanged(bool value)
    {
        if (value && SelectedArtist is not null)
        {
            ArtistSongsTitle = $"All {SelectedArtist.Name} Songs";
            RefreshArtistSongs();
        }

        UpdateSortContext();
    }

    public bool IsArtistDetailView => SelectedArtist is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArtistSongsView))]
    [NotifyPropertyChangedFor(nameof(ShowArtistDetail))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongs))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsNoResults))]
    private AlbumSummary? _selectedArtistAlbum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArtistSongsView))]
    [NotifyPropertyChangedFor(nameof(ShowArtistDetail))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongs))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsNoResults))]
    private bool _isArtistAllSongsView;

    public bool IsArtistSongsView => IsArtistAllSongsView || SelectedArtistAlbum is not null;

    public bool ShowArtistDetail => IsArtistDetailView && !IsArtistSongsView;

    public bool ShowArtistSongs => IsArtistSongsView;

    [ObservableProperty]
    private string _artistSongsTitle = "";

    /// <summary>Set by the shell while switching tabs so drill-down state can be
    /// reset to Layer 1 without discarding the underlying (queue-referenced)
    /// collections.</summary>
    public bool IsTabSwitching { get; set; }

    /// <summary>True when the active tab is the Artists (track-artist) view.
    /// Controls whether selecting an artist lands directly on all-songs.</summary>
    public bool IsArtistsTab { get; set; }

    /// <summary>True when the active tab is the Album Artists view.</summary>
    public bool IsAlbumArtistsTab { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortFields))]
    [NotifyPropertyChangedFor(nameof(SortContext))]
    private NavigationTab _currentTab = NavigationTab.AllSongs;

    private SortContext _sortContext = SortContext.AllSongsTracks;

    public SortContext SortContext
    {
        get => _sortContext;
        private set
        {
            if (_sortContext == value) return;
            _sortContext = value;
            OnPropertyChanged(nameof(SortContext));
            OnPropertyChanged(nameof(SortFields));
            var options = SortFields;
            if (options.Count > 0 && !options.Contains(SortField))
            {
                SortField = options[0];
            }
        }
    }

    public IReadOnlyList<TrackSortField> SortFields => SortContext switch
    {
        SortContext.AlbumsList => new[] { TrackSortField.Title, TrackSortField.Artist},
        SortContext.ArtistsList => new[] { TrackSortField.Artist },
        SortContext.AlbumArtistsList => new[] { TrackSortField.Artist },
        SortContext.AlbumArtistAlbums => new[] { TrackSortField.Title, TrackSortField.Year },
        SortContext.AlbumArtistTracks => new[] { TrackSortField.Title, TrackSortField.Album, TrackSortField.Genre },        SortContext.AlbumArtistAllTracks => new[] { TrackSortField.Title, TrackSortField.Album, TrackSortField.Genre, TrackSortField.Year },
        SortContext.GenresList => new[] { TrackSortField.Genre },
        SortContext.GenreTracks => new[] { TrackSortField.Title, TrackSortField.Artist, TrackSortField.Album, TrackSortField.Year },
        SortContext.AlbumTracks => new[] { TrackSortField.Title, TrackSortField.Artist, TrackSortField.Genre, TrackSortField.Year },
        SortContext.ArtistTracks => new[] { TrackSortField.Title, TrackSortField.Album, TrackSortField.Genre, TrackSortField.Year },
        _ => new[] { TrackSortField.Title, TrackSortField.Artist, TrackSortField.Album, TrackSortField.Genre, TrackSortField.Year },    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlbumDetailView))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumTracksEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumTracksNoResults))]
    private AlbumSummary? _selectedAlbum;

    partial void OnSelectedAlbumChanged(AlbumSummary? value)
    {
        if (_isRefreshingSummaries) return;

        UpdateSortContext();

        if (value is null)
        {
            if (!IsTabSwitching)
            {
                AlbumTracks.Clear();
            }
        }
        else
        {
            RefreshAlbumTracksView();
        }
    }

    public bool IsAlbumDetailView => SelectedAlbum is not null;

    public bool ShowAlbumsEmpty => !IsSearchActive && !IsAlbumDetailView && !HasAlbums;
    public bool ShowAlbumsNoResults => IsSearchActive && !IsAlbumDetailView && !HasAlbums;

    [ObservableProperty]
    private TrackSortField _sortField = TrackSortField.Title;

    [ObservableProperty]
    private bool _isSortDescending;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyLibrary))]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumTracksEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumTracksNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowArtistsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowArtistAlbumsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistAlbumsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowArtistSongsNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowGenresEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowGenresNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowGenreSongsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowGenreSongsNoResults))]
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

    [ObservableProperty]
    private bool _showToast;

    [ObservableProperty]
    private string _toastTitle = "Successfully added";

    [ObservableProperty]
    private string _toastMessage = "";

    /// <summary>Secondary line for the toast, mirroring TrackRow's artist/album label.</summary>
    [ObservableProperty]
    private string _toastSubtitle = "";

    /// <summary>Tertiary line for the toast, mirroring TrackRow's genre display.</summary>
    [ObservableProperty]
    private string _toastDetail = "";

    /// <summary>Cover image for the toast (file adds only; null for folders).</summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _toastCover;

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
            IsLoading = false;
            System.Diagnostics.Debug.WriteLine($"TuneVault: add files failed: {ex}");
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

            await ImportPathsAsync(files, folder);
        }
        catch (Exception ex)
        {
            IsLoading = false;
            System.Diagnostics.Debug.WriteLine($"TuneVault: add folder failed: {ex}");
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
            RefreshArtistsView();
            RefreshGenresView();

            // No auto-select on initial load: the first row should not show a
            // selection highlight until the user explicitly clicks a track.

        }
        catch (Exception ex)
        {
            _libraryLoaded = false;
            System.Diagnostics.Debug.WriteLine($"TuneVault: library load failed: {ex}");
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
        RefreshArtistsView();
        if (SelectedArtist is not null)
        {
            RefreshArtistSongs();
        }
        RefreshGenresView();
        if (SelectedGenre is not null)
        {
            RefreshGenreSongs();
        }

        SelectedTrack = selectedId is null ? null : Tracks.FirstOrDefault(t => t.Id == selectedId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Propagates a track's playing state to every list instance that represents
    /// the same track (by Id), so the playing row highlights consistently across
    /// views even though each view keeps its own collection instances.
    /// </summary>
    public void SetTrackPlayingState(Guid trackId, bool isPlaying)
    {
        foreach (var t in _allTracks.Where(t => t.Id == trackId)) t.IsPlaying = isPlaying;
        foreach (var t in Tracks.Where(t => t.Id == trackId)) t.IsPlaying = isPlaying;
        foreach (var t in AlbumTracks.Where(t => t.Id == trackId)) t.IsPlaying = isPlaying;
        foreach (var t in ArtistSongs.Where(t => t.Id == trackId)) t.IsPlaying = isPlaying;
        foreach (var t in GenreSongs.Where(t => t.Id == trackId)) t.IsPlaying = isPlaying;
    }

    /// <summary>
    /// Propagates an edited track's fields to every list instance that represents
    /// the same track (by Id), so rows in all views reflect the edit without a
    /// manual reload. Collections may hold distinct instances of the same logical
    /// track, so mutate each one in place.
    /// </summary>
    public void ApplyTrackMetadata(Track source)
    {
        var seen = new HashSet<Track>(ReferenceEqualityComparer.Instance);
        var targets = _allTracks.Where(t => t.Id == source.Id)
            .Concat(Tracks.Where(t => t.Id == source.Id))
            .Concat(AlbumTracks.Where(t => t.Id == source.Id))
            .Concat(ArtistSongs.Where(t => t.Id == source.Id))
            .Concat(GenreSongs.Where(t => t.Id == source.Id));
        foreach (var t in targets)
        {
            if (!seen.Add(t)) continue;
            if (ReferenceEquals(t, source)) continue;
            t.Title = source.Title;
            t.Artist = source.Artist;
            t.AlbumArtist = source.AlbumArtist;
            t.Album = source.Album;
            t.Genre = source.Genre;
            t.Year = source.Year;
            t.PictureData = source.PictureData;
            t.IsMissing = source.IsMissing;
        }

        // Summary tiles (artists/albums/genres) and drill-down headers are derived
        // aggregates, not observable track rows, so rebuild them too. Capture which
        // drill-down the edited track belonged to BEFORE refreshing, so a renamed
        // artist/album/genre is followed instead of collapsing to Layer 1.
        var inSelectedAlbum = SelectedAlbum is not null && AlbumTracks.Any(t => t.Id == source.Id);
        var inSelectedArtist = SelectedArtist is not null && ArtistSongs.Any(t => t.Id == source.Id);
        var inSelectedGenre = SelectedGenre is not null && GenreSongs.Any(t => t.Id == source.Id);

        RefreshTracksView();

        RefreshAlbumsView();
        if (inSelectedAlbum)
        {
            var match = Albums.FirstOrDefault(a => a.Name == NormalizeAlbum(source.Album));
            SelectedAlbum = match;
        }
        if (SelectedAlbum is not null)
        {
            RefreshAlbumTracksView();
        }

        var wasAllSongs = IsArtistAllSongsView;
        var prevArtistAlbum = SelectedArtistAlbum;
        RefreshArtistsView();
        if (inSelectedArtist)
        {
            var followKey = IsArtistsTab ? source.Artist : source.EffectiveAlbumArtist;
            var match = Artists.FirstOrDefault(a => a.Name == NormalizeArtist(followKey));
            SelectedArtist = match;
            if (match is not null)
            {
                if (wasAllSongs)
                {
                    IsArtistAllSongsView = true;
                }
                else if (prevArtistAlbum is not null)
                {
                    var newAlbum = ArtistAlbums.FirstOrDefault(a => a.Name == prevArtistAlbum.Name);
                    if (newAlbum is not null)
                    {
                        SelectedArtistAlbum = newAlbum;
                    }
                    else
                    {
                        RefreshArtistAlbums();
                    }
                }
                else
                {
                    RefreshArtistAlbums();
                }
            }
        }
        else if (SelectedArtist is not null)
        {
            if (IsArtistSongsView)
            {
                RefreshArtistSongs();
            }
            else
            {
                RefreshArtistAlbums();
            }
        }

        RefreshGenresView();
        if (inSelectedGenre)
        {
            var match = Genres.FirstOrDefault(g => g.Name == NormalizeGenre(source.Genre));
            SelectedGenre = match;
        }
        if (SelectedGenre is not null)
        {
            RefreshGenreSongs();
        }
    }

    public void ClearAllPlayingState()
    {
        foreach (var t in _allTracks) t.IsPlaying = false;
        foreach (var t in Tracks) t.IsPlaying = false;
        foreach (var t in AlbumTracks) t.IsPlaying = false;
        foreach (var t in ArtistSongs) t.IsPlaying = false;
        foreach (var t in GenreSongs) t.IsPlaying = false;
    }

    private static string NormalizeAlbum(string? album) =>
        string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album.Trim();

    private static bool MatchesSearch(Track t, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;
        var q = term.Trim();
        var album = string.IsNullOrWhiteSpace(t.Album) ? "Unknown Album" : t.Album;
        var genre = string.IsNullOrWhiteSpace(t.Genre) ? "Unknown Genre" : t.Genre;
        return (t.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (t.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || album.Contains(q, StringComparison.OrdinalIgnoreCase)
            || genre.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSortContext()
    {
        SortContext newContext;
        if (SelectedAlbum is not null)
        {
            newContext = SortContext.AlbumTracks;
        }
        else if (SelectedGenre is not null)
        {
            newContext = SortContext.GenreTracks;
        }
        else if (SelectedArtist is not null)
        {
            if (IsArtistAllSongsView)
            {
                newContext = CurrentTab == NavigationTab.AlbumArtists
                    ? SortContext.AlbumArtistAllTracks
                    : SortContext.ArtistTracks;
            }
            else if (SelectedArtistAlbum is not null)
            {
                newContext = CurrentTab == NavigationTab.AlbumArtists
                    ? SortContext.AlbumArtistTracks
                    : SortContext.ArtistTracks;
            }
            else
            {
                newContext = CurrentTab == NavigationTab.AlbumArtists
                    ? SortContext.AlbumArtistAlbums
                    : SortContext.ArtistTracks;
            }
        }
        else
        {
            newContext = CurrentTab switch
            {
                NavigationTab.Albums => SortContext.AlbumsList,
                NavigationTab.Artists => SortContext.ArtistsList,
                NavigationTab.AlbumArtists => SortContext.AlbumArtistsList,
                NavigationTab.Genres => SortContext.GenresList,
                _ => SortContext.AllSongsTracks,
            };
        }

        SortContext = newContext;
    }

    partial void OnCurrentTabChanged(NavigationTab value) => UpdateSortContext();

    private IOrderedEnumerable<Track> SortTracks(IEnumerable<Track> source)
    {
        // Mirror LoadAllAsync: tracks with a missing album/genre/year sort last
        // in both directions, then the chosen column, then title (always asc).
        Func<Track, int> missing = t => SortField switch
        {
            TrackSortField.Album => string.IsNullOrWhiteSpace(t.Album) ? 1 : 0,
            TrackSortField.Genre => string.IsNullOrWhiteSpace(t.Genre) ? 1 : 0,
            TrackSortField.Year => t.Year == 0 ? 1 : 0,
            _ => 0,
        };
        return IsSortDescending
            ? source.OrderBy(missing).ThenByDescending(t => SortKey(t)).ThenBy(t => t.Title)
            : source.OrderBy(missing).ThenBy(t => SortKey(t)).ThenBy(t => t.Title);
    }

    private object? SortKey(Track t) => SortField switch
    {
        TrackSortField.Artist => t.Artist,
        TrackSortField.Album => t.Album,
        TrackSortField.Genre => t.Genre,
        TrackSortField.Year => t.Year,
        TrackSortField.TotalDuration => t.Duration,
        TrackSortField.TotalTracks => t.Title,
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
            .Select(g =>
            {
                var artists = g.Select(t => t.Artist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var artist = artists.Count == 1 ? artists[0]
                    : artists.Count > 1 ? "Various Artists" : null;
                var year = g.Select(t => t.Year).Where(y => y > 0).DefaultIfEmpty((uint)0).Max();
                return new AlbumSummary(
                    g.Key,
                    g.Count(),
                    g.OrderBy(t => t.Title).FirstOrDefault()?.PictureData,
                    artist,
                    Year: (int)year);
            })
            .Where(a => string.IsNullOrWhiteSpace(term)
                        || a.Name.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)
                        || (a.Artist?.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        IOrderedEnumerable<AlbumSummary> albumOrdered;
        switch (SortField)
        {
            case TrackSortField.Artist:
                albumOrdered = IsSortDescending
                    ? groups.OrderByDescending(a => a.Artist ?? a.Name, StringComparer.OrdinalIgnoreCase)
                    : groups.OrderBy(a => a.Artist ?? a.Name, StringComparer.OrdinalIgnoreCase);
                break;
            case TrackSortField.Year:
                albumOrdered = IsSortDescending
                    ? groups.OrderBy(a => a.Year == 0 ? 1 : 0).ThenByDescending(a => a.Year)
                    : groups.OrderBy(a => a.Year == 0 ? 1 : 0).ThenBy(a => a.Year);
                break;
            default:
                albumOrdered = IsSortDescending
                    ? groups.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    : groups.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
                break;
        }

        var selectedAlbumName = SelectedAlbum?.Name;
        var selectedAlbumArtist = SelectedAlbum?.Artist;

        _isRefreshingSummaries = true;
        try
        {
            Albums.Clear();
            foreach (var a in albumOrdered)
            {
                Albums.Add(a);
            }

            AlbumSummary? match = null;
            if (selectedAlbumName is not null)
            {
                match = Albums.FirstOrDefault(a =>
                    a.Name == selectedAlbumName &&
                    (a.Artist ?? string.Empty) == (selectedAlbumArtist ?? string.Empty));
            }
            if (match is null && SelectedTrack is not null)
            {
                match = Albums.FirstOrDefault(a => a.Name == NormalizeAlbum(SelectedTrack.Album));
            }

            SelectedAlbum = match;

            if (match is not null)
            {
                RefreshAlbumTracksView();
            }
            else if (SelectedAlbum is null && !IsTabSwitching)
            {
                AlbumTracks.Clear();
            }
        }
        finally
        {
            _isRefreshingSummaries = false;
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

    private static string NormalizeArtist(string? artist) =>
        string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();

    public void RefreshArtistsView()
    {
        var term = SearchText;
        var groups = _allTracks
            .GroupBy(t => NormalizeArtist(IsArtistsTab ? t.Artist : t.EffectiveAlbumArtist))
            .Select(g => new ArtistSummary(
                g.Key,
                g.Count(),
                g.OrderBy(t => t.Title).FirstOrDefault()?.PictureData,
                Year: (int)g.Select(t => t.Year).Where(y => y > 0).DefaultIfEmpty((uint)0).Max(),
                AlbumCount: IsArtistsTab ? 0 : g.Select(t => NormalizeAlbum(t.Album)).Distinct().Count()))
            .Where(a => string.IsNullOrWhiteSpace(term)
                        || a.Name.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        IOrderedEnumerable<ArtistSummary> artistOrdered;
        switch (SortField)
        {
            case TrackSortField.Year:
                artistOrdered = IsSortDescending
                    ? groups.OrderBy(a => a.Year == 0 ? 1 : 0).ThenByDescending(a => a.Year)
                    : groups.OrderBy(a => a.Year == 0 ? 1 : 0).ThenBy(a => a.Year);
                break;
            default:
                artistOrdered = IsSortDescending
                    ? groups.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    : groups.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
                break;
        }

        var selectedArtistName = SelectedArtist?.Name;

        _isRefreshingSummaries = true;
        try
        {
            Artists.Clear();
            foreach (var a in artistOrdered)
            {
                Artists.Add(a);
            }

            ArtistSummary? match = null;
            if (selectedArtistName is not null)
            {
                match = Artists.FirstOrDefault(a => a.Name == selectedArtistName);
            }
            if (match is null && SelectedTrack is not null && !IsTabSwitching)
            {
                var trackKey = IsArtistsTab ? SelectedTrack.Artist : SelectedTrack.EffectiveAlbumArtist;
                match = Artists.FirstOrDefault(a => a.Name == NormalizeArtist(trackKey));
            }

            SelectedArtist = match;

            if (match is not null)
            {
                if (IsArtistDetailView && !IsArtistSongsView)
                {
                    RefreshArtistAlbums();
                }
                else if (IsArtistSongsView)
                {
                    RefreshArtistSongs();
                }
            }
            else if (SelectedArtist is null && !IsTabSwitching)
            {
                ArtistAlbums.Clear();
                ArtistSongs.Clear();
                IsArtistAllSongsView = false;
                SelectedArtistAlbum = null;
                ArtistSongsTitle = string.Empty;
            }
        }
        finally
        {
            _isRefreshingSummaries = false;
        }
    }

    private static string NormalizeGenre(string? genre) =>
        string.IsNullOrWhiteSpace(genre) ? "Unknown Genre" : genre.Trim();

    private void RefreshGenresView()
    {
        var term = SearchText;
        var groups = _allTracks
            .GroupBy(t => NormalizeGenre(t.Genre))
            .Select(g => new GenreSummary(
                g.Key,
                g.Count(),
                g.OrderBy(t => t.Title).FirstOrDefault()?.PictureData));
        IOrderedEnumerable<GenreSummary> genreOrdered;
        switch (SortField)
        {
            default:
                genreOrdered = IsSortDescending
                    ? groups.OrderByDescending(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    : groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase);
                break;
        }

        var selectedGenreName = SelectedGenre?.Name;

        _isRefreshingSummaries = true;
        try
        {
            Genres.Clear();
            foreach (var g in genreOrdered)
            {
                Genres.Add(g);
            }

            GenreSummary? match = null;
            if (selectedGenreName is not null)
            {
                match = Genres.FirstOrDefault(g => g.Name == selectedGenreName);
            }
            if (match is null && SelectedTrack is not null)
            {
                match = Genres.FirstOrDefault(g => g.Name == NormalizeGenre(SelectedTrack.Genre));
            }

            SelectedGenre = match;

            if (match is not null)
            {
                RefreshGenreSongs();
            }
            else if (SelectedGenre is null && !IsTabSwitching)
            {
                GenreSongs.Clear();
                GenreSongsTitle = string.Empty;
            }
        }
        finally
        {
            _isRefreshingSummaries = false;
        }
    }

    private void RefreshGenreSongs()
    {
        if (SelectedGenre is null) return;
        var genreName = SelectedGenre.Name;
        var term = SearchText;
        var filtered = _allTracks
            .Where(t => NormalizeGenre(t.Genre) == genreName)
            .Where(t => MatchesSearch(t, term));
        var ordered = SortTracks(filtered).ToList();

        var existing = GenreSongs.ToDictionary(t => t.Id);
        GenreSongs.Clear();
        foreach (var track in ordered)
        {
            GenreSongs.Add(existing.TryGetValue(track.Id, out var kept) ? kept : track);
        }
    }

    private void RefreshArtistAlbums()
    {
        if (SelectedArtist is null) return;
        var artistName = SelectedArtist.Name;
        var groups = _allTracks
            .Where(t => NormalizeArtist(t.EffectiveAlbumArtist) == artistName)
            .GroupBy(t => NormalizeAlbum(t.Album))
            .Select(g => new AlbumSummary(
                g.Key,
                g.Count(),
                g.OrderBy(t => t.Title).FirstOrDefault()?.PictureData,
                artistName,
                Year: (int)g.Select(t => t.Year).Where(y => y > 0).DefaultIfEmpty((uint)0).Max()))
            .ToList();

        IOrderedEnumerable<AlbumSummary> albumOrdered;
        switch (SortField)
        {
            case TrackSortField.Year:
                albumOrdered = IsSortDescending
                    ? groups.OrderBy(a => a.Year == 0 ? 1 : 0).ThenByDescending(a => a.Year)
                    : groups.OrderBy(a => a.Year == 0 ? 1 : 0).ThenBy(a => a.Year);
                break;
            default:
                albumOrdered = IsSortDescending
                    ? groups.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    : groups.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
                break;
        }

        ArtistAlbums.Clear();
        foreach (var a in albumOrdered)
        {
            ArtistAlbums.Add(a);
        }
    }

    private void RefreshArtistSongs()
    {
        if (SelectedArtist is null) return;
        var artistName = SelectedArtist.Name;
        var term = SearchText;
        IEnumerable<Track> source;

        if (SelectedArtistAlbum is not null)
        {
            var albumName = SelectedArtistAlbum.Name;
            source = _allTracks.Where(t =>
                NormalizeArtist(IsArtistsTab ? t.Artist : t.EffectiveAlbumArtist) == artistName
                && NormalizeAlbum(t.Album) == albumName);
        }
        else if (IsArtistAllSongsView)
        {
            source = _allTracks.Where(t => NormalizeArtist(IsArtistsTab ? t.Artist : t.EffectiveAlbumArtist) == artistName);
        }
        else
        {
            return;
        }

        var filtered = source.Where(t => MatchesSearch(t, term));
        var ordered = SortTracks(filtered).ToList();

        var existing = ArtistSongs.ToDictionary(t => t.Id);
        ArtistSongs.Clear();
        foreach (var track in ordered)
        {
            ArtistSongs.Add(existing.TryGetValue(track.Id, out var kept) ? kept : track);
        }
    }

    [RelayCommand]
    private void SelectArtist(ArtistSummary artist)
    {
        // State reset + album loading handled by OnSelectedArtistChanged.
        SelectedArtist = artist;
    }

    [RelayCommand]
    private void ShowAllArtistSongs()
    {
        if (SelectedArtist is null) return;
        // Clear album selection first so the all-songs branch is taken.
        SelectedArtistAlbum = null;
        IsArtistAllSongsView = true;
        ArtistSongsTitle = $"All {SelectedArtist.Name} Songs";
        RefreshArtistSongs();
    }

    [RelayCommand]
    private void SelectArtistAlbum(AlbumSummary album)
    {
        if (SelectedArtist is null) return;
        IsArtistAllSongsView = false;
        SelectedArtistAlbum = album;
        ArtistSongsTitle = album.Name;
        RefreshArtistSongs();
    }

    [RelayCommand]
    private void BackToArtist()
    {
        IsArtistAllSongsView = false;
        SelectedArtistAlbum = null;
        ArtistSongsTitle = string.Empty;
        // Back to Layer 2: repopulate with all artist songs for navigation.
        RefreshArtistSongs();
    }

    [RelayCommand]
    private void BackToArtists()
    {
        SelectedArtist = null;
        ArtistAlbums.Clear();
        ArtistSongs.Clear();
        IsArtistAllSongsView = false;
        SelectedArtistAlbum = null;
        ArtistSongsTitle = string.Empty;
    }

    [RelayCommand]
    private void SelectGenre(GenreSummary genre)
    {
        SelectedGenre = genre;
    }

    [RelayCommand]
    private void BackToGenres()
    {
        SelectedGenre = null;
        GenreSongs.Clear();
        GenreSongsTitle = string.Empty;
    }

    partial void OnSelectedGenreChanged(GenreSummary? value)
    {
        if (_isRefreshingSummaries) return;

        if (!IsTabSwitching)
        {
            GenreSongs.Clear();
        }
        GenreSongsTitle = value?.Name ?? string.Empty;

        if (value is not null)
        {
            RefreshGenreSongs();
        }

        UpdateSortContext();
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
            if (_uiContext is not null)
            {
                _uiContext.Post(async __ =>
                {
                    await ReloadSortedAsync();
                }, null);
            }
            else
            {
                // No captured UI context (e.g. headless/test construction): run
                // inline so search still refreshes the views.
                try { ReloadSortedAsync().GetAwaiter().GetResult(); }
                catch { /* best effort */ }
            }
        }, null, 350, Timeout.Infinite);
    }

    [RelayCommand]
    private void ToggleSortDirection() => IsSortDescending = !IsSortDescending;

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = "";
        _ = ReloadSortedAsync();
    }

    [RelayCommand]
    private void ClearSearchText() => ClearSearch();

    [RelayCommand]
    private void ClearField() => ClearSearch();

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

    private async Task ImportPathsAsync(IReadOnlyList<string> paths, string? importedFolder = null)
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
                    Artist = metadata.Artist ?? "Unknown Artist",
                    AlbumArtist = metadata.AlbumArtist,
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
        var addedTracks = new List<Track>();

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
                            _allTracks.Add(result.Track);
                            addedTracks.Add(result.Track);
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
                IsLoading = false;
                LoadingStatusText = "";
                System.Diagnostics.Debug.WriteLine($"TuneVault: import persist failed: {ex}");
                return;
            }
        }

        // No auto-select after import: avoid an implicit selection highlight.
        // The user can click a row to select + play.

        IsLoading = false;
        LoadingStatusText = "";
        LoadingProgress = 0;

        // Keep the visible order consistent with the selected sort after an import.
        if (added > 0)
        {
            await ReloadSortedAsync();
        }

        // Success toast (only when something was actually added).
        if (added > 0)
        {
            if (importedFolder is not null)
            {
                ToastTitle = "Successfully added";
                ToastMessage = $"Folder \"{Path.GetFileName(importedFolder)}\" added from:\n{importedFolder}";
                ToastCover = null;
            }
            else
            {
                var first = addedTracks[0];
                ToastTitle = "Successfully added";
                var meta = $"\"{first.Title}\" — {first.Artist}{(string.IsNullOrWhiteSpace(first.Album) ? "" : $" • {first.Album}")}";
                ToastMessage = added == 1 ? meta : $"{meta}  (+{added - 1} more)";
                ToastCover = CreateToastCover(addedTracks.FirstOrDefault(t => t.PictureData is { Length: > 0 }) ?? first);
            }
            ShowToast = true;
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

    private static Avalonia.Media.Imaging.Bitmap? CreateToastCover(Track? track)
    {
        if (track?.PictureData is not { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            return new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void CloseDuplicateModal()
    {
        ShowDuplicateModal = false;
    }

    [RelayCommand]
    private void CloseToast()
    {
        ShowToast = false;
        ToastCover = null;
        ToastSubtitle = "";
        ToastDetail = "";
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
        var index = -1;
        for (var i = 0; i < Tracks.Count; i++)
        {
            if (Tracks[i].Id == track.Id) { index = i; break; }
        }
        var wasSelected = SelectedTrack?.Id == track.Id;

        // Remove by Id (not reference) from every collection that may hold
        // this track, so removal works even during search/filter or from
        // another view.
        for (var i = Tracks.Count - 1; i >= 0; i--)
            if (Tracks[i].Id == track.Id) Tracks.RemoveAt(i);
        for (var i = AlbumTracks.Count - 1; i >= 0; i--)
            if (AlbumTracks[i].Id == track.Id) AlbumTracks.RemoveAt(i);
        for (var i = ArtistSongs.Count - 1; i >= 0; i--)
            if (ArtistSongs[i].Id == track.Id) ArtistSongs.RemoveAt(i);
        for (var i = GenreSongs.Count - 1; i >= 0; i--)
            if (GenreSongs[i].Id == track.Id) GenreSongs.RemoveAt(i);
        _allTracks.RemoveAll(t => t.Id == track.Id);

        if (wasSelected || SelectedTrack is null)
        {
            SelectedTrack = Tracks.Count == 0
                ? null
                : Tracks[Math.Min(Math.Max(index, 0), Tracks.Count - 1)];
        }

        TrackRemoved?.Invoke(track);
    }

    private static string TitleFromPath(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(title) ? "Untitled track" : title;
    }
}

public enum SortContext
{
    AllSongsTracks,
    AlbumsList,
    AlbumTracks,
    ArtistsList,
    ArtistTracks,
    AlbumArtistsList,
    AlbumArtistAlbums,
    AlbumArtistTracks,
    AlbumArtistAllTracks,
    GenresList,
    GenreTracks,
}
