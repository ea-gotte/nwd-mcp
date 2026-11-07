#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>Base for review write commands (visibility, appearance, camera): not read-only.</summary>
public abstract class ReviewWriteCommand : DocumentCommand
{
    public override bool IsReadOnly => false;

    protected static NW.Color ColorArg(JObject p, string key)
        => ColorSpec.TryParse((string?)p[key], out var c, out var e) ? new NW.Color(c.R, c.G, c.B) : throw CommandFailure.Invalid(e);

    protected static double? Transparency(JObject p)
    {
        var t = p["transparency"];
        if (t == null || t.Type == JTokenType.Null) return null;
        if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw CommandFailure.Invalid("transparency must be a number 0..1");
        var v = (double)t;
        if (v < 0 || v > 1) throw CommandFailure.Invalid("transparency must be between 0 and 1");
        return v;
    }
}

/// <summary>Set, add to, remove from, select all or clear the current selection.</summary>
public sealed class SelectItemsHandler : ReviewWriteCommand
{
    public override string Name => "select_items";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var mode = ((string?)p["mode"] ?? "set").ToLowerInvariant();
        var sel = doc.CurrentSelection;
        JArray unresolved = new JArray();
        switch (mode)
        {
            case "all": sel.SelectAll(); break;
            case "clear": sel.Clear(); break;
            case "set": sel.CopyFrom(Scope(p, doc, out unresolved)); break;
            case "add": sel.AddRange(Scope(p, doc, out unresolved)); break;
            case "remove": foreach (var i in Scope(p, doc, out unresolved)) sel.Remove(i); break;
            default: throw CommandFailure.Invalid("mode must be set, add, remove, all or clear");
        }
        return new JObject { ["mode"] = mode, ["selected_count"] = sel.SelectedItems.Count, ["unresolved"] = unresolved };
    }
}

/// <summary>Hides everything except the scope (its ancestors stay visible so it can display).</summary>
public sealed class IsolateItemsHandler : ReviewWriteCommand
{
    public override string Name => "isolate_items";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var keep = new HashSet<NW.ModelItem>(Scope(p, doc, out var unresolved));
        if (keep.Count == 0) throw CommandFailure.Invalid("nothing to isolate: the scope resolved to no items");
        var path = new HashSet<NW.ModelItem>();
        foreach (var k in keep) foreach (NW.ModelItem a in k.Ancestors) path.Add(a);

        var hide = new List<NW.ModelItem>();
        void Walk(NW.ModelItem item)
        {
            if (keep.Contains(item)) return;          // kept with its whole subtree
            if (!path.Contains(item)) { hide.Add(item); return; }
            foreach (NW.ModelItem c in item.Children) Walk(c);
        }
        foreach (NW.Model m in doc.Models) if (m.RootItem != null) Walk(m.RootItem);

        if (Bool(p, "reset_hidden_first", true)) doc.Models.ResetAllHidden();
        doc.Models.SetHidden(hide, true);
        return new JObject { ["kept"] = keep.Count, ["hidden_branches"] = hide.Count, ["unresolved"] = unresolved, ["undo"] = "nwd_reset_visibility which=hidden" };
    }
}

/// <summary>Sets hidden, required or frozen on a scope.</summary>
public sealed class SetItemFlagsHandler : ReviewWriteCommand
{
    public override string Name => "set_item_flags";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var flag = ((string?)p["flag"] ?? "").ToLowerInvariant();
        bool value = Bool(p, "value", true);
        var items = Scope(p, doc, out var unresolved);
        switch (flag)
        {
            case "hidden": doc.Models.SetHidden(items, value); break;
            case "required": doc.Models.SetRequired(items, value); break;
            case "frozen": doc.Models.SetFrozen(items, value); break;
            default: throw CommandFailure.Invalid("flag must be hidden, required or frozen");
        }
        return new JObject { ["flag"] = flag, ["value"] = value, ["count"] = items.Count, ["unresolved"] = unresolved };
    }
}

/// <summary>Resets hidden / required / frozen flags document-wide.</summary>
public sealed class ResetVisibilityHandler : ReviewWriteCommand
{
    public override string Name => "reset_visibility";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var which = ((string?)p["which"] ?? "hidden").ToLowerInvariant();
        switch (which)
        {
            case "hidden": doc.Models.ResetAllHidden(); break;
            case "hidden_to_model_state": doc.Models.ResetAllHiddenToModelState(); break;
            case "required": doc.Models.ResetAllRequired(); break;
            case "frozen": doc.Models.ResetAllFrozen(); break;
            case "all": doc.Models.ResetAllHidden(); doc.Models.ResetAllRequired(); doc.Models.ResetAllFrozen(); break;
            default: throw CommandFailure.Invalid("which must be hidden, hidden_to_model_state, required, frozen or all");
        }
        return new JObject { ["reset"] = which };
    }
}

