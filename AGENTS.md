# TuneVault

Avalonia desktop local-music player. Single project, .NET 9, MVVM with service interfaces.

## Build and run

- Requires the **.NET 9 SDK**. `/usr/bin/dotnet` may be 8; a 9.0.320 SDK lives at `~/.dotnet` with a shim at `~/.local/bin/dotnet`. `dotnet --version` should print `9.0.320`.
- If the IDE reports `NETSDK1045`/`CS0012` (`System.Runtime 8.0.0.0`), its language server picked SDK 8. Trust `dotnet build`, then restart the C# language server.
- Build: `dotnet build TuneVault.csproj`
- Run: `dotnet run --project TuneVault.csproj` (opens a window, blocks)
- Tests: `cd TuneVault.Tests && dotnet test`
- Single test: `cd TuneVault.Tests && dotnet test --filter "FullyQualifiedName~<TestName>"`
- Linux playback needs `libasound2` and `libsndfile1`. Other native requirements: `docs/AUDIO.md`.

## Wiring

- `Program.Main` → `App.OnFrameworkInitializationCompleted` is the composition root: constructs audio/metadata/file-picker services, `ILibraryRepository` (`SqliteLibraryRepository`), and feature ViewModels (`LibraryViewModel`, `PlaybackViewModel`, `MetadataEditorViewModel`), injects them into `MainWindowViewModel` and `Views/MainWindow`. Schema is created before any command runs; `MainWindow.OnOpened` kicks off `LibraryViewModel.LoadLibraryAsync()`.
- ViewModels split by feature, not by view file. `MainWindowViewModel` is a thin shell owning navigation and exposing `Library`, `Playback`, `Editor`; views bind nested paths (`Library.Tracks`, `Playback.NowPlayingTitle`, `Editor.EditTitle`).
- `MainWindow` owns the 250 ms `DispatcherTimer` calling `MainWindowViewModel.RefreshPlayback()` → `PlaybackViewModel`. No Avalonia controls, timers, or storage-provider types in `ViewModels/`.
- Compiled bindings are enabled project-wide. Every new window and `DataTemplate` needs `x:DataType` or bindings fail at build time.
- UI actions use `[RelayCommand]` (CommunityToolkit.Mvvm); ViewModels inherit `ObservableObject` and must be `partial`. Avalonia has no `CommandManager`: use `[NotifyCanExecuteChangedFor]` or call `command.NotifyCanExecuteChanged()`.

## Audio and metadata constraints

- Use **NAudio 3** APIs (`NAudio.Core`/`Alsa`/`SoundFile`/`Wasapi`). Do not downgrade to NAudio 2.
- Always decode with `SoundFileReader` (libsndfile). `AudioFileReader`/`Mp3FileReader` are Windows-only in NAudio 3.
- Output is `AlsaOut` on Linux, `WasapiPlayer` on Windows; macOS throws `PlatformNotSupportedException`.
- Supported formats: WAV, MP3, FLAC, OGG/Vorbis, Opus, AIFF.
- Metadata read/write uses **TagLibSharp** (`TagLib.File.Create`).
- File dialogs go through `IFilePickerService`.

## Persistence

