using System;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;

namespace Bimwright.Nwd.Tests;

public sealed class TaskRefTests
{
    [Theory]
    [InlineData("0", new[] { 0 })]
    [InlineData("2/0/5", new[] { 2, 0, 5 })]
    [InlineData(" task:1/3 ", new[] { 1, 3 })]
    public void ParsesIndexPaths(string raw, int[] path)
    {
        Assert.True(TaskRef.TryParse(raw, out var r, out _));
        Assert.Equal(TaskRefKind.IndexPath, r!.Kind);
        Assert.Equal(path, r.Path.ToArray());
        Assert.Equal("task:" + string.Join("/", path), TaskRef.FormatPath(path));
    }

    [Theory]
    [InlineData("display:A1010", TaskRefKind.DisplayId, "A1010")]
    [InlineData("sync:{abc}", TaskRefKind.SynchronizationId, "{abc}")]
    [InlineData("Cimentación Módulo 1", TaskRefKind.Name, "Cimentación Módulo 1")]
    [InlineData("name:3/4 losa", TaskRefKind.Name, "3/4 losa")]
    public void ParsesKeyedReferences(string raw, TaskRefKind kind, string key)
    {
        Assert.True(TaskRef.TryParse(raw, out var r, out _));
        Assert.Equal(kind, r!.Kind);
        Assert.Equal(key, r.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("display:")]
    [InlineData("task:1//2")]
    [InlineData("task:-1")]
    [InlineData("task:x")]
    public void RejectsBadReferences(string raw)
    {
        Assert.False(TaskRef.TryParse(raw, out _, out var e));
        Assert.NotEmpty(e);
    }
}

public sealed class TaskDateTests
{
    [Theory]
    [InlineData("2026-11-07", 2026, 11, 7, 0, 0)]
    [InlineData("2026-11-07T08:30", 2026, 11, 7, 8, 30)]
    [InlineData("2026-11-07 17:00:00", 2026, 11, 7, 17, 0)]
    [InlineData("07/11/2026", 2026, 11, 7, 0, 0)]
    public void ParsesIsoAndDayFirstDates(string raw, int y, int m, int d, int h, int min)
    {
        Assert.True(TaskDate.TryParse(raw, out var value, out var clear, out _));
        Assert.False(clear);
        Assert.Equal(new DateTime(y, m, d, h, min, 0), value);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("null")]
    [InlineData("")]
    public void ClearsWithNoneOrEmpty(string raw)
    {
        Assert.True(TaskDate.TryParse(raw, out var value, out var clear, out _));
        Assert.True(clear);
        Assert.Null(value);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("tomorrow")]
    [InlineData("11/07/2026x")]
    public void RejectsInvalidDates(string raw)
        => Assert.False(TaskDate.TryParse(raw, out _, out _, out var e) || e.Length == 0);

    [Fact]
    public void ChecksTheRangeWithNewValuesOverCurrentOnes()
    {
        var nov1 = new DateTime(2026, 11, 1);
        var dec1 = new DateTime(2026, 12, 1);
        // new start after new end
        Assert.False(TaskDate.CheckRange(dec1, true, nov1, true, null, null, out var e1));
        Assert.Contains("before", e1);
        // only the end changes, and falls before the current start
        Assert.False(TaskDate.CheckRange(null, false, nov1, true, dec1, null, out _));
        // only the start changes, current end is later
        Assert.True(TaskDate.CheckRange(nov1, true, null, false, null, dec1, out _));
        // clearing the start removes the constraint
        Assert.True(TaskDate.CheckRange(null, true, nov1, false, dec1, nov1, out _));
    }

