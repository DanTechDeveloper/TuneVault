# TuneVault

Avalonia desktop local-music player. Single project, .NET 9, MVVM with service interfaces.

## Build and run

- Requires the **.NET 9 SDK**. The system `/usr/bin/dotnet` is 8.0.131 and cannot build this project. A 9.0.320 SDK lives at `~/.dotnet` with a shim at `~/.local/bin/dotnet`; run `dotnet --version` and expect `9.0.320`.
- If the IDE reports `NETSDK1045` or `CS0012` (`System.Runtime, Version=8.0.0.0`), its language server picked SDK 8. Trust `dotnet build`; verify the SDK version, then restart the C# language server.
- Build: `dotnet build TuneVault.csproj`
- Run: `dotnet run --project TuneVault.csproj` (opens a desktop window and blocks)
- Tests: `cd TuneVault.Tests && dotnet test` (xUnit + Avalonia.Headless). Main project excludes `TuneVault.Tests/` via `Compile Remove` in `TuneVault.csproj`.
- No linter or formatter config. Verify with a clean build and a manual app run.
- Linux playback needs `libasound2` and `libsndfile1`. Other native requirements: `docs/AUDIO.md`.

## Wiring

- Entry point `Program.Main` → `App.OnFrameworkInitializationCompleted` is the composition root: constructs audio/metadata/file-picker services, `ILibraryRepository` (`SqliteLibraryRepository`), and feature ViewModels (`LibraryViewModel`, `PlaybackViewModel`, `MetadataEditorViewModel`), injects them into `MainWindowViewModel` and `Views/MainWindow`. Schema created there before any command runs; `MainWindow.OnOpened` kicks off `LibraryViewModel.LoadLibraryAsync()`.
- ViewModels split by feature, not by view file. `MainWindowViewModel` is a thin shell owning navigation and exposing `Library`, `Playback`, `Editor`; views bind nested paths (`Library.Tracks`, `Playback.PlayPauseLabel`, `Editor.EditTitle`).
- `LibraryViewModel` owns `Tracks`, `SelectedTrack`, imports, duplicate/delete confirmations, raises `TrackRemoved`. `PlaybackViewModel` reads the selected track, owns `IAudioPlayer` state, stops when the loaded track is removed. `MetadataEditorViewModel` owns the tag/cover-art modal and delegates deletion to `LibraryViewModel`.
- Views live in `Views/`, not the project root. Dynamic views: `AllSongsView`, `AlbumsView`, `AlbumArtistsView`, `ArtistsView`, `GenresView`, bound to `MainWindowViewModel` tabs. `AlbumsView` and `AlbumArtistsView` are two-layer: list → filtered songs.
- `MainWindow` owns the 250 ms `DispatcherTimer` calling `MainWindowViewModel.RefreshPlayback()` → `PlaybackViewModel`. No Avalonia controls, timers, or storage-provider types in `ViewModels/`.
- UI actions use `[RelayCommand]` from CommunityToolkit.Mvvm; ViewModels inherit `ObservableObject` and must be `partial`. Avalonia has no `CommandManager`: use `[NotifyCanExecuteChangedFor]` on observable state, or call `command.NotifyCanExecuteChanged()` when a `CanExecute` input is a plain field.
- `[ObservableProperty]` generates public properties; use `On<Name>Changed` partial hooks for side effects. Keep a property manual when its value must be clamped/coerced before storage.
- Models needing UI reactivity (e.g. `Models.Track`) inherit `ObservableObject` so tag/cover edits update bindings across views.
- In-window modal overlays (loading, duplicate audio warning, metadata tag editor) live in `Views/MainWindow.axaml` and use `DynamicResource` for light/dark contrast.
- Compiled bindings are enabled project-wide. Every new window and `DataTemplate` needs `x:DataType` or bindings fail at build time.

## Audio & metadata constraints

- Use **NAudio 3** APIs (`NAudio.Core`/`Alsa`/`SoundFile`/`Wasapi`). Do not downgrade to NAudio 2 (Windows-only playback).
- Always decode with `SoundFileReader` (libsndfile). `AudioFileReader`/`Mp3FileReader` are Windows-only in NAudio 3.
- Output is `AlsaOut` on Linux and `WasapiPlayer` on Windows; macOS playback is unsupported and throws `PlatformNotSupportedException`.
- Supported formats: WAV, MP3, FLAC, OGG/Vorbis, Opus, AIFF. Windows deployments must also ship libsndfile.
- Metadata read & write uses **TagLibSharp** (`TagLib.File.Create`) for title, artist, album, genre, year, embedded cover art.
- File dialogs go through `IFilePickerService` (`PickAudioFilesAsync`, `PickAudioFolderAsync`, `PickImageFileAsync`).

## Persistence

