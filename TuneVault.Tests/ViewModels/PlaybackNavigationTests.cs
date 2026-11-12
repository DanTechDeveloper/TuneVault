using Avalonia.Headless.XUnit;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class PlaybackNavigationTests
{
    private static (LibraryViewModel library, string dbPath, List<string> tempFiles) CreateLibraryWithTracks(int count)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-nav-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var vm = new LibraryViewModel(metadataReader, filePicker, repo);

        var tempFiles = new List<string>();
        var tracks = new System.Collections.Generic.List<Track>();
        for (int i = 0; i < count; i++)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"tunevault-audio-{Guid.NewGuid()}.mp3");
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
    public void Next_EmptyQueue_DoesNothing()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(0);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.NextCommand.Execute(null);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Next_NoLoadedTrack_PlaysFirst()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.NextCommand.Execute(null);
            Assert.NotNull(vm.NowPlayingTrack);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Next_MiddleTrack_PlaysNext()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            // load first track
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;
            // next
            vm.NextCommand.Execute(null);
            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Next_LastTrack_DoesNotWrap()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[2]); // last
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;
            var before = vm.NowPlayingTrack;
            vm.NextCommand.Execute(null);
            Assert.Same(before, vm.NowPlayingTrack);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_EmptyQueue_DoesNothing()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(0);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PreviousCommand.Execute(null);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_NoLoadedTrack_DoesNothing()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PreviousCommand.Execute(null);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
            Assert.Null(vm.NowPlayingTrack);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_MiddleTrack_PlaysPrevious()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[1]);
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;
            vm.PreviousCommand.Execute(null);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_FirstTrack_RestartsCurrent()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            player.Setup(p => p.IsPlaying).Returns(true);
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            vm.IsPlaying = true;
            vm.PreviousCommand.Execute(null);
            player.Verify(p => p.Seek(TimeSpan.Zero), Times.Once);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_FirstTrack_WhenPaused_Plays()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            player.Setup(p => p.IsPlaying).Returns(false);
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            vm.IsPlaying = false;
            vm.PreviousCommand.Execute(null);
            player.Verify(p => p.Play(), Times.AtLeastOnce);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Next_CrossView_FollowsCapturedQueue()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);

            vm.PlayTrackCommand.Execute(library.Tracks[1]);
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;

            // Browse an artist view whose queue is empty/different without
            // changing the queue the playing track belongs to.
            var nobody = new ArtistSummary("Nobody", 0, null);
            library.SelectedArtist = nobody;

            vm.NextCommand.Execute(null);

            Assert.Equal("Song2", vm.NowPlayingTrack!.Title);
            Assert.False(library.ShowToast);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_CrossView_FollowsCapturedQueue()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);

            vm.PlayTrackCommand.Execute(library.Tracks[1]);
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;

            var nobody = new ArtistSummary("Nobody", 0, null);
            library.SelectedArtist = nobody;

            vm.PreviousCommand.Execute(null);

            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Next_AtLastTrack_ShowsBoundaryToast()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[2]);
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;

            vm.NextCommand.Execute(null);

            Assert.Same(library.Tracks[2], vm.NowPlayingTrack);
            Assert.True(library.ShowToast);
            Assert.Equal("No next track in All Songs.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_AtFirstTrack_ShowsBoundaryToastAndRestarts()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            player.Setup(p => p.IsPlaying).Returns(true);
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);
            vm.IsPlaying = true;

            vm.PreviousCommand.Execute(null);

            player.Verify(p => p.Seek(TimeSpan.Zero), Times.Once);
            Assert.Equal("Song0", vm.NowPlayingTrack!.Title);
            Assert.True(library.ShowToast);
            Assert.Equal("Already at the first track of All Songs.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Next_EmptyQueue_ShowsNoTracksToast()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(0);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.NextCommand.Execute(null);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
            Assert.True(library.ShowToast);
            Assert.Equal("Add files or a folder to start playback.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Previous_EmptyQueue_ShowsNoTracksToast()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(0);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PreviousCommand.Execute(null);
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
            Assert.True(library.ShowToast);
            Assert.Equal("Add files or a folder to start playback.", library.ToastMessage);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    private static void SimulateTrackEnded(Mock<IAudioPlayer> player, PlaybackViewModel vm)
    {
        player.Setup(p => p.IsPlaying).Returns(false);
        player.Setup(p => p.Position).Returns(TimeSpan.FromSeconds(100));
        player.Setup(p => p.Duration).Returns(TimeSpan.FromSeconds(100));
        vm.IsPlaying = true;
        vm.RefreshPlayback();
    }

    [AvaloniaFact]
    public void RefreshPlayback_TrackEnded_AutoAdvancesToNext()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);

            SimulateTrackEnded(player, vm);

            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
            player.Verify(p => p.Load(tempFiles[1]), Times.Once);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void RefreshPlayback_LastTrackEnded_StopsWithoutWrapping()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[2]);

            SimulateTrackEnded(player, vm);

            Assert.Equal("Song2", vm.NowPlayingTrack!.Title); // kept for display
            Assert.False(vm.IsPlaying);
            Assert.Equal(0, vm.PositionSeconds);
            Assert.False(vm.StopCommand.CanExecute(null));
            Assert.Equal("No next track in All Songs.", library.ToastMessage);
            player.Verify(p => p.Load(tempFiles[0]), Times.Never);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void RefreshPlayback_UserSeeksToEnd_AdvancesExactlyOneTrack()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            // Next track is shorter, like a real playlist.
            player.Setup(p => p.Load(tempFiles[1])).Returns(TimeSpan.FromSeconds(80));
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[0]);

            // Stand-in for the Slider: when Maximum (DurationSeconds) shrinks
            // while Value sits at the old track's end, it writes the clamped
            // value back to PositionSeconds.
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PlaybackViewModel.DurationSeconds))
                {
                    vm.PositionSeconds = Math.Min(vm.PositionSeconds, vm.DurationSeconds);
                }
            };

            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;
            player.Setup(p => p.Position).Returns(TimeSpan.FromSeconds(100));
            player.Setup(p => p.Duration).Returns(TimeSpan.FromSeconds(100));
            vm.PositionSeconds = 100; // user drags the slider to the exact end

            player.Setup(p => p.IsPlaying).Returns(false);
            player.Invocations.Clear();
            vm.RefreshPlayback();

            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
            // No stale seek may land on the freshly loaded track.
            player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);

            // Next tick: the new track is at 0 and playing, so no extra skip.
            player.Setup(p => p.Position).Returns(TimeSpan.Zero);
            player.Setup(p => p.Duration).Returns(TimeSpan.FromSeconds(80));
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;
            vm.RefreshPlayback();
            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
            player.Verify(p => p.Load(tempFiles[2]), Times.Never);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void Next_SkipsRemovedTrackInCapturedQueue()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[1]);
            player.Setup(p => p.IsPlaying).Returns(true);
            vm.IsPlaying = true;

            library.Remove(library.Tracks[2]);

            vm.NextCommand.Execute(null);

            Assert.True(library.ShowToast);
            Assert.Equal("No next track in All Songs.", library.ToastMessage);
            Assert.Equal("Song1", vm.NowPlayingTrack!.Title);
        }
        finally { Cleanup(dbPath, tempFiles); }
    }

    [AvaloniaFact]
    public void OnTrackRemoved_DifferentInstance_StopsPlayback()
    {
        var (library, dbPath, tempFiles) = CreateLibraryWithTracks(3);
        try
        {
            var player = MakePlayer();
            player.Setup(p => p.IsPlaying).Returns(true);
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(library.Tracks[1]);
            vm.IsPlaying = true;

            // A different instance with the same Id (as a collection rebuild
            // could produce) must still stop playback.
            var ghost = new Track
            {
                Id = library.Tracks[1].Id,
                FilePath = library.Tracks[1].FilePath,
                Title = library.Tracks[1].Title,
                Artist = library.Tracks[1].Artist,
                Duration = TimeSpan.FromSeconds(100),
            };
            library.Remove(ghost);

            Assert.Null(vm.NowPlayingTrack);
            Assert.False(vm.IsPlaying);
            Assert.False(vm.StopCommand.CanExecute(null));
        }
        finally { Cleanup(dbPath, tempFiles); }
    }
}