using System;
using System.IO;
using TuneVault.Models;
using TuneVault.Services;
using Xunit;

namespace TuneVault.Tests.Services;

public class SqliteLibraryRepositoryEdgeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteLibraryRepository _repo;

    public SqliteLibraryRepositoryEdgeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-edge-{Guid.NewGuid()}.db");
        _repo = new SqliteLibraryRepository(_dbPath);
        _repo.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }

    private static Track MakeTrack(string title, string artist, string? album = null, byte[]? pic = null) => new Track
    {
        FilePath = $"/tmp/{title}-{Guid.NewGuid()}.mp3",
        Title = title,
        Artist = artist,
        Album = album,
        Duration = TimeSpan.FromSeconds(100),
        PictureData = pic,
    };

    [Fact]
    public async Task AddRangeAsync_Empty_ReturnsEmpty()
    {
        var results = await _repo.AddRangeAsync(Array.Empty<Track>());
        Assert.Empty(results);
    }

    [Fact]
    public async Task AddRangeAsync_NullFilePath_ReportsFailed()
    {
        var t = new Track { FilePath = null, Title = "T", Artist = "A", Duration = TimeSpan.FromSeconds(10) };
        var results = await _repo.AddRangeAsync(new[] { t });
        Assert.Single(results);
        Assert.Equal(AddOutcome.Failed, results[0].Outcome);
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_DoesNotThrow()
    {
        var t = MakeTrack("Ghost", "Nobody");
        await _repo.UpdateAsync(t); // should not throw
        var loaded = await _repo.LoadAllAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        await _repo.DeleteAsync(Guid.NewGuid());
        var loaded = await _repo.LoadAllAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task CoverArt_RoundTrips()
    {
        var pic = new byte[] { 10, 20, 30, 40, 50 };
        var t = MakeTrack("Song", "Artist", "Album", pic);
        await _repo.AddRangeAsync(new[] { t });

        var loaded = await _repo.LoadAllAsync();
        Assert.Single(loaded);
        Assert.Equal(pic, loaded[0].PictureData);
    }

    [Fact]
    public async Task UpdateAsync_RemoveCoverArt_DeletesArtRow()
    {
        var pic = new byte[] { 10, 20, 30 };
        var t = MakeTrack("Song", "Artist", "Album", pic);
        await _repo.AddRangeAsync(new[] { t });

        t.PictureData = null;
        await _repo.UpdateAsync(t);

        var loaded = await _repo.LoadAllAsync();
        Assert.Single(loaded);
        Assert.Null(loaded[0].PictureData);
    }

    [Fact]
    public async Task IdentityExistsAsync_ExcludeTrackId_IgnoresSelf()
    {
        var t = MakeTrack("Song", "Artist");
        await _repo.AddRangeAsync(new[] { t });
        var exists = await _repo.IdentityExistsAsync("Song", "Artist", excludeTrackId: t.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task LoadAllAsync_NullAlbum_SortsLast()
    {
        await _repo.AddRangeAsync(new[] {
            MakeTrack("B", "A", null),
            MakeTrack("A", "A", "Zebra"),
        });
        var loaded = await _repo.LoadAllAsync(TrackSortField.Album, false);
        // Album present sorts before null/empty
        Assert.Equal("Zebra", loaded[0].Album);
        Assert.Null(loaded[1].Album);
    }

    [Fact]
    public async Task LoadAllAsync_ByYear()
    {
        var t1 = MakeTrack("Old", "A");
        t1.Year = 1990;
        var t2 = MakeTrack("New", "A");
        t2.Year = 2020;
        await _repo.AddRangeAsync(new[] { t1, t2 });
        var loaded = await _repo.LoadAllAsync(TrackSortField.Year, false);
        Assert.Equal(1990u, loaded[0].Year);
        Assert.Equal(2020u, loaded[1].Year);
    }

    [Fact]
    public async Task InitializeAsync_Idempotent()
    {
        await _repo.InitializeAsync();
        await _repo.InitializeAsync();
        var loaded = await _repo.LoadAllAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task AlbumArtist_PersistsAndLoads()
    {
        var t = new Track { FilePath = "/tmp/x.mp3", Title = "T", Artist = "TrackArtist", AlbumArtist = "AlbumArtist", Duration = TimeSpan.FromSeconds(10) };
        await _repo.AddRangeAsync(new[] { t });
        var loaded = await _repo.LoadAllAsync();
        Assert.Equal("AlbumArtist", loaded[0].AlbumArtist);
    }

    [Fact]
    public async Task AlbumArtist_NullInDb_LoadsNull()
    {
        var t = new Track { FilePath = "/tmp/y.mp3", Title = "T2", Artist = "A", AlbumArtist = null, Duration = TimeSpan.FromSeconds(10) };
        await _repo.AddRangeAsync(new[] { t });
        var loaded = await _repo.LoadAllAsync();
        Assert.Null(loaded[0].AlbumArtist);
    }
}