# TuneVault

Avalonia 11 local music player, .NET 9, MVVM with service interfaces. One app project plus `TuneVault.Tests/`.

## Build / test

- SDK pinned to **9.0.320** (`global.json`). If `dotnet --version` prints 8, use the shim at `~/.local/bin/dotnet`. IDE `NETSDK1045`/`CS0012` (`System.Runtime 8.0.0.0`) is a stale language server: trust `dotnet build`, then restart it.
- Build: `dotnet build TuneVault.csproj`. Run: `dotnet run --project TuneVault.csproj` (opens a window, blocks).
- Tests: `cd TuneVault.Tests && dotnet test`; one test: `dotnet test --filter "FullyQualifiedName~<TestName>"`. No solution file — root `dotnet test` does nothing.
- The main project excludes tests via `<Compile Remove="TuneVault.Tests\**\*.cs">` but also globs `**/*.cs`: a stray `.cs` anywhere in the main project breaks the build. Never edit `obj/`/`bin/`.
- Linux playback needs `libasound2` and `libsndfile1`. Native details: `docs/AUDIO.md`. Implicit state machines/invariants: `docs/STATE_MACHINES.md`.

## Wiring / conventions

- Composition root: `Program.Main` → `App.OnFrameworkInitializationCompleted` (resolves the Last.fm key, builds `LastFmService`, passes it to `MetadataEditorViewModel(filePicker, library, audiometa)`).
- ViewModels split by feature; `MainWindowViewModel` is a thin shell. Views bind nested `Library.*` / `Playback.*` / `Editor.*`. No Avalonia controls, timers, or SQLite types in `ViewModels/`.
- Compiled bindings are on project-wide: every new window and `DataTemplate` needs `x:DataType`.
- ViewModels are `partial ObservableObject`; UI actions are `[RelayCommand]`. Avalonia has no `CommandManager` — use `[NotifyCanExecuteChangedFor]` / `command.NotifyCanExecuteChanged()`.
- Toasts live on `LibraryViewModel` (`ShowToast`, `Toast*`); `MainWindow` owns the 6 s timer. Setting `ShowToast = true` while already true raises no change, so toast helpers bounce false→true to restart the timer.
- Lyrics are file-based via `ILyricsService`: sidecar `.lrc` (same folder/base name, also `.LRC`) then embedded `TagLib.Tag.Lyrics`; no database column and no network. `LrcParser` handles all LRC timestamp forms, offsets, repeated stamps, and strips enhanced word tags.

## Audio / metadata / lookup

- NAudio 3: decode only with `SoundFileReader` (libsndfile); `AudioFileReader`/`Mp3FileReader` are Windows-only. Output `AlsaOut` on Linux, `WasapiPlayer` on Windows; macOS throws `PlatformNotSupportedException`.
- Tags via TagLibSharp; file dialogs via `IFilePickerService`.
- Last.fm key order in `App.axaml.cs`: `AUDIOMETA_API_KEY`, then `LastFm:ApiKey` in gitignored `appsettings.local.json` (`LASTFM_API_KEY` also checked). No key/network ⇒ empty results, never block manual editing; tests mock `IAudiometaService` (no network).

## Persistence

- `ILibraryRepository` / `SqliteLibraryRepository`; `library.db` in the project folder (beside `TuneVault.csproj`), resolved by `DefaultDatabasePath()` from the current working directory with a `TUNEVAULT_DB_PATH` override. Run the app from the project root or it creates an empty DB elsewhere; DB files are git-ignored and `.devdbrc` must track the same path. Never reference `Microsoft.Data.Sqlite`/`SqliteConnection` from `ViewModels/` or `Models/`.
- `InitializeAsync()` is idempotent and migrates via `PRAGMA user_version` (currently 4): v2 `album_artist` (backfilled from `artist`), v3 `playback_state` (single row: track/position/shuffle), v4 `tracks.is_favorite` (default 0). An additive migration must tolerate the column already existing on a fresh DB.
- `tracks.path_key` UNIQUE; `tracks.identity_key` UNIQUE (lower-cased title+artist, NULL for `"Untitled track"`). Imports insert one transaction and report per-row `AddOutcome`.
- `track_art` cascades on delete; removals delete the DB row and art, never the file. `Track.IsMissing` is computed from disk at load, never persisted.
- All Songs ordering is SQL-side (`LoadAllAsync(sortBy, descending)`); other filtering/sorting is in-memory from `_allTracks` (loaded once). `ReloadSortedAsync` reuses `Track` instances by `Id` so playback's loaded-track reference stays valid. `SetFavoriteAsync` touches only the favorite column; `UpdateAsync` leaves it untouched.

## State invariants (hard-won)