/// <summary>Overrides colour and/or transparency on a scope, temporarily (default) or permanently.</summary>
public sealed class OverrideAppearanceHandler : ReviewWriteCommand
{
    public override string Name => "override_appearance";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        bool permanent = Bool(p, "permanent", false);
        NW.Color? color = string.IsNullOrEmpty((string?)p["color"]) ? null : ColorArg(p, "color");
        var transparency = Transparency(p);
        if (color == null && transparency == null) throw CommandFailure.Invalid("give color and/or transparency");
        var items = Scope(p, doc, out var unresolved);
        if (color != null)
        {
            if (permanent) doc.Models.OverridePermanentColor(items, color); else doc.Models.OverrideTemporaryColor(items, color);
        }
        if (transparency != null)
        {
            if (permanent) doc.Models.OverridePermanentTransparency(items, transparency.Value); else doc.Models.OverrideTemporaryTransparency(items, transparency.Value);
        }
        return new JObject
        {
            ["count"] = items.Count, ["permanent"] = permanent, ["color"] = (string?)p["color"], ["transparency"] = transparency,
            ["unresolved"] = unresolved, ["undo"] = "nwd_reset_appearance",
        };
    }
}

/// <summary>Colours items by the value of a property and returns the legend (value, colour, count).</summary>
public sealed class ColorByPropertyHandler : ReviewWriteCommand
{
    public override string Name => "color_by_property";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var spec = OneProperty(p);
        bool permanent = Bool(p, "permanent", false);
        int maxValues = Int(p, "max_values", 64, 1, 1000);
        var items = Scope(p, doc, out var unresolved);

        var groups = new Dictionary<string, List<NW.ModelItem>>(StringComparer.Ordinal);
        var missing = new List<NW.ModelItem>();
        foreach (var item in items)
        {
            var v = NwJson.Text(NwJson.FindProperty(item, spec)?.Value);
            if (v == null) { missing.Add(item); continue; }
            if (!groups.TryGetValue(v, out var list)) groups[v] = list = new List<NW.ModelItem>();
            list.Add(item);
        }
        if (groups.Count > maxValues)
            throw CommandFailure.Invalid($"{groups.Count} distinct values exceed max_values={maxValues}; narrow the scope or raise max_values");

        var mapping = p["colors"] as JObject;
        var ordered = groups.OrderByDescending(g => g.Value.Count).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var palette = ColorSpec.Palette(ordered.Count);
        var legend = new JArray();
        for (int i = 0; i < ordered.Count; i++)
        {
            var color = palette[i];
            if (mapping?[ordered[i].Key] is JToken given && !ColorSpec.TryParse((string?)given, out color, out var err)) throw CommandFailure.Invalid(err);
            Apply(doc, ordered[i].Value, new NW.Color(color.R, color.G, color.B), permanent);
            legend.Add(new JObject { ["value"] = ordered[i].Key, ["color"] = color.Hex, ["count"] = ordered[i].Value.Count });
        }
        string? missingHex = null;
        if (missing.Count > 0 && !string.IsNullOrEmpty((string?)p["missing_color"]))
        {
            if (!ColorSpec.TryParse((string?)p["missing_color"], out var mc, out var err)) throw CommandFailure.Invalid(err);
            Apply(doc, missing, new NW.Color(mc.R, mc.G, mc.B), permanent);
            missingHex = mc.Hex;
        }
        return new JObject
        {
            ["property"] = spec.Label, ["permanent"] = permanent, ["legend"] = legend,
            ["missing"] = new JObject { ["count"] = missing.Count, ["color"] = missingHex },
            ["unresolved"] = unresolved, ["undo"] = "nwd_reset_appearance",
        };
    }

    private static void Apply(NW.Document doc, List<NW.ModelItem> items, NW.Color color, bool permanent)
    {
        if (permanent) doc.Models.OverridePermanentColor(items, color); else doc.Models.OverrideTemporaryColor(items, color);
    }
}

