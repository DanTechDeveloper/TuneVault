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

public class PlaybackViewModelTests
{
    private static (LibraryViewModel library, string dbPath) CreateLibrary()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-pb-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var vm = new LibraryViewModel(metadataReader, filePicker, repo);
        return (vm, dbPath);
    }

    private static void Cleanup(string dbPath)
    {
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    [AvaloniaFact]
    public void PlayTrack_NullTrack_DoesNothing()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(null);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
            Assert.Null(vm.NowPlayingTrack);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void PlayTrack_MissingFile_SetsIsMissing()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var track = new Track { FilePath = "/tmp/does-not-exist.mp3", Title = "T", Artist = "A", Duration = TimeSpan.FromSeconds(10) };
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(track);
            Assert.True(track.IsMissing);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void PlayTrack_NullFilePath_ReturnsEarly()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var track = new Track { FilePath = null, Title = "T", Artist = "A", Duration = TimeSpan.FromSeconds(10) };
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.PlayTrackCommand.Execute(track);
            player.Verify(p => p.Load(It.IsAny<string>()), Times.Never);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void PlayPauseCommand_CanExecute_False_WhenNoSelectedTrack()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            Assert.False(vm.PlayPauseCommand.CanExecute(null));
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void StopCommand_CanExecute_False_WhenNoLoadedTrack()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            Assert.False(vm.StopCommand.CanExecute(null));
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void Volume_Clamps_ToRange()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            vm.Volume = 1.5;
            Assert.Equal(1.0, vm.Volume);
            vm.Volume = -0.5;
            Assert.Equal(0.0, vm.Volume);
            vm.Volume = 0.5;
            Assert.Equal(0.5, vm.Volume);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void NowPlayingTitle_Default_WhenNoTrack()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            Assert.Equal("Nothing playing", vm.NowPlayingTitle);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void NowPlayingArtist_Default_WhenNoTrack()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            Assert.Equal("Add files, then press Play.", vm.NowPlayingArtist);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void PlayPauseIconKind_ReflectsIsPlaying()
    {
        var (library, dbPath) = CreateLibrary();
        try
        {
            var player = new Mock<IAudioPlayer>();
            var vm = new PlaybackViewModel(player.Object, library);
            Assert.Equal(Material.Icons.MaterialIconKind.PlayCircle, vm.PlayPauseIconKind);
            vm.IsPlaying = true;
            Assert.Equal(Material.Icons.MaterialIconKind.PauseCircle, vm.PlayPauseIconKind);
        }
        finally { Cleanup(dbPath); }
    }

}