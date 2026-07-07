using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace XaaDemo.Services;

public record TodoItem(
    string    Id,
    string    Title,
    bool      Completed,
    string?   Priority,
    string[]? Tags,
    DateTime? DueDate,
    string?   Description);

public class McpTodoService
{
    private readonly IConfiguration _config;

    public McpTodoService(IConfiguration config) => _config = config;

    public async Task<(string[] Resources, List<TodoItem> Todos, string RawContent)> FetchAsync(
        string accessToken, CancellationToken ct = default)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint          = new Uri(_config["Xaa:McpServerUrl"]!),
            TransportMode     = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(15),
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {accessToken}"
            }
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);

        var resources    = await client.ListResourcesAsync(cancellationToken: ct);
        var resourceUris = resources.Select(r => r.Uri).ToArray();

        var result = await client.ReadResourceAsync("todo0://todos", options: null, ct);

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
