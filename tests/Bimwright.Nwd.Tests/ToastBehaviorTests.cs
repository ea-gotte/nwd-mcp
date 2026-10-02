using System;
using System.IO;
using Bimwright.Nwd.Shared.Config;
using Bimwright.Nwd.Shared.Views.Toast;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Tests;

public sealed class ActivityAggregatorToastTests
{
    [Fact]
    public void Hover_leave_rearms_the_full_idle_interval()
    {
        var now = TimeSpan.Zero;
        var agg = new ActivityAggregator(() => 20, () => now);
        Assert.True(agg.RecordResult("Get Document Info", "Plant", true, false, true));
        var card = agg.TakeRender().Card;
        Assert.NotNull(card);

        agg.PointerEntered(card.CardId);
        now = TimeSpan.FromSeconds(100);
        Assert.False(agg.Tick(true));
        Assert.Equal(ActivityCardPhase.Visible, agg.TakeRender().Phase);

        agg.PointerLeft(card.CardId);
        now += TimeSpan.FromSeconds(19);
        Assert.False(agg.Tick(true));
        Assert.Equal(ActivityCardPhase.Visible, agg.TakeRender().Phase);

        now += TimeSpan.FromSeconds(1);
        Assert.True(agg.Tick(true));
        Assert.Equal(ActivityCardPhase.Closing, agg.TakeRender().Phase);
    }

    [Fact]
    public void Hover_after_the_deadline_closes_and_the_next_result_can_render()
    {
        var now = TimeSpan.Zero;
        var agg = new ActivityAggregator(() => 20, () => now);
        Assert.True(agg.RecordResult("Get Document Info", "Plant", true, false, true));
        var card = agg.TakeRender().Card;
        Assert.NotNull(card);

        now = TimeSpan.FromSeconds(20);
        Assert.True(agg.PointerEntered(card.CardId));
        Assert.Equal(ActivityCardPhase.Closing, agg.TakeRender().Phase);

        Assert.True(agg.RecordResult("Hide Items", "3 items", true, false, true));
        var next = agg.TakeRender();
        Assert.Equal(ActivityCardPhase.Visible, next.Phase);
        Assert.Equal("Hide Items", next.Card.Title);
    }

    [Fact]
    public void Status_does_not_cover_an_open_activity_card()
    {
        var agg = new ActivityAggregator(() => 20, () => TimeSpan.Zero);
        Assert.True(agg.RecordResult("Get Document Info", "Plant", true, false, true));
        Assert.False(agg.ShowStatus("Agent connected", "nwd-mcp is ready", 6));
        var render = agg.TakeRender();
        Assert.Equal(ActivityCardPhase.Visible, render.Phase);
        Assert.False(render.Card.IsStatus);
        Assert.Equal("Get Document Info", render.Card.Title);
    }
}

public sealed class ClientPresenceTests
{
    [Fact]
    public void Rising_edge_fires_once_per_thirty_second_burst()
    {
        var state = new ClientPresence.State();
        var t0 = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(ClientPresence.Observe(ref state, t0, socketOpen: true, lastCommandUtc: null));
        Assert.False(ClientPresence.Observe(ref state, t0.AddSeconds(1), socketOpen: false, lastCommandUtc: t0));
        // An idle poll inside the window must not push the rising edge out.
        Assert.False(ClientPresence.Observe(ref state, t0.AddSeconds(29), socketOpen: false, lastCommandUtc: t0));
        Assert.False(ClientPresence.Observe(ref state, t0.AddSeconds(30), socketOpen: false, lastCommandUtc: t0));
        Assert.True(ClientPresence.Observe(ref state, t0.AddSeconds(31), socketOpen: false, lastCommandUtc: t0.AddSeconds(31)));
    }

    [Fact]
    public void Open_socket_does_not_stretch_presence_after_it_closes()
    {
        var state = new ClientPresence.State();
        var t0 = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(ClientPresence.Observe(ref state, t0, socketOpen: true, lastCommandUtc: t0));
        Assert.False(ClientPresence.Observe(ref state, t0.AddSeconds(45), socketOpen: true, lastCommandUtc: t0));
        Assert.False(ClientPresence.Observe(ref state, t0.AddSeconds(45), socketOpen: false, lastCommandUtc: t0));
        Assert.True(ClientPresence.Observe(ref state, t0.AddSeconds(46), socketOpen: false, lastCommandUtc: t0.AddSeconds(46)));
    }
}