/// <summary>Removes colour/transparency overrides on a scope or everywhere.</summary>
public sealed class ResetAppearanceHandler : ReviewWriteCommand
{
    public override string Name => "reset_appearance";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var kind = ((string?)p["kind"] ?? "temporary").ToLowerInvariant();
        if (kind != "temporary" && kind != "permanent" && kind != "both") throw CommandFailure.Invalid("kind must be temporary, permanent or both");
        bool hasScope = p["item_ids"] != null || p["set_id"] != null || p["selection"] != null || p["all"] != null;
        int count = -1;
        JArray unresolved = new JArray();
        if (hasScope)
        {
            var items = Scope(p, doc, out unresolved);
            count = items.Count;
            if (kind != "permanent") doc.Models.ResetTemporaryMaterials(items);
            if (kind != "temporary") doc.Models.ResetPermanentMaterials(items);
        }
        else
        {
            if (kind != "permanent") doc.Models.ResetAllTemporaryMaterials();
            if (kind != "temporary") doc.Models.ResetAllPermanentMaterials();
        }
        return new JObject { ["kind"] = kind, ["scope"] = hasScope ? "items" : "document", ["count"] = count < 0 ? null : (JToken)count, ["unresolved"] = unresolved };
    }
}

/// <summary>Frames the camera on a scope's bounding box (optionally selecting it).</summary>
public sealed class ZoomToItemsHandler : ReviewWriteCommand
{
    public override string Name => "zoom_to_items";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var items = Scope(p, doc, out var unresolved);
        var box = ViewHelper.UnionBox(items);
        var vp = doc.CurrentViewpoint.CreateCopy();
        vp.ZoomBox(box);
        doc.CurrentViewpoint.CopyFrom(vp);
        if (Bool(p, "select", false)) doc.CurrentSelection.CopyFrom(items);
        return new JObject { ["count"] = items.Count, ["box"] = NwJson.Box(box), ["camera"] = ViewHelper.Camera(doc.CurrentViewpoint.CreateCopy()), ["unresolved"] = unresolved };
    }
}

/// <summary>Moves the camera: position / target / up, projection, render style, lighting, or a full Navisworks camera JSON.</summary>
public sealed class SetCameraHandler : ReviewWriteCommand
{
    public override string Name => "set_camera";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var vp = doc.CurrentViewpoint.CreateCopy();
        if (p["camera_json"] is JToken cj && cj.Type != JTokenType.Null)
        {
            var json = cj.Type == JTokenType.String ? (string)cj! : cj.ToString(Newtonsoft.Json.Formatting.None);
            if (!vp.TrySetCamera(json)) throw CommandFailure.Invalid("camera_json was not accepted by Navisworks (use the 'camera' object from nwd_get_current_viewpoint or nwd_get_section)");
        }
        if (Point(p, "position") is NW.Point3D pos) vp.Position = pos;
        if (Point(p, "target") is NW.Point3D target) vp.PointAt(target);
        if (Point(p, "up") is NW.Point3D up) vp.AlignUp(new NW.Vector3D(up.X, up.Y, up.Z));
        if ((string?)p["projection"] is string proj)
            vp.Projection = proj.ToLowerInvariant() switch
            {
                "perspective" => NW.ViewpointProjection.Perspective,
                "orthographic" => NW.ViewpointProjection.Orthographic,
                _ => throw CommandFailure.Invalid("projection must be perspective or orthographic"),
            };
        if ((string?)p["render_style"] is string rs)
            vp.RenderStyle = Enum.TryParse<NW.ViewpointRenderStyle>(rs.Replace("_", ""), true, out var r) ? r
                : throw CommandFailure.Invalid("render_style must be full_render, preview, shaded, wireframe or hidden_line");
        if ((string?)p["lighting"] is string li)
            vp.Lighting = Enum.TryParse<NW.ViewpointLighting>(li.Replace("_", ""), true, out var l) ? l
                : throw CommandFailure.Invalid("lighting must be none, scene_lights, headlight or full_lights");
        doc.CurrentViewpoint.CopyFrom(vp);
        return ViewHelper.Camera(doc.CurrentViewpoint.CreateCopy());
    }

    private static NW.Point3D? Point(JObject p, string key)
    {
        if (!(p[key] is JArray a)) return null;
        if (a.Count != 3 || a.Any(x => x.Type != JTokenType.Float && x.Type != JTokenType.Integer)) throw CommandFailure.Invalid(key + " must be [x, y, z]");
        return new NW.Point3D((double)a[0], (double)a[1], (double)a[2]);
    }
}

