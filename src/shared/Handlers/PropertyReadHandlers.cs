#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>Which categories and properties exist in a scope, with item counts, data type and a sample value.</summary>
public sealed class DescribePropertiesHandler : DocumentCommand
{
    public override string Name => "describe_properties";

    private sealed class PropStat { public string Name = ""; public string Type = ""; public int Count; public JToken Sample = JValue.CreateNull(); }
    private sealed class CatStat { public string Name = ""; public int Count; public Dictionary<string, PropStat> Props = new Dictionary<string, PropStat>(StringComparer.Ordinal); }

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        int maxScan = Int(p, "max_scan", 5000, 1, 1_000_000);
        var items = Scope(p, doc, out var unresolved);
        var cats = new Dictionary<string, CatStat>(StringComparer.Ordinal);
        int scanned = 0;
        bool truncated = false;

        foreach (var item in items)
        {
            if (scanned >= maxScan || ctx.PastDeadline) { truncated = true; break; }
            scanned++;
            foreach (NW.PropertyCategory c in item.PropertyCategories)
            {
                var key = c.DisplayName ?? c.Name ?? "?";
                if (!cats.TryGetValue(key, out var cs)) cats[key] = cs = new CatStat { Name = c.Name ?? "" };
                cs.Count++;
                foreach (NW.DataProperty dp in c.Properties)
                {
                    var pk = dp.DisplayName ?? dp.Name ?? "?";
                    if (!cs.Props.TryGetValue(pk, out var ps)) cs.Props[pk] = ps = new PropStat { Name = dp.Name ?? "", Type = NwJson.DataType(dp.Value), Sample = NwJson.Value(dp.Value) };
                    ps.Count++;
                }
            }
        }

        var categories = new JArray(cats.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new JObject
        {
            ["category"] = kv.Key,
            ["internal_name"] = kv.Value.Name,
            ["item_count"] = kv.Value.Count,
            ["properties"] = new JArray(kv.Value.Props.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => new JObject
            {
                ["property"] = x.Key,
                ["internal_name"] = x.Value.Name,
                ["spec"] = kv.Key + "/" + x.Key,
                ["data_type"] = x.Value.Type,
                ["item_count"] = x.Value.Count,
                ["sample"] = x.Value.Sample,
            })),
        }));

        return new JObject
        {
            ["scanned_items"] = scanned,
            ["scope_items"] = items.Count,
            ["truncated"] = truncated,
            ["categories"] = categories,
            ["unresolved"] = unresolved,
        };
    }
}

/// <summary>Distinct values of one property with counts (data-quality / coverage check).</summary>
public sealed class PropertyValueHistogramHandler : DocumentCommand
{
    public override string Name => "property_value_histogram";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var spec = OneProperty(p);
        int top = Int(p, "top", 50, 1, 5000);
        int maxScan = Int(p, "max_scan", 500_000, 1, 10_000_000);
        var items = Scope(p, doc, out var unresolved);
        var hist = new ValueHistogram();
        bool truncated = false;
        foreach (var item in items)
        {
            if (hist.Scanned >= maxScan || ctx.PastDeadline) { truncated = true; break; }
            hist.Add(NwJson.Text(NwJson.FindProperty(item, spec)?.Value));
        }
        return new JObject
        {
            ["property"] = spec.Label,
            ["scanned_items"] = hist.Scanned,
            ["missing"] = hist.Missing,
            ["distinct"] = hist.Distinct,
            ["truncated"] = truncated,
            ["top"] = new JArray(hist.Top(top).Select(kv => new JObject { ["value"] = kv.Key, ["count"] = kv.Value })),
            ["unresolved"] = unresolved,
        };
    }
}

/// <summary>
/// Writes a CSV (UTF-8 BOM, formula-safe) of items × chosen properties to %LOCALAPPDATA%\Bimwright\nwd-mcp\exports
/// and returns its path, row count and a preview. Does not change the document.
/// </summary>
public sealed class ExportPropertiesTableHandler : DocumentCommand
{
    public override string Name => "export_properties_table";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var specs = Properties(p);
        int maxRows = Int(p, "max_rows", 200_000, 1, 2_000_000);
        bool withNames = Bool(p, "include_name", true);
        var ids = Ids(p, doc);
        var items = Scope(p, doc, out var unresolved);

        var columns = new List<string> { "id" };
        if (withNames) { columns.Add("name"); columns.Add("class"); }
        columns.AddRange(specs.Select(s => s.Label));

        var rows = new List<IReadOnlyList<string?>>();
        bool truncated = false;
        var found = new int[specs.Count];
        foreach (var item in items)
        {
            if (rows.Count >= maxRows || ctx.PastDeadline) { truncated = true; break; }
            var row = new List<string?> { ids.IdOf(item) };
            if (withNames) { row.Add(item.DisplayName); row.Add(item.ClassDisplayName); }
            for (int i = 0; i < specs.Count; i++)
            {
                var v = NwJson.Text(NwJson.FindProperty(item, specs[i])?.Value);
                if (v != null) found[i]++;
                row.Add(v);
            }
            rows.Add(row);
        }

        string path;
        try { path = TableExport.WriteCsvFile((string?)p["file_name"], "properties", columns, rows); }
        catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
        { throw new CommandFailure("EXPORT_FAILED", "could not write the CSV: " + ex.GetType().Name); }

        return new JObject
        {
            ["path"] = path,
            ["format"] = "csv",
            ["rows"] = rows.Count,
            ["columns"] = new JArray(columns),
            ["filled_per_property"] = new JObject(specs.Select((s, i) => new JProperty(s.Label, found[i]))),
            ["truncated"] = truncated,
            ["preview"] = new JArray(rows.Take(5).Select(r => new JArray(r.Cast<object?>().ToArray()))),
            ["unresolved"] = unresolved,
        };
    }
}

/// <summary>Hyperlinks attached to items in a scope; only items that have links are listed. Paged.</summary>
public sealed class GetHyperlinksHandler : DocumentCommand
{
    public override string Name => "get_hyperlinks";

    protected override JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc)
    {
        var page = Page(p);
        var ids = Ids(p, doc);
        var items = Scope(p, doc, out var unresolved);
        var withLinks = items.Select(i => (Item: i, Links: doc.Hyperlinks.GetHyperlinks(i))).Where(x => x.Links != null && x.Links.Count > 0);
        var (slice, info) = page.Apply(withLinks);
        var rows = new JArray(slice.Select(x =>
        {
            var o = NwJson.ItemSummary(x.Item, ids);
            o["links"] = new JArray(x.Links.Select(l => new JObject { ["name"] = l.Name, ["url"] = l.URL }));
            return o;
        }));
        return new JObject { ["items"] = rows, ["unresolved"] = unresolved, ["page"] = info };
    }
}
#endif
