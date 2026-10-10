#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

public sealed class ExecuteSearchSetHandler : INwdCommand
{
    public string Name => "execute_search_set";
    public bool IsReadOnly => true;

    public NwdCommandResult Execute(NwdCommandContext ctx, JObject p)
    {
        var meta = new NwdResponseMeta { TargetId = ctx.TargetId, NavisworksYear = ctx.NavisworksYear };
        if (((bool?)p["read_only_enforced"] ?? false) || ctx.ReadOnly) meta.ReadOnlyEnforced = true;
        var doc = NW.Application.ActiveDocument;
        if (doc is null)
            return NwdCommandResult.Fail(System.Guid.Empty, "NO_DOCUMENT", "no active Navisworks document", meta);
        var setId = (string?)p["set_id"];
        if (string.IsNullOrEmpty(setId))
            return NwdCommandResult.Fail(System.Guid.Empty, "INVALID_ARGUMENT", "set_id is required", meta);
        var select = ((bool?)p["select"] ?? false) && meta.ReadOnlyEnforced != true;
        NW.ModelItemCollection matches;
        try { matches = GetSelectionSetItemsHandler.ResolveNamedSet(doc, setId!); }
        catch (System.Exception ex)
        {
            return NwdCommandResult.Fail(System.Guid.Empty, "INVALID_ARGUMENT", ex.Message, meta);
        }
        if (select) doc.CurrentSelection.CopyFrom(matches);
        var itemIds = new JArray();
        var ids = new ModelItemIdMap(doc);
        foreach (NW.ModelItem mi in matches)
        {
            var id = ids.IdOf(mi);
            if (!string.IsNullOrEmpty(id)) itemIds.Add(id);
        }
        return NwdCommandResult.Success(System.Guid.Empty, new JObject
        {
            ["item_ids"] = itemIds, ["matched_count"] = matches.Count, ["selected"] = select
        }, meta);
    }
}
#endif
