# TuneVault

Avalonia desktop local-music player. Single project, .NET 9, MVVM with service interfaces.

## Build and run

- SDK pinned to **9.0.320** by `global.json`. `/usr/bin/dotnet` may be 8; use the shim at `~/.local/bin/dotnet` so `dotnet --version` prints `9.0.320`.
- IDE `NETSDK1045`/`CS0012` (`System.Runtime 8.0.0.0`) means its language server picked SDK 8. Trust `dotnet build`, then restart the C# language server.
- Build: `dotnet build TuneVault.csproj`
- Run: `dotnet run --project TuneVault.csproj` (opens a window, blocks)
- Linux playback needs `libasound2` and `libsndfile1`. Other native requirements: `docs/AUDIO.md`. Implicit state machines/invariants: `docs/STATE_MACHINES.md`.

## Wiring

- `Program.Main` → `App.OnFrameworkInitializationCompleted` is the composition root; it resolves the Last.fm key, builds `LastFmService`, and passes it to `MetadataEditorViewModel(filePicker, library, audiometa)`.
- ViewModels split by feature; `MainWindowViewModel` is a thin shell. Views bind nested paths (`Library.*`, `Playback.*`, `Editor.*`).
- `MainWindow` owns the 250 ms `DispatcherTimer` → `RefreshPlayback()` and the 6 s toast timer. No Avalonia controls/timers/storage-provider types in `ViewModels/`.
- Compiled bindings enabled project-wide. Every new window and `DataTemplate` needs `x:DataType`.
- UI actions use `[RelayCommand]`; ViewModels inherit `ObservableObject` and must be `partial`. Avalonia has no `CommandManager`: use `[NotifyCanExecuteChangedFor]` or `command.NotifyCanExecuteChanged()`.

## Audio, metadata, online lookup

- Use **NAudio 3** APIs. Always decode with `SoundFileReader` (libsndfile). `AudioFileReader`/`Mp3FileReader` are Windows-only in NAudio 3.
- Output: `AlsaOut` on Linux, `WasapiPlayer` on Windows; macOS throws `PlatformNotSupportedException`.
- Supported: WAV, MP3, FLAC, OGG/Vorbis, Opus, AIFF. Metadata via TagLibSharp. File dialogs via `IFilePickerService`.
- `IAudiometaService`/`LastFmService` is optional online lookup. Key resolution in `App.axaml.cs`: `AUDIOMETA_API_KEY`, then `LastFm:ApiKey` in gitignored `appsettings.local.json` (also checked via `LASTFM_API_KEY`). No key/network must degrade to empty results, never block manual editing; tests mock the service (no network).

## Persistence

- SQLite via `ILibraryRepository`/`SqliteLibraryRepository`. DB: `library.db` in `Environment.SpecialFolder.ApplicationData` + `TuneVault`.
- Never reference `Microsoft.Data.Sqlite`/`SqliteConnection` from `ViewModels/` or `Models/`.
- Schema versioned with `PRAGMA user_version` (currently 2), created by `InitializeAsync()`, idempotent. v1→v2 adds `album_artist` (backfilled from `artist`).
- Duplicate rules: `tracks.path_key` UNIQUE; `tracks.identity_key` UNIQUE (lower-cased title+artist, NULL for `"Untitled track"`). Imports insert the batch in one transaction and report per-row `AddOutcome`.
- Cover art in `track_art` (`ON DELETE CASCADE`). Removals delete the row (and art), never the file. Metadata edits write file tags *and* DB; collisions rejected before tags written.
- `Track.IsMissing` computed at load from `File.Exists`, never persisted.
- All Songs ordering is SQL-side (`LoadAllAsync(sortBy, descending)`). `ReloadSortedAsync` rebuilds views but reuses `Track` instances by `Id` so `PlaybackViewModel`'s loaded-track reference stays valid.
- Search and album/artist/genre filtering are in-memory from `_allTracks` (loaded once). Search never switches tabs. Applying a term debounces 350 ms; **clearing is inline** and raises `SearchCleared` exactly once (`ReloadSortedAsync(fromSearch: true)` flag guards against a stale debounce post).

## Views / layers

- Albums: list → filtered songs. Artists: artist list → all artist songs. Album Artists: artist list → detail (albums + all-songs entry) → album songs / all songs. Genres: genre list → all genre songs. Tab switching resets drill-down state (`SelectedAlbum`, `SelectedArtist`, `SelectedGenre`) to Layer 1.
- Search filters summary lists by name, so playing/searched tracks collapse album/artist detail; clearing reselects album/artist/genre from `SelectedTrack`. `CurrentQueue` can therefore change purely from a search clear — account for this in queue logic and tests.

## Reusable UI

- `Views/TrackRow.axaml` (via `TrackRowTemplate`) and `Views/AudiometaTrackRow.axaml` (via `AudiometaTrackRowTemplate`) in `App.axaml`. Both use `$parent[Window]` to reach `MainWindowViewModel`.
- `AlbumRowTemplate` in `App.axaml` renders album tiles.
- Material Icons via `Material.Icons.Avalonia` 2.4.1; icon-only buttons have transparent backgrounds.
- Sort controls live in each view header, context-aware via `Library.SortFields`/`Library.SortContext`.
- `App.axaml` holds design tokens. Views use `{DynamicResource ...Brush}` for colors and `{StaticResource FontSize...}` for text sizes.

