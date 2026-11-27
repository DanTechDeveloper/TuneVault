using System.IO;
using Avalonia.Headless.XUnit;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class TabSortResetTests
{
    private static (LibraryViewModel lib, MainWindowViewModel main, string dbPath) Create()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-tabsort-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var library = new LibraryViewModel(metadataReader, filePicker, repo);
        var playback = new PlaybackViewModel(new NaudioAudioPlayer(), library);
        var editor = new MetadataEditorViewModel(filePicker, library);
        var main = new MainWindowViewModel(library, playback, editor);
        return (library, main, dbPath);
    }

    private static void Cleanup(string dbPath)
    {
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    [AvaloniaFact]
    public async Task SwitchingTabs_ResetsSortToDefault()
    {
        var (library, main, dbPath) = Create();
        try
        {
            await library.LoadLibraryAsync();

            library.SortField = TrackSortField.Artist;
            library.IsSortDescending = true;

            main.CurrentTab = NavigationTab.Artists;
            Assert.Equal(TrackSortField.Artist, library.SortField);
            Assert.False(library.IsSortDescending);

            main.CurrentTab = NavigationTab.AllSongs;
            Assert.Equal(TrackSortField.Title, library.SortField);
            Assert.False(library.IsSortDescending);
        }
        finally { Cleanup(dbPath); }
    }

    [AvaloniaFact]
    public void FavoritesTab_ResetsSortToDefault()
    {
        var (library, main, dbPath) = Create();
        try
        {
            library.SortField = TrackSortField.Year;
            library.IsSortDescending = true;

            main.CurrentTab = NavigationTab.Favorites;

            Assert.Equal(SortContext.FavoritesTracks, library.SortContext);
            Assert.Equal(TrackSortField.Title, library.SortField);
            Assert.False(library.IsSortDescending);
            Assert.Equal(5, library.SortFields.Count);

            main.CurrentTab = NavigationTab.AllSongs;
            Assert.Equal(SortContext.AllSongsTracks, library.SortContext);
        }
        finally { Cleanup(dbPath); }
    }
}
