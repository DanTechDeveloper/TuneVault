using Avalonia.Headless.XUnit;
using System.IO;
using TuneVault.Models;
using TuneVault.Services;
using Xunit;

namespace TuneVault.Tests.Services;

public class SqliteLibraryRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteLibraryRepository _repo;

    public SqliteLibraryRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-test-{Guid.NewGuid()}.db");
        _repo = new SqliteLibraryRepository(_dbPath);
        _repo.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }

    private static Track MakeTrack(string title, string artist, string? album = null) => new Track
    {
        FilePath = $"/tmp/{title}.mp3",
        Title = title,
        Artist = artist,
        Album = album,
        Duration = TimeSpan.FromSeconds(100),
    };

    [AvaloniaFact]
    public async Task AddRangeAsync_AddsNewTrack()
    {
        var t = MakeTrack("Song1", "Artist1");
        var results = await _repo.AddRangeAsync(new[] { t });
        Assert.Single(results);
        Assert.Equal(AddOutcome.Added, results[0].Outcome);

        var loaded = await _repo.LoadAllAsync();
        Assert.Single(loaded);
        Assert.Equal("Song1", loaded[0].Title);
    }

    [AvaloniaFact]
    public async Task AddRangeAsync_DuplicatePath_ReportsDuplicatePath()
    {
        var t1 = MakeTrack("Song1", "Artist1");
        await _repo.AddRangeAsync(new[] { t1 });

        var t2 = MakeTrack("Song1Renamed", "Artist1");
        t2 = new Track { FilePath = t1.FilePath, Title = "Song1Renamed", Artist = "Artist1", Duration = TimeSpan.FromSeconds(100) };
        var results = await _repo.AddRangeAsync(new[] { t2 });
        Assert.Single(results);
        Assert.Equal(AddOutcome.DuplicatePath, results[0].Outcome);
    }

    [AvaloniaFact]
    public async Task AddRangeAsync_DuplicateIdentity_ReportsDuplicateIdentity()
    {
        var t1 = MakeTrack("Song1", "Artist1");
        await _repo.AddRangeAsync(new[] { t1 });

        var t2 = MakeTrack("Song1", "Artist1"); // same title+artist, different path
        t2 = new Track { FilePath = "/tmp/other.mp3", Title = "Song1", Artist = "Artist1", Duration = TimeSpan.FromSeconds(100) };
        var results = await _repo.AddRangeAsync(new[] { t2 });
        Assert.Single(results);
        Assert.Equal(AddOutcome.DuplicateIdentity, results[0].Outcome);
    }

    [AvaloniaFact]
    public async Task LoadAllAsync_OrdersByTitle()
    {
        await _repo.AddRangeAsync(new[] { MakeTrack("Zebra", "A"), MakeTrack("Apple", "A") });
        var loaded = await _repo.LoadAllAsync(TrackSortField.Title, false);
        Assert.Equal("Apple", loaded[0].Title);
        Assert.Equal("Zebra", loaded[1].Title);
    }

    [AvaloniaFact]
    public async Task LoadAllAsync_Descending()
    {
        await _repo.AddRangeAsync(new[] { MakeTrack("Apple", "A"), MakeTrack("Zebra", "A") });
        var loaded = await _repo.LoadAllAsync(TrackSortField.Title, true);
        Assert.Equal("Zebra", loaded[0].Title);
        Assert.Equal("Apple", loaded[1].Title);
    }

    [AvaloniaFact]
    public async Task DeleteAsync_RemovesRow()
    {
        var t = MakeTrack("Song1", "Artist1");
        await _repo.AddRangeAsync(new[] { t });
        await _repo.DeleteAsync(t.Id);
        var loaded = await _repo.LoadAllAsync();
        Assert.Empty(loaded);
    }

    [AvaloniaFact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var t = MakeTrack("Song1", "Artist1", "Album1");
        await _repo.AddRangeAsync(new[] { t });
        t.Title = "Song1Updated";
        t.AlbumArtist = "Various Artists";
        await _repo.UpdateAsync(t);
        var loaded = await _repo.LoadAllAsync();
        Assert.Equal("Song1Updated", loaded[0].Title);
        Assert.Equal("Various Artists", loaded[0].AlbumArtist);
    }

    [AvaloniaFact]
    public async Task LoadAllAsync_LoadsAlbumArtist()
    {
        var t = new Track { FilePath = "/tmp/x.mp3", Title = "T", Artist = "A", AlbumArtist = "VA", Duration = TimeSpan.FromSeconds(10) };
        await _repo.AddRangeAsync(new[] { t });
        var loaded = await _repo.LoadAllAsync();
        Assert.Equal("VA", loaded[0].AlbumArtist);
    }

    [AvaloniaFact]
    public async Task IdentityExistsAsync_True_WhenExists()
    {
        await _repo.AddRangeAsync(new[] { MakeTrack("Song1", "Artist1") });
        var exists = await _repo.IdentityExistsAsync("Song1", "Artist1");
        Assert.True(exists);
    }

    [AvaloniaFact]
    public async Task IdentityExistsAsync_False_WhenNotExists()
    {
        var exists = await _repo.IdentityExistsAsync("Nope", "Nobody");
        Assert.False(exists);
    }

    [AvaloniaFact]
    public async Task LoadPathIndexAsync_ReturnsMap()
    {
        await _repo.AddRangeAsync(new[] { MakeTrack("Song1", "Artist1") });
        var index = await _repo.LoadPathIndexAsync();
        Assert.Single(index);
    }
}