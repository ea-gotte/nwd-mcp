#if NAVIS2022 || NAVIS2023 || NAVIS2024 || NAVIS2025 || NAVIS2026 || NAVIS2027
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using NW = Autodesk.Navisworks.Api;

namespace Bimwright.Nwd.Shared.Handlers;

/// <summary>Thrown by a <see cref="DocumentCommand"/> to fail with a specific code instead of an internal error.</summary>
public sealed class CommandFailure : Exception
{
    public string Code { get; }
    public CommandFailure(string code, string message) : base(message) { Code = code; }
    public static CommandFailure Invalid(string message) => new CommandFailure("INVALID_ARGUMENT", message);
    public static CommandFailure NotFound(string message) => new CommandFailure("NOT_FOUND", message);
}

/// <summary>
/// Base for handlers of the full-API extension: resolves the active document, builds meta, and turns
/// <see cref="CommandFailure"/> into a coded failure. Override <see cref="Run"/>.
/// </summary>
public abstract class DocumentCommand : INwdCommand
{
    public abstract string Name { get; }
    public virtual bool IsReadOnly => true;
    /// <summary>False for commands that work without an open document.</summary>
    protected virtual bool RequiresDocument => true;
    /// <summary>False for commands that are useful on an empty document.</summary>
    protected virtual bool RequiresModels => true;

    public NwdCommandResult Execute(NwdCommandContext ctx, JObject p)
    {
        var meta = new NwdResponseMeta { TargetId = ctx.TargetId, NavisworksYear = ctx.NavisworksYear };
        var doc = NW.Application.ActiveDocument;
        if (RequiresDocument && (doc is null || (RequiresModels && doc.IsClear)))
            return NwdCommandResult.Fail(Guid.Empty, "NO_DOCUMENT", "no active Navisworks document with models", meta);
        try
        {
            return NwdCommandResult.Success(Guid.Empty, Run(ctx, p ?? new JObject(), doc!), meta);
        }
        catch (CommandFailure f)
        {
            return NwdCommandResult.Fail(Guid.Empty, f.Code, f.Message, meta);
        }
    }

    protected abstract JToken Run(NwdCommandContext ctx, JObject p, NW.Document doc);

    // ---- argument helpers -------------------------------------------------------------------------------

    protected static PageRequest Page(JObject p, int defaultLimit = PageRequest.DefaultLimit)
        => PageRequest.TryFrom(p, out var page, out var e, defaultLimit) ? page : throw CommandFailure.Invalid(e);

    protected static List<NW.ModelItem> Scope(JObject p, NW.Document doc, out JArray unresolved)
        => ItemScope.TryResolve(p, doc, out var items, out unresolved, out var e) ? items : throw CommandFailure.Invalid(e);

    protected static ItemIdEmitter Ids(JObject p, NW.Document doc)
        => ItemIdEmitter.TryFrom(p, doc, out var em, out var e) ? em : throw CommandFailure.Invalid(e);

    protected static NW.ModelItem OneItem(JObject p, NW.Document doc, string key = "item_id")
    {
        var raw = (string?)p[key];
        if (string.IsNullOrEmpty(raw)) throw CommandFailure.Invalid(key + " is required");
        var r = new ItemRefResolver(doc).Resolve(new[] { raw });
        if (r.Items.Count == 0) throw CommandFailure.NotFound((string?)r.Unresolved.First?["reason"] ?? "item not found");
        return r.Items[0];
    }

    protected static int Int(JObject p, string key, int @default, int min, int max)
    {
        var t = p[key];
        if (t == null || t.Type == JTokenType.Null) return @default;
        if (t.Type != JTokenType.Integer) throw CommandFailure.Invalid(key + " must be an integer");
        var v = (long)t;
        if (v < min || v > max) throw CommandFailure.Invalid($"{key} must be between {min} and {max}");
        return (int)v;
    }

    protected static bool Bool(JObject p, string key, bool @default = false)
    {
        var t = p[key];
        return t == null || t.Type == JTokenType.Null ? @default : t.Type == JTokenType.Boolean ? (bool)t : throw CommandFailure.Invalid(key + " must be true or false");
    }

    protected static List<PropertySpec> Properties(JObject p, string key = "properties")
    {
        var arr = p[key] as JArray ?? throw CommandFailure.Invalid(key + " must be an array of \"Category/Property\"");
        return PropertySpec.TryParseAll(arr.Select(t => (string?)t), out var specs, out var e) ? specs : throw CommandFailure.Invalid(e);
    }

    protected static PropertySpec OneProperty(JObject p, string key = "property")
        => PropertySpec.TryParse((string?)p[key], out var s, out var e) ? s! : throw CommandFailure.Invalid(e);
}

