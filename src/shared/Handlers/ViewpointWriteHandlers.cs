#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>Rename, delete, move, create a folder, or overwrite a saved viewpoint with the current view.</summary>
public sealed class ManageSavedViewpointHandler : ReviewWriteCommand
{
    public override string Name => "manage_saved_viewpoint";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var vps = doc.SavedViewpoints;
        var root = vps.RootItem;
        var action = ((string?)p["action"] ?? "").ToLowerInvariant();

        NW.GroupItem Folder(string? id)
        {
            if (string.IsNullOrEmpty(id)) return root;
            return ViewHelper.ResolveSaved(root, id!, "folder") as NW.GroupItem ?? throw CommandFailure.Invalid($"'{id}' is not a folder");
        }
        NW.SavedItem Target() => ViewHelper.ResolveSaved(root, (string?)p["viewpoint_id"] ?? throw CommandFailure.Invalid("viewpoint_id is required"), "saved viewpoint");
        NW.GroupItem ParentOf(NW.SavedItem item) => item.Parent ?? root;

        switch (action)
        {
            case "rename":
            {
                var item = Target();
                var name = (string?)p["name"];
                if (string.IsNullOrWhiteSpace(name)) throw CommandFailure.Invalid("name is required");
                vps.EditDisplayName(item, name!.Trim());
                return new JObject { ["action"] = action, ["name"] = name.Trim() };
            }
            case "delete":
            {
                var item = Target();
                if (!vps.Remove(ParentOf(item), item)) throw new CommandFailure("NOT_REMOVED", "Navisworks did not remove the item");
                return new JObject { ["action"] = action, ["removed"] = item.DisplayName, ["undo"] = "Navisworks Undo (Ctrl+Z) only" };
            }
            case "move":
            {
                var item = Target();
                var oldParent = ParentOf(item);
                var newParent = Folder((string?)p["folder_id"]);
                int oldIndex = oldParent.Children.IndexOf(item);
                int newIndex = Int(p, "index", newParent.Children.Count, 0, newParent.Children.Count);
                vps.Move(oldParent, oldIndex, newParent, newIndex);
                return new JObject { ["action"] = action, ["moved"] = item.DisplayName, ["folder"] = newParent == root ? "(root)" : newParent.DisplayName };
            }
            case "create_folder":
            {
                var name = (string?)p["name"];
                if (string.IsNullOrWhiteSpace(name)) throw CommandFailure.Invalid("name is required");
                var parent = Folder((string?)p["folder_id"]);
                vps.AddCopy(parent, new NW.FolderItem { DisplayName = name!.Trim() });
                return new JObject { ["action"] = action, ["created"] = name.Trim(), ["folder"] = parent == root ? "(root)" : parent.DisplayName };
            }
            case "update_from_current":
            {
                var sv = Target() as NW.SavedViewpoint ?? throw CommandFailure.Invalid("viewpoint_id is a folder, not a viewpoint");
                vps.ReplaceFromCurrentView(sv);
                return new JObject { ["action"] = action, ["updated"] = sv.DisplayName };
            }
            default: throw CommandFailure.Invalid("action must be rename, delete, move, create_folder or update_from_current");
        }
    }
}

/// <summary>Adds a comment to a saved viewpoint or a selection/search set.</summary>
public sealed class AddCommentHandler : ReviewWriteCommand
{
    public override string Name => "add_comment";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var target = ((string?)p["target"] ?? "").ToLowerInvariant();
        var id = (string?)p["id"] ?? throw CommandFailure.Invalid("id is required");
        var body = (string?)p["body"];
        if (string.IsNullOrWhiteSpace(body)) throw CommandFailure.Invalid("body is required");
        var status = Enum.TryParse<NW.CommentStatus>((string?)p["status"] ?? "New", true, out var s) ? s
            : throw CommandFailure.Invalid("status must be New, Active, Approved or Resolved");
        var author = (string?)p["author"];
        var comment = string.IsNullOrEmpty(author) ? doc.CreateCommentWithUniqueId(body!, status) : doc.CreateCommentWithUniqueId(body!, status, author!);

