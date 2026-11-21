using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
                System.Diagnostics.Debug.WriteLine($"TuneVault: database initialization failed: {ex}");
            }

            var library = new LibraryViewModel(metadataReader, filePicker, repository);
            var playback = new PlaybackViewModel(audioPlayer, library);

            // Resolve the Audiometa API key without hard-coding it in source:
            // 1) AUDIOMETA_API_KEY env var, 2) appsettings.local.json (gitignored).
            var apiKey = Environment.GetEnvironmentVariable("AUDIOMETA_API_KEY");
            string? keySource = null;
            if (!string.IsNullOrWhiteSpace(apiKey)) keySource = "env";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var candidates = new List<string>
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "appsettings.local.json"),
                    Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"),
                };
                // Walk up a couple of levels to find the project root (useful when
                // the working directory is bin/Debug/net9.0).
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 3 && dir is not null; i++)
                {
                    dir = dir.Parent;
                    if (dir is not null) candidates.Add(Path.Combine(dir.FullName, "appsettings.local.json"));
                }

                foreach (var configPath in candidates.Distinct())
                {
                    if (!File.Exists(configPath)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                        if (doc.RootElement.TryGetProperty("LastFm", out var aud) &&
                            aud.TryGetProperty("ApiKey", out var keyProp))
                        {
                            var v = keyProp.GetString();
                            if (!string.IsNullOrWhiteSpace(v))
                            {
                                apiKey = v;
                                keySource = configPath;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"TuneVault: audiometa config read failed ({configPath}): {ex.Message}");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"TuneVault: audiometa API key source: {keySource ?? "NONE"}");

            var audiometa = new LastFmService(apiKey);
            var editor = new MetadataEditorViewModel(filePicker, library, audiometa);
            var viewModel = new MainWindowViewModel(library, playback, editor);

            window = new MainWindow(viewModel);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}