    [Fact]
    public void FormatsIsoWithoutOffset()
        => Assert.Equal("2026-11-07T08:30:00", TaskDate.Format(new DateTime(2026, 11, 7, 8, 30, 0)));
}

public sealed class TaskPathTests
{
    [Theory]
    [InlineData(new[] { 0 }, new[] { 0, 2 }, true)]
    [InlineData(new[] { 0 }, new[] { 0 }, true)]
    [InlineData(new[] { 0, 1 }, new[] { 0 }, false)]
    [InlineData(new[] { 1 }, new[] { 10, 1 }, false)]
    public void DetectsSelfOrDescendant(int[] ancestor, int[] candidate, bool expected)
        => Assert.Equal(expected, TaskRef.IsSelfOrDescendant(ancestor, candidate));
}

public sealed class NameMatchTests
{
    private static readonly string[] Types = { "Construcción", "Demoler", "Temporal" };

    [Theory]
    [InlineData("Construcción", "Construcción")]
    [InlineData("construccion", "Construcción")]
    [InlineData(" DEMOLER ", "Demoler")]
    public void MatchesIgnoringCaseAccentsAndSpaces(string requested, string expected)
    {
        Assert.True(NameMatch.TryResolve(requested, Types, out var resolved, out _));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ListsValidNamesWhenNothingMatches()
    {
        Assert.False(NameMatch.TryResolve("Construct", Types, out _, out var error));
        Assert.Contains("Construcción", error);
        Assert.Contains("Demoler", error);
        Assert.Contains("Temporal", error);
    }

    [Fact]
    public void EmptyRequestIsAllowedAndClears()
    {
        Assert.True(NameMatch.TryResolve("", Types, out var resolved, out _));
        Assert.Equal("", resolved);
    }
}

public sealed class TaskCodeMatcherTests
{
    [Fact]
    public void AssignsItemsToTasksByNormalisedCode()
    {
        var m = new TaskCodeMatcher(ignoreCase: true, separator: null);
        m.AddTask(0, "A1010");
        m.AddTask(1, "a1020 ");
        m.AddItem(10, " A1010");
        m.AddItem(11, "A1020");
        m.AddItem(12, "A9999");
        m.AddItem(13, null);
        var r = m.Match();

        Assert.Equal(new[] { 10 }, r.ItemsByTask[0]);
        Assert.Equal(new[] { 11 }, r.ItemsByTask[1]);
        Assert.Equal(new[] { "A9999" }, r.UnmatchedCodes.Keys.ToArray());
        Assert.Equal(1, r.UnmatchedCodes["A9999"]);
        Assert.Equal(1, r.ItemsWithoutCode);
        Assert.Empty(r.TasksWithoutItems);
        Assert.Empty(r.DuplicateTaskKeys);
    }

    [Fact]
    public void CaseSensitiveWhenAsked()
    {
        var m = new TaskCodeMatcher(ignoreCase: false, separator: null);
        m.AddTask(0, "A1010");
        m.AddItem(1, "a1010");
        var r = m.Match();
        Assert.Empty(r.ItemsByTask);
        Assert.Equal(new[] { 0 }, r.TasksWithoutItems);
    }

    [Fact]
    public void SplitsMultiCodeValuesWithSeparator()
    {
        var m = new TaskCodeMatcher(ignoreCase: true, separator: ";");
        m.AddTask(0, "A");
        m.AddTask(1, "B");
        m.AddItem(7, "A; B ;");
        var r = m.Match();
        Assert.Equal(new[] { 7 }, r.ItemsByTask[0]);
        Assert.Equal(new[] { 7 }, r.ItemsByTask[1]);
        Assert.Equal(1, r.ItemsMatched);
    }

    [Fact]
    public void ReportsDuplicateTaskKeysAndUsesTheFirstTask()
    {
        var m = new TaskCodeMatcher(ignoreCase: true, separator: null);
        m.AddTask(0, "X");
        m.AddTask(5, "x");
        m.AddTask(6, null);
        m.AddItem(1, "X");
        var r = m.Match();
        Assert.Equal(new[] { "X" }, r.DuplicateTaskKeys.ToArray());
        Assert.Equal(new[] { 1 }, r.ItemsByTask[0]);
        Assert.False(r.ItemsByTask.ContainsKey(5));
        Assert.Equal(new[] { 5 }, r.TasksWithoutItems);
        Assert.Equal(1, r.TasksWithoutKey);
    }
}
