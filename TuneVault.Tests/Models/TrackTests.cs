using Avalonia.Headless.XUnit;
using TuneVault.Models;
using Xunit;

namespace TuneVault.Tests.Models;

public class TrackTests
{
    [AvaloniaFact]
    public void EffectiveAlbumArtist_FallsBackToArtist_WhenAlbumArtistNull()
    {
        var t = new Track { Artist = "Artist A", AlbumArtist = null };
        Assert.Equal("Artist A", t.EffectiveAlbumArtist);
    }

    [AvaloniaFact]
    public void EffectiveAlbumArtist_FallsBackToArtist_WhenAlbumArtistWhitespace()
    {
        var t = new Track { Artist = "Artist A", AlbumArtist = "   " };
        Assert.Equal("Artist A", t.EffectiveAlbumArtist);
    }

    [AvaloniaFact]
    public void EffectiveAlbumArtist_UsesAlbumArtist_WhenSet()
    {
        var t = new Track { Artist = "Track Artist", AlbumArtist = "Album Artist" };
        Assert.Equal("Album Artist", t.EffectiveAlbumArtist);
    }

    [AvaloniaFact]
    public void ArtistAlbum_ReturnsArtistOnly_WhenAlbumNull()
    {
        var t = new Track { Artist = "A", Album = null };
        Assert.Equal("A", t.ArtistAlbum);
    }

    [AvaloniaFact]
    public void ArtistAlbum_ReturnsArtistOnly_WhenAlbumWhitespace()
    {
        var t = new Track { Artist = "A", Album = "   " };
        Assert.Equal("A", t.ArtistAlbum);
    }

    [AvaloniaFact]
    public void ArtistAlbum_Combines_WhenAlbumSet()
    {
        var t = new Track { Artist = "A", Album = "B" };
        Assert.Equal("A - B", t.ArtistAlbum);
    }

    [AvaloniaFact]
    public void PlayPauseIconKind_ReturnsPause_WhenPlaying()
    {
        var t = new Track { IsPlaying = true };
        Assert.Equal(Material.Icons.MaterialIconKind.PauseCircle, t.PlayPauseIconKind);
    }

    [AvaloniaFact]
    public void PlayPauseIconKind_ReturnsPlay_WhenNotPlaying()
    {
        var t = new Track { IsPlaying = false };
        Assert.Equal(Material.Icons.MaterialIconKind.PlayCircle, t.PlayPauseIconKind);
    }

    [AvaloniaFact]
    public void HasCoverArt_False_WhenNoPicture()
    {
        var t = new Track { PictureData = null };
        Assert.False(t.HasCoverArt);
    }

    [AvaloniaFact]
    public void HasCoverArt_True_WhenPicturePresent()
    {
        var t = new Track { PictureData = new byte[] { 1, 2, 3 } };
        Assert.True(t.HasCoverArt);
    }
}