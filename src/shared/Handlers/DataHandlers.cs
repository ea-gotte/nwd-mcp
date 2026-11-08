#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Bimwright.Nwd.Shared.Files;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using COM = Autodesk.Navisworks.Api.Interop.ComApi;
using NW = Autodesk.Navisworks.Api;
using Bridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>User-defined property tabs through the COM API (the .NET API cannot write properties).</summary>
public static class ComProps
{
    public static COM.InwGUIPropertyNode2 Node(NW.ModelItem item)
        => (COM.InwGUIPropertyNode2)Bridge.State.GetGUIPropertyNode(Bridge.ToInwOaPath(item), true);

    public static IEnumerable<COM.InwGUIAttribute2> Attributes(COM.InwGUIPropertyNode2 node)
    {
        foreach (COM.InwGUIAttribute2 a in node.GUIAttributes()) yield return a;
    }

    public static List<(bool UserDefined, string UserName, string InternalName)> Describe(COM.InwGUIPropertyNode2 node)
        => Attributes(node).Select(a => (a.UserDefined, a.ClassUserName ?? "", a.ClassName ?? "")).ToList();

    public static List<KeyValuePair<string, object?>> ReadTab(COM.InwGUIAttribute2 attr)
    {
        var list = new List<KeyValuePair<string, object?>>();
        foreach (COM.InwOaProperty p in attr.Properties()) list.Add(new KeyValuePair<string, object?>(p.UserName ?? p.name ?? "?", p.value));
        return list;
    }

    public static List<KeyValuePair<string, object?>> ExistingTab(COM.InwGUIPropertyNode2 node, string tab)
    {
        int index = UserTab.FindIndex(Describe(node), tab);
        if (index == 0) return new List<KeyValuePair<string, object?>>();
        return ReadTab(Attributes(node).Where(a => a.UserDefined).ElementAt(index - 1));
    }

    /// <summary>Writes the tab (creating it when missing) with exactly these properties.</summary>
    public static void WriteTab(COM.InwGUIPropertyNode2 node, string tab, IEnumerable<KeyValuePair<string, object?>> props)
    {
        var state = Bridge.State;
        var vec = (COM.InwOaPropertyVec)state.ObjectFactory(COM.nwEObjectType.eObjectType_nwOaPropertyVec, null, null);
        foreach (var kv in props)
        {
            var p = (COM.InwOaProperty)state.ObjectFactory(COM.nwEObjectType.eObjectType_nwOaProperty, null, null);
            p.name = UserTab.InternalName(kv.Key);
            p.UserName = kv.Key;
            p.value = kv.Value ?? "";
            vec.Properties().Add(p);
        }
        // Keep an existing tab's name: matching is case-insensitive, but SetUserDefined renames to whatever is passed.
        var attrs = Describe(node);
        int index = UserTab.FindIndex(attrs, tab);
        var name = index > 0 ? attrs.Where(a => a.UserDefined).ElementAt(index - 1).UserName : tab;
        node.SetUserDefined(index, name, UserTab.InternalName(name), vec);
    }

    public static bool RemoveTab(COM.InwGUIPropertyNode2 node, string tab)
    {
        int index = UserTab.FindIndex(Describe(node), tab);
        if (index == 0) return false;
        node.RemoveUserDefined(index);
        return true;
    }

    /// <summary>Runs edits as one Navisworks undo step.</summary>
    public static void Edit(string title, Action action)
    {
        var state = Bridge.State;
        state.BeginEdit(title);
        try { action(); }
        finally { state.EndEdit(); }
    }

    public static JToken ToJson(object? value)
    {
        if (value == null) return JValue.CreateNull();
        try { return JToken.FromObject(value); } catch (Exception) { return value.ToString(); }
    }

    /// <summary>JSON value → COM value: strings, numbers and booleans keep their type.</summary>
    public static object? FromJson(JToken t) => t.Type switch
    {
        JTokenType.Null => null,
        JTokenType.Integer => (object)(int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)t)),
        JTokenType.Float => (double)t,
        JTokenType.Boolean => (bool)t,
        _ => (string?)t ?? "",
    };
}

