# TuneVault

A modern, fast, and lightweight local music player desktop application built with **Avalonia UI** (.NET 9), **CommunityToolkit.Mvvm**, **NAudio 3**, and **TagLibSharp**.

## Features

- **Local Music Playback**: Decodes WAV, MP3, FLAC, OGG/Vorbis, Opus, and AIFF audio files using native `libsndfile` decoders (`SoundFileReader`).
- **Dynamic Library Views**: Tabbed navigation between **All Songs**, **Albums**, **Artists**, and **Genres**.
- **Audio File & Folder Importing**: Add individual audio files or import entire directory trees with recursive scanning.
- **Persistent SQLite Library**: Added files and folders are stored in `library.db` under the per-user application data folder, so the library survives restarts.
- **Duplicate Audio Detection**: Duplicate rules are enforced by the database — same file path, or the same title/artist pair — and reported with a warning modal.
- **Missing File Flagging**: Tracks whose file disappeared (or whose drive is offline) are flagged in the list instead of being deleted from the library.
- **Tag & Metadata Editor**: In-app modal dialog to view and update track metadata (Title, Artist, Album, Genre, Year) and embed/replace/remove cover art via TagLibSharp directly in files.
- **In-List Play Controls**: Play and pause songs directly from library rows, or use the dedicated Now Playing sidebar with a seek progress slider and volume control.
- **Fluent Design System**: Responsive UI with dark/light theme adaptation using Avalonia's fluent resources and high-contrast dialog overlays.
- **Cross-Platform Audio Architecture**: Native ALSA playback on Linux (`AlsaOut`) and WASAPI on Windows (`WasapiPlayer`).

## Requirements

- **.NET 9 SDK** (e.g. .NET SDK 9.0.320+)
- **Linux Native Dependencies**:
  - `libasound2`
  - `libsndfile1`

## Getting Started

### Build

```bash
dotnet build TuneVault.csproj
```

### Run

```bash
dotnet run --project TuneVault.csproj
```

## Tech Stack

- **Framework**: Avalonia UI 11 (.NET 9)
- **MVVM**: CommunityToolkit.Mvvm
- **Audio Output & Decoding**: NAudio 3 (`NAudio.Core`, `NAudio.Alsa`, `NAudio.SoundFile`, `NAudio.Wasapi`)
- **Metadata & ID3 Tags**: TagLibSharp
- **Persistence**: SQLite via `Microsoft.Data.Sqlite`

## Architecture & Code Structure

- `Program.cs` / `App.axaml.cs`: Application entry point and composition root.
- `ViewModels/MainWindowViewModel.cs`: Thin window shell; owns tab navigation and exposes the feature ViewModels.
- `ViewModels/LibraryViewModel.cs`: Track collection, selection, imports, duplicate/delete confirmations.
- `ViewModels/PlaybackViewModel.cs`: Audio transport, now playing state, progress, and volume.
- `ViewModels/MetadataEditorViewModel.cs`: Tag/cover-art editor modal and metadata saving.
- `Models/Track.cs`: Observable domain model for tracks.
- `Services/`:
  - `IAudioPlayer` / `NaudioAudioPlayer`: Audio engine abstraction.
  - `IAudioMetadataReader` / `SoundFileMetadataReader`: TagLibSharp and SoundFileReader metadata parser.
  - `IFilePickerService` / `AvaloniaFilePickerService`: Audio and image file selection.
  - `ILibraryRepository` / `SqliteLibraryRepository`: SQLite library storage, duplicate rules, and schema (`~/.config/TuneVault/library.db` on Linux).
  - `LibraryKeys`: Path/identity normalization shared by imports and the database.
- `Views/`:
  - `MainWindow.axaml`: Main window layout with header, navigation sidebar, dynamic content, now playing sidebar, and modal overlays.
  - `AllSongsView.axaml`, `AlbumsView.axaml`, `ArtistsView.axaml`, `GenresView.axaml`: Library view tabs.
