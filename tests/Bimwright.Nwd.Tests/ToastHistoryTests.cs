using Bimwright.Nwd.Shared.Views.Toast;

namespace Bimwright.Nwd.Tests;

public sealed class ToastHistoryTests
{
    [Fact]
    public void History_keeps_newest_thousand_and_freezes_prior_snapshots()
    {
        var history = new ActivityHistory();
        history.Record(new McpToastViewModel { Title = "First", Summary = "Done", Success = true, DurationMs = 125 });
        var first = history.Snapshot();
        for (var i = 0; i < 1005; i++) history.Record(new McpToastViewModel { Title = "Tool " + i, Success = i != 4 });
        Assert.Equal(ActivityHistory.Capacity, history.Snapshot().Length);
        Assert.Equal("Tool 1004", history.Snapshot()[0].Title);
        Assert.Equal("First", first[0].Title);
        Assert.Equal(125, first[0].DurationMs);
        Assert.Equal(1006, history.Version);
    }

    [Fact]
    public void History_uses_only_bounded_sanitized_outcome_copies()
    {
        var history = new ActivityHistory();
        var model = new McpToastViewModel { Title = "Query", Summary = "password=private-value; C:\\private\\model.nwd "+new string('x', 400), Success = false };
        history.Record(model);
        model.Summary = "Changed later";
        var entry = history.Snapshot()[0];
        Assert.DoesNotContain("private", entry.Body);
        Assert.DoesNotContain("Changed later", entry.Body);
        Assert.True(entry.Body.Length <= ToastActivityEntry.BodyLimit);
        Assert.False(entry.Success);
    }
}
