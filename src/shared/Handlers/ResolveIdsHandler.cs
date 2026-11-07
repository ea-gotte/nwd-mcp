#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>
/// Converts items given by any scope (ids in any form, a set, or the current selection) to every id form:
/// path (stale after document changes), pid (stable path id), nid (numeric id) and guid (instance guid). Paged.
/// </summary>
public sealed class ResolveIdsHandler : INwdCommand
{
    public string Name => "resolve_ids";
    public bool IsReadOnly => true;

    public NwdCommandResult Execute(NwdCommandContext ctx, JObject p)
    {
        var meta = new NwdResponseMeta { TargetId = ctx.TargetId, NavisworksYear = ctx.NavisworksYear };
        var doc = NW.Application.ActiveDocument;
        if (doc is null || doc.IsClear)
            return NwdCommandResult.Fail(Guid.Empty, "NO_DOCUMENT", "no active Navisworks document", meta);

        if (!PageRequest.TryFrom(p, out var page, out var pageError))
            return NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT", pageError, meta);
        if (!ItemScope.TryResolve(p, doc, out var items, out var unresolved, out var scopeError))
            return NwdCommandResult.Fail(Guid.Empty, "INVALID_ARGUMENT", scopeError, meta);

        var emitter = new ItemIdEmitter(doc, ItemIdMode.Path);
        var (pageItems, pageInfo) = page.Apply(items);
        var rows = new JArray();
        foreach (var item in pageItems)
        {
            if (ctx.PastDeadline) { pageInfo["truncated_by_deadline"] = true; break; }
            var row = emitter.AllIdsOf(item);
            row["name"] = item.DisplayName;
            row["class"] = item.ClassDisplayName;
            rows.Add(row);
        }

        return NwdCommandResult.Success(Guid.Empty, new JObject
        {
            ["items"] = rows,
            ["unresolved"] = unresolved,
            ["page"] = pageInfo,
        }, meta);
    }
}
#endif
