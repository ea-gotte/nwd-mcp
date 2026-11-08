using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Bimwright.Nwd.Shared.Infrastructure;

/// <summary>
/// RFC 4180 CSV reader with delimiter detection (comma, semicolon as saved by Spanish Excel, or tab), quoted cells
/// with embedded delimiters/quotes/newlines, a leading BOM and blank trailing lines.
/// </summary>
public sealed class CsvTable
{
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
    public char Delimiter { get; }

    private CsvTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, char delimiter)
    {
        Headers = headers; Rows = rows; Delimiter = delimiter;
    }

    public int ColumnIndex(string name)
    {
        var want = (name ?? "").Trim();
        for (int i = 0; i < Headers.Count; i++)
            if (string.Equals(Headers[i].Trim(), want, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static CsvTable Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        var firstLine = text.Split('\n')[0];
        char delimiter = new[] { ';', '\t', ',' }.OrderByDescending(d => firstLine.Count(c => c == d)).ThenBy(d => d == ',' ? 0 : 1).First();
        if (firstLine.IndexOf(delimiter) < 0) delimiter = ',';

        var records = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false, cellStarted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(c);
                continue;
            }
            if (c == '"' && !cellStarted) { inQuotes = true; cellStarted = true; continue; }
            if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); cellStarted = false; continue; }
            if (c == '\r') continue;
            if (c == '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); cellStarted = false;
                records.Add(row); row = new List<string>();
                continue;
            }
            cell.Append(c);
            cellStarted = true;
        }
        if (inQuotes) throw new FormatException("CSV ends inside a quoted cell");
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); records.Add(row); }

        records = records.Where(r => !(r.Count == 1 && r[0].Trim().Length == 0)).ToList();
        if (records.Count == 0) throw new FormatException("CSV has no header row");
        var headers = records[0].Select(h => h.Trim()).ToList();
        if (headers.Any(h => h.Length == 0)) throw new FormatException("CSV header has an empty column name");
        var dupe = headers.GroupBy(h => h, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dupe != null) throw new FormatException($"CSV header repeats column '{dupe.Key}'");

        var rows = records.Skip(1).Select(r =>
        {
            var padded = new List<string>(r);
            while (padded.Count < headers.Count) padded.Add("");
            return (IReadOnlyList<string>)padded;
        }).ToList();
        return new CsvTable(headers, rows, delimiter);
    }
}

/// <summary>User-defined property tab helpers (COM API SetUserDefined/RemoveUserDefined semantics).</summary>
public static class UserTab
{
    /// <summary>
    /// 1-based index of a user-defined tab among user-defined attributes only (what SetUserDefined/RemoveUserDefined
    /// expect), matched by display or internal name ignoring case; 0 when absent (SetUserDefined(0) creates a tab).
    /// </summary>
    public static int FindIndex(IEnumerable<(bool UserDefined, string UserName, string InternalName)> attributes, string tab)
    {
        int index = 0;
        foreach (var (userDefined, userName, internalName) in attributes)
        {
            if (!userDefined) continue;
            index++;
            if (string.Equals(userName?.Trim(), tab?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(internalName?.Trim(), tab?.Trim(), StringComparison.OrdinalIgnoreCase)) return index;
        }
        return 0;
    }

    /// <summary>
    /// Merges updates into existing properties keeping order (case-insensitive names). A null value removes the
    /// property. replace=true keeps only the updates.
    /// </summary>
    public static List<KeyValuePair<string, object?>> Merge(IEnumerable<KeyValuePair<string, object?>> existing,
        IEnumerable<KeyValuePair<string, object?>> updates, bool replace)
    {
        var result = replace ? new List<KeyValuePair<string, object?>>() : existing.ToList();
        foreach (var u in updates)
        {
            int i = result.FindIndex(kv => string.Equals(kv.Key, u.Key, StringComparison.OrdinalIgnoreCase));
            if (u.Value == null) { if (i >= 0) result.RemoveAt(i); continue; }
            if (i >= 0) result[i] = new KeyValuePair<string, object?>(result[i].Key, u.Value);
            else result.Add(u);
        }
        return result;
    }

    /// <summary>Internal (class) name for a tab: ASCII letters, digits and underscores, not starting with a digit.</summary>
    public static string InternalName(string tab)
    {
        var sb = new StringBuilder();
        foreach (var ch in (tab ?? "").Trim().Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch < 128 && char.IsLetterOrDigit(ch) ? ch : '_');
        }
        var s = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        if (s.Length == 0) s = "Tab";
        if (char.IsDigit(s[0])) s = "_" + s;
        return s;
    }
}

/// <summary>Accepts one read-only SELECT/WITH statement for the document database; rejects writes and batches.</summary>
public static class SqlGuard
{
    private static readonly Regex Forbidden = new Regex(
        @"\b(insert|update|delete|drop|alter|create|replace|attach|detach|vacuum|reindex|pragma|begin|commit|rollback|savepoint|release)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsReadOnly(string? sql, out string error)
    {
        error = "";
        var s = (sql ?? "").Trim();
        while (s.EndsWith(";")) s = s.Substring(0, s.Length - 1).TrimEnd();
        if (s.Length == 0) { error = "sql is empty"; return false; }
        var code = Regex.Replace(s, "'([^']|'')*'", "''"); // ignore string literals
        if (code.Contains(";")) { error = "only one statement is allowed"; return false; }
        if (!Regex.IsMatch(code, @"^\s*(select|with)\b", RegexOptions.IgnoreCase)) { error = "only SELECT or WITH … SELECT queries are allowed"; return false; }
        var bad = Forbidden.Match(code);
        if (bad.Success) { error = $"'{bad.Value}' is not allowed: the database is read-only through this tool"; return false; }
        return true;
    }
}
