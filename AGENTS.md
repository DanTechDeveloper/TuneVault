# TuneVault

Avalonia desktop local-music player. Single project, .NET 9, MVVM with service interfaces.

## Build and run

- Requires the **.NET 9 SDK**. The system `/usr/bin/dotnet` is 8.0.131 and cannot build this project. A 9.0.320 SDK lives at `~/.dotnet` with a shim at `~/.local/bin/dotnet`; run `dotnet --version` and expect `9.0.320`.
- If the IDE reports `NETSDK1045` or `CS0012` (`System.Runtime, Version=8.0.0.0`), its language server picked SDK 8. Trust `dotnet build`; verify the SDK version, then restart the C# language server.
- Build: `dotnet build TuneVault.csproj`
- Run: `dotnet run --project TuneVault.csproj` (opens a desktop window and blocks)
- There is no test project, linter, or formatter config. Verify with a clean build and a manual app run.
- Linux playback needs `libasound2` and `libsndfile1` (both installed here). Other native requirements: `docs/AUDIO.md`.

## Wiring

- Entry point is `Program.Main` → `App.OnFrameworkInitializationCompleted`. That method is the composition root: it constructs the audio/metadata/file-picker services, the `ILibraryRepository` (`SqliteLibraryRepository`), and the feature ViewModels (`LibraryViewModel`, `PlaybackViewModel`, `MetadataEditorViewModel`), then injects them into `MainWindowViewModel` and `Views/MainWindow`. Keep object construction there. The schema is created there too, before any command can run; `MainWindow.OnOpened` then kicks off `LibraryViewModel.LoadLibraryAsync()`.
- ViewModels are split by feature, not by view file. `MainWindowViewModel` is a thin shell that owns navigation and exposes `Library`, `Playback`, and `Editor`; the views bind nested paths such as `Library.Tracks`, `Playback.PlayPauseLabel`, and `Editor.EditTitle`.
- `LibraryViewModel` owns `Tracks`, `SelectedTrack`, imports, duplicate/delete confirmations, and raises `TrackRemoved`. `PlaybackViewModel` reads the selected track, owns `IAudioPlayer` state, and stops when the loaded track is removed. `MetadataEditorViewModel` owns the tag/cover-art modal and delegates deletion to `LibraryViewModel`.
- The view lives in `Views/`, not the project root. Dynamic views include `AllSongsView`, `AlbumsView`, `ArtistsView`, and `GenresView`, bound to `MainWindowViewModel` tabs.
- `MainWindow` owns the 250 ms `DispatcherTimer` that calls `MainWindowViewModel.RefreshPlayback()`, which forwards to `PlaybackViewModel`. Do not put Avalonia controls, timers, or storage-provider types in `ViewModels/`.
- UI actions use `[RelayCommand]` from `CommunityToolkit.Mvvm`; ViewModels inherit `ObservableObject` and must be `partial`. Avalonia has no `CommandManager`: use `[NotifyCanExecuteChangedFor]` on observable state, or call `command.NotifyCanExecuteChanged()` when a `CanExecute` input is a plain field.
- `[ObservableProperty]` generates public properties; use `On<Name>Changed` partial hooks for side effects. Keep a property manual when its value must be clamped or otherwise coerced before storage.
- Models requiring UI reactivity (e.g. `Models.Track`) inherit `ObservableObject` so tag/cover edits update bindings dynamically across views.
- In-window modal overlays (loading, duplicate audio warning, metadata tag editor) live in `Views/MainWindow.axaml` and use `DynamicResource` (`SystemControlBackgroundAltHighBrush`, `SystemControlForegroundBaseLowBrush`) for light/dark theme contrast.
- Compiled bindings are enabled project-wide. Every new window and `DataTemplate` needs `x:DataType` or bindings fail at build time.

## Audio & Metadata constraints

- Use **NAudio 3** APIs (`NAudio.Core`/`Alsa`/`SoundFile`/`Wasapi`). Do not downgrade to NAudio 2: its playback is Windows-only.
- Always decode with `SoundFileReader` (libsndfile). `AudioFileReader`/`Mp3FileReader` are Windows-only in NAudio 3.
- Output is `AlsaOut` on Linux and `WasapiPlayer` on Windows; macOS playback is unsupported and throws `PlatformNotSupportedException`.
- Supported formats: WAV, MP3, FLAC, OGG/Vorbis, Opus, AIFF. Windows deployments must also ship libsndfile.
- Metadata read & write uses **TagLibSharp** (`TagLib.File.Create`) for title, artist, album, genre, year, and embedded cover art images.
- File dialogs go through `IFilePickerService` (`PickAudioFilesAsync`, `PickAudioFolderAsync`, `PickImageFileAsync`).

## Persistence

- The library is stored in **SQLite** through `ILibraryRepository` / `SqliteLibraryRepository` (`Microsoft.Data.Sqlite`). The database is `library.db` in `Environment.SpecialFolder.ApplicationData` + `TuneVault` (`~/.config/TuneVault/library.db` on Linux).
- Never reference `Microsoft.Data.Sqlite` or `SqliteConnection` from `ViewModels/` or `Models/`: ViewModels talk to `ILibraryRepository` and pass `Models.Track` directly.
- Schema is versioned with `PRAGMA user_version` (currently 1) and created by `InitializeAsync()`, which is idempotent and safe to call on every start.
- Duplicate rules are enforced by the database, not by the in-memory collection:
  - `tracks.path_key` is `UNIQUE` (absolute path, lower-cased on Windows).
  - `tracks.identity_key` is `UNIQUE` (lower-cased `title`+`artist`, `NULL` for the `"Untitled track"` fallback so untitled files never collide). A second file with the same title+artist is **hard-skipped**.
  - Imports insert the whole batch in one transaction and report per-row `AddOutcome` (`Added`, `DuplicatePath`, `DuplicateIdentity`, `Failed`).
- Cover art lives in the `track_art` side table (`ON DELETE CASCADE`), so `tracks` stays small. Foreign keys are enabled in the connection string.
- Removals delete the row (and its art) but never the file on disk. Metadata edits are written to the file's tags *and* to the database; an edit that would collide with another track's identity key is rejected before the tags are written.
- `Track.IsMissing` is computed at load from `File.Exists` and never persisted. Missing files are flagged in the UI and never pruned from the database, so an unmounted drive cannot wipe the library.
- All Songs ordering is SQL-side (`LoadAllAsync(sortBy, descending)`), not in memory. `LibraryViewModel.ReloadSortedAsync` rebuilds `Tracks` from the query but reuses existing `Track` instances by `Id` so `PlaybackViewModel`'s loaded-track reference stays valid.

## References

- `docs/AUDIO.md` — native dependencies and playback architecture.
