using System;
using System.Linq;
using Bimwright.Nwd.Server.Bake;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Handlers;

public static class ListBakeSuggestionsHandler
{
    public static string Handle(BakeDb db, DateTimeOffset? utcNow = null)
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var suggestions = db.ListSuggestions()
            .Where(s => s.NeverReason == null &&
                (string.Equals(s.State, BakeSuggestionStates.Open, StringComparison.Ordinal) ||
                 (string.Equals(s.State, BakeSuggestionStates.Dismissed, StringComparison.Ordinal) &&
                  DateTimeOffset.TryParse(s.SnoozeUntil, out var deadline) && deadline <= now)))
            .Select(s => new JObject
            {
                ["id"] = s.Id,
                ["title"] = s.Title,
                ["source"] = s.Source,
                ["score"] = s.Score,
                // Expiry is an effective state projection: listing never mutates the DB.
                ["state"] = BakeSuggestionStates.Open,
                ["created_at"] = s.CreatedAt,
                ["output_choices"] = ParsePayload(s.PayloadJson)["output_choices"] ?? new JArray("mcp_only")
            });

        return new JObject { ["suggestions"] = new JArray(suggestions) }.ToString(Formatting.None);
    }

    private static JObject ParsePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try { return JObject.Parse(json); }
        catch (JsonException) { return new JObject(); }
    }
}
