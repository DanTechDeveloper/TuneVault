using System;
using NAudio.SoundFile;
using NAudio.Wave;
using NAudio.Wave.Alsa;

namespace TuneVault.Services;

/// <summary>
/// NAudio 3 playback: ALSA on Linux and WASAPI on Windows.
/// </summary>
public sealed class NaudioAudioPlayer : IAudioPlayer
{
    private WaveStream? _reader;
    private IWavePlayer? _output;
    private float _volume = 0.8f;
    private bool _disposed;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            ApplyVolume();
        }
    }

    public TimeSpan Load(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DisposePlayback();

        var reader = new SoundFileReader(filePath);
        var output = CreateOutputDevice();

        try
        {
            output.Init(reader);
        }
        catch
        {
            output.Dispose();
            reader.Dispose();
            throw;
        }

        _reader = reader;
        _output = output;
        ApplyVolume();
        return reader.TotalTime;
    }

    public void Play()
    {
        if (_output is null || _reader is null)
        {
            return;
        }

        if (_reader.Length > 0 && _reader.Position >= _reader.Length)
        {
            _reader.Position = 0;
        }

        _output.Play();
    }

    public void Pause() => _output?.Pause();

    public void Stop()
    {
        if (_output is null)
        {
            return;
        }

        _output.Stop();

        if (_reader is { CanSeek: true })
        {
            _reader.Position = 0;
        }
    }

    public void Seek(TimeSpan position)
    {
        if (_reader is null)
        {
            return;
        }

        var clamped = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > _reader.TotalTime
                ? _reader.TotalTime
                : position;

        _reader.CurrentTime = clamped;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposePlayback();
        _disposed = true;
    }

    private void DisposePlayback()
    {
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
    }

    private void ApplyVolume()
    {
        switch (_output)
        {
            case AlsaOut alsa when OperatingSystem.IsLinux():
                alsa.Volume = _volume;
                break;
            case WasapiPlayer wasapi when OperatingSystem.IsWindows():
                wasapi.Volume = _volume;
                break;
        }
    }

    private static IWavePlayer CreateOutputDevice()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WasapiPlayerBuilder().Build();
        }

        if (OperatingSystem.IsLinux())
        {
            return new AlsaOut();
        }

        throw new PlatformNotSupportedException(
            "TuneVault audio playback currently supports Windows and Linux.");
    }
}
