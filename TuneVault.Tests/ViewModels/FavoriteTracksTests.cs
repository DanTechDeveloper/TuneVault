using Avalonia.Headless.XUnit;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class FavoriteTracksTests
{
    private static (LibraryViewModel library, string dbPath, List<string> tempFiles) CreateLibrary(int count)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-favorites-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var vm = new LibraryViewModel(new SoundFileMetadataReader(), new AvaloniaFilePickerService(() => null), repo);

        var tempFiles = new List<string>();
        var tracks = new List<Track>();
        for (int i = 0; i < count; i++)
        {
            var file = Path.Combine(Path.GetTempPath(), $"tunevault-fav-audio-{Guid.NewGuid()}.mp3");
            File.WriteAllText(file, "dummy");
            tempFiles.Add(file);
            tracks.Add(new Track { FilePath = file, Title = $"Song{i}", Artist = "A", Duration = TimeSpan.FromSeconds(100) });
        }
        repo.AddRangeAsync(tracks).GetAwaiter().GetResult();
        vm.LoadLibraryAsync().GetAwaiter().GetResult();
        return (vm, dbPath, tempFiles);
    }

    private static void Cleanup(string dbPath, List<string> tempFiles)
    {
        foreach (var f in tempFiles) try { File.Delete(f); } catch { }
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    [AvaloniaFact]
    public async Task ToggleFavorite_AddsAndRemovesFromFavoriteTracks()
    {
        var (library, dbPath, files) = CreateLibrary(3);
        try
        {
            var track = library.Tracks[1];
            await library.ToggleFavoriteCommand.ExecuteAsync(track);

            Assert.Single(library.FavoriteTracks);
            Assert.Equal(track.Id, library.FavoriteTracks[0].Id);
            Assert.True(track.IsFavorite);
            Assert.True(library.ShowToast);
            Assert.Equal("Added to favorites", library.ToastMessage);
            Assert.Equal(track.Title, library.ToastTitle);

            await library.ToggleFavoriteCommand.ExecuteAsync(track);
            Assert.Empty(library.FavoriteTracks);
            Assert.Equal("Removed from favorites", library.ToastMessage);
        }
        finally { Cleanup(dbPath, files); }
    }

    [AvaloniaFact]
    public async Task ToggleFavorite_PersistsAcrossReload()
    {
        var (library, dbPath, files) = CreateLibrary(2);
        try
        {
            var track = library.Tracks[0];
            await library.ToggleFavoriteCommand.ExecuteAsync(track);

            var reloaded = new SqliteLibraryRepository(dbPath);
            var tracks = await reloaded.LoadAllAsync();
            Assert.True(tracks.Single(t => t.Id == track.Id).IsFavorite);
        }
        finally { Cleanup(dbPath, files); }
    }

    [AvaloniaFact]
    public async Task FavoriteTracks_RespectsSortAndSearch()
    {
        var (library, dbPath, files) = CreateLibrary(3);
        try
        {
            await library.ToggleFavoriteCommand.ExecuteAsync(library.Tracks[0]);
            await library.ToggleFavoriteCommand.ExecuteAsync(library.Tracks[2]);

            Assert.Equal(new[] { "Song0", "Song2" }, library.FavoriteTracks.Select(t => t.Title));

            library.IsSortDescending = true;
            Assert.Equal(new[] { "Song2", "Song0" }, library.FavoriteTracks.Select(t => t.Title));

            library.SearchText = "Song2";
            await library.ReloadSortedAsync();
            Assert.Single(library.FavoriteTracks);
            Assert.Equal("Song2", library.FavoriteTracks[0].Title);
            Assert.False(library.ShowFavoritesNoResults);
        }
        finally { Cleanup(dbPath, files); }
    }

    [AvaloniaFact]
    public async Task Remove_DropsFavoriteRow()
    {
        var (library, dbPath, files) = CreateLibrary(2);
        try
        {
            var track = library.Tracks[0];
            await library.ToggleFavoriteCommand.ExecuteAsync(track);

            library.Remove(track);

            Assert.Empty(library.FavoriteTracks);
            Assert.False(library.HasFavoriteTracks);
        }
        finally { Cleanup(dbPath, files); }
    }

    [AvaloniaFact]
    public async Task PlayingFromFavorites_NextStaysInFavorites()
    {
        var (library, dbPath, files) = CreateLibrary(3);
        try
        {
            library.CurrentTab = NavigationTab.Favorites;
            var player = new Mock<IAudioPlayer>();
            player.Setup(p => p.Load(It.IsAny<string>())).Returns(TimeSpan.FromSeconds(100));
            player.Setup(p => p.IsPlaying).Returns(false);
            var playback = new PlaybackViewModel(player.Object, library);

            await library.ToggleFavoriteCommand.ExecuteAsync(library.Tracks[0]);
            await library.ToggleFavoriteCommand.ExecuteAsync(library.Tracks[2]);
            playback.PlayTrackCommand.Execute(library.FavoriteTracks[0]);

            library.CurrentTab = NavigationTab.AllSongs;
            playback.NextCommand.Execute(null);
            Assert.Equal("Song2", playback.NowPlayingTrack!.Title);

            playback.NextCommand.Execute(null);
            Assert.Equal("No next track in Favorites.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, files); }
    }
}
