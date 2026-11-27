using System.Linq;
using TuneVault.Services;
using Xunit;

namespace TuneVault.Tests.Services;

public class LrcParserTests
{
    [Fact]
    public void Parse_PlainLrc_OrdersLinesByTime()
    {
        var result = LrcParser.Parse("[00:12.34]Second\n[00:05.00]First");

        Assert.True(result.IsSynced);
        Assert.Equal(5d, result.Lines[0].Time.TotalSeconds, 3);
        Assert.Equal(new[] { "First", "Second" }, result.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Parse_AllTimestampForms_AreSupported()
    {
        var result = LrcParser.Parse(
            "[00:01.5]a\n[00:02:34]b\n[00:03.345]c\n[00:04]d\n[1:05.25]e");

        Assert.True(result.IsSynced);
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, result.Lines.Select(l => l.Text));
        Assert.Equal(1.5d, result.Lines[0].Time.TotalSeconds, 3);
        Assert.Equal(2.34d, result.Lines[1].Time.TotalSeconds, 3);
        Assert.Equal(3.345d, result.Lines[2].Time.TotalSeconds, 3);
        Assert.Equal(65.25d, result.Lines[4].Time.TotalSeconds, 3);
    }

    [Fact]
    public void Parse_Offset_ShiftsEveryLine()
    {
        var result = LrcParser.Parse("[offset:500]\n[00:10.00]Line");

        Assert.Equal(10.5d, result.Lines[0].Time.TotalSeconds, 3);
        Assert.Equal("Line", result.Lines[0].Text);
    }

    [Fact]
    public void Parse_MultipleStamps_ExpandsIntoSeparateLines()
    {
        var result = LrcParser.Parse("[00:45.42][00:49.42]Chorus");

        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, line => Assert.Equal("Chorus", line.Text));
    }

    [Fact]
    public void Parse_MetadataTags_AreIgnored()
    {
        var result = LrcParser.Parse("[ti:Song]\n[ar:Artist]\n[length:3:20]\n[00:01.00]Line");

        Assert.Single(result.Lines);
        Assert.Equal("Line", result.Lines[0].Text);
    }

    [Fact]
    public void Parse_EnhancedWordTags_AreStrippedFromDisplay()
    {
        var result = LrcParser.Parse("[00:12.34]<00:12.34>I <00:12.60>love <00:12.95>you");

        Assert.Equal("I love you", result.Lines[0].Text);
    }

    [Fact]
    public void Parse_UnsyncedText_BecomesOneStaticLine()
    {
        var result = LrcParser.Parse("[ti:Song]\nJust words\non two lines");

        Assert.False(result.IsSynced);
        Assert.Single(result.Lines);
        Assert.Equal("Just words\non two lines", result.Lines[0].Text);
    }

    [Fact]
    public void Parse_Empty_ReturnsNoLines()
    {
        Assert.Empty(LrcParser.Parse("").Lines);
    }
}
