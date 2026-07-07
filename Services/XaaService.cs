using ModelContextProtocol.Authentication;

namespace XaaDemo.Services;

public class XaaService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILoggerFactory _loggerFactory;

    public XaaService(IConfiguration config, IHttpClientFactory httpFactory, ILoggerFactory loggerFactory)
    {
        _config        = config;
        _httpFactory   = httpFactory;
        _loggerFactory = loggerFactory;
    }

    public async Task<string> GetAccessTokenAsync(string idToken, CancellationToken ct = default)
    {
        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId         = _config["Xaa:McpClientId"]!,
                ClientSecret     = _config["Xaa:McpClientSecret"],
                IdpTokenEndpoint = $"{_config["Xaa:IdpBaseUrl"]}/token",
                IdpClientId      = _config["Xaa:ClientId"]!,
                IdpClientSecret  = _config["Xaa:ClientSecret"],
                Scope            = "todos.read mcp.access",
                IdTokenCallback  = (_, _) => Task.FromResult(idToken)
            },
            _httpFactory.CreateClient("xaa"),
            _loggerFactory);

        var result = await provider.GetAccessTokenAsync(
            resourceUrl:            new Uri(_config["Xaa:McpServerUrl"]!),
            authorizationServerUrl: new Uri(_config["Xaa:AuthServerUrl"]!),
            cancellationToken:      ct);

        return result.AccessToken;
    }
}
