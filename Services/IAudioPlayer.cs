using System;

namespace TuneVault.Services;

/// <summary>
/// Playback boundary for the ViewModel. Implementations may use NAudio, a fake,
/// or another backend without changing the UI or ViewModel.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    float Volume { get; set; }

    /// <summary>Loads a local audio file and returns its duration.</summary>
    TimeSpan Load(string filePath);

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);
}
