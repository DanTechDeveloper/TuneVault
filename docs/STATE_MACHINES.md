# TuneVault — Implicit State Machines

This document records the state machines that are currently encoded in
booleans/nullable fields rather than explicit state types. Treat the
invariants here as part of the public contract when changing the VMs.

## SM1 Playback lifecycle (`PlaybackViewModel`)
Fields: `_loadedTrack`, `NowPlayingTrack`, `_loadedQueue`, `IsPlaying`,
`_suppressSeek`, `PositionSeconds`, `DurationSeconds`.

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
when sort changes in the same view the track was played from.

## SM8 Track missingness
`Track.IsMissing` is computed from disk at load and on play; never persisted.

## SM9 Cover art cache
`Track._coverArt` is lazily decoded and reset when `PictureData` changes.