/// <summary>JSON shapes for Navisworks values. Doubles are in model units; see the document/model "units" field.</summary>
public static class NwJson
{
    public static JArray Point(NW.Point3D pt) => new JArray(R(pt.X), R(pt.Y), R(pt.Z));
    public static JArray Vector(NW.Vector3D v) => new JArray(R(v.X), R(v.Y), R(v.Z));
    public static JArray Vector(NW.UnitVector3D v) => new JArray(R(v.X), R(v.Y), R(v.Z));

    public static JToken Box(NW.BoundingBox3D? box)
    {
        if (box == null || box.IsEmpty) return JValue.CreateNull();
        return new JObject
        {
            ["min"] = Point(box.Min),
            ["max"] = Point(box.Max),
            ["center"] = Point(box.Center),
            ["size"] = Vector(box.Size),
        };
    }

    public static JToken Transform(NW.Transform3D? t)
    {
        if (t == null) return JValue.CreateNull();
        return new JObject
        {
            ["is_identity"] = t.IsIdentity(),
            ["is_translation"] = t.IsTranslation(),
            ["is_rotation"] = t.IsRotation(),
            ["is_uniform_scale"] = t.IsUniformScale(),
            ["translation"] = Vector(t.Translation),
        };
    }

    /// <summary>Typed JSON for a property value (numbers stay numbers, named constants show their display name).</summary>
    public static JToken Value(NW.VariantData? v)
    {
        if (v == null || v.IsNone) return JValue.CreateNull();
        try
        {
            if (v.IsDisplayString) return v.ToDisplayString();
            if (v.IsIdentifierString) return v.ToIdentifierString();
            if (v.IsBoolean) return v.ToBoolean();
            if (v.IsInt32) return v.ToInt32();
#if NAVIS2026 || NAVIS2027
            // 64-bit and unsigned variants are not in the 2024 API (2025 unverified); older years fall back to ToString().
            if (v.IsInt64) return v.ToInt64();
            if (v.IsNat32) return v.ToNat32();
            if (v.IsNat64) return v.ToNat64();
#endif
            if (v.IsAnyDouble) return R(v.ToAnyDouble());
            if (v.IsDateTime) return v.ToDateTime().ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            if (v.IsNamedConstant) return v.ToNamedConstant().DisplayName;
            if (v.IsPoint3D) return Point(v.ToPoint3D());
            if (v.IsPoint2D) { var p2 = v.ToPoint2D(); return new JArray(R(p2.X), R(p2.Y)); }
            return v.ToString();
        }
        catch (Exception) { return "(unreadable)"; }
    }

    /// <summary>Plain text for CSV cells and histogram keys; null when the value is absent.</summary>
    public static string? Text(NW.VariantData? v)
    {
        var j = Value(v);
        switch (j.Type)
        {
            case JTokenType.Null: return null;
            case JTokenType.String: return (string?)j;
            case JTokenType.Float: return ((double)j).ToString("R", CultureInfo.InvariantCulture);
            case JTokenType.Boolean: return (bool)j ? "true" : "false";
            case JTokenType.Array: return string.Join(" ", j.Select(x => Convert.ToString(((JValue)x).Value, CultureInfo.InvariantCulture)));
            default: return Convert.ToString(((JValue)j).Value, CultureInfo.InvariantCulture);
        }
    }

    public static string DataType(NW.VariantData? v) => v == null ? "None" : v.DataType.ToString();

    /// <summary>Finds a property by display names, then internal names, then case-insensitively.</summary>
    public static NW.DataProperty? FindProperty(NW.ModelItem item, PropertySpec spec)
    {
        var cats = item.PropertyCategories;
        var hit = cats.FindPropertyByDisplayName(spec.Category, spec.Property) ?? cats.FindPropertyByName(spec.Category, spec.Property);
        if (hit != null) return hit;
        foreach (NW.PropertyCategory c in cats)
        {
            if (!Same(c.DisplayName, spec.Category) && !Same(c.Name, spec.Category)) continue;
            foreach (NW.DataProperty dp in c.Properties)
                if (Same(dp.DisplayName, spec.Property) || Same(dp.Name, spec.Property)) return dp;
        }
        return null;
    }

    public static IEnumerable<NW.ModelItem> AllItems(NW.Document doc)
    {
        foreach (NW.Model m in doc.Models)
        {
            if (m.RootItem == null) continue;
            foreach (NW.ModelItem item in m.RootItem.DescendantsAndSelf) yield return item;
        }
    }

    public static JObject ItemSummary(NW.ModelItem item, ItemIdEmitter ids) => new JObject
    {
        ["id"] = ids.IdOf(item),
        ["name"] = item.DisplayName,
        ["class"] = item.ClassDisplayName,
    };

    public static double R(double d) => double.IsNaN(d) || double.IsInfinity(d) ? 0 : Math.Round(d, 6);

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
#endif
