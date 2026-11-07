#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;
using TL = Autodesk.Navisworks.Api.Timeliner;

namespace Bimwright.Nwd.Shared.Handlers;

public abstract class TimelinerWriteCommand : DocumentCommand
{
    public override bool IsReadOnly => false;
    protected override bool RequiresModels => false;

    /// <summary>Applies the fields present in p (others stay as they are) to a task copy.</summary>
    protected static List<string> ApplyFields(TL.TimelinerTask t, JObject p)
    {
        var changed = new List<string>();

        // Validate both ranges first: Navisworks silently moves the other date when one would invert the range.
        DateTime? Peek(string key, out bool has)
        {
            has = p[key] != null;
            if (!has) return null;
            if (!TaskDate.TryParse((string?)p[key], out var d, out var clear, out var e)) throw CommandFailure.Invalid(key + ": " + e);
            return clear ? null : d;
        }
        var ps = Peek("planned_start", out var hps); var pe = Peek("planned_end", out var hpe);
        if (!TaskDate.CheckRange(ps, hps, pe, hpe, t.PlannedStartDate, t.PlannedEndDate, out var rangeError)) throw CommandFailure.Invalid("planned " + rangeError);
        var a0 = Peek("actual_start", out var has0); var a1 = Peek("actual_end", out var has1);
        if (!TaskDate.CheckRange(a0, has0, a1, has1, t.ActualStartDate, t.ActualEndDate, out rangeError)) throw CommandFailure.Invalid("actual " + rangeError);

        void Date(string key, Action<DateTime?> set)
        {
            if (p[key] == null) return;
            if (!TaskDate.TryParse((string?)p[key], out var d, out var clear, out var e)) throw CommandFailure.Invalid(e);
            set(clear ? null : d);
            changed.Add(key);
        }
        void Num(string key, Action<double?> set)
        {
            var tok = p[key];
            if (tok == null) return;
            if (tok.Type == JTokenType.Null) { set(null); changed.Add(key); return; }
            if (tok.Type != JTokenType.Float && tok.Type != JTokenType.Integer) throw CommandFailure.Invalid(key + " must be a number");
            set((double)tok);
            changed.Add(key);
        }
        void Str(string key, Action<string?> set)
        {
            if (p[key] == null) return;
            set((string?)p[key]);
            changed.Add(key);
        }

        Str("display_id", v => t.DisplayId = v);
        Str("sync_id", v => t.SynchronizationId = v);
        Str("type", v => t.SimulationTaskTypeName = v);
        Date("planned_start", v => t.PlannedStartDate = v);
        Date("planned_end", v => t.PlannedEndDate = v);
        Date("actual_start", v => t.ActualStartDate = v);
        Date("actual_end", v => t.ActualEndDate = v);
        Num("progress", v =>
        {
            if (v.HasValue && (v < 0 || v > 100)) throw CommandFailure.Invalid("progress must be between 0 and 100");
            t.ProgressPercent = v;
        });
        Num("material_cost", v => t.MaterialCost = v);
        Num("labor_cost", v => t.LaborCost = v);
        Num("equipment_cost", v => t.EquipmentCost = v);
        Num("subcontractor_cost", v => t.SubcontractorCost = v);
        if (p["enabled"]?.Type == JTokenType.Boolean) { t.IsEnabled = (bool)p["enabled"]!; changed.Add("enabled"); }
        for (int i = 1; i <= 10; i++)
        {
            int n = i;
            Str("user" + n, v => TimelinerHelper.SetUser(t, n, v));
        }
        return changed;
    }

    /// <summary>
    /// Task types are localised ("Construcción" on a Spanish install) and an unknown name makes Navisworks throw,
    /// so resolve "type" against the document's types first (case/accent-insensitive) and fail with the valid list.
    /// </summary>
    protected static void ResolveType(TL.DocumentTimeliner tl, JObject p)
    {
        if (p["type"] == null || p["type"]!.Type == JTokenType.Null) return;
        var names = tl.SimulationTaskTypes.OfType<TL.SimulationTaskType>().Select(t => t.DisplayName);
        if (!NameMatch.TryResolve((string?)p["type"], names, out var resolved, out var error)) throw CommandFailure.Invalid("type " + error);
        p["type"] = resolved;
    }

