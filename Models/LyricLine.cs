using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TuneVault.Models;

/// <summary>
/// One lyric line. Unsynced lyrics are stored as a single line at TimeSpan.Zero.
/// </summary>
public sealed partial class LyricLine : ObservableObject
{
    public LyricLine(TimeSpan time, string text)
    {
        Time = time;
        Text = text;
    }

    public TimeSpan Time { get; }

    public string Text { get; }

    [ObservableProperty]
    private bool _isActive;
}
