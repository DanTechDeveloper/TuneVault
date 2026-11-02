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
    public bool IsGenresTab => CurrentTab == NavigationTab.Genres;

    public MainWindowViewModel(
        LibraryViewModel library,
        PlaybackViewModel playback,
        MetadataEditorViewModel editor,
        StatusViewModel status)
    {
        Library = library;
        Playback = playback;
        Editor = editor;
        Status = status;
    }

    public LibraryViewModel Library { get; }

    public PlaybackViewModel Playback { get; }

    public MetadataEditorViewModel Editor { get; }

    public StatusViewModel Status { get; }

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

    /// <summary>
    /// Called by the View's timer so the progress slider follows playback.
    /// </summary>
    public void RefreshPlayback()
    {
        Playback.RefreshPlayback();
    }

    public void Dispose()
    {
        Playback.Dispose();
    }
}
