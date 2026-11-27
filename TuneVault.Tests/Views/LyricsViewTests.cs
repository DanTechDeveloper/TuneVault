using TuneVault.Views;
using Xunit;

namespace TuneVault.Tests.Views;

public class LyricsViewTests
{
    [Fact]
    public void AnchorOffset_KeepsActiveLineAtAnchor_AndClamps()
    {
        Assert.Equal(60, LyricsView.AnchorOffset(0, 100, 40, 400));
        Assert.Equal(0, LyricsView.AnchorOffset(0, 10, 40, 400));
        Assert.Equal(400, LyricsView.AnchorOffset(0, 900, 40, 400));
    }
}
