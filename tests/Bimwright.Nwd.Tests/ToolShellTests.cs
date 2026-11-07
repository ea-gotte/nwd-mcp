using System.Reflection;
using Bimwright.Nwd.Server;
using Bimwright.Nwd.Server.Bake;
using Bimwright.Nwd.Server.Tools;
using Bimwright.Nwd.Shared.Infrastructure;
using Bimwright.Nwd.Tests.Helpers;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Tests;

/// <summary>
/// One shell case per MCP tool: the server-side wrapper is called against a loopback fake plug-in,
/// and the NDJSON envelope it sends (command, snake_case params, timeout) is pinned. Tools that never
/// reach the plug-in (targets, ToolBaker bookkeeping) are pinned by their local result instead.
/// Live timings for the same tools are recorded in the private smoke notes.
/// </summary>
public sealed class ToolShellTests
{
    private const int DefaultTimeoutMs = 30000;
    private const int FileTimeoutMs = PluginClient.FileOperationTimeoutMs;
    private const string Nwc = @"C:\models\site.nwc";

    private sealed record Case(string Tool, string Command, Func<FakePlugin, Task<string>> Call, string ParamsJson, int TimeoutMs = DefaultTimeoutMs);

    private static readonly CancellationToken Ct = CancellationToken.None;

