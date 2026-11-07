#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Files;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>Every model in the document: source files, units, transform, orientation and root id.</summary>
public sealed class ListModelsHandler : DocumentCommand
{
    public override string Name => "list_models";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var models = new JArray();
        for (int i = 0; i < doc.Models.Count; i++)
        {
            var m = doc.Models[i];
            models.Add(new JObject
            {
                ["index"] = i,
                ["root_id"] = i + ":",
                ["root_name"] = m.RootItem?.DisplayName,
                ["file_name"] = m.FileName,
                ["source_file_name"] = m.SourceFileName,
                ["creator"] = m.Creator,
                ["guid"] = m.Guid.ToString("D"),
                ["source_guid"] = m.SourceGuid.ToString("D"),
                ["units"] = m.Units.ToString(),
                ["transform"] = NwJson.Transform(m.Transform),
                ["is_transform_reflected"] = m.IsTransformReflected,
                ["up_vector"] = m.HasUpVector ? NwJson.Vector(m.UpVector) : null,
                ["front_vector"] = m.HasFrontVector ? NwJson.Vector(m.FrontVector) : null,
                ["right_vector"] = m.HasRightVector ? NwJson.Vector(m.RightVector) : null,
                ["north_vector"] = m.HasNorthVector ? NwJson.Vector(m.NorthVector) : null,
            });
        }
        return new JObject { ["count"] = models.Count, ["models"] = models };
    }
}

/// <summary>Document-level state: file version, modified flag, undo/redo, units, orientation, counts, active sheet.</summary>
public sealed class GetDocumentMetadataHandler : DocumentCommand
{
    public override string Name => "get_document_metadata";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        JToken sheet = JValue.CreateNull();
        try
        {
            var s = doc.ActiveSheet;
            if (s != null) sheet = new JObject { ["id"] = s.Id, ["name"] = s.DisplayName, ["type"] = s.SheetType.ToString() };
        }
        catch (Exception) { /* sheets are not available for every file type */ }

        return new JObject
        {
            ["title"] = doc.Title,
            ["file_name"] = doc.FileName,
            ["current_file_name"] = doc.CurrentFileName,
            // Document.FileVersion is missing from older APIs (not in 2024), so read it by reflection.
            ["file_version"] = doc.GetType().GetProperty("FileVersion")?.GetValue(doc)?.ToString(),
            ["is_clear"] = doc.IsClear,
            ["is_modified"] = doc.IsModified,
            ["is_undo_disabled"] = doc.IsUndoDisabled,
            ["next_undo"] = doc.NextUndo,
            ["next_redo"] = doc.NextRedo,
            ["units"] = doc.Units.ToString(),
            ["up_vector"] = NwJson.Vector(doc.UpVector),
            ["front_vector"] = NwJson.Vector(doc.FrontVector),
            ["right_vector"] = NwJson.Vector(doc.RightVector),
            ["model_count"] = doc.Models.Count,
            ["view_count"] = doc.Views.Count,
            ["active_sheet"] = sheet,
            ["grid_system_count"] = doc.Grids.Systems.Count,
            ["has_alignments"] = SafeAny(doc),
        };
    }

    private static bool SafeAny(NW.Document doc)
    {
        try { return doc.Alignments.Any(); } catch (Exception) { return false; }
    }
}

/// <summary>Reads a .nwd/.nwf/.nwc header (properties and sheets) without opening it.</summary>
public sealed class PeekFileInfoHandler : DocumentCommand
{
    public override string Name => "peek_file_info";
    protected override bool RequiresDocument => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var check = LocalModelFile.Check((string?)p["path"]);
        if (!check.Ok) throw new CommandFailure(check.Code, check.Message);
        var info = NW.Application.TryLoadDocumentInfo(check.FullPath)
                   ?? throw new CommandFailure("UNREADABLE_FILE", "Navisworks could not read document information from that file");

        var categories = new JArray();
        foreach (NW.InfoPropertyCategory cat in info.PropertyCategories)
        {
            var props = new JObject();
            foreach (NW.DataProperty dp in cat.Properties) props[dp.DisplayName ?? dp.Name ?? "?"] = NwJson.Value(dp.Value);
            categories.Add(new JObject { ["name"] = cat.DisplayName, ["properties"] = props });
        }
        var sheets = new JArray();
        foreach (NW.SavedItem child in info.Children)
            if (child is NW.SheetInfo s)
                sheets.Add(new JObject { ["id"] = s.Id, ["name"] = s.DisplayName, ["type"] = s.SheetType.ToString(), ["is_loaded"] = s.IsLoaded });

        return new JObject
        {
            ["path"] = check.FullPath,
            ["file_version_guid"] = info.FileVersionGuid.ToString("D"),
            ["source_guid"] = info.SourceGuid.ToString("D"),
            ["current_sheet_id"] = info.CurrentSheetId,
            ["categories"] = categories,
            ["sheets"] = sheets,
        };
    }
}

