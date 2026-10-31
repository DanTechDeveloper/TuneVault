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
            var viewModel = new MainWindowViewModel(audioPlayer, metadataReader, filePicker);

            window = new MainWindow(viewModel);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}