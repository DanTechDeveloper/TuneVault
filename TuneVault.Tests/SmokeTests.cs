using Avalonia.Headless.XUnit;
using Xunit;

namespace TuneVault.Tests;

public class SmokeTests
{
    [AvaloniaFact]
    public void AvaloniaHeadless_Works()
    {
        // Just verify headless platform initializes.
        Assert.True(true);
    }
}