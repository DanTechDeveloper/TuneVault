using System.Collections.Generic;
using System.Threading.Tasks;

namespace TuneVault.Services;

/// <summary>
/// UI-agnostic boundary for choosing local audio files.
/// </summary>
public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickAudioFilesAsync();

    Task<string?> PickAudioFolderAsync();

    Task<string?> PickImageFileAsync();
}

