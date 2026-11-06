using Avalonia.Headless.XUnit;
using System.IO;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class LibraryViewModelTests
{
    private static (LibraryViewModel vm, string dbPath) CreateVm()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-vm-{Guid.NewGuid()}.db");
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
    public async Task LoadLibraryAsync_LoadsTracks()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "Apple", Artist = "A", Duration = TimeSpan.FromSeconds(10) },
                new Track { FilePath = "/tmp/b.mp3", Title = "Banana", Artist = "B", Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            Assert.Equal(2, vm.Tracks.Count);
            Assert.True(vm.HasTracks);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task SearchText_FiltersTracks_InMemory()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "Apple", Artist = "A", Duration = TimeSpan.FromSeconds(10) },
                new Track { FilePath = "/tmp/b.mp3", Title = "Banana", Artist = "B", Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            Assert.Equal(2, vm.Tracks.Count);

            vm.SearchText = "Apple";
            // debounce 350ms
            await Task.Delay(500);
            Assert.Single(vm.Tracks);
            Assert.Equal("Apple", vm.Tracks[0].Title);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task SearchText_Empty_RestoresAll()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "Apple", Artist = "A", Duration = TimeSpan.FromSeconds(10) },
                new Track { FilePath = "/tmp/b.mp3", Title = "Banana", Artist = "B", Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();

            vm.SearchText = "Apple";
            await Task.Delay(500);
            Assert.Single(vm.Tracks);

            vm.SearchText = "";
            await Task.Delay(500);
            Assert.Equal(2, vm.Tracks.Count);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task Albums_GroupsByAlbum()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "T1", Artist = "A", Album = "AlbumX", Duration = TimeSpan.FromSeconds(10) },
                new Track { FilePath = "/tmp/b.mp3", Title = "T2", Artist = "A", Album = "AlbumX", Duration = TimeSpan.FromSeconds(10) },
                new Track { FilePath = "/tmp/c.mp3", Title = "T3", Artist = "B", Album = "AlbumY", Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            Assert.Equal(2, vm.Albums.Count);
            var ax = vm.Albums.First(a => a.Name == "AlbumX");
            Assert.Equal(2, ax.TrackCount);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task Albums_UnknownAlbum_Bucket()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "T1", Artist = "A", Album = null, Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            Assert.Single(vm.Albums);
            Assert.Equal("Unknown Album", vm.Albums[0].Name);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task SelectAlbum_LoadsAlbumTracks()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "T1", Artist = "A", Album = "AlbumX", Duration = TimeSpan.FromSeconds(10) },
                new Track { FilePath = "/tmp/b.mp3", Title = "T2", Artist = "A", Album = "AlbumY", Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            var album = vm.Albums.First(a => a.Name == "AlbumX");
            vm.SelectAlbumCommand.Execute(album);
            Assert.True(vm.IsAlbumDetailView);
            Assert.Single(vm.AlbumTracks);
            Assert.Equal("T1", vm.AlbumTracks[0].Title);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task BackToAlbums_ClearsAlbumTracks()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "T1", Artist = "A", Album = "AlbumX", Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            var album = vm.Albums.First();
            vm.SelectAlbumCommand.Execute(album);
            Assert.True(vm.IsAlbumDetailView);
            vm.BackToAlbumsCommand.Execute(null);
            Assert.False(vm.IsAlbumDetailView);
            Assert.Empty(vm.AlbumTracks);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task Import_ArtistlessTrack_UsesUnknownArtistBucket()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "T1", Artist = "", Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            Assert.Single(vm.Artists);
            Assert.Equal("Unknown Artist", vm.Artists[0].Name);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public async Task Search_UnknownAlbum_FindsNullAlbumTrack()
    {
        var (vm, dbPath) = CreateVm();
        try
        {
            var repo = new SqliteLibraryRepository(dbPath);
            await repo.AddRangeAsync(new[] {
                new Track { FilePath = "/tmp/a.mp3", Title = "T1", Artist = "A", Album = null, Duration = TimeSpan.FromSeconds(10) },
            });
            await vm.LoadLibraryAsync();
            vm.SearchText = "Unknown Album";
            await Task.Delay(500);
            Assert.Single(vm.Tracks);
        }
        finally { Cleanup(dbPath); }
    }
}