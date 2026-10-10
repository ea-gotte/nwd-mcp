#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

public sealed class GetSelectionSetItemsHandler : INwdCommand
{
    public string Name => "get_selection_set_items";
    public bool IsReadOnly => true;

    public NwdCommandResult Execute(NwdCommandContext ctx, JObject p)
    {
        var meta = new NwdResponseMeta { TargetId = ctx.TargetId, NavisworksYear = ctx.NavisworksYear };
        var doc = NW.Application.ActiveDocument;
        if (doc is null)
            return NwdCommandResult.Fail(Guid.Empty, "NO_DOCUMENT", "no active Navisworks document", meta);
        var setId = (string?)p["set_id"];
        if (string.IsNullOrEmpty(setId))
            return NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT", "set_id is required", meta);
        NW.ModelItemCollection items;
        try { items = ResolveNamedSet(doc, setId!); }
        catch (Exception ex) { return NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT", ex.Message, meta); }
        var itemIds = new JArray();
        var ids = new ModelItemIdMap(doc);
        foreach (NW.ModelItem mi in items)
        {
            var id = ids.IdOf(mi);
            if (!string.IsNullOrEmpty(id)) itemIds.Add(id);
        }
        return NwdCommandResult.Success(Guid.Empty, new JObject { ["item_ids"] = itemIds }, meta);
    }

    internal static NW.ModelItemCollection ResolveNamedSet(NW.Document doc, string name)
    {
        var hit = SavedItemResolver.Resolve(doc.SelectionSets.RootItem.Children.Cast<NW.SavedItem>(), name,
            x => x.DisplayName, x => x is NW.GroupItem g ? g.Children.Cast<NW.SavedItem>() : Enumerable.Empty<NW.SavedItem>(),
            x => x.Guid);
        if (hit == null) throw new InvalidOperationException($"set '{name}' not found");
        if (hit is NW.SelectionSet set)
            return set.HasSearch ? set.Search.FindAll(doc, false) : new NW.ModelItemCollection(set.ExplicitModelItems);
        throw new InvalidOperationException($"'{name}' is not a selection or search set");
    }
}
#endif
