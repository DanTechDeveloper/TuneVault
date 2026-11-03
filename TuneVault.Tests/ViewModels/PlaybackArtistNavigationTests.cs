using Avalonia.Headless.XUnit;
using Moq;
using System;
using System.IO;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class PlaybackArtistNavigationTests
{
    private static (LibraryViewModel library, string dbPath, List<string> tempFiles) CreateLibraryWithArtistTracks()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-pan-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var vm = new LibraryViewModel(metadataReader, filePicker, repo);

        var tempFiles = new List<string>();
        var tracks = new System.Collections.Generic.List<Track>();
        for (int i = 0; i < 2; i++)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"tunevault-audio-{Guid.NewGuid()}.mp3");
            File.WriteAllText(tempFile, "dummy");
            tempFiles.Add(tempFile);
            tracks.Add(new Track { FilePath = tempFile, Title = $"Song{i}", Artist = "Yeng Constantino", Album = "Salamat", Duration = TimeSpan.FromSeconds(100) });
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

    private static Mock<IAudioPlayer> MakePlayer()
    {
        var player = new Mock<IAudioPlayer>();
        player.Setup(p => p.Load(It.IsAny<string>())).Returns(TimeSpan.FromSeconds(100));
        player.Setup(p => p.IsPlaying).Returns(false);
        player.Setup(p => p.Position).Returns(TimeSpan.Zero);
        player.Setup(p => p.Duration).Returns(TimeSpan.FromSeconds(100));
        return player;
    }

    [AvaloniaFact]
    public void Yeng_Salamat_Next_AdvancesWithinAlbum()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithArtistTracks();
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);

            // Simulate user: play a song in AllSongs first
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);

            // Navigate to AlbumArtists, select Yeng, select Salamat (binding path)
            var artist = library.Artists.First(a => a.Name == "Yeng Constantino");
            library.SelectedArtist = artist;
            var album = library.ArtistAlbums.First(a => a.Name == "Salamat");
            library.SelectedArtistAlbum = album;

            Assert.True(library.IsArtistSongsView);
            Assert.Equal(2, library.ArtistSongs.Count);

            // Now play Song0 in the album context
            vm.PlayTrackCommand.Execute(library.ArtistSongs[0]);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);

            // Next should advance to Song1 within Salamat
            vm.NextCommand.Execute(null);
            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);

            // Next again: no wrap, stays on Song1
            vm.NextCommand.Execute(null);
            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);

            // Previous from Song1 -> Song0
            vm.PreviousCommand.Execute(null);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);

            // Previous from Song0 (first) -> restarts Song0
            vm.PreviousCommand.Execute(null);
            player.Verify(p => p.Seek(TimeSpan.Zero), Times.Once);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Yeng_Salamat_PlayFromAllSongs_ThenNext_StaysInAllSongs()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithArtistTracks();
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);

            // Play Song0 in AllSongs
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);

            // Navigate to AlbumArtists (but don't play there)
            var artist = library.Artists.First(a => a.Name == "Yeng Constantino");
            library.SelectedArtist = artist;
            var album = library.ArtistAlbums.First(a => a.Name == "Salamat");
            library.SelectedArtistAlbum = album;

            // NowPlayingTrack is still Song0 (from AllSongs). Next follows AllSongs.
            // (With playing-track's list model, queue = Tracks since _loadedTrack is in Tracks)
            vm.NextCommand.Execute(null);
            // Should advance within AllSongs (only 2 tracks, Song0->Song1)
            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }
}