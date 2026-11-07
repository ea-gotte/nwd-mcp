#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;
using TL = Autodesk.Navisworks.Api.Timeliner;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>A task found in the TimeLiner tree with where it lives (for TaskEdit / TaskRemoveAt / TaskMove).</summary>
public sealed class TaskNode
{
    public TL.TimelinerTask Task { get; }
    public NW.GroupItem Parent { get; }
    public int Index { get; }
    public IReadOnlyList<int> Path { get; }
    public int Depth => Path.Count - 1;
    public string Id => TaskRef.FormatPath(Path);

    public TaskNode(TL.TimelinerTask task, NW.GroupItem parent, int index, IReadOnlyList<int> path)
    {
        Task = task; Parent = parent; Index = index; Path = path;
    }
}

public static class TimelinerHelper
{
    public static TL.DocumentTimeliner Tl(NW.Document doc)
        => TL.TimelinerDocumentExtensions.GetTimeliner(doc) ?? throw new CommandFailure("NO_TIMELINER", "TimeLiner is not available for this document");

    /// <summary>Every task, depth first, with its parent and index path.</summary>
    public static IEnumerable<TaskNode> Walk(TL.DocumentTimeliner tl)
    {
        IEnumerable<TaskNode> Rec(NW.GroupItem parent, List<int> prefix)
        {
            int i = 0;
            foreach (NW.SavedItem child in parent.Children)
            {
                if (child is TL.TimelinerTask t)
                {
                    var path = new List<int>(prefix) { i };
                    yield return new TaskNode(t, parent, i, path);
                    foreach (var n in Rec(t, path)) yield return n;
                }
                i++;
            }
        }
        return Rec(tl.TasksRoot, new List<int>());
    }

