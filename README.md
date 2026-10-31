# TuneVault

A modern, fast, and lightweight local music player desktop application built with **Avalonia UI** (.NET 9), **CommunityToolkit.Mvvm**, **NAudio 3**, and **TagLibSharp**.

## Features

- **Local Music Playback**: Decodes WAV, MP3, FLAC, OGG/Vorbis, Opus, and AIFF audio files using native `libsndfile` decoders (`SoundFileReader`).
- **Dynamic Library Views**: Tabbed navigation between **All Songs**, **Albums**, **Artists**, and **Genres**.
- **Audio File & Folder Importing**: Add individual audio files or import entire directory trees with recursive scanning.
- **Duplicate Audio Detection**: Automatically flags duplicates (by file path, metadata matching title/artist, or batch duplicates) with a warning modal.
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

## Architecture & Code Structure

- `Program.cs` / `App.axaml.cs`: Application entry point and composition root.
- `ViewModels/MainWindowViewModel.cs`: Core state and MVVM commands for navigation, playback, file import, and metadata editing.
- `Models/Track.cs`: Observable domain model for tracks.
- `Services/`:
  - `IAudioPlayer` / `NaudioAudioPlayer`: Audio engine abstraction.
  - `IAudioMetadataReader` / `SoundFileMetadataReader`: TagLibSharp and SoundFileReader metadata parser.
  - `IFilePickerService` / `AvaloniaFilePickerService`: Audio and image file selection.
- `Views/`:
  - `MainWindow.axaml`: Main window layout with header, navigation sidebar, dynamic content, now playing sidebar, and modal overlays.
  - `AllSongsView.axaml`, `AlbumsView.axaml`, `ArtistsView.axaml`, `GenresView.axaml`: Library view tabs.