- Library stored in **SQLite** via `ILibraryRepository`/`SqliteLibraryRepository` (`Microsoft.Data.Sqlite`). DB: `library.db` in `Environment.SpecialFolder.ApplicationData` + `TuneVault` (`~/.config/TuneVault/library.db` on Linux).
- Never reference `Microsoft.Data.Sqlite` or `SqliteConnection` from `ViewModels/` or `Models/`.
- Schema versioned with `PRAGMA user_version` (currently 2), created by `InitializeAsync()`, idempotent. v1→v2 adds `album_artist` (backfilled from `artist`).
- Duplicate rules enforced by the database: `tracks.path_key` UNIQUE; `tracks.identity_key` UNIQUE (lower-cased title+artist, NULL for the `"Untitled track"` fallback). Imports insert the batch in one transaction and report per-row `AddOutcome`.
- Cover art lives in `track_art` (`ON DELETE CASCADE`).
- Removals delete the row (and its art), never the file on disk. Metadata edits write file tags *and* DB; an edit that would collide is rejected before tags are written.
- `Track.IsMissing` computed at load from `File.Exists`, never persisted.
- All Songs ordering is SQL-side (`LoadAllAsync(sortBy, descending)`). `LibraryViewModel.ReloadSortedAsync` rebuilds `Tracks` but reuses existing `Track` instances by `Id` so `PlaybackViewModel`'s loaded-track reference stays valid.
- Search and album/artist/genre filtering are **in-memory**; `_allTracks` loaded once. `SearchText` debounces 350 ms (`System.Threading.Timer`) and filters in memory. Search is **per view** and never switches tabs.
- Albums view: list → filtered songs. Artists view: artist list → all artist songs. Album Artists view: artist list → artist detail (albums + all-songs entry) → album songs/all songs. Genres view: genre list → all genre songs. Grouping uses `Track.EffectiveAlbumArtist` for album artists.
- Tab switching resets drill-down state (`SelectedAlbum`, `SelectedArtist`, `SelectedGenre`) to Layer 1.

## Reusable UI

- `Views/TrackRow.axaml` is the shared track row (cover, title, artist/album, genre/year, play, duration, edit, NOW PLAYING indicator), used via `TrackRowTemplate` in `App.axaml`. Command bindings use `$parent[Window]` to reach `MainWindowViewModel`.
- `AlbumRowTemplate` in `App.axaml` renders album tiles.
- Google Material Icons via `Material.Icons.Avalonia` 2.4.1; styles registered in `App.axaml`. Button backgrounds are transparent for icon-only buttons.
- Sort controls live in each view header and are context-aware through `Library.SortFields`/`Library.SortContext`.

## Playback navigation

- Next/Prev follows the **current view's song list** (AllSongs → `Tracks`, album detail → `AlbumTracks`, artist detail → `ArtistSongs`, genre detail → `GenreSongs`); non-song views fall back to `Tracks`.
- No wrap-around at boundaries; Previous restarts the current track at the first track.
- No auto-advance on track end (empty "track finished" branch stays as-is).

## Sorting

- `TrackSortField` is the shared sort key. `Library.SortFields` is context-aware and changes with `SortContext`/`CurrentTab`.
- List layers sort by their natural keys (name/artist/genre/year) with direction. Track layers use `SortTracks` with `SortField`.
- `ReloadSortedAsync` refreshes every collection but reuses `Track` instances by `Id`.
- Summary collections are rebuilt on refresh; selection is reselected by key to avoid collapsing to Layer 1 during search/sort.

## Testing

- Test project: `TuneVault.Tests/` (xUnit + Avalonia.Headless.XUnit + Moq).
- Run: `cd TuneVault.Tests && dotnet test`
- Main project excludes `TuneVault.Tests/` via `Compile Remove` in `TuneVault.csproj`.
- Covers Models, Services, ViewModels, and XAML smoke tests for compiled bindings.
- Edge cases: duplicate detection, missing files, unknown album/artist/genre, search filtering, in-memory reload, navigation boundaries, playback indicators.

## Gotchas

- `_allTracks` is the source for in-memory rebuilds. `SetTrackPlayingState` must update it too, and `Remove` must drop the track from it, or stale rows/reappearances occur.
- `ClearAllPlayingState()` (LibraryViewModel) clears `IsPlaying` on every track instance held anywhere; call it on playback transitions to avoid stale "NOW PLAYING" indicators.
- Summary collections are immutable records rebuilt on refresh; reselect by key instead of reusing instances.
- `TrackRow` uses `$parent[Window]` to reach `MainWindowViewModel`; if it fails at runtime, resolve the Window DataContext in code-behind.
- Search has no-results borders with a "Clear Field" button (`ClearSearchCommand`, aliases `ClearSearchText`/`ClearField`) in All Songs/Albums/Album Artists/Artists/Genres views.
- App theme is set to `Dark` in `App.axaml`.
- Do not edit generated `obj/`/`bin/` artifacts.