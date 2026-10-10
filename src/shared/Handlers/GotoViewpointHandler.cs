#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

public sealed class GotoViewpointHandler : INwdCommand
{
    public string Name => "goto_viewpoint";
    public bool IsReadOnly => false;

    public NwdCommandResult Execute(NwdCommandContext ctx, JObject p)
    {
        var meta = new NwdResponseMeta { TargetId = ctx.TargetId, NavisworksYear = ctx.NavisworksYear };
        var doc = NW.Application.ActiveDocument;
        if (doc is null)
            return NwdCommandResult.Fail(Guid.Empty, "NO_DOCUMENT", "no active Navisworks document", meta);
        var id = (string?)p["viewpoint_id"];
        if (string.IsNullOrEmpty(id))
            return NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT", "viewpoint_id is required", meta);
        NW.SavedViewpoint? sv;
        try
        {
            sv = SavedItemResolver.Resolve(doc.SavedViewpoints.RootItem.Children.Cast<NW.SavedItem>(), id!,
                x => x.DisplayName, x => x is NW.GroupItem g ? g.Children.Cast<NW.SavedItem>() : Enumerable.Empty<NW.SavedItem>(),
                x => x.Guid) as NW.SavedViewpoint;
        }
        catch (InvalidOperationException ex)
        {
            return NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT", ex.Message, meta);
        }
        if (sv is null)
            return NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT", $"Saved viewpoint '{id}' not found", meta);
        doc.CurrentViewpoint.CopyFrom(sv.Viewpoint);
        return NwdCommandResult.Success(Guid.Empty, new JObject { ["moved"] = true }, meta);
    }
}
#endif
