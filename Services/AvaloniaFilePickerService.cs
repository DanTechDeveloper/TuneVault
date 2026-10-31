using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace TuneVault.Services;

/// <summary>
/// Opens Avalonia's file picker for audio files. The window is resolved lazily
/// so this service can be created before the main window exists.
/// </summary>
public sealed class AvaloniaFilePickerService : IFilePickerService
{
    private readonly Func<Window?> _windowProvider;

    public AvaloniaFilePickerService(Func<Window?> windowProvider)
    {
        _windowProvider = windowProvider;
    }

    public async Task<IReadOnlyList<string>> PickAudioFilesAsync()
    {
        var window = _windowProvider();
        if (window is null)
        {
            return Array.Empty<string>();
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add music to TuneVault",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio files")
                {
                    Patterns = new[] { "*.wav", "*.mp3", "*.flac", "*.ogg", "*.opus", "*.aiff", "*.aif" },
                    MimeTypes = new[] { "audio/*" },
                    AppleUniformTypeIdentifiers = new[] { "public.audio" },
                },
                FilePickerFileTypes.All,
            },
        });

        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    public async Task<string?> PickAudioFolderAsync()
    {
        var window = _windowProvider();
        if (window is null)
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select music folder to import",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return null;
        }

        return folders[0].TryGetLocalPath();
    }
}
