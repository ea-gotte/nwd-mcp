using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Tools;

/// <summary>Toolset "timeliner_write": create and edit tasks, attach items, auto-attach by property, sync data sources.</summary>
[McpServerToolType]
public sealed class TimelinerWriteTools
{
    private const string FieldsHelp =
        "type must be one of the document's task types, which are localised (e.g. Construcción/Demoler/Temporal); see nwd_get_timeliner_config, case and accents are ignored. " +
        "Dates: yyyy-MM-dd, yyyy-MM-ddTHH:mm or dd/MM/yyyy; \"none\" clears. progress 0-100. userFieldsJson sets user1..user10, e.g. {\"user1\":\"Bloque A\"}. Only given fields change.";

    private readonly PluginClient _client;
    public TimelinerWriteTools(PluginClient client) => _client = client;
    private Task<string> Call(string command, JObject p, CancellationToken ct) => ToolCall.Run(_client, command, p, ct);

    internal static JObject Fields(JObject p, string? displayId, string? syncId, string? type, string? plannedStart, string? plannedEnd,
        string? actualStart, string? actualEnd, double? progress, bool? enabled, string? userFieldsJson)
    {
        void Put(string k, string? v) { if (v != null) p[k] = v; }
        Put("display_id", displayId); Put("sync_id", syncId); Put("type", type);
        Put("planned_start", plannedStart); Put("planned_end", plannedEnd); Put("actual_start", actualStart); Put("actual_end", actualEnd);
        if (progress.HasValue) p["progress"] = progress.Value;
        if (enabled.HasValue) p["enabled"] = enabled.Value;
        if (!string.IsNullOrEmpty(userFieldsJson))
            foreach (var prop in JObject.Parse(userFieldsJson).Properties()) p[prop.Name.ToLowerInvariant()] = prop.Value;
        return p;
    }

    [McpServerTool(Name = "nwd_create_timeliner_task", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Create a TimeLiner task under parentId (default the root) at index (default last), with optional fields and attached items. " + FieldsHelp + " " +
                 TimelinerTools.TaskIdHelp + " Items: " + ModelTools.ScopeHelp)]
    public Task<string> CreateTimelinerTask(string name, string? parentId = null, int? index = null, string? displayId = null, string? syncId = null, string? type = null,
        string? plannedStart = null, string? plannedEnd = null, string? actualStart = null, string? actualEnd = null, double? progress = null, string? userFieldsJson = null,
        string[]? itemIds = null, string? setId = null, string? selection = null, CancellationToken ct = default)
    {
        var p = Fields(ModelTools.ScopeArgs(itemIds, setId, selection), displayId, syncId, type, plannedStart, plannedEnd, actualStart, actualEnd, progress, null, userFieldsJson);
        p["name"] = name;
        if (!string.IsNullOrEmpty(parentId)) p["parent_id"] = parentId;
        if (index.HasValue) p["index"] = index.Value;
        return Call("create_timeliner_task", p, ct);
    }

