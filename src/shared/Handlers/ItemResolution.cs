#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>
/// Resolves item references in every supported form (path, pid:, nid:, guid:) to model items for one command.
/// Create one per command: guid lookups build a one-off index the first time they are needed.
/// </summary>
public sealed class ItemRefResolver
{
    private readonly NW.Document _doc;
    private Dictionary<Guid, List<NW.ModelItem>>? _byGuid;

    public ItemRefResolver(NW.Document doc) => _doc = doc;

    public sealed class Result
    {
        public List<NW.ModelItem> Items { get; } = new List<NW.ModelItem>();
        public JArray Unresolved { get; } = new JArray();
    }

    /// <summary>Resolves refs in order, dropping duplicates. Unparseable or missing refs land in Unresolved with a reason.</summary>
    public Result Resolve(IEnumerable<string?> refs)
    {
        var result = new Result();
        var seen = new HashSet<NW.ModelItem>();
        foreach (var raw in refs)
        {
            if (!ItemRef.TryParse(raw, out var r, out var error))
            {
                result.Unresolved.Add(new JObject { ["id"] = raw, ["reason"] = error });
                continue;
            }
            var hits = ResolveOne(r!, out var reason);
            if (hits.Count == 0)
            {
                result.Unresolved.Add(new JObject { ["id"] = raw, ["reason"] = reason });
                continue;
            }
            foreach (var h in hits) if (seen.Add(h)) result.Items.Add(h);
        }
        return result;
    }

    private List<NW.ModelItem> ResolveOne(ItemRef r, out string reason)
    {
        reason = "not found in the active document";
        var none = new List<NW.ModelItem>();
        if (r.Kind != ItemRefKind.InstanceGuid && (r.ModelIndex < 0 || r.ModelIndex >= _doc.Models.Count))
        {
            reason = $"model index {r.ModelIndex} is out of range (document has {_doc.Models.Count} models)";
            return none;
        }

        switch (r.Kind)
        {
            case ItemRefKind.Path:
            {
                var item = _doc.Models.ResolveIndexPath(new[] { r.ModelIndex }.Concat(r.ChildPath).ToList())
                           ?? ModelItemHelper.ResolveModelItemId(r.Raw, _doc);
                return item == null ? none : new List<NW.ModelItem> { item };
            }
            case ItemRefKind.PathId:
            {
                var item = _doc.Models.ResolvePathId(new NW.DocumentParts.ModelItemPathId { ModelIndex = r.ModelIndex, PathId = r.PathId });
                return item == null ? none : new List<NW.ModelItem> { item };
            }
            case ItemRefKind.NumericId:
                return _doc.Models.FindModelItemsById(r.ModelIndex, r.NumericId).Cast<NW.ModelItem>().ToList();
            case ItemRefKind.InstanceGuid:
                return GuidIndex().TryGetValue(r.Guid, out var list) ? list : none;
            default:
                reason = "unsupported id kind";
                return none;
        }
    }

    private Dictionary<Guid, List<NW.ModelItem>> GuidIndex()
    {
        if (_byGuid != null) return _byGuid;
        _byGuid = new Dictionary<Guid, List<NW.ModelItem>>();
        foreach (NW.Model model in _doc.Models)
        {
            if (model.RootItem == null) continue;
            foreach (NW.ModelItem item in model.RootItem.DescendantsAndSelf)
            {
                var g = item.InstanceGuid;
                if (g == Guid.Empty) continue;
                if (!_byGuid.TryGetValue(g, out var list)) _byGuid[g] = list = new List<NW.ModelItem>(1);
                list.Add(item);
            }
        }
        return _byGuid;
    }
}

/// <summary>Formats item ids in the requested <see cref="ItemIdMode"/>. Falls back to the path form when an item has no id of that kind.</summary>
public sealed class ItemIdEmitter
{
    private readonly NW.Document _doc;
    private readonly ModelItemIdMap _paths;
    public ItemIdMode Mode { get; }

    public ItemIdEmitter(NW.Document doc, ItemIdMode mode)
    {
        _doc = doc; Mode = mode; _paths = new ModelItemIdMap(doc);
    }

