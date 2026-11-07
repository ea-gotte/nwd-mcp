#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>Grid systems with levels (elevations) and lines.</summary>
public sealed class GetGridsHandler : DocumentCommand
{
    public override string Name => "get_grids";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var grids = doc.Grids;
        var systems = new JArray();
        for (int i = 0; i < grids.Systems.Count; i++)
        {
            var s = grids.Systems[i];
            systems.Add(new JObject
            {
                ["index"] = i,
                ["name"] = s.DisplayName,
                ["is_active"] = grids.ActiveSystem != null && ReferenceEquals(grids.ActiveSystem, s) || grids.ActiveSystem?.DisplayName == s.DisplayName,
                ["origin"] = NwJson.Point(s.Origin),
                ["up"] = NwJson.Vector(s.UpDirection),
                ["locked_level"] = s.LockedLevel?.DisplayName,
                ["levels"] = new JArray(s.Levels.Cast<NW.GridLevel>().OrderBy(l => l.Elevation)
                    .Select(l => new JObject { ["name"] = l.DisplayName, ["elevation"] = NwJson.R(l.Elevation) })),
                ["lines"] = new JArray(s.Lines.Cast<NW.GridLine>().Select(l => l.DisplayName)),
            });
        }
        return new JObject { ["units"] = doc.Units.ToString(), ["render_mode"] = grids.RenderMode.ToString(), ["systems"] = systems };
    }

    internal static NW.GridSystem PickSystem(NW.Document doc, JObject p)
    {
        var grids = doc.Grids;
        if (grids.Systems.Count == 0) throw CommandFailure.NotFound("the document has no grid systems");
        var wanted = (string?)p["grid_system"];
        if (string.IsNullOrEmpty(wanted)) return grids.ActiveSystem ?? grids.Systems[0];
        if (int.TryParse(wanted, out var idx) && idx >= 0 && idx < grids.Systems.Count) return grids.Systems[idx];
        return grids.Systems.Cast<NW.GridSystem>().FirstOrDefault(s => string.Equals(s.DisplayName, wanted, StringComparison.OrdinalIgnoreCase))
               ?? throw CommandFailure.NotFound($"grid system '{wanted}' not found");
    }
}

/// <summary>Nearest grid intersection and level for the centre of each item. Paged.</summary>
public sealed class LocateItemsOnGridHandler : DocumentCommand
{
    public override string Name => "locate_items_on_grid";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var system = GetGridsHandler.PickSystem(doc, p);
        var page = Page(p);
        var ids = Ids(p, doc);
        var items = Scope(p, doc, out var unresolved);
        var (slice, info) = page.Apply(items);
        var rows = new JArray();
        foreach (var item in slice)
        {
            if (ctx.PastDeadline) { info["truncated_by_deadline"] = true; break; }
            var o = NwJson.ItemSummary(item, ids);
            var box = item.BoundingBox(false);
            if (box == null || box.IsEmpty) { o["location"] = null; o["reason"] = "item has no geometry"; rows.Add(o); continue; }
            var center = box.Center;
            o["center"] = NwJson.Point(center);
            var hit = system.ClosestIntersection(center);
            if (hit == null) { o["location"] = null; rows.Add(o); continue; }
            string? label = null;
            try { label = hit.FormatCombinedDisplayString(center, 1.0); } catch (Exception) { }
            o["location"] = new JObject
            {
                ["intersection"] = hit.DisplayName,
                ["line1"] = hit.Line1?.DisplayName,
                ["line2"] = hit.Line2?.DisplayName,
                ["level"] = hit.Level?.DisplayName,
                ["level_elevation"] = hit.Level == null ? (double?)null : NwJson.R(hit.Level.Elevation),
                ["position"] = NwJson.Point(hit.Position),
                ["offset"] = NwJson.Vector(center.Subtract(hit.Position)),
                ["distance"] = NwJson.R(center.DistanceTo(hit.Position)),
                ["label"] = label,
            };
            rows.Add(o);
        }
        return new JObject { ["grid_system"] = system.DisplayName, ["units"] = doc.Units.ToString(), ["items"] = rows, ["unresolved"] = unresolved, ["page"] = info };
    }
}

