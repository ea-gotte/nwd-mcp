using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;

namespace Bimwright.Nwd.Tests;

public sealed class ColorSpecTests
{
    [Theory]
    [InlineData("#FF8000", "#FF8000")]
    [InlineData("ff8000", "#FF8000")]
    [InlineData("255, 128, 0", "#FF8000")]
    [InlineData("Red", "#FF0000")]
    [InlineData(" grey ", "#808080")]
    public void ParsesHexRgbAndNames(string raw, string hex)
    {
        Assert.True(ColorSpec.TryParse(raw, out var c, out _));
        Assert.Equal(hex, c.Hex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("256,0,0")]
    [InlineData("1,2")]
    [InlineData("chartreuse-ish")]
    public void RejectsBadColours(string raw)
    {
        Assert.False(ColorSpec.TryParse(raw, out _, out var e));
        Assert.NotEmpty(e);
    }

    [Fact]
    public void ChannelsAreClampedToUnitRange()
    {
        var c = new ColorSpec(2, -1, 0.5);
        Assert.Equal((1.0, 0.0, 0.5), (c.R, c.G, c.B));
    }

    [Fact]
    public void PaletteIsDeterministicAndDistinct()
    {
        var a = ColorSpec.Palette(24).Select(c => c.Hex).ToArray();
        var b = ColorSpec.Palette(24).Select(c => c.Hex).ToArray();
        Assert.Equal(a, b);
        Assert.Equal(24, a.Distinct().Count());
        Assert.Empty(ColorSpec.Palette(0));
    }

    [Fact]
    public void HsvPrimaries()
    {
        Assert.Equal("#FF0000", ColorSpec.FromHsv(0, 1, 1).Hex);
        Assert.Equal("#00FF00", ColorSpec.FromHsv(1.0 / 3, 1, 1).Hex);
        Assert.Equal("#0000FF", ColorSpec.FromHsv(2.0 / 3, 1, 1).Hex);
    }
}