    protected static void Attach(NW.Document doc, TL.TimelinerTask copy, JObject p, string mode, out int count, out JArray unresolved)
    {
        unresolved = new JArray();
        var setId = (string?)p["set_id"];
        if (mode == "clear") { copy.Selection.Clear(); count = 0; return; }
        if (!string.IsNullOrEmpty(setId) && Bool(p, "link_set", true) && mode == "replace")
        {
            // Keep a live link to the set: the task follows later changes of the set.
            var saved = ViewHelper.ResolveSaved(doc.SelectionSets.RootItem, setId!, "set");
            var sources = new NW.SelectionSourceCollection { doc.SelectionSets.CreateSelectionSource(saved) };
            copy.Selection.CopyFrom(sources);
            count = copy.Selection.GetSelectedItems(doc).Count;
            return;
        }
        var items = Scope(p, doc, out unresolved);
        if (mode == "add") items = TimelinerHelper.SelectedItems(copy, doc).Concat(items).Distinct().ToList();
        copy.Selection.CopyFrom(items);
        count = items.Count;
    }

    protected static string Mode(JObject p, string @default = "replace")
    {
        var m = ((string?)p["mode"] ?? @default).ToLowerInvariant();
        return m == "replace" || m == "add" || m == "clear" ? m : throw CommandFailure.Invalid("mode must be replace, add or clear");
    }
}

/// <summary>Creates a task (optionally under a parent, at an index) with fields and attached items.</summary>
public sealed class CreateTimelinerTaskHandler : TimelinerWriteCommand
{
    public override string Name => "create_timeliner_task";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var name = (string?)p["name"];
        if (string.IsNullOrWhiteSpace(name)) throw CommandFailure.Invalid("name is required");
        var parentNode = string.IsNullOrEmpty((string?)p["parent_id"]) ? null : TimelinerHelper.Resolve(tl, (string?)p["parent_id"]);
        NW.GroupItem parent = parentNode == null ? tl.TasksRoot : parentNode.Task;
        int index = Int(p, "index", parent.Children.Count, 0, parent.Children.Count);

        var task = new TL.TimelinerTask { DisplayName = name!.Trim() };
        ResolveType(tl, p);
        var changed = ApplyFields(task, p);
        int count = 0; JArray unresolved = new JArray();
        if (p["item_ids"] != null || p["set_id"] != null || p["selection"] != null || p["all"] != null)
            Attach(doc, task, p, "replace", out count, out unresolved);
        tl.TaskInsertCopy(parent, index, task);

        // Navisworks wrappers are not identity-stable across walks, so locate the new task by its index path.
        var createdPath = (parentNode?.Path ?? Array.Empty<int>()).Concat(new[] { index }).ToList();
        var created = TimelinerHelper.Walk(tl).First(n => n.Path.SequenceEqual(createdPath));
        var o = TimelinerHelper.TaskJson(created);
        o["fields_set"] = new JArray(changed);
        o["attached_items"] = count;
        o["unresolved"] = unresolved;
        return o;
    }
}

/// <summary>Updates only the given fields of a task (and renames it when name is given).</summary>
public sealed class UpdateTimelinerTaskHandler : TimelinerWriteCommand
{
    public override string Name => "update_timeliner_task";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var node = TimelinerHelper.Resolve(tl, (string?)p["task_id"]);
        var copy = (TL.TimelinerTask)node.Task.CreateCopy();
        ResolveType(tl, p);
        var changed = ApplyFields(copy, p);
        if (changed.Count > 0) tl.TaskEdit(node.Parent, node.Index, copy);
        var newName = (string?)p["name"];
        if (!string.IsNullOrWhiteSpace(newName))
        {
            var current = TimelinerHelper.Resolve(tl, node.Id).Task;
            tl.TaskEditDisplayName(current, newName!.Trim());
            changed.Add("name");
        }
        if (changed.Count == 0) throw CommandFailure.Invalid("give at least one field to change");
        var o = TimelinerHelper.TaskJson(TimelinerHelper.Resolve(tl, node.Id));
        o["fields_set"] = new JArray(changed);
        return o;
    }
}

/// <summary>Deletes a task and its children.</summary>
public sealed class DeleteTimelinerTaskHandler : TimelinerWriteCommand
{
    public override string Name => "delete_timeliner_task";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var node = TimelinerHelper.Resolve(tl, (string?)p["task_id"]);
        int subtree = TimelinerHelper.Walk(tl).Count(n => n.Path.Take(node.Path.Count).SequenceEqual(node.Path));
        tl.TaskRemoveAt(node.Parent, node.Index);
        return new JObject { ["deleted"] = node.Task.DisplayName, ["id"] = node.Id, ["tasks_removed"] = subtree, ["undo"] = "Navisworks Undo (Ctrl+Z) only" };
    }
}