/// <summary>User-defined property tabs of items in a scope (only items that have some, unless include_empty). Paged.</summary>
public sealed class GetUserPropertiesHandler : DocumentCommand
{
    public override string Name => "get_user_properties";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var ids = Ids(p, doc);
        var tabFilter = ((string?)p["tab"])?.Trim();
        bool includeEmpty = Bool(p, "include_empty", false);
        var items = Scope(p, doc, out var unresolved);
        IEnumerable<JObject> Rows()
        {
            foreach (var item in items)
            {
                if (ctx.PastDeadline) yield break;
                var tabs = new JArray();
                foreach (var a in ComProps.Attributes(ComProps.Node(item)).Where(a => a.UserDefined))
                {
                    if (!string.IsNullOrEmpty(tabFilter) && !string.Equals(a.ClassUserName, tabFilter, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(a.ClassName, tabFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    var props = new JObject();
                    foreach (var kv in ComProps.ReadTab(a)) props[kv.Key] = ComProps.ToJson(kv.Value);
                    tabs.Add(new JObject { ["tab"] = a.ClassUserName, ["internal_name"] = a.ClassName, ["properties"] = props });
                }
                if (tabs.Count == 0 && !includeEmpty) continue;
                var o = NwJson.ItemSummary(item, ids);
                o["tabs"] = tabs;
                yield return o;
            }
        }
        var (slice, info) = Page(p).Apply(Rows(), countTotal: false);
        return new JObject { ["items"] = new JArray(slice), ["unresolved"] = unresolved, ["page"] = info };
    }
}

public abstract class DataWriteCommand : DocumentCommand
{
    public override bool IsReadOnly => false;

    protected static string Tab(JObject p)
    {
        var tab = ((string?)p["tab"])?.Trim();
        if (string.IsNullOrEmpty(tab)) throw CommandFailure.Invalid("tab is required (the property tab name shown in Navisworks)");
        return tab!;
    }

    protected static List<NW.ModelItem> Capped(List<NW.ModelItem> items, JObject p)
    {
        int max = Int(p, "max_items", 50_000, 1, 1_000_000);
        if (items.Count > max) throw CommandFailure.Invalid($"scope has {items.Count} items, above max_items={max}; narrow it or raise max_items");
        return items;
    }
}

/// <summary>Writes a user-defined tab on every item of a scope: merge (default) or replace its properties.</summary>
public sealed class SetUserPropertiesHandler : DataWriteCommand
{
    public override string Name => "set_user_properties";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tab = Tab(p);
        var props = p["properties"] as JObject ?? throw CommandFailure.Invalid("properties must be an object, e.g. {\"Disciplina\":\"MEC\"}");
        if (props.Count == 0) throw CommandFailure.Invalid("properties is empty");
        bool replace = ((string?)p["mode"] ?? "merge").Equals("replace", StringComparison.OrdinalIgnoreCase);
        var updates = props.Properties().Select(x => new KeyValuePair<string, object?>(x.Name.Trim(), ComProps.FromJson(x.Value))).ToList();
        var items = Capped(Scope(p, doc, out var unresolved), p);

        int written = 0;
        ComProps.Edit("Set user properties: " + tab, () =>
        {
            foreach (var item in items)
            {
                var node = ComProps.Node(item);
                ComProps.WriteTab(node, tab, UserTab.Merge(ComProps.ExistingTab(node, tab), updates, replace));
                written++;
            }
        });
        return new JObject
        {
            ["tab"] = tab, ["mode"] = replace ? "replace" : "merge", ["items_written"] = written,
            ["properties"] = new JArray(updates.Select(u => u.Key)), ["unresolved"] = unresolved,
            ["undo"] = "one Navisworks Undo step, or nwd_remove_user_property_tab",
        };
    }
}

/// <summary>Removes a user-defined tab from every item of a scope that has it.</summary>
public sealed class RemoveUserPropertyTabHandler : DataWriteCommand
{
    public override string Name => "remove_user_property_tab";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var tab = Tab(p);
        var items = Capped(Scope(p, doc, out var unresolved), p);
        int removed = 0;
        ComProps.Edit("Remove user property tab: " + tab, () =>
        {
            foreach (var item in items) if (ComProps.RemoveTab(ComProps.Node(item), tab)) removed++;
        });
        return new JObject { ["tab"] = tab, ["items_scanned"] = items.Count, ["items_removed"] = removed, ["unresolved"] = unresolved };
    }
}