    public static TaskNode Resolve(TL.DocumentTimeliner tl, string? raw)
    {
        if (!TaskRef.TryParse(raw, out var r, out var error)) throw CommandFailure.Invalid(error);
        if (r!.Kind == TaskRefKind.IndexPath)
        {
            var hit = Walk(tl).FirstOrDefault(n => n.Path.SequenceEqual(r.Path));
            return hit ?? throw CommandFailure.NotFound($"no task at {TaskRef.FormatPath(r.Path)}");
        }
        Func<TL.TimelinerTask, string?> key = r.Kind switch
        {
            TaskRefKind.DisplayId => t => t.DisplayId,
            TaskRefKind.SynchronizationId => t => t.SynchronizationId,
            _ => t => t.DisplayName,
        };
        var matches = Walk(tl).Where(n => string.Equals(key(n.Task)?.Trim(), r.Key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (matches.Count == 0) throw CommandFailure.NotFound($"task '{raw}' not found");
        if (matches.Count > 1) throw CommandFailure.Invalid($"task '{raw}' is ambiguous; use its task: path from nwd_list_timeliner_tasks");
        return matches[0];
    }

    /// <summary>Key of a task for matching: display_id (default), sync_id, name or user1..user10.</summary>
    public static Func<TL.TimelinerTask, string?> KeyField(string? field)
    {
        var f = (field ?? "display_id").Trim().ToLowerInvariant();
        switch (f)
        {
            case "display_id": return t => t.DisplayId;
            case "sync_id": return t => t.SynchronizationId;
            case "name": return t => t.DisplayName;
        }
        if (f.StartsWith("user") && int.TryParse(f.Substring(4), out var n) && n >= 1 && n <= 10) return t => User(t, n);
        throw CommandFailure.Invalid("task_key must be display_id, sync_id, name or user1..user10");
    }

    public static string? User(TL.TimelinerTask t, int n) => n switch
    {
        1 => t.User1, 2 => t.User2, 3 => t.User3, 4 => t.User4, 5 => t.User5,
        6 => t.User6, 7 => t.User7, 8 => t.User8, 9 => t.User9, _ => t.User10,
    };

    public static void SetUser(TL.TimelinerTask t, int n, string? v)
    {
        switch (n)
        {
            case 1: t.User1 = v; break; case 2: t.User2 = v; break; case 3: t.User3 = v; break; case 4: t.User4 = v; break;
            case 5: t.User5 = v; break; case 6: t.User6 = v; break; case 7: t.User7 = v; break; case 8: t.User8 = v; break;
            case 9: t.User9 = v; break; default: t.User10 = v; break;
        }
    }

    public static JToken Date(DateTime? d) => d.HasValue ? (JToken)TaskDate.Format(d.Value) : JValue.CreateNull();

    public static JObject TaskJson(TaskNode n, NW.Document? docForItems = null)
    {
        var t = n.Task;
        var users = new JObject();
        for (int i = 1; i <= 10; i++) { var u = User(t, i); if (!string.IsNullOrEmpty(u)) users["user" + i] = u; }
        string? status = null;
        try { status = t.TaskStatus.ToString(); } catch (Exception) { }
        var o = new JObject
        {
            ["id"] = n.Id,
            ["depth"] = n.Depth,
            ["name"] = t.DisplayName,
            ["display_id"] = t.DisplayId,
            ["sync_id"] = t.SynchronizationId,
            ["type"] = t.SimulationTaskTypeName,
            ["enabled"] = t.IsEnabled,
            ["planned_start"] = Date(t.PlannedStartDate),
            ["planned_end"] = Date(t.PlannedEndDate),
            ["actual_start"] = Date(t.ActualStartDate),
            ["actual_end"] = Date(t.ActualEndDate),
            ["progress"] = t.ProgressPercent,
            ["status"] = status,
            ["children"] = t.Children.Count,
            ["data_source"] = string.IsNullOrEmpty(t.DataSourceName) ? null : t.DataSourceName,
            ["has_selection"] = t.Selection != null && !t.Selection.IsClear,
            ["selection"] = t.Selection == null || t.Selection.IsClear ? null : t.Selection.DisplayString,
        };
        var costs = new JObject();
        void Cost(string k, double? v) { if (v.HasValue) costs[k] = v.Value; }
        Cost("material", t.MaterialCost); Cost("labor", t.LaborCost); Cost("equipment", t.EquipmentCost); Cost("subcontractor", t.SubcontractorCost);
        if (costs.Count > 0) { Cost("total", t.TotalCost); o["costs"] = costs; }
        if (users.Count > 0) o["user_fields"] = users;
        if (docForItems != null) o["item_count"] = SelectedItems(t, docForItems).Count;
        return o;
    }

    public static List<NW.ModelItem> SelectedItems(TL.TimelinerTask t, NW.Document doc)
    {
        if (t.Selection == null || t.Selection.IsClear) return new List<NW.ModelItem>();
        try { return t.Selection.GetSelectedItems(doc).Cast<NW.ModelItem>().ToList(); }
        catch (Exception) { return new List<NW.ModelItem>(); }
    }

    /// <summary>Maps each attached item to the tasks that attach it directly.</summary>
    public static Dictionary<NW.ModelItem, List<TaskNode>> Attachments(TL.DocumentTimeliner tl, NW.Document doc, NwdCommandContext ctx)
    {
        var map = new Dictionary<NW.ModelItem, List<TaskNode>>();
        foreach (var n in Walk(tl))
        {
            if (ctx.PastDeadline) break;
            foreach (var item in SelectedItems(n.Task, doc))
            {
                if (!map.TryGetValue(item, out var list)) map[item] = list = new List<TaskNode>(1);
                list.Add(n);
            }
        }
        return map;
    }

    /// <summary>Tasks covering an item: attached to it or to any ancestor.</summary>
    public static List<TaskNode> TasksCovering(NW.ModelItem item, Dictionary<NW.ModelItem, List<TaskNode>> attachments)
    {
        var result = new List<TaskNode>();
        foreach (NW.ModelItem a in item.AncestorsAndSelf)
            if (attachments.TryGetValue(a, out var list))
                foreach (var n in list) if (!result.Contains(n)) result.Add(n);
        return result;
    }
}

/// <summary>Flattened TimeLiner task tree with dates, type, progress, costs and user fields. Filterable and paged.</summary>
public sealed class ListTimelinerTasksHandler : DocumentCommand
{
    public override string Name => "list_timeliner_tasks";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var page = Page(p);
        int maxDepth = Int(p, "max_depth", int.MaxValue, 0, int.MaxValue);
        var contains = ((string?)p["name_contains"])?.Trim();
        var type = ((string?)p["type"])?.Trim();
        bool countItems = Bool(p, "count_items", false);
        bool? hasSelection = p["has_selection"]?.Type == JTokenType.Boolean ? (bool?)p["has_selection"] : null;

        var nodes = TimelinerHelper.Walk(tl).Where(n =>
            n.Depth <= maxDepth &&
            (string.IsNullOrEmpty(contains) || (n.Task.DisplayName ?? "").IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0
                                            || (n.Task.DisplayId ?? "").IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (string.IsNullOrEmpty(type) || string.Equals(n.Task.SimulationTaskTypeName, type, StringComparison.OrdinalIgnoreCase)) &&
            (hasSelection == null || (n.Task.Selection != null && !n.Task.Selection.IsClear) == hasSelection.Value));
        var (slice, info) = page.Apply(nodes);
        return new JObject
        {
            ["total_tasks"] = tl.TaskTotalTasks(),
            ["tasks"] = new JArray(slice.Select(n => TimelinerHelper.TaskJson(n, countItems ? doc : null))),
            ["page"] = info,
        };
    }
}

/// <summary>One task in full, with the items it attaches (paged, any id form).</summary>
public sealed class GetTimelinerTaskHandler : DocumentCommand
{
    public override string Name => "get_timeliner_task";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var node = TimelinerHelper.Resolve(tl, (string?)p["task_id"]);
        var ids = Ids(p, doc);
        var items = TimelinerHelper.SelectedItems(node.Task, doc);
        var (slice, info) = Page(p).Apply(items);
        var o = TimelinerHelper.TaskJson(node);
        o["item_count"] = items.Count;
        o["items"] = new JArray(slice.Select(i => NwJson.ItemSummary(i, ids)));
        o["items_page"] = info;
        return o;
    }
}

/// <summary>Data sources, task types (with start/end appearances), simulation appearances and settings.</summary>
public sealed class GetTimelinerConfigHandler : DocumentCommand
{
    public override string Name => "get_timeliner_config";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        string? F(TL.TimelinerDataSourceField? f) => f == null || f.IsUndefined ? null : f.DisplayName;
        JToken S(TL.SimulationStatus? s) => s == null ? JValue.CreateNull() : new JObject { ["mode"] = s.AppearanceMode.ToString(), ["appearance"] = s.SimulationAppearanceName };

