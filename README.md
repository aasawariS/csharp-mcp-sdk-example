# AI Productivity Assistant — XAA Demo (C#)

A C# ASP.NET Core web app demonstrating **Cross-App Access (XAA)** using the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk). It acts as a requester app that fetches a user's tasks from a protected MCP Server — with no OAuth consent screens.

In this example, the C# app is a requester app that fetches the To-Do list from the ToDo app and analyzes the tasks fetched.

Please read [Build a Secure C# MCP App with Cross-App Access (XAA)](https://example.com) to see how you can use Okta to secure your MCP server and implement Cross-App Access in a C# ASP.NET Core application.

---

## Understanding the Cross-App Access flow

XAA enables one app to securely access another app's resources on behalf of the same logged-in user, without additional consent prompts. The demo resource app streams each step live so you can watch the token exchange, Bearer token issuance, and MCP resource fetch happen in real time:

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
        ↓  Real task data → AI analysis rendered in the UI
```

---

## Testing with xaa.dev

[xaa.dev](https://xaa.dev) is a testing playground. It provides a standardized, functional environment that lets you verify your end-to-end flow immediately, acting as the bridge between your app and the downstream resource.

### Step 1 — Register your app on xaa.dev

1. Go to [xaa.dev](https://xaa.dev) and select the **Register, test, and manage your requesting app** tab, then click **Continue with your app**.
2. Enter your email and click **Continue**, then **Register New App**.
3. Enter the following URIs:
   - **Redirect URI:** `http://localhost:5000/callback`
   - **Post-logout URI:** `http://localhost:5000`
4. Click **Add Resource** and select **ToDo MCP Server**.
5. Click **Register App** — your app is now registered as a requester app in the XAA flow.

### Step 2 — Clone the repository

```bash
git clone https://github.com/oktadev/xaa-csharp-mcp-sdk-example
cd xaa-csharp-mcp-sdk-example
```

### Step 3 — Configure credentials

Open `appsettings.json` and fill in the values from your xaa.dev Integration Guide:

```json
{
  "Xaa": {
    "ClientId":        "<your-client-id>",
    "ClientSecret":    "<your-client-secret>",
    "IdpBaseUrl":      "https://idp.xaa.dev",

    "McpClientId":     "<your-mcp-client-id>",
    "McpClientSecret": "<your-mcp-client-secret>",

    "AuthServerUrl":   "https://auth.resource.xaa.dev",
    "McpServerUrl":    "https://mcp.xaa.dev/mcp",

    "RedirectUri":     "http://localhost:5000/callback"
  },
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "AllowedHosts": "*"
}
```

> Make sure to copy the correct client ID and secrets from xaa.dev.

### Step 4 — Run the app

```bash
dotnet run
```

Open [http://localhost:5000](http://localhost:5000), sign in with a xaa.dev test user, and click **Analyze My Todos**.

---

## Verifying the flow

The access token displayed in the sidebar after the flow runs is a real JWT issued by `https://auth.resource.xaa.dev`. The decoded claims (`iss`, `aud`, `sub`) are shown in the **Token Claims** chip — `iss = https://auth.resource.xaa.dev` confirms the token came from xaa.dev's authorization server, not mock data.

To see server-side logs: go to [xaa.dev/inspect?tab=mcp-server](https://xaa.dev/inspect?tab=mcp-server), paste the `sub` value from the Token Claims chip into the filter, then re-run the analysis.

## License
Apache 2.0 - see [LICENSE](LICENSE)