/// <summary>
/// Writes user properties from a CSV: each row is matched to model items by a key column (item id, a property value,
/// or the item's name path) and its other columns become properties of one tab. dry_run (default) only reports.
/// </summary>
public sealed class BulkSetPropertiesFromTableHandler : DataWriteCommand
{
    public override string Name => "bulk_set_properties_from_table";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var check = LocalModelFile.Check((string?)p["path"]);
        if (!check.Ok) throw new CommandFailure(check.Code, check.Message);
        CsvTable table;
        try { table = CsvTable.Parse(File.ReadAllText(check.FullPath, Encoding.UTF8)); }
        catch (FormatException ex) { throw CommandFailure.Invalid("CSV: " + ex.Message); }
        catch (IOException ex) { throw new CommandFailure("READ_FAILED", "could not read the CSV: " + ex.GetType().Name); }

        var tab = Tab(p);
        var keyColumn = (string?)p["key_column"] ?? throw CommandFailure.Invalid("key_column is required");
        int keyIndex = table.ColumnIndex(keyColumn);
        if (keyIndex < 0) throw CommandFailure.Invalid($"key_column '{keyColumn}' is not in the CSV header: {string.Join(", ", table.Headers)}");
        var columns = p["columns"] is JArray cols ? cols.Select(c => (string?)c ?? "").ToList() : table.Headers.Where((h, i) => i != keyIndex).ToList();
        var colIdx = columns.Select(c => (Name: c, Index: table.ColumnIndex(c))).ToList();
        var missing = colIdx.Where(c => c.Index < 0).Select(c => c.Name).ToList();
        if (missing.Count > 0) throw CommandFailure.Invalid("columns not in the CSV: " + string.Join(", ", missing));
        bool dryRun = Bool(p, "dry_run", true), writeEmpty = Bool(p, "write_empty", false), ignoreCase = !Bool(p, "case_sensitive", false);
        int sample = Int(p, "sample", 20, 0, 1000);

        var lookup = BuildLookup(p, doc, ignoreCase, out var matchDescription);
        var updatesByItem = new Dictionary<NW.ModelItem, List<KeyValuePair<string, object?>>>();
        var unmatched = new List<string>();
        int rowsMatched = 0, ambiguousRows = 0;
        foreach (var row in table.Rows)
        {
            var key = row[keyIndex].Trim();
            if (key.Length == 0) continue;
            var targets = lookup(key);
            if (targets.Count == 0) { unmatched.Add(key); continue; }
            rowsMatched++;
            if (targets.Count > 1) ambiguousRows++;
            var values = colIdx.Where(c => writeEmpty || row[c.Index].Length > 0)
                               .Select(c => new KeyValuePair<string, object?>(c.Name.Trim(), row[c.Index])).ToList();
            foreach (var item in targets)
            {
                if (!updatesByItem.TryGetValue(item, out var list)) updatesByItem[item] = list = new List<KeyValuePair<string, object?>>();
                list.AddRange(values);
            }
        }

