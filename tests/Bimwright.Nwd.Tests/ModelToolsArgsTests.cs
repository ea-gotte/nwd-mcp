using Bimwright.Nwd.Server.Tools;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Tests;

public sealed class ModelToolsArgsTests
{
    [Fact]
    public void ScopeArgsOmitsEverythingNotGiven()
        => Assert.Equal("{}", ModelTools.ScopeArgs(null, null, null).ToString(Newtonsoft.Json.Formatting.None));

    [Fact]
    public void ScopeArgsCarriesSetAndCursor()
    {
        var p = ModelTools.ScopeArgs(null, "path:/Clash/Pipes", null, cursor: "bzoxMA==");
        Assert.Equal("path:/Clash/Pipes", (string?)p["set_id"]);
        Assert.Equal("bzoxMA==", (string?)p["cursor"]);
        Assert.Null(p["item_ids"]);
        Assert.Null(p["offset"]);
    }

    [Fact]
    public void ScopeArgsCarriesCurrentSelection()
    {
        var p = ModelTools.ScopeArgs(null, null, "current", limit: 5);
        Assert.Equal("current", (string?)p["selection"]);
        Assert.Equal(5, (int)p["limit"]!);
    }
}
