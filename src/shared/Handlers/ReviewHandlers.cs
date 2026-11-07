#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Bimwright.Nwd.Shared.Security;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>Shared view helpers for the review toolsets.</summary>
public static class ViewHelper
{
    public static NW.View ActiveView(NW.Document doc)
        => doc.ActiveView ?? throw new CommandFailure("NO_VIEW", "Navisworks has no active view");

    public static NW.ImageGenerationStyle Style(string? raw)
    {
        switch ((raw ?? "scene").Trim().ToLowerInvariant())
        {
            case "scene": return NW.ImageGenerationStyle.Scene;
            case "overlay": case "scene_plus_overlay": return NW.ImageGenerationStyle.ScenePlusOverlay;
            case "ray_trace": case "raytrace": return NW.ImageGenerationStyle.SceneUsingRayTrace;
            default: throw CommandFailure.Invalid("style must be scene, overlay or ray_trace");
        }
    }

    /// <summary>Renders the active view to a PNG in the captures folder and returns its description.</summary>
    public static JObject Capture(NW.Document doc, int width, int height, NW.ImageGenerationStyle style, bool sectioning, string? fileName, string prefix)
    {
        var view = ActiveView(doc);
        var dir = PathAllowlist.CapturesDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, TableExport.SafeFileName(fileName, prefix, ".png", DateTime.UtcNow));
        using (var bmp = view.GenerateImage(style, width, height, sectioning))
        {
            if (bmp == null) throw new CommandFailure("CAPTURE_FAILED", "Navisworks returned no image");
            bmp.Save(path, ImageFormat.Png);
        }
        return new JObject { ["path"] = path, ["width"] = width, ["height"] = height, ["style"] = style.ToString(), ["bytes"] = new FileInfo(path).Length };
    }

    public static JObject Camera(NW.Viewpoint vp)
    {
        JToken camera;
        try { camera = JToken.Parse(vp.GetCamera()); } catch (Exception) { camera = JValue.CreateNull(); }
        return new JObject
        {
            ["position"] = NwJson.Point(vp.Position),
            ["projection"] = vp.Projection.ToString(),
            ["render_style"] = vp.HasRenderStyle ? vp.RenderStyle.ToString() : null,
            ["lighting"] = vp.HasLighting ? vp.Lighting.ToString() : null,
            ["world_up"] = vp.HasWorldUpVector ? NwJson.Vector(vp.WorldUpVector) : null,
            ["camera"] = camera,
        };
    }

    public static NW.BoundingBox3D UnionBox(IEnumerable<NW.ModelItem> items)
    {
        NW.BoundingBox3D? box = null;
        foreach (var i in items)
        {
            var b = i.BoundingBox(false);
            if (b == null || b.IsEmpty) continue;
            box = box == null ? b : box.Extend(b);
        }
        return box ?? throw CommandFailure.Invalid("none of the items has geometry");
    }

    public static NW.SavedItem ResolveSaved(NW.GroupItem root, string id, string what)
    {
        try
        {
            return SavedItemResolver.Resolve(root.Children.Cast<NW.SavedItem>(), id, x => x.DisplayName,
                       x => x is NW.GroupItem g ? g.Children.Cast<NW.SavedItem>() : Enumerable.Empty<NW.SavedItem>(), x => x.Guid)
                   ?? throw CommandFailure.NotFound($"{what} '{id}' not found");
        }
        catch (InvalidOperationException ex) { throw CommandFailure.Invalid(ex.Message); }
    }
}

/// <summary>Renders the active view to a PNG (captures folder). Does not change the document.</summary>
public sealed class CaptureImageHandler : DocumentCommand
{
    public override string Name => "capture_image";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
        => ViewHelper.Capture(doc, Int(p, "width", 1280, 16, 8192), Int(p, "height", 720, 16, 8192),
            ViewHelper.Style((string?)p["style"]), Bool(p, "sectioning", true), (string?)p["file_name"], "view");
}

/// <summary>Current camera and sectioning (clip planes JSON from the active view).</summary>
public sealed class GetSectionHandler : DocumentCommand
{
    public override string Name => "get_section";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var view = ViewHelper.ActiveView(doc);
        JToken planes;
        try { planes = JToken.Parse(view.GetClippingPlanes()); } catch (Exception) { planes = JValue.CreateNull(); }
#if NAVIS2026 || NAVIS2027
        // Viewpoint.ClipPlanes / ClipPlaneSet are not in the 2024 API (2025 unverified).
        var cps = doc.CurrentViewpoint.CreateCopy().ClipPlanes;
        return new JObject
        {
            ["enabled"] = cps?.Enabled,
            ["mode"] = cps?.Mode.ToString(),
            ["box"] = cps != null && cps.Mode == NW.ClipPlaneSetMode.Box ? NwJson.Box(cps.Box) : null,
            ["clipping_planes_json"] = planes,
        };
#else
        return new JObject { ["clipping_planes_json"] = planes };
#endif
    }
}

/// <summary>Redlines of the active view as Navisworks JSON.</summary>
public sealed class GetRedlinesHandler : DocumentCommand
{
    public override string Name => "get_redlines";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var raw = ViewHelper.ActiveView(doc).GetRedlines();
        JToken parsed;
        try { parsed = string.IsNullOrEmpty(raw) ? JValue.CreateNull() : JToken.Parse(raw); } catch (Exception) { parsed = raw; }
        return new JObject { ["redlines_json"] = parsed };
    }
}
#endif