public sealed class ToastCommandFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("health_check")]
    public void Health_check_and_blank_names_are_skipped(string? command)
    {
        Assert.False(ToastCommands.ShouldToast(command));
    }

    [Fact]
    public void Document_commands_are_shown()
    {
        Assert.True(ToastCommands.ShouldToast("get_document_info"));
    }
}

public sealed class ToastContentBuilderTests
{
    [Fact]
    public void Document_info_reads_the_result_envelope()
    {
        var json = new JObject
        {
            ["ok"] = true,
            ["data"] = new JObject
            {
                ["title"] = "Plant",
                ["file_name"] = @"C:\models\plant.nwd",
                ["model_count"] = 2
            },
            ["error"] = null
        }.ToString();

        var vm = ToastContentBuilder.BuildCompleted("get_document_info", json, true, null, null);
        Assert.Equal("Get Document Info", vm.Title);
        Assert.Equal("Plant", vm.Summary);
        Assert.Contains("2", vm.Detail);
        Assert.True(vm.Success);
    }

    [Fact]
    public void Failed_envelope_uses_error_message()
    {
        var json = new JObject
        {
            ["ok"] = false,
            ["data"] = null,
            ["error"] = new JObject
            {
                ["code"] = "NO_DOCUMENT",
                ["message"] = "no active Navisworks document"
            }
        }.ToString();

        var vm = ToastContentBuilder.BuildCompleted("get_document_info", json, false, null, null);
        Assert.Equal("no active Navisworks document", vm.Summary);
        Assert.False(vm.Success);
    }

    [Fact]
    public void Send_code_inner_error_is_shown_as_the_summary()
    {
        var json = new JObject
        {
            ["ok"] = true,
            ["data"] = new JObject
            {
                ["ok"] = false,
                ["stdout"] = "",
                ["error"] = "compile error: boom"
            }
        }.ToString();

        var vm = ToastContentBuilder.BuildCompleted("send_code", json, true, null, null);
        Assert.Contains("compile error: boom", vm.Summary);
        Assert.False(vm.Success); // A visible script error must also increment Failed, not Success.
    }
}

