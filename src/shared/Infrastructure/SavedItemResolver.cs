using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Nwd.Shared.Infrastructure;

/// <summary>Round-trip saved-item identifiers without confusing literal slashes with folders.</summary>
public static class SavedItemResolver
{
    public static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");
    public static string Id(Guid guid, string? escapedPath = null)
        => guid != Guid.Empty ? "id:" + guid.ToString("D") :
            "path:" + (escapedPath ?? throw new ArgumentException("An escaped path is required for saved items without a GUID."));

    public static T? Resolve<T>(IEnumerable<T> roots, string query, Func<T, string> name,
        Func<T, IEnumerable<T>> children, Func<T, Guid> guid) where T : class
    {
        var matches = new List<T>();
        void Walk(IEnumerable<T> items, string path, string legacy)
        {
            foreach (var item in items)
            {
                var label = name(item);
                var encoded = path + "/" + Escape(label);
                var raw = legacy.Length == 0 ? label : legacy + "/" + label;
                bool match;
                if (query.StartsWith("id:", StringComparison.Ordinal))
                    match = Guid.TryParse(query.Substring(3), out var id) && id != Guid.Empty && guid(item) == id;
                else if (query.StartsWith("path:", StringComparison.Ordinal))
                    match = string.Equals("path:" + encoded, query, StringComparison.OrdinalIgnoreCase);
                else
                    match = string.Equals(label, query, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, query, StringComparison.OrdinalIgnoreCase);
                if (match && !matches.Contains(item)) matches.Add(item);
                Walk(children(item), encoded, raw);
            }
        }
        Walk(roots, "", "");
        if (matches.Count > 1)
            throw new InvalidOperationException("Saved item identifier is ambiguous; use the id or escaped path returned by list.");
        return matches.SingleOrDefault();
    }
}
