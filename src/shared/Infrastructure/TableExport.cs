using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Bimwright.Nwd.Shared.Infrastructure;

/// <summary>"Category/Property" reference. The category ends at the first '/', so property names may contain '/'.</summary>
public sealed class PropertySpec
{
    public string Category { get; }
    public string Property { get; }
    public string Label => Category + "/" + Property;

    public PropertySpec(string category, string property) { Category = category; Property = property; }

    public static bool TryParse(string? raw, out PropertySpec? spec, out string error)
    {
        spec = null; error = "";
        var s = (raw ?? "").Trim();
        int slash = s.IndexOf('/');
        if (slash <= 0 || slash == s.Length - 1)
        {
            error = $"property '{raw}' must be \"Category/Property\" (display or internal names)";
            return false;
        }
        spec = new PropertySpec(s.Substring(0, slash).Trim(), s.Substring(slash + 1).Trim());
        return true;
    }

    public static bool TryParseAll(IEnumerable<string?> raws, out List<PropertySpec> specs, out string error)
    {
        specs = new List<PropertySpec>(); error = "";
        foreach (var r in raws)
        {
            if (!TryParse(r, out var s, out error)) return false;
            specs.Add(s!);
        }
        if (specs.Count == 0) { error = "give at least one property as \"Category/Property\""; return false; }
        return true;
    }
}

/// <summary>Counts values of one property: distinct values, how many items lack it, top values by count.</summary>
public sealed class ValueHistogram
{
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
    public int Scanned { get; private set; }
    public int Missing { get; private set; }
    public int Distinct => _counts.Count;

    public void Add(string? value)
    {
        Scanned++;
        if (value == null) { Missing++; return; }
        _counts.TryGetValue(value, out var c);
        _counts[value] = c + 1;
    }

    /// <summary>Most frequent first; ties by ordinal value so output is stable.</summary>
    public IEnumerable<KeyValuePair<string, int>> Top(int n)
        => _counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(Math.Max(0, n));
}

/// <summary>
/// RFC 4180 CSV written as UTF-8 with BOM (Excel opens it with accents intact) into the per-user exports folder.
/// File names are sanitised: callers pick a name, never a directory.
/// </summary>
public static class TableExport
{
    public static string ExportsDirectory =>
        Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bimwright", "nwd-mcp", "exports"));

    public static string EscapeCsv(string? value)
    {
        if (value == null) return "";
        bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value.StartsWith(" ") || value.EndsWith(" ");
        // Neutralise spreadsheet formula injection in exported model data.
        if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0 && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
            quote = true;
        }
        return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static void WriteCsv(TextWriter writer, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows)
    {
        writer.Write(string.Join(",", columns.Select(EscapeCsv)));
        writer.Write("\r\n");
        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) writer.Write(',');
                writer.Write(EscapeCsv(i < row.Count ? row[i] : null));
            }
            writer.Write("\r\n");
        }
    }

    /// <summary>Turns a requested name into "&lt;safe&gt;.csv"; empty means "&lt;prefix&gt;-yyyyMMdd-HHmmss".</summary>
    public static string SafeFileName(string? requested, string prefix, string extension, DateTime nowUtc)
    {
        var name = Path.GetFileNameWithoutExtension((requested ?? "").Trim());
        var sb = new StringBuilder();
        foreach (var ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.' ? ch : '_');
        var clean = sb.ToString().Trim('.', '_');
        if (clean.Length == 0) clean = prefix + "-" + nowUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        if (clean.Length > 100) clean = clean.Substring(0, 100);
        return clean + extension;
    }

    /// <summary>Writes the CSV under <paramref name="directory"/> (exports folder by default) and returns the full path.</summary>
    public static string WriteCsvFile(string? requestedName, string prefix, IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string?>> rows, string? directory = null)
    {
        var dir = directory ?? ExportsDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, SafeFileName(requestedName, prefix, ".csv", DateTime.UtcNow));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            WriteCsv(writer, columns, rows);
        return path;
    }
}