        switch (target)
        {
            case "viewpoint":
                doc.SavedViewpoints.AddComment(ViewHelper.ResolveSaved(doc.SavedViewpoints.RootItem, id, "saved viewpoint"), comment); break;
            case "set":
                doc.SelectionSets.AddComment(ViewHelper.ResolveSaved(doc.SelectionSets.RootItem, id, "set"), comment); break;
            default: throw CommandFailure.Invalid("target must be viewpoint or set");
        }
        return new JObject { ["target"] = target, ["comment"] = GetCommentsHandler.Comment(comment) };
    }
}

/// <summary>Saves the current view including hidden/colour overrides (CaptureRuntimeOverrides) as a named viewpoint.</summary>
public sealed class SaveViewpointWithOverridesHandler : ReviewWriteCommand
{
    public override string Name => "save_viewpoint_with_overrides";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var name = (string?)p["name"];
        if (string.IsNullOrWhiteSpace(name)) throw CommandFailure.Invalid("name is required");
        var vps = doc.SavedViewpoints;
        var sv = vps.CaptureRuntimeOverrides() ?? throw new CommandFailure("CAPTURE_FAILED", "Navisworks could not capture the view");
        sv.DisplayName = name!.Trim();
        var folderId = (string?)p["folder_id"];
        if (string.IsNullOrEmpty(folderId)) vps.AddCopy(sv);
        else vps.AddCopy(ViewHelper.ResolveSaved(vps.RootItem, folderId!, "folder") as NW.GroupItem ?? throw CommandFailure.Invalid($"'{folderId}' is not a folder"), sv);
        return new JObject
        {
            ["saved"] = sv.DisplayName,
            ["contains_appearance_overrides"] = sv.ContainsAppearanceOverrides,
            ["contains_visibility_overrides"] = sv.ContainsVisibilityOverrides,
        };
    }
}

/// <summary>Renders one PNG per saved viewpoint (all, or those under a folder), then restores the camera.</summary>
public sealed class BatchViewpointImagesHandler : ReviewWriteCommand
{
    public override string Name => "batch_viewpoint_images";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        int width = Int(p, "width", 1280, 16, 8192), height = Int(p, "height", 720, 16, 8192);
        int max = Int(p, "max_images", 50, 1, 500);
        var style = ViewHelper.Style((string?)p["style"]);
        var root = doc.SavedViewpoints.RootItem;
        var folderId = (string?)p["folder_id"];
        var start = string.IsNullOrEmpty(folderId) ? root
            : ViewHelper.ResolveSaved(root, folderId!, "folder") as NW.GroupItem ?? throw CommandFailure.Invalid($"'{folderId}' is not a folder");

        var targets = new List<(NW.SavedViewpoint Vp, string Path)>();
        void Walk(NW.GroupItem g, string prefix)
        {
            foreach (NW.SavedItem item in g.Children)
            {
                var path = prefix + "/" + SavedItemResolver.Escape(item.DisplayName);
                if (item is NW.SavedViewpoint sv) targets.Add((sv, path));
                else if (item is NW.GroupItem sub) Walk(sub, path);
            }
        }
        Walk(start, "");

        var original = doc.CurrentViewpoint.CreateCopy();
        var images = new JArray();
        bool truncated = false;
        try
        {
            foreach (var (vp, path) in targets)
            {
                if (images.Count >= max || ctx.PastDeadline) { truncated = true; break; }
                doc.CurrentViewpoint.CopyFrom(vp.Viewpoint);
                var shot = ViewHelper.Capture(doc, width, height, style, true, "vp-" + vp.DisplayName, "vp");
                shot["viewpoint"] = vp.DisplayName;
                shot["viewpoint_id"] = SavedItemResolver.Id(vp.Guid, path);
                images.Add(shot);
            }
        }
        finally { doc.CurrentViewpoint.CopyFrom(original); }
        return new JObject { ["count"] = images.Count, ["of"] = targets.Count, ["truncated"] = truncated, ["images"] = images };
    }
}
#endif