/// <summary>Moves a task under another parent (or the root) at an index.</summary>
public sealed class MoveTimelinerTaskHandler : TimelinerWriteCommand
{
    public override string Name => "move_timeliner_task";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var node = TimelinerHelper.Resolve(tl, (string?)p["task_id"]);
        var parentNode = string.IsNullOrEmpty((string?)p["parent_id"]) ? null : TimelinerHelper.Resolve(tl, (string?)p["parent_id"]);
        var parentPath = parentNode?.Path ?? Array.Empty<int>();
        if (parentNode != null && TaskRef.IsSelfOrDescendant(node.Path, parentPath))
            throw CommandFailure.Invalid("cannot move a task under itself or one of its children");
        NW.GroupItem newParent = parentNode == null ? tl.TasksRoot : parentNode.Task;
        int index = Int(p, "index", newParent.Children.Count, 0, newParent.Children.Count);
        string? name = node.Task.DisplayName, displayId = node.Task.DisplayId, syncId = node.Task.SynchronizationId;
        tl.TaskMove(node.Parent, node.Index, newParent, index);

        // The parent's own path shifts when the moved task was an earlier sibling of one of its ancestors, so look it
        // up again by identity fields among the children of the (re-resolved) parent.
        var parentAfter = parentNode == null ? null : TimelinerHelper.Walk(tl).FirstOrDefault(n =>
            n.Task.DisplayName == parentNode.Task.DisplayName && n.Task.DisplayId == parentNode.Task.DisplayId && n.Task.SynchronizationId == parentNode.Task.SynchronizationId);
        int wantDepth = parentAfter == null ? 0 : parentAfter.Path.Count;
        var moved = TimelinerHelper.Walk(tl).FirstOrDefault(n => n.Path.Count == wantDepth + 1 &&
            (parentAfter == null || TaskRef.IsSelfOrDescendant(parentAfter.Path, n.Path)) &&
            n.Task.DisplayName == name && n.Task.DisplayId == displayId && n.Task.SynchronizationId == syncId);
        return moved == null ? new JObject { ["moved"] = name, ["note"] = "moved; list the tasks to see its new id" } : TimelinerHelper.TaskJson(moved);
    }
}

/// <summary>Attaches items to a task: replace (default), add or clear. A set_id is linked live unless link_set=false.</summary>
public sealed class AttachItemsToTaskHandler : TimelinerWriteCommand
{
    public override string Name => "attach_items_to_task";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var node = TimelinerHelper.Resolve(tl, (string?)p["task_id"]);
        var mode = Mode(p);
        var copy = (TL.TimelinerTask)node.Task.CreateCopy();
        Attach(doc, copy, p, mode, out var count, out var unresolved);
        tl.TaskEdit(node.Parent, node.Index, copy);
        var after = TimelinerHelper.Resolve(tl, node.Id).Task;
        return new JObject
        {
            ["task"] = node.Id, ["name"] = node.Task.DisplayName, ["mode"] = mode,
            ["item_count"] = TimelinerHelper.SelectedItems(after, doc).Count,
            ["selection"] = after.Selection.IsClear ? null : after.Selection.DisplayString,
            ["unresolved"] = unresolved,
        };
    }
}

/// <summary>
/// Links items to tasks by matching an item property (e.g. a 4D task code) to a task field (display_id by default).
/// dry_run (default true) only reports; apply with dry_run=false in one undoable transaction.
/// </summary>
public sealed class AutoAttachTasksByPropertyHandler : TimelinerWriteCommand
{
    public override string Name => "auto_attach_tasks_by_property";
    protected override bool RequiresModels => true;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var spec = OneProperty(p);
        var key = TimelinerHelper.KeyField((string?)p["task_key"]);
        bool dryRun = Bool(p, "dry_run", true);
        var mode = Mode(p);
        if (mode == "clear") throw CommandFailure.Invalid("mode must be replace or add");
        int sample = Int(p, "sample", 30, 0, 1000);

        bool hasScope = p["item_ids"] != null || p["set_id"] != null || p["selection"] != null || p["all"] != null;
        JArray unresolved = new JArray();
        var items = hasScope ? Scope(p, doc, out unresolved) : NwJson.AllItems(doc).Where(i => i.HasGeometry).ToList();
        var tasks = TimelinerHelper.Walk(tl).ToList();

        var matcher = new TaskCodeMatcher(!Bool(p, "case_sensitive", false), (string?)p["separator"]);
        for (int i = 0; i < tasks.Count; i++) matcher.AddTask(i, key(tasks[i].Task));
        for (int i = 0; i < items.Count; i++) matcher.AddItem(i, NwJson.Text(NwJson.FindProperty(items[i], spec)?.Value));
        var r = matcher.Match();

