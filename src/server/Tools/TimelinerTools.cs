using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Tools;

/// <summary>Toolset "timeliner": read-only 4D schedule inspection and coverage audits.</summary>
[McpServerToolType]
public sealed class TimelinerTools
{
    internal const string TaskIdHelp =
        "Task ids: task:2/0/5 (index path from nwd_list_timeliner_tasks), display:<DisplayId>, sync:<SynchronizationId>, or the task name.";

    private readonly PluginClient _client;
    public TimelinerTools(PluginClient client) => _client = client;
    private Task<string> Call(string command, JObject p, CancellationToken ct) => ToolCall.Run(_client, command, p, ct);

    [McpServerTool(Name = "nwd_list_timeliner_tasks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("TimeLiner tasks as a flattened tree (id, depth, name, display id, sync id, type, planned/actual dates, progress, status, costs, user fields, attached selection). " +
                 "Filters: nameContains (name or display id), type, maxDepth, hasSelection. countItems=true adds item_count (slower). " + ModelTools.PageHelp)]
    public Task<string> ListTimelinerTasks(string? nameContains = null, string? type = null, int? maxDepth = null, bool? hasSelection = null, bool countItems = false,
        int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = ModelTools.ScopeArgs(null, null, null, limit, offset, cursor);
        if (!string.IsNullOrEmpty(nameContains)) p["name_contains"] = nameContains;
        if (!string.IsNullOrEmpty(type)) p["type"] = type;
        if (maxDepth.HasValue) p["max_depth"] = maxDepth.Value;
        if (hasSelection.HasValue) p["has_selection"] = hasSelection.Value;
        if (countItems) p["count_items"] = true;
        return Call("list_timeliner_tasks", p, ct);
    }

    [McpServerTool(Name = "nwd_get_timeliner_task", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("One TimeLiner task in full plus the model items it attaches (paged). " + TaskIdHelp + " " + ModelTools.IdModeHelp + " " + ModelTools.PageHelp)]
    public Task<string> GetTimelinerTask(string taskId, string? idMode = null, int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = ModelTools.ScopeArgs(null, null, null, limit, offset, cursor);
        p["task_id"] = taskId;
        return Call("get_timeliner_task", ModelTools.WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_get_timeliner_config", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("TimeLiner configuration: data sources (provider, project, last sync, mapped fields), task types with their start/end appearances, simulation appearances (colour, transparency) and default status.")]
    public Task<string> GetTimelinerConfig(CancellationToken ct = default) => Call("get_timeliner_config", new JObject(), ct);

    [McpServerTool(Name = "nwd_timeliner_coverage", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("4D coverage audit: % of geometry items covered by a task (directly or via an ancestor), uncovered items, items in more than one task, and leaf tasks with no items, each with a sample (default 50). " +
                 "Default scope is every geometry item; or give a scope. " + ModelTools.ScopeHelp + " " + ModelTools.IdModeHelp)]
    public Task<string> TimelinerCoverage(string[]? itemIds = null, string? setId = null, string? selection = null, int? sample = null, string? idMode = null, CancellationToken ct = default)
    {
        var p = ModelTools.ScopeArgs(itemIds, setId, selection);
        if (sample.HasValue) p["sample"] = sample.Value;
        return Call("timeliner_coverage", ModelTools.WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_find_tasks_for_items", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("For each item, the TimeLiner tasks that cover it (attached to it or an ancestor) with planned dates. " + ModelTools.ScopeHelp + " " + ModelTools.IdModeHelp + " " + ModelTools.PageHelp)]
    public Task<string> FindTasksForItems(string[]? itemIds = null, string? setId = null, string? selection = null, string? idMode = null,
        int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
        => Call("find_tasks_for_items", ModelTools.WithIdMode(ModelTools.ScopeArgs(itemIds, setId, selection, limit, offset, cursor), idMode), ct);

    [McpServerTool(Name = "nwd_export_timeliner_schedule", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Write the whole task tree to CSV (UTF-8 BOM, Excel-ready) in %LOCALAPPDATA%\\Bimwright\\nwd-mcp\\exports: id, depth, name, display/sync ids, type, dates, progress, item_count (countItems, default true) and user1..user10. Does not change the document.")]
    public Task<string> ExportTimelinerSchedule(string? fileName = null, bool countItems = true, CancellationToken ct = default)
    {
        var p = new JObject { ["count_items"] = countItems };
        if (!string.IsNullOrEmpty(fileName)) p["file_name"] = fileName;
        return Call("export_timeliner_schedule", p, ct);
    }
}
