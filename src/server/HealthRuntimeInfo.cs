using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server;

internal static class HealthRuntimeInfo
{
    internal const string ClientRuntimeMetadataKey = "bimwright/nwd-mcp/runtime";

    internal static JObject ClientRuntime(JObject gateway) => new()
    {
        ["server_informational_version"] = gateway["assembly_informational_version"]?.DeepClone(),
        ["module_version_id"] = gateway["module_version_id"]?.DeepClone(),
        ["process_id"] = gateway["process_id"]?.DeepClone(),
        ["process_started_utc"] = gateway["process_started_utc"]?.DeepClone(),
    };

    internal static void Enrich(JObject health, JObject gateway, bool connected)
    {
        var plugin = connected ? health["setup_plugin"] as JObject : null;
        var serverInfo = Text(gateway, "assembly_informational_version");
        var pluginInfo = Text(plugin, "assembly_informational_version");
        var serverVersion = VersionOf(serverInfo) ?? Program.ServerVersion;
        var pluginVersion = VersionOf(pluginInfo);
        var serverRevision = RevisionOf(serverInfo);
        var pluginRevision = RevisionOf(pluginInfo);
        health["host_connected"] = connected;
        health["setup_gateway"] = gateway.DeepClone();
        health["mcp"] = new JObject
        {
            ["name"] = "nwd-mcp",
            ["server_version"] = serverVersion,
            ["server_informational_version"] = serverInfo,
            ["plugin_version"] = pluginVersion,
            ["server_build_id"] = BuildId(serverInfo, gateway),
            ["plugin_build_id"] = BuildId(pluginInfo, plugin),
            ["version_alignment"] = pluginVersion is null ? "unknown"
                : serverVersion == pluginVersion ? "match" : "mismatch",
            ["revision_alignment"] = serverRevision is null || pluginRevision is null ? "unknown"
                : string.Equals(serverRevision, pluginRevision, StringComparison.OrdinalIgnoreCase) ? "match" : "mismatch",
        };
        health["client_verification"] = new JObject
        {
            ["status"] = "not_observable_by_server",
            ["expected_server_info"] = new JObject { ["name"] = "nwd-mcp", ["version"] = serverInfo },
            ["tools_list_metadata_key"] = ClientRuntimeMetadataKey,
            ["expected_runtime"] = ClientRuntime(gateway),
        };
    }

    internal static JObject UpdateGuidance(JObject update)
    {
        var notification = update["notification"] as JObject;
        var proposalAllowed = notification?.Value<bool?>("bundle_update_proposal_allowed") == true;
        var mode = update.Value<string>("status") == "not_checked" ? "check_only"
            : update.Value<string>("status") == "unknown" ? "retry_check"
            : proposalAllowed ? "prepare_update" : notification is not null ? "review" : "none";
        JArray steps;
        if (mode == "prepare_update")
            steps = new JArray(
                "Review the confirmed release and affected components, download the official Setup ZIP and verify its checksum. Keep server and plugin from the same release; never automatically replace a development build.",
                "Preview install.ps1 -WhatIf and obtain or reuse user authorization. Close every Navisworks instance manually; never save, close documents or kill processes automatically to enable an update.",
                "Back up the installed bundle and server before running the installer; preserve client configuration and personal data. The current installer does not provide atomic bundle+server rollback; restore those backups on failure instead of uninstalling as upgrade rollback. Restart Navisworks and reconnect/restart the MCP client; record a new initialize serverInfo and fresh tools/list on the same connection.",
                "Call nwd_health_check on that connection and compare client_verification.expected_server_info/expected_runtime with the fresh handshake and catalog, including schema and annotations. Verify the loaded plugin; unavailable client evidence remains unverified.");
        else if (mode == "check_only")
            steps = new JArray(
                "The release check was not requested. Report the current runtime observations without claiming the release is current.",
                "If network access is permitted and release comparison is needed, call nwd_health_check(check_updates=true). No installation is proposed and no installation authorization is needed for this check.");
        else if (mode == "retry_check")
            steps = new JArray(
                notification?.Value<string>("message") ?? "The official release lookup is unavailable; reported runtime identities are unchanged.",
                "Report the lookup reason and retry the check when access is available. Do not infer a local runtime fault or request installation authorization.");
        else if (mode == "review")
            steps = new JArray(
                notification!.Value<string>("message") ?? "Review the available runtime and release comparison evidence.",
                "Inspect update.components and the protected_component_reasons/unobserved_components. Obtain missing runtime or release evidence and preserve known newer or unresolved builds. No installation is proposed.");
        else
            steps = new JArray("Report the completed component comparison and its evidence limits. No update action or installation authorization is requested.");
        return new JObject
        {
            ["mode"] = mode,
            ["automatic_update_supported"] = false,
            ["requires_user_authorization"] = mode == "prepare_update",
            ["documentation_url"] = "https://github.com/bimwright/nwd-mcp/blob/master/AGENTS.md",
            ["steps"] = steps,
        };
    }

    internal static string? Text(JObject? identity, string key)
        => identity?[key] is { Type: JTokenType.String } value ? (string?)value : null;

    internal static string? VersionOf(string? informational)
        => string.IsNullOrWhiteSpace(informational) ? null : informational.Split('+')[0];

    internal static string? BuildId(string? informational, JObject? identity)
        => informational?.Split('+', 2) is { Length: 2 } parts && parts[1].Length > 0 ? parts[1]
            : Text(identity, "module_version_id") is { } mvid ? "mvid:" + mvid : null;

    internal static string? RevisionOf(string? informational)
        => informational?.Split('+', 2) is { Length: 2 } parts
            && Regex.IsMatch(parts[1], "\\A[0-9a-fA-F]{40}([0-9a-fA-F]{24})?\\z") ? parts[1] : null;
}
