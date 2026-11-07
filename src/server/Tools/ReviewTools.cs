using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Nwd.Server.Tools;

/// <summary>Toolset "review": read-only visual inspection (images, section and redline state).</summary>
[McpServerToolType]
public sealed class ReviewTools
{
    private readonly PluginClient _client;
    public ReviewTools(PluginClient client) => _client = client;

    [McpServerTool(Name = "nwd_capture_image", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Render the active view to a PNG in %LOCALAPPDATA%\\Bimwright\\nwd-mcp\\captures and return its path (open it to see the model). " +
                 "width/height default 1280×720; style scene (default), overlay (with redlines/HUD) or ray_trace (slow); sectioning applies the section box. Does not change the document.")]
    public Task<string> CaptureImage(int width = 1280, int height = 720, string style = "scene", bool sectioning = true, string? fileName = null, CancellationToken ct = default)
    {
        var p = new JObject { ["width"] = width, ["height"] = height, ["style"] = style, ["sectioning"] = sectioning };
        if (!string.IsNullOrEmpty(fileName)) p["file_name"] = fileName;
        return ToolCall.Run(_client, "capture_image", p, ct);
    }

    [McpServerTool(Name = "nwd_get_section", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Current sectioning: enabled, planes/box mode, the box (2026+), and the Navisworks clip-planes JSON you can edit and pass to nwd_set_section mode=json.")]
    public Task<string> GetSection(CancellationToken ct = default) => ToolCall.Run(_client, "get_section", new JObject(), ct);

    [McpServerTool(Name = "nwd_get_redlines", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Redlines of the active view as Navisworks JSON (pass back to nwd_set_redlines).")]
    public Task<string> GetRedlines(CancellationToken ct = default) => ToolCall.Run(_client, "get_redlines", new JObject(), ct);
}

/// <summary>Shared plugin call with the gateway's error envelope.</summary>
internal static class ToolCall
{
    public static async Task<string> Run(PluginClient client, string command, JObject p, CancellationToken ct)
    {
        try
        {
            var data = await client.SendAsync(command, p, ct);
            return JsonConvert.SerializeObject(data, Formatting.None);
        }
        catch (NwdGatewayException ex)
        {
            return JsonConvert.SerializeObject(new { ok = false, error = new { code = ex.Code, message = ex.Message } }, Formatting.None);
        }
    }
}
