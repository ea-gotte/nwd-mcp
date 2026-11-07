using System;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Tests;

public sealed class ItemRefTests
{
    [Theory]
    [InlineData("0:", 0, new int[0])]
    [InlineData("0:6:3", 0, new[] { 6, 3 })]
    [InlineData(" 12:0 ", 12, new[] { 0 })]
    public void ParsesLegacyPathIds(string raw, int model, int[] children)
    {
        Assert.True(ItemRef.TryParse(raw, out var r, out _));
        Assert.Equal(ItemRefKind.Path, r!.Kind);
        Assert.Equal(model, r.ModelIndex);
        Assert.Equal(children, r.ChildPath.ToArray());
        Assert.Equal(raw.Trim(), ItemRef.FormatPath(r.ModelIndex, r.ChildPath));
    }

    [Fact]
    public void ParsesPathIdKeepingColonsInsideThePathId()
    {
        Assert.True(ItemRef.TryParse("pid:2:a:b/c", out var r, out _));
        Assert.Equal(ItemRefKind.PathId, r!.Kind);
        Assert.Equal(2, r.ModelIndex);
        Assert.Equal("a:b/c", r.PathId);
        Assert.Equal("pid:2:a:b/c", ItemRef.FormatPathId(2, "a:b/c"));
    }

    [Fact]
    public void ParsesNumericIdAndGuid()
    {
        Assert.True(ItemRef.TryParse("nid:1:18446744073709551615", out var n, out _));
        Assert.Equal(ItemRefKind.NumericId, n!.Kind);
        Assert.Equal(ulong.MaxValue, n.NumericId);
        Assert.Equal("nid:1:18446744073709551615", ItemRef.FormatNumericId(1, ulong.MaxValue));

        var g = Guid.NewGuid();
        Assert.True(ItemRef.TryParse("GUID:" + g, out var gr, out _));
        Assert.Equal(ItemRefKind.InstanceGuid, gr!.Kind);
        Assert.Equal(g, gr.Guid);
        Assert.Equal("guid:" + g.ToString("D"), ItemRef.FormatGuid(g));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("0::3")]
    [InlineData("-1:2")]
    [InlineData("0:x")]
    [InlineData("pid:0")]
    [InlineData("pid:0:")]
    [InlineData("pid:x:abc")]
    [InlineData("nid:0:0")]
    [InlineData("nid:0")]
    [InlineData("guid:00000000-0000-0000-0000-000000000000")]
    [InlineData("guid:nope")]
    public void RejectsMalformedIdsWithAReason(string raw)
    {
        Assert.False(ItemRef.TryParse(raw, out var r, out var error));
        Assert.Null(r);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData(null, ItemIdMode.Path, true)]
    [InlineData("path", ItemIdMode.Path, true)]
    [InlineData("PID", ItemIdMode.PathId, true)]
    [InlineData("numeric_id", ItemIdMode.NumericId, true)]
    [InlineData("guid", ItemIdMode.InstanceGuid, true)]
    [InlineData("bogus", ItemIdMode.Path, false)]
    public void ParsesIdMode(string? value, ItemIdMode expected, bool ok)
    {
        Assert.Equal(ok, ItemRef.TryParseMode(value, out var mode));
        Assert.Equal(expected, mode);
    }
}

public sealed class PagingTests
{
    [Fact]
    public void DefaultsWhenNoParameters()
    {
        Assert.True(PageRequest.TryFrom(new JObject(), out var p, out _));
        Assert.Equal(0, p.Offset);
        Assert.Equal(PageRequest.DefaultLimit, p.Limit);
    }

    [Fact]
    public void AppliesOffsetLimitAndReportsNextCursor()
    {
        Assert.True(PageRequest.TryFrom(new JObject { ["offset"] = 10, ["limit"] = 5 }, out var p, out _));
        var (items, page) = p.Apply(Enumerable.Range(0, 23));
        Assert.Equal(new[] { 10, 11, 12, 13, 14 }, items);
        Assert.True((bool)page["has_more"]!);
        Assert.Equal(23, (int)page["total"]!);

        Assert.True(PageRequest.TryFrom(new JObject { ["cursor"] = (string)page["next_cursor"]!, ["limit"] = 100 }, out var next, out _));
        var (rest, lastPage) = next.Apply(Enumerable.Range(0, 23));
        Assert.Equal(Enumerable.Range(15, 8), rest);
        Assert.False((bool)lastPage["has_more"]!);
        Assert.Equal(JTokenType.Null, lastPage["next_cursor"]!.Type);
    }

    [Fact]
    public void SkippingTotalStopsEnumeratingAfterThePage()
    {
        int pulled = 0;
        System.Collections.Generic.IEnumerable<int> Source() { for (int i = 0; i < 1_000_000; i++) { pulled++; yield return i; } }
        var (items, page) = new PageRequest(0, 3).Apply(Source(), countTotal: false);
        Assert.Equal(3, items.Count);
        Assert.True(pulled <= 4);
        Assert.Null(page["total"]);
        Assert.True((bool)page["has_more"]!);
    }

    [Fact]
    public void ClampsLimitToMaximum()
    {
        Assert.True(PageRequest.TryFrom(new JObject { ["limit"] = 1_000_000 }, out var p, out _));
        Assert.Equal(PageRequest.MaxLimit, p.Limit);
    }

    [Theory]
    [InlineData("{\"offset\":-1}")]
    [InlineData("{\"offset\":\"3\"}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"cursor\":\"not-base64!\"}")]
    [InlineData("{\"cursor\":\"eHl6\"}")]
    public void RejectsInvalidParameters(string json)
    {
        Assert.False(PageRequest.TryFrom(JObject.Parse(json), out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
