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

public class MetadataEditorViewModelTests
{
    private static (MetadataEditorViewModel vm, LibraryViewModel library, string dbPath) Create()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-me-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var library = new LibraryViewModel(metadataReader, filePicker, repo);
        var vm = new MetadataEditorViewModel(filePicker, library);
        return (vm, library, dbPath);
    }

    private static void Cleanup(string dbPath)
    {
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    [AvaloniaFact]
    public void EditTrack_Null_DoesNothing()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            vm.EditTrackCommand.Execute(null);
            Assert.False(vm.IsEditMetadataModalOpen);
            Assert.Null(vm.EditingTrack);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void EditTrack_PopulatesFields()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            var track = new Track
            {
                FilePath = "/tmp/x.mp3",
                Title = "MyTitle",
                Artist = "MyArtist",
                Album = "MyAlbum",
                Genre = "Rock",
                Year = 2020,
                Duration = TimeSpan.FromSeconds(120),
                PictureData = new byte[] { 1, 2, 3 },
            };
            vm.EditTrackCommand.Execute(track);
            Assert.True(vm.IsEditMetadataModalOpen);
            Assert.Same(track, vm.EditingTrack);
            Assert.Equal("MyTitle", vm.EditTitle);
            Assert.Equal("MyArtist", vm.EditArtist);
            Assert.Equal("MyAlbum", vm.EditAlbum);
            Assert.Equal("Rock", vm.EditGenre);
            Assert.Equal("2020", vm.EditYear);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void EditTrack_NullAlbumGenre_UsesEmpty()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "T", Artist = "A", Album = null, Genre = null, Year = 0, Duration = TimeSpan.Zero };
            vm.EditTrackCommand.Execute(track);
            Assert.Equal("", vm.EditAlbum);
            Assert.Equal("", vm.EditGenre);
            Assert.Equal("", vm.EditYear);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void CancelEditMetadata_Resets()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "T", Artist = "A", Duration = TimeSpan.Zero };
            vm.EditTrackCommand.Execute(track);
            Assert.True(vm.IsEditMetadataModalOpen);
            vm.CancelEditMetadataCommand.Execute(null);
            Assert.False(vm.IsEditMetadataModalOpen);
            Assert.Null(vm.EditingTrack);
            Assert.False(vm.HasEditCoverArt);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void RemoveCoverArt_SetsFlag()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            var track = new Track { FilePath = "/tmp/x.mp3", Title = "T", Artist = "A", Duration = TimeSpan.Zero, PictureData = new byte[] { 1 } };
            vm.EditTrackCommand.Execute(track);
            Assert.True(vm.HasEditCoverArt);
            vm.RemoveCoverArtCommand.Execute(null);
            Assert.False(vm.HasEditCoverArt);
            Assert.Null(vm.EditPictureData);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task SaveTrackMetadataAsync_NullEditingTrack_DoesNothing()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            await vm.SaveTrackMetadataCommand.ExecuteAsync(null);
            Assert.False(vm.IsEditMetadataModalOpen);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void RequestRemoveEditingTrack_NullEditingTrack_DoesNotThrow()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            // EditingTrack is null; should not throw
            vm.RequestRemoveEditingTrackCommand.Execute(null);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void HasEditCoverArt_False_WhenNoBitmap()
    {
        var (vm, _, dbPath) = Create();
        try
        {
            Assert.False(vm.HasEditCoverArt);
        }
        finally { Cleanup(dbPath); }
    }
}