- Library stored in **SQLite** through `ILibraryRepository` / `SqliteLibraryRepository` (`Microsoft.Data.Sqlite`). DB is `library.db` in `Environment.SpecialFolder.ApplicationData` + `TuneVault` (`~/.config/TuneVault/library.db` on Linux).
- Never reference `Microsoft.Data.Sqlite` or `SqliteConnection` from `ViewModels/` or `Models/`.
- Schema versioned with `PRAGMA user_version` (currently 2), created by `InitializeAsync()`, idempotent. v1→v2 migration adds `album_artist` column (backfilled from `artist`).
- Duplicate rules enforced by the database, not in-memory:
  - `tracks.path_key` UNIQUE (absolute path, lower-cased on Windows).
  - `tracks.identity_key` UNIQUE (lower-cased `title`+`artist`, NULL for the `"Untitled track"` fallback so untitled files never collide). A second file with the same title/artist is hard-skipped.
  - Imports insert the whole batch in one transaction and report per-row `AddOutcome` (`Added`, `DuplicatePath`, `DuplicateIdentity`, `Failed`).
- Cover art lives in `track_art` side table (`ON DELETE CASCADE`).
- Removals delete the row (and its art) but never the file on disk. Metadata edits write to the file's tags *and* the DB; an edit that would collide with another track's identity key is rejected before tags are written.
- `Track.IsMissing` computed at load from `File.Exists`, never persisted. Missing files flagged in UI, never pruned.
- All Songs ordering is SQL-side (`LoadAllAsync(sortBy, descending)`), not in memory. `LibraryViewModel.ReloadSortedAsync` rebuilds `Tracks` from the query but reuses existing `Track` instances by `Id` so `PlaybackViewModel`'s loaded-track reference stays valid.
- Search and album/artist filtering are **in-memory** (YAGNI; library is small). `LibraryViewModel` keeps `_allTracks` loaded once; `SearchText` debounces (350ms, `System.Threading.Timer`) and filters `_allTracks` in memory. Search is **per view**: it filters the current tab's list (Tracks / Albums / Artists / album or artist songs) and never switches tabs. Search bar lives at the top of `MainWindow` (after header), live per keystroke.
- Albums view is two-layer: album list (grouped in memory, incl. "Unknown Album" for null/empty `album`) → filtered songs per album. `AlbumSummary` read-model in `Models/`.
- Album Artists view is four-layer: artist list → artist detail (all-songs entry + album list) → all-artist-songs / album-songs. `ArtistSummary` read-model in `Models/`. Grouping uses `Track.EffectiveAlbumArtist` (AlbumArtist tag, falling back to Artist).
- Tab switching resets drill-down state (`SelectedAlbum`, `SelectedArtist`) to Layer 1 so the view always lands on the list, not a stale detail.

## Reusable UI

- `Views/TrackRow.axaml` is the shared track row (cover, title, artist/album, play, duration, edit, NOW PLAYING indicator). Used by `AllSongsView`, `AlbumsView`, and `AlbumArtistsView` via `TrackRowTemplate` in `App.axaml` resources. Its command bindings use `$parent[Window]` to reach `MainWindowViewModel` so the control works in any view. If `$parent[Window]` fails to resolve at runtime, fall back to resolving the Window DataContext in `TrackRow.axaml.cs`.
- `AlbumRowTemplate` in `App.axaml` renders album tiles (cover + name + track count) for the Albums and AlbumArtists views.

## Icons

- Google Material Icons via `Material.Icons.Avalonia` (2.4.1). Styles registered in `App.axaml` (`<materialIcons:MaterialIconStyles />`). Use `<materialIcons:MaterialIcon Kind="..." />` for icons or `{materialIcons:MaterialIconTextExt Kind=..., Text=...}` for icon+text. Sidebar nav buttons use MaterialIconTextExt. Button backgrounds are transparent for icon-only buttons.
- Now Playing panel: Previous (SkipPrevious), Play/Pause (PlayCircle/PauseCircle), Next (SkipNext) — all transparent icon buttons.

## Playback navigation

- Next/Prev follows the **current view's song list** (view-based): AllSongs → `Tracks`, album detail → `AlbumTracks`, artist detail → `ArtistSongs`. Non-song list views fall back to `Tracks`.
- No wrap-around at boundaries: Next is a no-op at the last track; Previous restarts the current track at the first track.
- No auto-advance on track end (existing empty "track finished" branch stays as-is).

## Testing

- Test project: `TuneVault.Tests/` (xUnit + Avalonia.Headless.XUnit + Moq).
- Run: `cd TuneVault.Tests && dotnet test`
- Main project excludes `TuneVault.Tests/` via `Compile Remove` in `TuneVault.csproj`.
- Tests cover: Models (Track, AlbumSummary, ArtistSummary), Services (SqliteLibraryRepository), ViewModels (LibraryViewModel, PlaybackViewModel, MetadataEditorViewModel), Views (XAML smoke tests for compiled bindings).
- Edge cases: duplicate detection, missing files, unknown album/artist, search filtering, in-memory reload, navigation boundaries.

## References

- `docs/AUDIO.md` — native dependencies and playback architecture.
```