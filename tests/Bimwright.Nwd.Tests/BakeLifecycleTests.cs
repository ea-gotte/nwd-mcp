using Bimwright.Nwd.Server;
using Bimwright.Nwd.Server.Bake;
using Bimwright.Nwd.Server.Tools;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Tests;

public sealed class BakeLifecycleTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "nwd-lifecycle-" + Guid.NewGuid().ToString("N"));
    private NwdMcpConfig Config => new() { BakeDirectory = dir };

    private void Seed(string id, string state)
    {
        using var db = new BakeDb(BakePaths.Db(Config));
        db.Migrate();
        db.UpsertSuggestion(new BakeSuggestionRecord
        {
            Id = id, ClusterKey = id, Source = "preset", Title = id, Description = "test",
            State = state, PayloadJson = "{\"tool\":\"find_items_by_name\"}"
        });
    }

    [Fact]
    public void Active_list_excludes_accepted_dismissed_and_archived_suggestions()
    {
        Seed("open", BakeSuggestionStates.Open);
        Seed("accepted", BakeSuggestionStates.Accepted);
        Seed("dismissed", BakeSuggestionStates.Dismissed);
        Seed("archived", BakeSuggestionStates.Archived);
        var data = JObject.Parse(new ToolBakerTools(Config).ListBakeSuggestions());
        Assert.Equal(new[] { "open" }, data["suggestions"]!.Select(x => x.Value<string>("id")).ToArray());
    }

    [Fact]
    public async Task Dismiss_hides_suggestion_until_thirty_days_and_reports_deadline()
    {
        Seed("snooze", BakeSuggestionStates.Open);
        var before = DateTimeOffset.UtcNow.AddDays(30);
        var tools = new ToolBakerWriteTools(new PluginClient(Config), Config);
        var result = JObject.Parse(await tools.DismissBakeSuggestion("snooze", default));
        Assert.True(result.Value<bool>("ok"));
        Assert.InRange(result["snooze_until"]!.ToObject<DateTimeOffset>(), before,
            DateTimeOffset.UtcNow.AddDays(30));
        Assert.Empty(JObject.Parse(new ToolBakerTools(Config).ListBakeSuggestions())["suggestions"]!);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Snooze_is_idempotent_and_becomes_active_exactly_at_deadline()
    {
        Seed("expiry", BakeSuggestionStates.Open);
        var clock = new Clock();
        var writer = new ToolBakerWriteTools(new PluginClient(Config), Config, clock);
        var reader = new ToolBakerTools(Config, clock);
        var first = JObject.Parse(await writer.DismissBakeSuggestion("expiry", default));
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero),
            first["snooze_until"]!.ToObject<DateTimeOffset>());
        clock.Now = clock.Now.AddDays(29);
        var replay = JObject.Parse(await writer.DismissBakeSuggestion("expiry", default));
        Assert.Equal(first["snooze_until"], replay["snooze_until"]);
        Assert.Empty(JObject.Parse(reader.ListBakeSuggestions())["suggestions"]!);
        clock.Now = clock.Now.AddDays(1);
        var active = JObject.Parse(reader.ListBakeSuggestions())["suggestions"]!;
        Assert.Single(active);
        Assert.Equal("open", active[0]!.Value<string>("state"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
