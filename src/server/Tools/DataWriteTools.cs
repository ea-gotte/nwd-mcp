using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Tools;

/// <summary>Toolset "data_write": write user-defined properties and hyperlinks into the model.</summary>
[McpServerToolType]
public sealed class DataWriteTools
{
    private readonly PluginClient _client;
    public DataWriteTools(PluginClient client) => _client = client;
    private Task<string> Call(string command, JObject p, CancellationToken ct) => ToolCall.Run(_client, command, p, ct);

    [McpServerTool(Name = "nwd_set_user_properties", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Write a user-defined property tab (shown in Navisworks' Properties window) on every item of a scope. propertiesJson is an object of name → value (strings, numbers, booleans; null removes a property). " +
                 "mode = merge (default, keeps other properties of the tab) or replace. Applied as one Navisworks Undo step; the change is saved only when the file is saved. max_items guards large scopes (default 50000). " +
                 ModelTools.ScopeHelp + " " + ModelTools.AllHelp)]
    public Task<string> SetUserProperties(string tab, string propertiesJson, string mode = "merge", string[]? itemIds = null, string? setId = null, string? selection = null,
        bool all = false, bool geometryOnly = false, int? maxItems = null, CancellationToken ct = default)
    {
        var p = ModelTools.WithAll(ModelTools.ScopeArgs(itemIds, setId, selection), all, geometryOnly);
        p["tab"] = tab;
        p["properties"] = JObject.Parse(propertiesJson);
        p["mode"] = mode;
        if (maxItems.HasValue) p["max_items"] = maxItems.Value;
        return Call("set_user_properties", p, ct);
    }

    [McpServerTool(Name = "nwd_bulk_set_properties_from_table", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Write user properties from a local CSV (comma, semicolon or tab; Excel 'CSV UTF-8'). Each row is matched to items by keyColumn: match = id (the column holds item ids), property:<Category/Property> (equal to that property's value) or name_path (the item's names from the root joined by pathSeparator, default '>'; pathSkipRoot drops the file node). " +
                 "columns picks the CSV columns to write (default all except the key) into one tab. Empty cells are skipped unless writeEmpty. dryRun=true (default) reports matched rows, unmatched keys, rows matching several items and a sample plan; dryRun=false writes as one Navisworks Undo step. " +
                 "For property/name_path matching every item is a candidate (including group and layer nodes); pass all=true, geometryOnly=true or another scope to limit them. " + ModelTools.ScopeHelp)]
    public Task<string> BulkSetPropertiesFromTable(string path, string keyColumn, string tab, string match = "id", string[]? columns = null, bool dryRun = true, string mode = "merge",
        bool writeEmpty = false, bool caseSensitive = false, string? pathSeparator = null, bool pathSkipRoot = false, string[]? itemIds = null, string? setId = null,
        string? selection = null, bool all = false, bool geometryOnly = false, int? sample = null, CancellationToken ct = default)
    {
        var p = ModelTools.WithAll(ModelTools.ScopeArgs(itemIds, setId, selection), all, geometryOnly);
        p["path"] = path;
        p["key_column"] = keyColumn;
        p["tab"] = tab;
        p["match"] = match;
        if (columns != null) p["columns"] = new JArray(columns);
        p["dry_run"] = dryRun;
        p["mode"] = mode;
        if (writeEmpty) p["write_empty"] = true;
        if (caseSensitive) p["case_sensitive"] = true;
        if (!string.IsNullOrEmpty(pathSeparator)) p["path_separator"] = pathSeparator;
        if (pathSkipRoot) p["path_skip_root"] = true;
        if (sample.HasValue) p["sample"] = sample.Value;
        return Call("bulk_set_properties_from_table", p, ct);
    }

    [McpServerTool(Name = "nwd_remove_user_property_tab", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Remove a user-defined property tab from every item of a scope that has it (one Navisworks Undo step). " + ModelTools.ScopeHelp + " " + ModelTools.AllHelp)]
    public Task<string> RemoveUserPropertyTab(string tab, string[]? itemIds = null, string? setId = null, string? selection = null, bool all = false, bool geometryOnly = false,
        int? maxItems = null, CancellationToken ct = default)
    {
        var p = ModelTools.WithAll(ModelTools.ScopeArgs(itemIds, setId, selection), all, geometryOnly);
        p["tab"] = tab;
        if (maxItems.HasValue) p["max_items"] = maxItems.Value;
        return Call("remove_user_property_tab", p, ct);
    }

    [McpServerTool(Name = "nwd_set_hyperlinks", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Description("Set hyperlinks on items: mode = replace (default), add or clear. linksJson is [{\"name\":\"Ficha\",\"url\":\"https://…\"}] (not needed for clear). " + ModelTools.ScopeHelp)]
    public Task<string> SetHyperlinks(string mode = "replace", string? linksJson = null, string[]? itemIds = null, string? setId = null, string? selection = null,
        CancellationToken ct = default)
    {
        var p = ModelTools.ScopeArgs(itemIds, setId, selection);
        p["mode"] = mode;
        if (!string.IsNullOrEmpty(linksJson)) p["links"] = JArray.Parse(linksJson);
        return Call("set_hyperlinks", p, ct);
    }
}