## Playback navigation

- Next/Prev follows the queue captured when the track was loaded, so browsing other views does not hijack playback; with nothing loaded it falls back to `CurrentQueue`. Boundary presses toast instead of wrapping.
- No wrap-around; Previous at the first track restarts it. Auto-advance happens only when a track ends and uses the same queue/label/toast path as Next.
- Track end is detected only in `RefreshPlayback()`: `wasPlaying && !IsPlaying && Position >= Duration - 500 ms`. A manual seek to the exact end counts as finished.
- Keep `SetPosition(0)` **before** `DurationSeconds = ...` in every load branch. `Slider.Maximum` binds to `DurationSeconds` and `Value` TwoWay to `PositionSeconds`; shrinking the maximum while the value sits at the old track's end writes that end back as a seek, so the new track instantly finishes and skips a song.
- `_loadedQueue` is a snapshot; a sort change in the **same** view rebuilds it from `CurrentQueue` (source guard `_loadedQueueSource == CurrentQueueSource`). Never rebuild on sorting a different view.
- On `SearchCleared`, rebuild `_loadedQueue` from the restored `CurrentQueue` so Next/Prev leave the old filtered results. Guards, all pure no-ops otherwise: no loaded track, `CurrentTab != _loadedQueueTab` (explicit tab switch), or loaded track's `Id` absent from the restored collection. Never call `Load`/`Seek`/`Stop` or change `_loadedTrack`/`NowPlayingTrack` here.
- Do **not** use the queue-source *string* as the cross-view guard for search clear: reselects during the clear change it (e.g. `AllSongs` → `Album`) even without user navigation. `CurrentTab` only changes on explicit tab switches.
- Boundary toasts are context-aware (coarse labels: All Songs / Album / Artist Songs / Album Artist Songs / Genre).

## Sorting

- `TrackSortField` is the shared key. `Library.SortFields` changes with `SortContext`/`CurrentTab`.
- Changing tab or drill-down resets sort to that context's first `SortFields` entry, ascending (`ResetSortToDefault()`); `_isResettingSort` suppresses reloads from those setter changes. Do not let a sort from one view leak into another.
- Missing album/genre/year sort last in both directions, then the chosen column, then title (always ascending).
- `SortContext.GenresList` → Genre only; `AlbumArtistAlbums` → Title + Year; `AlbumArtistTracks` → Title, Album, Genre; `AlbumArtistAllTracks` → Title, Album, Genre, Year.

## Testing

- `TuneVault.Tests/` (xUnit + Avalonia.Headless.XUnit + Moq), `[AvaloniaFact]`, headless. Run from that directory — there is no solution file, so root `dotnet test` does not work.
- Build/run: `cd TuneVault.Tests && dotnet test`; one test: `dotnet test --filter "FullyQualifiedName~<TestName>"`.
- Main project excludes tests via `Compile Remove`; a stray `.cs` anywhere in the main project breaks build (`**/*.cs` glob). Never edit `obj/`/`bin/`.
- Playback tests mock `IAudioPlayer` (`Position`/`Duration`/`IsPlaying`) and call `Playback.RefreshPlayback()` directly. Online-lookup tests mock `IAudiometaService`. Focused suites: `PlaybackSearchContextTests`, `TabSortResetTests`, `MetadataEditorAudiometaTests`.

## Gotchas

- `_allTracks` is the source for in-memory rebuilds. `SetTrackPlayingState` must update it too, and `Remove` must drop the track from it and every view collection (by `Id`).
- `ClearAllPlayingState()` clears `IsPlaying` on every track instance held anywhere; call it on playback transitions.
- Summary collections are immutable records rebuilt on refresh; reselect by key instead of reusing instances.
- `Track` has **no** `Equals`/`GetHashCode` override (reference equality); match by `Id` explicitly — different instances with the same `Id` are possible.
- `ApplyTrackMetadata` propagates edits (incl. `AlbumArtist`) to every instance and rebuilds summaries; call it after mutating a track.
- Metadata editor validates `EditYear` (empty → 0/unknown; else uint ≤ 9999). Invalid input sets `EditYearError` and blocks save without closing. `Year == 0` shows "Unknown Year". It writes the `AlbumArtists` tag from `track.AlbumArtist` (fallback: new artist).
- Unknown fallbacks: `"Unknown Artist"`; album/genre stored `null`, normalized to `"Unknown Album"`/`"Unknown Genre"` in views and search; title `"Untitled track"`; year `0`.
- `TrackRow`/`AudiometaTrackRow` use `$parent[Window]`; if it fails, resolve the Window DataContext in code-behind.
- `ClearSearchCommand` has no `ClearSearchText`/`ClearField` aliases — views bind `Library.ClearSearchCommand` directly.
- App theme is `Dark`; global `FontFamily` via `MainWindow.FontFamily`. Use `{DynamicResource ...}` (not `{StaticResource}`) for tokens inside views/styles.
- Modal scrims `#88000000`; slider track/thumb `#40FFFFFF`/`#FFFFFF`; toast overlay 320–460 wide.
- `ArtistSummary` first layer (Album Artists) shows `AlbumCount`, not track count. `IsAlbumArtistsTab` (not `IsAlbumArtists`) is the LibraryViewModel Album Artists flag.