        var plan = r.ItemsByTask.OrderBy(kv => kv.Key).Select(kv => (Node: tasks[kv.Key], Items: kv.Value.Select(i => items[i]).ToList())).ToList();
        var report = new JObject
        {
            ["dry_run"] = dryRun,
            ["property"] = spec.Label,
            ["task_key"] = (string?)p["task_key"] ?? "display_id",
            ["mode"] = mode,
            ["items_scanned"] = items.Count,
            ["items_matched"] = r.ItemsMatched,
            ["items_without_code"] = r.ItemsWithoutCode,
            ["tasks_matched"] = plan.Count,
            ["tasks_without_items"] = new JObject
            {
                ["count"] = r.TasksWithoutItems.Count,
                ["sample"] = new JArray(r.TasksWithoutItems.Take(sample).Select(i => new JObject { ["id"] = tasks[i].Id, ["name"] = tasks[i].Task.DisplayName, ["key"] = key(tasks[i].Task) })),
            },
            ["tasks_without_key"] = r.TasksWithoutKey,
            ["unmatched_codes"] = new JObject
            {
                ["count"] = r.UnmatchedCodes.Count,
                ["sample"] = new JArray(r.UnmatchedCodes.OrderByDescending(kv => kv.Value).Take(sample).Select(kv => new JObject { ["code"] = kv.Key, ["items"] = kv.Value })),
            },
            ["duplicate_task_keys"] = new JArray(r.DuplicateTaskKeys.Take(sample)),
            ["plan_sample"] = new JArray(plan.Take(sample).Select(x => new JObject { ["task"] = x.Node.Id, ["name"] = x.Node.Task.DisplayName, ["key"] = key(x.Node.Task), ["items"] = x.Items.Count })),
            ["unresolved"] = unresolved,
        };
        if (dryRun) return report;

        // Apply bottom-up by path so earlier edits never shift the index of a later one.
        using (var tx = doc.BeginTransaction("Auto-attach TimeLiner tasks by " + spec.Label))
        {
            foreach (var (node, attach) in plan.OrderByDescending(x => x.Node.Path.Count))
            {
                var current = TimelinerHelper.Resolve(tl, node.Id);
                var copy = (TL.TimelinerTask)current.Task.CreateCopy();
                var final = mode == "add" ? TimelinerHelper.SelectedItems(copy, doc).Concat(attach).Distinct().ToList() : attach;
                copy.Selection.CopyFrom(final);
                tl.TaskEdit(current.Parent, current.Index, copy);
            }
            tx.Commit();
        }
        report["applied_tasks"] = plan.Count;
        report["undo"] = "one Navisworks Undo step";
        return report;
    }
}

/// <summary>Re-imports tasks from a data source (P6, MS Project, CSV…) and merges them: synchronize (default) or rebuild.</summary>
public sealed class SyncTimelinerDataSourceHandler : TimelinerWriteCommand
{
    public override string Name => "sync_timeliner_data_source";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var name = (string?)p["data_source"] ?? throw CommandFailure.Invalid("data_source is required");
        var ds = tl.DataSources.OfType<TL.TimelinerDataSource>().FirstOrDefault(d => string.Equals(d.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                 ?? throw CommandFailure.NotFound($"data source '{name}' not found (see nwd_get_timeliner_config)");
        var mode = ((string?)p["mode"] ?? "synchronize").ToLowerInvariant();
        if (mode != "synchronize" && mode != "rebuild") throw CommandFailure.Invalid("mode must be synchronize or rebuild");

        var record = TL.TimelinerDataSourceProvider.FindDataSourceProvider(ds)
                     ?? throw new CommandFailure("PROVIDER_NOT_FOUND", $"no provider plug-in for '{ds.DataSourceProviderName}'");
        var provider = (record.LoadedPlugin ?? record.TryLoadPlugin()) as TL.TimelinerDataSourceProvider
                       ?? throw new CommandFailure("PROVIDER_NOT_LOADED", $"provider '{record.DisplayName}' could not be loaded");
        int before = (int)tl.TaskTotalTasks();
        TL.TimelinerImportTasksResult result;
        try { result = provider.ImportTasks(ds); }
        catch (TL.TimelinerDataSourceProviderException ex) { throw new CommandFailure("IMPORT_FAILED", ex.Message); }
        if (result?.RootTask == null) throw new CommandFailure("IMPORT_FAILED", "the provider returned no tasks");

        bool importType = Bool(p, "import_task_type", true);
        if (mode == "rebuild") tl.TaskMergeRebuild(result.RootTask, importType); else tl.TaskMergeSynchronize(result.RootTask, importType);
        return new JObject
        {
            ["data_source"] = ds.DisplayName, ["mode"] = mode, ["provider"] = record.DisplayName,
            ["tasks_before"] = before, ["tasks_after"] = tl.TaskTotalTasks(),
            ["warning"] = result.ImportWarning,
            ["flags"] = new JObject
            {
                ["duplicate_child_sync_id"] = result.DuplicateChildSynchronizationIdFound,
                ["missing_child_sync_id"] = result.MissingChildSynchronizationIdFound,
                ["invalid_task_types"] = result.InvalidTaskTypesFound,
                ["root_sync_id_corrected"] = result.RootSynchronizationIdCorrected,
            },
        };
    }
}

/// <summary>Recalculates summary (parent) task dates and/or costs from their children, for the whole tree.</summary>
public sealed class RecalculateTaskSummariesHandler : TimelinerWriteCommand
{
    public override string Name => "recalculate_task_summaries";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        bool dates = Bool(p, "dates", true), costs = Bool(p, "costs", true);
        if (!dates && !costs) throw CommandFailure.Invalid("dates and costs cannot both be false");
        var roots = TimelinerHelper.Walk(tl).Where(n => n.Depth == 0).ToList();
        foreach (var n in roots)
        {
            var copy = (TL.TimelinerTask)n.Task.CreateCopy();
            if (dates) TL.DocumentTimeliner.TaskSummaryHierarchyRecalculateDates(copy);
            if (costs) TL.DocumentTimeliner.TaskSummaryHierarchyRecalculateCosts(copy);
            tl.TaskReplaceWithCopy(n.Parent, n.Index, copy);
        }
        return new JObject { ["top_level_tasks"] = roots.Count, ["dates"] = dates, ["costs"] = costs };
    }
}

