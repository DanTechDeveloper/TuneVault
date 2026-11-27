using Avalonia.Headless.XUnit;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class PlaybackResumeTests
{
    private static (LibraryViewModel library, SqliteLibraryRepository repo, string dbPath, List<string> tempFiles) CreateLibraryWithTracks(int count)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-resume-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var vm = new LibraryViewModel(metadataReader, filePicker, repo);

        var tempFiles = new List<string>();
        var tracks = new List<Track>();
        for (int i = 0; i < count; i++)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"tunevault-resume-audio-{Guid.NewGuid()}.mp3");
            File.WriteAllText(tempFile, "dummy");
            tempFiles.Add(tempFile);
            tracks.Add(new Track { FilePath = tempFile, Title = $"Song{i}", Artist = "A", Duration = TimeSpan.FromSeconds(180) });
        }
        repo.AddRangeAsync(tracks).GetAwaiter().GetResult();
        vm.LoadLibraryAsync().GetAwaiter().GetResult();
        return (vm, repo, dbPath, tempFiles);
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
        player.Setup(p => p.Load(It.IsAny<string>())).Returns(TimeSpan.FromSeconds(180));
        player.Setup(p => p.IsPlaying).Returns(false);
        return player;
    }

    [AvaloniaFact]
    public void RestoreSession_LoadsTrackPausedAtSavedPosition()
    {
        var (library, repo, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            repo.SavePlaybackStateAsync(library.Tracks[1].Id, 120, false).GetAwaiter().GetResult();
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library, repo);

            vm.RestoreSessionAsync().GetAwaiter().GetResult();

            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
            Assert.Equal(120, vm.PositionSeconds, 1);
            Assert.False(vm.IsPlaying);
            player.Verify(p => p.Load(tempFiles[1]), Times.Once);
            player.Verify(p => p.Seek(TimeSpan.FromSeconds(120)), Times.Once);
            player.Verify(p => p.Play(), Times.Never);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void RestoreSession_MissingFile_DoesNothing()
    {
        var (library, repo, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            repo.SavePlaybackStateAsync(library.Tracks[1].Id, 120, false).GetAwaiter().GetResult();
            File.Delete(tempFiles[1]);
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library, repo);

            vm.RestoreSessionAsync().GetAwaiter().GetResult();

            Assert.Null(vm.NowPlayingTrack);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void RestoreSession_DoesNotOverrideLoadedTrack()
    {
        var (library, repo, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library, repo);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            repo.SavePlaybackStateAsync(library.Tracks[1].Id, 120, false).GetAwaiter().GetResult();

            vm.RestoreSessionAsync().GetAwaiter().GetResult();

            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void RestoreSession_RestoresShuffle()
    {
        var (library, repo, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            repo.SavePlaybackStateAsync(library.Tracks[0].Id, 0, true).GetAwaiter().GetResult();
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library, repo);

            vm.RestoreSessionAsync().GetAwaiter().GetResult();

            Assert.True(vm.IsShuffle);
            vm.NextCommand.Execute(null);
            Assert.NotEqual("Song0", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void SaveSessionState_PersistsLoadedTrackAndPosition()
    {
        var (library, repo, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library, repo);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            vm.PositionSeconds = 42;
            vm.IsShuffle = true;

            vm.SaveSessionState();

            var state = repo.LoadPlaybackStateAsync().GetAwaiter().GetResult();
            Assert.NotNull(state);
            Assert.Equal(library.Tracks[0].Id, state!.TrackId);
            Assert.Equal(42, state.PositionSeconds, 3);
            Assert.True(state.IsShuffle);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }
}