        var ids = Ids(p, doc);
        var report = new JObject
        {
            ["dry_run"] = dryRun, ["path"] = check.FullPath, ["delimiter"] = table.Delimiter.ToString(), ["match"] = matchDescription,
            ["tab"] = tab, ["columns"] = new JArray(columns),
            ["rows"] = table.Rows.Count, ["rows_matched"] = rowsMatched, ["rows_matching_several_items"] = ambiguousRows,
            ["items_to_update"] = updatesByItem.Count,
            ["unmatched_keys"] = new JObject { ["count"] = unmatched.Count, ["sample"] = new JArray(unmatched.Take(sample)) },
            ["plan_sample"] = new JArray(updatesByItem.Take(sample).Select(kv =>
            {
                var o = NwJson.ItemSummary(kv.Key, ids);
                o["values"] = new JObject(kv.Value.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(g => new JProperty(g.Key, g.Last().Value)));
                return o;
            })),
        };
        if (dryRun) return report;

        Capped(updatesByItem.Keys.ToList(), p);
        bool replace = ((string?)p["mode"] ?? "merge").Equals("replace", StringComparison.OrdinalIgnoreCase);
        int written = 0;
        ComProps.Edit("Set user properties from " + Path.GetFileName(check.FullPath), () =>
        {
            foreach (var kv in updatesByItem)
            {
                var node = ComProps.Node(kv.Key);
                ComProps.WriteTab(node, tab, UserTab.Merge(ComProps.ExistingTab(node, tab), kv.Value, replace));
                written++;
            }
        });
        report["items_written"] = written;
        report["undo"] = "one Navisworks Undo step, or nwd_remove_user_property_tab";
        return report;
    }

    /// <summary>key → items, according to match = id | property:&lt;Category/Property&gt; | name_path.</summary>
    private static Func<string, List<NW.ModelItem>> BuildLookup(JObject p, NW.Document doc, bool ignoreCase, out string description)
    {
        var none = new List<NW.ModelItem>();
        var cmp = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var match = ((string?)p["match"] ?? "id").Trim();
        var index = new Dictionary<string, List<NW.ModelItem>>(cmp);
        void Add(string? key, NW.ModelItem item)
        {
            var k = key?.Trim();
            if (string.IsNullOrEmpty(k)) return;
            if (!index.TryGetValue(k!, out var list)) index[k!] = list = new List<NW.ModelItem>(1);
            if (!list.Contains(item)) list.Add(item);
        }

        bool hasScope = p["item_ids"] != null || p["set_id"] != null || p["selection"] != null || p["all"] != null;
        List<NW.ModelItem> Candidates() => hasScope ? Scope(p, doc, out _) : NwJson.AllItems(doc).ToList();

        List<NW.ModelItem> FromIndex(string key) => index.TryGetValue(key, out var list) ? list : none;

        if (match.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            // Resolve keys lazily as item ids, so the whole model is never indexed.
            description = "item id (any form)";
            var resolver = new ItemRefResolver(doc);
            return key =>
            {
                if (!index.TryGetValue(key, out var list)) index[key] = list = resolver.Resolve(new[] { key }).Items;
                return list;
            };
        }
        if (match.StartsWith("property:", StringComparison.OrdinalIgnoreCase))
        {
            if (!PropertySpec.TryParse(match.Substring("property:".Length), out var spec, out var e)) throw CommandFailure.Invalid(e);
            description = "property " + spec!.Label;
            foreach (var item in Candidates()) Add(NwJson.Text(NwJson.FindProperty(item, spec)?.Value), item);
            return FromIndex;
        }
        if (match.Equals("name_path", StringComparison.OrdinalIgnoreCase))
        {
            var sep = (string?)p["path_separator"] ?? ">";
            bool skipRoot = Bool(p, "path_skip_root", false);
            description = $"name path joined by '{sep}'" + (skipRoot ? " without the file node" : "");
            foreach (var item in Candidates())
            {
                var names = item.AncestorsAndSelf.Cast<NW.ModelItem>().Reverse().Select(a => a.DisplayName ?? "").ToList();
                if (skipRoot && names.Count > 0) names.RemoveAt(0);
                Add(string.Join(sep, names), item);
                Add(string.Join(sep, names.Select(n => n.Trim())), item);
            }
            return FromIndex;
        }
        throw CommandFailure.Invalid("match must be id, property:<Category/Property> or name_path");
    }
}

