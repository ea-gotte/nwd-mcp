using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bimwright.Nwd.Shared.Infrastructure;

public enum TaskRefKind { IndexPath, DisplayId, SynchronizationId, Name }

/// <summary>
/// TimeLiner task reference: "task:2/0/5" or "2/0/5" (child indices from the task root), "display:&lt;DisplayId&gt;",
/// "sync:&lt;SynchronizationId&gt;", "name:&lt;name&gt;" or a bare name.
/// </summary>
public sealed class TaskRef
{
    public TaskRefKind Kind { get; }
    public IReadOnlyList<int> Path { get; }
    public string Key { get; }

    private TaskRef(TaskRefKind kind, IReadOnlyList<int>? path, string key)
    {
        Kind = kind; Path = path ?? Array.Empty<int>(); Key = key;
    }

    public static string FormatPath(IEnumerable<int> path) => "task:" + string.Join("/", path.Select(i => i.ToString(CultureInfo.InvariantCulture)));

    /// <summary>True when candidate is ancestor itself or lies under it (index-path prefix).</summary>
    public static bool IsSelfOrDescendant(IReadOnlyList<int> ancestor, IReadOnlyList<int> candidate)
        => candidate.Count >= ancestor.Count && ancestor.Select((v, i) => candidate[i] == v).All(x => x);

    public static bool TryParse(string? raw, out TaskRef? result, out string error)
    {
        result = null; error = "";
        var s = (raw ?? "").Trim();
        if (s.Length == 0) { error = "task id is empty"; return false; }

        if (TryPrefix(s, "display:", out var display)) return Keyed(TaskRefKind.DisplayId, display, out result, out error);
        if (TryPrefix(s, "sync:", out var sync)) return Keyed(TaskRefKind.SynchronizationId, sync, out result, out error);
        if (TryPrefix(s, "name:", out var name)) return Keyed(TaskRefKind.Name, name, out result, out error);

        bool explicitPath = TryPrefix(s, "task:", out var pathText);
        var text = explicitPath ? pathText : s;
        var parts = text.Split('/');
        var path = new List<int>();
        bool numeric = true;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i)) { numeric = false; break; }
            path.Add(i);
        }
        if (numeric && path.Count > 0) { result = new TaskRef(TaskRefKind.IndexPath, path, ""); return true; }
        if (explicitPath) { error = "task: path must be child indices like task:2/0/5"; return false; }
        result = new TaskRef(TaskRefKind.Name, null, s);
        return true;
    }

    private static bool Keyed(TaskRefKind kind, string key, out TaskRef? result, out string error)
    {
        result = null; error = "";
        if (key.Trim().Length == 0) { error = "task reference needs a value after the prefix"; return false; }
        result = new TaskRef(kind, null, key.Trim());
        return true;
    }

    private static bool TryPrefix(string s, string prefix, out string rest)
    {
        if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { rest = s.Substring(prefix.Length); return true; }
        rest = ""; return false;
    }
}

/// <summary>
/// Resolves a requested name against the names that exist (e.g. TimeLiner task types, which are localised:
/// "Construcción" on a Spanish install). Case, accents and surrounding spaces are ignored; empty resolves to empty.
/// </summary>
public static class NameMatch
{
    public static bool TryResolve(string? requested, IEnumerable<string> available, out string resolved, out string error)
    {
        resolved = ""; error = "";
        var want = Fold(requested);
        if (want.Length == 0) return true;
        var names = available.Where(n => !string.IsNullOrEmpty(n)).ToList();
        var hit = names.FirstOrDefault(n => Fold(n) == want);
        if (hit != null) { resolved = hit; return true; }
        error = $"'{requested}' does not exist; valid values: {string.Join(", ", names)}";
        return false;
    }

