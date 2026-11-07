using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bimwright.Nwd.Shared.Infrastructure;

/// <summary>How an item reference identifies a model item.</summary>
public enum ItemRefKind
{
    /// <summary>"0:6:3" — model index then child index per level. Cheap; goes stale when the document changes.</summary>
    Path,
    /// <summary>"pid:&lt;modelIndex&gt;:&lt;pathId&gt;" — Navisworks ModelItemPathId. Survives sessions of the same model.</summary>
    PathId,
    /// <summary>"nid:&lt;modelIndex&gt;:&lt;id&gt;" — DocumentModels.FindIdForModelItem numeric id (may match several instances).</summary>
    NumericId,
    /// <summary>"guid:&lt;guid&gt;" — ModelItem.InstanceGuid (shared by instances of the same object).</summary>
    InstanceGuid,
}

/// <summary>Which id form a command emits for items it returns (parameter <c>id_mode</c>).</summary>
public enum ItemIdMode { Path, PathId, NumericId, InstanceGuid }

/// <summary>
/// API-free parsing and formatting of item references. Every handler that accepts item ids accepts all
/// four forms, so ids from any tool round-trip. Unprefixed strings stay the legacy path form.
/// </summary>
public sealed class ItemRef
{
    public const string PathIdPrefix = "pid:";
    public const string NumericIdPrefix = "nid:";
    public const string GuidPrefix = "guid:";

    public ItemRefKind Kind { get; }
    public string Raw { get; }
    /// <summary>Model index for Path/PathId/NumericId; -1 for InstanceGuid.</summary>
    public int ModelIndex { get; }
    /// <summary>Child indices below the model root (Path only; empty for a model root).</summary>
    public IReadOnlyList<int> ChildPath { get; }
    /// <summary>Navisworks path identifier (PathId only).</summary>
    public string PathId { get; }
    /// <summary>Numeric id (NumericId only).</summary>
    public ulong NumericId { get; }
    /// <summary>Instance guid (InstanceGuid only).</summary>
    public Guid Guid { get; }

    private ItemRef(ItemRefKind kind, string raw, int modelIndex, IReadOnlyList<int>? childPath = null,
        string pathId = "", ulong numericId = 0, Guid guid = default)
    {
        Kind = kind; Raw = raw; ModelIndex = modelIndex; ChildPath = childPath ?? Array.Empty<int>();
        PathId = pathId; NumericId = numericId; Guid = guid;
    }

    public static bool TryParse(string? raw, out ItemRef? result, out string error)
    {
        result = null; error = "";
        if (string.IsNullOrWhiteSpace(raw)) { error = "empty item id"; return false; }
        var s = raw!.Trim();

        if (s.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(s.Substring(GuidPrefix.Length), out var g) || g == Guid.Empty)
            { error = "guid: id needs a non-empty GUID"; return false; }
            result = new ItemRef(ItemRefKind.InstanceGuid, s, -1, guid: g);
            return true;
        }

        if (s.StartsWith(PathIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // pid:<modelIndex>:<pathId>. The path id itself may contain ':' so split once.
            var rest = s.Substring(PathIdPrefix.Length);
            int colon = rest.IndexOf(':');
            if (colon <= 0 || !TryIndex(rest.Substring(0, colon), out int mi) || colon == rest.Length - 1)
            { error = "pid: id must be pid:<modelIndex>:<pathId>"; return false; }
            result = new ItemRef(ItemRefKind.PathId, s, mi, pathId: rest.Substring(colon + 1));
            return true;
        }

        if (s.StartsWith(NumericIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = s.Substring(NumericIdPrefix.Length).Split(':');
            if (parts.Length != 2 || !TryIndex(parts[0], out int mi) ||
                !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var nid) || nid == 0)
            { error = "nid: id must be nid:<modelIndex>:<positive id>"; return false; }
            result = new ItemRef(ItemRefKind.NumericId, s, mi, numericId: nid);
            return true;
        }

        // Legacy path form: "N:" is a model root, "N:a:b" a descendant.
        var segs = s.Split(':');
        if (segs.Length < 2 || !TryIndex(segs[0], out int model))
        { error = "path id must look like 0: or 0:6:3"; return false; }
        var children = new List<int>();
        for (int i = 1; i < segs.Length; i++)
        {
            if (segs[i].Length == 0)
            {
                if (i == segs.Length - 1 && i == 1) continue; // "N:" root
                error = "path id has an empty segment"; return false;
            }
            if (!TryIndex(segs[i], out int c)) { error = "path id segments must be non-negative integers"; return false; }
            children.Add(c);
        }
        result = new ItemRef(ItemRefKind.Path, s, model, childPath: children);
        return true;
    }

    public static string FormatPath(int modelIndex, IEnumerable<int> childPath)
    {
        var s = modelIndex.ToString(CultureInfo.InvariantCulture) + ":";
        bool first = true;
        foreach (var c in childPath)
        {
            s += (first ? "" : ":") + c.ToString(CultureInfo.InvariantCulture);
            first = false;
        }
        return s;
    }

    public static string FormatPathId(int modelIndex, string pathId)
        => PathIdPrefix + modelIndex.ToString(CultureInfo.InvariantCulture) + ":" + pathId;

    public static string FormatNumericId(int modelIndex, ulong id)
        => NumericIdPrefix + modelIndex.ToString(CultureInfo.InvariantCulture) + ":" + id.ToString(CultureInfo.InvariantCulture);

    public static string FormatGuid(Guid guid) => GuidPrefix + guid.ToString("D");

    /// <summary>Parses the <c>id_mode</c> parameter; null/empty means the legacy path form.</summary>
    public static bool TryParseMode(string? value, out ItemIdMode mode)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "": case "path": mode = ItemIdMode.Path; return true;
            case "pid": case "pathid": case "path_id": mode = ItemIdMode.PathId; return true;
            case "nid": case "numeric": case "numeric_id": mode = ItemIdMode.NumericId; return true;
            case "guid": case "instance_guid": mode = ItemIdMode.InstanceGuid; return true;
            default: mode = ItemIdMode.Path; return false;
        }
    }

    private static bool TryIndex(string s, out int value)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