/// <summary>Sorts the task tree by a field.</summary>
public sealed class SortTimelinerTasksHandler : TimelinerWriteCommand
{
    public override string Name => "sort_timeliner_tasks";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var raw = ((string?)p["field"] ?? "PlannedStartDate").Replace("_", "");
        if (!Enum.TryParse<TL.TaskField>(raw, true, out var field))
            throw CommandFailure.Invalid("field must be a TaskField such as planned_start_date, planned_end_date, display_id, simulation_task_type or progress_percent");
        bool asc = Bool(p, "ascending", true);
        tl.TasksSort(field, asc);
        return new JObject { ["sorted_by"] = field.ToString(), ["ascending"] = asc };
    }
}

/// <summary>Adds, updates or deletes a simulation appearance (name, colour, transparency).</summary>
public sealed class ManageSimulationAppearanceHandler : TimelinerWriteCommand
{
    public override string Name => "manage_simulation_appearance";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tl = TimelinerHelper.Tl(doc);
        var action = ((string?)p["action"] ?? "").ToLowerInvariant();
        var name = (string?)p["name"];
        if (string.IsNullOrWhiteSpace(name)) throw CommandFailure.Invalid("name is required");
        var list = tl.SimulationAppearances.OfType<TL.SimulationAppearance>().ToList();
        int index = list.FindIndex(a => string.Equals(a.DisplayName, name, StringComparison.OrdinalIgnoreCase));

        TL.SimulationAppearance Build()
        {
            if (!ColorSpec.TryParse((string?)p["color"], out var c, out var e)) throw CommandFailure.Invalid(e);
            var t = p["transparency"]?.Type == JTokenType.Float || p["transparency"]?.Type == JTokenType.Integer ? (double)p["transparency"]! : 0;
            if (t < 0 || t > 1) throw CommandFailure.Invalid("transparency must be between 0 and 1");
            return new TL.SimulationAppearance(name!.Trim(), new NW.Color(c.R, c.G, c.B), t);
        }

        switch (action)
        {
            case "add":
                if (index >= 0) throw CommandFailure.Invalid($"appearance '{name}' already exists; use action=update");
                tl.SimulationAppearanceAddCopy(Build());
                break;
            case "update":
                if (index < 0) throw CommandFailure.NotFound($"appearance '{name}' not found");
                tl.SimulationAppearanceReplaceWithCopy(index, Build());
                break;
            case "delete":
                if (index < 0) throw CommandFailure.NotFound($"appearance '{name}' not found");
                tl.SimulationAppearanceRemoveByDisplayName(list[index].DisplayName);
                break;
            default: throw CommandFailure.Invalid("action must be add, update or delete");
        }
        return new JObject { ["action"] = action, ["name"] = name!.Trim(), ["appearances"] = tl.SimulationAppearances.Count };
    }
}
#endif
