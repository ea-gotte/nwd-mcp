using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Tools;

/// <summary>Toolset "model" (data part): user-defined properties and the read-only document database.</summary>
[McpServerToolType]
public sealed class DataTools
{
    private readonly PluginClient _client;
    public DataTools(PluginClient client) => _client = client;
    private Task<string> Call(string command, JObject p, CancellationToken ct) => ToolCall.Run(_client, command, p, ct);

    [McpServerTool(Name = "nwd_get_user_properties", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("User-defined property tabs (tab, internal name, properties) of items in a scope; only items that have some unless includeEmpty. tab filters one tab. " +
                 ModelTools.ScopeHelp + " " + ModelTools.AllHelp + " " + ModelTools.IdModeHelp + " " + ModelTools.PageHelp)]
    public Task<string> GetUserProperties(string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, bool geometryOnly = false,
        string? tab = null, bool includeEmpty = false, string? idMode = null, int? limit = null, int? offset = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = ModelTools.WithAll(ModelTools.ScopeArgs(itemIds, setId, selection, limit, offset, cursor), all, geometryOnly);
        if (!string.IsNullOrEmpty(tab)) p["tab"] = tab;
        if (includeEmpty) p["include_empty"] = true;
        return Call("get_user_properties", ModelTools.WithIdMode(p, idMode), ct);
    }

    [McpServerTool(Name = "nwd_db_list_tables", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Tables and views of the document's embedded database (Quantification and other data), for nwd_db_query.")]
    public Task<string> DbListTables(CancellationToken ct = default) => Call("db_list_tables", new JObject(), ct);

    [McpServerTool(Name = "nwd_db_query", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Run one read-only SELECT (or WITH … SELECT) on the document database and return columns and up to maxRows rows (default 500, max 5000). Writes, PRAGMA and multiple statements are rejected.")]
    public Task<string> DbQuery(string sql, int? maxRows = null, CancellationToken ct = default)
    {
        var p = new JObject { ["sql"] = sql };
        if (maxRows.HasValue) p["max_rows"] = maxRows.Value;
        return Call("db_query", p, ct);
    }
}
