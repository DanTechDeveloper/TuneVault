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

- `Program.Main` → `App.OnFrameworkInitializationCompleted` is the composition root.
- ViewModels split by feature; `MainWindowViewModel` is a thin shell. Views bind nested paths (`Library.*`, `Playback.*`, `Editor.*`).
- `MainWindow` owns the 250 ms `DispatcherTimer` → `RefreshPlayback()`. No Avalonia controls/timers/storage-provider types in `ViewModels/`.
- Compiled bindings enabled project-wide. Every new window and `DataTemplate` needs `x:DataType`.
- UI actions use `[RelayCommand]`; ViewModels inherit `ObservableObject` and must be `partial`. Avalonia has no `CommandManager`: use `[NotifyCanExecuteChangedFor]` or `command.NotifyCanExecuteChanged()`.

## Audio and metadata constraints

- Use **NAudio 3** APIs. Always decode with `SoundFileReader` (libsndfile). `AudioFileReader`/`Mp3FileReader` are Windows-only in NAudio 3.
- Output: `AlsaOut` on Linux, `WasapiPlayer` on Windows; macOS throws `PlatformNotSupportedException`.
- Supported: WAV, MP3, FLAC, OGG/Vorbis, Opus, AIFF. Metadata via TagLibSharp. File dialogs via `IFilePickerService`.

## Persistence

- SQLite via `ILibraryRepository`/`SqliteLibraryRepository`. DB: `library.db` in `Environment.SpecialFolder.ApplicationData` + `TuneVault`.
- Never reference `Microsoft.Data.Sqlite`/`SqliteConnection` from `ViewModels/` or `Models/`.
- Schema versioned with `PRAGMA user_version` (currently 2), created by `InitializeAsync()`, idempotent. v1→v2 adds `album_artist` (backfilled from `artist`).
- Duplicate rules: `tracks.path_key` UNIQUE; `tracks.identity_key` UNIQUE (lower-cased title+artist, NULL for `"Untitled track"`). Imports insert the batch in one transaction and report per-row `AddOutcome`.
- Cover art in `track_art` (`ON DELETE CASCADE`). Removals delete the row (and art), never the file. Metadata edits write file tags *and* DB; collisions rejected before tags written.
- `Track.IsMissing` computed at load from `File.Exists`, never persisted.
- All Songs ordering is SQL-side (`LoadAllAsync(sortBy, descending)`). `ReloadSortedAsync` rebuilds `Tracks` but reuses `Track` instances by `Id` so `PlaybackViewModel`'s loaded-track reference stays valid.
- Search and album/artist/genre filtering are in-memory; `_allTracks` loaded once. `SearchText` debounces 350 ms and filters in memory. Search is per view, never switches tabs.

## Views / layers

- Albums: list → filtered songs. Artists: artist list → all artist songs. Album Artists: artist list → artist detail (albums + all-songs entry) → album songs / all songs. Genres: genre list → all genre songs. Tab switching resets drill-down state.
- Tab switching resets `SelectedAlbum`, `SelectedArtist`, `SelectedGenre` to Layer 1.

## Reusable UI

- `Views/TrackRow.axaml` shared track row, used via `TrackRowTemplate` in `App.axaml`. Command bindings use `$parent[Window]` to reach `MainWindowViewModel`.
- `AlbumRowTemplate` in `App.axaml` renders album tiles.
- Material Icons via `Material.Icons.Avalonia` 2.4.1; styles in `App.axaml`. Icon-only buttons have transparent backgrounds.
- Sort controls live in each view header, context-aware via `Library.SortFields`/`Library.SortContext`.
- `App.axaml` holds design tokens. Views use `{DynamicResource ...Brush}` for colors and `{StaticResource FontSize...}` for text sizes.

## Playback navigation

