using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace XaaDemo.Services;

public record TodoItem(
    string    Id,
    string    Title,
    bool      Completed,
    string?   Priority,     // "high" | "medium" | "low" — matches MCP server response
    string[]? Tags,
    DateTime? DueDate,
    string?   Description);

public class McpTodoService
{
    private readonly IConfiguration _config;

    public McpTodoService(IConfiguration config) => _config = config;

    /// <summary>
    /// Connects to the xaa.dev MCP Server using Streamable HTTP transport.
    ///
    /// Per transports.md, Streamable HTTP is the recommended transport for remote servers.
    /// TransportMode is set explicitly to StreamableHttp — confirmed by the Service Inspector
    /// which shows TRANSPORT: StreamableHTTP for https://mcp.xaa.dev/mcp.
    ///
    /// The XAA Bearer token obtained from IdentityAssertionGrantProvider is injected
    /// via AdditionalHeaders so every HTTP request to the MCP server carries it.
    ///
    /// The MCP server exposes RESOURCES (not tools):
    ///   todo0://todos            — all todos
    ///   todo0://todos/completed  — completed todos
    ///   todo0://todos/incomplete — incomplete todos
    ///   todo0://todos/stats      — statistics
    /// </summary>
    public async Task<(string[] Resources, List<TodoItem> Todos, string RawContent)> FetchAsync(
        string accessToken, CancellationToken ct = default)
    {
        // Streamable HTTP transport — from transports.md:
        //   var transport = new HttpClientTransport(new HttpClientTransportOptions {
        //       Endpoint = new Uri("https://my-mcp-server.example.com/mcp"),
        //       TransportMode = HttpTransportMode.StreamableHttp,
        //       AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer ..." }
        //   });
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint          = new Uri(_config["Xaa:McpServerUrl"]!),
            TransportMode     = HttpTransportMode.StreamableHttp,   // explicit per transports.md
            ConnectionTimeout = TimeSpan.FromSeconds(15),
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {accessToken}"
            }
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);

        // Discover resources — this call hits the live MCP server
        var resources     = await client.ListResourcesAsync(cancellationToken: ct);
        var resourceUris  = resources.Select(r => r.Uri).ToArray();

        // This ReadResourceAsync call is what the Service Inspector logs.
        // If the sub claim in the access token matches, you will see a
        // "resource_read" event appear in the xaa.dev Service Inspector.
        var result = await client.ReadResourceAsync("todo0://todos", options: null, ct);

        // Extract text content (TextResourceContents per MCP protocol)
        var rawContent = string.Join("",
            result.Contents.OfType<TextResourceContents>().Select(c => c.Text ?? ""));

        return (resourceUris, ParseTodos(rawContent), rawContent);
    }

    private static List<TodoItem> ParseTodos(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (raw.TrimStart().StartsWith('['))
                return JsonSerializer.Deserialize<List<TodoItem>>(raw, opts) ?? [];

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            foreach (var key in new[] { "todos", "items", "data", "results" })
            {
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<TodoItem>>(arr.GetRawText(), opts) ?? [];
            }
        }
        catch { }
        return [];
    }
}
