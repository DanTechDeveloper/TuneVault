# Local audio playback

TuneVault plays local files through **NAudio 3**, with a different output backend per OS.

## Requirements

- **.NET 9 SDK** — NAudio 3 requires `net9.0` (the project targets it).
  On this machine it is installed at `~/.dotnet` and added to `PATH` in
  `~/.zshrc` / `~/.bashrc`. Open a new terminal after changing those files.
- **NAudio 3 packages** — `NAudio.Core`, `NAudio.Alsa`, `NAudio.SoundFile`,
  `NAudio.Wasapi`.

### Linux

```bash
sudo apt install libasound2 libsndfile1
```

Playback uses `AlsaOut`; file decoding uses `SoundFileReader` (libsndfile).
With PulseAudio/PipeWire, ALSA's `default` device routes through the sound server.

### Windows

WASAPI (`WasapiPlayer`) is used for output. File decoding still goes through
`SoundFileReader`, so **libsndfile must be available** (for example from the
official libsndfile builds or vcpkg) and its DLL must be findable at runtime.

### macOS

Not wired up yet: NAudio 3 has no CoreAudio output backend. The app will throw
`PlatformNotSupportedException` when playback starts.

## Supported formats

WAV, AIFF, FLAC, OGG/Vorbis, Opus and MP3 — whatever the installed
libsndfile build can decode.

## Architecture

```text
MainWindow (View) ──bindings/commands──▶ MainWindowViewModel
                                                │
                                                ├── IAudioPlayer         (NaudioAudioPlayer)
                                                ├── IAudioMetadataReader (SoundFileMetadataReader)
                                                └── IFilePickerService   (AvaloniaFilePickerService)
```

- `IAudioPlayer` hides NAudio output details from the ViewModel.
- `IFilePickerService` keeps Avalonia's storage provider out of the ViewModel.
- `MainWindow` runs a 250 ms `DispatcherTimer` that calls
  `MainWindowViewModel.RefreshPlayback()`; the ViewModel remains free of
  Avalonia timer/UI types.
