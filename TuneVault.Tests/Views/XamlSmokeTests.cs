using System;
using System.IO;
using Avalonia.Headless.XUnit;
using TuneVault.Services;
using TuneVault.ViewModels;
using TuneVault.Views;
using Xunit;

namespace TuneVault.Tests.Views;

public class XamlSmokeTests
{
    private static MainWindow CreateWindow()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-xaml-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var metadataReader = new SoundFileMetadataReader();
        var filePicker = new AvaloniaFilePickerService(() => null);
        var library = new LibraryViewModel(metadataReader, filePicker, repo);
        var playback = new PlaybackViewModel(new NaudioAudioPlayer(), library);
        var editor = new MetadataEditorViewModel(filePicker, library);
        var vm = new MainWindowViewModel(library, playback, editor);
        return new MainWindow(vm);
    }

    [AvaloniaFact]
    public void MainWindow_Loads()
    {
        var w = CreateWindow();
        Assert.NotNull(w);
    }

    [AvaloniaFact]
    public void AllSongsView_Loads()
    {
        var w = CreateWindow();
        var v = new AllSongsView { DataContext = w.DataContext };
        Assert.NotNull(v);
    }

    [AvaloniaFact]
    public void AlbumsView_Loads()
    {
        var w = CreateWindow();
        var v = new AlbumsView { DataContext = w.DataContext };
        Assert.NotNull(v);
    }

    [AvaloniaFact]
    public void AlbumArtistsView_Loads()
    {
        var w = CreateWindow();
        var v = new AlbumArtistsView { DataContext = w.DataContext };
        Assert.NotNull(v);
    }

    [AvaloniaFact]
    public void TrackRow_Loads()
    {
        var t = new TuneVault.Models.Track { Title = "T", Artist = "A" };
        var v = new TrackRow { DataContext = t };
        Assert.NotNull(v);
    }
}