    [McpServerTool(Name = "nwd_update_timeliner_task", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Change fields of a TimeLiner task (name, display/sync id, type, planned/actual dates, progress, enabled, user fields). " + FieldsHelp + " " + TimelinerTools.TaskIdHelp)]
    public Task<string> UpdateTimelinerTask(string taskId, string? name = null, string? displayId = null, string? syncId = null, string? type = null,
        string? plannedStart = null, string? plannedEnd = null, string? actualStart = null, string? actualEnd = null, double? progress = null, bool? enabled = null,
        string? userFieldsJson = null, CancellationToken ct = default)
    {
        var p = Fields(new JObject { ["task_id"] = taskId }, displayId, syncId, type, plannedStart, plannedEnd, actualStart, actualEnd, progress, enabled, userFieldsJson);
        if (!string.IsNullOrEmpty(name)) p["name"] = name;
        return Call("update_timeliner_task", p, ct);
    }

    [McpServerTool(Name = "nwd_delete_timeliner_task", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Delete a TimeLiner task and all its children (only Navisworks Undo restores them). " + TimelinerTools.TaskIdHelp)]
    public Task<string> DeleteTimelinerTask(string taskId, CancellationToken ct = default)
        => Call("delete_timeliner_task", new JObject { ["task_id"] = taskId }, ct);

    [McpServerTool(Name = "nwd_move_timeliner_task", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Move a TimeLiner task under parentId (default the root) at index (default last). " + TimelinerTools.TaskIdHelp)]
    public Task<string> MoveTimelinerTask(string taskId, string? parentId = null, int? index = null, CancellationToken ct = default)
    {
        var p = new JObject { ["task_id"] = taskId };
        if (!string.IsNullOrEmpty(parentId)) p["parent_id"] = parentId;
        if (index.HasValue) p["index"] = index.Value;
        return Call("move_timeliner_task", p, ct);
    }

    [McpServerTool(Name = "nwd_attach_items_to_task", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Attach model items to a TimeLiner task. mode = replace (default), add or clear. A setId is linked live to the set (the task follows the set) unless linkSet=false. " +
                 TimelinerTools.TaskIdHelp + " " + ModelTools.ScopeHelp + " " + ModelTools.AllHelp)]
    public Task<string> AttachItemsToTask(string taskId, string mode = "replace", string[]? itemIds = null, string? setId = null, string? selection = null,
        bool all = false, bool geometryOnly = false, bool linkSet = true, CancellationToken ct = default)
    {
        var p = mode == "clear" ? new JObject() : ModelTools.WithAll(ModelTools.ScopeArgs(itemIds, setId, selection), all, geometryOnly);
        p["task_id"] = taskId;
        p["mode"] = mode;
        if (!linkSet) p["link_set"] = false;
        return Call("attach_items_to_task", p, ct);
    }

    [McpServerTool(Name = "nwd_auto_attach_tasks_by_property", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Link model items to TimeLiner tasks in bulk by matching an item property (\"Category/Property\", e.g. a 4D task code) to a task field: taskKey = display_id (default), sync_id, name or user1..user10. " +
                 "dryRun=true (default) only reports matches, unmatched codes, tasks without items and duplicate keys; dryRun=false applies it as one Navisworks Undo step. mode = replace (default) or add. " +
                 "separator splits multi-code values (e.g. \";\"); matching is trimmed and case-insensitive unless caseSensitive=true. Default scope: every geometry item. " + ModelTools.ScopeHelp)]
    public Task<string> AutoAttachTasksByProperty(string property, string taskKey = "display_id", bool dryRun = true, string mode = "replace", string? separator = null,
        bool caseSensitive = false, string[]? itemIds = null, string? setId = null, string? selection = null, int? sample = null, CancellationToken ct = default)
    {
        var p = ModelTools.ScopeArgs(itemIds, setId, selection);
        p["property"] = property;
        p["task_key"] = taskKey;
        p["dry_run"] = dryRun;
        p["mode"] = mode;
        if (!string.IsNullOrEmpty(separator)) p["separator"] = separator;
        if (caseSensitive) p["case_sensitive"] = true;
        if (sample.HasValue) p["sample"] = sample.Value;
        return Call("auto_attach_tasks_by_property", p, ct);
    }

    [McpServerTool(Name = "nwd_sync_timeliner_data_source", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Re-import tasks from a TimeLiner data source (Primavera P6, MS Project, CSV… by its name in nwd_get_timeliner_config) and merge: mode = synchronize (default, updates matching tasks) or rebuild (replaces the task tree). importTaskType default true.")]
    public Task<string> SyncTimelinerDataSource(string dataSource, string mode = "synchronize", bool importTaskType = true, CancellationToken ct = default)
        => Call("sync_timeliner_data_source", new JObject { ["data_source"] = dataSource, ["mode"] = mode, ["import_task_type"] = importTaskType }, ct);

    [McpServerTool(Name = "nwd_recalculate_task_summaries", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Recalculate summary (parent) task dates and/or costs from their children across the whole tree, overwriting the parents' values.")]
    public Task<string> RecalculateTaskSummaries(bool dates = true, bool costs = true, CancellationToken ct = default)
        => Call("recalculate_task_summaries", new JObject { ["dates"] = dates, ["costs"] = costs }, ct);

    [McpServerTool(Name = "nwd_sort_timeliner_tasks", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Sort the TimeLiner task tree by field (planned_start_date default, planned_end_date, actual_start_date, display_id, simulation_task_type, progress_percent…), replacing the manual order.")]
    public Task<string> SortTimelinerTasks(string field = "planned_start_date", bool ascending = true, CancellationToken ct = default)
        => Call("sort_timeliner_tasks", new JObject { ["field"] = field, ["ascending"] = ascending }, ct);

    [McpServerTool(Name = "nwd_manage_simulation_appearance", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Add, update or delete a TimeLiner simulation appearance: action = add|update (name, color, transparency 0..1) or delete (name). Colours: #RRGGBB, r,g,b or a basic name.")]
    public Task<string> ManageSimulationAppearance(string action, string name, string? color = null, double? transparency = null, CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action, ["name"] = name };
        if (!string.IsNullOrEmpty(color)) p["color"] = color;
        if (transparency.HasValue) p["transparency"] = transparency.Value;
        return Call("manage_simulation_appearance", p, ct);
    }
}
