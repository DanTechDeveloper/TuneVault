using CommunityToolkit.Mvvm.ComponentModel;

namespace TuneVault.ViewModels;

/// <summary>
/// Shared status text shown in the main window status bar.
/// </summary>
public sealed partial class StatusViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = "Add local audio files to start your library.";
}
