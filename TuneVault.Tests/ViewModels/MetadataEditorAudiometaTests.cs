using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Moq;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class MetadataEditorAudiometaTests
{
    private static (MetadataEditorViewModel vm, LibraryViewModel library, string dbPath) Create(Mock<IAudiometaService> serviceMock)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-me-audio-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var library = new LibraryViewModel(metadataReader, filePicker, repo);
        var vm = new MetadataEditorViewModel(filePicker, library, serviceMock.Object);
        return (vm, library, dbPath);
    }

    private static void Cleanup(string dbPath)
    {
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    [AvaloniaFact]
    public async Task FetchMetadata_PopulatesResults()
    {
        var mock = new Mock<IAudiometaService>();
        mock.Setup(s => s.SearchTracksAsync("Hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AudiometaTrack("id1", "Hello", "Adele", "25", "pop", 2015, 295000, null) });
        mock.Setup(s => s.SearchArtistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AudiometaArtist("Adele", new[] { "pop", "soul" }) });

        var (vm, _, dbPath) = Create(mock);
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "Hello", Artist = "Adele", Duration = TimeSpan.Zero };
            vm.EditTrackCommand.Execute(track);
            vm.EditTitle = "Hello";
            await vm.FetchMetadataCommand.ExecuteAsync(null);

            Assert.True(vm.HasMetadataResults);
            Assert.Single(vm.MetadataResults);
            Assert.Equal("Hello", vm.MetadataResults[0].Name);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task FetchMetadata_Empty_ShowsError()
    {
        var mock = new Mock<IAudiometaService>();
        mock.Setup(s => s.SearchTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AudiometaTrack>());

        var (vm, _, dbPath) = Create(mock);
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "Unknown", Artist = "Nobody", Duration = TimeSpan.Zero };
            vm.EditTrackCommand.Execute(track);
            vm.EditTitle = "Unknown";
            await vm.FetchMetadataCommand.ExecuteAsync(null);

            Assert.False(vm.HasMetadataResults);
            Assert.False(string.IsNullOrWhiteSpace(vm.MetadataError));
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task ApplyMetadata_FillsFields()
    {
        var mock = new Mock<IAudiometaService>();
        mock.Setup(s => s.SearchTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AudiometaTrack("id1", "Hello", "Adele", "25", "pop", 2015, 295000, null) });
        mock.Setup(s => s.SearchArtistsAsync("Adele", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AudiometaArtist("Adele", new[] { "pop", "soul" }) });

        var (vm, _, dbPath) = Create(mock);
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "Hello", Artist = "Adele", Duration = TimeSpan.Zero };
            vm.EditTrackCommand.Execute(track);
            await vm.ApplyTrackMetadataCommand.ExecuteAsync(new AudiometaTrack("id1", "Hello", "Adele", "25", "pop", 2015, 295000, null));

            Assert.Equal("Hello", vm.EditTitle);
            Assert.Equal("Adele", vm.EditArtist);
            Assert.Equal("25", vm.EditAlbum);
            Assert.Equal("2015", vm.EditYear);
            Assert.Equal("pop", vm.EditGenre);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void SelectPictureResult_ChangesOnlyCover()
    {
        var coverBytes = new byte[] { 1, 2, 3, 4 };
        var mock = new Mock<IAudiometaService>();
        mock.Setup(s => s.DownloadImageAsync("http://example.com/cover.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(coverBytes);

        var (vm, library, dbPath) = Create(mock);
        try
        {
            var track = new Track
            {
                FilePath = "/tmp/x.mp3",
                Title = "Original",
                Artist = "A",
                Album = "B",
                Genre = "C",
                Year = 1999,
                Duration = TimeSpan.FromSeconds(125),
            };
            vm.EditTrackCommand.Execute(track);

            vm.SelectedPictureResult = new AudiometaTrack(
                "id1", "Hello", "Adele", "25", "pop", 2015, 295000, "http://example.com/cover.jpg");

            Assert.Equal(coverBytes, vm.EditPictureData);
            Assert.Equal("Original", vm.EditTitle);
            Assert.Equal("A", vm.EditArtist);
            Assert.Equal("B", vm.EditAlbum);
            Assert.Equal("C", vm.EditGenre);
            Assert.Equal("1999", vm.EditYear);
            Assert.Equal("2:05", vm.EditDuration);
            Assert.True(library.ShowToast);
            Assert.Equal("Cover changed!", library.ToastMessage);
            Assert.Equal("Original", library.ToastTitle);
            mock.Verify(s => s.SearchArtistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            vm.CancelEditMetadataCommand.Execute(null);
            Assert.Null(vm.SelectedPictureResult);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void SelectRowResult_StillAppliesAllMetadata()
    {
        var mock = new Mock<IAudiometaService>();
        mock.Setup(s => s.SearchArtistsAsync("Adele", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AudiometaArtist("Adele", new[] { "pop", "soul" }) });

        var (vm, _, dbPath) = Create(mock);
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "Original", Artist = "A", Duration = TimeSpan.Zero };
            vm.EditTrackCommand.Execute(track);

            vm.SelectedMetadataResult = new AudiometaTrack("id1", "Hello", "Adele", "25", "pop", 2015, 295000, null);

            Assert.Equal("Hello", vm.EditTitle);
            Assert.Equal("Adele", vm.EditArtist);
            Assert.Equal("25", vm.EditAlbum);
            Assert.Equal("2015", vm.EditYear);
            Assert.Equal("pop", vm.EditGenre);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task ApplyMetadata_DoesNotChangeDuration()
    {
        var mock = new Mock<IAudiometaService>();
        mock.Setup(s => s.SearchArtistsAsync("Adele", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AudiometaArtist("Adele", new[] { "pop", "soul" }) });

        var (vm, _, dbPath) = Create(mock);
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "Hello", Artist = "Adele", Duration = TimeSpan.FromSeconds(125) };
            vm.EditTrackCommand.Execute(track);
            Assert.Equal("2:05", vm.EditDuration);

            await vm.ApplyTrackMetadataCommand.ExecuteAsync(
                new AudiometaTrack("id1", "Hello", "Adele", "25", "pop", 2015, 295000, null));

            Assert.Equal("2:05", vm.EditDuration);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void SelectPictureResult_FailedDownload_ShowsNoToast()
    {
        var mock = new Mock<IAudiometaService>();
        mock.Setup(s => s.DownloadImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var (vm, library, dbPath) = Create(mock);
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "Original", Artist = "A", Duration = TimeSpan.Zero };
            vm.EditTrackCommand.Execute(track);

            vm.SelectedPictureResult = new AudiometaTrack(
                "id1", "Hello", "Adele", "25", "pop", 2015, 295000, "http://example.com/cover.jpg");

            Assert.False(library.ShowToast);
            Assert.Null(vm.EditPictureData);
        }
        finally { Cleanup(dbPath); }
    }
}
