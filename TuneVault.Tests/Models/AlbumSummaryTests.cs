using Avalonia.Headless.XUnit;
using TuneVault.Models;
using Xunit;

namespace TuneVault.Tests.Models;

public class AlbumSummaryTests
{
    [AvaloniaFact]
    public void HasCoverArt_False_WhenNull()
    {
        var a = new AlbumSummary("Album", 3, null);
        Assert.False(a.HasCoverArt);
        Assert.Null(a.CoverArt);
    }

    [AvaloniaFact]
    public void HasCoverArt_False_WhenEmpty()
    {
        var a = new AlbumSummary("Album", 3, Array.Empty<byte>());
        Assert.False(a.HasCoverArt);
    }

    [AvaloniaFact]
    public void HasCoverArt_True_WhenBytesPresent()
    {
        // Invalid bytes → CoverArt null but HasCoverArt true
        var a = new AlbumSummary("Album", 3, new byte[] { 1, 2, 3 });
        Assert.True(a.HasCoverArt);
    }

    [AvaloniaFact]
    public void Properties_SetCorrectly()
    {
        var a = new AlbumSummary("Name", 5, null);
        Assert.Equal("Name", a.Name);
        Assert.Equal(5, a.TrackCount);
    }
}