using Avalonia.Headless.XUnit;
using Moq;
using System;
using System.IO;
using System.Threading;
using TuneVault.Models;
using TuneVault.Services;
using TuneVault.ViewModels;
using Xunit;

namespace TuneVault.Tests.ViewModels;

public class PlaybackLyricsTests
{
    private static (LibraryViewModel library, PlaybackViewModel playback, Mock<IAudioPlayer> player,
        Mock<ILyricsService> lyrics, Track track, string dbPath, string tempFile) Create(
        LyricsResult lyricsResult)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tunevault-lyrics-{Guid.NewGuid()}.db");
        var repo = new SqliteLibraryRepository(dbPath);
        repo.InitializeAsync().GetAwaiter().GetResult();
        var library = new LibraryViewModel(new SoundFileMetadataReader(), new AvaloniaFilePickerService(() => null), repo);

        var tempFile = Path.Combine(Path.GetTempPath(), $"tunevault-lyrics-audio-{Guid.NewGuid()}.mp3");
        File.WriteAllText(tempFile, "dummy");
        var track = new Track
        {
            FilePath = tempFile,
            Title = "Song",
            Artist = "Artist",
            Duration = TimeSpan.FromSeconds(100),
        };
        repo.AddRangeAsync(new[] { track }).GetAwaiter().GetResult();
        library.LoadLibraryAsync().GetAwaiter().GetResult();

        var position = TimeSpan.Zero;
        var player = new Mock<IAudioPlayer>();
        player.Setup(p => p.Load(It.IsAny<string>())).Returns(TimeSpan.FromSeconds(100));
        player.Setup(p => p.IsPlaying).Returns(false);
        player.Setup(p => p.Position).Returns(() => position);
        player.Setup(p => p.Duration).Returns(TimeSpan.FromSeconds(100));

        var lyrics = new Mock<ILyricsService>();
        lyrics.Setup(s => s.LoadAsync(It.IsAny<Track>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lyricsResult);

        var playback = new PlaybackViewModel(player.Object, library, null, lyrics.Object);
        return (library, playback, player, lyrics, track, dbPath, tempFile);
    }

    private static void Cleanup(string dbPath, string tempFile)
    {
        foreach (var path in new[] { tempFile, dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static LyricsResult TwoLines() => new(
        new[]
        {
            new LyricLine(TimeSpan.Zero, "First"),
            new LyricLine(TimeSpan.FromSeconds(10), "Second"),
        },
        true,
        "lrc");

    [AvaloniaFact]
    public async Task OpenLyrics_LoadsLines_AndCloseTogglesView()
    {
        var (_, playback, _, _, track, dbPath, tempFile) = Create(TwoLines());
        try
        {
            playback.PlayTrackCommand.Execute(track);
            await playback.OpenLyricsCommand.ExecuteAsync(null);

            Assert.True(playback.IsLyricsView);
            Assert.True(playback.HasLyrics);
            Assert.Equal(2, playback.Lyrics.Count);
            Assert.Equal("First", playback.ActiveLyricLine!.Text);

            playback.CloseLyricsCommand.Execute(null);
            Assert.False(playback.IsLyricsView);
        }
        finally { Cleanup(dbPath, tempFile); }
    }

    [AvaloniaFact]
    public async Task RefreshPlayback_MovesActiveLineWithPosition()
    {
        var (_, playback, _, _, track, dbPath, tempFile) = Create(TwoLines());
        try
        {
            playback.PlayTrackCommand.Execute(track);
            await playback.OpenLyricsCommand.ExecuteAsync(null);

            // Mock player position comes from the closure captured in Create.
            playback.RefreshPlayback();
            Assert.Equal("First", playback.ActiveLyricLine!.Text);

            // Seek to the second (last) line's timestamp via the slider-bound property.
            playback.PositionSeconds = 10;
            Assert.Equal("Second", playback.ActiveLyricLine!.Text);
            Assert.True(playback.Lyrics[1].IsActive);
            Assert.False(playback.Lyrics[0].IsActive);
        }
        finally { Cleanup(dbPath, tempFile); }
    }

    [AvaloniaFact]
    public async Task UpdateActiveLyric_WrapsToFirstLineAfterLast()
    {
        var lines = new[]
        {
            new LyricLine(TimeSpan.Zero, "First"),
            new LyricLine(TimeSpan.FromSeconds(10), "Second"),
            new LyricLine(TimeSpan.FromSeconds(20), "Last"),
        };
        var (_, playback, _, _, track, dbPath, tempFile) = Create(new LyricsResult(lines, true, "lrc"));
        try
        {
            playback.PlayTrackCommand.Execute(track);
            await playback.OpenLyricsCommand.ExecuteAsync(null);

            playback.PositionSeconds = 20;
            Assert.Equal("Last", playback.ActiveLyricLine!.Text);

            // Past the last timestamp, the next line is the first again.
            playback.PositionSeconds = 25;
            Assert.Equal("First", playback.ActiveLyricLine!.Text);

            // And it keeps cycling through the block.
            playback.PositionSeconds = 35;
            Assert.Equal("Second", playback.ActiveLyricLine!.Text);
            playback.PositionSeconds = 45;
            Assert.Equal("Last", playback.ActiveLyricLine!.Text);
        }
        finally { Cleanup(dbPath, tempFile); }
    }

    [AvaloniaFact]
    public async Task OpenLyrics_NoLyrics_ShowsStatus()
    {
        var (_, playback, _, _, track, dbPath, tempFile) = Create(LyricsResult.Empty);
        try
        {
            playback.PlayTrackCommand.Execute(track);
            await playback.OpenLyricsCommand.ExecuteAsync(null);

            Assert.True(playback.IsLyricsView);
            Assert.False(playback.HasLyrics);
            Assert.Contains("No lyrics", playback.LyricsStatusText);
        }
        finally { Cleanup(dbPath, tempFile); }
    }
}
