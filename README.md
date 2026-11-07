# TuneVault

A modern, fast, and lightweight local music player desktop application built with **Avalonia UI** (.NET 9), **CommunityToolkit.Mvvm**, **NAudio 3**, and **TagLibSharp**.

## Features

- **Local Music Playback**: Decodes WAV, MP3, FLAC, OGG/Vorbis, Opus, and AIFF audio files using native `libsndfile` decoders (`SoundFileReader`).
- **Dynamic Library Views**: Tabbed navigation between **All Songs**, **Albums**, **Album Artists**, **Artists**, and **Genres**.
  - **Albums view**: two-layer — album list (with cover art, track counts, year, and an "Unknown Album" bucket) drilling into the album's filtered track list.
  - **Album Artists view**: artist list → artist detail (all-songs entry + album list) → album songs / all-artist songs, with reusable track rows.
  - **Artists view**: two-layer — artist list (grouped by track artist, with track counts, year, and an "Unknown Artist" bucket) → all artist songs.
  - **Genres view**: two-layer — genre list (with track counts, year, and an "Unknown Genre" bucket) → all genre songs.
- **Audio File & Folder Importing**: Add individual audio files or import entire directory trees with recursive scanning.
- **Persistent SQLite Library**: Added files and folders are stored in `library.db` under the per-user application data folder, so the library survives restarts.
- **Duplicate Audio Detection**: Duplicate rules are enforced by the database — same file path, or the same title/artist pair — and reported with a warning modal.
- **Missing File Flagging**: Tracks whose file disappeared (or whose drive is offline) are flagged in the list instead of being deleted from the library.
- **Tag & Metadata Editor**: In-app modal dialog to view and update track metadata (Title, Artist, Album, Genre, Year) and embed/replace/remove cover art via TagLibSharp directly in files. Validates Year (empty → unknown; invalid input shows an inline error and blocks save) and shows a TrackRow-style toast on success.
- **In-List Play Controls**: Play and pause songs directly from library rows, or use the dedicated Now Playing sidebar with a seek progress slider and volume control.
- **Now Playing Controls**: Previous, Play/Pause, and Next buttons with Google Material Icons; volume slider inside the Now Playing panel.
- **Now Playing Subtitle**: Shows an appropriate prompt based on whether the library has tracks when nothing is playing.
- **Live Search**: Debounced (350ms) search bar at the top of the window filters the current view in memory by title, artist, album, and genre. Search is per view (All Songs / Albums / Artists / Album Artists / Genres) and never switches tabs. Each view shows a clarifying no-results message and a "Clear Field" button when a search returns nothing.
- **Reusable Track Rows**: A shared `TrackRow` control renders track rows consistently across All Songs, Albums, Album Artists, Artists, and Genres views, with a "NOW PLAYING" indicator and metadata (including year/unknown genre).
- **Context-Aware Boundary Toasts**: Next/Prev at list boundaries report which view hit the boundary (All Songs / Album / Artist Songs / Album Artist Songs / Genre).
- **Unknown Year Display**: Tracks with a missing year show "Unknown Year" in the track row and leave the edit field blank.
- **Context-Aware Sorting**: Each view has its own sort controls and valid sort options; list layers sort by name/artist/genre/year, while song layers sort by track fields.
- **Clear Search & No-Results States**: When a search returns no results, views show a clarifying message and a "Clear Field" button that resets the search and restores all rows.
- **Accurate Playback Indicators**: Track rows clear "NOW PLAYING" and revert to the play icon when a track ends or playback moves; selecting a new track marks only that row as playing.
- **Material Icons**: Google Material Icons (via `Material.Icons.Avalonia`) for navigation, playback, and UI controls.
- **Fluent Design System**: Responsive UI with dark/light theme adaptation using Avalonia's fluent resources and high-contrast dialog overlays.
- **Cross-Platform Audio Architecture**: Native ALSA playback on Linux (`AlsaOut`) and WASAPI on Windows (`WasapiPlayer`).