/// <summary>Comments on saved viewpoints and selection/search sets. Paged over commented items.</summary>
public sealed class GetCommentsHandler : DocumentCommand
{
    public override string Name => "get_comments";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var source = ((string?)p["source"] ?? "all").ToLowerInvariant();
        if (source != "all" && source != "viewpoints" && source != "sets")
            throw CommandFailure.Invalid("source must be all, viewpoints or sets");
        var page = Page(p);

        var found = new List<JObject>();
        void Walk(string kind, IEnumerable<NW.SavedItem> items, string prefix)
        {
            foreach (var item in items)
            {
                var path = prefix + "/" + SavedItemResolver.Escape(item.DisplayName);
                if (item.Comments != null && item.Comments.Count > 0)
                    found.Add(new JObject
                    {
                        ["source"] = kind,
                        ["id"] = SavedItemResolver.Id(item.Guid, path),
                        ["path"] = "path:" + path,
                        ["name"] = item.DisplayName,
                        ["comments"] = new JArray(item.Comments.Select(Comment)),
                    });
                if (item is NW.GroupItem g) Walk(kind, g.Children.Cast<NW.SavedItem>(), path);
            }
        }
        if (source != "sets") Walk("viewpoint", doc.SavedViewpoints.RootItem.Children.Cast<NW.SavedItem>(), "");
        if (source != "viewpoints") Walk("set", doc.SelectionSets.RootItem.Children.Cast<NW.SavedItem>(), "");

        var (slice, info) = page.Apply(found);
        return new JObject { ["items"] = new JArray(slice), ["page"] = info };
    }

    internal static JObject Comment(NW.Comment c) => new JObject
    {
        ["id"] = c.Id,
        ["author"] = c.Author,
        ["status"] = c.Status.ToString(),
        ["created"] = c.CreationDate.ToUniversalTime().ToString("o"),
        ["body"] = c.Body,
    };
}

/// <summary>Sheets / 2D-3D layouts of the document (multi-sheet files such as DWF or PDF).</summary>
public sealed class ListSheetsHandler : DocumentCommand
{
    public override string Name => "list_sheets";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var info = doc.DocumentInfo.ToDocumentInfo();
        string? active = null;
        try { active = doc.ActiveSheet?.Id; } catch (Exception) { }
        var sheets = new JArray();
        if (info != null)
            foreach (NW.SavedItem child in info.Children)
                if (child is NW.SheetInfo s)
                    sheets.Add(new JObject { ["id"] = s.Id, ["name"] = s.DisplayName, ["type"] = s.SheetType.ToString(), ["is_loaded"] = s.IsLoaded, ["is_active"] = s.Id == active });
        return new JObject { ["active_sheet_id"] = active, ["sheets"] = sheets };
    }
}

/// <summary>Civil alignments (stationing) in the scene.</summary>
public sealed class ListAlignmentsHandler : DocumentCommand
{
    public override string Name => "list_alignments";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var a = doc.Alignments;
        var list = a.Any() ? a.GetAlignments().ToList() : new List<NW.CivilAlignment>();
        return new JObject
        {
            ["enabled"] = a.Enabled,
            ["active_index"] = list.Count == 0 ? (int?)null : a.ActiveAlignmentIndex,
            ["alignments"] = new JArray(list.Select((x, i) => new JObject
            {
                ["index"] = i,
                ["name"] = x.Name,
                ["start_station"] = NwJson.R(x.StartStation),
                ["end_station"] = NwJson.R(x.EndStation),
                ["start_label"] = SafeLabel(x, x.StartStation),
                ["end_label"] = SafeLabel(x, x.EndStation),
            })),
        };
    }

    private static string? SafeLabel(NW.CivilAlignment a, double station)
    {
        try { return a.GetStationLabel(station); } catch (Exception) { return null; }
    }
}
#endif