/// <summary>Replaces, adds or clears hyperlinks on items.</summary>
public sealed class SetHyperlinksHandler : DataWriteCommand
{
    public override string Name => "set_hyperlinks";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var mode = ((string?)p["mode"] ?? "replace").ToLowerInvariant();
        if (mode != "replace" && mode != "add" && mode != "clear") throw CommandFailure.Invalid("mode must be replace, add or clear");
        var links = new List<NW.Hyperlink>();
        if (mode != "clear")
        {
            var arr = p["links"] as JArray ?? throw CommandFailure.Invalid("links must be [{\"name\":…, \"url\":…}]");
            foreach (var l in arr.OfType<JObject>())
            {
                var url = ((string?)l["url"])?.Trim();
                if (string.IsNullOrEmpty(url)) throw CommandFailure.Invalid("every link needs a url");
                links.Add(new NW.Hyperlink(((string?)l["name"])?.Trim() ?? url!, url!));
            }
            if (links.Count == 0) throw CommandFailure.Invalid("links is empty");
        }
        var items = Capped(Scope(p, doc, out var unresolved), p);
        foreach (var item in items)
        {
            if (mode == "clear") { doc.Hyperlinks.DeleteHyperlinks(item); continue; }
            var coll = mode == "add" ? new NW.HyperlinkCollection(doc.Hyperlinks.GetHyperlinks(item) ?? new NW.HyperlinkCollection()) : new NW.HyperlinkCollection();
            foreach (var l in links) coll.Add(l);
            doc.Hyperlinks.SetHyperlinks(item, coll);
        }
        return new JObject { ["mode"] = mode, ["items"] = items.Count, ["links"] = links.Count, ["unresolved"] = unresolved };
    }
}

/// <summary>Tables and views of the document database (Quantification and other embedded data).</summary>
public sealed class DbListTablesHandler : DocumentCommand
{
    public override string Name => "db_list_tables";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var rows = DbQueryHandler.Query(doc, "SELECT name, type FROM sqlite_master WHERE type IN ('table','view') ORDER BY name", 5000, out _);
        return new JObject { ["tables"] = new JArray(rows.Select(r => new JObject { ["name"] = r[0], ["type"] = r[1] })) };
    }
}

/// <summary>Runs one read-only SELECT against the document database.</summary>
public sealed class DbQueryHandler : DocumentCommand
{
    public override string Name => "db_query";
    protected override bool RequiresModels => false;

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var sql = (string?)p["sql"];
        if (!SqlGuard.IsReadOnly(sql, out var error)) throw CommandFailure.Invalid(error);
        int maxRows = Int(p, "max_rows", 500, 1, 5000);
        var rows = Query(doc, sql!, maxRows + 1, out var columns);
        bool truncated = rows.Count > maxRows;
        return new JObject
        {
            ["columns"] = new JArray(columns),
            ["rows"] = new JArray(rows.Take(maxRows).Select(r => new JArray(r))),
            ["row_count"] = Math.Min(rows.Count, maxRows),
            ["truncated"] = truncated,
        };
    }

    internal static List<JToken[]> Query(NW.Document doc, string sql, int limit, out List<string> columns)
    {
        columns = new List<string>();
        var rows = new List<JToken[]>();
        var conn = doc.Database.ToNavisworksConnection();
        if (conn.State != ConnectionState.Open) conn.Open();
        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                using (var reader = cmd.ExecuteReader())
                {
                    for (int i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
                    while (rows.Count < limit && reader.Read())
                    {
                        var row = new JToken[reader.FieldCount];
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var v = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            row[i] = v is byte[] bytes ? $"<blob {bytes.Length} bytes>" : ComProps.ToJson(v);
                        }
                        rows.Add(row);
                    }
                }
            }
        }
        catch (Exception ex) when (!(ex is CommandFailure))
        {
            throw new CommandFailure("SQL_ERROR", ex.Message);
        }
        return rows;
    }
}
#endif