public sealed class PluginSettingsToastTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _previousEnv;
    private readonly string? _previousRecordEnv;

    public PluginSettingsToastTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nwd-mcp-settings-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
        PluginSettings.FilePathOverride = Path.Combine(_dir, "nwdmcp.config.json");
        _previousEnv = Environment.GetEnvironmentVariable(PluginSettings.EnvEnableToast);
        _previousRecordEnv = Environment.GetEnvironmentVariable(PluginSettings.EnvRecordCalls);
        Environment.SetEnvironmentVariable(PluginSettings.EnvEnableToast, null);
        Environment.SetEnvironmentVariable(PluginSettings.EnvRecordCalls, null);
    }

    [Fact]
    public void Missing_file_uses_branding_off_and_idle_20()
    {
        Assert.False(PluginSettings.LoadShowBranding());
        Assert.Equal(20, PluginSettings.LoadToastIdleSeconds());
        Assert.True(PluginSettings.LoadToastEnabled());
        Assert.False(PluginSettings.LoadRecordCalls());
    }

    [Fact]
    public void Illegal_idle_is_stored_and_loaded_as_20()
    {
        Assert.True(PluginSettings.SaveToastIdleSeconds(15));
        Assert.Equal(20, PluginSettings.LoadToastIdleSeconds());
        var saved = JObject.Parse(File.ReadAllText(PluginSettings.FilePathOverride!));
        Assert.Equal(20, saved["toastIdleSeconds"]!.Value<int>());
    }

    [Fact]
    public void Save_keeps_other_keys()
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, "{\n  \"other\": 1\n}");
        Assert.True(PluginSettings.SaveShowBranding(false));
        var saved = JObject.Parse(File.ReadAllText(PluginSettings.FilePathOverride!));
        Assert.Equal(1, saved["other"]!.Value<int>());
        Assert.False(saved["showBranding"]!.Value<bool>());
    }

    [Fact]
    public void Malformed_file_is_left_unchanged()
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, "{");
        Assert.False(PluginSettings.SaveEnableToast(false));
        Assert.Equal("{", File.ReadAllText(PluginSettings.FilePathOverride!));

        File.WriteAllText(PluginSettings.FilePathOverride!, "[]");
        Assert.False(PluginSettings.SaveToastIdleSeconds(30));
        Assert.Equal("[]", File.ReadAllText(PluginSettings.FilePathOverride!));
    }

    [Fact]
    public void Enable_env_wins_over_the_file()
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, "{ \"enableToast\": false }");
        Environment.SetEnvironmentVariable(PluginSettings.EnvEnableToast, "1");
        Assert.True(PluginSettings.LoadToastEnabled());
        Environment.SetEnvironmentVariable(PluginSettings.EnvEnableToast, "off");
        Assert.False(PluginSettings.LoadToastEnabled());
    }

    [Fact]
    public void Record_save_round_trips_and_keeps_other_keys()
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, "{ \"enableToast\": true }");
        Assert.True(PluginSettings.SaveRecordCalls(true));
        var saved = JObject.Parse(File.ReadAllText(PluginSettings.FilePathOverride!));
        Assert.True(saved["enableToast"]!.Value<bool>());
        Assert.True(saved["recordCalls"]!.Value<bool>());
        Assert.True(PluginSettings.LoadRecordCalls());
    }

    [Fact]
    public void Record_env_wins_over_the_file()
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, "{ \"recordCalls\": true }");
        Environment.SetEnvironmentVariable(PluginSettings.EnvRecordCalls, "0");
        Assert.False(PluginSettings.LoadRecordCalls());
        Environment.SetEnvironmentVariable(PluginSettings.EnvRecordCalls, "on");
        Assert.True(PluginSettings.LoadRecordCalls());
    }

    [Fact]
    public void Position_preferences_round_trip_preserve_other_keys_and_reset_only_offset()
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, "{\"other\":42,\"toastIdleSeconds\":30}");
        var options = new ToastPositionOptions(true, true, true, -40.5, -20);
        Assert.True(PluginSettings.SaveToastPosition(options));
        var loaded = PluginSettings.LoadToastPosition();
        Assert.True(loaded.Right && loaded.Bottom && loaded.DragEnabled && loaded.HasOffset);
        Assert.Equal(-40.5, loaded.OffsetX);
        Assert.Equal(-20, loaded.OffsetY);
        Assert.True(PluginSettings.SaveToastPosition(loaded.WithDrag(false)));
        Assert.True(PluginSettings.LoadToastPosition().HasOffset);
        Assert.True(PluginSettings.SaveToastPosition(loaded.WithOffset(null, null)));
        var saved = JObject.Parse(File.ReadAllText(PluginSettings.FilePathOverride!));
        Assert.Null(saved["toastDragOffset"]);
        Assert.Equal(42, saved["other"]!.Value<int>());
        Assert.Equal(30, saved["toastIdleSeconds"]!.Value<int>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"toastHorizontalAlign\":\"invalid\",\"toastVerticalAlign\":12,\"toastDragEnabled\":\"true\"}")]
    [InlineData("{\"toastDragOffset\":{\"x\":\"NaN\",\"y\":20}}")]
    [InlineData("{\"toastDragOffset\":{\"x\":NaN,\"y\":20}}")]
    [InlineData("{\"toastDragOffset\":{\"x\":Infinity,\"y\":20}}")]
    [InlineData("{\"toastDragOffset\":{\"x\":12}}")]
    [InlineData("[]")]
    [InlineData("{")]
    public void Invalid_or_missing_position_values_use_safe_defaults(string json)
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, json);
        var loaded = PluginSettings.LoadToastPosition();
        Assert.False(loaded.Right || loaded.Bottom || loaded.DragEnabled || loaded.HasOffset);
    }

    [Fact]
    public void Position_save_failure_preserves_existing_file_and_session_options()
    {
        File.WriteAllText(PluginSettings.FilePathOverride!, "{");
        var options = new ToastPositionOptions(true, true, true, 20, 30);
        Assert.False(PluginSettings.SaveToastPosition(options));
        Assert.Equal("{", File.ReadAllText(PluginSettings.FilePathOverride!));
        Assert.True(options.Right && options.Bottom && options.HasOffset);
    }

    public void Dispose()
    {
        PluginSettings.FilePathOverride = null;
        Environment.SetEnvironmentVariable(PluginSettings.EnvEnableToast, _previousEnv);
        Environment.SetEnvironmentVariable(PluginSettings.EnvRecordCalls, _previousRecordEnv);
        try { Directory.Delete(_dir, true); } catch { }
    }
}
