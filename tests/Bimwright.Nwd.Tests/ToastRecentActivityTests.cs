using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Bimwright.Nwd.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Tests;

public sealed class ToastRecentActivityTests
{
    [Fact]
    public void Card_keeps_every_outcome_in_completion_order()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        for (var i = 1; i <= 8; i++)
            agg.RecordResult("Tool " + i, "Result " + i, i != 4, false, true, durationMs: i * 100);
        var card = agg.TakeRender().Card;
        Assert.Equal(7, card.Succeeded);
        Assert.Equal(1, card.Failed);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => "Tool " + i), card.RecentEntries.Select(e => e.Title));
        Assert.Equal(800, card.RecentEntries[7].DurationMs);
        Assert.False(card.RecentEntries[3].Success);
    }

    [Fact]
    public void Render_snapshot_is_immutable_while_more_results_arrive()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        agg.RecordResult("First", "First result", true, false, true);
        var first = agg.TakeRender().Card;
        agg.RecordResult("Next", "Next result", true, false, true);
        var next = agg.TakeRender().Card;
        Assert.Single(first.RecentEntries);
        Assert.Equal("First", first.RecentEntries[0].Title);
        Assert.Equal(2, next.RecentEntries.Count);
        Assert.False(first.RecentEntries is IList<ToastActivityEntry>);
        Assert.Throws<ArgumentOutOfRangeException>(() => first.RecentEntries[1]);
    }

    [Fact]
    public void Reset_and_new_card_do_not_resurrect_old_outcomes()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        agg.RecordResult("Old", "Old result", true, false, true);
        var oldId = agg.TakeRender().Card.CardId;
        Assert.True(agg.Reset());
        Assert.Equal(ActivityCardPhase.Hidden, agg.TakeRender().Phase);
        agg.RecordResult("New", "New result", true, false, true);
        var card = agg.TakeRender().Card;
        Assert.NotEqual(oldId, card.CardId);
        Assert.Single(card.RecentEntries);
        Assert.Equal("New", card.RecentEntries[0].Title);
    }

    [Fact]
    public void Dismiss_and_close_start_an_empty_recent_list_next_time()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        agg.RecordResult("Old", "Done", true, false, true);
        var id = agg.TakeRender().Card.CardId;
        agg.Dismiss(id);
        agg.TakeRender();
        agg.CardClosed(id);
        agg.RecordResult("New", "Done", true, false, true);
        Assert.Equal("New", Assert.Single(agg.TakeRender().Card.RecentEntries).Title);
    }

    [Fact]
    public void Parked_card_retains_recent_results_and_counts()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        agg.RecordResult("First", "Done", true, false, false);
        agg.RecordResult("Second", "Failed", false, false, false);
        Assert.Equal(ActivityCardPhase.Hidden, agg.TakeRender().Phase);
        Assert.True(agg.FlushIfUsable(true));
        var card = agg.TakeRender().Card;
        Assert.Equal(2, card.RecentEntries.Count);
        Assert.Equal(1, card.Succeeded);
        Assert.Equal(1, card.Failed);
    }

    [Fact]
    public void Status_has_no_activity_preview_and_does_not_count_as_a_tool()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        agg.ShowStatus("Connected", "Ready", 6);
        var status = agg.TakeRender().Card;
        Assert.Empty(status.RecentEntries);
        Assert.Equal(0, status.Succeeded);
        agg.RecordResult("Tool", "Done", true, false, true);
        var card = agg.TakeRender().Card;
        Assert.False(card.IsStatus);
        Assert.Single(card.RecentEntries);
    }

    [Fact]
    public void Hover_during_a_burst_keeps_the_card_alive_then_leave_rearms()
    {
        var now = TimeSpan.Zero;
        var agg = new ActivityAggregator(() => 20, () => now);
        agg.RecordResult("First", "Done", true, false, true);
        var id = agg.TakeRender().Card.CardId;
        agg.PointerEntered(id);
        now = TimeSpan.FromSeconds(100);
        agg.RecordResult("Next", "Done", true, false, true);
        Assert.False(agg.Tick(true));
        Assert.Equal(id, agg.TakeRender().Card.CardId);
        agg.PointerLeft(id);
        now += TimeSpan.FromSeconds(20);
        Assert.True(agg.Tick(true));
        Assert.Equal(ActivityCardPhase.Closing, agg.TakeRender().Phase);
    }

    [Fact]
    public void Entries_are_bounded_and_mask_credentials_paths_and_line_breaks()
    {
        var entry = new ToastActivityEntry("Tool\nname",
            "password=super-private; api_key='key-value'; auth_token=token-value; C:\\private\\model.nwd", true, -50);
        Assert.Equal("Tool name", entry.Title);
        Assert.DoesNotContain("super-private", entry.Body);
        Assert.DoesNotContain("key-value", entry.Body);
        Assert.DoesNotContain("token-value", entry.Body);
        Assert.DoesNotContain("private", entry.Body);
        Assert.Contains("<redacted>", entry.Body);
        Assert.Contains("<path>", entry.Body);
        Assert.Equal(0, entry.DurationMs);
        var bounded = new ToastActivityEntry(string.Concat(Enumerable.Repeat("tool ", 50)),
            string.Concat(Enumerable.Repeat("summary ", 100)), true);
        Assert.Equal(ToastActivityEntry.TitleLimit, bounded.Title.Length);
        Assert.Equal(ToastActivityEntry.BodyLimit, bounded.Body.Length);
        Assert.Null(bounded.DurationMs);
    }

    [Theory]
    [InlineData("C:/private/model.nwd")]
    [InlineData("\\\\server\\private\\model.nwd")]
    public void Preview_does_not_expose_full_file_paths(string path)
    {
        Assert.Equal("<path>", new ToastActivityEntry("Tool", path, true).Body);
    }

    [Theory]
    [InlineData("hide_items", "hidden_count", 24, "Hidden 24 items")]
    [InlineData("select_items_by_search", "selected_count", 12, "Selected 12 items")]
    [InlineData("execute_search_set", "matched_count", 6, "Matched 6 items")]
    public void Common_mutation_and_search_results_have_readable_outcomes(string tool, string key, int count, string expected)
    {
        var vm = Build(tool, new JObject { [key] = count });
        Assert.Equal(expected, vm.Summary);
        Assert.True(vm.Success);
        Assert.Equal(325, vm.DurationMs);
    }

    [Fact]
    public void Unhide_result_does_not_claim_that_items_were_hidden()
    {
        Assert.Equal("Shown 24 items", Build("hide_items", new JObject { ["hidden_count"] = 24, ["hide"] = false }).Summary);
    }

    [Fact]
    public void Send_code_soft_failure_is_counted_as_failed_even_without_error_text()
    {
        var vm = Build("send_code", new JObject { ["ok"] = false });
        Assert.False(vm.Success);
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        agg.RecordResult(vm.Title, vm.Body, vm.Success, false, true, durationMs: vm.DurationMs);
        var card = agg.TakeRender().Card;
        Assert.Equal(0, card.Succeeded);
        Assert.Equal(1, card.Failed);
        Assert.False(Assert.Single(card.RecentEntries).Success);
    }

    [Fact]
    public void Script_object_result_is_summarised_without_serializing_its_content()
    {
        var vm = Build("send_code", new JObject
        {
            ["ok"] = true, ["stdout"] = "",
            ["result"] = new JObject { ["sensitive"] = "must-not-appear", ["other"] = new JArray(1, 2) }
        });
        Assert.Equal("Result: 2 fields", vm.Summary);
        Assert.DoesNotContain("must-not-appear", vm.Body);
    }

    [Fact]
    public void Empty_stdout_does_not_hide_a_real_script_result()
    {
        Assert.Equal("42", Build("send_code", new JObject { ["ok"] = true, ["stdout"] = "", ["result"] = 42 }).Summary);
    }

    [Fact]
    public void Error_credentials_are_masked_before_the_builder_truncates_them()
    {
        var error = "password=\"" + string.Concat(Enumerable.Repeat("private value ", 30)) + "\"";
        var vm = ToastContentBuilder.BuildCompleted("send_code", null, false, error, null);
        Assert.DoesNotContain("private", vm.Summary);
        Assert.Contains("<redacted>", vm.Summary);
    }

    [Fact]
    public void Missing_duration_is_unknown_not_an_invented_zero()
    {
        var vm = ToastContentBuilder.BuildCompleted("find_items", "{\"ok\":true,\"data\":{\"count\":2}}", true, null, null);
        Assert.Null(vm.DurationMs);
    }

    [Theory]
    [InlineData(0, "00:06:09")]
    [InlineData(13, "13:06:09")]
    [InlineData(23, "23:06:09")]
    public void Completion_time_uses_local_24_hour_HH_mm_ss_not_duration(int hour, string expected)
    {
        var local = new DateTime(2026, 10, 1, hour, 6, 9);
        var completed = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero, wallClock: () => completed);
        agg.RecordResult("Tool", "Done", true, false, true, durationMs: 830);
        var entry = Assert.Single(agg.TakeRender().Card.RecentEntries);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal(expected, entry.LocalTimeText);
            Assert.Equal(completed, entry.CompletedAt);
            Assert.Equal(830, entry.DurationMs); // measured metadata is not the clock display
            completed += TimeSpan.FromHours(2);
            Assert.Equal(expected, entry.LocalTimeText); // no timestamp changes when hovering later
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Thousands_of_calls_survive_without_a_retention_cap_and_snapshots_allocate_constant_space()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        for (var i = 0; i < 4097; i++) agg.RecordResult("Tool " + i, "Done", true, false, true);
        var card = agg.TakeRender().Card;
        Assert.Equal(4097, card.RecentEntries.Count);
        Assert.Equal("Tool 0", card.RecentEntries[0].Title);
        Assert.Equal("Tool 4096", card.RecentEntries[4096].Title);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) agg.TakeRender();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 512_000, "Snapshots copied the growing log: " + allocated + " bytes.");
    }

    [Fact]
    public void Frozen_snapshots_survive_block_boundaries_reset_and_reuse()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        for (var i = 0; i < 65; i++) agg.RecordResult("Old " + i, "Done", true, false, true);
        var old = agg.TakeRender().Card;
        for (var i = 65; i < 130; i++) agg.RecordResult("Old " + i, "Done", true, false, true);
        Assert.Equal(65, old.RecentEntries.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => old.RecentEntries[65]);
        agg.Reset();
        for (var i = 0; i < 130; i++) agg.RecordResult("New " + i, "Done", true, false, true);
        Assert.Equal(Enumerable.Range(0, 65).Select(i => "Old " + i), old.RecentEntries.Select(e => e.Title));
        Assert.Equal("New 0", agg.TakeRender().Card.RecentEntries[0].Title);
    }

    [Fact]
    public void Indexed_WPF_snapshot_collection_cannot_be_modified()
    {
        var agg = new ActivityAggregator(now: () => TimeSpan.Zero);
        agg.RecordResult("First", "Done", true, false, true);
        var entries = agg.TakeRender().Card.RecentEntries;
        var indexed = Assert.IsAssignableFrom<System.Collections.IList>(entries);
        Assert.True(indexed.IsReadOnly);
        Assert.True(indexed.IsFixedSize);
        Assert.Same(entries[0], indexed[0]);
        Assert.Equal(0, indexed.IndexOf(entries[0]));
        Assert.True(indexed.Contains(entries[0]));
        Assert.Equal(-1, indexed.IndexOf(new ToastActivityEntry("Other", "Done", true)));
        Assert.Throws<NotSupportedException>(() => indexed.Clear());
        Assert.Throws<NotSupportedException>(() => indexed.Add(entries[0]));
        Assert.Throws<NotSupportedException>(() => indexed[0] = entries[0]);
        var copied = new ToastActivityEntry[1];
        indexed.CopyTo(copied, 0);
        Assert.Same(entries[0], copied[0]);
    }

    [Fact]
    public void External_snapshot_lists_are_copied_not_retained_live()
    {
        var entries = new List<ToastActivityEntry> { new ToastActivityEntry("First", "Done", true) };
        var card = new ActivitySnapshot(1, false, 1, 0, 0, "First", "Done", true, false, recentEntries: entries);
        entries[0] = new ToastActivityEntry("Replacement", "Done", true);
        entries.Clear();
        Assert.Equal("First", Assert.Single(card.RecentEntries).Title);
        Assert.Throws<NotSupportedException>(() => ((IList<ToastActivityEntry>)card.RecentEntries).Clear());
    }

    private static McpToastViewModel Build(string tool, JObject data) => ToastContentBuilder.BuildCompleted(tool,
        new JObject { ["ok"] = true, ["data"] = data, ["meta"] = new JObject { ["duration_ms"] = 325 } }.ToString(),
        true, null, null);
}
