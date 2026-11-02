using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TuneVault.Services;
using TuneVault.ViewModels;
using TuneVault.Views;

namespace TuneVault;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: create the services and ViewModel once, then inject
            // the ViewModel into the View.
            MainWindow? window = null;

            var audioPlayer = new NaudioAudioPlayer();
            var metadataReader = new SoundFileMetadataReader();
            var filePicker = new AvaloniaFilePickerService(() => window);

            var status = new StatusViewModel();
            var repository = new SqliteLibraryRepository();

            // Schema creation is a few milliseconds of DDL: do it here so no command
            // can fire before the tables exist. A broken database must not block startup.
            // Task.Run keeps the blocking wait off the UI synchronization context.
            try
            {
                Task.Run(() => repository.InitializeAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                status.Message = $"Could not open the library database: {ex.Message}";
            }

            var library = new LibraryViewModel(metadataReader, filePicker, repository, status);
            var playback = new PlaybackViewModel(audioPlayer, library, status);
            var editor = new MetadataEditorViewModel(filePicker, library, status);
            var viewModel = new MainWindowViewModel(library, playback, editor, status);

            window = new MainWindow(viewModel);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}