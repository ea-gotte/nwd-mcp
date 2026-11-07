using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Nwd.Server;

public static class ToolsetFilter
{
    public static readonly string[] KnownToolsets =
    {
        "meta", "files", "files_write", "query", "selection", "selection_write", "sets",
        "view", "view_write", "visibility", "code", "toolbaker", "toolbaker_write",
        // Full-API extension toolsets (see PLAN_NWD_MCP.md). Read halves first, write halves second.
        "model", "review", "review_write", "sets_write",
        "clash", "clash_write", "timeliner", "timeliner_write", "takeoff", "takeoff_write",
        "data_write", "export"
    };

    // Empty --toolsets means every known toolset. code drops only when send-code is turned off.
    public static readonly string[] DefaultOn = KnownToolsets;

    public static readonly string[] WriteCapable =
    {
        "files_write", "selection_write", "view_write", "visibility", "code", "toolbaker_write",
        "review_write", "sets_write", "clash_write", "timeliner_write", "takeoff_write", "data_write", "export"
    };

    private static readonly string[] Core = { "meta", "files", "query", "model", "selection", "sets", "view" };

    private static readonly string[] Review = Core.Concat(new[]
        { "review", "review_write", "selection_write", "view_write", "visibility", "sets_write" }).ToArray();

    /// <summary>
    /// Workflow profiles accepted by --toolsets alongside toolset names. A profile expands to the read
    /// core plus its domain toolsets; combine profiles and toolsets freely (e.g. "4d,code").
    /// --read-only still strips every write-capable toolset after expansion.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Profiles =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = Core,
            ["visual"] = Review,
            ["coordination"] = Review.Concat(new[] { "clash", "clash_write" }).ToArray(),
            ["4d"] = Review.Concat(new[] { "timeliner", "timeliner_write", "data_write" }).ToArray(),
            ["qto"] = Core.Concat(new[] { "takeoff", "takeoff_write" }).ToArray(),
            ["data"] = Core.Concat(new[] { "data_write", "export", "files_write" }).ToArray(),
        };

    public static HashSet<string> Resolve(NwdMcpConfig config)
    {
        var requested = config.Toolsets;
        HashSet<string> set;
        if (requested.Count == 0)
            set = new HashSet<string>(DefaultOn, StringComparer.OrdinalIgnoreCase);
        else if (requested.Any(t => string.Equals(t, "all", StringComparison.OrdinalIgnoreCase)))
            set = new HashSet<string>(KnownToolsets, StringComparer.OrdinalIgnoreCase);
        else
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in requested)
            {
                if (Profiles.TryGetValue(t, out var expanded)) set.UnionWith(expanded);
                else set.Add(t);
            }
        }

        set.IntersectWith(KnownToolsets);          // silently drop unknown names

        if (!config.EnableSendCode) set.Remove("code");
        if (!config.EnableToolBaker) { set.Remove("toolbaker"); set.Remove("toolbaker_write"); }
        if (config.ReadOnly) foreach (var w in WriteCapable) set.Remove(w);

        return set;
    }
}