    private static readonly Case[] PluginCases =
    {
        // files / files_write
        new("nwd_list_recent_files", "list_recent_files", p => new FileTools(p.Client).ListRecentFiles(10, Ct), """{"limit":10}"""),
        new("nwd_open_file", "open_file", p => new FileWriteTools(p.Client).OpenFile(Nwc, false, Ct), """{"path":"C:\\models\\site.nwc","discard_changes":false}""", FileTimeoutMs),
        new("nwd_import_model", "import_model", p => new FileWriteTools(p.Client).ImportModel(Nwc, "merge", Ct), """{"path":"C:\\models\\site.nwc","mode":"merge"}""", FileTimeoutMs),
        // query
        new("nwd_health_check", "health_check", p => new QueryTools(p.Client).HealthCheck(Ct), "{}"),
        new("nwd_get_document_info", "get_document_info", p => new QueryTools(p.Client).GetDocumentInfo(Ct), "{}"),
        new("nwd_get_model_statistics", "get_model_statistics", p => new QueryTools(p.Client).GetModelStatistics(Ct), "{}"),
        new("nwd_get_model_tree", "get_model_tree", p => new QueryTools(p.Client).GetModelTree(3, 50, "inline", Ct), """{"max_depth":3,"max_items":50}"""),
        new("nwd_get_item_properties", "get_item_properties", p => new QueryTools(p.Client).GetItemProperties("0:0:6", Ct), """{"item_id":"0:0:6"}"""),
        new("nwd_batch_get_properties", "batch_get_properties", p => new QueryTools(p.Client).BatchGetProperties(new[] { "0:0", "0:1" }, 2, "inline", Ct), """{"item_ids":["0:0","0:1"],"max_items":2}"""),
        new("nwd_find_items", "find_items", p => new QueryTools(p.Client).FindItems("""{"category":"Item","property":"Name","operator":"contains","value":"Pipe"}""", 5, Ct), """{"filters":{"category":"Item","property":"Name","operator":"contains","value":"Pipe"},"max_items":5}"""),
        new("nwd_find_items_by_name", "find_items_by_name", p => new QueryTools(p.Client).FindItemsByName("Pipe", true, 5, "inline", Ct), """{"name":"Pipe","exact":true,"max_items":5}"""),
        // selection / selection_write
        new("nwd_get_current_selection", "get_current_selection", p => new SelectionTools(p.Client).GetCurrentSelection(Ct), "{}"),
        new("nwd_clear_selection", "clear_selection", p => new SelectionWriteTools(p.Client).ClearSelection(Ct), "{}"),
        new("nwd_select_items_by_search", "select_items_by_search", p => new SelectionWriteTools(p.Client).SelectItemsBySearch("""[{"value":"Pipe"}]""", Ct), """{"filters":[{"value":"Pipe"}]}"""),
        // sets
        new("nwd_list_sets", "list_sets", p => new SetsTools(p.Client, new ServerState(p.Config)).ListSets(Ct), "{}"),
        new("nwd_get_selection_set_items", "get_selection_set_items", p => new SetsTools(p.Client, new ServerState(p.Config)).GetSelectionSetItems("Clash/Pipes", Ct), """{"set_id":"Clash/Pipes"}"""),
        new("nwd_execute_search_set", "execute_search_set", p => new SetsTools(p.Client, new ServerState(p.Config)).ExecuteSearchSet("Clash/Pipes", true, Ct), """{"set_id":"Clash/Pipes","select":true}"""),
        // view / view_write
        new("nwd_list_viewpoints", "list_viewpoints", p => new ViewTools(p.Client).ListViewpoints(Ct), "{}"),
        new("nwd_get_current_viewpoint", "get_current_viewpoint", p => new ViewTools(p.Client).GetCurrentViewpoint(Ct), "{}"),
        new("nwd_goto_viewpoint", "goto_viewpoint", p => new ViewWriteTools(p.Client).GotoViewpoint("3D View/{3D}", Ct), """{"viewpoint_id":"3D View/{3D}"}"""),
        new("nwd_save_viewpoint", "save_viewpoint", p => new ViewWriteTools(p.Client).SaveViewpoint("review-1", Ct), """{"name":"review-1"}"""),
        // visibility
        new("nwd_hide_items", "hide_items", p => new VisibilityWriteTools(p.Client).HideItems(new[] { "0:0:6" }, false, Ct), """{"item_ids":["0:0:6"],"hide":false}"""),
        new("nwd_unhide_all", "unhide_all", p => new VisibilityWriteTools(p.Client).UnhideAll(Ct), "{}"),
        // model
        new("nwd_resolve_ids", "resolve_ids", p => new ModelTools(p.Client).ResolveIds(new[] { "0:0:6", "pid:0:abc" }, null, null, 50, 10, null, Ct), """{"item_ids":["0:0:6","pid:0:abc"],"limit":50,"offset":10}"""),
        new("nwd_list_models", "list_models", p => new ModelTools(p.Client).ListModels(Ct), "{}"),
        new("nwd_get_document_metadata", "get_document_metadata", p => new ModelTools(p.Client).GetDocumentMetadata(Ct), "{}"),
        new("nwd_peek_file_info", "peek_file_info", p => new ModelTools(p.Client).PeekFileInfo(Nwc, Ct), """{"path":"C:\\models\\site.nwc"}"""),
        new("nwd_get_scene_bounds", "get_scene_bounds", p => new ModelTools(p.Client).GetSceneBounds(false, Ct), """{"ignore_hidden":false}"""),
        new("nwd_get_item_hierarchy", "get_item_hierarchy", p => new ModelTools(p.Client).GetItemHierarchy("guid:6f1c2c1e-1d5b-4a3a-9c1e-0a1b2c3d4e5f", "pid", 20, null, null, Ct), """{"item_id":"guid:6f1c2c1e-1d5b-4a3a-9c1e-0a1b2c3d4e5f","id_mode":"pid","limit":20}"""),
        new("nwd_get_items_geometry", "get_items_geometry", p => new ModelTools(p.Client).GetItemsGeometry(null, "Clash/Pipes", null, false, true, true, "guid", null, null, "bzoxMA==", Ct), """{"set_id":"Clash/Pipes","geometry_only":true,"ignore_hidden":true,"id_mode":"guid","cursor":"bzoxMA=="}"""),
        new("nwd_get_visibility_state", "get_visibility_state", p => new ModelTools(p.Client).GetVisibilityState(null, 25, Ct), """{"limit":25}"""),
        new("nwd_describe_properties", "describe_properties", p => new ModelTools(p.Client).DescribeProperties(null, null, "current", false, false, 100, Ct), """{"selection":"current","max_scan":100}"""),
        new("nwd_property_value_histogram", "property_value_histogram", p => new ModelTools(p.Client).PropertyValueHistogram("Element/B_02_Cod-Tarea-4D", null, null, null, true, true, 10, null, Ct), """{"all":true,"geometry_only":true,"property":"Element/B_02_Cod-Tarea-4D","top":10}"""),
        new("nwd_export_properties_table", "export_properties_table", p => new ModelTools(p.Client).ExportPropertiesTable(new[] { "Item/Name", "Element/C_05_Disciplina" }, null, null, null, true, true, false, "ecoplanta", 1000, "pid", Ct), """{"all":true,"geometry_only":true,"properties":["Item/Name","Element/C_05_Disciplina"],"include_name":false,"file_name":"ecoplanta","max_rows":1000,"id_mode":"pid"}"""),
        new("nwd_get_hyperlinks", "get_hyperlinks", p => new ModelTools(p.Client).GetHyperlinks(new[] { "0:1" }, null, null, false, null, 5, null, null, Ct), """{"item_ids":["0:1"],"limit":5}"""),
        new("nwd_get_grids", "get_grids", p => new ModelTools(p.Client).GetGrids(Ct), "{}"),
        new("nwd_locate_items_on_grid", "locate_items_on_grid", p => new ModelTools(p.Client).LocateItemsOnGrid(null, null, "current", "Structural", null, null, null, null, Ct), """{"selection":"current","grid_system":"Structural"}"""),
        new("nwd_get_comments", "get_comments", p => new ModelTools(p.Client).GetComments("viewpoints", 10, 20, null, Ct), """{"source":"viewpoints","limit":10,"offset":20}"""),
        new("nwd_list_sheets", "list_sheets", p => new ModelTools(p.Client).ListSheets(Ct), "{}"),
        new("nwd_list_alignments", "list_alignments", p => new ModelTools(p.Client).ListAlignments(Ct), "{}"),
        // review / review_write
        new("nwd_capture_image", "capture_image", p => new ReviewTools(p.Client).CaptureImage(800, 600, "overlay", false, "clash-01", Ct), """{"width":800,"height":600,"style":"overlay","sectioning":false,"file_name":"clash-01"}"""),
        new("nwd_get_section", "get_section", p => new ReviewTools(p.Client).GetSection(Ct), "{}"),
        new("nwd_get_redlines", "get_redlines", p => new ReviewTools(p.Client).GetRedlines(Ct), "{}"),
        new("nwd_select_items", "select_items", p => new ReviewWriteTools(p.Client).SelectItems("add", new[] { "pid:0:a" }, null, null, false, Ct), """{"item_ids":["pid:0:a"],"mode":"add"}"""),
        new("nwd_isolate_items", "isolate_items", p => new ReviewWriteTools(p.Client).IsolateItems(null, "Clash/Pipes", null, false, Ct), """{"set_id":"Clash/Pipes","reset_hidden_first":false}"""),
        new("nwd_set_item_flags", "set_item_flags", p => new ReviewWriteTools(p.Client).SetItemFlags("required", true, null, null, "current", false, false, Ct), """{"selection":"current","flag":"required","value":true}"""),
        new("nwd_reset_visibility", "reset_visibility", p => new ReviewWriteTools(p.Client).ResetVisibility("all", Ct), """{"which":"all"}"""),
        new("nwd_override_appearance", "override_appearance", p => new ReviewWriteTools(p.Client).OverrideAppearance("#FF0000", 0.5, false, new[] { "0:1" }, null, null, false, false, Ct), """{"item_ids":["0:1"],"color":"#FF0000","transparency":0.5,"permanent":false}"""),
        new("nwd_color_by_property", "color_by_property", p => new ReviewWriteTools(p.Client).ColorByProperty("Element/C_05_Disciplina", null, null, null, true, true, """{"MEC":"red"}""", "grey", 20, false, Ct), """{"all":true,"geometry_only":true,"property":"Element/C_05_Disciplina","colors":{"MEC":"red"},"missing_color":"grey","max_values":20,"permanent":false}"""),
        new("nwd_reset_appearance", "reset_appearance", p => new ReviewWriteTools(p.Client).ResetAppearance("both", null, null, null, false, Ct), """{"kind":"both"}"""),
        new("nwd_zoom_to_items", "zoom_to_items", p => new ReviewWriteTools(p.Client).ZoomToItems(new[] { "0:1" }, null, null, true, Ct), """{"item_ids":["0:1"],"select":true}"""),
        new("nwd_set_camera", "set_camera", p => new ReviewWriteTools(p.Client).SetCamera(new[] { 1.0, 2, 3 }, new[] { 0.0, 0, 0 }, null, "orthographic", "shaded", null, null, Ct), """{"position":[1.0,2.0,3.0],"target":[0.0,0.0,0.0],"projection":"orthographic","render_style":"shaded"}"""),
        new("nwd_set_section", "set_section", p => new ReviewWriteTools(p.Client).SetSection("box", null, null, "current", 0.5, true, null, Ct), """{"selection":"current","mode":"box","padding":0.5,"zoom":true}"""),
        new("nwd_set_background", "set_background", p => new ReviewWriteTools(p.Client).SetBackground("graduated", null, "#FFFFFF", "grey", null, null, null, null, Ct), """{"mode":"graduated","top_color":"#FFFFFF","bottom_color":"grey"}"""),
        new("nwd_set_redlines", "set_redlines", p => new ReviewWriteTools(p.Client).SetRedlines("[]", Ct), """{"redlines_json":"[]"}"""),
        new("nwd_manage_saved_viewpoint", "manage_saved_viewpoint", p => new ReviewWriteTools(p.Client).ManageSavedViewpoint("move", "path:/Review/V1", null, "path:/Archive", 0, Ct), """{"action":"move","viewpoint_id":"path:/Review/V1","folder_id":"path:/Archive","index":0}"""),
        new("nwd_add_comment", "add_comment", p => new ReviewWriteTools(p.Client).AddComment("viewpoint", "path:/Review/V1", "Revisar soporte", "Active", null, Ct), """{"target":"viewpoint","id":"path:/Review/V1","body":"Revisar soporte","status":"Active"}"""),
        new("nwd_save_viewpoint_with_overrides", "save_viewpoint_with_overrides", p => new ReviewWriteTools(p.Client).SaveViewpointWithOverrides("Disciplinas", "path:/Review", Ct), """{"name":"Disciplinas","folder_id":"path:/Review"}"""),
        new("nwd_batch_viewpoint_images", "batch_viewpoint_images", p => new ReviewWriteTools(p.Client).BatchViewpointImages("path:/Review", 640, 480, "scene", 10, Ct), """{"width":640,"height":480,"style":"scene","max_images":10,"folder_id":"path:/Review"}"""),
        // code
        new("nwd_send_code", "send_code", p => new CodeTools(p.Client).SendCode("Console.WriteLine(1);", Ct), """{"code":"Console.WriteLine(1);"}"""),
    };