## Requirements

- **.NET 9 SDK** (e.g. .NET SDK 9.0.320+)
- **Linux Native Dependencies**:
  - `libasound2`
  - `libsndfile1`

## Getting Started

### Build

> If `dotnet --version` prints 8.x, use the SDK 9 shim at `~/.local/bin/dotnet` (see `AGENTS.md`).

```bash
dotnet build TuneVault.csproj
```

### Run

```bash
dotnet run --project TuneVault.csproj
```

### Test

```bash
cd TuneVault.Tests && dotnet test
```

## Tech Stack

- **Framework**: Avalonia UI 11.3.12 (.NET 9)
- **MVVM**: CommunityToolkit.Mvvm 8.4.2
- **Audio Output & Decoding**: NAudio 3.3.0 (`NAudio.Core`, `NAudio.Alsa`, `NAudio.SoundFile`, `NAudio.Wasapi`)
- **Metadata & ID3 Tags**: TagLibSharp 2.3.0
- **Persistence**: SQLite via `Microsoft.Data.Sqlite` 9.0.22
- **Icons**: Material.Icons.Avalonia 2.4.1
- **Testing**: xUnit + Avalonia.Headless.XUnit + Moq

## Persistence

- Library is stored in `library.db` under the per-user application data folder (`~/.config/TuneVault/library.db` on Linux).
- Schema is versioned (`PRAGMA user_version`, currently 2) and created/migrated by `ILibraryRepository.InitializeAsync()` on startup.
- Duplicate rules are enforced by the database (same file path, or same title/artist). Imports report per-row outcomes.
- Removing a track deletes the row and its cover art, never the file on disk.

## Architecture & Code Structure

- `Program.cs` / `App.axaml.cs`: Application entry point and composition root.
- `ViewModels/MainWindowViewModel.cs`: Thin window shell; owns tab navigation and exposes the feature ViewModels.
- `ViewModels/LibraryViewModel.cs`: Track collection, selection, imports, duplicate/delete confirmations, search, album/artist grouping, and in-memory filtering.
- `ViewModels/PlaybackViewModel.cs`: Audio transport, now playing state, progress, volume, and Next/Previous navigation.
- `ViewModels/MetadataEditorViewModel.cs`: Tag/cover-art editor modal and metadata saving.
- `Models/Track.cs`: Observable domain model for tracks (incl. AlbumArtist, EffectiveAlbumArtist).
- `Models/AlbumSummary.cs`: Read-model for album tiles (name, track count, cover art).
- `Models/ArtistSummary.cs`: Read-model for artist tiles (name, track count, cover art).
- `Services/`:
  - `IAudioPlayer` / `NaudioAudioPlayer`: Audio engine abstraction.
  - `IAudioMetadataReader` / `SoundFileMetadataReader`: TagLibSharp and SoundFileReader metadata parser (incl. AlbumArtist).
  - `IFilePickerService` / `AvaloniaFilePickerService`: Audio and image file selection.
  - `ILibraryRepository` / `SqliteLibraryRepository`: SQLite library storage, duplicate rules, schema v2 (incl. album_artist), and migrations.
  - `LibraryKeys`: Path/identity normalization shared by imports and the database.
- `Views/`:
  - `MainWindow.axaml`: Main window layout with header, search bar, navigation sidebar, dynamic content, now playing sidebar, and modal overlays.
  - `AllSongsView.axaml`, `AlbumsView.axaml`, `AlbumArtistsView.axaml`, `ArtistsView.axaml`, `GenresView.axaml`: Library view tabs.
  - `TrackRow.axaml`: Reusable track row control shared across views (cover, title, artist/album, play, duration, edit, NOW PLAYING indicator).
- `App.axaml`: Application styles and shared resources (including `TrackRowTemplate` and `AlbumRowTemplate`).
- `TuneVault.Tests/`: xUnit test project (Models, Services, ViewModels, Views smoke tests).
```