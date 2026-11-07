using Bimwright.Nwd.Server;

namespace Bimwright.Nwd.Tests;

public sealed class ToolsetFilterTests
{
    [Fact]
    public void DefaultSurfaceIncludesCode()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig());
        foreach (var t in new[] { "meta","files","files_write","query","selection","selection_write","sets","view","view_write","visibility","code","toolbaker","toolbaker_write" })
            Assert.Contains(t, set);
    }

    [Fact]
    public void DisableSendCodeRemovesCode()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { EnableSendCode = false });
        Assert.DoesNotContain("code", set);
    }

    [Fact]
    public void AllPlusSendCodeIncludesCode()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { Toolsets = new() { "all" }, EnableSendCode = true });
        Assert.Contains("code", set);
    }

    [Fact]
    public void ReadOnlyRemovesWriteCapableToolsetsButKeepsMeta()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { Toolsets = new() { "all" }, ReadOnly = true, EnableSendCode = true });
        foreach (var keep in new[] { "meta","files","query","selection","sets","view","toolbaker" })
            Assert.Contains(keep, set);
        foreach (var gone in new[] { "files_write","selection_write","view_write","visibility","code","toolbaker_write" })
            Assert.DoesNotContain(gone, set);
    }

    [Fact]
    public void DefaultMatchesEveryKnownToolset()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig());
        Assert.Equal(
            ToolsetFilter.KnownToolsets.OrderBy(x => x, StringComparer.Ordinal),
            set.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void NewDomainToolsetsAreKnownAndOnByDefault()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig());
        foreach (var t in new[] { "model","review","review_write","sets_write","clash","clash_write",
                                   "timeliner","timeliner_write","takeoff","takeoff_write","data_write","export" })
            Assert.Contains(t, set);
    }

    [Fact]
    public void ReadOnlyRemovesNewWriteToolsetsButKeepsTheirReadHalves()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { ReadOnly = true });
        foreach (var keep in new[] { "model","review","clash","timeliner","takeoff" })
            Assert.Contains(keep, set);
        foreach (var gone in new[] { "review_write","sets_write","clash_write","timeliner_write","takeoff_write","data_write","export" })
            Assert.DoesNotContain(gone, set);
    }

    [Fact]
    public void EveryWriteCapableToolsetIsKnown()
    {
        foreach (var w in ToolsetFilter.WriteCapable)
            Assert.Contains(w, ToolsetFilter.KnownToolsets);
    }

    [Fact]
    public void ProfileExpandsToCoreAndDomainToolsets()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { Toolsets = new() { "4d" } });
        foreach (var t in new[] { "meta","query","model","selection","sets","view","timeliner","timeliner_write","data_write" })
            Assert.Contains(t, set);
        Assert.DoesNotContain("clash", set);
        Assert.DoesNotContain("takeoff", set);
        Assert.DoesNotContain("code", set);
    }

    [Fact]
    public void ProfilesCombineWithExplicitToolsets()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { Toolsets = new() { "coordination", "code" }, EnableSendCode = true });
        Assert.Contains("clash", set);
        Assert.Contains("clash_write", set);
        Assert.Contains("review_write", set);
        Assert.Contains("code", set);
        Assert.DoesNotContain("timeliner", set);
    }

    [Fact]
    public void ReadOnlyStripsWriteToolsetsFromProfiles()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { Toolsets = new() { "coordination" }, ReadOnly = true });
        Assert.Contains("clash", set);
        Assert.DoesNotContain("clash_write", set);
        Assert.DoesNotContain("review_write", set);
    }

    [Fact]
    public void EveryProfileOnlyExpandsToKnownToolsets()
    {
        foreach (var kv in ToolsetFilter.Profiles)
        {
            Assert.DoesNotContain(kv.Key, ToolsetFilter.KnownToolsets);
            foreach (var t in kv.Value) Assert.Contains(t, ToolsetFilter.KnownToolsets);
        }
    }

    [Fact]
    public void DisableToolBakerRemovesBothBakerToolsets()
    {
        var set = ToolsetFilter.Resolve(new NwdMcpConfig { Toolsets = new() { "all" }, EnableToolBaker = false });
        Assert.DoesNotContain("toolbaker", set);
        Assert.DoesNotContain("toolbaker_write", set);
    }
}
