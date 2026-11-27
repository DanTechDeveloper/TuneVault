# TuneVault — Implicit State Machines

This document records the state machines that are currently encoded in
booleans/nullable fields rather than explicit state types. Treat the
invariants here as part of the public contract when changing the VMs.

## SM1 Playback lifecycle (`PlaybackViewModel`)
Fields: `_loadedTrack`, `NowPlayingTrack`, `_loadedQueue`, `_unshuffledQueue`,
`IsShuffle`, `IsPlaying`, `_suppressSeek`, `PositionSeconds`, `DurationSeconds`.

States:
- `Idle`: `_loadedTrack == null`, no audio loaded.
- `LoadedPlaying`: loaded, `IsPlaying == true`.
- `LoadedPaused`: loaded, `IsPlaying == false`, position < end.
- `Completed`: `_loadedTrack == null`, `NowPlayingTrack != null`, slider at 0
  (track ended with no next track).
- `MissingFile`: play attempted on a missing file; `IsMissing == true`,
  no load.

Invariants:
- `_loadedTrack` and `NowPlayingTrack` are set together on load; only the
  `Completed` state clears `_loadedTrack` while keeping `NowPlayingTrack`.
- `SetPosition(0)` must run before `DurationSeconds = ...` on every load
  branch (slider max/value feedback).
- Teardown goes through `ResetPlaybackState(unloadTrack, clearDisplay)` only.
- Removals match by `Id`, not by reference.
- Shuffle reorders `_loadedQueue` only; `_unshuffledQueue` holds the sort order
  restored when shuffle is turned off. Toggling shuffle never calls
  `Load`/`Seek`/`Stop`. Toggling a favorite is equally inert: it does not
  reload, seek, stop, or rewrite the loaded queue snapshot.
- `RestoreSessionAsync` re-enters `LoadedPaused` from the saved `playback_state`
  row (load + seek, never `Play`). `SaveSessionState` runs on window close and
  writes the last displayed track id, `PositionSeconds`, and `IsShuffle`; it
  no-ops when nothing is displayed so a close during startup keeps the row.

## SM2 View drill-down (`LibraryViewModel`)
Fields: `SelectedAlbum`, `SelectedArtist`, `SelectedArtistAlbum`,
`IsArtistAllSongsView`, `SelectedGenre`, `IsTabSwitching`, `IsArtistsTab`,
`IsAlbumArtistsTab`, `_isRefreshingSummaries`.

Invariants:
- While `_isRefreshingSummaries == true`, `OnSelectedAlbum/Artist/GenreChanged`
  no-op.
- Tab switching sets `IsTabSwitching = true`, clears all selections, then
  resets it to `false`.
- `SelectedArtist == null` ⇒ `ArtistAlbums`/`ArtistSongs` cleared.
- `SelectedAlbum == null` (and not tab-switching) ⇒ `AlbumTracks` cleared.
- Favorites is a flat tab: `FavoriteTracks` is derived from `_allTracks` by
  `IsFavorite`, has no drill-down, and is refreshed on load, sort, search,
  metadata edit, and favorite toggle.

## SM3 Import pipeline
Fields: `IsLoading`, `LoadingProgress`, `LoadingStatusText`, `_libraryLoaded`.
`LoadLibraryAsync` is idempotent via `_libraryLoaded`; on failure it resets
the flag so a retry is possible.

## SM4 Edit modal (`MetadataEditorViewModel`)
Fields: `IsEditMetadataModalOpen`, `EditingTrack`, edit fields, `EditYearError`,
`_editCoverArtChanged`, `_pendingRemoval`, `ShowDeleteConfirmation`.
Removals match by `Id`.

## SM5 Search debounce
`_searchDebounce` is disposed and recreated on each keystroke; callback posts
to `_uiContext`. If no context is captured, it runs inline.

## SM6 Toast
`ShowToast` plus `Toast*` fields. Toasts are replaced, not queued.

## SM7 Sort
`SortField`, `IsSortDescending`, `SortContext`. `_loadedQueue` is rebuilt only
when sort changes in the same view the track was played from. Queue sources are
All Songs, Favorites, Album, Artist Songs, and Genre; Favorites uses the full
sort field set (Title, Artist, Album, Genre, Year).

## SM8 Track missingness
`Track.IsMissing` is computed from disk at load and on play; never persisted.

## SM9 Cover art cache
`Track._coverArt` is lazily decoded and reset when `PictureData` changes.

## SM10 Lyrics view
Fields: `IsLyricsView`, `Lyrics`, `ActiveLyricLine`, `_activeLyricIndex`,
`LyricsStatusText`.

Invariants:
- `OpenLyricsCommand` loads file-based lyrics (`ILyricsService`: sidecar `.lrc`
  then embedded plain lyrics) and opens the focused view; `CloseLyricsCommand`
  only hides it. No database row and no network are involved.
- The active line is derived from `PositionSeconds` during the existing 250 ms
  `RefreshPlayback()` tick and after seeks. `IsActive` flips only when the line
  index changes, so the 4 Hz tick does not thrash notifications.
- Loading a different track clears lines and, if the lyrics view is still open,
  reloads for that track. Unsynced text is one static, non-highlighted line.
