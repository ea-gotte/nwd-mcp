using System.Net;
using System.Net.Sockets;
using Bimwright.Nwd.Server;
using Bimwright.Nwd.Server.Tools;
using Bimwright.Nwd.Shared.Infrastructure;
using Bimwright.Nwd.Tests.Helpers;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Tests;

public sealed class HealthToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nwd-health-" + Guid.NewGuid().ToString("N"));
    private readonly ReleaseUpdateCheckTests.Handler _http = new();
    private readonly HttpClient _httpClient;
    private readonly PluginClient _client;
    private readonly QueryTools _tools;

    public HealthToolsTests()
    {
        _httpClient = new HttpClient(_http);
        _client = new PluginClient(new NwdMcpConfig { DescriptorDirectory = _root });
        _tools = new QueryTools(_client, new ReleaseUpdateCheck(_httpClient));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task No_target_preserves_server_identity_without_network_or_install_writes()
    {
        var data = JObject.Parse(await _tools.HealthCheck());
        Assert.False(data.Value<bool>("ok"));
        Assert.False(data.Value<bool>("host_connected"));
        Assert.Equal("NO_TARGET", (string?)data["error"]?["code"]);
        Assert.Equal(Program.ServerInformationalVersion, (string?)data["mcp"]?["server_informational_version"]);
        Assert.Equal("unknown", (string?)data["mcp"]?["version_alignment"]);
        Assert.Null((string?)data["mcp"]?["plugin_version"]);
        Assert.Equal("not_checked", (string?)data["update"]?["status"]);
        Assert.Equal("check_only", (string?)data["update_guidance"]?["mode"]);
        Assert.False(data["update_guidance"]!.Value<bool>("requires_user_authorization"));
        Assert.DoesNotContain("install.ps1", data["update_guidance"]!["steps"]!.ToString());
        Assert.Equal("not_observable_by_server", (string?)data["client_verification"]?["status"]);
        Assert.Equal(0, _http.Calls);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Gateway_snapshot_is_captured_once_and_is_not_mutable_through_responses()
    {
        var first = JObject.Parse(await _tools.HealthCheck());
        var expected = first["setup_gateway"]!.DeepClone();
        first["setup_gateway"]!["module_version_id"] = "changed";
        var snapshot = _client.GatewayIdentity;
        snapshot["assembly_informational_version"] = "changed";
        var second = JObject.Parse(await _tools.HealthCheck());
        Assert.True(JToken.DeepEquals(expected, second["setup_gateway"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connected_health_preserves_host_state_and_reports_the_replying_plugins_identity(bool hasDocument)
    {
        var pluginIdentity = ReleaseUpdateCheckTests.Identity("1.0.0+" + new string('b', 40));
        using var plugin = new FakePlugin
        {
            Reply = env => NwdCommandResult.Success(env.Id, new JObject
            {
                ["healthy"] = true, ["navisworks_year"] = 2025, ["process_id"] = 1234,
                ["has_document"] = hasDocument, ["document_title"] = hasDocument ? "sample.nwd" : null,
                ["setup_plugin"] = pluginIdentity.DeepClone(),
            }, new NwdResponseMeta())
        };
        var tools = new QueryTools(plugin.Client, new ReleaseUpdateCheck(_httpClient));
        var data = JObject.Parse(await tools.HealthCheck());
        Assert.True(data.Value<bool>("host_connected"));
        Assert.True(data.Value<bool>("healthy"));
        Assert.Equal(hasDocument, data.Value<bool>("has_document"));
        Assert.Equal(1234, data.Value<int>("process_id"));
        Assert.Equal(2025, data.Value<int>("navisworks_year"));
        Assert.Equal(hasDocument ? "sample.nwd" : null, data.Value<string>("document_title"));
        Assert.True(JToken.DeepEquals(pluginIdentity, data["setup_plugin"]));
        Assert.Equal("1.0.0", (string?)data["mcp"]?["plugin_version"]);
        Assert.Equal(0, _http.Calls);
        Assert.Equal("health_check", plugin.Single().Command);
        Assert.Empty(plugin.Single().Params.Properties());
    }

    [Fact]
    public async Task Legacy_plugin_remains_connected_but_has_unknown_version_and_build()
    {
        using var plugin = new FakePlugin();
        var tools = new QueryTools(plugin.Client, new ReleaseUpdateCheck(_httpClient));
        var data = JObject.Parse(await tools.HealthCheck());
        Assert.True(data.Value<bool>("host_connected"));
        Assert.Null((string?)data["mcp"]?["plugin_version"]);
        Assert.Null((string?)data["mcp"]?["plugin_build_id"]);
        Assert.Equal("unknown", (string?)data["mcp"]?["version_alignment"]);
        Assert.Equal("unknown", (string?)data["mcp"]?["revision_alignment"]);
    }

    [Fact]
    public async Task Explicit_update_check_without_host_does_not_hide_the_failed_connection()
    {
        var data = JObject.Parse(await _tools.HealthCheck(check_updates: true));
        Assert.False(data.Value<bool>("host_connected"));
        Assert.Equal("NO_TARGET", (string?)data["error"]?["code"]);
        Assert.Equal("checked", (string?)data["update"]?["status"]);
        Assert.Equal("unknown", (string?)data["update"]?["components"]?["plugin"]?["status"]);
        Assert.Equal(2, _http.Calls);
    }

    [Fact]
    public async Task Update_check_compares_plugin_reply_not_gateway_or_discovery_metadata()
    {
        _http.Body = """{"tag_name":"v1.0.1","draft":false,"prerelease":false}""";
        using var plugin = new FakePlugin
        {
            Reply = env => NwdCommandResult.Success(env.Id, new JObject
            {
                ["healthy"] = true,
                ["setup_plugin"] = ReleaseUpdateCheckTests.Identity("1.0.0+" + new string('b', 40)),
            }, new NwdResponseMeta())
        };
        var tools = new QueryTools(plugin.Client, new ReleaseUpdateCheck(_httpClient));
        var data = JObject.Parse(await tools.HealthCheck(check_updates: true));
        Assert.True(data.Value<bool>("host_connected"));
        Assert.Equal("update_available", (string?)data["update"]?["components"]?["plugin"]?["status"]);
        Assert.True(data["update"]!.Value<bool>("update_available"));
        Assert.Equal("health_check", plugin.Single().Command);
        Assert.Empty(plugin.Single().Params.Properties()); // check_updates stays on the gateway.
    }

    [Fact]
    public async Task Release_lookup_failure_does_not_hide_successful_host_probe()
    {
        _http.Status = HttpStatusCode.Forbidden;
        using var plugin = new FakePlugin();
        var data = JObject.Parse(await new QueryTools(plugin.Client, new ReleaseUpdateCheck(_httpClient))
            .HealthCheck(check_updates: true));
        Assert.True(data.Value<bool>("host_connected"));
        Assert.Equal("unknown", (string?)data["update"]?["status"]);
        Assert.Equal("http_403", (string?)data["update"]?["reason"]);
        Assert.Null(data["update"]!.Value<bool?>("update_available"));
        Assert.Equal("UPDATE_CHECK_UNAVAILABLE", (string?)data["update"]?["notification"]?["code"]);
        Assert.Equal("retry_check", (string?)data["update_guidance"]?["mode"]);
        Assert.False(data["update_guidance"]!.Value<bool>("requires_user_authorization"));
        Assert.DoesNotContain("install.ps1", data["update_guidance"]!["steps"]!.ToString());
    }

    [Fact]
    public async Task Unreachable_target_preserves_gateway_identity_and_host_failure()
    {
        Directory.CreateDirectory(_root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var descriptor = new TargetDescriptor
        {
            TargetId = "navis-2025-" + Environment.ProcessId,
            NavisworksYear = 2025, ProcessId = Environment.ProcessId, HostProduct = "Manage",
            Port = port, AuthToken = "test", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(Path.Combine(_root, descriptor.TargetId + ".json"), Newtonsoft.Json.JsonConvert.SerializeObject(descriptor));
        var data = JObject.Parse(await _tools.HealthCheck());
        Assert.Equal("TARGET_UNAVAILABLE", (string?)data["error"]?["code"]);
        Assert.False(data.Value<bool>("host_connected"));
        Assert.Equal(Program.ServerInformationalVersion, (string?)data["mcp"]?["server_informational_version"]);
        Assert.Equal(0, _http.Calls);
    }

    [Fact]
    public async Task Malformed_health_payload_keeps_gateway_identity_instead_of_throwing()
    {
        using var plugin = new FakePlugin
        {
            Reply = env => NwdCommandResult.Success(env.Id, new JArray(), new NwdResponseMeta())
        };
        var data = JObject.Parse(await new QueryTools(plugin.Client).HealthCheck());
        Assert.False(data.Value<bool>("host_connected"));
        Assert.Equal("API_ERROR", (string?)data["error"]?["code"]);
        Assert.NotNull(data["setup_gateway"]);
    }

    [Theory]
    [InlineData("v99.0.0", "1.0.0", "prepare_update", true)]
    [InlineData("v0.1.0", "0.0.1", "review", false)]
    public async Task Connected_health_returns_the_installation_workflow_only_for_a_safe_proposal(
        string tag, string pluginVersion, string mode, bool installationAllowed)
    {
        _http.Body = new JObject { ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = false }.ToString();
        using var plugin = new FakePlugin
        {
            Reply = env => NwdCommandResult.Success(env.Id, new JObject
            {
                ["healthy"] = true, ["has_document"] = false,
                ["setup_plugin"] = ReleaseUpdateCheckTests.Identity(pluginVersion),
            }, new NwdResponseMeta())
        };
        var tools = new QueryTools(plugin.Client, new ReleaseUpdateCheck(_httpClient));
        var data = JObject.Parse(await tools.HealthCheck(check_updates: true));
        Assert.True(data.Value<bool>("host_connected"));
        Assert.Equal("NEW_RELEASE_AVAILABLE", (string?)data["update"]?["notification"]?["code"]);
        Assert.Equal(installationAllowed, data["update"]!["notification"]!.Value<bool>("bundle_update_proposal_allowed"));
        Assert.Equal(mode, (string?)data["update_guidance"]?["mode"]);
        Assert.Equal(installationAllowed, data["update_guidance"]!.Value<bool>("requires_user_authorization"));
        if (installationAllowed) Assert.Contains("install.ps1 -WhatIf", data["update_guidance"]!["steps"]!.ToString());
        else
        {
            Assert.Equal(new[] { "server" }, data["update"]!["notification"]!["protected_components"]!.Values<string>());
            Assert.DoesNotContain("install.ps1", data["update_guidance"]!["steps"]!.ToString());
        }
        Assert.Equal(2, _http.Calls);
        Assert.Equal("health_check", plugin.Single().Command);
        Assert.Empty(plugin.Single().Params.Properties());
    }

    [Fact]
    public async Task A_legacy_plugin_needs_observation_not_an_installation_or_corruption_warning()
    {
        using var plugin = new FakePlugin();
        var data = JObject.Parse(await new QueryTools(plugin.Client, new ReleaseUpdateCheck(_httpClient))
            .HealthCheck(check_updates: true));
        Assert.True(data.Value<bool>("host_connected"));
        Assert.False(data["update"]!["components"]!["plugin"]!.Value<bool>("identity_observed"));
        Assert.Equal(new[] { "plugin" }, data["update"]!["notification"]!["unobserved_components"]!.Values<string>());
        Assert.False(data["update"]!["notification"]!.Value<bool>("bundle_update_proposal_allowed"));
        Assert.Equal("review", (string?)data["update_guidance"]?["mode"]);
        Assert.DoesNotContain("install.ps1", data["update_guidance"]!["steps"]!.ToString());
    }

    [Theory]
    [InlineData("1.0.1+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.0.1+bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "match", "mismatch")]
    [InlineData("1.0.1+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.0.1+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "match", "match")]
    [InlineData("1.0.1+dev.local", "1.0.0+dev.local", "mismatch", "unknown")]
    public void Same_version_does_not_hide_different_or_unverifiable_revisions(
        string server, string plugin, string versionAlignment, string revisionAlignment)
    {
        var data = new JObject { ["setup_plugin"] = ReleaseUpdateCheckTests.Identity(plugin) };
        HealthRuntimeInfo.Enrich(data, ReleaseUpdateCheckTests.Identity(server), true);
        Assert.Equal(versionAlignment, (string?)data["mcp"]?["version_alignment"]);
        Assert.Equal(revisionAlignment, (string?)data["mcp"]?["revision_alignment"]);
    }
}