    public string IdOf(NW.ModelItem item)
    {
        switch (Mode)
        {
            case ItemIdMode.PathId:
            {
                var pid = _doc.Models.CreatePathId(item);
                if (pid != null && pid.ModelIndex >= 0 && !string.IsNullOrEmpty(pid.PathId))
                    return ItemRef.FormatPathId(pid.ModelIndex, pid.PathId);
                break;
            }
            case ItemIdMode.NumericId:
            {
                var nid = _doc.Models.FindIdForModelItem(item);
                int modelIndex = ModelIndexOf(item);
                if (nid != 0 && modelIndex >= 0) return ItemRef.FormatNumericId(modelIndex, nid);
                break;
            }
            case ItemIdMode.InstanceGuid:
            {
                var g = item.InstanceGuid;
                if (g != Guid.Empty) return ItemRef.FormatGuid(g);
                break;
            }
        }
        return _paths.IdOf(item);
    }

    /// <summary>All id forms for one item, for nwd_resolve_ids.</summary>
    public JObject AllIdsOf(NW.ModelItem item)
    {
        var pid = _doc.Models.CreatePathId(item);
        var nid = _doc.Models.FindIdForModelItem(item);
        int modelIndex = ModelIndexOf(item);
        var g = item.InstanceGuid;
        return new JObject
        {
            ["path"] = _paths.IdOf(item),
            ["pid"] = pid != null && !string.IsNullOrEmpty(pid.PathId) ? ItemRef.FormatPathId(pid.ModelIndex, pid.PathId) : null,
            ["nid"] = nid != 0 && modelIndex >= 0 ? ItemRef.FormatNumericId(modelIndex, nid) : null,
            ["guid"] = g != Guid.Empty ? ItemRef.FormatGuid(g) : null,
        };
    }

    private int ModelIndexOf(NW.ModelItem item)
    {
        var root = item;
        while (root.Parent != null) root = root.Parent;
        return root.Model == null ? -1 : _doc.Models.IndexOf(root.Model);
    }

    public static bool TryFrom(JObject p, NW.Document doc, out ItemIdEmitter emitter, out string error)
    {
        error = "";
        var raw = (string?)p["id_mode"];
        if (!ItemRef.TryParseMode(raw, out var mode))
        {
            emitter = new ItemIdEmitter(doc, ItemIdMode.Path);
            error = "id_mode must be path, pid, nid or guid";
            return false;
        }
        emitter = new ItemIdEmitter(doc, mode);
        return true;
    }
}

/// <summary>
/// The common "which items" argument: exactly one of item_ids (any id form), set_id (selection/search set name,
/// id: or path:), or selection="current".
/// </summary>
public static class ItemScope
{
    public static bool TryResolve(JObject p, NW.Document doc, out List<NW.ModelItem> items, out JArray unresolved, out string error)
    {
        items = new List<NW.ModelItem>(); unresolved = new JArray(); error = "";
        var ids = p["item_ids"] as JArray;
        var setId = (string?)p["set_id"];
        var selection = (string?)p["selection"];
        int given = (ids != null ? 1 : 0) + (!string.IsNullOrEmpty(setId) ? 1 : 0) + (!string.IsNullOrEmpty(selection) ? 1 : 0);
        if (given != 1) { error = "give exactly one of item_ids, set_id or selection=\"current\""; return false; }

        if (ids != null)
        {
            var r = new ItemRefResolver(doc).Resolve(ids.Select(t => t.Type == JTokenType.String ? (string?)t : t.ToString()));
            items = r.Items; unresolved = r.Unresolved;
            return true;
        }
        if (!string.IsNullOrEmpty(setId))
        {
            try { items = GetSelectionSetItemsHandler.ResolveNamedSet(doc, setId!).Cast<NW.ModelItem>().ToList(); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        if (!string.Equals(selection, "current", StringComparison.OrdinalIgnoreCase))
        { error = "selection only accepts \"current\""; return false; }
        items = doc.CurrentSelection.SelectedItems.Cast<NW.ModelItem>().ToList();
        return true;
    }
}
#endif