- Next/Prev follow the queue **captured when the track loaded**, not the current view; boundary presses toast instead of wrapping. Auto-advance only at track end, through the same path. No wrap-around; Previous at the first track restarts it.
- Track end is detected **only** in `RefreshPlayback()`: `wasPlaying && !IsPlaying && Position >= Duration - 500 ms`. A manual seek to the exact end counts as finished.
- Keep `SetPosition(0)` **before** `DurationSeconds = ...` in every load branch: `Slider.Maximum` shrinks while `Value` still holds the old track's end, and that writes back a seek so the new track instantly finishes and skips a song.
- `_loadedQueue` is a snapshot. A sort change rebuilds it only when `_loadedQueueSource == CurrentQueueSource` (same view). `SearchCleared` rebuilds from the restored `CurrentQueue` and is otherwise a pure no-op: no loaded track, `CurrentTab != _loadedQueueTab`, or loaded track's `Id` absent from the restored collection. Never `Load`/`Seek`/`Stop` or change the loaded track there. Do **not** use the queue-source *string* as the cross-view guard — reselects change it without navigation.
- Queue sources/labels: All Songs, Favorites, Album, Artist Songs, Album Artist Songs, Genre. `CurrentQueue` picks from the active view; Favorites is a flat tab backed by `FavoriteTracks`.
- Search never switches tabs. Applying a term debounces 350 ms; clearing is inline and raises `SearchCleared` exactly once. Search also filters summary lists by name, so playing/searched tracks collapse album/artist detail; clearing reselects from `SelectedTrack`.
- Tab switch or drill-down resets sort to that context's first `SortFields` entry, ascending; `_isResettingSort` suppresses reloads from the reset. Missing album/genre/year sort last in both directions, then the column, then title ascending. `FavoritesTracks` uses the full field set.
- `ToggleFavoriteAsync` writes the DB first, then propagates by `Id` and refreshes `FavoriteTracks`; a failed write leaves the UI unchanged. Toggling never calls `Load`/`Seek`/`Stop` and does not rewrite the loaded queue.
- Lyrics ride the existing 250 ms `RefreshPlayback()` tick: `UpdateActiveLyric` flips `IsActive` only when the line index changes. Loading a different track clears lines and reloads them if the lyrics view is open. Past the last line's timestamp the block loops back so the next line is the first again — this is lyrics-only, not the track queue's no-wrap rule.

## UI / views

- Albums / Artists / Album Artists / Genres drill down; tab switching resets `SelectedAlbum`, `SelectedArtist`, `SelectedGenre` to Layer 1. Favorites is flat with its own content and sort controls (`Views/FavoritesView.axaml` mirrors `AllSongsView`).
- `TrackRow` (via `TrackRowTemplate`) is the shared library row: heart, play/pause, duration, ⋮, NOW PLAYING. `AudiometaTrackRow` is only for Last.fm results — not a library track, so it has no favorite/heart.
- Now Playing swaps between its default content and `Views/LyricsView.axaml` via `Playback.IsLyricsView`; the lyrics button is in the track-info row because the transport row is already full at 760 px. Lyric text styling lives in that view (style token references use `{DynamicResource}`); the active line advances down the list and wraps back to the first only after the last line; `AutoScrollToSelectedItem` is off: `LyricsView.axaml.cs` anchors the active line by measuring its realized container and setting `ScrollViewer.Offset` on a layout pass. The Now Playing background cover art blur goes 50 → 110 via the `Border.now-playing.lyrics Image.cover-bg` style in `MainWindow.axaml` (`Classes.lyrics` binds `Playback.IsLyricsView`).
- Material Icons via `Material.Icons.Avalonia`. Use `{DynamicResource ...Brush}` for tokens and `{StaticResource FontSize...}` for text; theme is Dark, global `FontFamily` from `MainWindow`. Exception: inside a `<Style>` Setter, App.axaml tokens must use `{DynamicResource ...}` — `StaticResource` throws `KeyNotFoundException` in headless XAML smoke tests.
- `$parent[Window].((vm:MainWindowViewModel)DataContext)` is how row templates reach shell commands; if it fails, resolve the Window DataContext in code-behind.
- `ClearSearchCommand` has no `ClearSearchText`/`ClearField` aliases — bind it directly. Toast is bottom-right; modal scrims `#88000000`.

## Gotchas

- `Track` has **no** `Equals`/`GetHashCode` — reference equality; match by `Id` explicitly. `SetTrackPlayingState`, `SetFavoriteState`, `ClearAllPlayingState`, `ApplyTrackMetadata`, and `Remove` must cover `_allTracks` plus every view collection (including `FavoriteTracks`); `Remove` drops rows by `Id`.
- Summary collections are immutable records rebuilt on refresh: reselect by key, don't reuse instances. `ApplyTrackMetadata` propagates edits (incl. `AlbumArtist`) and rebuilds summaries; call it after mutating a track.
- Unknown fallbacks: `"Unknown Artist"`, `"Untitled track"`, `Year == 0` → "Unknown Year"; album/genre stored `null`, normalized to `"Unknown Album"`/`"Unknown Genre"` in views and search.
- Metadata editor validates `EditYear` (empty → 0; else uint ≤ 9999): invalid sets `EditYearError` and blocks save without closing. It writes the `AlbumArtists` tag from `track.AlbumArtist` (fallback: new artist).
- `ArtistSummary` on the first Album Artists layer shows `AlbumCount`, not track count. The LibraryViewModel flag is `IsAlbumArtistsTab` (shell property is `IsAlbumArtists`).
- Tests are headless `[AvaloniaFact]`; playback tests mock `IAudioPlayer` and call `RefreshPlayback()` directly. Focused suites: `PlaybackSearchContextTests`, `TabSortResetTests`, `PlaybackShuffleTests`, `FavoriteTracksTests`, `MetadataEditorAudiometaTests`, `PlaybackLyricsTests`, `LrcParserTests`, `LyricsViewTests`.
