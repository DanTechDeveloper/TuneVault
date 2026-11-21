using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TuneVault.ViewModels;

/// <summary>
/// Window shell. Composes the feature ViewModels and owns navigation only.
/// Contains no Avalonia or control types, so it can be tested without a window.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private NavigationTab _currentTab = NavigationTab.AllSongs;

    public bool IsAllSongsTab => CurrentTab == NavigationTab.AllSongs;
    public bool IsAlbumsTab => CurrentTab == NavigationTab.Albums;
    public bool IsArtistsTab => CurrentTab == NavigationTab.Artists;
    public bool IsAlbumArtists => CurrentTab == NavigationTab.AlbumArtists;
    public bool IsGenresTab => CurrentTab == NavigationTab.Genres;

    public MainWindowViewModel(
        LibraryViewModel library,
        PlaybackViewModel playback,
        MetadataEditorViewModel editor)
    {
        Library = library;
        Playback = playback;
        Editor = editor;
    }

    public LibraryViewModel Library { get; }

    public PlaybackViewModel Playback { get; }

    public MetadataEditorViewModel Editor { get; }

    partial void OnCurrentTabChanged(NavigationTab value)
    {
        OnPropertyChanged(nameof(IsAllSongsTab));
        OnPropertyChanged(nameof(IsAlbumsTab));
        OnPropertyChanged(nameof(IsArtistsTab));
        OnPropertyChanged(nameof(IsAlbumArtists));
        OnPropertyChanged(nameof(IsGenresTab));

        // Reset drill-down state so switching tabs always lands on Layer 1
        // (album/artist list) instead of retaining the last selected detail.
        // Keep the underlying collections intact so the playback queue (which
        // references them) keeps working after a tab switch.
        Library.IsTabSwitching = true;
        Library.IsArtistsTab = value == NavigationTab.Artists;
        Library.IsAlbumArtistsTab = value == NavigationTab.AlbumArtists;
        Library.CurrentTab = value;
        Library.SelectedAlbum = null;
        Library.SelectedArtist = null;
        Library.SelectedGenre = null;
        // Ensure drill-down flags reset even when SelectedArtist was already null
        // (in which case OnSelectedArtistChanged won't fire).
        Library.SelectedArtistAlbum = null;
        Library.IsArtistAllSongsView = false;
        // Tab switch lands on Layer 1 of the new tab: restore that tab's default
        // sort field and ascending order instead of inheriting the previous tab's.
        Library.ResetSortToDefault();
        Library.RefreshArtistsView();
        Library.IsTabSwitching = false;
    }

    [RelayCommand]
    private void SelectTab(NavigationTab tab)
    {
        CurrentTab = tab;
    }

    /// <summary>
    /// Called by the View's timer so the progress slider follows playback.
    /// </summary>
    public void RefreshPlayback()
    {
        Playback.RefreshPlayback();
    }

    public void Dispose()
    {
        Library.Dispose();
        Playback.Dispose();
    }
}
