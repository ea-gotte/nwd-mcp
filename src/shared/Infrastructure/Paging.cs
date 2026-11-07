using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Shared.Infrastructure;

/// <summary>
/// API-free offset/limit paging shared by list-style commands. Parameters: <c>offset</c>, <c>limit</c>, or an
/// opaque <c>cursor</c> returned as <c>page.next_cursor</c> (cursor wins over offset).
/// </summary>
public sealed class PageRequest
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 5000;

    public int Offset { get; }
    public int Limit { get; }

    public PageRequest(int offset, int limit)
    {
        Offset = Math.Max(0, offset);
        Limit = Math.Min(Math.Max(1, limit), MaxLimit);
    }

    public static bool TryFrom(JObject? p, out PageRequest page, out string error, int defaultLimit = DefaultLimit)
    {
        page = new PageRequest(0, defaultLimit); error = "";
        if (p is null) return true;

        int offset = 0;
        var cursor = p["cursor"]?.Type == JTokenType.String ? (string?)p["cursor"] : null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryDecodeCursor(cursor!, out offset)) { error = "cursor is not a value returned by this server"; return false; }
        }
        else if (p["offset"] is JToken o && o.Type != JTokenType.Null)
        {
            if (o.Type != JTokenType.Integer || (long)o < 0) { error = "offset must be a non-negative integer"; return false; }
            offset = (int)Math.Min((long)o, int.MaxValue);
        }

        int limit = defaultLimit;
        if (p["limit"] is JToken l && l.Type != JTokenType.Null)
        {
            if (l.Type != JTokenType.Integer || (long)l < 1) { error = "limit must be a positive integer"; return false; }
            limit = (int)Math.Min((long)l, MaxLimit);
        }

        page = new PageRequest(offset, limit);
        return true;
    }

    /// <summary>Takes one page. Counts the rest of the sequence only when <paramref name="countTotal"/> is true.</summary>
    public (List<T> Items, JObject Page) Apply<T>(IEnumerable<T> source, bool countTotal = true)
    {
        var items = new List<T>(Math.Min(Limit, 1024));
        int index = 0;
        bool hasMore = false;
        int total = 0;
        foreach (var x in source)
        {
            if (index >= Offset)
            {
                if (items.Count < Limit) items.Add(x);
                else { hasMore = true; if (!countTotal) { index++; break; } }
            }
            index++;
        }
        total = index;
        return (items, Describe(items.Count, hasMore, countTotal ? total : (int?)null));
    }

    public JObject Describe(int returned, bool hasMore, int? total)
    {
        var page = new JObject
        {
            ["offset"] = Offset,
            ["limit"] = Limit,
            ["returned"] = returned,
            ["has_more"] = hasMore,
            ["next_cursor"] = hasMore ? new JValue(EncodeCursor(Offset + returned)) : JValue.CreateNull(),
        };
        if (total.HasValue) page["total"] = total.Value;
        return page;
    }

    public static string EncodeCursor(int offset)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes("o:" + offset.ToString(CultureInfo.InvariantCulture)));

    public static bool TryDecodeCursor(string cursor, out int offset)
    {
        offset = 0;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return text.StartsWith("o:", StringComparison.Ordinal) &&
                   int.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out offset);
        }
        catch (FormatException) { return false; }
    }
}
