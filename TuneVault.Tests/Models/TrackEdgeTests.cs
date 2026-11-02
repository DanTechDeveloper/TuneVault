using Avalonia.Headless.XUnit;
using TuneVault.Models;
using Xunit;

namespace TuneVault.Tests.Models;

public class TrackEdgeTests
{
    [AvaloniaFact]
    public void DurationDisplay_Zero_ReturnsDash()
    {
        var t = new Track { Duration = TimeSpan.Zero };
        Assert.Equal("—", t.DurationDisplay);
    }

    [AvaloniaFact]
    public void DurationDisplay_SecondsOnly()
    {
        var t = new Track { Duration = TimeSpan.FromSeconds(45) };
        Assert.Equal("0:45", t.DurationDisplay);
    }

    [AvaloniaFact]
    public void DurationDisplay_MinutesAndSeconds()
    {
        var t = new Track { Duration = TimeSpan.FromSeconds(125) };
        Assert.Equal("2:05", t.DurationDisplay);
    }

    [AvaloniaFact]
    public void DurationDisplay_Hours()
    {
        var t = new Track { Duration = TimeSpan.FromSeconds(3661) };
        Assert.Equal("1:01:01", t.DurationDisplay);
    }

    [AvaloniaFact]
    public void CoverArt_Null_WhenNoPictureData()
    {
        var t = new Track { PictureData = null };
        Assert.Null(t.CoverArt);
    }

    [AvaloniaFact]
    public void CoverArt_Null_WhenEmptyPictureData()
    {
        var t = new Track { PictureData = Array.Empty<byte>() };
        Assert.Null(t.CoverArt);
        Assert.False(t.HasCoverArt);
    }

    [AvaloniaFact]
    public void CoverArt_CachesAfterFirstAccess()
    {
        var t = new Track { PictureData = new byte[] { 0, 1, 2, 3 } };
        var first = t.CoverArt;
        var second = t.CoverArt;
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public void CoverArt_ResetsOnPictureDataChange()
    {
        var t = new Track { PictureData = new byte[] { 0, 1, 2, 3 } };
        _ = t.CoverArt; // trigger cache (null due to invalid)
        t.PictureData = new byte[] { 5, 6, 7 };
        // cache reset; HasCoverArt true
        Assert.True(t.HasCoverArt);
    }

    [AvaloniaFact]
    public void IsMissing_DefaultFalse()
    {
        var t = new Track();
        Assert.False(t.IsMissing);
    }

    [AvaloniaFact]
    public void PlayPauseToolTip_ReflectsState()
    {
        var t = new Track { IsPlaying = false };
        Assert.Equal("Play track", t.PlayPauseToolTip);
        t.IsPlaying = true;
        Assert.Equal("Pause track", t.PlayPauseToolTip);
    }
}