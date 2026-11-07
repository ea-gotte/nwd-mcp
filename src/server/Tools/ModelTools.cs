using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Nwd.Server;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Tools;

/// <summary>Toolset "model": deep, read-only model inspection (Phase 1 of the full-API plan).</summary>
[McpServerToolType]
public sealed class ModelTools
{
    internal const string ScopeHelp =
        "Pick items with exactly one of: itemIds (any id form: path \"0:6:3\", \"pid:<model>:<pathId>\", \"nid:<model>:<id>\", \"guid:<guid>\"), " +
        "setId (selection/search set name, id: or path: from nwd_list_sets), or selection=\"current\".";

    internal const string PageHelp =
        "Paged: limit (default 200, max 5000) with offset, or pass page.next_cursor back as cursor.";

    private readonly PluginClient _client;
    public ModelTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "nwd_resolve_ids", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Convert items to every id form: path (cheap, stale after append/merge/refresh), pid (stable path id), " +
                 "nid (numeric id) and guid (instance guid, shared by instances). Use pid or guid ids in multi-step work. " +
                 ScopeHelp + " " + PageHelp + " Unresolved ids are listed with a reason.")]
    public Task<string> ResolveIds(string[]? itemIds = null, string? setId = null, string? selection = null,
        int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
        => Call("resolve_ids", ScopeArgs(itemIds, setId, selection, limit, offset, cursor), ct);

    internal const string IdModeHelp = "idMode picks the returned id form: path (default), pid, nid or guid.";
    internal const string AllHelp = "all=true scans every item in every model; geometryOnly=true keeps only items with geometry.";

    [McpServerTool(Name = "nwd_list_models", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List every model in the document: index, root id, source file, creator, GUIDs, units, transform and orientation vectors.")]
    public Task<string> ListModels(CancellationToken ct = default) => Call("list_models", new JObject(), ct);

    [McpServerTool(Name = "nwd_get_document_metadata", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Document state: file version, modified/clear flags, next undo/redo, units, up/front/right vectors, model/view counts, active sheet, grid systems and alignments.")]
    public Task<string> GetDocumentMetadata(CancellationToken ct = default) => Call("get_document_metadata", new JObject(), ct);

    [McpServerTool(Name = "nwd_peek_file_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Read the header of a local .nwd/.nwf/.nwc (document properties and sheets) without opening it or touching the current document.")]
    public Task<string> PeekFileInfo(string path, CancellationToken ct = default)
        => Call("peek_file_info", new JObject { ["path"] = path }, ct);

    [McpServerTool(Name = "nwd_get_scene_bounds", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Bounding box (min, max, center, size in document units) of the whole scene and of each model. ignoreHidden defaults to true.")]
    public Task<string> GetSceneBounds(bool ignoreHidden = true, CancellationToken ct = default)
        => Call("get_scene_bounds", new JObject { ["ignore_hidden"] = ignoreHidden }, ct);

    [McpServerTool(Name = "nwd_get_item_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Ancestors (root first), first object ancestor and paged children of one item (any id form). " + IdModeHelp + " " + PageHelp)]
    public Task<string> GetItemHierarchy(string itemId, string? idMode = null, int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = ScopeArgs(null, null, null, limit, offset, cursor);
        p["item_id"] = itemId;
        return Call("get_item_hierarchy", WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_get_items_geometry", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Bounding box, transform and node flags (geometry, composite, insert, layer, collection) per item. " + ScopeHelp + " " + AllHelp + " " + IdModeHelp + " " + PageHelp)]
    public Task<string> GetItemsGeometry(string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, bool geometryOnly = false,
        bool ignoreHidden = false, string? idMode = null, int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = WithAll(ScopeArgs(itemIds, setId, selection, limit, offset, cursor), all, geometryOnly);
        p["ignore_hidden"] = ignoreHidden;
        return Call("get_items_geometry", WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_get_visibility_state", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Hidden, required and frozen items across all models: counts and up to limit (default 200) top-most items of each (a flagged parent stands for its subtree), plus the count hidden by the source models. " + IdModeHelp)]
    public Task<string> GetVisibilityState(string? idMode = null, int? limit = null, CancellationToken ct = default)
    {
        var p = new JObject();
        if (limit.HasValue) p["limit"] = limit.Value;
        return Call("get_visibility_state", WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_describe_properties", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Discover which property categories and properties exist in a scope: item counts, data type, sample value and the \"Category/Property\" spec to use with other tools. Scans up to maxScan items (default 5000). " + ScopeHelp + " " + AllHelp)]
    public Task<string> DescribeProperties(string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, bool geometryOnly = false,
        int? maxScan = null, CancellationToken ct = default)
    {
        var p = WithAll(ScopeArgs(itemIds, setId, selection), all, geometryOnly);
        if (maxScan.HasValue) p["max_scan"] = maxScan.Value;
        return Call("describe_properties", p, ct);
    }

    [McpServerTool(Name = "nwd_property_value_histogram", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Distinct values of one property (\"Category/Property\", display or internal names) with counts, how many items lack it, and the top values (default 50). Use it to check data quality or coverage, e.g. which 4D task codes are used. " + ScopeHelp + " " + AllHelp)]
    public Task<string> PropertyValueHistogram(string property, string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false,
        bool geometryOnly = false, int? top = null, int? maxScan = null, CancellationToken ct = default)
    {
        var p = WithAll(ScopeArgs(itemIds, setId, selection), all, geometryOnly);
        p["property"] = property;
        if (top.HasValue) p["top"] = top.Value;
        if (maxScan.HasValue) p["max_scan"] = maxScan.Value;
        return Call("property_value_histogram", p, ct);
    }

    [McpServerTool(Name = "nwd_export_properties_table", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Write a CSV (UTF-8 with BOM, opens in Excel; formula-safe) of items × properties (each \"Category/Property\") to %LOCALAPPDATA%\\Bimwright\\nwd-mcp\\exports and return its path, row count, how many items had each property and a preview. Does not change the document. fileName is a name only, never a folder. Up to maxRows (default 200000). " + ScopeHelp + " " + AllHelp + " " + IdModeHelp)]
    public Task<string> ExportPropertiesTable(string[] properties, string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false,
        bool geometryOnly = false, bool includeName = true, string? fileName = null, int? maxRows = null, string? idMode = null, CancellationToken ct = default)
    {
        var p = WithAll(ScopeArgs(itemIds, setId, selection), all, geometryOnly);
        p["properties"] = new JArray(properties);
        p["include_name"] = includeName;
        if (!string.IsNullOrEmpty(fileName)) p["file_name"] = fileName;
        if (maxRows.HasValue) p["max_rows"] = maxRows.Value;
        return Call("export_properties_table", WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_get_hyperlinks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Hyperlinks (name, url) attached to items; lists only items that have links. " + ScopeHelp + " " + AllHelp + " " + IdModeHelp + " " + PageHelp)]
    public Task<string> GetHyperlinks(string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false,
        string? idMode = null, int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
        => Call("get_hyperlinks", WithIdMode(WithAll(ScopeArgs(itemIds, setId, selection, limit, offset, cursor), all, false), idMode), ct);

    [McpServerTool(Name = "nwd_get_grids", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Grid systems with origin, active flag, levels (name, elevation) and grid line names.")]
    public Task<string> GetGrids(CancellationToken ct = default) => Call("get_grids", new JObject(), ct);

    [McpServerTool(Name = "nwd_locate_items_on_grid", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("For each item, the grid intersection (line names) and level nearest its bounding-box centre, with offset and distance. gridSystem is a name or index (default the active one). " + ScopeHelp + " " + IdModeHelp + " " + PageHelp)]
    public Task<string> LocateItemsOnGrid(string[]? itemIds = null, string? setId = null, string? selection = null, string? gridSystem = null,
        string? idMode = null, int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = ScopeArgs(itemIds, setId, selection, limit, offset, cursor);
        if (!string.IsNullOrEmpty(gridSystem)) p["grid_system"] = gridSystem;
        return Call("locate_items_on_grid", WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_get_comments", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Comments (author, status, date, body) on saved viewpoints and selection/search sets. source = all (default), viewpoints or sets. Lists only commented items. " + PageHelp)]
    public Task<string> GetComments(string source = "all", int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = ScopeArgs(null, null, null, limit, offset, cursor);
        p["source"] = source;
        return Call("get_comments", p, ct);
    }

    [McpServerTool(Name = "nwd_list_sheets", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Sheets of a multi-sheet document (id, name, 2D/3D type, loaded, active).")]
    public Task<string> ListSheets(CancellationToken ct = default) => Call("list_sheets", new JObject(), ct);

    [McpServerTool(Name = "nwd_list_alignments", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Civil alignments in the scene with start/end stations and labels, the active one and whether rendering is enabled.")]
    public Task<string> ListAlignments(CancellationToken ct = default) => Call("list_alignments", new JObject(), ct);

    internal static JObject WithIdMode(JObject p, string? idMode)
    {
        if (!string.IsNullOrEmpty(idMode)) p["id_mode"] = idMode;
        return p;
    }

    internal static JObject WithAll(JObject p, bool all, bool geometryOnly)
    {
        if (all) p["all"] = true;
        if (geometryOnly) p["geometry_only"] = true;
        return p;
    }

    internal static JObject ScopeArgs(string[]? itemIds, string? setId, string? selection,
        int? limit = null, int? offset = null, string? cursor = null)
    {
        var p = new JObject();
        if (itemIds != null) p["item_ids"] = new JArray(itemIds);
        if (!string.IsNullOrEmpty(setId)) p["set_id"] = setId;
        if (!string.IsNullOrEmpty(selection)) p["selection"] = selection;
        if (limit.HasValue) p["limit"] = limit.Value;
        if (offset.HasValue) p["offset"] = offset.Value;
        if (!string.IsNullOrEmpty(cursor)) p["cursor"] = cursor;
        return p;
    }

    private async Task<string> Call(string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await _client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.None);
        }
        catch (NwdGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.None);
        }
    }
}
