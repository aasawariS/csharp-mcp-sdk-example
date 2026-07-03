# AI Productivity Assistant — XAA Demo

A C# ASP.NET Core web app that demonstrates **Cross App Access (XAA)** using the
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk). It acts as an MCP
Requesting App registered on [xaa.dev](https://xaa.dev) and fetches a user's tasks
from a protected MCP Server — with no OAuth consent screens.

---

## What it demonstrates

The complete XAA flow, end-to-end:

```
Step 1  User logs in via Enterprise SSO (OIDC + PKCE)
        ↓  id_token
Step 2  Token Exchange (RFC 8693) — id_token → ID-JAG assertion
        POST https://idp.xaa.dev/token
        ↓  ID-JAG
Step 3  JWT Bearer Grant (RFC 7523) — ID-JAG → Access Token
        POST https://auth.resource.xaa.dev/token
        ↓  Bearer token (aud: mcp.xaa.dev/mcp)
Step 4  MCP Server call via Streamable HTTP transport
        HttpClientTransport → https://mcp.xaa.dev/mcp
        ReadResourceAsync("todo0://todos")
        ↓  Real task data
        AI analysis rendered in the UI
```

Each step is animated live in the UI as it executes.

---

## SDK usage

| SDK Class | Package | Used for |
|---|---|---|
| `IdentityAssertionGrantProvider` | `ModelContextProtocol.Authentication` | Steps 2 + 3: XAA token exchange |
| `HttpClientTransport` | `ModelContextProtocol.Client` 1.4.0 | Step 4: Streamable HTTP to MCP Server |
| `McpClient` | `ModelContextProtocol.Client` 1.4.0 | `ListResourcesAsync` + `ReadResourceAsync` |

> **Note on `SdkStub.cs`:** The real `IdentityAssertionGrantProvider` in the package has a
> URI trailing-slash normalisation issue that causes `invalid_target` on xaa.dev's IdP. The
> stub mirrors the exact API but trims trailing slashes from the `audience` claim. Delete
> `Services/SdkStub.cs` and uncomment the `PackageReference` comment in `XaaDemoApp.csproj`
> once the SDK patch ships.

---

## Project structure

```
XaaDemoApp.csproj          Project config — ModelContextProtocol 1.4.0 + OpenIdConnect
appsettings.json           All xaa.dev credentials and endpoint URLs
Program.cs                 Web host, OIDC middleware, routes, SSE flow, AI analysis
Services/
  XaaService.cs            Creates IdentityAssertionGrantProvider and gets the Bearer token
  McpTodoService.cs        Connects via HttpClientTransport, lists resources, reads todos
  SdkStub.cs               Temporary — mirrors real SDK API, fixes trailing-slash issue
wwwroot/
  index.html               Single-page UI (login + dashboard)
```

---

## Setup

### 1. Register on xaa.dev

1. Go to [xaa.dev/developer/register](https://xaa.dev/developer/register)
2. Create a new requesting app, set redirect URI to `http://localhost:5000/callback`
3. Add the **Todo0 MCP Server** resource connection (`https://mcp.xaa.dev/mcp`)
4. Note down the credentials shown in the Integration Guide

### 2. Configure `appsettings.json`

```json
{
  "Xaa": {
    "ClientId":        "<your app client_id>",
    "ClientSecret":    "<your app client_secret>",
    "IdpBaseUrl":      "https://idp.xaa.dev",
    "McpClientId":     "<client_id>-at-todo0-mcp",
    "McpClientSecret": "<resource client secret from JWT Bearer Grant section>",
    "AuthServerUrl":   "https://auth.resource.xaa.dev",
    "McpServerUrl":    "https://mcp.xaa.dev/mcp",
    "RedirectUri":     "http://localhost:5000/callback"
  }
}
```

### 3. Run

```bash
dotnet run
```

Open [http://localhost:5000](http://localhost:5000), sign in with a xaa.dev test user,
and click **Analyze My Todos**.

---

## Verifying live data

The access token displayed in the sidebar after the flow runs is a real JWT issued by
`https://auth.resource.xaa.dev`. The decoded claims (`iss`, `aud`, `sub`) are shown
in the **Token Claims** chip — `iss = https://auth.resource.xaa.dev` confirms the token
came from xaa.dev's authorization server, not mock data.

To see server-side logs: go to [xaa.dev/inspect?tab=mcp-server](https://xaa.dev/inspect?tab=mcp-server),
paste the `sub` value from the Token Claims chip into the filter, then re-run the analysis.