- Next/Prev follows the queue captured when the current track was loaded (so switching views does not hijack playback); when nothing loaded, falls back to the current view's song list. Boundary presses show a toast instead of wrapping.
- No wrap-around; Previous restarts the current track at the first track.
- Auto-advances to the next track in the captured queue when a track ends; stops (with a "Last track" toast) at the end of the queue. No wrap-around.
- `_loadedQueue` is captured at play time; on a sort change in the **same** view it is rebuilt from `CurrentQueue` so Next/Prev follow the new sort. Captured queue is not touched when sorting a different view.
- Boundary toasts are context-aware (coarse labels: All Songs / Album / Artist Songs / Album Artist Songs / Genre).

## Sorting

- `TrackSortField` is the shared sort key. `Library.SortFields` is context-aware and changes with `SortContext`/`CurrentTab`.
- `ReloadSortedAsync` refreshes every collection but reuses `Track` instances by `Id`.
- Summary collections are rebuilt on refresh; selection is reselected by key.
- Missing album/genre/year sort last in both directions; then the chosen column, then title (always ascending).
- `SortContext.GenresList` → Genre only; `AlbumArtistAlbums` → Title + Year; `AlbumArtistTracks` → Title, Album, Genre; `AlbumArtistAllTracks` → Title, Album, Genre, Year.

## Testing

- Test project: `TuneVault.Tests/` (xUnit + Avalonia.Headless.XUnit + Moq).
- Run: `cd TuneVault.Tests && dotnet test`
- Single test: `cd TuneVault.Tests && dotnet test --filter "FullyQualifiedName~<TestName>"`
- Main project excludes `TuneVault.Tests/` via `Compile Remove`.

## Gotchas

- `_allTracks` is the source for in-memory rebuilds. `SetTrackPlayingState` must update it too, and `Remove` must drop the track from it.
- `ClearAllPlayingState()` (LibraryViewModel) clears `IsPlaying` on every track instance held anywhere; call it on playback transitions.
- Summary collections are immutable records rebuilt on refresh; reselect by key instead of reusing instances.
- `TrackRow` uses `$parent[Window]`; if it fails at runtime, resolve the Window DataContext in code-behind.
- Search has no-results borders with a "Clear Field" button (`ClearSearchCommand`, aliases `ClearSearchText`/`ClearField`).
- App theme is `Dark` in `App.axaml`.
- Do not edit generated `obj/`/`bin/` artifacts.
- `Track` overrides `Equals`/`GetHashCode` by `Id`.
- `ApplyTrackMetadata` (LibraryViewModel) propagates edits (including `AlbumArtist`) to every instance of a track and rebuilds summary collections; call it after mutating a track.
- Metadata editor validates `EditYear` (empty → 0/unknown; otherwise must parse as uint ≤ 9999). Invalid input sets `EditYearError` and aborts save without closing the modal. `Year == 0` displays as "Unknown Year" in `GenreDisplay`.
- Metadata editor writes the `AlbumArtists` tag from `track.AlbumArtist` (falling back to the new artist) on save.
- Unknown fallbacks: artist uses `"Unknown Artist"`; album/genre stored as `null`, normalized to `"Unknown Album"`/`"Unknown Genre"` in views; search treats null album/genre as their bucket names. Title fallback is `"Untitled track"`. Year `0` = unknown.
- Global `FontFamily` is set via `MainWindow.FontFamily`.
- Use `{DynamicResource ...}` (not `{StaticResource}`) for token lookups inside views/styles.
- Modal scrims use `#88000000`; slider track/thumb use `#40FFFFFF`/`#FFFFFF`.
- `ArtistSummary` first layer (Album Artists view) shows `AlbumCount` ("1 Album" / "N Albums"), not track count.
- Removal deletes by `Id` from every collection (`Tracks`, `AlbumTracks`, `ArtistSongs`, `GenreSongs`, `_allTracks`), so it works during search/filter or from another view.
- Toast overlay width 320–460 to avoid text overlap.
- `IsAlbumArtistsTab` (not `IsAlbumArtists`) is the LibraryViewModel flag for the Album Artists tab.