    /// <summary>Tools answered by the server alone; each has its own fact below.</summary>
    private static readonly string[] LocalTools =
    {
        "nwd_list_available_targets", "nwd_get_current_target", "nwd_switch_target",
        "nwd_list_baked_tools", "nwd_list_bake_suggestions", "nwd_create_bake_issue_draft",
        "nwd_run_baked_tool", "nwd_accept_bake_suggestion", "nwd_dismiss_bake_suggestion",
    };

    public static IEnumerable<object[]> PluginCaseNames() => PluginCases.Select(c => new object[] { c.Tool });

    [Fact]
    public void EveryToolHasAShellCase()
    {
        var surface = Program.ResolveToolTypesForRegistration(new NwdMcpConfig { Toolsets = new() { "all" }, EnableSendCode = true })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n != null)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        var covered = PluginCases.Select(c => c.Tool).Concat(LocalTools).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(surface, covered);
    }

    [Theory]
    [MemberData(nameof(PluginCaseNames))]
    public async Task PluginToolSendsPinnedEnvelope(string tool)
    {
        var c = PluginCases.Single(x => x.Tool == tool);
        using var plugin = new FakePlugin();

        var text = await c.Call(plugin);

        var env = plugin.Single();
        Assert.Equal(c.Command, env.Command);
        Assert.True(JToken.DeepEquals(JObject.Parse(c.ParamsJson), env.Params), $"{tool} params: {env.Params.ToString(Newtonsoft.Json.Formatting.None)}");
        Assert.Equal(c.TimeoutMs, env.TimeoutMs);
        Assert.Equal("token", env.AuthToken);
        Assert.Equal(c.Command, (string?)JObject.Parse(text)["echo"]);
    }

    [Theory]
    [MemberData(nameof(PluginCaseNames))]
    public async Task PluginFailureComesBackAsErrorJson(string tool)
    {
        var c = PluginCases.Single(x => x.Tool == tool);
        using var plugin = new FakePlugin
        {
            Reply = env => NwdCommandResult.Fail(env.Id, "NO_DOCUMENT", "no active Navisworks document", new NwdResponseMeta())
        };

        var result = JObject.Parse(await c.Call(plugin));

        Assert.False((bool?)result["ok"]);
        Assert.Equal("NO_DOCUMENT", (string?)result["error"]?["code"]);
    }

    [Fact]
    public async Task ServerRejectsAReplyOverMaxResponseBytes()
    {
        using var plugin = new FakePlugin
        {
            Reply = env => NwdCommandResult.Success(env.Id, new JObject { ["roots"] = new string('x', 4096) }, new NwdResponseMeta())
        };
        plugin.Config.MaxResponseBytes = 1024;

        var result = JObject.Parse(await new QueryTools(plugin.Client).GetModelTree(30, 1000000, "inline", Ct));

        Assert.Equal("RESPONSE_TOO_LARGE", (string?)result["error"]?["code"]);
    }

    [Fact]
    public async Task ToolResultsAreCompactJson()
    {
        using var plugin = new FakePlugin();
        var text = await new QueryTools(plugin.Client).HealthCheck(Ct);
        Assert.DoesNotContain("\n", text);
    }

    [Fact]
    public async Task NoLiveTargetFailsWithoutThrowing()
    {
        using var plugin = new FakePlugin();
        var client = new PluginClient(new NwdMcpConfig { DescriptorDirectory = Path.Combine(Path.GetTempPath(), "nwd-none-" + Guid.NewGuid().ToString("N")) });

        var result = JObject.Parse(await new QueryTools(client).HealthCheck(Ct));

        Assert.Equal("NO_TARGET", (string?)result["error"]?["code"]);
        Assert.Empty(plugin.Received);
    }

    [Fact]
    public async Task ExecuteSearchSetForcesSelectOffInReadOnlyMode()
    {
        using var plugin = new FakePlugin(readOnly: true);

        await new SetsTools(plugin.Client, new ServerState(plugin.Config)).ExecuteSearchSet("Clash/Pipes", true, Ct);

        var env = plugin.Single();
        Assert.False((bool?)env.Params["select"]);
        Assert.True((bool?)env.Params["read_only_enforced"]);
    }

    [Fact]
    public void MetaToolsReadTheDescriptorWithoutCallingThePlugin()
    {
        using var plugin = new FakePlugin();
        var meta = new MetaTools(plugin.Client);

        var targets = JArray.Parse(meta.ListAvailableTargets());
        var current = JObject.Parse(meta.GetCurrentTarget());
        var switched = JObject.Parse(meta.SwitchTarget(plugin.TargetId));
        var unknown = JObject.Parse(meta.SwitchTarget("navis-9999-1"));

        Assert.Equal(plugin.TargetId, (string?)Assert.Single(targets)["TargetId"]);
        Assert.Equal(2025, (int?)current["NavisworksYear"]);
        Assert.True((bool?)switched["ok"]);
        Assert.False((bool?)unknown["ok"]);
        Assert.Empty(plugin.Received);
    }

    [Fact]
    public async Task ToolBakerToolsUseTheLocalStoreAndRejectUnknownIds()
    {
        using var plugin = new FakePlugin();
        var read = new ToolBakerTools(plugin.Config);
        var write = new ToolBakerWriteTools(plugin.Client, plugin.Config);

        Assert.Empty((JArray)JObject.Parse(read.ListBakedTools())["tools"]!);
        Assert.Empty((JArray)JObject.Parse(read.ListBakeSuggestions())["suggestions"]!);
        Assert.Equal("not_found", (string?)JObject.Parse(read.CreateBakeIssueDraft("missing"))["error_code"]);
        Assert.Equal("INVALID_ARGUMENT", (string?)JObject.Parse(await write.RunBakedTool("missing", "{}", "inline", Ct))["error"]?["code"]);
        Assert.Equal("INVALID_ARGUMENT", (string?)JObject.Parse(await write.RunBakedTool("missing", "not json", "inline", Ct))["error"]?["code"]);
        Assert.Equal("not_found", (string?)JObject.Parse(await write.AcceptBakeSuggestion("missing", "my_tool", Ct))["error_code"]);
        Assert.Equal("not_found", (string?)JObject.Parse(await write.DismissBakeSuggestion("missing", Ct))["error_code"]);
        Assert.Empty(plugin.Received);
    }

    [Fact]
    public async Task OutputFileIsNotForwardedToThePlugin()
    {
        using var plugin = new FakePlugin();
        var query = new QueryTools(plugin.Client);

        await query.GetModelTree(2, 10, "file", Ct);
        await query.BatchGetProperties(new[] { "0:0" }, 5, "file", Ct);
        await query.FindItemsByName("Pipe", false, 5, "file", Ct);
        await RunBakedToolWithFileOutput(plugin);

        var envs = plugin.Received.ToArray();
        Assert.Equal(4, envs.Length);
        foreach (var env in envs)
            Assert.Null(env.Params["output"]);
    }

    private static async Task RunBakedToolWithFileOutput(FakePlugin plugin)
    {
        BakePaths.EnsureDir(plugin.Config);
        using (var db = new BakeDb(BakePaths.Db(plugin.Config)))
        {
            db.Migrate();
            Assert.True(db.TryInsertRegistryRecord(new Bimwright.Nwd.Shared.ToolBaker.BakedToolRecord
            {
                Name = "fake_tool",
                Description = "test",
                Source = "test",
                HandlerTool = "fake_tool"
            }));
        }

        await new ToolBakerWriteTools(plugin.Client, plugin.Config).RunBakedTool("fake_tool", "{}", "file", Ct);
    }

    [Fact]
    public async Task InvalidOutputIsRejectedWithoutCallingThePlugin()
    {
        using var plugin = new FakePlugin();
        var query = new QueryTools(plugin.Client);

        var tree = JObject.Parse(await query.GetModelTree(2, 10, "weird", Ct));
        var batch = JObject.Parse(await query.BatchGetProperties(new[] { "0:0" }, 5, "weird", Ct));
        var find = JObject.Parse(await query.FindItemsByName("Pipe", false, 5, "weird", Ct));
        var baked = JObject.Parse(await new ToolBakerWriteTools(plugin.Client, plugin.Config).RunBakedTool("x", "{}", "weird", Ct));

        foreach (var result in new[] { tree, batch, find, baked })
            Assert.Equal("INVALID_ARGUMENT", (string?)result["error"]?["code"]);
        Assert.Empty(plugin.Received);
    }
}