/// <summary>Bounding box of the whole scene and of each model.</summary>
public sealed class GetSceneBoundsHandler : DocumentCommand
{
    public override string Name => "get_scene_bounds";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        bool ignoreHidden = Bool(p, "ignore_hidden", true);
        var models = new JArray();
        for (int i = 0; i < doc.Models.Count; i++)
        {
            var root = doc.Models[i].RootItem;
            models.Add(new JObject { ["index"] = i, ["name"] = root?.DisplayName, ["bounds"] = NwJson.Box(root?.BoundingBox(ignoreHidden)) });
        }
        return new JObject
        {
            ["units"] = doc.Units.ToString(),
            ["ignore_hidden"] = ignoreHidden,
            ["bounds"] = NwJson.Box(doc.GetBoundingBox(ignoreHidden)),
            ["models"] = models,
        };
    }
}

/// <summary>Ancestors (root first) and paged children of one item.</summary>
public sealed class GetItemHierarchyHandler : DocumentCommand
{
    public override string Name => "get_item_hierarchy";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var item = OneItem(p, doc);
        var ids = Ids(p, doc);
        var page = Page(p);

        var ancestors = new JArray();
        foreach (NW.ModelItem a in item.Ancestors.Reverse()) ancestors.Add(NwJson.ItemSummary(a, ids));

        var (children, info) = page.Apply(item.Children.Cast<NW.ModelItem>());
        var kids = new JArray();
        foreach (var c in children)
        {
            var o = NwJson.ItemSummary(c, ids);
            o["has_children"] = c.Children.Any();
            o["has_geometry"] = c.HasGeometry;
            kids.Add(o);
        }

        var self = NwJson.ItemSummary(item, ids);
        self["has_geometry"] = item.HasGeometry;
        var objectAncestor = item.FindFirstObjectAncestor();
        return new JObject
        {
            ["item"] = self,
            ["first_object_ancestor"] = objectAncestor == null ? null : NwJson.ItemSummary(objectAncestor, ids),
            ["ancestors"] = ancestors,
            ["children"] = kids,
            ["page"] = info,
        };
    }
}

/// <summary>Bounding box, transform and node flags for a scope of items. Paged.</summary>
public sealed class GetItemsGeometryHandler : DocumentCommand
{
    public override string Name => "get_items_geometry";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var page = Page(p);
        var ids = Ids(p, doc);
        bool ignoreHidden = Bool(p, "ignore_hidden", false);
        var items = Scope(p, doc, out var unresolved);
        var (slice, info) = page.Apply(items);

        var rows = new JArray();
        foreach (var item in slice)
        {
            if (ctx.PastDeadline) { info["truncated_by_deadline"] = true; break; }
            var o = NwJson.ItemSummary(item, ids);
            o["has_geometry"] = item.HasGeometry;
            o["is_composite"] = item.IsComposite;
            o["is_insert"] = item.IsInsert;
            o["is_layer"] = item.IsLayer;
            o["is_collection"] = item.IsCollection;
            o["bounds"] = NwJson.Box(item.BoundingBox(ignoreHidden));
            JToken transform;
            try { transform = NwJson.Transform(item.Transform); } catch (Exception) { transform = JValue.CreateNull(); }
            o["transform"] = transform;
            rows.Add(o);
        }
        return new JObject { ["units"] = doc.Units.ToString(), ["items"] = rows, ["unresolved"] = unresolved, ["page"] = info };
    }
}

/// <summary>
/// Hidden / required / frozen state. Walks every model once and lists only top-most flagged items (a flagged
/// parent stands for its subtree), with counts.
/// </summary>
public sealed class GetVisibilityStateHandler : DocumentCommand
{
    public override string Name => "get_visibility_state";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var ids = Ids(p, doc);
        int limit = Int(p, "limit", 200, 1, PageRequest.MaxLimit);
        var hidden = new List<NW.ModelItem>();
        var required = new List<NW.ModelItem>();
        var frozen = new List<NW.ModelItem>();
        bool truncated = false;

        void Walk(NW.ModelItem item, bool underHidden, bool underRequired, bool underFrozen)
        {
            if (truncated) return;
            if (ctx.PastDeadline) { truncated = true; return; }
            bool h = item.IsHidden, r = item.IsRequired, f = item.IsFrozen;
            if (h && !underHidden) hidden.Add(item);
            if (r && !underRequired) required.Add(item);
            if (f && !underFrozen) frozen.Add(item);
            // Children can be required/frozen even under a hidden parent, so always descend.
            foreach (NW.ModelItem c in item.Children) Walk(c, underHidden || h, underRequired || r, underFrozen || f);
        }
        foreach (NW.Model m in doc.Models) if (m.RootItem != null) Walk(m.RootItem, false, false, false);

        JObject List(List<NW.ModelItem> xs) => new JObject
        {
            ["top_most_count"] = xs.Count,
            ["items"] = new JArray(xs.Take(limit).Select(x => NwJson.ItemSummary(x, ids))),
            ["truncated"] = xs.Count > limit,
        };

        int hiddenAtModelState = 0;
        try { hiddenAtModelState = doc.Models.GetAllHiddenAtModelState().Count; } catch (Exception) { }

        return new JObject
        {
            ["hidden"] = List(hidden),
            ["required"] = List(required),
            ["frozen"] = List(frozen),
            ["hidden_at_model_state_count"] = hiddenAtModelState,
            ["truncated_by_deadline"] = truncated,
        };
    }
}
#endif
