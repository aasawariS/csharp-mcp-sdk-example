using ModelContextProtocol.Authentication;

namespace XaaDemo.Services;

// Drives the XAA flow using the real IdentityAssertionGrantProvider from the MCP SDK
// (ModelContextProtocol.Core — now available in ModelContextProtocol 1.4.0).
//
// Spec sections handled:
//   Section 4 — RFC 8693 Token Exchange: ID Token → ID-JAG (at the IdP)
//   Section 5 — RFC 7523 JWT Bearer Grant: ID-JAG → Access Token (at Resource Auth Server)
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
                // Section 5: credentials for the Resource Authorization Server
                ClientId        = _config["Xaa:McpClientId"]!,
                ClientSecret    = _config["Xaa:McpClientSecret"],

                // Section 4: IdP token endpoint (string, not Uri — per real SDK API)
                IdpTokenEndpoint = $"{_config["Xaa:IdpBaseUrl"]}/token",

                // Section 4: credentials for the IdP
                IdpClientId     = _config["Xaa:ClientId"]!,
                IdpClientSecret = _config["Xaa:ClientSecret"],

                // MCP server requires both scopes (confirmed by Service Inspector)
                Scope = "todos.read mcp.access",

                // Section 3: supplies the OIDC ID Token from the user's active SSO session.
                // The SDK calls this callback to obtain a fresh Identity Assertion.
                IdTokenCallback = (_, _) => Task.FromResult(idToken)
            },
            _httpFactory.CreateClient("xaa"),
            _loggerFactory);

        // resourceUrl:   Section 4 'resource' param — the RFC 9728 MCP Server identifier
        // authServerUrl: Section 4 'audience' param — MUST be the issuer of the Resource Auth Server
        var result = await provider.GetAccessTokenAsync(
            resourceUrl:            new Uri(_config["Xaa:McpServerUrl"]!),
            authorizationServerUrl: new Uri(_config["Xaa:AuthServerUrl"]!),
            cancellationToken:      ct);

        return result.AccessToken;
    }
}