        var sources = new JArray(tl.DataSources.OfType<TL.TimelinerDataSource>().Select(d => new JObject
        {
            ["name"] = d.DisplayName,
            ["provider"] = d.DataSourceProviderName,
            ["provider_id"] = d.DataSourceProviderId,
            ["project"] = d.ProjectIdentifier,
            ["last_sync"] = TimelinerHelper.Date(d.LastSyncTime == default ? (DateTime?)null : d.LastSyncTime),
            ["fields"] = new JObject
            {
                ["sync_id"] = F(d.TaskSynchronizationIdField), ["type"] = F(d.TaskTypeField),
                ["planned_start"] = F(d.TaskPlannedStartField), ["planned_end"] = F(d.TaskPlannedEndField),
                ["actual_start"] = F(d.TaskActualStartField), ["actual_end"] = F(d.TaskActualEndField),
            },
        }));
        var types = new JArray(tl.SimulationTaskTypes.OfType<TL.SimulationTaskType>().Select(t => new JObject
        {
            ["name"] = t.DisplayName, ["start"] = S(t.StartStatus), ["end"] = S(t.EndStatus),
            ["early"] = S(t.UnderrunStatus), ["late"] = S(t.OverrunStatus), ["simulation_start"] = S(t.SimulationStartStatus),
        }));
        var appearances = new JArray(tl.SimulationAppearances.OfType<TL.SimulationAppearance>().Select(a => new JObject
        {
            ["name"] = a.DisplayName,
            ["color"] = new ColorSpec(a.Color.R, a.Color.G, a.Color.B).Hex,
            ["transparency"] = NwJson.R(a.Transparency),
        }));
        return new JObject
        {
            ["data_sources"] = sources,
            ["task_types"] = types,
            ["appearances"] = appearances,
            ["default_simulation_status"] = S(tl.Settings?.DefaultSimulationStatus),
            ["total_tasks"] = tl.TaskTotalTasks(),
        };
    }
}

/// <summary>
/// 4D coverage audit: leaf tasks without items, geometry items not covered by any task (directly or via an
/// ancestor), and items covered by more than one task.
/// </summary>
public sealed class TimelinerCoverageHandler : DocumentCommand
{
    public override string Name => "timeliner_coverage";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var ids = Ids(p, doc);
        int sample = Int(p, "sample", 50, 0, 5000);
        bool hasScope = p["item_ids"] != null || p["set_id"] != null || p["selection"] != null || p["all"] != null;
        JArray unresolved = new JArray();
        var items = hasScope ? Scope(p, doc, out unresolved) : NwJson.AllItems(doc).Where(i => i.HasGeometry).ToList();

        var attachments = TimelinerHelper.Attachments(tl, doc, ctx);
        var leafWithout = TimelinerHelper.Walk(tl).Where(n => n.Task.Children.Count == 0 && (n.Task.Selection == null || n.Task.Selection.IsClear)).ToList();

