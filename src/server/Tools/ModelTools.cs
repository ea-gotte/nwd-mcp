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
