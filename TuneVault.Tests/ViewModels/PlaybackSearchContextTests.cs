using Avalonia.Headless.XUnit;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class PlaybackSearchContextTests
{
    private static (LibraryViewModel library, string dbPath, List<string> tempFiles) CreateLibrary()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-psc-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var library = new LibraryViewModel(
            new SoundFileMetadataReader(), new AvaloniaFilePickerService(() => null), repo);

        var tempFiles = new List<string>();
        var tracks = new List<Track>();
        void Add(string title, string artist, string album, string genre)
        {
            var file = Path.Combine(Path.GetTempPath(), $"tunevault-audio-{Guid.NewGuid()}.mp3");
            File.WriteAllText(file, "dummy");
            tempFiles.Add(file);
            tracks.Add(new Track
            {
                FilePath = file, Title = title, Artist = artist, AlbumArtist = artist,
                Album = album, Genre = genre, Duration = TimeSpan.FromSeconds(100),
            });
        }
        // Three tracks share one album/artist/genre context; two are elsewhere.
        Add("MatchA", "Yeng Constantino", "Salamat", "Rock");
        Add("MatchB", "Yeng Constantino", "Salamat", "Rock");
        Add("PlainC", "Yeng Constantino", "Salamat", "Rock");
        Add("OtherD", "Other Artist", "Other Album", "Pop");
        Add("OtherE", "Other Artist", "Other Album", "Pop");
        repo.AddRangeAsync(tracks).GetAwaiter().GetResult();
        library.LoadLibraryAsync().GetAwaiter().GetResult();
        return (library, dbPath, tempFiles);
    }

    private static void Cleanup(string dbPath, List<string> tempFiles)
    {
        foreach (var f in tempFiles) try { File.Delete(f); } catch { }
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    private static Mock<IAudioPlayer> MakePlayer()
    {
        var player = new Mock<IAudioPlayer>();
        player.Setup(p => p.Load(It.IsAny<string>())).Returns(TimeSpan.FromSeconds(100));
        player.Setup(p => p.IsPlaying).Returns(true);
        player.Setup(p => p.Position).Returns(TimeSpan.Zero);
        player.Setup(p => p.Duration).Returns(TimeSpan.FromSeconds(100));
        return player;
    }

    private static void ConfigureContext(LibraryViewModel library, NavigationTab tab)
    {
        switch (tab)
        {
            case NavigationTab.Albums:
                library.CurrentTab = NavigationTab.Albums;
                library.SelectedAlbum = library.Albums.First(a => a.Name == "Salamat");
                break;
            case NavigationTab.Artists:
                library.IsArtistsTab = true;
                library.CurrentTab = NavigationTab.Artists;
                library.SelectedArtist = library.Artists.First(a => a.Name == "Yeng Constantino");
                break;
            case NavigationTab.AlbumArtists:
                library.IsAlbumArtistsTab = true;
                library.CurrentTab = NavigationTab.AlbumArtists;
                library.SelectedArtist = library.Artists.First(a => a.Name == "Yeng Constantino");
                library.SelectedArtistAlbum = library.ArtistAlbums.First(a => a.Name == "Salamat");
                break;
            case NavigationTab.Genres:
                library.CurrentTab = NavigationTab.Genres;
                library.SelectedGenre = library.Genres.First(a => a.Name == "Rock");
                break;
            default:
                library.CurrentTab = NavigationTab.AllSongs;
                break;
        }
    }

    private static async Task AssertClearRestoresQueue(
        NavigationTab tab, string term, string playTitle, string expectedNext)
    {
        var (library, dbPath, files) = CreateLibrary();
        try
        {
            ConfigureContext(library, tab);
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);

            library.SearchText = term;
            await Task.Delay(500); // search debounce
            // Search filters the summary lists, which collapses album/artist
            // drill-down; the visible search results live in Tracks.
            Assert.Equal(term == "MatchA" ? 1 : 2, library.Tracks.Count);
            var playing = library.Tracks.Single(t => t.Title == playTitle);
            vm.PlayTrackCommand.Execute(playing);

            player.Invocations.Clear();
            library.ClearSearchCommand.Execute(null);

            // Unfiltered library and the restored detail queue (reselected from
            // the playing track) are both back; the playing track is untouched.
            Assert.Equal(5, library.Tracks.Count);
            Assert.Equal(3, library.AlbumTracks.Count);
            Assert.Same(playing, vm.NowPlayingTrack);
            Assert.True(vm.IsPlaying);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
            player.Verify(p => p.Stop(), Times.Never);

            // Navigation uses the restored queue, not the old search results.
            vm.NextCommand.Execute(null);
            Assert.Equal(expectedNext, vm.NowPlayingTrack!.Title);
            Assert.False(library.ShowToast);
            vm.PreviousCommand.Execute(null);
            Assert.Same(playing, vm.NowPlayingTrack);
        }
        finally { Cleanup(dbPath, files); }
    }

    // Single-result searches: "MatchA" is the only result in every context.

    [AvaloniaFact]
    public Task SingleResult_AllSongs() =>
        AssertClearRestoresQueue(NavigationTab.AllSongs, "MatchA", "MatchA", "MatchB");

    [AvaloniaFact]
    public Task SingleResult_Albums() =>
        AssertClearRestoresQueue(NavigationTab.Albums, "MatchA", "MatchA", "MatchB");

    [AvaloniaFact]
    public Task SingleResult_Artists() =>
        AssertClearRestoresQueue(NavigationTab.Artists, "MatchA", "MatchA", "MatchB");

    [AvaloniaFact]
    public Task SingleResult_AlbumArtists() =>
        AssertClearRestoresQueue(NavigationTab.AlbumArtists, "MatchA", "MatchA", "MatchB");

    [AvaloniaFact]
    public Task SingleResult_Genres() =>
        AssertClearRestoresQueue(NavigationTab.Genres, "MatchA", "MatchA", "MatchB");

    // Multiple-result searches: play the LAST result so Next must leave the
    // filtered queue. Before the fix this showed "Last track" instead.

    [AvaloniaFact]
    public Task MultipleResults_AllSongs() =>
        AssertClearRestoresQueue(NavigationTab.AllSongs, "Match", "MatchB", "PlainC");

    [AvaloniaFact]
    public Task MultipleResults_Albums() =>
        AssertClearRestoresQueue(NavigationTab.Albums, "Match", "MatchB", "PlainC");

    [AvaloniaFact]
    public Task MultipleResults_Artists() =>
        AssertClearRestoresQueue(NavigationTab.Artists, "Match", "MatchB", "PlainC");

    [AvaloniaFact]
    public Task MultipleResults_AlbumArtists() =>
        AssertClearRestoresQueue(NavigationTab.AlbumArtists, "Match", "MatchB", "PlainC");

    [AvaloniaFact]
    public Task MultipleResults_Genres() =>
        AssertClearRestoresQueue(NavigationTab.Genres, "Match", "MatchB", "PlainC");

    [AvaloniaFact]
    public async Task ClearSearch_RaisesSingleNotification()
    {
        var (library, dbPath, files) = CreateLibrary();
        try
        {
            var notifications = 0;
            library.SearchCleared += () => notifications++;

            library.SearchText = "Match";
            await Task.Delay(500);
            Assert.Equal(0, notifications); // applying a search is not a clear

            library.ClearSearchCommand.Execute(null);
            Assert.Equal(1, notifications);

            await Task.Delay(500); // a stale debounce post must not notify again
            Assert.Equal(1, notifications);
        }
        finally { Cleanup(dbPath, files); }
    }

    [AvaloniaFact]
    public async Task ClearSearch_KeepsPosition_NoReload()
    {
        var (library, dbPath, files) = CreateLibrary();
        try
        {
            library.SearchText = "Match";
            await Task.Delay(500);
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            var playing = library.Tracks.Single(t => t.Title == "MatchB");
            vm.PlayTrackCommand.Execute(playing);
            vm.PositionSeconds = 25;

            player.Invocations.Clear();
            library.ClearSearchCommand.Execute(null);

            Assert.Same(playing, vm.NowPlayingTrack);
            Assert.True(vm.IsPlaying);
            Assert.Equal(25, vm.PositionSeconds);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
            player.Verify(p => p.Stop(), Times.Never);
        }
        finally { Cleanup(dbPath, files); }
    }

    [AvaloniaFact]
    public async Task ClearSearch_AfterViewSwitch_KeepsCapturedQueue()
    {
        var (library, dbPath, files) = CreateLibrary();
        try
        {
            library.SearchText = "Match";
            await Task.Delay(500);
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            var playing = library.Tracks.Single(t => t.Title == "MatchB");
            vm.PlayTrackCommand.Execute(playing);

            // Switch to a different tab while the search is still active.
            library.CurrentTab = NavigationTab.Genres;
            library.SelectedGenre = library.Genres.First(g => g.Name == "Rock");

            player.Invocations.Clear();
            library.ClearSearchCommand.Execute(null);

            Assert.Same(playing, vm.NowPlayingTrack);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);

            // Still on the captured queue, where MatchB was the last track.
            vm.NextCommand.Execute(null);
            Assert.Same(playing, vm.NowPlayingTrack);
            Assert.True(library.ShowToast);
            Assert.Equal("No next track in All Songs.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, files); }
    }

    [AvaloniaFact]
    public async Task ClearSearch_PlayingTrackAbsentFromRestoredCollection_IsNoOp()
    {
        var (library, dbPath, files) = CreateLibrary();
        try
        {
            library.CurrentTab = NavigationTab.Albums;
            library.SelectedAlbum = library.Albums.First(a => a.Name == "Salamat");
            var otherAlbum = library.Albums.First(a => a.Name == "Other Album");

            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);

            library.SearchText = "Match";
            await Task.Delay(500);
            var playing = library.Tracks.Single(t => t.Title == "MatchB");
            vm.PlayTrackCommand.Execute(playing);

            // Same tab, but point the detail view at an album that does not
            // contain the playing track before clearing the search.
            library.SelectedAlbum = otherAlbum;

            player.Invocations.Clear();
            library.ClearSearchCommand.Execute(null);

            Assert.DoesNotContain(library.AlbumTracks, t => t.Id == playing.Id);
            Assert.Same(playing, vm.NowPlayingTrack);
            Assert.True(vm.IsPlaying);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
            player.Verify(p => p.Stop(), Times.Never);
            Assert.False(library.ShowToast);
        }
        finally { Cleanup(dbPath, files); }
    }
}
