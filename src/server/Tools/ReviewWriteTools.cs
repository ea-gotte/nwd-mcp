using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Tools;

/// <summary>Toolset "review_write": selection, visibility, appearance, camera, sectioning, redlines and saved viewpoints.</summary>
[McpServerToolType]
public sealed class ReviewWriteTools
{
    private const string Colors = "Colours: #RRGGBB, r,g,b (0-255) or a basic name.";
    private readonly PluginClient _client;
    public ReviewWriteTools(PluginClient client) => _client = client;

    private Task<string> Call(string command, JObject p, CancellationToken ct) => ToolCall.Run(_client, command, p, ct);

    private static JObject Scope(string[]? itemIds, string? setId, string? selection, bool all = false, bool geometryOnly = false)
        => ModelTools.WithAll(ModelTools.ScopeArgs(itemIds, setId, selection), all, geometryOnly);

    [McpServerTool(Name = "nwd_select_items", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Change the current selection. mode = set (default), add, remove, all or clear. For set/add/remove: " + ModelTools.ScopeHelp)]
    public Task<string> SelectItems(string mode = "set", string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, CancellationToken ct = default)
    {
        var p = mode is "all" or "clear" ? new JObject() : Scope(itemIds, setId, selection, all);
        p["mode"] = mode;
        return Call("select_items", p, ct);
    }

    [McpServerTool(Name = "nwd_isolate_items", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Hide everything except the scope (its ancestors stay visible). By default unhides everything first. Undo with nwd_reset_visibility which=hidden. " + ModelTools.ScopeHelp)]
    public Task<string> IsolateItems(string[]? itemIds = null, string? setId = null, string? selection = null, bool resetHiddenFirst = true, CancellationToken ct = default)
    {
        var p = Scope(itemIds, setId, selection);
        p["reset_hidden_first"] = resetHiddenFirst;
        return Call("isolate_items", p, ct);
    }

    [McpServerTool(Name = "nwd_set_item_flags", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Set flag = hidden, required or frozen to value (default true) on a scope; call again with value=false to revert. " + ModelTools.ScopeHelp + " " + ModelTools.AllHelp)]
    public Task<string> SetItemFlags(string flag, bool value = true, string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, bool geometryOnly = false, CancellationToken ct = default)
    {
        var p = Scope(itemIds, setId, selection, all, geometryOnly);
        p["flag"] = flag;
        p["value"] = value;
        return Call("set_item_flags", p, ct);
    }

    [McpServerTool(Name = "nwd_reset_visibility", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Reset document-wide flags: which = hidden (unhide all), hidden_to_model_state, required, frozen or all. The previous state is not kept.")]
    public Task<string> ResetVisibility(string which = "hidden", CancellationToken ct = default)
        => Call("reset_visibility", new JObject { ["which"] = which }, ct);

    [McpServerTool(Name = "nwd_override_appearance", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Override colour and/or transparency (0..1) on a scope. Temporary by default; permanent=true is saved with the file. Undo with nwd_reset_appearance. " + Colors + " " + ModelTools.ScopeHelp + " " + ModelTools.AllHelp)]
    public Task<string> OverrideAppearance(string? color = null, double? transparency = null, bool permanent = false, string[]? itemIds = null, string? setId = null,
        string? selection = null, bool all = false, bool geometryOnly = false, CancellationToken ct = default)
    {
        var p = Scope(itemIds, setId, selection, all, geometryOnly);
        if (!string.IsNullOrEmpty(color)) p["color"] = color;
        if (transparency.HasValue) p["transparency"] = transparency.Value;
        p["permanent"] = permanent;
        return Call("override_appearance", p, ct);
    }

    [McpServerTool(Name = "nwd_color_by_property", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Colour items by the value of one property (\"Category/Property\") and return the legend (value, colour, count). Distinct colours are picked automatically; colorsJson maps values to colours, e.g. {\"MEC\":\"#FF0000\"}. " +
                 "missingColor paints items without the property. Fails above maxValues (default 64) distinct values. Temporary unless permanent=true; undo with nwd_reset_appearance. " + Colors + " " + ModelTools.ScopeHelp + " " + ModelTools.AllHelp)]
    public Task<string> ColorByProperty(string property, string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, bool geometryOnly = false,
        string? colorsJson = null, string? missingColor = null, int? maxValues = null, bool permanent = false, CancellationToken ct = default)
    {
        var p = Scope(itemIds, setId, selection, all, geometryOnly);
        p["property"] = property;
        if (!string.IsNullOrEmpty(colorsJson)) p["colors"] = JObject.Parse(colorsJson);
        if (!string.IsNullOrEmpty(missingColor)) p["missing_color"] = missingColor;
        if (maxValues.HasValue) p["max_values"] = maxValues.Value;
        p["permanent"] = permanent;
        return Call("color_by_property", p, ct);
    }

    [McpServerTool(Name = "nwd_reset_appearance", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Remove colour/transparency overrides. kind = temporary (default), permanent or both. With no scope it resets the whole document. " + ModelTools.ScopeHelp)]
    public Task<string> ResetAppearance(string kind = "temporary", string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, CancellationToken ct = default)
    {
        var p = Scope(itemIds, setId, selection, all);
        p["kind"] = kind;
        return Call("reset_appearance", p, ct);
    }

    [McpServerTool(Name = "nwd_zoom_to_items", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Frame the camera on the bounding box of a scope; select=true also selects it. Returns the new camera. " + ModelTools.ScopeHelp)]
    public Task<string> ZoomToItems(string[]? itemIds = null, string? setId = null, string? selection = null, bool select = false, CancellationToken ct = default)
    {
        var p = Scope(itemIds, setId, selection);
        p["select"] = select;
        return Call("zoom_to_items", p, ct);
    }

    [McpServerTool(Name = "nwd_set_camera", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Move the camera. Any of: position [x,y,z], target [x,y,z] to look at, up [x,y,z], projection perspective|orthographic, renderStyle full_render|preview|shaded|wireframe|hidden_line, lighting none|scene_lights|headlight|full_lights, or cameraJson (the 'camera' object from a previous call). Coordinates in document units. Returns the new camera.")]
    public Task<string> SetCamera(double[]? position = null, double[]? target = null, double[]? up = null, string? projection = null, string? renderStyle = null,
        string? lighting = null, string? cameraJson = null, CancellationToken ct = default)
    {
        var p = new JObject();
        if (position != null) p["position"] = new JArray(position);
        if (target != null) p["target"] = new JArray(target);
        if (up != null) p["up"] = new JArray(up);
        if (!string.IsNullOrEmpty(projection)) p["projection"] = projection;
        if (!string.IsNullOrEmpty(renderStyle)) p["render_style"] = renderStyle;
        if (!string.IsNullOrEmpty(lighting)) p["lighting"] = lighting;
        if (!string.IsNullOrEmpty(cameraJson)) p["camera_json"] = cameraJson;
        return Call("set_camera", p, ct);
    }

    [McpServerTool(Name = "nwd_set_section", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Sectioning. mode = box: section box around a scope plus padding (document units) and zoom to it (Navisworks 2026+); mode = off: disable sectioning (2026+); mode = json: apply clippingPlanesJson from nwd_get_section (all years). " + ModelTools.ScopeHelp)]
    public Task<string> SetSection(string mode, string[]? itemIds = null, string? setId = null, string? selection = null, double padding = 0, bool zoom = true,
        string? clippingPlanesJson = null, CancellationToken ct = default)
    {
        var p = mode == "box" ? Scope(itemIds, setId, selection) : new JObject();
        p["mode"] = mode;
        if (mode == "box") { p["padding"] = padding; p["zoom"] = zoom; }
        if (!string.IsNullOrEmpty(clippingPlanesJson)) p["clipping_planes_json"] = clippingPlanesJson;
        return Call("set_section", p, ct);
    }

    [McpServerTool(Name = "nwd_set_background", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Viewport background. mode = plain (color), graduated (topColor, bottomColor) or horizon (skyTopColor, skyBottomColor, groundTopColor, groundBottomColor). " + Colors)]
    public Task<string> SetBackground(string mode = "plain", string? color = null, string? topColor = null, string? bottomColor = null, string? skyTopColor = null,
        string? skyBottomColor = null, string? groundTopColor = null, string? groundBottomColor = null, CancellationToken ct = default)
    {
        var p = new JObject { ["mode"] = mode };
        void Put(string k, string? v) { if (!string.IsNullOrEmpty(v)) p[k] = v; }
        Put("color", color); Put("top_color", topColor); Put("bottom_color", bottomColor);
        Put("sky_top_color", skyTopColor); Put("sky_bottom_color", skyBottomColor); Put("ground_top_color", groundTopColor); Put("ground_bottom_color", groundBottomColor);
        return Call("set_background", p, ct);
    }

    [McpServerTool(Name = "nwd_set_redlines", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Replace the active view's redlines with Navisworks redline JSON (edit the output of nwd_get_redlines). The previous redlines are replaced.")]
    public Task<string> SetRedlines(string redlinesJson, CancellationToken ct = default)
        => Call("set_redlines", new JObject { ["redlines_json"] = redlinesJson }, ct);

    [McpServerTool(Name = "nwd_manage_saved_viewpoint", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Organise saved viewpoints. action = rename (viewpointId, name), delete (viewpointId; only Navisworks Undo restores it), move (viewpointId, folderId, optional index), create_folder (name, optional folderId) or update_from_current (viewpointId). Ids are id:/path: values or names from nwd_list_viewpoints.")]
    public Task<string> ManageSavedViewpoint(string action, string? viewpointId = null, string? name = null, string? folderId = null, int? index = null, CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (!string.IsNullOrEmpty(viewpointId)) p["viewpoint_id"] = viewpointId;
        if (!string.IsNullOrEmpty(name)) p["name"] = name;
        if (!string.IsNullOrEmpty(folderId)) p["folder_id"] = folderId;
        if (index.HasValue) p["index"] = index.Value;
        return Call("manage_saved_viewpoint", p, ct);
    }

    [McpServerTool(Name = "nwd_add_comment", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Add a comment to a saved viewpoint (target=viewpoint) or a selection/search set (target=set). status = New (default), Active, Approved or Resolved; author optional.")]
    public Task<string> AddComment(string target, string id, string body, string status = "New", string? author = null, CancellationToken ct = default)
    {
        var p = new JObject { ["target"] = target, ["id"] = id, ["body"] = body, ["status"] = status };
        if (!string.IsNullOrEmpty(author)) p["author"] = author;
        return Call("add_comment", p, ct);
    }

    [McpServerTool(Name = "nwd_save_viewpoint_with_overrides", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Save the current view as a named viewpoint including hidden items and colour overrides, optionally inside folderId. Use it to keep a coloured review state (e.g. after nwd_color_by_property).")]
    public Task<string> SaveViewpointWithOverrides(string name, string? folderId = null, CancellationToken ct = default)
    {
        var p = new JObject { ["name"] = name };
        if (!string.IsNullOrEmpty(folderId)) p["folder_id"] = folderId;
        return Call("save_viewpoint_with_overrides", p, ct);
    }

    [McpServerTool(Name = "nwd_batch_viewpoint_images", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Render a PNG of every saved viewpoint (or those under folderId) into the captures folder, up to maxImages (default 50), then restore the camera. Returns each path with its viewpoint.")]
    public Task<string> BatchViewpointImages(string? folderId = null, int width = 1280, int height = 720, string style = "scene", int maxImages = 50, CancellationToken ct = default)
    {
        var p = new JObject { ["width"] = width, ["height"] = height, ["style"] = style, ["max_images"] = maxImages };
        if (!string.IsNullOrEmpty(folderId)) p["folder_id"] = folderId;
        return Call("batch_viewpoint_images", p, ct);
    }
}