        var uncovered = new List<NW.ModelItem>();
        var multi = new List<(NW.ModelItem Item, List<TaskNode> Tasks)>();
        bool truncated = false;
        foreach (var item in items)
        {
            if (ctx.PastDeadline) { truncated = true; break; }
            var tasks = TimelinerHelper.TasksCovering(item, attachments);
            if (tasks.Count == 0) uncovered.Add(item);
            else if (tasks.Count > 1) multi.Add((item, tasks));
        }

        return new JObject
        {
            ["items_checked"] = items.Count,
            ["items_covered"] = items.Count - uncovered.Count,
            ["coverage_percent"] = items.Count == 0 ? 0 : Math.Round(100.0 * (items.Count - uncovered.Count) / items.Count, 2),
            ["uncovered"] = new JObject { ["count"] = uncovered.Count, ["sample"] = new JArray(uncovered.Take(sample).Select(i => NwJson.ItemSummary(i, ids))) },
            ["multiple_tasks"] = new JObject
            {
                ["count"] = multi.Count,
                ["sample"] = new JArray(multi.Take(sample).Select(x =>
                {
                    var o = NwJson.ItemSummary(x.Item, ids);
                    o["tasks"] = new JArray(x.Tasks.Select(t => new JObject { ["id"] = t.Id, ["name"] = t.Task.DisplayName, ["display_id"] = t.Task.DisplayId }));
                    return o;
                })),
            },
            ["leaf_tasks_without_items"] = new JObject
            {
                ["count"] = leafWithout.Count,
                ["sample"] = new JArray(leafWithout.Take(sample).Select(n => new JObject { ["id"] = n.Id, ["name"] = n.Task.DisplayName, ["display_id"] = n.Task.DisplayId })),
            },
            ["truncated_by_deadline"] = truncated,
            ["unresolved"] = unresolved,
        };
    }
}

/// <summary>Which tasks cover each item (directly or through an ancestor). Paged.</summary>
public sealed class FindTasksForItemsHandler : DocumentCommand
{
    public override string Name => "find_tasks_for_items";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var ids = Ids(p, doc);
        var items = Scope(p, doc, out var unresolved);
        var (slice, info) = Page(p).Apply(items);
        var attachments = TimelinerHelper.Attachments(tl, doc, ctx);
        var rows = new JArray(slice.Select(i =>
        {
            var o = NwJson.ItemSummary(i, ids);
            o["tasks"] = new JArray(TimelinerHelper.TasksCovering(i, attachments).Select(t => new JObject
            {
                ["id"] = t.Id, ["name"] = t.Task.DisplayName, ["display_id"] = t.Task.DisplayId,
                ["planned_start"] = TimelinerHelper.Date(t.Task.PlannedStartDate), ["planned_end"] = TimelinerHelper.Date(t.Task.PlannedEndDate),
            }));
            return o;
        }));
        return new JObject { ["items"] = rows, ["unresolved"] = unresolved, ["page"] = info };
    }
}

/// <summary>Writes the task tree to CSV (exports folder). Does not change the document.</summary>
public sealed class ExportTimelinerScheduleHandler : DocumentCommand
{
    public override string Name => "export_timeliner_schedule";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        bool countItems = Bool(p, "count_items", true);
        var columns = new List<string> { "id", "depth", "name", "display_id", "sync_id", "type", "planned_start", "planned_end", "actual_start", "actual_end", "progress", "item_count" };
        for (int i = 1; i <= 10; i++) columns.Add("user" + i);
        var rows = new List<IReadOnlyList<string?>>();
        foreach (var n in TimelinerHelper.Walk(tl))
        {
            var t = n.Task;
            string? D(DateTime? d) => d.HasValue ? TaskDate.Format(d.Value) : null;
            var row = new List<string?>
            {
                n.Id, n.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture), t.DisplayName, t.DisplayId, t.SynchronizationId, t.SimulationTaskTypeName,
                D(t.PlannedStartDate), D(t.PlannedEndDate), D(t.ActualStartDate), D(t.ActualEndDate),
                t.ProgressPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                countItems ? TimelinerHelper.SelectedItems(t, doc).Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            };
            for (int i = 1; i <= 10; i++) row.Add(TimelinerHelper.User(t, i));
            rows.Add(row);
        }
        string path;
        try { path = TableExport.WriteCsvFile((string?)p["file_name"], "timeliner", columns, rows); }
        catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
        { throw new CommandFailure("EXPORT_FAILED", "could not write the CSV: " + ex.GetType().Name); }
        return new JObject { ["path"] = path, ["format"] = "csv", ["rows"] = rows.Count, ["columns"] = new JArray(columns) };
    }
}
#endif
