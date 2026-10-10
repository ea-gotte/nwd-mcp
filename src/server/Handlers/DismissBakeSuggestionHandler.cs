using System;
using Bimwright.Nwd.Server.Bake;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Handlers;

public static class DismissBakeSuggestionHandler
{
    public static string Handle(BakeDb db, string id, string action, DateTimeOffset? utcNow = null)
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        var suggestion = db.GetSuggestion(id);
        if (suggestion == null)
        {
            return Fail("not_found", "Bake suggestion was not found.");
        }

        if (action != "snooze_30d" && action != "never" && action != "never_with_gap_signal")
        {
            return Fail("invalid_action", "action must be snooze_30d, never, or never_with_gap_signal.");
        }

        var now = (utcNow ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (suggestion.State != BakeSuggestionStates.Open && suggestion.State != BakeSuggestionStates.Dismissed)
            return Fail("invalid_state", "Only active or dismissed suggestions can be dismissed.");

        // Replays must not move the deadline or resurrect a permanent dismissal.
        if (suggestion.NeverReason != null ||
            (DateTimeOffset.TryParse(suggestion.SnoozeUntil, out var deadline) && deadline > now))
            return Success(id, suggestion.SnoozeUntil, suggestion.NeverReason);

        var until = action == "snooze_30d" ? now.AddDays(30).ToString("o") : null;
        var reason = action == "snooze_30d" ? null : action;
        if (!db.TryDismissSuggestion(id, until, reason, now))
        {
            var current = db.GetSuggestion(id);
            if (current?.State == BakeSuggestionStates.Dismissed &&
                (current.NeverReason != null ||
                 (DateTimeOffset.TryParse(current.SnoozeUntil, out deadline) && deadline > now)))
                return Success(id, current.SnoozeUntil, current.NeverReason);
            return Fail("state_conflict", "Suggestion changed concurrently or has an invalid snooze deadline.");
        }
        return Success(id, until, reason);
    }

    private static string Success(string id, string? until, string? reason)
        => new JObject { ["ok"] = true, ["id"] = id, ["state"] = BakeSuggestionStates.Dismissed,
            ["snooze_until"] = until, ["never_reason"] = reason }.ToString(Newtonsoft.Json.Formatting.None);

    private static string Fail(string code, string message)
        => new JObject { ["ok"] = false, ["error_code"] = code, ["message"] = message }.ToString(Newtonsoft.Json.Formatting.None);
}
