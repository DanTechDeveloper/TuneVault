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

public class PlaybackShuffleTests
{
    private static (LibraryViewModel library, string dbPath, List<string> tempFiles) CreateLibraryWithTracks(int count)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-shuffle-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var vm = new LibraryViewModel(metadataReader, filePicker, repo);

        var tempFiles = new List<string>();
        var tracks = new List<Track>();
        for (int i = 0; i < count; i++)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"tunevault-shuffle-audio-{Guid.NewGuid()}.mp3");
            File.WriteAllText(tempFile, "dummy");
            tempFiles.Add(tempFile);
            tracks.Add(new Track { FilePath = tempFile, Title = $"Song{i}", Artist = "A", Duration = TimeSpan.FromSeconds(100) });
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
        return player;
    }

    [AvaloniaFact]
    public void Shuffle_NextVisitsEveryQueueTrackOnce()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.ToggleShuffleCommand.Execute(null);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);

            var played = new HashSet<Guid> { vm.NowPlayingTrack!.Id };
            for (var i = 0; i < 2; i++)
            {
                vm.NextCommand.Execute(null);
                Assert.True(played.Add(vm.NowPlayingTrack!.Id), "a track repeated before the queue was exhausted");
            }

            vm.NextCommand.Execute(null);
            Assert.Equal(3, played.Count);
            Assert.Equal("No next track in All Songs.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void ToggleShuffle_DoesNotReloadOrSeekCurrentTrack()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            var loaded = vm.NowPlayingTrack;

            player.Invocations.Clear();
            vm.ToggleShuffleCommand.Execute(null);
            vm.ToggleShuffleCommand.Execute(null);

            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
            Assert.Same(loaded, vm.NowPlayingTrack);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void ToggleShuffleOff_RestoresViewOrder()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);

            vm.ToggleShuffleCommand.Execute(null);
            vm.ToggleShuffleCommand.Execute(null);
            vm.NextCommand.Execute(null);

            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void ToggleShuffle_ShowsToastInBothStates()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);

            vm.ToggleShuffleCommand.Execute(null);
            Assert.True(library.ShowToast);
            Assert.Equal("Shuffle on", library.ToastTitle);
            Assert.Equal("Tracks play in random order.", library.ToastMessage);

            vm.ToggleShuffleCommand.Execute(null);
            Assert.True(library.ShowToast);
            Assert.Equal("Shuffle off", library.ToastTitle);
            Assert.Equal("Tracks play in their original order.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }
}
