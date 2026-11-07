using System;
using System.IO;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;

namespace Bimwright.Nwd.Tests;

public sealed class PropertySpecTests
{
    [Fact]
    public void SplitsAtTheFirstSlash()
    {
        Assert.True(PropertySpec.TryParse(" Element / Size/Width ", out var s, out _));
        Assert.Equal("Element", s!.Category);
        Assert.Equal("Size/Width", s.Property);
        Assert.Equal("Element/Size/Width", s.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NoSlash")]
    [InlineData("/Name")]
    [InlineData("Item/")]
    public void RejectsIncompleteSpecs(string? raw)
        => Assert.False(PropertySpec.TryParse(raw, out _, out var e) || e.Length == 0);

    [Fact]
    public void TryParseAllNeedsAtLeastOne()
    {
        Assert.False(PropertySpec.TryParseAll(Array.Empty<string>(), out _, out _));
        Assert.True(PropertySpec.TryParseAll(new[] { "Item/Name", "Item/Type" }, out var specs, out _));
        Assert.Equal(2, specs.Count);
    }
}

public sealed class ValueHistogramTests
{
    [Fact]
    public void CountsDistinctMissingAndOrdersTopStably()
    {
        var h = new ValueHistogram();
        foreach (var v in new[] { "b", "a", "b", null, "c", "a", "b" }) h.Add(v);
        Assert.Equal(7, h.Scanned);
        Assert.Equal(1, h.Missing);
        Assert.Equal(3, h.Distinct);
        Assert.Equal(new[] { ("b", 3), ("a", 2) }, h.Top(2).Select(kv => (kv.Key, kv.Value)).ToArray());
    }
}

public sealed class TableExportTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("=HYPERLINK(1)", "\"'=HYPERLINK(1)\"")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("+cmd", "\"'+cmd\"")]
    public void EscapesCsvCellsAndBlocksFormulas(string input, string expected)
        => Assert.Equal(expected, TableExport.EscapeCsv(input));

    [Theory]
    [InlineData("..\\..\\evil", "evil.csv")]
    [InlineData("C:\\temp\\report.xlsx", "report.csv")]
    [InlineData("Ecoplanta 4D", "Ecoplanta_4D.csv")]
    [InlineData("", "props-20261107-101500.csv")]
    public void SanitisesFileNamesToTheExportsFolder(string requested, string expected)
        => Assert.Equal(expected, TableExport.SafeFileName(requested, "props", ".csv", new DateTime(2026, 11, 7, 10, 15, 0, DateTimeKind.Utc)));

    [Fact]
    public void WritesBomHeaderAndPadsShortRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nwd-mcp-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = TableExport.WriteCsvFile("t", "x", new[] { "id", "Item/Name", "Ñandú" },
                new[] { new string?[] { "0:1", "Pipe, 2\"" }, new string?[] { "0:2", null, "sí" } }, dir);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            var text = File.ReadAllText(path);
            Assert.Equal("id,Item/Name,Ñandú\r\n0:1,\"Pipe, 2\"\"\",\r\n0:2,,sí\r\n", text);
            Assert.StartsWith(Path.GetFullPath(dir), Path.GetFullPath(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
