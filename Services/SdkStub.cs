// Stub for IdentityAssertionGrantProvider — mirrors the SDK API from ModelContextProtocol 1.4.0.
// TrimEnd('/') on audience/resource values is required: xaa.dev IdP does exact-string matching
// and C# Uri normalizes bare hostnames with a trailing slash.
// Replace with the real package once the SDK handles this edge case.

namespace ModelContextProtocol.Authentication;

public class IdentityAssertionGrantProviderOptions
{
    public string  ClientId          { get; set; } = "";
    public string? ClientSecret      { get; set; }
    public string  IdpTokenEndpoint  { get; set; } = "";
    public string  IdpClientId       { get; set; } = "";
    public string? IdpClientSecret   { get; set; }
    public string? Scope             { get; set; }
    public Func<object?, CancellationToken, Task<string>> IdTokenCallback { get; set; }
        = (_, _) => Task.FromResult("");
}

public class TokenContainer
{
    public string AccessToken { get; init; } = "";
}

public class IdentityAssertionGrantProvider
{
    private readonly IdentityAssertionGrantProviderOptions _opts;
    private readonly HttpClient _http;
    private TokenContainer? _cached;

    public IdentityAssertionGrantProvider(
        IdentityAssertionGrantProviderOptions opts,
        HttpClient http,
        ILoggerFactory? _ = null)
    {
        _opts = opts;
        _http = http;
    }

    public async Task<TokenContainer> GetAccessTokenAsync(
        Uri resourceUrl,
        Uri authorizationServerUrl,
        CancellationToken cancellationToken = default)
    {
        if (_cached != null) return _cached;

        var idToken = await _opts.IdTokenCallback(null, cancellationToken);
        var jag     = await ExchangeForJag(idToken, resourceUrl, authorizationServerUrl, cancellationToken);
        _cached     = await RequestAccessToken(jag, authorizationServerUrl, cancellationToken);
        return _cached;
    }

    public void InvalidateCache() => _cached = null;

    private async Task<string> ExchangeForJag(
        string idToken, Uri resourceUrl, Uri authServerUrl, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"]           = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["requested_token_type"] = "urn:ietf:params:oauth:token-type:id-jag",
            ["subject_token"]        = idToken,
            ["subject_token_type"]   = "urn:ietf:params:oauth:token-type:id_token",
            ["audience"]             = authServerUrl.ToString().TrimEnd('/'),
            ["resource"]             = resourceUrl.ToString().TrimEnd('/'),
            ["client_id"]            = _opts.IdpClientId,
        };
        if (_opts.Scope           is not null) fields["scope"]         = _opts.Scope;
        if (_opts.IdpClientSecret is not null) fields["client_secret"] = _opts.IdpClientSecret;

        var res  = await _http.PostAsync(_opts.IdpTokenEndpoint, new FormUrlEncodedContent(fields), ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        using var json = System.Text.Json.JsonDocument.Parse(body);
        var root = json.RootElement;

        if (!res.IsSuccessStatusCode || root.TryGetProperty("error", out _))
        {
            var code = root.TryGetProperty("error",             out var e) ? e.GetString() : res.StatusCode.ToString();
            var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : body;
            throw new Exception($"Token exchange failed with status {(int)res.StatusCode}. Error: {code} ({desc})");
        }

        return root.GetProperty("access_token").GetString()!;
    }

    private async Task<TokenContainer> RequestAccessToken(
        string jag, Uri authServerUrl, CancellationToken ct)
    {
        var tokenEndpoint = new Uri(authServerUrl, "/token");
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"]  = jag,
            ["client_id"]  = _opts.ClientId,
        };
        if (_opts.Scope        is not null) fields["scope"]         = _opts.Scope;
        if (_opts.ClientSecret is not null) fields["client_secret"] = _opts.ClientSecret;

        var res  = await _http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(fields), ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        using var json = System.Text.Json.JsonDocument.Parse(body);
        var root = json.RootElement;

        if (!res.IsSuccessStatusCode || root.TryGetProperty("error", out _))
        {
            var code = root.TryGetProperty("error",             out var e) ? e.GetString() : res.StatusCode.ToString();
            var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : body;
            throw new Exception($"Access token request failed with status {(int)res.StatusCode}. Error: {code} ({desc})");
        }

        return new TokenContainer { AccessToken = root.GetProperty("access_token").GetString()! };
    }
}