    public static string Fold(string? s)
    {
        var norm = (s ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(norm.Length);
        foreach (var ch in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}

/// <summary>Task dates: ISO (yyyy-MM-dd[THH:mm[:ss]]) or day-first dd/MM/yyyy; "none"/"null"/"" clears the date.</summary>
public static class TaskDate
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss",
    };

    public static bool TryParse(string? raw, out DateTime? value, out bool clear, out string error)
    {
        value = null; clear = false; error = "";
        var s = (raw ?? "").Trim();
        if (s.Length == 0 || s.Equals("none", StringComparison.OrdinalIgnoreCase) || s.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            clear = true;
            return true;
        }
        if (DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            value = d;
            return true;
        }
        error = $"date '{raw}' must be yyyy-MM-dd, yyyy-MM-ddTHH:mm or dd/MM/yyyy (or none to clear)";
        return false;
    }

    public static string Format(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Checks start &lt;= end using the new value where one is given (hasNew; null means cleared) and the current value
    /// otherwise. Run it before touching the task: Navisworks silently moves the other date instead of failing.
    /// </summary>
    public static bool CheckRange(DateTime? newStart, bool hasNewStart, DateTime? newEnd, bool hasNewEnd,
        DateTime? currentStart, DateTime? currentEnd, out string error)
    {
        error = "";
        var start = hasNewStart ? newStart : currentStart;
        var end = hasNewEnd ? newEnd : currentEnd;
        if (start.HasValue && end.HasValue && end.Value < start.Value)
        {
            error = $"end {Format(end.Value)} is before start {Format(start.Value)}";
            return false;
        }
        return true;
    }
}

/// <summary>
/// Matches model items to TimeLiner tasks by a code: each item's property value (optionally several codes split by a
/// separator) against each task's key field (DisplayId, SynchronizationId, name or a user field). Codes are trimmed;
/// case-insensitive by default. Duplicate task keys are reported and the first task wins.
/// </summary>
public sealed class TaskCodeMatcher
{
    private readonly StringComparer _cmp;
    private readonly string? _separator;
    private readonly List<(int Task, string? Key)> _tasks = new List<(int, string?)>();
    private readonly List<(int Item, string? Code)> _items = new List<(int, string?)>();

    public TaskCodeMatcher(bool ignoreCase, string? separator)
    {
        _cmp = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _separator = string.IsNullOrEmpty(separator) ? null : separator;
    }

    public void AddTask(int task, string? key) => _tasks.Add((task, key));
    public void AddItem(int item, string? code) => _items.Add((item, code));

    public sealed class Result
    {
        public Dictionary<int, List<int>> ItemsByTask { get; } = new Dictionary<int, List<int>>();
        /// <summary>Codes present on items with no task, with how many items carry each.</summary>
        public SortedDictionary<string, int> UnmatchedCodes { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Tasks that have a key but received no items (tasks without a key are only counted).</summary>
        public List<int> TasksWithoutItems { get; } = new List<int>();
        public int TasksWithoutKey { get; set; }
        public List<string> DuplicateTaskKeys { get; } = new List<string>();
        public int ItemsMatched { get; set; }
        public int ItemsWithoutCode { get; set; }
    }

    public Result Match()
    {
        var r = new Result();
        var byKey = new Dictionary<string, int>(_cmp);
        var canonical = new Dictionary<string, string>(_cmp);
        var dupes = new HashSet<string>(_cmp);
        var keyed = new HashSet<int>();
        foreach (var (task, key) in _tasks)
        {
            var k = key?.Trim();
            if (string.IsNullOrEmpty(k)) { r.TasksWithoutKey++; continue; }
            keyed.Add(task);
            if (byKey.ContainsKey(k!)) { if (dupes.Add(k!)) r.DuplicateTaskKeys.Add(canonical[k!]); continue; }
            byKey[k!] = task;
            canonical[k!] = k!;
        }

        foreach (var (item, code) in _items)
        {
            var codes = Split(code);
            if (codes.Count == 0) { r.ItemsWithoutCode++; continue; }
            bool any = false;
            foreach (var c in codes)
            {
                if (byKey.TryGetValue(c, out var task))
                {
                    if (!r.ItemsByTask.TryGetValue(task, out var list)) r.ItemsByTask[task] = list = new List<int>();
                    if (list.Count == 0 || list[list.Count - 1] != item) list.Add(item);
                    any = true;
                }
                else
                {
                    r.UnmatchedCodes.TryGetValue(c, out var n);
                    r.UnmatchedCodes[c] = n + 1;
                }
            }
            if (any) r.ItemsMatched++;
        }

        foreach (var (task, _) in _tasks)
            if (keyed.Contains(task) && !r.ItemsByTask.ContainsKey(task) && !r.TasksWithoutItems.Contains(task)) r.TasksWithoutItems.Add(task);
        return r;
    }

    private List<string> Split(string? code)
    {
        if (code == null) return new List<string>();
        var parts = _separator == null ? new[] { code } : code.Split(new[] { _separator }, StringSplitOptions.None);
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(_cmp).ToList();
    }
}