/// <summary>Sectioning: off, a section box around items (with padding), or Navisworks clip-planes JSON.</summary>
public sealed class SetSectionHandler : ReviewWriteCommand
{
    public override string Name => "set_section";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var mode = ((string?)p["mode"] ?? "").ToLowerInvariant();
        var view = ViewHelper.ActiveView(doc);
        switch (mode)
        {
            case "json":
            {
                var j = p["clipping_planes_json"] ?? throw CommandFailure.Invalid("clipping_planes_json is required for mode=json");
                var json = j.Type == JTokenType.String ? (string)j! : j.ToString(Newtonsoft.Json.Formatting.None);
                if (!view.TrySetClippingPlanes(json)) throw CommandFailure.Invalid("Navisworks rejected clipping_planes_json (start from nwd_get_section)");
                return new JObject { ["mode"] = mode, ["applied"] = true };
            }
            case "off":
            case "box":
            {
#if !(NAVIS2026 || NAVIS2027)
                throw new CommandFailure("UNSUPPORTED_IN_THIS_VERSION", "mode=off/box needs the ClipPlaneSet API (Navisworks 2026+); use mode=json here");
#else
                var vp = doc.CurrentViewpoint.CreateCopy();
                var cps = vp.ClipPlanes ?? new NW.ClipPlaneSet();
                JArray unresolved = new JArray();
                if (mode == "off") cps.Enabled = false;
                else
                {
                    var box = ViewHelper.UnionBox(Scope(p, doc, out unresolved));
                    double pad = p["padding"]?.Type == JTokenType.Float || p["padding"]?.Type == JTokenType.Integer ? (double)p["padding"]! : 0;
                    var padded = new NW.BoundingBox3D(new NW.Point3D(box.Min.X - pad, box.Min.Y - pad, box.Min.Z - pad),
                                                      new NW.Point3D(box.Max.X + pad, box.Max.Y + pad, box.Max.Z + pad));
                    cps.Mode = NW.ClipPlaneSetMode.Box;
                    cps.Box = padded;
                    cps.Enabled = true;
                    if (Bool(p, "zoom", true)) vp.ZoomBox(padded);
                }
                vp.ClipPlanes = cps;
                doc.CurrentViewpoint.CopyFrom(vp);
                var now = doc.CurrentViewpoint.CreateCopy().ClipPlanes;
                return new JObject { ["mode"] = mode, ["enabled"] = now?.Enabled, ["box"] = now != null && now.Mode == NW.ClipPlaneSetMode.Box ? NwJson.Box(now.Box) : null, ["unresolved"] = unresolved };
#endif
            }
            default: throw CommandFailure.Invalid("mode must be off, box or json");
        }
    }
}

/// <summary>Background: plain colour, graduated (top/bottom) or horizon (sky/ground).</summary>
public sealed class SetBackgroundHandler : ReviewWriteCommand
{
    public override string Name => "set_background";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var mode = ((string?)p["mode"] ?? "plain").ToLowerInvariant();
        switch (mode)
        {
            case "plain": doc.SetPlainBackground(ColorArg(p, "color")); break;
            case "graduated": doc.SetGraduatedBackground(ColorArg(p, "top_color"), ColorArg(p, "bottom_color")); break;
            case "horizon": doc.SetHorizonBackground(ColorArg(p, "sky_top_color"), ColorArg(p, "sky_bottom_color"), ColorArg(p, "ground_top_color"), ColorArg(p, "ground_bottom_color")); break;
            default: throw CommandFailure.Invalid("mode must be plain, graduated or horizon");
        }
        return new JObject { ["mode"] = mode };
    }
}

/// <summary>Replaces the active view's redlines with Navisworks redline JSON (from nwd_get_redlines).</summary>
public sealed class SetRedlinesHandler : ReviewWriteCommand
{
    public override string Name => "set_redlines";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var j = p["redlines_json"] ?? throw CommandFailure.Invalid("redlines_json is required");
        var json = j.Type == JTokenType.String ? (string)j! : j.ToString(Newtonsoft.Json.Formatting.None);
        if (!ViewHelper.ActiveView(doc).TrySetRedlines(json)) throw CommandFailure.Invalid("Navisworks rejected redlines_json (start from nwd_get_redlines)");
        return new JObject { ["applied"] = true };
    }
}
#endif
